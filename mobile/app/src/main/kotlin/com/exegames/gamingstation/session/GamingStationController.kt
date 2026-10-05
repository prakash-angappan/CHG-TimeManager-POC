package com.exegames.gamingstation.session

import com.exegames.gamingstation.domain.DomainClock
import com.exegames.gamingstation.domain.GamingStation
import com.exegames.gamingstation.domain.GamingStationRepository
import com.exegames.gamingstation.domain.SessionChange
import com.exegames.gamingstation.domain.SessionIds
import com.exegames.gamingstation.domain.SessionReconciler
import com.exegames.gamingstation.domain.SessionRepository
import com.exegames.gamingstation.domain.SessionSnapshot
import com.exegames.gamingstation.domain.SessionStateMachine
import kotlinx.coroutines.CancellationException

/**
 * Phone-side session commands. Transitions go through [SessionStateMachine].
 * Expiry goes through [SessionReconciler]. This type does not decide expiry itself.
 */
class GamingStationController(
    private val stations: GamingStationRepository,
    private val sessions: SessionRepository,
    private val clock: DomainClock,
    ids: SessionIds,
    private val durationSeconds: Int = LocalStation.DurationSeconds,
) {
    private val machine = SessionStateMachine(clock, ids)
    private val reconciler = SessionReconciler(stations, sessions, machine)

    suspend fun refresh(): StationUiState = guard {
        val (station, open) = current()
        presentStation(station, open, clock.now(), message = null)
    }

    suspend fun startSession(): StationUiState = guard {
        val (station, open) = current()
        when (val change = machine.start(station, open, durationSeconds)) {
            is SessionChange.Applied -> {
                sessions.save(change.snapshot)
                presentStation(station, change.snapshot, clock.now(), message = null)
            }
            is SessionChange.Rejected -> presentStation(station, open, clock.now(), change.reason.message)
        }
    }

    suspend fun markPayment(): StationUiState = guard {
        val (station, open) = current()
        if (open == null) {
            presentStation(station, null, clock.now(), StationMessages.NoPaymentPending)
        } else {
            when (val change = machine.markPayment(open, durationSeconds)) {
                is SessionChange.Applied -> {
                    sessions.save(change.snapshot)
                    presentStation(station, change.snapshot, clock.now(), message = null)
                }
                is SessionChange.Rejected -> presentStation(station, open, clock.now(), change.reason.message)
            }
        }
    }

    suspend fun endSession(): StationUiState = guard {
        val (station, open) = current()
        if (open == null) {
            presentStation(station, null, clock.now(), StationMessages.NoSessionToEnd)
        } else {
            when (val change = machine.end(open)) {
                is SessionChange.Applied -> {
                    sessions.close(change.snapshot)
                    presentStation(station, null, clock.now(), message = null)
                }
                is SessionChange.Rejected -> presentStation(station, open, clock.now(), change.reason.message)
            }
        }
    }

    private suspend fun current(): Pair<GamingStation, SessionSnapshot?> {
        val station = ensureStation()
        val open = reconciler.reconcile().openSessions.firstOrNull { it.stationId == station.id }
        return station to open
    }

    private suspend fun ensureStation(): GamingStation {
        val existing = stations.list()
        existing.firstOrNull { it.stationCode == LocalStation.Code }?.let { return it }
        if (existing.isNotEmpty()) return existing.first()
        val created = GamingStation(
            id = LocalStation.Id,
            name = LocalStation.Name,
            stationCode = LocalStation.Code,
        )
        stations.save(created)
        return created
    }

    private suspend fun guard(block: suspend () -> StationUiState): StationUiState {
        return try {
            block()
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Exception) {
            StationUiState.Loading.copy(
                stationName = LocalStation.Name,
                stationCode = LocalStation.Code,
                stateLabel = StationMessages.Available,
                statusLabel = StationMessages.Status,
                commands = emptyList(),
                message = StationMessages.SaveFailed,
                ready = true,
            )
        }
    }
}
