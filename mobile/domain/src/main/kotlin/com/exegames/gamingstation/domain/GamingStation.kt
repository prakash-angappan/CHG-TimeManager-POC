package com.exegames.gamingstation.domain

/**
 * A gaming station. The display device is not part of this model.
 */
data class GamingStation(
    val id: String,
    val name: String,
    val stationCode: String,
)
