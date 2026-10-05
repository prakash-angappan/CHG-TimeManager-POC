package com.exegames.gamingstation

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.getValue
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.exegames.gamingstation.session.GamingStationViewModel
import com.exegames.gamingstation.ui.GamingStationScreen
import com.exegames.gamingstation.ui.GamingStationTheme

class GamingStationActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val viewModel = ViewModelProvider(
            this,
            (application as GamingStationApplication).graph.viewModelFactory,
        )[GamingStationViewModel::class.java]
        setContent {
            GamingStationTheme {
                val state by viewModel.state.collectAsStateWithLifecycle()
                GamingStationScreen(
                    state = state,
                    onCommand = viewModel::onCommand,
                )
            }
        }
    }
}
