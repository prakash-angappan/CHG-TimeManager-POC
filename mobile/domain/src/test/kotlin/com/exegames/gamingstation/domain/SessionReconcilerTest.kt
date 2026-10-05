package com.exegames.gamingstation.domain

import java.time.Instant
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlinx.coroutines.runBlocking

class SessionReconcilerTest {
    private val openedAt = Instant.parse("2026-10-05T18:00:00Z")
    private val clock = ReconcileClock(openedAt)
    private val station = GamingStation(id = "station-1", name = "PS5 #01", stationCode = "PS5-01")
    private val machine = SessionStateMachine(clock, ReconcileIds("session-1"))

    @Test
    fun runningSessionBeforeExpiryStaysRunning() = runBlocking {
        val sessions = MemorySessions()
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        sessions.save(started)
        clock.instant = openedAt.plusSeconds(119)

        val reconciled = reconciler(sessions).reconcile()

        assertEquals(listOf(station), reconciled.stations)
        assertEquals(started, reconciled.openSessions.single())
        assertEquals(1L, sessions.getById(started.sessionId)?.revision)
    }

    @Test
    fun runningSessionAtExpiryBecomesAwaitingPaymentOnce() = runBlocking {
        val sessions = MemorySessions()
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        sessions.save(started)
        clock.instant = started.expiryTimeUtc

        val once = reconciler(sessions).reconcile().openSessions.single()
        val twice = reconciler(sessions).reconcile().openSessions.single()

        assertEquals(started.sessionId, once.sessionId)
        assertEquals(SessionState.AwaitingPayment, once.state)
        assertEquals(started.startTimeUtc, once.startTimeUtc)
        assertEquals(started.expiryTimeUtc, once.expiryTimeUtc)
        assertEquals(started.durationSeconds, once.durationSeconds)
        assertEquals(2L, once.revision)
        assertEquals(once, twice)
    }

    @Test
    fun awaitingPaymentAndClosedSessionsAreNotExpiredAgain() = runBlocking {
        val sessions = MemorySessions()
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        clock.instant = started.expiryTimeUtc
        val awaiting = applied(machine.expire(started))
        sessions.save(awaiting)

        val stillAwaiting = reconciler(sessions).reconcile().openSessions.single()
        assertEquals(awaiting, stillAwaiting)

        val closed = applied(machine.end(stillAwaiting))
        sessions.close(closed)
        clock.instant = openedAt.plusSeconds(500)

        val reconciled = reconciler(sessions).reconcile()
        assertEquals(emptyList(), reconciled.openSessions)
        assertNull(sessions.getOpenSession(station.id))
        assertEquals(closed, sessions.getById(closed.sessionId))
    }

    private fun reconciler(sessions: SessionRepository) = SessionReconciler(
        stations = MemoryStations(station),
        sessions = sessions,
        machine = machine,
    )

    private fun applied(change: SessionChange): SessionSnapshot {
        val applied = change as SessionChange.Applied
        return applied.snapshot
    }
}

private class ReconcileClock(var instant: Instant) : DomainClock {
    override fun now(): Instant = instant
}

private class ReconcileIds(private val values: Iterator<String>) : SessionIds {
    constructor(vararg values: String) : this(values.iterator())

    override fun newId(): String = values.next()
}

private class MemoryStations(private val station: GamingStation) : GamingStationRepository {
    override suspend fun save(station: GamingStation) = Unit

    override suspend fun get(id: String): GamingStation? = station.takeIf { it.id == id }

    override suspend fun list(): List<GamingStation> = listOf(station)
}

private class MemorySessions : SessionRepository {
    private val rows = linkedMapOf<String, SessionSnapshot>()

    override suspend fun save(snapshot: SessionSnapshot) {
        rows[snapshot.sessionId] = snapshot
    }

    override suspend fun update(snapshot: SessionSnapshot) {
        check(rows.containsKey(snapshot.sessionId))
        rows[snapshot.sessionId] = snapshot
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
}
