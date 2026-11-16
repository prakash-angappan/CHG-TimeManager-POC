package com.exegames.gamingstation.data

import androidx.room.withTransaction
import com.exegames.gamingstation.domain.DomainClock
import com.exegames.gamingstation.domain.SessionRepository
import com.exegames.gamingstation.domain.SessionSnapshot
import com.exegames.gamingstation.domain.SessionState

class RoomSessionRepository(
    private val database: GamingStationDatabase,
    private val clock: DomainClock,
) : SessionRepository {
    private val dao = database.sessionDao()

    override suspend fun save(snapshot: SessionSnapshot) {
        database.withTransaction {
            val existing = dao.get(snapshot.sessionId)
            val now = clock.now()
            val entity = snapshot.toEntity(
                createdAt = existing?.createdAt ?: now,
                updatedAt = now,
            )
            if (existing == null) {
                dao.insert(entity)
            } else {
                dao.update(entity)
            }
        }
    }

    override suspend fun update(snapshot: SessionSnapshot) {
        database.withTransaction {
            val existing = dao.get(snapshot.sessionId)
                ?: error("Cannot update session ${snapshot.sessionId} before it has been saved.")
            dao.update(
                snapshot.toEntity(
                    createdAt = existing.createdAt,
                    updatedAt = clock.now(),
                ),
            )
        }
    }

    override suspend fun close(snapshot: SessionSnapshot) {
        require(snapshot.state == SessionState.Closed) {
            "SessionRepository.close persists a session SessionStateMachine has already closed."
        }
        save(snapshot)
    }

    override suspend fun getById(sessionId: String): SessionSnapshot? = dao.get(sessionId)?.toSnapshot()

    override suspend fun getOpenSession(stationId: String): SessionSnapshot? =
        dao.getOpen(stationId)?.toSnapshot()

    override suspend fun listOpenSessions(): List<SessionSnapshot> = dao.listOpen().map { it.toSnapshot() }
}
