package com.exegames.gamingstation.session

import com.exegames.gamingstation.domain.GamingStation
import com.exegames.gamingstation.domain.SessionSnapshot
import com.exegames.gamingstation.domain.SessionState
import java.time.Duration
import java.time.Instant

object LocalStation {
    const val Id = "local-ps5-01"
    const val Name = "PS5 #01"
    const val Code = "PS5-01"
    const val DurationSeconds = 120
}

object StationMessages {
    const val Status = "Local only"
    const val Available = "AVAILABLE"
    const val GameRunning = "GAME RUNNING"
    const val TimeExpired = "TIME EXPIRED"
    const val Start = "START SESSION"
    const val Payment = "MARK PAYMENT"
    const val End = "END SESSION"
    const val NoSessionToEnd = "No session to end."
    const val NoPaymentPending = "No payment is pending."
    const val SaveFailed = "Couldn't update the station. Try again."
}

enum class StationPhase {
    Available,
    GameRunning,
    TimeExpired,
}

enum class StationCommand {
    StartSession,
    MarkPayment,
    EndSession,
    ;

    fun label(): String = when (this) {
        StartSession -> StationMessages.Start
        MarkPayment -> StationMessages.Payment
        EndSession -> StationMessages.End
    }
}

data class StationUiState(
    val stationName: String,
    val stationCode: String,
    val phase: StationPhase,
    val stateLabel: String,
    val countdown: String?,
    val statusLabel: String,
    val commands: List<StationCommand>,
    val message: String?,
    val ready: Boolean = true,
) {
    companion object {
        val Loading = StationUiState(
            stationName = "",
            stationCode = "",
            phase = StationPhase.Available,
            stateLabel = "",
            countdown = null,
            statusLabel = StationMessages.Status,
            commands = emptyList(),
            message = null,
            ready = false,
        )
    }
}

/**
 * Maps a station and its open snapshot to screen state.
 * Remaining time is calculated from [SessionSnapshot.expiryTimeUtc]. This function does not persist.
 */
fun presentStation(
    station: GamingStation,
    openSession: SessionSnapshot?,
    now: Instant,
    message: String?,
): StationUiState {
    val phase = when (openSession?.state) {
        null, SessionState.Closed -> StationPhase.Available
        SessionState.Running -> StationPhase.GameRunning
        SessionState.AwaitingPayment -> StationPhase.TimeExpired
    }
    val countdown = if (openSession?.state == SessionState.Running) {
        formatCountdown(openSession.remainingTime(now))
    } else {
        null
    }
    val commands = when (phase) {
        StationPhase.Available -> listOf(StationCommand.StartSession)
        StationPhase.GameRunning -> listOf(StationCommand.EndSession)
        StationPhase.TimeExpired -> listOf(StationCommand.MarkPayment, StationCommand.EndSession)
    }
    val stateLabel = when (phase) {
        StationPhase.Available -> StationMessages.Available
        StationPhase.GameRunning -> StationMessages.GameRunning
        StationPhase.TimeExpired -> StationMessages.TimeExpired
    }
    return StationUiState(
        stationName = station.name,
        stationCode = station.stationCode,
        phase = phase,
        stateLabel = stateLabel,
        countdown = countdown,
        statusLabel = StationMessages.Status,
        commands = commands,
        message = message,
    )
}

fun formatCountdown(remaining: Duration): String {
    val totalSeconds = remaining.seconds.coerceAtLeast(0)
    val minutes = totalSeconds / 60
    val seconds = totalSeconds % 60
    return "%02d:%02d".format(minutes, seconds)
}
