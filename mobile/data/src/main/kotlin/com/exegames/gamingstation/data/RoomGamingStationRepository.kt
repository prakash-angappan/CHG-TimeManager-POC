package com.exegames.gamingstation.data

import androidx.room.withTransaction
import com.exegames.gamingstation.domain.DomainClock
import com.exegames.gamingstation.domain.GamingStation
import com.exegames.gamingstation.domain.GamingStationRepository

class RoomGamingStationRepository(
    private val database: GamingStationDatabase,
    private val clock: DomainClock,
) : GamingStationRepository {
    private val dao = database.gamingStationDao()

    override suspend fun save(station: GamingStation) {
        database.withTransaction {
            val existing = dao.get(station.id)
            if (existing == null) {
                dao.insert(station.toEntity(createdAt = clock.now()))
            } else {
                dao.update(station.toEntity(createdAt = existing.createdAt))
            }
        }
    }

    override suspend fun get(id: String): GamingStation? = dao.get(id)?.toDomain()

    override suspend fun list(): List<GamingStation> = dao.list().map { it.toDomain() }
}
