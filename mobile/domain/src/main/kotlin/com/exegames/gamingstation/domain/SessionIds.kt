package com.exegames.gamingstation.domain

/** Creates session identifiers. Injected so tests can use stable ids. */
fun interface SessionIds {
    fun newId(): String
}
