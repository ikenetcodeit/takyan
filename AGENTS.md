# AGENTS.md — Takyan

Instructions for AI coding agents and new contributors. **MUST** marks a mandatory rule. **SHOULD** marks a recommendation.
For known issues and planned work, see [`ROADMAP.md`](ROADMAP.md). For running the dedicated server, see [`README.md`](README.md).

---

## 1. Project Identity

- **Name:** Takyan.
- **Genre:** 2D side-view, 2-player competitive juggling and kicking game, based on the Filipino game *sipa/takyan*.
- **Core loop:**
  1. The tosser auto-tosses the takyan after a 3 s "Tossing in N…" countdown.
  2. The server must juggle it with **5 kicks** to start the rally.
  3. During the rally, players alternate mega-kicks. Kicking twice in a row is rejected online.
  4. When the takyan hits the `Floor`, the **last player who touched it scores**. If it drops during the serve, the receiver scores.
  5. The scorer serves next. **First to 5** (`GameManager.PointsToWin`) wins, and the post-match screen offers rematch or return to the menu.
- **Modes:** Online, with a headless dedicated server, 4-char room codes, and 2 players. Local Battle, with 2 players on one keyboard.
- **Platform:** Windows desktop, keyboard only. No other platforms are configured, and there are no export presets.
- **Stack:** Godot **4.7 .NET**, C# (`net8.0`, `Godot.NET.Sdk/4.7.0`), GL Compatibility renderer, Godot high-level multiplayer over ENet (UDP 7777).
- **State:** Playable prototype. The core loop works for a single match. Session reset, disconnect handling, and server-side validation have known gaps; see ROADMAP §2 before touching networking or game state.

---

## 2. Architecture Overview

### Scene flow
`ui/test.tscn` (main scene, script `TitleScreen.cs`) → `ui/main_menu.tscn` (Create/Join/Local Battle) → `ui/lobby.tscn` → `world.tscn`. `ui/howtoplay.tscn` is reachable from the title. All transitions use `GetTree().ChangeSceneToFile("res://...")`.

### Autoloads (`project.godot` `[autoload]`, initialized in this order)
| Node | File | Role |
|---|---|---|
| `/root/NetworkManager` | `networking/NetworkManager.cs` | ENet peer lifecycle, `Role` (None/Server/Client), `LocalPlayerNumber`, `CurrentRoomId`, `Log()`. Starts a server when `--server` is on the command line. |
| `/root/RoomManager` | `networking/RoomManager.cs` | Server: room creation, join, and teardown (`Room.cs` is plain C#). Client: RPC stubs re-emitted as C# signals for the UI. |
| `/root/GameManager` | `game/GameManager.cs` | Scores, `GameState`, the ready handshake, round restarts, rematch. Creates its child **`/root/GameManager/ServeManager`** (`game/ServeManager.cs`) in `_Ready`. |

`ServeManager` owns the serve and rally state machine (`ServePhase`: Waiting → Tossing → Juggling → Rally) and the server/receiver roles. It emits `PointScored`, which `GameManager.AwardPoint` handles.

### World (`world.tscn` + `game/World.cs`)
- Spawns two `player/player_one.tscn` instances (class `PlayerOne`, named `Player1`/`Player2`) and one `takyan/takyan.tscn` (class `Takyan`, a `RigidBody2D`) at the `Spawns/*` markers.
- Has three spawn paths: `SpawnServerEntities`, `SpawnClientEntities`, and `SpawnOfflineEntities`.
- Arena: `Floor`, `LeftSide`, `RightSide`, and `TopSide` StaticBody2Ds. **Note:** `LeftSide` is the right wall and `RightSide` is the left wall.
- HUD: `CanvasLayer/ScoreUI` (`ui/score_ui.tscn`, `ui/ScoreUI.cs`). `World` also drives the indicator labels inside ScoreUI by node path.
- **A round restart reloads the entire `world.tscn`.** State that must survive a round lives in the autoloads (`RoundRestartPending`, `PreservedServer`, scores).

### Authority model
| | Server (`--server`) | Client | Offline (Local Battle) |
|---|---|---|---|
| Takyan physics | Simulates and broadcasts `RpcSyncState` every tick | Frozen kinematic puppet that interpolates | Simulates |
| Player movement | Accepts client position (`CmdSendPlayerState`) | Simulates own player and interpolates the remote one | Simulates both |
| Kicks | Applies `CmdSendKickAction` through `World.OnPlayerKickRequested` | Sends the RPC | `PlayerOne` sets the takyan velocity directly |
| Scoring and serve | `GameManager` + `ServeManager` | Mirrors state via `Authority` RPCs | Same code, called locally |

"Authority" means server **or** offline. The code expresses this as `!_net.IsClient`, `_offlineMode`, or `_net.IsServer || (!IsServer && !IsClient)`.

### Directory layout
```
art/          sprites (Idle/Kicking/Running/Takyan sheets), backgrounds, VCR_OSD_MONO font
sfx/          mp3 SFX, loaded at runtime by PlayerOne
game/         GameManager, ServeManager, World, Camera/GameCamera (plain C# helper)
networking/   NetworkManager, RoomManager, Room
player/       PlayerOne.cs + player_one.tscn (used for BOTH players)
takyan/       Takyan.cs + takyan.tscn
ui/           menus, lobby, HUD, how-to-play (test.tscn = title screen)
TitleScreen.cs  title-screen script (lives at the root)
```
There are no external packages beyond the Godot SDK, and no persistence, accounts, or save files.

---

## 3. Development Environment

- **Required:** Godot **4.7 stable, .NET (mono) build**, and the .NET SDK 8 or newer. The standard non-.NET Godot build cannot open the project.
- **First open:** open the folder in the Godot editor once so `.godot/` is generated and the C# assembly builds.
- **Compile check without the editor:** `dotnet build Takyan.csproj`. NuGet restores `Godot.NET.Sdk`, so the build works without Godot installed. This only compiles the C#; it does not validate scenes.
- **Run a dedicated server:** `<Godot_mono_console.exe> --headless --server --path <repo>`. See README. `StartServer.bat` contains a developer-specific path; do not rely on it.
- **Run clients:** press F5 in the editor. ⚠ `project.godot` currently sets `editor/run/main_run_args="--server"`, so editor-launched instances also try to host (ROADMAP TK-006). When testing on one machine, start the headless server **first** so port 7777 is taken.
- **Tests:** none exist yet (ROADMAP TK-020). **CI:** none (TK-021). **Export presets:** none (TK-036).
- **Environment variables or secrets:** none are required. Never add any to tracked files.
- **Debugging:** logs go to stdout through `GD.Print` and `NetworkManager.Log(component, msg)`, which adds a timestamp. Use the Godot Debugger → Monitors for FPS, physics, and network data.

---

## 4. Coding Standards

Match the existing code:

- **C# style:** tab indentation. One `public partial class X : GodotType` per file, with the file named after the class. PascalCase for public members, `_camelCase` for private fields.
  - Networking and UI files put opening braces on the same line. Gameplay files (`PlayerOne`, `Takyan`, `World`, `ServeManager`) use the next line. **Follow the style of the file you are editing.**
- **Section banners** such as `// ── Signals ──────` are used in `networking/` and `ui/`. Keep them when you add members to those files.
- **Godot integration:**
  - Tunables are `[Export]` fields.
  - Events are `[Signal] delegate void XEventHandler(...)`, subscribed with `+=` in `_Ready` and **unsubscribed with `-=` in `_ExitTree`**. This is MUST whenever the emitter is an autoload, because autoloads outlive scenes.
  - UI nodes are fetched with `%UniqueName` (`unique_name_in_owner`).
  - Autoloads are fetched with `GetNode<T>("/root/Name")`.
- **RPC naming:**
  - `Cmd*` / `Request*` are client → server, `AnyPeer`.
  - `Rpc*` are server → client, `Authority`.
  - Always pass the method name with `nameof(...)`.
- **Phases and states:** use the enums (`ServePhase`, `GameState`, `RoomState`). Do not add new magic numbers such as `phase == 2`; existing ones are tracked in TK-014.
- **Nullability:** nullable reference types are off. Use `GetNodeOrNull` plus null checks for optional nodes. Use `IsInstanceValid(obj)`, not `?.`, for Godot objects that may have been freed.
- **Logging:** use `NetworkManager.Log("Component", "...")` for gameplay and network events, and `GD.PrintErr` for errors. Do not add per-frame logging.
- **Comments:** short and explaining *why*. Update or remove comments that your change makes false. Some existing comments are already wrong, for example `World.ServerResetForServe`.

---

## 5. Game Architecture Rules

- **MUST** keep gameplay authority on the server/offline side. Clients send *requests* (`Cmd*`/`Request*`) and render state received via `Rpc*`. New gameplay decisions (scoring, kick validity, phase changes) **MUST NOT** be computed only on the client.
- **MUST** derive the acting player on the server from `Multiplayer.GetRemoteSenderId()` → `RoomManager.GetRoomForPeer(...)`. Never use a client-supplied player number or the target node's `PlayerNumber` alone. Existing code violates this (TK-010/011); do not copy that pattern.
- **MUST** put state that has to survive a round restart (a `world.tscn` reload) in `GameManager` or `ServeManager`, **and** reset it at match boundaries (TK-002). When you add a field to an autoload, add it to the reset path.
- **MUST NOT** call `ChangeSceneToFile("res://world.tscn")` on the server from new code paths without considering other rooms. The server has one World (TK-001).
- **SHOULD** send server → client RPCs only to the two peers of the match, not with a global `Rpc(...)` broadcast (TK-007).
- **Gameplay → UI:** emit a signal from the autoload or `ServeManager`, and have UI scripts (`ScoreUI`, `World` indicator handlers) subscribe. UI scripts call gameplay only through `Cmd*` RPCs or public `GameManager` methods.
- **New systems:** prefer a child node of `GameManager` (as `ServeManager` is) for match-wide logic, or a node inside `world.tscn` for per-round logic. Avoid adding new autoloads without a clear need.
- **Scenes:** one `player_one.tscn` serves both players. `PlayerNumber` selects the `Player1`/`Player2` sprite. Do not fork it into per-player scenes.
- **Avoid:** new `async void` flows without a cancellation or staleness guard (see TK-008/TK-024), string-matching node names for gameplay (for example `"Floor"`), and polling with `GetNodeOrNull` inside `_PhysicsProcess`.

---

## 6. Gameplay Development Guidelines

- **Movement:** `PlayerOne.LocalPhysicsProcess` is a `CharacterBody2D` with `MoveAndSlide`, project gravity, `Speed`, `JumpVelocity`, and Dash (`DashSpeed`/`DashDuration`/`DashCooldown`). Movement is frozen during `Waiting` and `Tossing`. Use `delta` for all timers.
- **Input:** use InputMap actions only (`move_left`, `move_right`, `jump`, `kick`, plus `_p2` variants for Local Battle P2), selected through the `_moveLeftAction`-style properties. Online, both players use the P1 bindings (WASD + Space). **Do not** read raw keys; the hardcoded Alt dash is a known bug (TK-005).
- **Kicks:** charge while held (`MinKickForce` → `MaxKickForce` at `ChargeSpeed`), release to kick, respect `KickCooldown`. A kick only connects if the takyan is in the `KickZone` Area2D (mask 2; the takyan is on layer 2).
  - Mega kick = the 5th serve kick, or any rally kick.
  - Online kicks are applied in `World.OnPlayerKickRequested`. Offline kicks are applied in `PlayerOne.HandleKickInput`. These currently differ (TK-013). Change both, or unify them; do not let them drift further.
- **Takyan:** a `RigidBody2D` frozen until `ApplyKick`. `OnBodyEntered` with `Floor` calls `ServeManager.TakyanHitGround`. Clients never simulate it.
- **Animation:** `AnimatedSprite2D` animations `default`, `run`, `jump`, and `kick`. Kick ends through `AnimationFinished`. Remote players get the animation name over the network.
- **Serve and scoring rules:** live only in `ServeManager.TakyanHitGround` and `ServerKick`, plus `GameManager.AwardPoint`. Change rules there, update `ui/howtoplay.tscn` and README in the same PR, and **get explicit approval before changing game rules**.
- **Save/load and progression:** none exist. If you add settings persistence, use `ConfigFile` under `user://` and treat its contents as untrusted.

---

## 7. Asset Management Guidelines

- **MUST NOT** rename, move, or delete assets, scenes, or scripts arbitrarily. Godot references them by `uid://` and `res://` path in `.tscn` and `.import` files. If a move is required, do it in the Godot editor's FileSystem dock so references update, and commit the `.import` and `.uid` files with it.
- Always commit `*.import` and `*.cs.uid` files alongside their sources. Never commit `.godot/`.
- Sprites are 64×64-frame horizontal sheets: `art/Idle/Idle.png` and `P2Idle.png`, and the equivalents in `Kicking/` and `Running/`. The takyan uses 16×16 frames. The font is `art/VCR_OSD_MONO_1.001.ttf`. Put new art under `art/<Category>/` and new SFX under `sfx/`.
- Reference textures as `ext_resource` to the source file. **Never** use a `CompressedTexture2D` with `load_path` into `.godot/imported/`; three UI scenes do this today (TK-023).
- Several root-level and `art/` files appear unused (ROADMAP TK-032). Do not delete them without confirmation.
- Edit `.tscn` files in the Godot editor when possible. If you must hand-edit one, keep `uid`/`unique_id` values intact and reopen the scene in the editor to verify it.

---

## 8. Performance Guidelines

- No per-tick `GetNode`/`GetNodeOrNull`, `GetChildren()` scans, LINQ, or `new List<>` in `_PhysicsProcess` or `_Process`. Cache references in `_Ready`.
- Network state is currently sent every physics tick, unreliable, for each player and for the takyan. Do not add more per-tick RPCs. Use `Reliable` only for discrete events.
- Prefer configuring nodes in the `.tscn` over creating them in code each spawn. `PlayerOne` builds labels and five `AudioStreamPlayer`s at runtime; do not extend that pattern.
- Large backgrounds are lossless 3200×1800. Size new textures to their on-screen use.
- Do not claim performance improvements without Godot Monitor or profiler numbers.

---

## 9. Security Guidelines

The attack surface is the dedicated server's RPC interface. Treat every `AnyPeer` RPC as untrusted input.

- **MUST** start every `AnyPeer` RPC with `if (!_net.IsServer) return;`, then validate the sender's identity and room membership, the current phase or state, and value ranges (including NaN and infinity) before acting.
- **MUST NOT** add `AnyPeer` RPCs that change client-side state. `PlayerOne.RpcTeleport` is a known violation (TK-012).
- **MUST NOT** commit credentials, personal paths, or private IPs. `MainMenu.cs` currently defaults to a private VPN IP (TK-030).
- **MUST NOT** weaken existing checks: room-full and in-progress checks, the double-kick rejection, and `Authority` modes on server → client RPCs.
- If you add file I/O, use `user://` only, never deserialize into arbitrary types, and handle corrupt files gracefully.
- Known open findings SEC-01 through SEC-08 are in ROADMAP §2. Reference them in related PRs.

---

## 10. Testing & Validation

Before reporting a task as done, an agent **MUST**:
1. Run `dotnet build Takyan.csproj` and confirm it passes. Run `dotnet test` once tests exist.
2. If Godot is available, run the relevant manual checks from ROADMAP §7. The **Online smoke test** applies to any change in `networking/`, `game/`, `player/`, or `takyan/`. The **Local smoke test** applies to any gameplay change.
3. Check both **Online and Local Battle** for gameplay changes, plus the scene transitions affected (menu ↔ lobby ↔ world ↔ round restart ↔ rematch).
4. Check edge cases relevant to the change: a disconnect mid-match, a second match in the same session, and a kick at the exact moment the takyan lands.
5. **Explicitly report any check that could not be run.** For example: "Godot not installed — only `dotnet build` verified." Never claim gameplay was tested if it was not.

---

## 11. AI Agent Development Workflow

1. Read the task, its ROADMAP entry, and its acceptance criteria.
2. Inspect the relevant scripts **and** scenes. Behaviour is often configured in `.tscn` overrides, for example `MaxKickForce` in `world.tscn` and `DashSpeed` in `player_one.tscn`.
3. Trace both the server/offline path and the client path before changing networked code.
4. List likely side effects: other modes, round-restart reloads, autoload state persistence.
5. For changes to networking, game state, or rules, write a short plan first. Simple, local fixes can proceed directly.
6. Implement the smallest change that meets the acceptance criteria. Preserve existing gameplay feel and rules unless the task says otherwise.
7. Validate per §10.
8. Update ROADMAP.md (tick the task or add new findings), README, and How To Play as needed.
9. Write the completion report (§14).

---

## 12. Repository Safety Rules

Without explicit authorization, agents **MUST NOT**:
- delete or replace scenes, scripts, or assets, including ones that look unused;
- replace or rewrite working gameplay systems, or change game rules or tuning values;
- change the Godot or .NET SDK version (`Takyan.csproj`, `config/features`);
- change project-wide settings in `project.godot` (input map, autoloads, rendering, run args) beyond what a task requires;
- add NuGet packages, Godot addons, or other dependencies;
- rename `PlayerOne`, autoloads, or node names that are used in `GetNode` paths or RPC paths (`Player1`, `Player2`, `Takyan`, `ScoreUI` children);
- force-push, rewrite history, or overwrite uncommitted user changes;
- touch any deployed server or external service.

---

## 13. Documentation Maintenance

| Change | Update |
|---|---|
| Setup, run, server, or port instructions | `README.md` |
| Task completed, added, or reprioritized | `ROADMAP.md` (tick the box; never tick unverified work) |
| Controls or rules | `ui/howtoplay.tscn` text **and** `README.md` |
| Architecture, autoloads, authority model, or conventions | This file |

---

## 14. Completion Report

Every finished task ends with:
- **Summary:** what changed and why (with the ROADMAP ID).
- **Files modified.**
- **Systems affected:** Online, Local, server, UI, scenes.
- **Key decisions:** including anything that needs team confirmation.
- **Validation performed:** the exact commands and manual checks, with results.
- **Not validated:** checks that could not be run, and why.
- **Known limitations and remaining issues:** new ROADMAP tasks if any.
