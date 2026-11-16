package com.exegames.gamingstation.domain

/**
 * Persistence port for stations. Implementations store rows; they do not apply session rules.
 */
interface GamingStationRepository {
    suspend fun save(station: GamingStation)

    suspend fun get(id: String): GamingStation?

    suspend fun list(): List<GamingStation>
}
