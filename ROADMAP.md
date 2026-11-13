# Takyan — Technical Roadmap

Central technical improvement backlog. Generated from a full repository audit on 2026-11-13 (commit `1b4ad48`).
Keep this file current: tick tasks when done, add new findings as tasks, and never mark unverified work complete.

**How the audit was done:** every `.cs`, `.tscn`, `.tres`, `.import`, and config file was read. `dotnet build` was run on a scratch copy of the sources (it passed with 0 warnings and 0 errors). `gitleaks` scanned all 49 commits plus the working tree and found nothing.
**What was not done:** Godot is not installed on the audit machine, so the game was **not run, played, profiled, or exported**. "Confirmed" findings below are confirmed by reading the code, not by reproducing them at runtime. Every P0/P1 task should start by reproducing the bug in the editor.

---

## 1. Project Health Overview

| | |
|---|---|
| **Game** | Takyan: a 2D, 2-player competitive juggling and kick game based on the Filipino game *sipa/takyan*. |
| **Stack** | Godot 4.7 .NET (`Godot.NET.Sdk/4.7.0`), C# targeting `net8.0`, GL Compatibility renderer, ENet high-level multiplayer. |
| **Modes** | Online, with a headless dedicated server (`--server`), 4-char room codes, and server-authoritative takyan physics. Local Battle, with 2 players on one keyboard. |
| **Size** | 12 C# files (~1,900 LOC), 8 scenes, no tests, no CI, no export presets. |
| **Maturity** | Playable prototype or student project. The core loop works end to end for a single match. Session lifecycle, multi-match flows, and cheating resistance are not production-grade. |

**Strengths**
- Clear separation of the networking autoloads (`NetworkManager`, `RoomManager`) from the gameplay autoload (`GameManager` and its child `ServeManager`).
- The server owns takyan physics and scoring. Clients run takyan as a kinematic puppet.
- Consistent signal-based UI updates, and most `_ExitTree` handlers unsubscribe C# events.
- Gameplay values are mostly `[Export]` fields that can be tuned in the inspector.
- Compiles cleanly. No secrets in history.

**Weaknesses**
- The dedicated server is advertised as multi-room (`MaxClients = 64`) but has **one** World scene and **one** `GameManager`/`ServeManager`. A second match corrupts the first.
- Session state in autoloads is never fully reset between matches, which breaks the second match in the same session (online and local).
- Clients are trusted for player position and for every kick parameter (force, mega-kick flag, which player kicked).
- No handling for an opponent or the server disconnecting mid-match. The remaining player gets stuck.
- `PlayerOne.cs` (820 lines) mixes input, physics, kick rules, networking, audio, camera, and HUD.
- Online and Local kick rules diverge. Magic phase integers are used throughout.

**Most urgent concerns:** TK-001 (one match per server), TK-002 (second-match softlock), TK-003 (disconnect handling), and TK-010/TK-011 (server-side validation of kicks and player state).

---

## 2. Security & Critical Issues

The game has no accounts, no persistence, no save files, no payments, and no third-party SDKs. The security surface is the **ENet RPC interface of the dedicated server**. The README encourages exposing it to the internet through port forwarding.

### Security findings

| ID | Sev. | Title | Location | Confidence |
|---|---|---|---|---|
| SEC-01 | High | Kick RPC is fully client-authoritative | `player/PlayerOne.cs:599-619`, `game/World.cs:254-291`, `takyan/Takyan.cs:133-141` | Confirmed (code) |
| SEC-02 | High | Player-state RPC has no ownership or plausibility check | `player/PlayerOne.cs:542-577` | Confirmed (code) |
| SEC-03 | Medium | Unused `AnyPeer` `PlayerOne.RpcTeleport` can move any player | `player/PlayerOne.cs:579-586` | Likely, requires verification |
| SEC-04 | Medium | Server broadcasts game RPCs to every connected peer and room | `game/GameManager.cs:99,121,128,134,150,202,216`, `game/ServeManager.cs:54,67,79,85,106,144,150,172,178`, `takyan/Takyan.cs:57-63,158-170`, `game/World.cs:314-324` | Confirmed (code) |
| SEC-05 | Low | Guessable room codes, no join rate limiting | `networking/RoomManager.cs:20-21,72-103,175-184` | Likely |
| SEC-06 | Low | Unvalidated client strings (animation name, room code) | `player/PlayerOne.cs:557`, `networking/RoomManager.cs:76` | Confirmed (code) |
| SEC-07 | Low | Unauthenticated, unencrypted transport promoted for internet play | `networking/NetworkManager.cs:59-74`, `README.md:128-141` | Confirmed |
| SEC-08 | Info | Hardcoded VPN IP default and developer's personal path | `ui/MainMenu.cs:47`, `ui/main_menu.tscn:73`, `StartServer.bat:3` | Confirmed |

**SEC-01: Kick RPC is client-authoritative.** `CmdSendKickAction(forcePower, horizontalInfluence, spinStrength, isMegaKick)` is `AnyPeer`. The server:
- never checks that the sender owns the node it called (it uses the node's `PlayerNumber`, not the sender's);
- never checks that the takyan is inside that player's KickZone;
- never enforces the cooldown or the `MinKickForce`/`MaxKickForce` bounds;
- never rejects NaN or infinite values;
- trusts the client's `isMegaKick` flag. During juggling, that flag comes from the client's own `_kickCount`, which is independent of the server's `serveKicks`.

*Exploit:* a modified client calls `CmdSendKickAction` on `/root/world/Player1` or `/root/world/Player2` every tick. It can kick from anywhere on the map, keep the takyan airborne forever, count serve kicks for the opponent's node, or launch the takyan with arbitrary velocity. *Impact:* match integrity. *Fix:* TK-010. *Complexity:* Medium.

**SEC-02: Player-state RPC trusts the client.** `CmdSendPlayerState` is `AnyPeer` and does `GlobalPosition = position` on whichever player node was targeted. It never checks that the sender owns that node, and it applies no speed or bounds check. Server-side player bodies collide with the server-simulated takyan. *Exploit:* teleport or speedhack your own player, or move the opponent's server-side body to block or redirect the takyan. *Fix:* TK-011. *Complexity:* Medium.

**SEC-03: `PlayerOne.RpcTeleport` is unused attack surface.** It is `AnyPeer, CallLocal = true` with no checks, and nothing in the codebase calls it. Godot's `SceneMultiplayer.server_relay` defaults to `true` and the project does not change it. If relay applies, a client could invoke it on other clients and move their *local* player. *Fix:* delete it, or make it `Authority` (TK-012). *Complexity:* Small.

**SEC-04: Game RPCs reach peers outside the match.** `GameManager` and `ServeManager` use `Rpc(...)`, which broadcasts to every connected peer. `Takyan` and `World` loop over `GetPlayingRooms()`, which includes every playing room. Two consequences:
- A client waiting in another room's lobby receives `RpcRoundRestart`. Three seconds later it calls `ChangeSceneToFile("res://world.tscn")` and is pulled into a match it is not part of.
- Score, phase, and takyan state leak across rooms.

This is the security face of the architectural defect TK-001.

**SEC-05: Room codes are guessable.** Codes are 4 characters from a 32-character alphabet (about 1M combinations) generated with `System.Random`. Join attempts are not rate-limited, so a scripted client can enumerate codes and take the P2 slot of a waiting room. Low severity for a casual game. Fix: TK-031.

**SEC-06: Client strings are not validated.** The animation name a client sends is assigned straight to `AnimatedSprite2D.Animation` on the server, so an invalid name produces error spam. Room codes are not length-checked before `ToUpperInvariant()`. Fix: covered by TK-011 and TK-031.

**SEC-07: Transport.** The ENet transport has no DTLS and no handshake or version check. Acceptable for LAN or Tailscale play, which the README already suggests. Document the risk before recommending public port forwarding. Fix: TK-031 (docs) and deferred D-04.

**SEC-08: Hardcoded addresses.** `26.205.78.70` (a Radmin VPN range address) is the default server address. It is not a credential, but it ships every build pointed at someone's private host, and the README says the default is `127.0.0.1`. `StartServer.bat` hardcodes `C:\Users\chris\...`. Fix: TK-030.

**Not applicable:** authentication and authorization (no accounts), save-file tampering (no persistence), economy or currency (none), third-party SDKs or analytics (none). Secrets: `gitleaks` found no leaks in 49 commits or the working tree.

### Critical gameplay and reliability bugs

| ID | Title | Evidence | Task |
|---|---|---|---|
| BUG-01 | Dedicated server can only host one match at a time. A second room's match reloads the server World and destroys the first. | `RoomManager.ServerStartMatch` calls `GetTree().ChangeSceneToFile("res://world.tscn")` (`networking/RoomManager.cs:141`). There is one `GameManager` and one `ServeManager` autoload. Player nodes are named `Player1`/`Player2`, so RPC paths collide across rooms. | TK-001 |
| BUG-02 | A client's second online match in the same session never starts. | The client sets `RoundRestartPending = true` in `RpcRoundRestart` (`game/GameManager.cs:158`) and never clears it, because `ServerBeginPlayRound` returns early on clients (`:95`). On the next fresh match, `World.SpawnClientEntities` skips `RpcClientReady` (`game/World.cs:220-229`), so the server waits forever. | TK-002 |
| BUG-03 | If the opponent or server disconnects mid-match, the remaining player is stuck. | `RpcOpponentDisconnected` only has a listener in `Lobby` (`ui/Lobby.cs:40`). Nothing in `World` or `ScoreUI` handles it. `NetworkManager` never subscribes to `Multiplayer.ServerDisconnected`. The server keeps the room and the World running. | TK-003 |
| BUG-04 | Server ready flags persist across matches. | `_p1Ready`/`_p2Ready` are only reset on rematch (`game/GameManager.cs:208-209`), not in `ServerStartGame`. The next match can start serving as soon as one client is ready. A duplicate `RpcClientReady` starts a second concurrent `async StartServe` loop (`game/ServeManager.cs:44`), which has no re-entry guard. | TK-002 |
| BUG-05 | Local Battle keeps the previous match's score. | `SpawnOfflineEntities` calls `ServerStartServeSequence` instead of `ServerStartGame` (`game/World.cs:250`). Scores and `ServeManager` roles are never reset. | TK-004 |
| BUG-06 | The Local Battle REMATCH button does nothing. | `CmdRequestRematch` returns when `!_net.IsServer` (`game/GameManager.cs:187`), and offline mode is never the server. | TK-004 |
| BUG-07 | In Local Battle, Dash triggers both players. | `Input.IsPhysicalKeyPressed(Key.Alt)` is hardcoded and not an InputMap action (`player/PlayerOne.cs:278`). | TK-005 |
| BUG-08 | Running from the editor makes every instance try to host. | `project.godot:35` sets `run/main_run_args="--server"`, and `NetworkManager._Ready` reads it. This contradicts README steps 3 and 4. It only "works" when a dedicated server already holds port 7777. | TK-006 |

---

## 3. Prioritized Development Backlog

| ID | Pri | Category | Title | Size |
|---|---|---|---|---|
| TK-001 | P0 | Architecture / Networking | Enforce one active match per server process | M |
| TK-002 | P0 | Game state | Reset all per-match state at match boundaries (server and client) | M |
| TK-003 | P0 | Networking / UX | Handle opponent and server disconnects during a match | M |
| TK-010 | P1 | Security | Server-side validation of kick RPCs | M |
| TK-011 | P1 | Security | Server-side ownership and plausibility checks on player state | M |
| TK-012 | P1 | Security | Remove or lock down `PlayerOne.RpcTeleport`, and audit RPC modes | S |
| TK-004 | P1 | Gameplay | Fix the Local Battle match lifecycle (score reset and rematch) | S |
| TK-005 | P1 | Input | Make Dash an InputMap action per player | S |
| TK-006 | P1 | Dev workflow | Remove `--server` from the editor run args, or make it opt-in | S |
| TK-007 | P1 | Networking | Scope all game RPCs to the match's peers | M |
| TK-013 | P1 | Gameplay | Unify Online and Local kick resolution | M |
| TK-008 | P2 | Game state | Guard `ServeManager.StartServe` against re-entry | S |
| TK-014 | P2 | Code quality | Replace magic phase integers with the `ServePhase` enum | S |
| TK-015 | P2 | Code quality | Remove dead code and unused exports, signals, and states | S |
| TK-016 | P2 | Architecture | Split `PlayerOne.cs` into focused components | L |
| TK-017 | P2 | Architecture | Centralize role and authority checks plus per-match peer lists | S |
| TK-018 | P2 | Scenes / Physics | Clean up `world.tscn` (scaling, stray instance, names) | S |
| TK-019 | P2 | Performance | Remove per-tick lookups and allocations in hot paths | S |
| TK-020 | P2 | Testing | Add a pure-C# rules core and unit tests | M |
| TK-021 | P2 | Testing / DX | Add a CI build workflow | S |
| TK-022 | P2 | Docs / UX | Make How To Play and README match the actual rules and controls | S |
| TK-023 | P2 | Assets | Fix fragile `.godot/imported` texture references in UI scenes | S |
| TK-024 | P2 | Lifecycle | Make scene-change timers and async handlers safe when the scene is freed | S |
| TK-030 | P3 | Config | Remove the hardcoded IP and personal path, default to localhost | S |
| TK-031 | P3 | Security | Harden room codes and requests (CSPRNG, length checks, rate limit) | S |
| TK-032 | P3 | Assets | Remove duplicate and unused assets after confirming they are unreferenced | S |
| TK-033 | P3 | Assets / Perf | Review texture import settings for large backgrounds | S |
| TK-034 | P3 | UX | Add a pause/leave option during a match | M |
| TK-035 | P3 | UX / Accessibility | Settings: volume, fullscreen, key display | M |
| TK-036 | P3 | DX | Add export presets and a server/client run guide | S |
| TK-037 | P3 | DX | Formatting rules (`.editorconfig`) and a `.gitignore` tidy-up | S |

---

## 4. Implementation Phases

1. **Phase 1: Session correctness (P0).** TK-001, TK-002, TK-003, TK-006, TK-008. Goal: back-to-back matches and disconnects behave predictably.
2. **Phase 2: Server authority (P1 security).** TK-017, then TK-007, TK-012, TK-010, TK-011. Goal: a modified client cannot alter match outcomes.
3. **Phase 3: Gameplay consistency.** TK-004, TK-005, TK-013, TK-014, TK-022. Goal: Local and Online play by the same rules, and the docs match.
4. **Phase 4: Code health.** TK-015, TK-016, TK-018, TK-019, TK-023, TK-024.
5. **Phase 5: Testing and tooling.** TK-020, TK-021, TK-036, TK-037.
6. **Phase 6: Polish.** TK-030 through TK-035.

---

## 5. Detailed Task Breakdown

### Phase 1: Session correctness

- [ ] **Enforce one active match per server process**
  - **ID:** TK-001 · **Priority:** P0 · **Category:** Architecture / Networking · **Complexity:** Medium
  - **Description:** The server has a single World scene and a single `GameManager`/`ServeManager`, but `RoomManager` allows many rooms and `MaxClients = 64`. Starting a second match reloads `world.tscn` on the server, which destroys the first.
  - **Reason:** BUG-01 and SEC-04. This is the most damaging failure on a shared server.
  - **Affected:** `networking/RoomManager.cs` (`RequestCreateRoom`, `RequestJoinRoom`, `ServerStartMatch`), `networking/NetworkManager.cs:23`, `README.md`.
  - **Approach:** Choose the minimal model and keep it. While any room is `Playing`, reject `RequestCreateRoom` and `RequestJoinRoom` from other peers with a clear reason such as "Server busy". Lower `MaxClients` to a small number. Document "one server = one match" in the README. True multi-room hosting is deferred to D-01.
  - **Dependencies:** None.
  - **Acceptance criteria:**
    1. With a match in progress, a third client trying to Create or Join gets a "Server busy" style failure in `MainMenu` within 2 s, and the running match continues unaffected (scores, phase, takyan).
    2. After both players leave, a new room can be created and played.
    3. The README states the one-match limit.

- [ ] **Reset all per-match state at match boundaries**
  - **ID:** TK-002 · **Priority:** P0 · **Category:** Game state · **Complexity:** Medium
  - **Description:** The autoloads keep `RoundRestartPending`, `PreservedServer`, `_p1Ready`/`_p2Ready`, rematch flags, scores, `State`, and `ServeManager` roles across matches. Clients never clear `RoundRestartPending`.
  - **Reason:** BUG-02 and BUG-04. The second online match softlocks, and serving can start before the second player has loaded.
  - **Affected:** `game/GameManager.cs`, `game/ServeManager.cs` (`ResetState`), `game/World.cs` (`SpawnClientEntities`, `SpawnServerEntities`), `networking/NetworkManager.cs` (`Disconnect`), `networking/RoomManager.cs` (room teardown).
  - **Approach:** Add a single `GameManager.ResetMatchState()` that resets every field above and calls `_serveManager.ResetState()`. Call it:
    - on the server when a room starts a fresh match (`ServerStartMatch`) and when a room is torn down;
    - on clients from `NetworkManager.Disconnect()`, or when returning to the menu.

    On clients, clear `RoundRestartPending` when the new World has spawned for a round restart. Better still, have the server tell the client explicitly whether a load is a fresh match or a round restart (for example, a parameter on `RpcStartMatch`), so the client stops inferring it from local flags.
  - **Dependencies:** TK-001 (simplifies the server side).
  - **Acceptance criteria:**
    1. Two clients play a match that includes at least one non-final point, return to the menu, create a new room, and the new match starts with the toss countdown at 0–0.
    2. Same flow after a rematch.
    3. The server log shows "ready" from both players before "Toss countdown starting" in every match.

- [ ] **Handle opponent and server disconnects during a match**
  - **ID:** TK-003 · **Priority:** P0 · **Category:** Networking / UX · **Complexity:** Medium
  - **Description:** Nothing in `World` or `ScoreUI` reacts to `RpcOpponentDisconnected` or to a lost server connection. The server keeps simulating a half-empty room.
  - **Reason:** BUG-03. The remaining player is soft-locked with no menu to return to.
  - **Affected:** `networking/NetworkManager.cs` (add a `Multiplayer.ServerDisconnected` handler and signal), `networking/RoomManager.cs:147-171`, `game/World.cs` or `ui/ScoreUI.cs` (handler and message), `game/GameManager.cs` (reset).
  - **Approach:**
    - Client: in the World or ScoreUI, show "Opponent left" or "Lost connection" and return to `main_menu.tscn`, or show the existing post-match VBox with only "Return to Menu".
    - Server: on a mid-match disconnect, end the match, reset state (TK-002), and tear down or reset the room.
    - Verify whether clients get `PeerDisconnected(1)` or only `ServerDisconnected` when the server dies. `Lobby.cs:98-104` assumes the former.
  - **Dependencies:** TK-002.
  - **Acceptance criteria:**
    1. Killing one client mid-rally shows the other client a message within 5 s and lets them reach the main menu.
    2. Killing the server shows both clients a message and lets them reach the main menu.
    3. The server can host a new match afterwards.

- [ ] **Remove `--server` from the editor run args, or make it opt-in**
  - **ID:** TK-006 · **Priority:** P1 · **Category:** Dev workflow · **Complexity:** Small
  - **Description:** `project.godot:35` sets `run/main_run_args="--server"`, so every F5 instance tries to start a server on port 7777.
  - **Reason:** BUG-08. This contradicts the README, causes confusing logs, and can make an editor instance silently switch from server to client.
  - **Affected:** `project.godot`, `README.md`, optionally `networking/NetworkManager.cs`.
  - **Approach:** Clear `main_run_args`. Use Godot's "Customize Run Instances" (Debug menu) to give one instance `--server` when needed, and document it. Optionally also require `DisplayServer.GetName() == "headless"` or a `--server` user argument after `++`.
  - **Dependencies:** None. This changes a project setting, which is expected for this task.
  - **Acceptance criteria:**
    1. Pressing F5 with no dedicated server running logs no "Starting Dedicated Server" line.
    2. The README single-machine test procedure works exactly as written.

- [ ] **Guard `ServeManager.StartServe` against re-entry**
  - **ID:** TK-008 · **Priority:** P2 · **Category:** Game state · **Complexity:** Small
  - **Description:** `async void StartServe()` can run concurrently if it is called twice, for example after a duplicate `RpcClientReady`. Two countdowns then race and the takyan is tossed twice.
  - **Affected:** `game/ServeManager.cs:44-91`, `game/GameManager.cs:63-90`.
  - **Approach:** Add a serve-sequence token (an incrementing int captured by the loop). A newer call or `ResetState` invalidates older loops. Ignore `RpcClientReady` once `State == Playing`.
  - **Dependencies:** None.
  - **Acceptance criteria:** Calling `ServerStartServeSequence()` twice within 1 s produces exactly one countdown and one `TakyanTossed` emission, verified by log.

### Phase 2: Server authority

- [ ] **Centralize role and authority checks plus per-match peer lists**
  - **ID:** TK-017 · **Priority:** P2 · **Category:** Architecture · **Complexity:** Small
  - **Description:** The "server or offline" authority test is re-implemented in several forms (`!_net.IsClient`, `_net.IsServer || (!IsServer && !IsClient)`, `_offlineMode`). The "send to both room peers" loop is copy-pasted four times.
  - **Affected:** `networking/NetworkManager.cs`, `game/ServeManager.cs:51`, `takyan/Takyan.cs:26,48,57-63,158-170`, `game/World.cs:314-324,340-350`, `player/PlayerOne.cs:80`.
  - **Approach:**
    - Add `NetworkManager.IsAuthority` (server or offline) and `IsOffline`.
    - Add one helper, either `RoomManager.GetActiveMatchPeers()` or `RpcToMatch(Node, StringName, params Variant[])`, and use it everywhere.
  - **Dependencies:** TK-001.
  - **Acceptance criteria:**
    1. No remaining `foreach (var room in roomManager.GetPlayingRooms())` loops outside the helper.
    2. Online and Local matches still play through to a win.

- [ ] **Scope all game RPCs to the match's peers**
  - **ID:** TK-007 · **Priority:** P1 · **Category:** Networking · **Complexity:** Medium
  - **Description:** `Rpc(...)` in `GameManager` and `ServeManager` broadcasts to every connected peer, including clients sitting in the menu or a lobby.
  - **Reason:** SEC-04. Unrelated clients get pulled into `world.tscn` by `RpcRoundRestart`.
  - **Affected:** `game/GameManager.cs`, `game/ServeManager.cs`.
  - **Approach:** Replace `Rpc(name, ...)` with sends to the two match peers through the TK-017 helper, plus a local call where `CallLocal` semantics are needed on the server.
  - **Dependencies:** TK-017.
  - **Acceptance criteria:** A third client connected and idling in the main menu or a lobby receives no `GameManager` or `ServeManager` RPCs during another match, verified by log, and never changes scene.

- [ ] **Remove or lock down `PlayerOne.RpcTeleport`, and audit RPC modes**
  - **ID:** TK-012 · **Priority:** P1 · **Category:** Security · **Complexity:** Small
  - **Description:** `RpcTeleport` is `AnyPeer` with no checks, and nothing calls it.
  - **Affected:** `player/PlayerOne.cs:579-586`, and every `[Rpc(AnyPeer ...)]` method.
  - **Approach:**
    - Delete it, or change it to `RpcMode.Authority`.
    - Review every `AnyPeer` RPC. Each must return early unless `_net.IsServer`, and must derive the acting player from `Multiplayer.GetRemoteSenderId()`.
    - Consider setting `((SceneMultiplayer)Multiplayer).ServerRelay = false` on the server. Clients never need to message each other directly. Verify nothing relies on relay first.
  - **Dependencies:** None.
  - **Acceptance criteria:**
    1. No `AnyPeer` RPC can change state on a client.
    2. A written list of the remaining `AnyPeer` RPCs and their server-side checks is added to AGENTS.md §9 or a code comment.

- [ ] **Server-side validation of kick RPCs**
  - **ID:** TK-010 · **Priority:** P1 · **Category:** Security · **Complexity:** Medium
  - **Description:** Today the server accepts any kick, from any peer, for any player, at any force. See SEC-01.
  - **Affected:** `player/PlayerOne.cs` (`CmdSendKickAction`), `game/World.cs` (`OnPlayerKickRequested`), `takyan/Takyan.cs` (`ApplyKick`), `game/ServeManager.cs` (`ServerKick`).
  - **Approach:** On the server:
    1. Map the sender to a player number through `RoomManager` and reject a mismatch with the target node's `PlayerNumber`.
    2. Require the takyan to be inside that player's `KickZone`. Check the server-side `_isBallInZone`, or a distance threshold with tolerance for latency.
    3. Enforce `KickCooldown` per player on the server.
    4. Clamp `forcePower` to `[MinKickForce, MaxKickForce]` and `horizontalInfluence` to the client formula's range (±150). Reject NaN or infinite values.
    5. Compute `isMegaKick` on the server from `ServeManager` state (serve kick count or rally phase). Ignore the client value.
    6. Reject kicks in the `Waiting` and `Tossing` phases.
  - **Dependencies:** TK-017, TK-013 (shared kick resolution).
  - **Acceptance criteria:**
    1. A test client that sends `CmdSendKickAction` with force 99999, out of range, too fast, or for the other player's node has no effect on takyan velocity, and the server logs a rejection.
    2. Normal play is unchanged: the 5-kick serve still starts the rally and a 5-point match still completes.

- [ ] **Server-side ownership and plausibility checks on player state**
  - **ID:** TK-011 · **Priority:** P1 · **Category:** Security · **Complexity:** Medium
  - **Description:** `CmdSendPlayerState` applies any position, from any peer, to any player node. See SEC-02 and SEC-06.
  - **Affected:** `player/PlayerOne.cs:542-577`.
  - **Approach:**
    - Reject the update unless the sender's room player number equals the node's `PlayerNumber`.
    - Clamp the position to arena bounds and reject jumps greater than `max(Speed, DashSpeed) × elapsed × tolerance`.
    - Accept only known animation names (`default`, `run`, `jump`, `kick`).
    - Full server-simulated movement is deferred to D-02.
  - **Dependencies:** TK-017.
  - **Acceptance criteria:**
    1. Spoofed updates for the other player's node are ignored.
    2. Teleports beyond the threshold are rejected and logged.
    3. Invalid animation names produce no engine errors.
    4. Legitimate movement, including dash, is not rejected during 5 minutes of normal play.

### Phase 3: Gameplay consistency

- [ ] **Fix the Local Battle match lifecycle**
  - **ID:** TK-004 · **Priority:** P1 · **Category:** Gameplay · **Complexity:** Small
  - **Description:** Starting a second Local Battle keeps old scores and serve roles. REMATCH does nothing offline.
  - **Reason:** BUG-05 and BUG-06.
  - **Affected:** `game/World.cs:232-252`, `game/GameManager.cs:50-59,185-218`, `ui/ScoreUI.cs:90-95`.
  - **Approach:**
    - Call `ResetMatchState()` (TK-002) when a fresh Local Battle starts.
    - When offline, the Play Again button should reset state and reload `world.tscn` directly instead of sending `CmdRequestRematch`, or `CmdRequestRematch` should handle the offline case by treating both players as ready.
  - **Dependencies:** TK-002.
  - **Acceptance criteria:**
    1. Finish a Local Battle, return to the menu, and start another: the score shows 0–0 and P1 serves.
    2. Press REMATCH after a Local Battle: a new match starts at 0–0.

- [ ] **Make Dash an InputMap action per player**
  - **ID:** TK-005 · **Priority:** P1 · **Category:** Input · **Complexity:** Small
  - **Description:** Dash reads `Key.Alt` directly, so both local players dash together and the key cannot be rebound.
  - **Affected:** `project.godot` `[input]`, `player/PlayerOne.cs:82-85,278`, `ui/howtoplay.tscn`.
  - **Approach:**
    - Add `dash` (Alt) and `dash_p2` (choose a key with the team, for example Right Shift or Right Ctrl) actions.
    - Add a `_dashAction` property that mirrors the existing `_jumpAction` pattern.
    - Use `IsActionJustPressed` so dash behaves like jump.
  - **Dependencies:** None.
  - **Acceptance criteria:**
    1. In Local Battle, P1's dash key dashes only P1 and P2's dash key dashes only P2.
    2. Online dash is unchanged.
    3. How To Play lists both keys.

- [ ] **Unify Online and Local kick resolution**
  - **ID:** TK-013 · **Priority:** P1 · **Category:** Gameplay · **Complexity:** Medium
  - **Description:** Kicks are resolved in two places with different rules:
    - Offline mega-kick velocity is 600–1000 horizontally and 700–1100 vertically, always toward the opponent (`player/PlayerOne.cs:500-524`).
    - Online mega-kick velocity is 1000–2000 and 1500–2500, in a random direction (`game/World.cs:270-280`).
    - The "no double kick in rally" rule exists only online (`game/World.cs:261-268`).
    - `isMegaKick` is computed on the client.
  - **Reason:** The two modes play differently. This is also the prerequisite for TK-010.
  - **Affected:** `player/PlayerOne.cs` (`HandleKickInput`), `game/World.cs` (`OnPlayerKickRequested`), `takyan/Takyan.cs` (`ApplyKick`).
  - **Approach:**
    - In both modes, players only *request* a kick: a signal offline, an RPC online.
    - One authority-side method in `World` or a new `KickResolver` applies the rules and velocities.
    - **Ask the team which mega-kick tuning is intended before merging.** Do not pick one silently.
  - **Dependencies:** TK-017.
  - **Acceptance criteria:**
    1. A single code path computes takyan velocity after a kick.
    2. The double-kick rule applies in both modes.
    3. The chosen tuning is documented in the PR.

- [ ] **Replace magic phase integers with the `ServePhase` enum**
  - **ID:** TK-014 · **Priority:** P2 · **Category:** Code quality · **Complexity:** Small
  - **Affected:** `player/PlayerOne.cs:220-231,457-475,803-806`. RPCs that pass `(int)Phase` may keep int marshalling at the boundary only.
  - **Acceptance criteria:**
    1. `grep -n "phase == [0-9]" player/` returns nothing.
    2. Behaviour is unchanged in a full match.

- [ ] **Make How To Play and README match the actual rules and controls**
  - **ID:** TK-022 · **Priority:** P2 · **Category:** Docs / UX · **Complexity:** Small
  - **Description:** The docs and in-game text disagree with the code:
    - How To Play says "Hold to Mega Kick" (`ui/howtoplay.tscn:145`). Holding only charges force. Mega kicks are the 5th serve kick and every rally kick.
    - It says "Don't let it hit the ground on your side" (`:203`). There are no sides. When the takyan lands during a rally, the last player who touched it scores (`game/ServeManager.cs:158-183`).
    - Dash and the Local Battle P2 controls (arrows + Enter) are not listed.
    - The README says the default address is `127.0.0.1` and describes a "3-2-1-Go countdown". The code shows "Tossing in N…" then "GO!".
  - **Approach:** Update the text to match the code. **Do not change game rules in this task.** If the team wants different rules, that is a design decision and needs its own task.
  - **Dependencies:** TK-005 (dash keys), TK-013 (if tuning changes).
  - **Acceptance criteria:** Every control and rule statement in How To Play and the README can be traced to code.

### Phase 4: Code health

- [ ] **Remove dead code and unused exports, signals, and states**
  - **ID:** TK-015 · **Priority:** P2 · **Category:** Code quality · **Complexity:** Small
  - **Items (all verified unreferenced by grep):**
    - `World.ServerResetForServe` and `RpcResetForServe` (`game/World.cs:333-372`). The comment claims ServeManager calls them; nothing does.
    - `World.PlayerTwoScene` (`:7`).
    - `PlayerOne.TakyanScene` (`player/PlayerOne.cs:6`, also set in `player/player_one.tscn:305`).
    - `PlayerOne._hasServed` and `PlayerOne._activeTakyan` (never assigned).
    - The `serveManager == null` "offline juggling fallback" (`player/PlayerOne.cs:478-488`). `ServeManager` always exists, so this branch never runs.
    - `ServeManager.IsBallSpawned`, `MarkBallSpawned`, and `ballSpawned`.
    - `GameState.Countdown`, `RoomState.Finished`.
    - The `GameManager.CountdownUpdated` and `RematchReady` signals (emitted or declared, never consumed).
    - The `ScoreUI` lookups for `%WinnerLabel` and `%CountdownLabel` (`ui/ScoreUI.cs:24-25`). Those nodes do not exist.
    - `takyan/takyan_bounce.tres` (unreferenced; the scene uses an inline material).
  - **Approach:** Remove the items in one PR. Re-grep each symbol and confirm the project builds.
  - **Dependencies:** TK-012 (RpcTeleport), TK-013 (the fallback branch).
  - **Acceptance criteria:**
    1. `dotnet build` passes.
    2. A full Online match and a full Local match play through.
    3. Every listed symbol is gone.

- [ ] **Split `PlayerOne.cs` into focused components**
  - **ID:** TK-016 · **Priority:** P2 · **Category:** Architecture · **Complexity:** Large
  - **Description:** The 820-line class owns input, movement, dash, kick charging and rules, network sync (send and interpolate), five SFX players, particles, the kick counter HUD, a dynamically built "Juggler" label, the off-screen takyan tracker, and the camera.
  - **Approach:**
    - Do this incrementally, after TK-013 and TK-014, keeping the scene and node names stable.
    - Extract child nodes or plain C# helpers along the existing seams: `PlayerNetSync` (send, receive, interpolate), `PlayerAudio`, `PlayerHud` (score tracker, juggler label, takyan tracker). `GameCamera` is already extracted and can stay.
    - Consider renaming the class to `Player` only together with the scene, in a dedicated PR.
  - **Dependencies:** TK-013, TK-014, TK-020 (for a safety net).
  - **Acceptance criteria:**
    1. `PlayerOne.cs` is under 350 lines.
    2. There are no behaviour changes in Online or Local play, verified with the manual checklist in §7.

- [ ] **Clean up `world.tscn`**
  - **ID:** TK-018 · **Priority:** P2 · **Category:** Scenes / Physics · **Complexity:** Small
  - **Items:**
    - The root `world` Node2D has non-uniform scale `(0.994517, 1)` (`world.tscn:173`). It is inherited by every physics body, and Godot does not support non-uniform scaling of physics bodies.
    - An editor-placed `player one` instance with an extra `Player2` sprite and about 150 lines of duplicated SpriteFrames (`world.tscn:12-152,183-189`) is `QueueFree`d at runtime.
    - The walls are named backwards: `RightSide` is at x ≈ -288 and `LeftSide` at x ≈ 2270.
    - Players are scaled 2.5× at runtime on the `CharacterBody2D` (`game/World.cs:171`). Prefer scaling the sprite and collision shapes in `player_one.tscn`.
    - Floor detection relies on the node name `"Floor"` (`takyan/Takyan.cs:83`). Use a group or a collision layer.
  - **Approach:** Make these changes in the Godot editor, not by hand-editing the `.tscn`. Retune values only if feel changes.
  - **Dependencies:** None. Coordinate with the scene owner.
  - **Acceptance criteria:**
    1. The root scale is `(1, 1)`.
    2. No stray player instance remains.
    3. Takyan bounce, landing, and floor scoring behave the same in a 5-minute play session.
    4. The Godot editor shows no physics-scale warnings.

- [ ] **Remove per-tick lookups and allocations in hot paths**
  - **ID:** TK-019 · **Priority:** P2 · **Category:** Performance · **Complexity:** Small
  - **Description:** These are not measured bottlenecks. They are cheap hygiene fixes for code that runs every physics tick:
    - `GetNodeOrNull<ServeManager>` every tick (`player/PlayerOne.cs:217,453`), although `_serveManager` is already cached.
    - `GetNodeOrNull<RoomManager>` plus `GetPlayingRooms()` allocating a `List` every tick (`takyan/Takyan.cs:54-63`).
    - `UpdateTakyanTracker` scanning `GetParent().GetChildren()` every tick, because `_activeTakyan` is never assigned (`player/PlayerOne.cs:740-787`).
  - **Approach:**
    - Use the cached references.
    - Have `World` assign the takyan reference to each player after spawn, or find it once by the existing `takyan` group.
  - **Dependencies:** TK-017.
  - **Acceptance criteria:** None of the listed calls happen inside `_PhysicsProcess` paths. Verify with grep and code review.

- [ ] **Fix fragile `.godot/imported` texture references**
  - **ID:** TK-023 · **Priority:** P2 · **Category:** Assets · **Complexity:** Small
  - **Description:** `ui/main_menu.tscn:6-7`, `ui/lobby.tscn:6-7`, and `ui/howtoplay.tscn:6-7` embed a `CompressedTexture2D` whose `load_path` points into the git-ignored `.godot/imported/` cache. They should reference `res://art/GameMainMenuBackground.png` as an `ext_resource`. The current setup breaks silently if the source asset or its import settings change.
  - **Approach:** In the editor, reassign each TextureRect's texture to `res://art/GameMainMenuBackground.png`.
  - **Acceptance criteria:**
    1. `grep -rn "\.godot/imported" --include=*.tscn .` returns nothing.
    2. The backgrounds still render after deleting `.godot/` and reimporting.

- [ ] **Make scene-change timers and async handlers safe when the scene is freed**
  - **ID:** TK-024 · **Priority:** P2 · **Category:** Lifecycle · **Complexity:** Small
  - **Description:** Three timer and async callbacks can outlive what they touch:
    - The `GameManager.RpcRoundRestart` 3 s timer calls `ChangeSceneToFile(world)` unconditionally (`game/GameManager.cs:162-164`), even if the player has meanwhile left for the menu after a disconnect.
    - The `World.OnTossCountdownUpdated` timer lambda (`game/World.cs:139`) and `async void OnRoundEndedAnnounced` (`:143-156`) touch Labels that may have been freed. C#'s `?.` does not detect freed Godot objects; use `IsInstanceValid`.
  - **Approach:**
    - Check the expected state, such as `RoundRestartPending` and still being connected, before changing scenes.
    - Guard Label access with `IsInstanceValid(this)` and `IsInstanceValid(label)`.
  - **Dependencies:** TK-003.
  - **Acceptance criteria:** Disconnecting during the "Round Ends" window never sends the player back into `world.tscn` and raises no `ObjectDisposedException`.

### Phase 5: Testing and tooling

- [ ] **Add a pure-C# rules core and unit tests**
  - **ID:** TK-020 · **Priority:** P2 · **Category:** Testing · **Complexity:** Medium
  - **Description:** There are no tests. The riskiest logic is room management, serve and rally scoring, and match-state transitions. Today it is tangled with Godot nodes.
  - **Approach:**
    - `networking/Room.cs` is already plain C#. Extract the scoring and serve rules from `ServeManager` and `GameManager` into a plain C# class with no `Godot` dependency. The nodes keep doing RPC and signals and delegate the decisions to it.
    - Add a small `tests/Takyan.Tests` xUnit project that references only those files, and exclude the folder from the Godot csproj.
    - Cover: room join, full, promote-on-leave; serve-drop scoring; rally scoring by last touch; double-kick rejection; match end at `PointsToWin`; and reset between matches (the TK-002 regression).
    - Keep it small. Engine-level test frameworks (GdUnit4) are deferred (D-05).
  - **Dependencies:** TK-002, TK-013 (rules should be settled first).
  - **Acceptance criteria:**
    1. `dotnet test tests/Takyan.Tests` passes locally and in CI.
    2. At least the scenarios listed above are covered.

- [ ] **Add a CI build workflow**
  - **ID:** TK-021 · **Priority:** P2 · **Category:** Testing / DX · **Complexity:** Small
  - **Description:** The audit confirmed that `dotnet build Takyan.csproj` succeeds without the Godot editor, because NuGet restores `Godot.NET.Sdk 4.7.0`. A CI job can catch compile breaks on every PR.
  - **Approach:** Add `.github/workflows/build.yml` that runs `dotnet build Takyan.csproj` and `dotnet test` (once TK-020 exists) on `pull_request` and on pushes to `main`.
  - **Acceptance criteria:** A PR that introduces a C# compile error fails CI.

- [ ] **Add export presets and a server/client run guide** (TK-036 · P3 · DX · Small)
  - Commit `export_presets.cfg` for a Windows client and a Linux or Windows headless server, without any signing secrets. Document the export commands in the README.
  - *Acceptance:* a fresh clone can produce both builds by following the README alone.

- [ ] **Formatting rules and a `.gitignore` tidy-up** (TK-037 · P3 · DX · Small)
  - Extend `.editorconfig`: tabs for `*.cs`, matching the current code, plus `end_of_line = lf`, which matches `.gitattributes`. Remove the duplicate `.godot/` line in `.gitignore`.
  - *Acceptance:* `dotnet format --verify-no-changes` passes, or the team decides to enforce formatting only on new files.

### Phase 6: Polish

- [ ] **Remove the hardcoded IP and personal path** (TK-030 · P3 · Config · Small)
  - Default `HostInput` to `127.0.0.1` (`ui/MainMenu.cs:47`, `ui/main_menu.tscn:73` placeholder). Optionally remember the last-used address in `user://` with `ConfigFile`.
  - Make `StartServer.bat` take the Godot path from an environment variable, such as `GODOT_EXE`, or an argument, and use `%~dp0` for the project path.
  - *Acceptance:* no personal paths or private IPs in tracked files, and the batch file works on another machine.
- [ ] **Harden room codes and requests** (TK-031 · P3 · Security · Small)
  - Use `System.Security.Cryptography.RandomNumberGenerator` for codes. Reject room codes whose length is not `CodeLength`. Disconnect a peer after N failed joins within a short window. Document the LAN/VPN-first stance and the lack of encryption in the README (SEC-05, SEC-06, SEC-07).
  - *Acceptance:* the 6th failed join within 10 s disconnects the peer, and oversized codes are rejected without processing.
- [ ] **Remove duplicate and unused assets** (TK-032 · P3 · Assets · Small)
  - Candidates, all unreferenced by any `.tscn`, `.cs`, or `.tres`:
    - root `GameLogo.png` and `GameMainMenuBackground.png` (byte-identical to the `art/` copies);
    - `art/background.png` (about 6 MB, 2752×1536);
    - `art/tiles.jpg`;
    - `art/{Idle,Kicking,Running}/frame_*.png` (13 files);
    - `takyan/takyan_bounce.tres`.
  - **Confirm with the art owner before deleting.** Some may be source frames kept on purpose, and Godot's "export all resources" mode would otherwise ship them.
  - *Acceptance:* after deletion, the editor reports no missing dependencies and every scene opens.
- [ ] **Review texture import settings** (TK-033 · P3 · Assets / Perf · Small)
  - `art/GameBackground.png` (3200×1800) and `art/GameMainMenuBackground.png` are imported lossless with no size limit. That is about 23 MB of VRAM each, uncompressed, while they are displayed at about 0.36–0.69 scale. Consider VRAM compression or a `process/size_limit` of 2048. Measure before and after with the Godot monitor; this is not a measured problem today.
  - *Acceptance:* the VRAM figure before and after is recorded in the PR, with no visible quality regression at 1152×648 and 1920×1080.
- [ ] **Pause/leave option during a match** (TK-034 · P3 · UX · Medium)
  - Today a player can only leave from the post-match screen. Add an Esc menu with Resume (local) and Leave Match, which reuses the TK-003 path.
  - *Acceptance:* Esc during play opens the menu, and Leave returns to the main menu and correctly notifies the opponent.
- [ ] **Settings: volume, fullscreen, key display** (TK-035 · P3 · UX / Accessibility · Medium)
  - Add a master or SFX audio bus (SFX players currently use the default bus), a volume slider, and a fullscreen toggle, persisted with `ConfigFile` in `user://`. Gamepad support is in D-03.
  - *Acceptance:* the settings persist across restarts.

---

## 6. Dependencies & Implementation Order

```
TK-006 ─┐
TK-001 ─┼─> TK-002 ─> TK-003 ─> TK-024
        │      │
        │      └────> TK-004
        └─> TK-017 ─> TK-007
               ├────> TK-013 ─> TK-010
               ├────> TK-011
               └────> TK-019
TK-012 (independent)        TK-005 ─> TK-022 <─ TK-013
TK-008 (independent)        TK-014 ─> TK-016 <─ TK-013, TK-020
TK-013, TK-002 ─> TK-020 ─> TK-021 (CI can land earlier with build only)
TK-012, TK-013 ─> TK-015
```

**Recommended sequence:**
1. TK-006, TK-001, TK-002, TK-008, TK-003.
2. TK-021 (build-only CI) and TK-012.
3. TK-017, TK-007, TK-004, TK-005.
4. TK-013, TK-010, TK-011.
5. TK-014, TK-015, TK-022, TK-023, TK-024, TK-018, TK-019.
6. TK-020, TK-016.
7. P3 items.

---

## 7. Testing & Validation Requirements

There is no automated test suite yet. Until TK-020 and TK-021 land, every change needs **`dotnet build Takyan.csproj` passing** plus the relevant manual checks below. Record in the PR which checks were run.

| Area | Validation |
|---|---|
| Session and lifecycle (TK-001/002/003/004/024) | **Online smoke test:** 1 headless server and 2 clients. Create, join, play to 5, rematch, play to 5, return to the menu, create a new room, play again. Repeat with a client killed mid-rally and with the server killed mid-rally. **Local smoke test:** two Local Battles in a row, plus REMATCH. |
| Security (TK-007/010/011/012/031) | Use a debug-only test hook or a temporary script to send malformed and spoofed RPCs: wrong player node, out-of-range force, NaN, teleport positions, bogus animation names, oversized room code. Confirm rejection logs and unchanged state. Also confirm a third idle client receives no game RPCs. Do not ship the test hook. |
| Gameplay rules (TK-005/013/014/022) | Serve: the tosser auto-tosses after "Tossing in 3…", the server needs 5 kicks, and a drop gives the point to the receiver. Rally: double kicks are rejected, and on landing the last toucher scores. The match ends at 5. Check each rule in both Online and Local. |
| Physics and scenes (TK-018) | 5 minutes of play. The takyan never tunnels through the floor, walls, or ceiling. Players do not float or clip. Floor scoring fires exactly once per round. |
| Performance (TK-019/033) | Use the Godot Debugger Monitors (FPS, physics time, VRAM, network bandwidth). Record before and after values. Do not claim improvements without numbers. |
| Assets (TK-023/032) | Delete `.godot/`, reopen the project, and confirm no missing-dependency dialogs. Open every scene. |
| Docs (TK-022/036) | A teammate follows the README from a fresh clone without extra help. |

---

## 8. Deferred Improvements

These are useful, but not now. Revisit them after Phases 1–3.

- **D-01: True multi-room dedicated server.** Instance one World, `GameManager`, and `ServeManager` per room, for example under separate root nodes or `SubViewport`s with per-room physics spaces and room-scoped RPC paths. This is a Large refactor. TK-001 is the interim answer.
- **D-02: Server-simulated player movement.** The server would simulate movement from client inputs, with client-side prediction and reconciliation. It is the full answer to SEC-02 and a large change to game feel. Only pursue it if cheating becomes a real problem.
- **D-03: Gamepad support and full key rebinding UI.**
- **D-04: Transport hardening.** ENet DTLS, a protocol version handshake, and a server browser or matchmaking. Not needed for LAN or VPN play.
- **D-05: Engine-level automated tests (GdUnit4 for C#).** Scene tests for World spawning and UI flows, to add once the pure-C# tests from TK-020 exist.
- **D-06: Interpolation quality.** Snapshot buffering and time-based interpolation instead of a fixed `Lerp(…, 0.15f)` per tick (`player/PlayerOne.cs:390`, `takyan/Takyan.cs:74-75`). Address it if players report jitter.
- **D-07: Bandwidth tuning.** State is currently sent every physics tick (60 Hz) per player and for the takyan. Fine for 2 players. Revisit only if D-01 lands.

---

## 9. Definition of Done

A task is complete only when **all** of the following are true:

- [ ] The implementation meets every acceptance criterion of the task.
- [ ] `dotnet build Takyan.csproj` passes, and `dotnet test` passes once tests exist.
- [ ] The relevant manual checks from §7 were run in the Godot editor or an exported build, and the PR lists which ones.
- [ ] No known regressions in either Online or Local Battle.
- [ ] README, How To Play, AGENTS.md, and this roadmap are updated where affected, and the task box is ticked here.
- [ ] Any check that could not be run is explicitly reported as not run.
