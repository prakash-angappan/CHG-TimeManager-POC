package com.exegames.gamingstation.data

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import java.time.Instant

/**
 * Persistence row for one session. Remaining time is not a column.
 * It is calculated from [expiryTimeUtc] when a snapshot is read.
 */
@Entity(
    tableName = "sessions",
    indices = [Index(value = ["stationId"])],
)
data class SessionEntity(
    @PrimaryKey val id: String,
    val stationId: String,
    val stationCode: String,
    val stationName: String,
    val state: String,
    val startTimeUtc: Instant,
    val expiryTimeUtc: Instant,
    val durationSeconds: Int,
    val revision: Long,
    val createdAt: Instant,
    val updatedAt: Instant,
)
