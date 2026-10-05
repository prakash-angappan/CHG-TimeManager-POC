package com.exegames.gamingstation.domain

import java.time.Duration
import java.time.Instant
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

class SessionStateMachineTest {
    private val openedAt = Instant.parse("2026-10-05T18:00:00Z")
    private val clock = SettableClock(openedAt)
    private val ids = SequenceIds("session-1", "session-2")
    private val machine = SessionStateMachine(clock, ids)
    private val station = GamingStation(id = "station-1", name = "PS5 #01", stationCode = "PS5-01")

    @Test
    fun startSessionMovesAvailableStationToRunning() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))

        assertEquals("session-1", started.sessionId)
        assertEquals(station.id, started.stationId)
        assertEquals("PS5-01", started.stationCode)
        assertEquals("PS5 #01", started.stationName)
        assertEquals(SessionState.Running, started.state)
        assertEquals(openedAt, started.startTimeUtc)
        assertEquals(openedAt.plusSeconds(120), started.expiryTimeUtc)
        assertEquals(120, started.durationSeconds)
        assertEquals(1, started.revision)
    }

    @Test
    fun runningSessionHasCorrectRemainingTime() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))

        assertEquals(Duration.ofSeconds(90), machine.remainingTime(started, openedAt.plusSeconds(30)))
        assertEquals(Duration.ofSeconds(120), started.remainingTime(openedAt))
    }

    @Test
    fun runningSessionExpiresOnlyWhenTheExpiryTimestampIsReached() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        clock.instant = openedAt.plusSeconds(119)
        val early = machine.expire(started)
        assertRejected(early, RejectionReason.NotYetExpired)

        clock.instant = openedAt.plusSeconds(120)
        val expired = applied(machine.expire(started))
        assertEquals(SessionState.AwaitingPayment, expired.state)
    }

    @Test
    fun expiryProducesAwaitingPaymentWithoutChangingTheWindow() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        clock.instant = openedAt.plusSeconds(120)

        val expired = applied(machine.expire(started))

        assertEquals(SessionState.AwaitingPayment, expired.state)
        assertEquals(started.sessionId, expired.sessionId)
        assertEquals(started.startTimeUtc, expired.startTimeUtc)
        assertEquals(started.expiryTimeUtc, expired.expiryTimeUtc)
        assertEquals(started.durationSeconds, expired.durationSeconds)
        assertEquals(2, expired.revision)
    }

    @Test
    fun paymentResumesTheSameSession() {
        val awaiting = awaitingPayment()
        clock.instant = openedAt.plusSeconds(150)

        val resumed = applied(machine.markPayment(awaiting, durationSeconds = 120))

        assertEquals(awaiting.sessionId, resumed.sessionId)
        assertEquals(awaiting.stationId, resumed.stationId)
        assertEquals(awaiting.stationCode, resumed.stationCode)
        assertEquals(awaiting.stationName, resumed.stationName)
        assertEquals(SessionState.Running, resumed.state)
    }

    @Test
    fun paymentCreatesANewExpiryWindow() {
        val awaiting = awaitingPayment()
        val paidAt = openedAt.plusSeconds(150)
        clock.instant = paidAt

        val resumed = applied(machine.markPayment(awaiting, durationSeconds = 120))

        assertEquals(paidAt, resumed.startTimeUtc)
        assertEquals(paidAt.plusSeconds(120), resumed.expiryTimeUtc)
        assertEquals(120, resumed.durationSeconds)
        assertTrue(resumed.expiryTimeUtc.isAfter(awaiting.expiryTimeUtc))
    }

    @Test
    fun paymentIncrementsRevision() {
        val awaiting = awaitingPayment()
        clock.instant = openedAt.plusSeconds(150)

        val resumed = applied(machine.markPayment(awaiting, durationSeconds = 120))

        assertEquals(awaiting.revision + 1, resumed.revision)
        assertEquals(3, resumed.revision)
    }

    @Test
    fun endFromRunningClosesTheSession() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))

        val closed = applied(machine.end(started))

        assertEquals(SessionState.Closed, closed.state)
        assertEquals(started.sessionId, closed.sessionId)
        assertEquals(started.expiryTimeUtc, closed.expiryTimeUtc)
        assertEquals(2, closed.revision)
    }

    @Test
    fun endFromAwaitingPaymentClosesTheSession() {
        val awaiting = awaitingPayment()

        val closed = applied(machine.end(awaiting))

        assertEquals(SessionState.Closed, closed.state)
        assertEquals(awaiting.sessionId, closed.sessionId)
        assertEquals(3, closed.revision)
    }

    @Test
    fun closedSessionCannotBePaid() {
        val closed = applied(machine.end(awaitingPayment()))

        assertRejected(machine.markPayment(closed, durationSeconds = 120), RejectionReason.NotAwaitingPayment)
    }

    @Test
    fun closedSessionCannotBeEndedAgain() {
        val closed = applied(machine.end(awaitingPayment()))

        assertRejected(machine.end(closed), RejectionReason.AlreadyClosed)
    }

    @Test
    fun invalidTransitionsAreRejected() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))

        assertRejected(machine.start(station, started, durationSeconds = 120), RejectionReason.StationOccupied)
        assertRejected(machine.markPayment(started, durationSeconds = 120), RejectionReason.NotAwaitingPayment)
        assertRejected(machine.expire(applied(machine.end(started))), RejectionReason.NotRunning)
        assertRejected(machine.start(station, current = null, durationSeconds = 0), RejectionReason.DurationMustBePositive)

        val awaiting = awaitingPayment()
        assertRejected(machine.start(station, awaiting, durationSeconds = 120), RejectionReason.StationOccupied)
        assertRejected(machine.expire(awaiting), RejectionReason.NotRunning)
        assertRejected(machine.markPayment(awaiting, durationSeconds = -1), RejectionReason.DurationMustBePositive)
    }

    @Test
    fun remainingTimeIsDerivedFromExpiryAndDoesNotMutateTheSnapshot() {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        val before = started.copy()

        val afterThirtySeconds = machine.remainingTime(started, openedAt.plusSeconds(30))
        val afterNinetySeconds = started.remainingTime(openedAt.plusSeconds(90))
        val afterTheWindow = started.remainingTime(openedAt.plusSeconds(500))

        assertEquals(Duration.ofSeconds(90), afterThirtySeconds)
        assertEquals(Duration.ofSeconds(30), afterNinetySeconds)
        assertEquals(Duration.ZERO, afterTheWindow)
        assertEquals(before, started)
    }

    @Test
    fun snapshotsWithDifferentRevisionsCanBeCompared() {
        val awaiting = awaitingPayment()
        clock.instant = openedAt.plusSeconds(150)
        val resumed = applied(machine.markPayment(awaiting, durationSeconds = 120))

        assertTrue(resumed.isNewerThan(awaiting))
        assertFalse(awaiting.isNewerThan(resumed))
        assertEquals(awaiting.revision + 1, resumed.revision)
    }

    @Test
    fun startingAfterClosedCreatesANewRunningSession() {
        val closed = applied(machine.end(awaitingPayment()))
        clock.instant = openedAt.plusSeconds(300)

        val next = applied(machine.start(station, closed, durationSeconds = 90))

        assertNotEquals(closed.sessionId, next.sessionId)
        assertEquals("session-2", next.sessionId)
        assertEquals(SessionState.Running, next.state)
        assertEquals(1, next.revision)
        assertEquals(90, next.durationSeconds)
        assertEquals(openedAt.plusSeconds(300), next.startTimeUtc)
        assertEquals(next.startTimeUtc.plusSeconds(90), next.expiryTimeUtc)
    }

    private fun awaitingPayment(): SessionSnapshot {
        val started = applied(machine.start(station, current = null, durationSeconds = 120))
        clock.instant = openedAt.plusSeconds(120)
        return applied(machine.expire(started))
    }

    private fun applied(change: SessionChange): SessionSnapshot {
        val applied = assertIs<SessionChange.Applied>(change)
        return applied.snapshot
    }

    private fun assertRejected(change: SessionChange, reason: RejectionReason) {
        val rejected = assertIs<SessionChange.Rejected>(change)
        assertEquals(reason, rejected.reason)
    }
}

private class SettableClock(var instant: Instant) : DomainClock {
    override fun now(): Instant = instant
}

private class SequenceIds(private val values: Iterator<String>) : SessionIds {
    constructor(vararg values: String) : this(values.iterator())

    override fun newId(): String = values.next()
}
