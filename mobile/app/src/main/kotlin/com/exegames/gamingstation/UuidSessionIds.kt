package com.exegames.gamingstation

import com.exegames.gamingstation.domain.SessionIds
import java.util.UUID

object UuidSessionIds : SessionIds {
    override fun newId(): String = UUID.randomUUID().toString()
}
