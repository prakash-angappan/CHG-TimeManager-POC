package com.exegames.gamingstation

import java.time.Instant
import com.exegames.gamingstation.domain.DomainClock

/** Wall clock for the running app. Tests inject a [DomainClock] of their own. */
object SystemDomainClock : DomainClock {
    override fun now(): Instant = Instant.now()
}
