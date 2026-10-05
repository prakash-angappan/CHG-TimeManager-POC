package com.exegames.gamingstation.data

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import com.exegames.gamingstation.domain.DomainClock
import com.exegames.gamingstation.domain.GamingStation
import com.exegames.gamingstation.domain.SessionChange
import com.exegames.gamingstation.domain.SessionIds
import com.exegames.gamingstation.domain.SessionReconciler
import com.exegames.gamingstation.domain.SessionSnapshot
import com.exegames.gamingstation.domain.SessionState
import com.exegames.gamingstation.domain.SessionStateMachine
import java.time.Duration
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28])
class GamingStationPersistenceTest {
    private val openedAt = Instant.parse("2026-10-05T18:00:00.123456789Z")
    private val station = GamingStation(id = "station-1", name = "PS5 #01", stationCode = "PS5-01")
    private lateinit var context: Context
    private val openDatabases = mutableListOf<GamingStationDatabase>()

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
    }

    @After
    fun tearDown() {
        openDatabases.forEach { database ->
            if (database.isOpen) database.close()
        }
        openDatabases.clear()
    }

    @Test
    fun stationCanBeSavedAndLoaded() = dbTest {
        val clock = SettableClock(openedAt)
        val harness = memory(clock)
        val second = GamingStation(id = "station-2", name = "PS5 #02", stationCode = "PS5-02")

        harness.stations.save(second)
        harness.stations.save(station)

        assertEquals(station, harness.stations.get(station.id))
        assertNull(harness.stations.get("missing"))
        assertEquals(listOf(station, second), harness.stations.list())
        assertEquals(openedAt.toString(), raw(harness, "SELECT createdAt FROM gaming_stations WHERE id = ?", station.id))
        assertEquals(GAMING_STATION_SCHEMA_VERSION, harness.database.openHelper.readableDatabase.version)
    }

    @Test
    fun sessionCanBeSavedAndLoaded() = dbTest {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val harness = memory(clock)
        harness.stations.save(station)
        val started = start(machine)

        harness.sessions.save(started)

        assertEquals(started, harness.sessions.getById(started.sessionId))
        assertEquals(started, harness.sessions.getOpenSession(station.id))
        assertNull(harness.sessions.getById("missing"))
    }

    @Test
    fun instantTimestampsRoundTrip() = dbTest {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val harness = memory(clock)
        val started = start(machine)

        harness.sessions.save(started)
        val loaded = harness.sessions.getById(started.sessionId)

        assertEquals(openedAt, loaded?.startTimeUtc)
        assertEquals(openedAt.plusSeconds(120), loaded?.expiryTimeUtc)
        assertEquals(openedAt.toString(), raw(harness, "SELECT startTimeUtc FROM sessions WHERE id = ?", started.sessionId))
        assertEquals(
            openedAt.plusSeconds(120).toString(),
            raw(harness, "SELECT expiryTimeUtc FROM sessions WHERE id = ?", started.sessionId),
        )
        assertEquals("TEXT", columnType(harness, "sessions", "startTimeUtc"))
        assertEquals("TEXT", columnType(harness, "sessions", "expiryTimeUtc"))
    }

    @Test
    fun sessionRevisionRoundTrips() = dbTest {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val harness = memory(clock)
        val started = start(machine)
        harness.sessions.save(started)

        clock.instant = started.expiryTimeUtc
        val awaiting = applied(machine.expire(started))
        harness.sessions.update(awaiting)

        val loaded = harness.sessions.getById(started.sessionId)
        assertEquals(2L, loaded?.revision)
        assertEquals(awaiting, loaded)
        assertEquals("2", raw(harness, "SELECT revision FROM sessions WHERE id = ?", started.sessionId))
        assertEquals(openedAt.toString(), raw(harness, "SELECT createdAt FROM sessions WHERE id = ?", started.sessionId))
        assertEquals(
            started.expiryTimeUtc.toString(),
            raw(harness, "SELECT updatedAt FROM sessions WHERE id = ?", started.sessionId),
        )
    }

    @Test
    fun runningSessionSurvivesProcessRestart() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val started = start(machine)

        restart("restart-running", clock, machine, prepare = { harness ->
            harness.stations.save(station)
            harness.sessions.save(started)
            started
        }, verify = { harness, saved ->
            val loaded = harness.sessions.getById(saved.sessionId)
            assertEquals(saved, loaded)
            assertEquals(SessionState.Running, loaded?.state)
            assertEquals(saved, harness.sessions.getOpenSession(station.id))
        })
    }

    @Test
    fun runningSessionWithFutureExpiryStaysRunning() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val started = start(machine)

        restart("restart-future", clock, machine, prepare = { harness ->
            harness.stations.save(station)
            harness.sessions.save(started)
        }, verify = { harness, _ ->
            clock.instant = openedAt.plusSeconds(30)
            val reconciled = harness.reconciler.reconcile().openSessions.single()
            assertEquals(SessionState.Running, reconciled.state)
            assertEquals(started.sessionId, reconciled.sessionId)
            assertEquals(1L, reconciled.revision)
            assertEquals(started, harness.sessions.getById(started.sessionId))
        })
    }

    @Test
    fun runningSessionPastExpiryBecomesAwaitingPaymentOnReconcile() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val started = start(machine)

        restart("restart-expired", clock, machine, prepare = { harness ->
            harness.stations.save(station)
            harness.sessions.save(started)
        }, verify = { harness, _ ->
            clock.instant = started.expiryTimeUtc
            val reconciled = harness.reconciler.reconcile()
            val open = reconciled.openSessions.single()

            assertEquals(listOf(station), reconciled.stations)
            assertEquals(SessionState.AwaitingPayment, open.state)
            assertEquals(started.sessionId, open.sessionId)
            assertEquals(started.stationId, open.stationId)
            assertEquals(started.startTimeUtc, open.startTimeUtc)
            assertEquals(started.expiryTimeUtc, open.expiryTimeUtc)
            assertEquals(started.durationSeconds, open.durationSeconds)
            assertEquals(2L, open.revision)
            assertEquals(open, harness.sessions.getById(started.sessionId))
            assertEquals(open, harness.sessions.getOpenSession(station.id))
        })
    }

    @Test
    fun awaitingPaymentSurvivesRestart() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val awaiting = awaiting(machine, clock)

        restart("restart-awaiting", clock, machine, prepare = { harness ->
            harness.stations.save(station)
            harness.sessions.save(awaiting)
        }, verify = { harness, _ ->
            clock.instant = openedAt.plusSeconds(500)
            val reconciled = harness.reconciler.reconcile().openSessions.single()
            assertEquals(awaiting, reconciled)
            assertEquals(SessionState.AwaitingPayment, harness.sessions.getById(awaiting.sessionId)?.state)
            assertEquals(2L, harness.sessions.getById(awaiting.sessionId)?.revision)
        })
    }

    @Test
    fun closedSessionSurvivesRestartButIsNotOpen() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val started = start(machine)
        val closed = applied(machine.end(started))

        restart("restart-closed", clock, machine, prepare = { harness ->
            harness.stations.save(station)
            harness.sessions.save(started)
            try {
                harness.sessions.close(started)
                throw AssertionError("close must persist a snapshot the state machine has already closed")
            } catch (_: IllegalArgumentException) {
                assertEquals(started, harness.sessions.getById(started.sessionId))
            }
            harness.sessions.close(closed)
        }, verify = { harness, _ ->
            assertEquals(closed, harness.sessions.getById(closed.sessionId))
            assertNull(harness.sessions.getOpenSession(station.id))
            assertTrue(harness.sessions.listOpenSessions().isEmpty())
            assertTrue(harness.reconciler.reconcile().openSessions.isEmpty())
            assertEquals(SessionState.Closed.name, raw(harness, "SELECT state FROM sessions WHERE id = ?", closed.sessionId))
        })
    }

    @Test
    fun sessionIdIsPreservedAfterReload() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock, "session-kept")
        val started = start(machine)

        restart("restart-same-id", clock, machine, prepare = { harness ->
            harness.sessions.save(started)
            started.sessionId
        }, verify = { harness, sessionId ->
            assertEquals("session-kept", sessionId)
            assertEquals(sessionId, harness.sessions.getById(sessionId)?.sessionId)
            assertEquals(started, harness.sessions.getById(sessionId))
        })
    }

    @Test
    fun countdownIsNotPersisted() = dbTest {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val harness = memory(clock)
        val started = start(machine)
        harness.sessions.save(started)

        val columns = columnNames(harness, "sessions")
        assertEquals(
            listOf(
                "id",
                "stationId",
                "stationCode",
                "stationName",
                "state",
                "startTimeUtc",
                "expiryTimeUtc",
                "durationSeconds",
                "revision",
                "createdAt",
                "updatedAt",
            ),
            columns,
        )
        assertFalse(columns.any { it.contains("remaining", ignoreCase = true) || it.contains("countdown", ignoreCase = true) })

        val expiryBefore = raw(harness, "SELECT expiryTimeUtc FROM sessions WHERE id = ?", started.sessionId)
        val loaded = harness.sessions.getById(started.sessionId)!!
        assertEquals(Duration.ofSeconds(90), loaded.remainingTime(openedAt.plusSeconds(30)))
        assertEquals(Duration.ZERO, loaded.remainingTime(openedAt.plusSeconds(500)))
        assertEquals(expiryBefore, raw(harness, "SELECT expiryTimeUtc FROM sessions WHERE id = ?", started.sessionId))
        assertEquals(started, harness.sessions.getById(started.sessionId))
    }

    @Test
    fun reconciliationIsIdempotent() {
        val clock = SettableClock(openedAt)
        val machine = machine(clock)
        val started = start(machine)

        restart("restart-idempotent", clock, machine, prepare = { harness ->
            harness.stations.save(station)
            harness.sessions.save(started)
        }, verify = { harness, _ ->
            clock.instant = started.expiryTimeUtc.plusSeconds(30)
            val first = harness.reconciler.reconcile().openSessions.single()
            val second = harness.reconciler.reconcile().openSessions.single()
            assertEquals(SessionState.AwaitingPayment, first.state)
            assertEquals(2L, first.revision)
            assertEquals(first, second)

            harness.database.close()
            val reopened = file("restart-idempotent", clock, machine)
            val third = reopened.reconciler.reconcile().openSessions.single()
            assertEquals(first, third)
            assertEquals(2L, reopened.sessions.getById(started.sessionId)?.revision)
        })
    }

    private fun machine(clock: SettableClock, vararg ids: String): SessionStateMachine {
        val sequence = if (ids.isEmpty()) arrayOf("session-1") else ids
        return SessionStateMachine(clock, SequenceIds(*sequence))
    }

    private fun start(machine: SessionStateMachine): SessionSnapshot =
        applied(machine.start(station, current = null, durationSeconds = 120))

    private fun awaiting(machine: SessionStateMachine, clock: SettableClock): SessionSnapshot {
        val started = start(machine)
        clock.instant = started.expiryTimeUtc
        return applied(machine.expire(started))
    }

    private fun memory(clock: SettableClock): Harness = open(name = null, clock = clock, machine = machine(clock))

    private fun file(name: String, clock: SettableClock, machine: SessionStateMachine): Harness =
        open(name = name, clock = clock, machine = machine)

    private fun open(name: String?, clock: SettableClock, machine: SessionStateMachine): Harness {
        val builder = if (name == null) {
            Room.inMemoryDatabaseBuilder(context, GamingStationDatabase::class.java)
        } else {
            Room.databaseBuilder(context, GamingStationDatabase::class.java, name)
        }
        val database = builder.allowMainThreadQueries().build()
        openDatabases += database
        val sessions = RoomSessionRepository(database, clock)
        return Harness(
            database = database,
            stations = RoomGamingStationRepository(database, clock),
            sessions = sessions,
            reconciler = SessionReconciler(
                stations = RoomGamingStationRepository(database, clock),
                sessions = sessions,
                machine = machine,
            ),
        )
    }

    private fun <T> restart(
        name: String,
        clock: SettableClock,
        machine: SessionStateMachine,
        prepare: suspend (Harness) -> T,
        verify: suspend (Harness, T) -> Unit,
    ) {
        dbTest {
            context.deleteDatabase(name)
            val first = file(name, clock, machine)
            val prepared = prepare(first)
            first.database.close()
            val second = file(name, clock, machine)
            verify(second, prepared)
        }
    }

    private fun raw(harness: Harness, sql: String, id: String): String {
        harness.database.openHelper.readableDatabase.query(sql, arrayOf(id)).use { cursor ->
            check(cursor.moveToFirst()) { sql }
            return cursor.getString(0)
        }
    }

    private fun columnNames(harness: Harness, table: String): List<String> {
        val names = mutableListOf<String>()
        harness.database.openHelper.readableDatabase.query("PRAGMA table_info(`$table`)").use { cursor ->
            val index = cursor.getColumnIndexOrThrow("name")
            while (cursor.moveToNext()) {
                names += cursor.getString(index)
            }
        }
        return names
    }

    private fun columnType(harness: Harness, table: String, column: String): String {
        harness.database.openHelper.readableDatabase.query("PRAGMA table_info(`$table`)").use { cursor ->
            val name = cursor.getColumnIndexOrThrow("name")
            val type = cursor.getColumnIndexOrThrow("type")
            while (cursor.moveToNext()) {
                if (cursor.getString(name) == column) return cursor.getString(type)
            }
        }
        error("Missing column $table.$column")
    }

    private fun dbTest(block: suspend () -> Unit) {
        runBlocking(Dispatchers.IO) { block() }
    }

    private fun applied(change: SessionChange): SessionSnapshot {
        check(change is SessionChange.Applied)
        return change.snapshot
    }
}

private class Harness(
    val database: GamingStationDatabase,
    val stations: RoomGamingStationRepository,
    val sessions: RoomSessionRepository,
    val reconciler: SessionReconciler,
)

private class SettableClock(var instant: Instant) : DomainClock {
    override fun now(): Instant = instant
}

private class SequenceIds(private val values: Iterator<String>) : SessionIds {
    constructor(vararg values: String) : this(values.iterator())

    override fun newId(): String = values.next()
}
