package com.exegames.gamingstation.domain

/**
 * Authoritative session states from the proven POC.
 * Available is the absence of an open session, not a stored state.
 * ReadyToResume is intentionally absent: payment returns the same session to [Running].
 */
enum class SessionState {
    Running,
    AwaitingPayment,
    Closed,
}
