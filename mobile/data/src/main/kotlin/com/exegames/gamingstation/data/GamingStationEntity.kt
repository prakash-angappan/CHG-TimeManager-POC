package com.exegames.gamingstation.data

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import java.time.Instant

@Entity(
    tableName = "gaming_stations",
    indices = [Index(value = ["stationCode"], unique = true)],
)
data class GamingStationEntity(
    @PrimaryKey val id: String,
    val name: String,
    val stationCode: String,
    val createdAt: Instant,
)
