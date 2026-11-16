package com.exegames.gamingstation.domain

import java.time.Duration
import java.time.Instant

/**
 * Authoritative session view. Remaining time is not stored.
 * [revision] increases by one on every committed change to this same session.
 * A later session, started after [SessionState.Closed], begins again at revision 1.
 */
data class SessionSnapshot(
    val sessionId: String,
    val stationId: String,
    val stationCode: String,
    val stationName: String,
    val state: SessionState,
    val startTimeUtc: Instant,
    val expiryTimeUtc: Instant,
    val durationSeconds: Int,
    val revision: Long,
) {
    /** Higher [revision] is the newer committed update of a session. */
    fun isNewerThan(other: SessionSnapshot): Boolean = revision > other.revision

    fun remainingTime(at: Instant): Duration {
        val remaining = Duration.between(at, expiryTimeUtc)
        return if (remaining.isNegative) Duration.ZERO else remaining
    }
}
