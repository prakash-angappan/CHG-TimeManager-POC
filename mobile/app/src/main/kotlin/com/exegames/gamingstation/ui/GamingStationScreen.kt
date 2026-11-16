package com.exegames.gamingstation.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import com.exegames.gamingstation.session.StationCommand
import com.exegames.gamingstation.session.StationUiState

@Composable
fun GamingStationTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = lightColorScheme(),
        content = content,
    )
}

/**
 * Displays [StationUiState]. It does not start, expire, pay, or end a session.
 */
@Composable
fun GamingStationScreen(
    state: StationUiState,
    onCommand: (StationCommand) -> Unit,
) {
    if (!state.ready) {
        Box(
            modifier = Modifier.fillMaxSize(),
            contentAlignment = Alignment.Center,
        ) {
            Text("Loading the station…")
        }
        return
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text(
            text = state.statusLabel,
            style = MaterialTheme.typography.labelLarge,
        )
        Card(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Text(
                    text = state.stationName,
                    style = MaterialTheme.typography.headlineMedium,
                )
                Text(
                    text = state.stationCode,
                    style = MaterialTheme.typography.titleMedium,
                )
                Text(
                    text = state.stateLabel,
                    style = MaterialTheme.typography.displaySmall,
                )
                val countdown = state.countdown
                if (countdown != null) {
                    Text(
                        text = countdown,
                        style = MaterialTheme.typography.displayLarge,
                        fontFamily = FontFamily.Monospace,
                    )
                }
            }
        }
        val message = state.message
        if (message != null) {
            Text(
                text = message,
                color = MaterialTheme.colorScheme.error,
                style = MaterialTheme.typography.bodyLarge,
            )
        }
        state.commands.forEach { command ->
            Button(
                onClick = { onCommand(command) },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(64.dp),
            ) {
                Text(
                    text = command.label(),
                    style = MaterialTheme.typography.titleMedium,
                )
            }
        }
    }
}
