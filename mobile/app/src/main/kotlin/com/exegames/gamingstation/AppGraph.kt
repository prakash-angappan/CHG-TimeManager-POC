package com.exegames.gamingstation

import android.app.Application
import com.exegames.gamingstation.data.GamingStationDatabase
import com.exegames.gamingstation.data.RoomGamingStationRepository
import com.exegames.gamingstation.data.RoomSessionRepository
import com.exegames.gamingstation.session.GamingStationController
import com.exegames.gamingstation.session.GamingStationViewModelFactory

class GamingStationApplication : Application() {
    lateinit var graph: AppGraph
        private set

    override fun onCreate() {
        super.onCreate()
        graph = AppGraph(this)
    }
}

class AppGraph(application: Application) {
    private val clock = SystemDomainClock
    private val database = GamingStationDatabase.create(application)
    private val stations = RoomGamingStationRepository(database, clock)
    private val sessions = RoomSessionRepository(database, clock)
    val controller = GamingStationController(
        stations = stations,
        sessions = sessions,
        clock = clock,
        ids = UuidSessionIds,
    )
    val viewModelFactory = GamingStationViewModelFactory(controller)
}
