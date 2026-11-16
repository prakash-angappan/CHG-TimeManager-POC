package com.exegames.gamingstation.data

import androidx.room.TypeConverter
import java.time.Instant

/**
 * Stores [Instant] as ISO-8601 text from [Instant.toString].
 * [Instant.parse] restores the same instant, including nanoseconds.
 */
class InstantConverters {
    @TypeConverter
    fun toStored(value: Instant): String = value.toString()

    @TypeConverter
    fun fromStored(value: String): Instant = Instant.parse(value)
}
