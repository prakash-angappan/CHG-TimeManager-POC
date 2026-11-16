package com.exegames.gamingstation.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Update

@Dao
interface GamingStationDao {
    @Insert
    suspend fun insert(entity: GamingStationEntity)

    @Update
    suspend fun update(entity: GamingStationEntity)

    @Query("SELECT * FROM gaming_stations WHERE id = :id")
    suspend fun get(id: String): GamingStationEntity?

    @Query("SELECT * FROM gaming_stations ORDER BY stationCode")
    suspend fun list(): List<GamingStationEntity>
}
