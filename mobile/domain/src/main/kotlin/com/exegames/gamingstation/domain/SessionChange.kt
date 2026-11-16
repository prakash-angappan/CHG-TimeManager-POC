package com.exegames.gamingstation.domain

sealed interface SessionChange {
    data class Applied(val snapshot: SessionSnapshot) : SessionChange

    data class Rejected(val reason: RejectionReason) : SessionChange
}

enum class RejectionReason(val message: String) {
    DurationMustBePositive("Duration must be greater than zero."),
    StationOccupied("A station with an open session cannot be started again."),
    NotRunning("Only a running session can expire."),
    NotYetExpired("A running session cannot expire before its expiry time."),
    NotAwaitingPayment("Payment can be marked only when the session is awaiting payment."),
    AlreadyClosed("A closed session cannot be ended again."),
}
