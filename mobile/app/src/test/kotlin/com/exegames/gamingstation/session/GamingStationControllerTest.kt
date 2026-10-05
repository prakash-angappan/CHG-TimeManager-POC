package com.exegames.gamingstation.session

import com.exegames.gamingstation.domain.DomainClock
import com.exegames.gamingstation.domain.GamingStation
import com.exegames.gamingstation.domain.GamingStationRepository
import com.exegames.gamingstation.domain.RejectionReason
import com.exegames.gamingstation.domain.SessionIds
import com.exegames.gamingstation.domain.SessionRepository
import com.exegames.gamingstation.domain.SessionSnapshot
import com.exegames.gamingstation.domain.SessionState
import java.time.Instant
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class GamingStationControllerTest {
    @Test
    fun initialStationCanBeDisplayed() = runBlocking {
        val fixture = Fixture()

        val state = fixture.controller.refresh()

        assertEquals(LocalStation.Name, state.stationName)
        assertEquals(LocalStation.Code, state.stationCode)
        assertEquals(StationMessages.Status, state.statusLabel)
        assertEquals(listOf(fixture.station), fixture.stations.list())
    }

    @Test
    fun availableStationShowsStartSession() = runBlocking {
        val state = Fixture().controller.refresh()

        assertEquals(StationPhase.Available, state.phase)
        assertEquals(StationMessages.Available, state.stateLabel)
        assertEquals(listOf(StationCommand.StartSession), state.commands)
        assertEquals(StationMessages.Start, state.commands.single().label())
        assertNull(state.countdown)
    }

    @Test
    fun startCreatesAPersistedRunningSession() = runBlocking {
        val fixture = Fixture()

        val state = fixture.controller.startSession()
        val stored = fixture.sessions.only()

        assertEquals(StationPhase.GameRunning, state.phase)
        assertEquals(SessionState.Running, stored.state)
        assertEquals("session-1", stored.sessionId)
        assertEquals(LocalStation.DurationSeconds, stored.durationSeconds)
        assertEquals(openedAt, stored.startTimeUtc)
        assertEquals(openedAt.plusSeconds(120), stored.expiryTimeUtc)
        assertEquals(1L, stored.revision)
    }

    @Test
    fun runningStateDisplaysCountdown() = runBlocking {
        val fixture = Fixture()

        val state = fixture.controller.startSession()

        assertEquals(StationMessages.GameRunning, state.stateLabel)
        assertEquals("02:00", state.countdown)
        assertEquals(listOf(StationCommand.EndSession), state.commands)
    }

    @Test
    fun countdownIsDerivedFromExpiryTimeUtc() = runBlocking {
        val fixture = Fixture()
        fixture.controller.startSession()
        val before = fixture.sessions.only()

        fixture.clock.instant = openedAt.plusSeconds(30)
        val afterThirty = fixture.controller.refresh()
        fixture.clock.instant = openedAt.plusSeconds(90)
        val afterNinety = presentStation(
            fixture.station,
            before,
            fixture.clock.instant,
            message = null,
        )
        val atExpiry = presentStation(
            fixture.station,
            before,
            before.expiryTimeUtc,
            message = null,
        )

        assertEquals("01:30", afterThirty.countdown)
        assertEquals("00:30", afterNinety.countdown)
        assertEquals("00:00", atExpiry.countdown)
        assertEquals(StationPhase.GameRunning, atExpiry.phase)
        assertEquals(before, fixture.sessions.only())
    }

    @Test
    fun expiryTransitionsToAwaitingPaymentThroughReconciler() = runBlocking {
        val fixture = Fixture()
        val started = fixture.controller.startSession()
        fixture.clock.instant = openedAt.plusSeconds(120)

        val expired = fixture.controller.refresh()
        val again = fixture.controller.refresh()
        val stored = fixture.sessions.only()

        assertEquals(StationPhase.TimeExpired, expired.phase)
        assertEquals(StationMessages.TimeExpired, expired.stateLabel)
        assertNull(expired.countdown)
        assertEquals(SessionState.AwaitingPayment, stored.state)
        assertEquals("session-1", stored.sessionId)
        assertEquals(started.stationName, expired.stationName)
        assertEquals(2L, stored.revision)
        assertEquals(openedAt, stored.startTimeUtc)
        assertEquals(openedAt.plusSeconds(120), stored.expiryTimeUtc)
        assertEquals(expired.phase, again.phase)
        assertEquals(2L, fixture.sessions.only().revision)
    }

    @Test
    fun paymentResumesTheSameSession() = runBlocking {
        val fixture = Fixture()
        expire(fixture)

        val resumed = fixture.controller.markPayment()

        assertEquals("session-1", resumed.let { fixture.sessions.only().sessionId })
        assertEquals(SessionState.Running, fixture.sessions.only().state)
        assertEquals(StationPhase.GameRunning, resumed.phase)
        assertEquals(LocalStation.Id, fixture.sessions.only().stationId)
    }

    @Test
    fun paymentCreatesANewExpiry() = runBlocking {
        val fixture = Fixture()
        val previousExpiry = expire(fixture).expiryTimeUtc
        val paidAt = openedAt.plusSeconds(150)
        fixture.clock.instant = paidAt

        fixture.controller.markPayment()
        val stored = fixture.sessions.only()

        assertEquals(paidAt, stored.startTimeUtc)
        assertEquals(paidAt.plusSeconds(120), stored.expiryTimeUtc)
        assertEquals(120, stored.durationSeconds)
        assertTrue(stored.expiryTimeUtc.isAfter(previousExpiry))
        assertEquals("02:00", fixture.controller.refresh().countdown)
    }

    @Test
    fun paymentIncrementsRevision() = runBlocking {
        val fixture = Fixture()
        expire(fixture)

        fixture.controller.markPayment()

        assertEquals(3L, fixture.sessions.only().revision)
        assertEquals("session-1", fixture.sessions.only().sessionId)
    }

    @Test
    fun endClosesTheSession() = runBlocking {
        val fixture = Fixture()
        fixture.controller.startSession()

        val state = fixture.controller.endSession()

        assertEquals(StationPhase.Available, state.phase)
        assertEquals(StationMessages.Available, state.stateLabel)
        assertEquals(listOf(StationCommand.StartSession), state.commands)
        assertEquals(SessionState.Closed, fixture.sessions.only().state)
        assertNull(fixture.sessions.getOpenSession(LocalStation.Id))
    }

    @Test
    fun endFromAwaitingPaymentShowsAvailable() = runBlocking {
        val fixture = Fixture()
        expire(fixture)

        val state = fixture.controller.endSession()

        assertEquals(StationPhase.Available, state.phase)
        assertEquals(SessionState.Closed, fixture.sessions.only().state)
        assertEquals(3L, fixture.sessions.only().revision)
        assertEquals("session-1", fixture.sessions.getById("session-1")?.sessionId)
    }

    @Test
    fun closedSessionIsStillPersisted() = runBlocking {
        val fixture = Fixture()
        fixture.controller.startSession()
        fixture.controller.endSession()

        val stored = fixture.sessions.getById("session-1")

        assertEquals(SessionState.Closed, stored?.state)
        assertEquals(2L, stored?.revision)
        assertEquals(1, fixture.sessions.rows.size)
    }

    @Test
    fun appRestartRestoresRunningSession() = runBlocking {
        val fixture = Fixture()
        fixture.controller.startSession()
        fixture.clock.instant = openedAt.plusSeconds(30)
        val restarted = fixture.restart()

        val state = restarted.refresh()
        val stored = fixture.sessions.only()

        assertEquals(StationPhase.GameRunning, state.phase)
        assertEquals("01:30", state.countdown)
        assertEquals("session-1", stored.sessionId)
        assertEquals(SessionState.Running, stored.state)
        assertEquals(1L, stored.revision)
    }

    @Test
    fun appRestartAfterExpiryRestoresAwaitingPayment() = runBlocking {
        val fixture = Fixture()
        fixture.controller.startSession()
        fixture.clock.instant = openedAt.plusSeconds(120)
        assertEquals(SessionState.Running, fixture.sessions.only().state)
        val restarted = fixture.restart()

        val state = restarted.refresh()

        assertEquals(StationPhase.TimeExpired, state.phase)
        assertEquals(SessionState.AwaitingPayment, fixture.sessions.only().state)
        assertEquals("session-1", fixture.sessions.only().sessionId)
        assertEquals(2L, fixture.sessions.only().revision)
        assertEquals(listOf(StationCommand.MarkPayment, StationCommand.EndSession), state.commands)
    }

    @Test
    fun invalidActionsAreRejectedGracefully() = runBlocking {
        val fixture = Fixture()
        fixture.controller.startSession()

        val secondStart = fixture.controller.startSession()
        assertEquals(RejectionReason.StationOccupied.message, secondStart.message)
        assertEquals(SessionState.Running, fixture.sessions.only().state)
        assertEquals(1L, fixture.sessions.only().revision)

        val earlyPayment = fixture.controller.markPayment()
        assertEquals(RejectionReason.NotAwaitingPayment.message, earlyPayment.message)
        assertEquals(1L, fixture.sessions.only().revision)

        fixture.controller.endSession()
        val endAgain = fixture.controller.endSession()
        assertEquals(StationMessages.NoSessionToEnd, endAgain.message)
        assertEquals(SessionState.Closed, fixture.sessions.only().state)

        val paymentAfterClose = fixture.controller.markPayment()
        assertEquals(StationMessages.NoPaymentPending, paymentAfterClose.message)
        assertFalse(endAgain.message!!.contains("Exception"))
        assertFalse(paymentAfterClose.message!!.contains("Exception"))
    }

    @Test
    fun databaseFailureShowsAFriendlyMessage() = runBlocking {
        val fixture = Fixture()
        fixture.sessions.failSave = true

        val state = fixture.controller.startSession()

        assertEquals(StationMessages.SaveFailed, state.message)
        assertFalse(state.message!!.contains("sqlite"))
        assertFalse(state.message.contains("boom"))
        assertTrue(fixture.sessions.rows.isEmpty())
        assertTrue(state.commands.isEmpty())
    }

    @Test
    fun noReadyToResumeUiExists() = runBlocking {
        val fixture = Fixture()
        val available = fixture.controller.refresh()
        val running = fixture.controller.startSession()
        fixture.clock.instant = openedAt.plusSeconds(120)
        val expired = fixture.controller.refresh()
        val labels = listOf(
            available.stateLabel,
            running.stateLabel,
            expired.stateLabel,
            available.statusLabel,
        ) + StationCommand.entries.map { it.label() }

        assertFalse(labels.any { it.contains("resume", ignoreCase = true) })
        assertFalse(StationCommand.entries.any { it.name.contains("Resume") })
        assertFalse(StationPhase.entries.any { it.name.contains("Resume") })
        assertEquals(
            listOf(StationMessages.Available, StationMessages.GameRunning, StationMessages.TimeExpired),
            listOf(available.stateLabel, running.stateLabel, expired.stateLabel),
        )
    }

    private suspend fun expire(fixture: Fixture): SessionSnapshot {
        fixture.controller.startSession()
        fixture.clock.instant = openedAt.plusSeconds(120)
        fixture.controller.refresh()
        return fixture.sessions.only()
    }
}

private val openedAt: Instant = Instant.parse("2026-10-05T18:00:00Z")

private class Fixture {
    val clock = MutableClock(openedAt)
    val stations = MemoryStations()
    val sessions = MemorySessions()
    val controller = controller()
    val station = GamingStation(LocalStation.Id, LocalStation.Name, LocalStation.Code)

    fun restart(): GamingStationController = controller(SequenceIds("session-2"))

    private fun controller(ids: SessionIds = SequenceIds("session-1")): GamingStationController =
        GamingStationController(stations, sessions, clock, ids)
}

private class MutableClock(var instant: Instant) : DomainClock {
    override fun now(): Instant = instant
}

private class SequenceIds(private val values: Iterator<String>) : SessionIds {
    constructor(vararg values: String) : this(values.iterator())

    override fun newId(): String = values.next()
}

private class MemoryStations : GamingStationRepository {
    private val rows = linkedMapOf<String, GamingStation>()

    override suspend fun save(station: GamingStation) {
        rows[station.id] = station
    }

    override suspend fun get(id: String): GamingStation? = rows[id]

    override suspend fun list(): List<GamingStation> = rows.values.toList()
}

private class MemorySessions : SessionRepository {
    val rows = linkedMapOf<String, SessionSnapshot>()
    var failSave = false

    override suspend fun save(snapshot: SessionSnapshot) {
        if (failSave) error("sqlite boom")
        rows[snapshot.sessionId] = snapshot
    }

    override suspend fun update(snapshot: SessionSnapshot) {
        check(rows.containsKey(snapshot.sessionId))
        save(snapshot)
    }

    override suspend fun close(snapshot: SessionSnapshot) {
        check(snapshot.state == SessionState.Closed)
        save(snapshot)
    }

    override suspend fun getById(sessionId: String): SessionSnapshot? = rows[sessionId]

    override suspend fun getOpenSession(stationId: String): SessionSnapshot? =
        rows.values.firstOrNull { it.stationId == stationId && it.state != SessionState.Closed }

    override suspend fun listOpenSessions(): List<SessionSnapshot> =
        rows.values.filter { it.state != SessionState.Closed }

    fun only(): SessionSnapshot = rows.values.single()
}
