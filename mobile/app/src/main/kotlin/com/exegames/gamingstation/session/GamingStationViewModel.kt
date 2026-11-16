package com.exegames.gamingstation.session

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

class GamingStationViewModel(
    private val controller: GamingStationController,
) : ViewModel() {
    private val gate = Mutex()
    private val _state = MutableStateFlow(StationUiState.Loading)
    val state: StateFlow<StationUiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            gate.withLock { publishTick() }
        }
        viewModelScope.launch {
            while (isActive) {
                delay(REFRESH_INTERVAL_MS)
                gate.withLock { publishTick() }
            }
        }
    }

    fun onCommand(command: StationCommand) {
        viewModelScope.launch {
            gate.withLock {
                _state.value = when (command) {
                    StationCommand.StartSession -> controller.startSession()
                    StationCommand.MarkPayment -> controller.markPayment()
                    StationCommand.EndSession -> controller.endSession()
                }
            }
        }
    }

    private suspend fun publishTick() {
        val previous = _state.value
        val refreshed = controller.refresh()
        val carried = if (refreshed.phase != previous.phase || !previous.ready) {
            null
        } else {
            previous.message
        }
        _state.value = if (refreshed.message != null) refreshed else refreshed.copy(message = carried)
    }

    private companion object {
        const val REFRESH_INTERVAL_MS = 1_000L
    }
}

class GamingStationViewModelFactory(
    private val controller: GamingStationController,
) : ViewModelProvider.Factory {
    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T {
        if (modelClass.isAssignableFrom(GamingStationViewModel::class.java)) {
            return GamingStationViewModel(controller) as T
        }
        error("Unknown model ${modelClass.name}")
    }
}
