package com.exegames.gamingstation.data

import com.exegames.gamingstation.domain.GamingStation
import com.exegames.gamingstation.domain.SessionSnapshot
import com.exegames.gamingstation.domain.SessionState
import java.time.Instant

fun GamingStationEntity.toDomain(): GamingStation = GamingStation(
    id = id,
    name = name,
    stationCode = stationCode,
)

fun GamingStation.toEntity(createdAt: Instant): GamingStationEntity = GamingStationEntity(
    id = id,
    name = name,
    stationCode = stationCode,
    createdAt = createdAt,
)

fun SessionEntity.toSnapshot(): SessionSnapshot = SessionSnapshot(
    sessionId = id,
    stationId = stationId,
    stationCode = stationCode,
    stationName = stationName,
    state = SessionState.valueOf(state),
    startTimeUtc = startTimeUtc,
    expiryTimeUtc = expiryTimeUtc,
    durationSeconds = durationSeconds,
    revision = revision,
)

fun SessionSnapshot.toEntity(createdAt: Instant, updatedAt: Instant): SessionEntity = SessionEntity(
    id = sessionId,
    stationId = stationId,
    stationCode = stationCode,
    stationName = stationName,
    state = state.name,
    startTimeUtc = startTimeUtc,
    expiryTimeUtc = expiryTimeUtc,
    durationSeconds = durationSeconds,
    revision = revision,
    createdAt = createdAt,
    updatedAt = updatedAt,
)
