package com.exegames.gamingstation.domain

import java.time.Duration
import java.time.Instant

/**
 * Pure session transitions. Persistence, networking, and UI stay outside this type.
 * Pass null as the current session when the station is available.
 */
class SessionStateMachine(
    private val clock: DomainClock,
    private val ids: SessionIds,
) {
    fun start(station: GamingStation, current: SessionSnapshot?, durationSeconds: Int): SessionChange {
        if (durationSeconds <= 0) {
            return reject(RejectionReason.DurationMustBePositive)
        }
        if (current != null && current.state != SessionState.Closed) {
            return reject(RejectionReason.StationOccupied)
        }

        val start = clock.now()
        return SessionChange.Applied(
            SessionSnapshot(
                sessionId = ids.newId(),
                stationId = station.id,
                stationCode = station.stationCode,
                stationName = station.name,
                state = SessionState.Running,
                startTimeUtc = start,
                expiryTimeUtc = start.plusSeconds(durationSeconds.toLong()),
                durationSeconds = durationSeconds,
                revision = 1,
            ),
        )
    }

    fun expire(current: SessionSnapshot): SessionChange {
        if (current.state != SessionState.Running) {
            return reject(RejectionReason.NotRunning)
        }
        if (clock.now() < current.expiryTimeUtc) {
            return reject(RejectionReason.NotYetExpired)
        }

        return SessionChange.Applied(
            current.copy(
                state = SessionState.AwaitingPayment,
                revision = current.revision + 1,
            ),
        )
    }

    fun markPayment(current: SessionSnapshot, durationSeconds: Int): SessionChange {
        if (durationSeconds <= 0) {
            return reject(RejectionReason.DurationMustBePositive)
        }
        if (current.state != SessionState.AwaitingPayment) {
            return reject(RejectionReason.NotAwaitingPayment)
        }

        val start = clock.now()
        return SessionChange.Applied(
            current.copy(
                state = SessionState.Running,
                startTimeUtc = start,
                expiryTimeUtc = start.plusSeconds(durationSeconds.toLong()),
                durationSeconds = durationSeconds,
                revision = current.revision + 1,
            ),
        )
    }

    fun end(current: SessionSnapshot): SessionChange {
        if (current.state == SessionState.Closed) {
            return reject(RejectionReason.AlreadyClosed)
        }

        return SessionChange.Applied(
            current.copy(
                state = SessionState.Closed,
                revision = current.revision + 1,
            ),
        )
    }

    fun remainingTime(snapshot: SessionSnapshot, at: Instant = clock.now()): Duration =
        snapshot.remainingTime(at)

    private fun reject(reason: RejectionReason): SessionChange = SessionChange.Rejected(reason)
}
