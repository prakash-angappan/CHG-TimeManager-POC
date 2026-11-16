package com.exegames.gamingstation.data

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.TypeConverters

const val GAMING_STATION_SCHEMA_VERSION = 1

@Database(
    entities = [GamingStationEntity::class, SessionEntity::class],
    version = GAMING_STATION_SCHEMA_VERSION,
    exportSchema = true,
)
@TypeConverters(InstantConverters::class)
abstract class GamingStationDatabase : RoomDatabase() {
    abstract fun gamingStationDao(): GamingStationDao

    abstract fun sessionDao(): SessionDao

    companion object {
        const val NAME = "gaming_station.db"

        fun create(context: Context): GamingStationDatabase =
            Room.databaseBuilder(context, GamingStationDatabase::class.java, NAME).build()
    }
}
