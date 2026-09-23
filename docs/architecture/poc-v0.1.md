# Gaming Station Session Manager — POC v0.1 Architecture

Status: decisions below are accepted. Step 1 (LAN `GET /api/health`) is the only implementation authorized so far. Do not start Step 2 until it is requested.

Accepted on 2026-09-23:

- The v0.1 TV client is a fullscreen browser at `/display/ps5-01`. Do not build a native Kotlin Android TV application in v0.1.
- That browser page is only the POC client. The backend contract stays platform-independent so later clients can be native Android TV, Samsung Tizen, LG webOS, another smart-TV browser, or an external Android box, mini-PC, or device agent.
- End Session is in v0.1. Staff can end a session while it is running, awaiting payment, or ready to resume. The station then shows AVAILABLE.
- Start grants 120 seconds. Resume grants a new 120-second period. Both durations come from server configuration `Session:DefaultDurationSeconds`.
- The TV has no separate PAID screen. After payment is marked, the TV stays on TIME EXPIRED until resume. The backend remains authoritative for state and expiry.
- Step 1 configures the listen URL and the session duration only. The data directory arrives with SQLite in Step 2.

This document is the blueprint for POC v0.1. It records requirements, the recommended shape of the system, the alternatives considered, and which decisions will be expensive to change later. It does not contain implementation code.

The product language below is normative for v0.1. Where a later stage needs a different shape, the stage is named.

---

## 1. Requirements

### 1.1 What v0.1 must prove

One Windows PC on the café LAN runs the backend. A staff phone and an Android TV, both on that LAN, stay in sync with one gaming station while a short session runs, expires, is marked paid, and is resumed.

The scripted flow:

1. The backend is the only writer of session state.
2. SQLite stores the station and its sessions.
3. Staff opens a mobile web dashboard.
4. The Android TV shows the same station.
5. Station `PS5 #01` exists.
6. Staff starts a session.
7. The TV changes from **AVAILABLE** to **GAME RUNNING**.
8. A server-owned timer of about 2 minutes starts.
9. The TV shows remaining time.
10. Expiry is decided by the backend from stored timestamps.
11. On expiry the TV shows **TIME EXPIRED**.
12. The dashboard receives that state without a manual refresh.
13. Staff sees **MARK PAYMENT DONE**.
14. Payment is a simulation. There is no gateway, amount, or receipt.
15. After payment, staff sees **RESUME SESSION**.
16. Resume puts the TV back on **GAME RUNNING** with a new server-owned window.
17. Mobile and TV receive state changes as they are committed.
18. A client may disconnect and reconnect without creating, extending, or destroying the session.

### 1.2 Functional requirements — POC

| Id | Requirement | Notes |
|---|---|---|
| F1 | One seeded station, `ps5-01` / `PS5 #01` | The model is a list of stations. The POC seeds one. |
| F2 | Staff can start a session for a station that has no open session | Duration comes from server configuration, default 120 seconds. |
| F3 | TV shows AVAILABLE, GAME RUNNING, or TIME EXPIRED | The server decides which of these three applies. |
| F4 | Dashboard shows the matching staff action | Start, Mark payment done, or Resume. |
| F5 | Payment simulation advances the session | No money, no gateway. |
| F6 | Resume returns the station to a running window | New `startedAtUtc` / `expiresAtUtc`. Previous window is not extended. |
| F7 | Staff can end an open session and return the station to AVAILABLE | Confirmed. End is valid while the session is running, awaiting payment, or ready to resume. |
| F8 | Real-time update on staff and display clients | After each committed transition. |
| F9 | Reconnect and browser refresh show current server state | Including remaining time. |
| F10 | The backend does not control the console | The TV is a status display. Staff operate the PS5 itself. |

### 1.3 Functional requirements — explicitly deferred

Customer bookings, customer accounts, price lists, real payments, refunds, receipts, session extensions chosen by staff, pause, loyalty, reports, revenue, device monitoring, console power/HDMI control, staff accounts, multi-branch, cloud hosting, native apps for Tizen/webOS, and a device-pairing workflow.

### 1.4 Non-functional requirements — POC

| Id | Requirement |
|---|---|
| N1 | One backend process on one Windows PC. No cloud dependency at runtime. |
| N2 | Staff and display clients talk only to that PC over the local network. |
| N3 | A committed transition reaches a connected client in well under a second on a healthy LAN. |
| N4 | The system stays correct with a handful of staff browsers and one display on the station. The data model must not assume "there is only one station" in code. |
| N5 | The public contract is HTTP JSON plus a small SignalR event. Any later TV platform can implement that contract. |
| N6 | Setup for the POC is a running ASP.NET process and a browser. No separate database server. |

### 1.5 Reliability requirements — POC

| Id | Requirement |
|---|---|
| R1 | An open session survives display disconnect, staff refresh, and a backend process restart. |
| R2 | Expiry is a consequence of `expiresAtUtc`, not of a client staying connected. |
| R3 | While the process is up, a due session moves to the expired state within about one second, and connected clients are notified. |
| R4 | After a restart, due sessions are reconciled immediately, before serving normal traffic is required for correctness. A client that reconnects also forces reconciliation. |
| R5 | Two payment clicks do not corrupt the session. Two resume clicks do not grant extra time. Two start clicks do not create two open sessions. |
| R6 | The TV must not invent a committed TIME EXPIRED state while it cannot reach the server. |

Production concerns that are out of scope: high availability, automatic failover, off-site backup, monitoring alerts, and multi-instance consistency.

### 1.6 Network requirements — POC

- The PC, the staff phone, and the TV are on one LAN.
- The PC must be reachable from Wi-Fi clients. A typical failure is an access point with client isolation enabled: phones can reach the internet and cannot reach each other or the PC.
- The backend listens on the LAN interface, not only on localhost.
- Prefer Ethernet from the PC to the router. Wi-Fi for the server adds sleep, roaming, and signal problems to the one machine that owns session truth.
- Give the PC a DHCP reservation so display URLs stay stable.
- The POC uses HTTP on the LAN. HTTPS is deferred.
- Runtime does not require internet access. Package restore happens at build time.
- Bandwidth is negligible if clients are notified on transitions only. The design must not send a countdown tick every second.

### 1.7 Security considerations — POC versus later

POC trust boundary: the LAN itself. There is no login.

Anyone who can reach the PC can start a session, mark payment, and resume it. That is acceptable for a supervised demo and unacceptable for a café with guest Wi-Fi.

POC mitigations:

- Use a private SSID or VLAN for staff devices, the TV, and the server. Do not put this POC on guest Wi-Fi.
- Do not port-forward the PC to the internet.
- Do not put a shared API key in the TV URL. It will leak into logs and screenshots and will be mistaken for real security.

Before any real café use: staff authentication, authorization on every command, HTTPS, and a decision about whether display clients are allowed to do anything except join and read. The v0.1 split (commands on HTTP, reads as snapshots, one application service that performs transitions) is the place those checks will go. v0.1 does not add empty auth scaffolding.

### 1.8 Scope boundary that must stay visible

v0.1 does not turn the console on or off, switch HDMI, or know whether someone is holding a controller. "GAME RUNNING" means "this station's paid-time window is open," not "the PS5 process is running." Console control, if it ever exists, is a later adapter behind the station. It is not part of the session state machine.

---

## 2. High-level architecture

### 2.1 Recommended shape

One ASP.NET Core process on the Windows PC is the source of truth. It exposes a small HTTP API for commands and queries, a SignalR hub for notifications, and the static display page. SQLite, accessed only through EF Core, is the durable store. A single in-process sweeper reconciles expiry.

Staff use a React dashboard. The TV uses a fullscreen browser page served by the same process. Android TV is one way to host that page.

```text
                         WINDOWS PC  (Ethernet to router)
        ┌──────────────────────────────────────────────────────┐
        │                  ASP.NET Core process                │
        │                                                      │
        │   Staff SPA (built)     Display page /display/{code} │
        │                                                      │
        │   REST API  ◄── commands & snapshot queries          │
        │      │                                               │
        │      ▼                                               │
        │   Session coordinator                                │
        │      │  transitions, expiry reconcile, concurrency   │
        │      ├──────────────► SQLite (EF Core, WAL)          │
        │      │                                               │
        │      └──────────────► SignalR hub                    │
        │                         groups: station:{code}, staff│
        └──────────────┬───────────────────────┬───────────────┘
                       │ LAN                   │
                       ▼                       ▼
               Staff phone browser      Android TV browser
               React dashboard          Display page
```

There is no separate session-manager process, message bus, or database server in v0.1.

### 2.2 Responsibilities

**Backend.** Owns validation, the state machine, clock reads, persistence, and notification after a successful commit. The only component allowed to change a session.

**Session coordinator.** The application service inside the backend that performs every transition (start, expire, payment, resume, end) and the lazy expiry check. The HTTP API, the startup pass, and the sweeper all call it. UI code and the SignalR hub do not implement transitions of their own.

**Database.** Durable stations and sessions. Survives process restart. Not accessed over the network by clients.

**REST API.** The command and query path. Starting a session, marking payment, resuming, and ending are HTTP POST requests. Current state is always available from GET, with or without a live socket. The HTTP response body is the new snapshot, so the staff member who clicked does not depend on SignalR to see their own action.

**SignalR.** A notification channel after commit. Clients join a group and receive a full station snapshot per change. The hub is not a second command API. A client that never connects to SignalR still works if it refreshes or polls; it is merely slower. That property is what keeps later TV platforms from being blocked on a SignalR client library.

**Staff dashboard.** Renders one station list (one row in the POC), shows the server snapshot, ticks a local countdown between snapshots, and sends the staff commands. It does not decide expiry.

**TV / display client.** Renders the server `displayMode` in large type and ticks the same way. It never POSTs a business command in v0.1. It refetches when its local countdown reaches zero so a missed socket message still converges.

**Sweeper.** An `IHostedService` on a one-second loop. Finds running sessions whose `expiresAtUtc` has passed and asks the coordinator to expire them. This exists so expiry is pushed even when nobody is polling.

### 2.3 Main data flow

Commands:

```text
Staff action
    → POST
    → coordinator validates the transition
    → SQLite commit
    → snapshot published to SignalR
    → HTTP response returns the same snapshot
    → TV and other staff browsers apply it
```

Expiry when nobody clicks:

```text
Sweeper or a client GET
    → coordinator sees Running && now >= expiresAtUtc
    → commit AwaitingPayment
    → snapshot published
    → TV shows TIME EXPIRED
    → dashboard shows MARK PAYMENT DONE
```

Reconnect:

```text
Client reconnects
    → joins its SignalR group again
    → GET snapshot
    → renders from server timestamps
```

### 2.4 Repository shape for the POC

Keep a single API project with folders for data, sessions, and the hub. Do not split Clean Architecture projects, and do not introduce a generic plugin model.

```text
/src/GamingStation.Api/          ASP.NET Core: API, hub, display page, EF
/src/gaming-station-staff/       Vite + React + TypeScript
/docs/architecture/poc-v0.1.md
```

The display page lives with the API and is served by Kestrel so the TV has one origin and no frontend build. The staff app is separate because that UI will grow. For the packaged demo, the built staff app is hosted by the same Kestrel site so the phone also uses one origin and one port.

### 2.5 Decisions that are expensive to change later

These are cheap to get right in the first implementation and costly to unwind after clients exist:

1. **Station is the resource.** URLs, groups, and sessions are addressed by station. They are not addressed by TV, Android device, or PS5.
2. **`expiresAtUtc` is the timer.** Status is a projection that the server reconciles. Clients decorate a countdown; they do not own it.
3. **Commands are HTTP. Notifications are full snapshots.** A client can always recover with GET.
4. **The server computes `displayMode`.** Hard-to-update TVs switch on that field. Staff UI switches on session `status` to choose buttons.
5. **Clients use relative URLs** (or one configured base URL) for API and hub. They do not embed a LAN IP in source.
6. **Timestamps are UTC.** The server clock is the only clock that matters.
7. **Business rules stay in the coordinator,** not in controllers, the hub, or React.
8. **The code paths are multi-station** even though the seed data has one row: list endpoints, per-station groups, no global `CurrentSession` singleton.
9. **EF Core is the data access path.** No SQLite-only SQL in business logic, so a later provider change is a hosting change.
10. **JSON contracts are additive.** New fields are allowed. Renaming `displayMode` or status strings breaks TVs that are awkward to redeploy.

---

## 3. Comparison of alternatives

### 3.1 Real-time communication

#### SignalR

- **How it works.** ASP.NET hosts a hub. The official JavaScript client opens a WebSocket (and can fall back to other transports). The server adds connections to groups and pushes a snapshot after each commit.
- **Advantages.** Ships with ASP.NET Core. Groups map cleanly onto stations. The JS client handles reconnect. A later multi-instance deployment can add a Redis backplane or Azure SignalR without changing the hub's public events.
- **Disadvantages.** Native Android, Tizen, and webOS do not get a client as mature as the JS client. The protocol is not useful to a device that can only do occasional HTTP.
- **Complexity.** Low for browser clients. Medium if every future TV must speak SignalR natively.
- **POC suitability.** High for the staff dashboard and a browser-based display.
- **Future scalability.** Sufficient for 10–50 stations of transition-only messages on one server. Scale-out needs a backplane once more than one API process is running.
- **Caveat.** Groups are dropped when the connection id changes. After automatic reconnect the client must join again and GET a snapshot. SignalR does not, by itself, repair missed business state.

#### Raw WebSockets

- **How it works.** Kestrel accepts WebSocket connections and a custom protocol carries session messages.
- **Advantages.** No SignalR dependency. Any language with a socket can participate.
- **Disadvantages.** Reconnect, heartbeats, grouping, and message framing become project code. That code is a worse, smaller SignalR.
- **Complexity.** High relative to the POC.
- **POC suitability.** Poor. It spends the POC budget on transport.
- **Future scalability.** Possible, and still something the team would have to operate.
- **Caveat.** A custom protocol becomes a compatibility surface for every TV app. SignalR already is a protocol; inventing another one does not make Tizen easier.

#### HTTP polling

- **How it works.** Each client calls `GET /api/stations/{code}` every one or two seconds and renders the result.
- **Advantages.** Works on every platform that can issue HTTP, including weak TV browsers. Easy to debug with a browser. Survives networks that interfere with long-lived sockets. At café scale, 50 displays polling once a second is a trivial load for one PC.
- **Disadvantages.** Expiry appears up to one poll interval late. More radio wakeups on a phone. Easier to accidentally make the client treat a failed poll as "session gone."
- **Complexity.** Low.
- **POC suitability.** Acceptable, and more predictable on bad Wi-Fi. Weaker fit for the "both screens move together" demo if the interval is lazy.
- **Future scalability.** Fine through a single café. Wasteful only if polls become chatty or carry large payloads. They should not.
- **Caveat.** Polling is a valid permanent read model for a device that cannot hold a socket. It must hit the same GET the interactive clients use.

#### Recommendation — real-time

Use **SignalR to push a snapshot on each committed transition**, and keep **GET as the correctness path**.

Reasoning: the POC demo needs both screens to move when staff click and when the timer fires. SignalR does that with little code in the stack already chosen. Correctness does not depend on the socket. On reconnect, after a missed message, and on any future TV that cannot run SignalR, the client GETs the station and renders it. A slow safety poll (tens of seconds) is optional later; v0.1 relies on reconnect plus an immediate refetch when the local countdown hits zero.

Do not send a SignalR tick every second. The timestamps inside the snapshot are the countdown.

### 3.2 Timer architecture

#### Client-authoritative timer

- **How it works.** The TV counts down locally and tells the server when it reaches zero. The server believes it.
- **Advantages.** Simple on a single always-on screen. The number on the TV is smooth.
- **Disadvantages.** Disconnect stops expiry. Two clients disagree. A refreshed page resets or stalls the clock. A wrong device clock changes a commercial session. The backend is no longer the source of truth.
- **Complexity.** Low to build, high to explain when it misbehaves.
- **POC suitability.** Poor. It fails requirements R1, R2, and the disconnect test.
- **Future scalability.** Unsuitable once more than one display can show the same station.
- **Caveat.** This model is the one that forces a rewrite when a second client appears.

#### Server pushes remaining seconds every second

- **How it works.** The server is authoritative and broadcasts `remainingSeconds` once a second to each station group.
- **Advantages.** Every client shows the same integer without doing clock math. Drift is corrected continuously.
- **Disadvantages.** The display depends on a steady stream. Wi-Fi jitter looks like a stuck clock. One hundred messages per station per session is unnecessary traffic. A client that misses the stream cannot render a smooth countdown from the last integer alone unless it also received an expiry timestamp.
- **Complexity.** Medium. Requires a scheduler and a "missed tick" policy.
- **POC suitability.** Works, and it is more machinery than the demo needs.
- **Future scalability.** Acceptable at 50 stations (50 small messages per second) but it makes every weak client depend on socket health for a number that is already determined.
- **Caveat.** It teaches clients the wrong contract: "I only know the time if the stream is alive."

#### Hybrid: server timestamps, client countdown

- **How it works.** The server stores `startedAtUtc` and `expiresAtUtc` and sends them with `serverTimeUtc` on every snapshot. The client computes an offset between its clock and the server clock, then animates remaining time locally. The server alone commits the transition to expired. When the animation reaches zero, the client refetches. It changes the words on screen only when the snapshot says so.
- **Advantages.** Matches the source-of-truth rule. Disconnect does not pause the session. Reconnect is a GET. Any number of clients derive the same deadline. Socket traffic stays on the rare transitions. A two-minute POC and a sixty-minute café session use the same mechanism.
- **Disadvantages.** Clients must implement a small clock-offset calculation. A wildly jumping client clock during the session could skew the animation until the next snapshot. That is a display glitch, not a state bug, because expiry is still server-side.
- **Complexity.** Low on the server. Low-to-medium on the client (a few dozen lines, easy to get subtly wrong; specify the formula once and share it).
- **POC suitability.** High.
- **Future scalability.** This is the model to keep through later stages.
- **Caveat.** The client clock is never used to decide status. `serverTimeUtc` is captured with the snapshot and used only to remove skew from the animation.

#### Recommendation — timer

Use the **hybrid**. Persist `startedAtUtc` and `expiresAtUtc` from the server clock. Animate on the client. Commit expiry only in the coordinator.

Client formula, applied on each snapshot:

```text
offsetMs     = serverTimeUtc - clientNowAtReceipt
remainingMs  = expiresAtUtc - (clientNow + offsetMs)
```

Tick `remainingMs` locally for display. At `remainingMs <= 0`, refetch. Replace the on-screen status only from the returned `displayMode`.

### 3.3 Database

#### SQLite

- **How it works.** One database file on the PC. EF Core uses the SQLite provider. The API process is the only writer. WAL mode allows reads during a write.
- **Advantages.** No database service to install. Backup is a file copy. EF Core support is mature. One process and 50 stations are far below its limits. Fits a machine that may have no Docker and no SQL Server instance.
- **Disadvantages.** One writer family: do not point two API processes at the same file. Weaker operational tooling than a server database. Easy to accidentally store the file under `bin/` and delete it on every rebuild. Date comparisons depend on a consistent UTC representation.
- **Complexity.** Low.
- **POC suitability.** High.
- **Future scalability.** Enough for a single café on one process, including 10–50 stations and session history, provided the file lives on local disk. It is the wrong store once two API servers must share writes, or once a multi-branch control plane needs remote access to the live database.
- **Caveat.** Treat the provider as replaceable. Business code should not contain SQLite SQL. Enable WAL. Put the file in a configured data directory outside the build output.

#### PostgreSQL

- **How it works.** A database server, local or later in the cloud. EF Core uses the Npgsql provider.
- **Advantages.** Real concurrency, remote access, backups, roles, and a straight path to a hosted cloud database. The likely Stage 2/3 engine if the team is not committed to SQL Server.
- **Disadvantages.** Another Windows service to install, patch, and start before the first demo. More failure modes for a one-station POC. Encourages people to connect ad-hoc tools to production data before there is a production.
- **Complexity.** Medium for the POC, mostly operational.
- **POC suitability.** Poor fit for the first milestone. Good fit for the stage that needs backups, reporting, or more than one API process.
- **Future scalability.** Suitable through multi-branch, with one database per branch or a shared cloud database depending on the Stage 3 topology in section 11.
- **Caveat.** Starting here does not remove the need for the session rules above. It only changes hosting.

#### SQL Server

- **How it works.** SQL Server Express or LocalDB on the Windows PC. EF Core uses the SQL Server provider.
- **Advantages.** Natural on Windows. Strong tooling (SSMS). EF Core's SQL Server provider is the most traveled path. Express is free within its limits, which a café will not hit soon.
- **Disadvantages.** Heavier install than SQLite. LocalDB is awkward as a always-on café service. The later cloud path is Azure SQL or paid SQL Server, which is a narrower choice than PostgreSQL. Licensing questions appear as soon as the café is a real business.
- **Complexity.** Medium.
- **POC suitability.** Acceptable and unnecessary.
- **Future scalability.** Fine for a Microsoft-centric single café. Less attractive for a portable cloud deployment.
- **Caveat.** Choosing SQL Server now is still reversible with EF Core, and it is still extra surface area before the flow is proven.

#### Recommendation — database

Use **SQLite + EF Core** for v0.1.

Reasoning: the POC has one writer, one machine, and no reporting load. SQLite removes install work that has nothing to do with the session flow. The expensive mistake is coupling rules to SQLite dialect or to local time, not the choice of file database. Move to PostgreSQL when a second API instance, remote reporting, or managed backups become real. That move is a provider and connection-string change if the rules above are respected. It is a rewrite if they are not.

Startup may apply EF migrations for this single-process POC. Revisit that before running more than one instance.

### 3.4 TV client

#### Native Android / Kotlin

- **How it works.** An Android TV application renders the three states, opens SignalR with the Java client (or polls), and is sideloaded or installed from a store.
- **Advantages.** Real kiosk behavior: start on boot, keep the screen on, hide system UI. Matches the initial technology proposal. Can later talk to Android-only device APIs.
- **Disadvantages.** The UI exists only on Android. Samsung, LG, a browser on a PC, and a Raspberry Pi do not run it. Iteration is a build-and-sideload loop. The SignalR Java client is a second client to keep compatible with the hub. Business logic tends to leak into Kotlin "just for the demo."
- **Complexity.** Medium to high for a three-screen POC.
- **POC suitability.** Viable, and it spends the milestone on a platform shell rather than on the session contract.
- **Future scalability.** One client among several. It does not become the café-wide UI strategy.
- **Caveat.** A native UI that computes its own status from raw timestamps, or that calls ad-hoc endpoints, will diverge from the web dashboard the first time a state is added.

#### Unity

- **How it works.** A Unity project draws the countdown and is exported to Android TV, and theoretically to other devices.
- **Advantages.** One rendering codebase if the display later becomes a heavily animated brand experience. Familiar if the team is a game team.
- **Disadvantages.** A large runtime for three lines of text. TV export targets are uneven. Build and debugging cost dominates the POC. Input, focus, and overscan become Unity problems.
- **Complexity.** High.
- **POC suitability.** Poor.
- **Future scalability.** Only worth revisiting if the display becomes a product surface that must match a game-quality scene on every device. That is not the café session product.
- **Caveat.** Unity does not remove the need for the HTTP contract. It only changes the renderer.

#### Web display, opened fullscreen on the TV

- **How it works.** Kestrel serves `/display/ps5-01`. The page uses the JS SignalR client and the station GET. Android TV opens that URL in its browser. The same URL works in Chrome on a PC, a phone standing in for the TV, a Pi running Chromium, and the browsers on current Tizen and webOS sets.
- **Advantages.** One display implementation for every platform that has a browser. The official JavaScript SignalR client is the maintained one. Instant reload during development. Relative URLs mean the page follows the host: LAN PC now, another host later. A non-smart TV is served by plugging in any small device that can run a browser. The backend stays unaware of the panel brand.
- **Disadvantages.** Kiosk details move to the device: hide the browser chrome, start on boot, stop the screen from sleeping. Old TV browsers can be outdated. A pure browser demo is easier to "exit" than a native kiosk app.
- **Complexity.** Low for the product UI. Low-to-medium for a polished unattended kiosk.
- **POC suitability.** Highest. A second phone can play the TV role before the physical Android TV is configured.
- **Future scalability.** This is the display strategy that survives mixed TV hardware. Native shells, where a platform needs them, are wrappers around the URL.
- **Caveat.** Keep the page plain: small script, large type, no framework that assumes a current desktop browser. The v0.1 capability target is WebSocket plus everyday JavaScript. That covers Android TV and current smart-TV browsers. A set that cannot do that gets an external browser device, not a custom backend.

#### Thin native shell around the web display

- **How it works.** A small Kotlin (or other) app is a fullscreen WebView locked to `/display/{code}`, with cleartext allowed for the POC, screen-on, and start-on-boot. All session UI remains the web page.
- **Advantages.** Android TV can look like an appliance without a second UI implementation. The same pattern (a shell that loads a URL) is how other platforms get kiosk behavior later.
- **Disadvantages.** Still an Android build, sideload, and cleartext configuration. Easy to overgrow into a second client if someone starts rendering status in Kotlin.
- **Complexity.** Low once the page exists. Should not block the first end-to-end proof.
- **POC suitability.** Out of v0.1. The accepted decision is the Android TV browser only. Revisit a shell only after the POC flow is proven and a set cannot stay on that page.
- **Future scalability.** Good. The shell has no domain knowledge.
- **Caveat.** The WebView must load the server page. It must not bundle a forked copy of the UI that can drift.

#### Recommendation — TV client

**Accepted.** v0.1 uses a fullscreen browser at `/display/ps5-01`. No native Kotlin application and no WebView shell are part of this POC.

The page is a client of the station API, the same way a later Tizen, webOS, Android, or mini-PC client will be. Serving that page from Kestrel must not make the API web-specific: no user-agent branches, no Android-only fields, and no session rules that exist only so the browser demo works. A platform that cannot run the page still uses `GET /api/stations/{code}` and, if it can, the same SignalR snapshot.

Reasoning: the milestone is session truth and two live clients, not an Android UI toolkit. A native renderer remains a valid later client of the same contract. Building it now would spend the POC on one panel and would tempt the backend to grow around that client.

### 3.5 Staff dashboard

#### React + TypeScript

- **How it works.** A Vite app renders station state, posts commands, and subscribes with `@microsoft/signalr`. It is developed against the API and, for the demo, built and hosted by Kestrel.
- **Advantages.** Fits a UI that will grow into bookings, lists, and reports. Strong mobile-browser support. PWA install can wait until staff want a home-screen icon. Cursor and similar tools implement this stack reliably. TypeScript keeps the snapshot fields honest as the contract grows.
- **Disadvantages.** A second toolchain (Node, Vite) beside .NET. Dev mode needs a proxy or CORS so the phone can talk to the API. Easy to over-build with a global state library.
- **Complexity.** Low if the POC stays on `fetch`, component state, and the SignalR subscription. Medium if a large frontend platform is introduced early.
- **POC suitability.** High.
- **Future scalability.** The right staff UI through a single café and beyond.
- **Caveat.** Do not introduce Redux or a shared cross-app component library in v0.1. Server state lives in the snapshot. The acting user's POST response is applied immediately; SignalR updates everyone, including the actor, and stale versions are ignored.

#### Blazor

- **How it works.** Either Blazor Server (UI events round-trip over a SignalR circuit) or Blazor WebAssembly (C# runs in the browser).
- **Advantages.** One language with the API. Blazor Server is productive for an internal admin screen on a stable network. Shared C# contracts are natural.
- **Disadvantages.** Blazor Server's circuit is a poor match for a phone on café Wi-Fi: walk out of range and the UI session drops. Blazor WebAssembly is a heavier client and a weaker fit for a small mobile UI. The staff product's ecosystem (mobile layout, later PWA) is more direct in React.
- **Complexity.** Low for a happy-path Blazor Server page. Medium once mobile reconnect behavior has to feel solid.
- **POC suitability.** Acceptable on a laptop tethered to the PC. Weaker for the stated mobile-browser dashboard.
- **Future scalability.** Fine for a back-office screen. Not the recommended staff handheld.
- **Caveat.** Choosing Blazor Server would make the staff UI depend on a second SignalR lifetime (the circuit) in addition to business notifications.

#### Plain HTML and JavaScript

- **How it works.** One page in `wwwroot`, no Node build, talks to the same API and hub.
- **Advantages.** Fastest path to a clickable demo. No frontend toolchain. Same operational model as the display page.
- **Disadvantages.** A single file becomes hard to change once the dashboard has a station list, connection state, and several actions. The staff UI is the surface that will grow first.
- **Complexity.** Low now, rising quickly.
- **POC suitability.** Enough for one button column. A rewrite is likely before Stage 2.
- **Future scalability.** Poor as the café UI.
- **Caveat.** Reasonable for the display page, where the UI is three states and must stay light. A poor default for staff.

#### Recommendation — staff dashboard

Use **React + TypeScript** for staff, and keep the display page as plain HTML/JS.

Reasoning: the two UIs have different lifespans. The display must stay simple enough for TV browsers and may be wrapped by native shells. The staff UI is a normal web app that will accumulate workflows. React matches that growth. Hosting the production build on the same Kestrel origin avoids a permanent CORS setup on the café PC.

---

## 4. Domain model

### 4.1 Verdict on the central concept

**The gaming station is the central business concept.** Staff start time on a station. A session belongs to a station. A display is a client watching a station. A console is equipment at a station, and v0.1 does not manage it.

A TV-centric model fails as soon as the panel is swapped, a Pi is plugged into a dumb TV, or a station is a gaming PC with a monitor. A console-centric model fails for the same reason and also couples the product to PS5.

The useful abstraction in v0.1 is the **boundary**: every API, group, and session is station-scoped. The premature abstraction is a schema of Console, Display, and Device tables with nothing to put in them.

```text
v0.1 (build this)

    GamingStation
        code, name, consoleLabel
        └── zero or one open Session

v0.1 clients (not tables)

    a browser at /display/{code}
    a staff browser watching the station list

Later, only when a feature needs them

    GamingStation
        ├── Console          when the system controls or inventories hardware
        ├── Display          when a station has more than one managed panel
        └── Device           when a physical client must be provisioned or monitored
```

`consoleLabel` is a string attribute (`"PS5"`, later `"Xbox"`, `"PC"`). It is display data. There is no console type hierarchy.

### 4.2 Entities required in v0.1

**GamingStation**

| Field | Purpose |
|---|---|
| `Id` (Guid) | Internal identity. Stable if the database is ever merged or rebuilt carefully. |
| `Code` | Public identity. `ps5-01`. Used in URLs and SignalR groups. Unique. Immutable in practice because TVs will bookmark it. |
| `Name` | `PS5 #01`. Can be renamed without breaking clients. |
| `ConsoleLabel` | `PS5`. Free text. |
| `StateVersion` | Monotonic integer. Incremented on every committed station change. Clients drop snapshots older than the version they have. |
| `IsActive` | Allows a later "hide this station" without a new concept. POC value is true. |

**Session**

| Field | Purpose |
|---|---|
| `Id` (Guid) | Identity used by payment, resume, and end. |
| `StationId` | Owner. |
| `Status` | `running`, `awaitingPayment`, `readyToResume`, `closed`. Stored as a string. |
| `StartedAtUtc` | Server time when the current running window began. |
| `ExpiresAtUtc` | Server time when the current window ends. The timer. |
| `DurationSeconds` | The length that was granted for the current window. Copied from configuration at start and at resume. |
| `PaidAtUtc` | Set when staff mark payment for the current expired window. Cleared conceptually by moving to a new running window (set null on resume). |
| `Version` | Optimistic concurrency token. |
| `CreatedAtUtc`, `UpdatedAtUtc` | Diagnostics and ordering. |

Invariant: at most one session per station whose status is not `closed`. Enforce with a unique filtered index. Closed rows remain so the table is already a history of visits, even though v0.1 has no report.

**Not stored on the station:** a `CurrentSessionId` column. The open session is found by query. Two writable copies of "which session is current" will drift.

### 4.3 What is deliberately not an entity

| Concept | v0.1 representation | Add a table when |
|---|---|---|
| Console | `ConsoleLabel` string | The system inventories serial numbers or controls power/HDMI. |
| Display / TV | The client watching `station:{code}` | A station must manage two panels with different roles. |
| Device | In-memory SignalR presence only | Provisioning, monitoring, or revocation of a physical client is required. |
| Payment | `PaidAtUtc` plus status `readyToResume` | A gateway, amount, currency, or receipt exists. |
| Staff user | Anonymous LAN client | More than a trusted demo network exists. |
| Branch | The one deployment | A second site exists. |
| Customer, booking, price | Absent | Those products are scheduled. |

### 4.4 History limitation to remember before real money

Resume overwrites `StartedAtUtc`, `ExpiresAtUtc`, and `PaidAtUtc` on the same session row. v0.1 cannot reconstruct earlier windows or prove what was paid. That is acceptable because payment is a button with no amount.

Before a real charge is stored, introduce an append-only period (or charge) row per window. Do not try to recover that history from logs later. This is called out again in Stage 2.

### 4.5 Identity choices

- Station **code** is the external key (`ps5-01`). It is stable, short, and safe in a TV URL.
- Station and session **Guids** are internal. Session commands use the session Guid so a stale browser cannot hit "whatever is open" by station code alone after the session has changed.
- Display names are never used as keys. Renaming `PS5 #01` must not change the group or the URL.

---

## 5. Session state machine

### 5.1 Station presentation versus session status

AVAILABLE is not a session status. It means "this station has no open session."

Two different fields are sent to clients:

| Field | Used by | Values |
|---|---|---|
| `displayMode` | TV, any future panel | `available`, `gameRunning`, `timeExpired` |
| `session.status` | Staff dashboard, to choose buttons | `running`, `awaitingPayment`, `readyToResume`, `closed` |

Server mapping:

| Condition | `displayMode` | Staff action |
|---|---|---|
| No open session | `available` | Start session |
| Open session `running`, and `now < expiresAtUtc` | `gameRunning` | End session. The countdown stays visible. |
| `awaitingPayment`, or `running` but `now >= expiresAtUtc` | `timeExpired` | Mark payment done, or End |
| `readyToResume` | `timeExpired` | Resume session, or End |

Confirmed staff-visible flow:

```text
AVAILABLE
  → GAME RUNNING          start, 120 seconds from server configuration
  → TIME EXPIRED          server expiry; payment is pending
  → payment simulation    staff marks payment; TV stays TIME EXPIRED
  → RESUME                new 120-second period from server configuration
  → GAME RUNNING
  → END                   also allowed during an active running session
  → AVAILABLE
```

The TV stays on TIME EXPIRED between payment and resume. There is no PAID screen. The dashboard is where payment becomes visible. The TV returns to GAME RUNNING only when play actually resumes.

If a later state is inserted (for example a real gateway's `paymentPending`), the server maps it to an existing `displayMode` until the TV software is updated. Old panels keep working. The staff UI, which is easy to redeploy, learns the new button.

### 5.2 States and transitions

```text
        (no open session)
              │
              │ staff: start
              ▼
          ┌─────────┐
          │ running │◄──────────────────────────┐
          └────┬────┘                           │
               │ server: now >= expiresAtUtc    │ staff: resume
               ▼                                │
      ┌──────────────────┐                      │
      │ awaitingPayment  │                      │
      └────────┬─────────┘                      │
               │ staff: mark payment            │
               ▼                                │
      ┌──────────────────┐                      │
      │ readyToResume    │──────────────────────┘
      └──────────────────┘

   staff: end, from any open status
              │
              ▼
           closed   → station displayMode = available
```

### 5.3 Who may trigger what

| Transition | Trigger | Not allowed |
|---|---|---|
| start → `running` | Staff, via POST | Display client. A second start while a session is open. |
| `running` → `awaitingPayment` | Backend sweeper, startup reconcile, or lazy reconcile on read | Staff, display, or a client "time's up" message. |
| `awaitingPayment` → `readyToResume` | Staff: mark payment | Display. Payment while still `running`. |
| `readyToResume` → `running` | Staff: resume | Display. Resume while `running` or `awaitingPayment`. Resume must not add time if the session is already running. |
| any open status → `closed` | Staff: end | Display. |

Devices do not participate in the state machine. They render snapshots.

### 5.4 Transition rules

- Start creates a row. `expiresAtUtc = serverNow + configured duration`. Default duration is 120 seconds.
- Expire only changes status to `awaitingPayment`. It does not change `expiresAtUtc`.
- Payment sets `paidAtUtc = serverNow` and status `readyToResume`. It does not start time.
- Resume sets status `running`, `startedAtUtc = serverNow`, `expiresAtUtc = serverNow + configured duration`, `paidAtUtc = null`, and stores the new `durationSeconds`. It is a new window, not an extension of the expired one.
- End sets `closed`. The station has no open session.
- Every successful transition increments `GamingStation.StateVersion` in the same transaction as the session write.
- Illegal transitions return HTTP 409 and the current snapshot, and they do not write.
- Duplicate payment: if status is already `readyToResume`, return 200 with the current snapshot.
- Duplicate resume: if status is already `running`, return 409 and do not move `expiresAtUtc`.
- Two parallel payments: the concurrency token lets one commit. The loser reloads, sees `readyToResume`, and returns 200 without writing.
- Two parallel resumes: the concurrency token lets one commit. The loser reloads and follows the duplicate-resume rule. Extra time is not granted.
- Two parallel starts: the filtered unique index lets one insert. The loser returns 409.

There is no pause in v0.1. Disconnect does not pause.

### 5.5 Why these states and not a shorter list

Collapsing "awaiting payment" and "ready to resume" into a single TIME EXPIRED status would hide which button the dashboard must show, or it would push that distinction into ad-hoc booleans. The three open statuses match the three staff steps (wait, pay, resume) without adding payment infrastructure.

`closed` exists so AVAILABLE is reachable again without deleting the database.

---

## 6. Timer and expiry

### 6.1 Time model

- The only clock that writes state is the server clock, read through `TimeProvider` so tests can move time without sleeping.
- All persisted timestamps are UTC.
- `startedAtUtc` is set at start and again at resume.
- `expiresAtUtc` is set at the same moments from configuration. Clients cannot submit an expiry.
- `serverTimeUtc` is included on every snapshot so clients can remove skew.
- A session is due when `status == running` and `now >= expiresAtUtc`. The due condition is evaluated in one place inside the coordinator. The sweeper, startup, and GET all use it.

Status is a cache of that fact. If the process dies after the deadline and before the status write, the next reconcile still expires the session because `expiresAtUtc` is unchanged. Do not make a flag the only record of expiry.

### 6.2 What each client does with time

- On snapshot, compute `offsetMs` and `remainingMs` as in section 3.2.
- Animate locally at a short interval (a quarter-second is enough).
- Do not flip the label to TIME EXPIRED when the animation hits zero.
- Refetch the station at zero. Render whatever `displayMode` comes back.
- If the refetch fails, keep the last confirmed mode and show a disconnected indicator. The number may sit at 0:00 while the mode still says GAME RUNNING. That is preferable to a lie.
- A new snapshot replaces offset and deadline. Do not "correct" the server with the client clock.

Multiple clients will agree within a fraction of a second. Frame-level sync is unnecessary and should not be built.

### 6.3 Scenarios

| Scenario | Behavior |
|---|---|
| TV disconnects during play | Session keeps running. Sweeper can still expire it. Nothing is paused. |
| TV reconnects | Join group, GET, render `expiresAtUtc` against current `serverTimeUtc`. Remaining time does not restart. |
| Mobile disconnects | Same for the dashboard. An in-flight click that got no response is retried by the human. The state machine makes the retry safe. |
| Browser refresh | In-memory UI state is discarded. GET is the new source. SignalR joins after load. |
| Backend restarts while time remains | SQLite still has `running` and a future `expiresAtUtc`. Clients reconnect and show the remaining time. Presence counts reset until sockets return. |
| Backend is down across the deadline | On startup the sweeper's first pass expires the session and broadcasts to whoever is already connected. Clients that connect afterward GET the expired snapshot. |
| TV clock is wrong | The offset absorbs a stable skew. Status does not use the TV clock. |
| Many clients on one station | Each applies the same snapshot. One `stateVersion` prevents an older snapshot from overwriting a newer one. |

### 6.4 Backend implementation options

| Approach | Behavior | POC fit |
|---|---|---|
| One-second sweep in a hosted service | Selects due `running` sessions, expires each through the coordinator, broadcasts after commit. Survives restart because the deadline is in the database. | Recommended. At 50 stations the query is trivial with an index on status and `expiresAtUtc`. |
| Lazy reconcile only | Expire when a GET or command loads the row. | Necessary as a backstop, and insufficient alone: if no client calls, nobody is notified. |
| In-memory timer per session | Wake exactly at `expiresAtUtc`. | Lost on restart, duplicated if a sweep also exists, more code. |
| Hangfire or Quartz | Durable job per session. | Extra infrastructure for a single periodic check. Defer. |

### 6.5 Recommended expiry mechanism

Use **lazy reconcile plus a one-second sweep**, both calling the coordinator.

- The sweep provides push notification around the deadline.
- Lazy reconcile on GET means a client that wakes at zero observes the expired status even if it races the sweep. The GET that notices a due session performs the same transition and broadcast, so the other client hears it too.
- Once status is `awaitingPayment`, further sweeps and GETs do not write.
- Broadcast only after the database commit, so a client that refetches immediately cannot observe the old status.
- If the broadcast fails because nobody is connected, nothing retries it. The next GET or the next client to join-and-get still sees the committed status.

Configure `Session:DefaultDurationSeconds` (120) and `Session:SweepIntervalSeconds` (1). Changing the demo to a longer session is a configuration change, not a design change.

---

## 7. SignalR design

### 7.1 Hub, groups, and event

One hub: `/hubs/stations`.

| Group | Who joins | What they receive |
|---|---|---|
| `station:{code}` | The display for that station | Snapshots for that station |
| `staff` | Every staff dashboard | Snapshots for every station |

Example: the Android TV for the POC joins `station:ps5-01`. The phone joins `staff`. A client does not join both, so it is not delivered the same event twice.

Group names use `code`, never the display name. A rename of `PS5 #01` does not move the group.

There is one server-to-client event, `stationStateChanged`. The payload is the station snapshot (section 8), plus a `reason` string for diagnostics: `sessionStarted`, `sessionExpired`, `paymentMarked`, `sessionResumed`, `sessionEnded`. Clients render the snapshot and may ignore `reason`. They do not apply partial patches.

This is intentional. A missed intermediate event is healed by the next snapshot or by GET. Patch-based messages are not.

### 7.2 Registration and station association

Hub methods in v0.1:

- `JoinStation(code)` — validate that the station exists, then add the connection to `station:{code}`.
- `JoinStaff()` — add the connection to `staff`.

No device registration message. The display knows its station because it was opened at `/display/ps5-01`.

On disconnect, SignalR removes the connection from its groups. The client library is configured to reconnect. On reconnect it must call `JoinStation` or `JoinStaff` again, then GET the snapshot. Automatic reconnect does not restore groups.

### 7.3 Ordering rule

Every snapshot carries `stateVersion`. The client applies a snapshot from either HTTP or SignalR only when `stateVersion` is greater than the version it last applied. Equal versions are ignored. This closes the race where a GET response and a live event pass each other.

Suggested client sequence:

1. Connect.
2. Join the group (live events start arriving and are applied if newer).
3. GET the station or station list and apply if newer.

### 7.4 Presence

An in-memory count of connections per station group is exposed on the snapshot as `connectedDisplayCount`. The dashboard can show that the TV is offline. The count is not stored in SQLite and is cleared by a process restart until the TV reconnects. It is diagnostic. Session state must not depend on it.

This count is valid for one API process. A future second process needs a shared backplane before presence is trustworthy. Session truth does not have that problem, because it lives in the database.

### 7.5 Message flow — start session

```text
Staff phone
    │  POST /api/stations/ps5-01/sessions
    ▼
REST API
    ▼
Session coordinator
    │  insert Session(running), increment stateVersion
    ▼
SQLite commit
    │
    ├── HTTP 200 snapshot ──────────────► Staff phone applies it
    │
    └── SignalR stationStateChanged
            ├──► group station:ps5-01   TV shows GAME RUNNING + countdown
            └──► group staff            other dashboards show the same
```

### 7.6 Message flow — session expired

```text
Sweeper (or a GET that notices the deadline)
    ▼
Session coordinator
    │  status = awaitingPayment, increment stateVersion
    ▼
SQLite commit
    ▼
SignalR stationStateChanged
    ├──► TV        displayMode = timeExpired     → TIME EXPIRED
    └──► Staff     status = awaitingPayment      → MARK PAYMENT DONE
```

Payment and resume use the same shape as start: POST, commit, response snapshot, then one broadcast.

### 7.7 What the hub must not do

The hub does not start, expire, pay, or resume. A TV cannot invoke a command method that was "only for the demo." Future platforms inherit a hub whose only job is join and receive.

---

## 8. REST API

The API is intentionally small. Station snapshots are the only read model. There is no second "session DTO" to keep in sync.

### 8.1 Endpoints

| Method | Path | Effect |
|---|---|---|
| GET | `/api/health` | Process is up. Returns `serverTimeUtc`. Used to confirm a phone can reach the PC. |
| GET | `/api/stations` | List of station snapshots. POC returns one. Dashboard uses the list. |
| GET | `/api/stations/{code}` | One snapshot. Display page and reconnect use this. |
| POST | `/api/stations/{code}/sessions` | Start. Empty body. 409 if an open session exists. |
| POST | `/api/sessions/{id}/payment` | Mark payment. 200 if already `readyToResume`. 409 if the session is not awaiting payment. |
| POST | `/api/sessions/{id}/resume` | New running window. 409 if not `readyToResume`, including if it is already `running`. |
| POST | `/api/sessions/{id}/end` | Close. 409 if already closed. |

Successful POSTs return the station snapshot. Failures with a known station return 409 and that snapshot so the client can resync. Unknown station or session returns 404.

No PATCH, no generic update, no client-supplied expiry, no `/api/tvs`.

### 8.2 Snapshot shape

Field names are camelCase. Enums are strings, not integers.

```json
{
  "code": "ps5-01",
  "name": "PS5 #01",
  "consoleLabel": "PS5",
  "stateVersion": 4,
  "serverTimeUtc": "2026-09-23T10:15:00.123Z",
  "displayMode": "gameRunning",
  "connectedDisplayCount": 1,
  "session": {
    "id": "7f1c2c0e-4b7a-4f0e-9d2a-6a0b9e5d1234",
    "status": "running",
    "startedAtUtc": "2026-09-23T10:13:00.000Z",
    "expiresAtUtc": "2026-09-23T10:15:00.000Z",
    "paidAtUtc": null,
    "durationSeconds": 120
  }
}
```

When the station is available, `session` is null and `displayMode` is `available`.

`reason` is added on the SignalR payload only. It is not required on GET.

This shape is additive. New fields may appear. Existing names and string values stay stable so a TV that is hard to update keeps working.

### 8.3 Commands are idempotent in the way the state machine allows

| Command | Safe retry | Unsafe interpretation to avoid |
|---|---|---|
| Start | A retry after success receives 409 and the open session. No second row. | Treating 409 as a hard error and starting over in the UI without reading the snapshot. |
| Payment | A retry after success receives 200 and the same `readyToResume` snapshot. | Starting the clock on payment. |
| Resume | A retry after success receives 409 because status is already `running`. The deadline does not move. | Treating "POST resume" as "add another duration." |
| End | A retry after success receives 409 because the session is closed. | Deleting history. |

The staff UI should disable the button while the POST is in flight, and then render the response snapshot regardless.

### 8.4 Versioning

No `/v1` prefix in the POC. Compatibility is handled by additive JSON and by server-computed `displayMode`. Add a version prefix later only if an incompatible break is actually required.

---

## 9. Device architecture

### 9.1 How a display finds its station in v0.1

```text
Staff configures the TV browser once
        │
        ▼
http://<pc-lan-host>:5080/display/ps5-01
        │
        ▼
Page reads the code from the URL
        │
        ├── GET /api/stations/ps5-01
        └── SignalR JoinStation("ps5-01")
```

The station code is the association. There is no device id, pairing code, or certificate in v0.1.

The Android TV is a host for that page. The backend does not read the user agent to decide behavior.

### 9.2 How this stays valid for other panels

```text
Station ps5-01
    ├── Browser at /display/ps5-01     v0.1, including the Android TV browser
    ├── Native Android TV              later, same GET and same hub
    ├── Samsung Tizen                  later, same contract
    ├── LG webOS                       later, same contract
    ├── Other browser-based TVs        later, same contract
    └── Android box / mini-PC / agent  later, same contract
```

A platform that cannot hold a WebSocket polls `GET /api/stations/{code}`. It is late by one poll interval and otherwise correct. No backend change is required to permit that.

### 9.3 What we will not build yet

- A native Kotlin Android TV app, WebView shell, or other device agent.
- A device registry, pairing PIN, or claim flow.
- Per-platform endpoints or payloads.
- Server logic that branches on browser, Android, Tizen, or webOS.
- A stored mapping from hardware serial to station. The bookmarked URL is the mapping.

When Stage 2 needs "which box is online," add a Device row that points at `StationId`, with a client-generated install id and a platform string. The session coordinator should still ignore it. Presence and inventory are not session truth.

Until that exists, a replacement TV is reconfigured by opening the same URL. That is the right operational model for one café PC and a handful of sets.

### 9.4 Android TV practical notes (deployment, not domain)

- The POC base URL is HTTP. The Android TV browser opens `http://<pc>:5080/display/ps5-01` directly. A native shell is not part of v0.1.
- Pin the PC to a DHCP reservation before bookmarking the TV.
- The display page, when it is built in a later step, uses relative `/api` and `/hubs/stations`. Do not bake a LAN address into source. A future native client stores one base URL in its own configuration.

---

## 10. Failure and reconnection

POC behavior is what v0.1 implements. "Later" is noted so it is not silently built now.

| Situation | POC behavior | Later |
|---|---|---|
| TV drops during a session | Session continues on the server clock. Dashboard is unaffected. TV shows a disconnected banner on its last confirmed mode. | Same rule. Optionally alert staff using presence. |
| TV returns | Rejoin `station:{code}`, GET, render. Countdown continues from `expiresAtUtc`. It does not restart. | Same. |
| Staff phone drops | The other clients stay live. When the phone returns it GETs the list and rejoins `staff`. | Same, plus re-authentication. |
| Browser refresh | Full reload from GET, then SignalR. | Same. |
| Wi-Fi blip | SignalR reconnects. If it cannot, clients keep the last snapshot and show disconnected. Commands fail visibly and can be tapped again. The state machine absorbs duplicate payment and rejects duplicate resume. | Same, with better offline messaging. |
| Backend process restarts | Sessions remain in SQLite. Startup reconcile expires anything already due. Clients reconnect and GET. In-memory presence resets. | Run as a Windows Service with a restart policy. |
| PC sleeps or reboots | The café stops. On wake, the same reconcile path runs. Clients had been disconnected. | Disable sleep. Production uses a machine that is a server, not a laptop lid. |
| TV clock differs from the server | Offset is applied to the animation. Status comes from the server. | Same. Do not depend on NTP for correctness. |
| Two staff act on one station | Both receive the same snapshots. Buttons follow status. Parallel payment: one write, the other sees `readyToResume` and succeeds idempotently. Parallel resume: one window is created, the other gets 409 and does not extend time. Parallel start: one session, the other gets 409. | Staff accounts and an audit of who clicked. Optimistic concurrency remains. |
| Session expires while the TV is offline | Sweeper commits `awaitingPayment`. On reconnect the TV GETs TIME EXPIRED. It does not replay a fake GAME RUNNING interval. | Same. |
| Countdown hits zero as the socket drops | The TV refetches. If the network is still down, it stays on the last confirmed mode with a disconnected banner and a time of 0:00, and retries the GET. | Same. |
| Broadcast succeeds and a client misses it | The next snapshot or the zero-refetch heals it. `stateVersion` stops an older packet from winning later. | Same. An outbox is unnecessary at this scale. |
| Database file deleted or rebuilt | Sessions are gone. The seed recreates `ps5-01` only. | Backups before this is a real café. |

Practical POC rule: **if the client is unsure, it asks the server. It does not repair session state locally.**

---

## 11. Future expansion

### 11.1 Stage 1 — POC (this document)

```text
1 PC · 1 station · 1 PS5 label · 1 display page · 1 staff dashboard
```

Ships the contract, the state machine, the hybrid timer, and SQLite.

Deliberately absent: auth, real payments, periods/charges, device tables, console control, reports, service hardening.

### 11.2 Stage 2 — one café

```text
1 backend process · 10–50 stations · PS5 / Xbox / PC labels
several displays · several staff phones
```

**Unchanged**

- Station-scoped API, hub groups, and snapshots.
- Coordinator as the only writer.
- Hybrid timer and the expiry sweep.
- Display page and its `displayMode` contract.
- Relative URLs so clients are not reissued when the PC's IP is reserved properly.
- "One open session per station."

**Added**

- More station rows. Console variety is more `consoleLabel` values, not new code.
- Staff choose a duration. The server validates it and still writes `expiresAtUtc` itself.
- Immutable session periods (or charges) written before real money is taken. Payment becomes a row that references a period. The state machine gains `paymentPending` if the gateway is asynchronous. Existing TVs keep working because `displayMode` stays server-mapped.
- Staff login and roles. Checks live in the coordinator's command path.
- PostgreSQL when backups, reporting, or a second process are required. Same EF model.
- Windows Service, firewall rule, disk backup of the database, PC sleep disabled.
- Optional device records and a "display offline" alert that uses presence properly.
- Price list and a minimal revenue report, read from periods, not from the mutable session row.

**May need to change**

- SQLite file to PostgreSQL.
- Anonymous access to authenticated commands.
- Resume-overwrites-timestamps, replaced by appending a period.
- In-memory presence, if more than one API process appears (not expected in Stage 2).

**Design correctly now:** items in section 2.5.

**Defer until Stage 2 is actually funded:** gateway integration, bookings, customer accounts, HDMI/power control, PWA packaging, device pairing, audit log UI.

### 11.3 Stage 3 — several branches

```text
Cloud control plane
    ├── Branch A local session backend · tens of stations
    ├── Branch B local session backend
    └── Branch C ...
```

**The live session authority should stay at the branch.** A cloud outage must not freeze customers who are already playing, and the LAN path from TV to a local PC is more reliable than TV to cloud to TV. The POC rule "the backend on this site is the source of truth for these sessions" still holds. It becomes "the branch backend is the source of truth for this branch."

The cloud side owns what is not a live countdown: organization, branch directory, staff identity, bookings that can sync down, prices, and rolled-up revenue. Each branch syncs completed periods upward. Commands for a live session still go to the branch.

**Unchanged for clients on the café floor**

- The display and the staff dashboard still use a base URL and the same JSON and hub contract.
- They do not learn about other branches.

**Added**

- A `Branch` concept on the server that is about to serve more than one site.
- Sync of closed periods and station inventory to the cloud.
- Central user management. Branch tokens so a site can sync without sharing a database.

**May need to change**

- Hosting of the branch API (still one process, possibly still on a small PC, database PostgreSQL).
- Identity. The POC's open LAN is retired.
- Reporting queries, which move off the live session row.

**Defer:** multi-tenant sharding tricks, a global SignalR bus across branches, active-active session writes in the cloud.

### 11.4 Moving the host without rewriting clients

Clients are already host-agnostic if they call relative `/api` and `/hubs/stations` on the origin that served them, or if a native shell stores a single base URL.

| Move | What changes | What does not |
|---|---|---|
| PC IP changes | DHCP reservation, or the shell's configured URL. The web page itself has no IP in it. | Session rules, TV UI, dashboard UI. |
| Replace the PC with another machine on the LAN | Install the same app. Restore or recreate the database. Point DNS or the bookmark at the new host. | Clients, if they use the bookmark or DNS name. |
| Put a reverse proxy in front | Proxy terminates HTTPS and forwards to Kestrel. | Clients, if the public origin is what served the page. |
| Move a single-site backend to a cloud VM | Possible, because the contract is just HTTP and WebSocket. Update DNS. Add auth and HTTPS first. | Client code. |

That last row is technically possible and operationally the wrong default for a trading café. Internet loss would stop start, expiry notification, and payment. Prefer the Stage 3 split: local session authority, cloud control plane. Clients still do not change, because their base URL continues to be the branch.

What would force a client rewrite later: embedding IPs in source, computing status only in Kotlin, using a custom socket protocol, or making SignalR the only way to read state. Section 2.5 exists to avoid those.

### 11.5 Feature path that should not disturb the core

Bookings, accounts, and console control should attach to the station and the session from the outside.

```text
Booking  → creates or queues a Session on a Station
Payment  → records money against a Session period
Console control → listens for session status; it is not the status
```

None of those require the TV to become the aggregate.

---

## 12. Risks and caveats

| Risk | Why it matters | Mitigation in v0.1 | Later |
|---|---|---|---|
| Café Wi-Fi is lossy or isolates clients | Sockets drop; sometimes clients cannot see the PC at all. | GET remains valid. Rejoin groups on reconnect. Refetch at countdown zero. Disconnected banner. Document "AP isolation off." | Dedicated SSID/VLAN. Ethernet for the PC. |
| SignalR reconnect loses groups | A reconnected TV is silent and looks "stuck" until refresh. | On reconnect: join, then GET. Test this explicitly. | Same, plus a slow safety poll if a platform's socket is unreliable. |
| Timer skew or a client that "expires itself" | Staff and TV disagree, or a session ends early. | Server `expiresAtUtc` plus `displayMode`. Client animation is cosmetic. Integration test with a fake clock. | Same. |
| SQLite limits | Two processes corrupt or lock the file. A rebuild deletes the file if it lives under `bin/`. | One process. WAL. Configured data directory. No SQLite SQL in business rules. | PostgreSQL when operations need it. |
| SQLite UTC comparisons | A bad date format makes the sweep miss due sessions. | One representation, UTC only, and a test that a past `expiresAtUtc` is selected and expired. | Provider change does not remove the test. |
| The Windows PC is a laptop, not a server | Sleep, Windows Update reboots, a closed lid, or a user signing out kills the café. | Document: AC power, sleep disabled, lid open or headless, firewall rule for the chosen port, process started on purpose. | Windows Service, auto-start, a machine that is not someone's workstation. |
| Binding to localhost | The phone and TV cannot connect, and the code looks fine on the PC. | Listen on the LAN interface. Step 1 is done only when a phone opens `/api/health`. | Same. |
| Multiple stations implemented as a singleton | The second station requires a rewrite of groups, screens, and commands. | List API, station code on every command, groups per code, seed one row. | Add rows. |
| Multiple staff | Double clicks grant free time or double-charge later. | Concurrency token. Idempotent payment. Resume is not additive. Unique open session. | Audit who clicked. |
| Device identity | A replaced TV or a mistyped URL watches the wrong station, or nothing. | The URL is the configuration. Staff see `connectedDisplayCount` for the station they think they started. | Device registry when there are many sets to administer. |
| Mixed TV platforms | A Kotlin-only UI strands non-Android panels. | Web display. `displayMode` owned by the server. HTTP GET always enough to render. | Thin shells per platform. Native apps only where a browser cannot kiosk. |
| Open LAN commands | A customer on guest Wi-Fi can mark their own payment. | Private SSID for the demo. No port forwarding. State this limit in the demo script. | Real authentication before real use. |
| Cloud migration done by moving session truth off-site | The café stops when the internet stops. | Clients are host-agnostic, so either topology is possible. The recommended later topology keeps session truth at the branch. | Control-plane sync. Do not rewrite clients to achieve it. |
| Resume overwrites the only copy of the window | Fine for a fake payment. Fatal for revenue. | Called out as a known v0.1 limit. No amount is stored, so there is nothing to misreport. | Append-only periods before the first real charge. |
| Scope growth during the POC | Bookings, accounts, and a device framework consume the milestone. | This document's deferred list. The implementation plan stops at the scripted flow. | Stage 2. |

---

## 13. Architecture decision table

| Area | Alternatives | Recommended approach | Why |
|---|---|---|---|
| Backend | Separate session service; modular monolith; microservices | One ASP.NET Core process, session coordinator in-process | One PC, one writer, one demo. A second deployable adds failure modes and no capability. |
| Real-time | SignalR; raw WebSockets; polling | SignalR snapshots after commit, GET for truth | Demo latency without making the socket required for correctness or for future panels. |
| Database | SQLite; PostgreSQL; SQL Server | SQLite via EF Core, WAL, file outside the build output | No database install for v0.1. Provider stays swappable if business code stays on EF and UTC. |
| Timer | Client-owned; server tick stream; hybrid timestamps | Server `expiresAtUtc`, client animation, server-committed expiry | Survives disconnect, restart, and multiple screens. Traffic stays on transitions. |
| Expiry mechanism | Per-session memory timer; lazy only; Hangfire; sweep | One-second sweep plus lazy reconcile on read | Restart-safe, one code path, push still happens if nobody is polling. |
| TV client | Native Kotlin; Unity; web page; WebView shell | Browser page at `/display/ps5-01` for v0.1 | Accepted. Later native, Tizen, webOS, and device-agent clients use the same HTTP contract. The backend does not learn the panel. |
| Staff UI | React+TS; Blazor; plain HTML | React + TypeScript, later hosted on the same site | Staff UI will grow. Blazor Server is a weak fit for a roaming phone. The display stays plain JS. |
| Station model | TV-centric; console-centric; station with equipment tables; station as aggregate | Station as the only aggregate. `consoleLabel` string. No device tables | Matches how time is sold. Avoids empty tables and PS5 coupling. |
| Device model | Pairing registry now; anonymous URL now | Bookmark `/display/{code}` | Enough to associate a panel. A registry has no second feature to serve yet. |
| Commands | SignalR methods; REST | REST commands, full snapshot responses | Idempotency, 409, and non-SignalR clients stay simple. |
| Notifications | Patches; per-event custom payloads; full snapshot | One `stationStateChanged` snapshot with `stateVersion` | Reconnect and missed messages collapse to "render the latest snapshot." |
| Display contract | Clients interpret status; server sends `displayMode` | Server sends both `displayMode` and `status` | TVs key off a stable presentation. Staff key off status to pick buttons. New states can map to old modes. |
| Deployment | IIS; Windows Service; `dotnet run`; cloud | Kestrel on the LAN for the POC, one port, HTTP | Smallest path. Service install waits until the flow is proven. |
| Auth | None; shared API key; full login | None, private LAN only | An API key in a TV URL is false safety. Real auth is Stage 2 and has a clear insertion point. |
| Multi-branch future | Cloud-only session brain; branch-local session brain | Keep session authority at the site; make clients host-agnostic now | Avoids an internet dependency for live play, and avoids a client rewrite when the host moves. |

---

## 14. Implementation plan

Each step is independently buildable and testable. Do not start a step by adding the next step's UI. Automated tests belong with the coordinator from step 3 onward; the LAN demo belongs at the end.

Suggested layout and names: `GamingStation.Api`, a `SessionCoordinator`, a `SessionExpirySweep` hosted service, hub path `/hubs/stations`. These names are guidance so the steps fit together.

### Step 1 — Backend skeleton

- **Build.** ASP.NET Core host. Kestrel listens on the LAN interface and a chosen port (for example 5080). `GET /api/health` returns status and `serverTimeUtc`. Configuration for the listen URL and `Session:DefaultDurationSeconds=120`. Logging to the console. No database and no data directory.
- **Test.** From the PC: health returns JSON. From a phone on the same Wi-Fi: the same URL using the PC's LAN address. Confirm a localhost-only binding fails this test.
- **Done.** A second device can reach health. The port and bind address are configuration, not a hardcoded loopback URL. Session duration is loaded from configuration and is not used for behavior until sessions exist.

### Step 2 — Database

- **Build.** EF Core SQLite. WAL. This is the step that introduces a configured data directory, outside the build output. `GamingStation` and `Session` with the fields and the filtered unique index from section 4. String status values. Migrate on startup. Seed `ps5-01` / `PS5 #01` / console label `PS5` if that code is missing. Do not seed sessions. Do not reseed or duplicate the station on restart.
- **Test.** Start twice: still one station. Delete is not required. Inspect the file path and confirm a rebuild does not move or wipe it. Round-trip a UTC timestamp through a scratch test or a short-lived developer check.
- **Done.** Schema matches section 4. Seed is idempotent. The file survives a restart and a rebuild.

### Step 3 — Station and session API

- **Build.** `SessionCoordinator` using `TimeProvider`. Endpoints from section 8 except the hub. Lazy expiry on read. Concurrency token behavior from section 5.4. Snapshot JSON from section 8.2 (`connectedDisplayCount` may be 0 until step 4). Integration tests with a fake clock.
- **Test.** Automated: start → pay fails while running → force clock past expiry → GET shows `awaitingPayment` / `timeExpired` → payment is idempotent → resume sets a new deadline and does not stack when repeated → end returns `available` → second start works → parallel double start yields one open session. Also hit the endpoints with an HTTP client against the running app.
- **Done.** The full state cycle is proven without a UI and without SignalR. Illegal transitions do not change `expiresAtUtc`.

### Step 4 — SignalR

- **Build.** Hub, `JoinStation`, `JoinStaff`, broadcast of the snapshot after commit, including broadcasts that originate from lazy expiry and, later, the sweep. `stateVersion` enforced in a tiny reference client or test harness. In-memory `connectedDisplayCount` on the snapshot.
- **Test.** Two connections, staff and station. REST start causes both to observe the same snapshot once. Disconnect, reconnect, rejoin, GET: state matches and the countdown deadline is unchanged. An older `stateVersion` is ignored by the test client. Presence count goes 0 → 1 → 0.
- **Done.** A transition produces one snapshot per group after the commit. Reconnect has an explicit rejoin. Session state does not live only in the hub.

### Step 5 — Staff dashboard

- **Build.** Vite + React + TypeScript. Load `GET /api/stations`. Render the one station. Buttons: Start, Mark payment done, Resume, and End, enabled from `status`. Local countdown from the section 3.2 formula. Apply the POST response immediately. Subscribe to `staff` and apply newer versions only. Show a disconnected banner. Mobile-width layout. Dev server proxies to the API.
- **Test.** On a phone-sized browser: start, watch the countdown, refresh and see the same deadline, click payment before expiry and observe 409 handled by rendering the snapshot, complete payment and resume, confirm the second browser moves without a refresh.
- **Done.** Staff can run the command cycle on a phone. Refresh does not reset the session. The UI does not contain a hardcoded single-station exception that would block a second row.

### Step 6 — Display client

- **Build.** Plain page at `/display/{code}` served by the API. Large AVAILABLE / GAME RUNNING / TIME EXPIRED from `displayMode`. Same countdown formula. Refetch when the animation hits zero. Rejoin `station:{code}` after reconnect. Disconnected banner. No command buttons.
- **Test.** Desktop browser at `/display/ps5-01` first. Then the Android TV browser, or a second phone if the set is not ready. Kill the page mid-session and reopen it: remaining time matches the dashboard and was not restarted.
- **Done.** The display's words change only when a snapshot's `displayMode` changes. The page has no API host hardcoded. The Android TV opens this URL in its browser. No Kotlin project is added.

### Step 7 — Integration on one origin

- **Build.** Publish the staff app onto the same Kestrel site. One LAN base URL serves health, API, hub, staff UI, and display page. Document the firewall rule and the DHCP reservation.
- **Test.** Phone uses the Kestrel staff URL, not the Vite dev server. TV uses the display URL. Both follow a start command. Stop Vite and confirm the demo still works.
- **Done.** A person can run the demo from one process and two browsers on the LAN.

### Step 8 — Timer, expiry, and payment simulation

- **Build.** `SessionExpirySweep` at the configured interval, plus a startup pass through the same coordinator. Confirm payment does not change the TV mode, and resume starts a full new window of `DefaultDurationSeconds`.
- **Test.** Start a 120-second session and do not touch either client. Both move to TIME EXPIRED / Mark payment done at about 120 seconds. Mark payment: TV stays TIME EXPIRED, dashboard shows Resume. Resume: both show GAME RUNNING and a new 120-second deadline. Restart the process with 30 seconds left: after clients reconnect, the remaining time continues and then expires on schedule. Restart the process after the deadline but before anyone clicks: the station comes back already expired.
- **Done.** The scripted POC flow, including expiry with idle clients and a process restart, matches sections 5 and 6.

### Step 9 — End-to-end test on the LAN

- **Build.** A short manual script that checks each item in section 1.1, plus: duplicate payment click, duplicate resume click, TV unplugged from Wi-Fi during play, TV restored, staff refresh, backend restart.
- **Test.** Run it on the real PC, a staff phone, and the Android TV (or record that a second browser substituted for the TV). Fix defects before calling the POC done.
- **Done.** Each section 1.1 item is checked. Known limits from section 12 (no auth, no payment amount, no console control) are listed in the test notes so they are not reported as bugs.

---

## Accepted decisions and remaining assumptions

Confirmed for implementation:

| Id | Decision |
|---|---|
| A2 | Start and resume each use 120 seconds from `Session:DefaultDurationSeconds`. Staff cannot type a duration in v0.1. |
| A3 | The TV has no PAID screen. It stays on TIME EXPIRED from expiry until resume. |
| A4 | End Session is in v0.1, including ending a session that is still running. |
| A10 | v0.1 display host is the browser page `/display/ps5-01`. Native Android TV, Tizen, webOS, and device agents are later clients of the same contract. |

Still assumptions, not re-opened by the review:

| Id | Assumption |
|---|---|
| A1 | The demo network is private. Guest devices are not on the same L2 network as the API. |
| A5 | One display is bookmarked per station. Extra browsers may also watch; they are not a new product concept. |
| A6 | The backend clock is roughly correct. Clients do not need correct clocks. |
| A7 | English UI strings are enough for the POC. |
| A8 | HTTP on the LAN is enough for the POC. |
| A9 | "PS5" is a label. The POC does not integrate with PlayStation hardware. |

---

## Final recommended architecture

Run a single ASP.NET Core process on the café Windows PC. It is the only writer of session state. SQLite, through EF Core, stores gaming stations and sessions. The gaming station is the business object; the TV is only a client.

Staff commands travel over a small REST API. After each commit the process publishes one full station snapshot on SignalR, to `station:{code}` and to `staff`. Clients that miss the event recover with GET. Every snapshot carries UTC timestamps, a server clock reading, a monotonic `stateVersion`, a session status for the dashboard, and a server-chosen `displayMode` for the TV.

The timer is `expiresAtUtc`. Clients only animate it. A one-second in-process sweep, plus a check on read, moves a due session to awaiting payment and notifies both screens. Payment is a simulated status change. Resume opens a new server-timed window. Disconnect never pauses or destroys a session.

The staff UI is React + TypeScript. The v0.1 TV client is a plain web page at `/display/{code}`, opened fullscreen in the Android TV browser. Native Android TV, Tizen, webOS, and an external device agent are later clients. They are not v0.1 deliverables, and the API must not grow a dependency on the browser client.

Nothing in the client hardcodes the PC's address. That is what later allows a different host, and what allows session authority to stay at the branch if a cloud control plane is added. v0.1 does not build that cloud, auth, real payments, or equipment tables.

---

## Open questions

None. Q1 and Q2 are decided, and the duration and TV-wording assumptions are confirmed. Do not add a native TV client, a data directory, or session behavior under this decision set.

---

## Next step

Step 1 is implemented in `src/GamingStation.Api`: `GET /api/health`, listen URL `http://0.0.0.0:5080`, and `Session:DefaultDurationSeconds` of 120. It does not include SQLite, a data directory, SignalR, React, the display page, or authentication.

When this step is accepted, the following implementation step is **Step 2 — Database**. That step introduces the SQLite file and its data directory. Do not start it until it is requested.
