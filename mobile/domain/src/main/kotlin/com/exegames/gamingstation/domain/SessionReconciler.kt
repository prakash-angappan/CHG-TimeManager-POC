package com.exegames.gamingstation.domain

/**
 * Startup recovery. Loads what was stored, then asks [SessionStateMachine]
 * whether a running session is due. The machine reads its [DomainClock];
 * this type does not compare timestamps itself.
 */
class SessionReconciler(
    private val stations: GamingStationRepository,
    private val sessions: SessionRepository,
    private val machine: SessionStateMachine,
) {
    suspend fun reconcile(): ReconciledState {
        val loadedStations = stations.list()
        val openSessions = sessions.listOpenSessions().map { session ->
            when (session.state) {
                SessionState.Running -> applyExpiry(session)
                SessionState.AwaitingPayment -> session
                SessionState.Closed -> session
            }
        }
        return ReconciledState(
            stations = loadedStations,
            openSessions = openSessions,
        )
    }

    private suspend fun applyExpiry(session: SessionSnapshot): SessionSnapshot {
        return when (val change = machine.expire(session)) {
            is SessionChange.Applied -> {
                sessions.save(change.snapshot)
                change.snapshot
            }
            is SessionChange.Rejected -> session
        }
    }
}

data class ReconciledState(
    val stations: List<GamingStation>,
    val openSessions: List<SessionSnapshot>,
)
