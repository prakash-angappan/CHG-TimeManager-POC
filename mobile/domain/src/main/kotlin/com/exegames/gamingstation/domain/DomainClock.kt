package com.exegames.gamingstation.domain

import java.time.Instant

/**
 * Time source for session decisions. Production can supply the device clock later.
 * Tests supply a fixed instant so expiry does not depend on the machine clock.
 */
fun interface DomainClock {
    fun now(): Instant
}
