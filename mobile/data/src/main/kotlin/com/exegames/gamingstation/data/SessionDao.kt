package com.exegames.gamingstation.data

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.Query
import androidx.room.Update

@Dao
interface SessionDao {
    @Insert
    suspend fun insert(entity: SessionEntity)

    @Update
    suspend fun update(entity: SessionEntity): Int

    @Query("SELECT * FROM sessions WHERE id = :id")
    suspend fun get(id: String): SessionEntity?

    @Query(
        """
        SELECT * FROM sessions
        WHERE stationId = :stationId AND state != 'Closed'
        LIMIT 1
        """,
    )
    suspend fun getOpen(stationId: String): SessionEntity?

    @Query(
        """
        SELECT * FROM sessions
        WHERE state != 'Closed'
        ORDER BY id
        """,
    )
    suspend fun listOpen(): List<SessionEntity>
}
