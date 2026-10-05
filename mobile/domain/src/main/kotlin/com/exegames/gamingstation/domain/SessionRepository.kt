package com.exegames.gamingstation.domain

/**
 * Persistence port for sessions.
 * Callers pass snapshots already produced by [SessionStateMachine].
 * Implementations must not change state, revision, or the expiry window themselves.
 */
interface SessionRepository {
    suspend fun save(snapshot: SessionSnapshot)

    suspend fun update(snapshot: SessionSnapshot)

    /**
     * Persists a snapshot [SessionStateMachine.end] has already moved to [SessionState.Closed].
     */
    suspend fun close(snapshot: SessionSnapshot)

    suspend fun getById(sessionId: String): SessionSnapshot?

    /** The station's session in [SessionState.Running] or [SessionState.AwaitingPayment], if any. */
    suspend fun getOpenSession(stationId: String): SessionSnapshot?

    suspend fun listOpenSessions(): List<SessionSnapshot>
}
