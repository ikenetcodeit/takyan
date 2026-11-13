# Takyan

A 2-player competitive kick-ball game built with Godot 4.7 + C#.

---

## 🎮 How to Run Multiplayer (Dedicated Server)

This guide walks through running a dedicated server from the command line and connecting two Godot editor instances as Player 1 and Player 2.

### Prerequisites

- Godot 4.7 **.NET** version installed.
- The project opened in the Godot editor at least once so the `.godot` folder is generated.
- Both player machines on the same network (for LAN play), or proper port forwarding / VPN for internet play.

---

### Step 1: Find the Godot Executable

You need the **Godot console executable** to run the dedicated server. It is typically located at:

| Platform | Typical Path |
|---|---|
| **Windows** | `C:\Program Files\Godot_4.7\Godot_v4.7-stable_mono_win64.exe` |
| **macOS** | `/Applications/Godot_mono.app/Contents/MacOS/Godot` |
| **Linux** | `/usr/local/bin/godot` or wherever you extracted it |

> ⚠️ Make sure you are using the **.NET / Mono** version of Godot (the filename says `_mono_`). The standard version will not work with C# projects.

---

### Step 2: Start the Dedicated Server

Open a **terminal / command prompt** and run Godot with the `--headless` and `--server` flags. Point it at the project folder:

```bash
"path/to/Godot_v4.7-stable_mono_win64.exe" --headless --server --path "C:\path\to\Takyan"
```

Example (Any location):
```bash
"D:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --headless --server --path "D:\C# Projects\Takyan"
```

Example (At the root project):
```bash
"D:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe" --headless --server
```

**What this does:**
- `--headless` — Runs Godot without a window. No graphics, no audio.
- `--server` — Detected by `NetworkManager.cs` to start an ENet server on UDP port **7777**.
- `--path` — Points to the project root folder.

You should see log output like:

```
[NetworkManager] --server flag detected. Starting Dedicated Server.
[NetworkManager] Server started on port 7777.
```

Keep this terminal window open — the server is now running and waiting for players to join.

---

### Step 3: Open the Godot Editor (Player 1)

1. Open the **Godot editor** (the same `.exe` but without `--headless`).
2. Load the Takyan project.
3. Press **F5** (or click **Play > Play the project** in the top right).
4. In the Main Menu, you will see the server address and port already filled in (`127.0.0.1` : `7777` — this is correct because the dedicated server is running locally).
5. Click **Create Room**.
6. You will be taken to the Lobby screen. A 4-character **room code** will be displayed in green (e.g., `A3X9`). Share this code with Player 2.

---

### Step 4: Open the Godot Editor (Player 2)

1. On the **other machine** (or a second editor instance on the same machine for testing), open another Godot editor window.
2. Load the same Takyan project.
3. Press **F5** to run the project.
4. In the Main Menu:
   - **Address**: Enter the **dedicated server machine's IP address**.
     - On a local network, find it via `ipconfig` (Windows) or `ip addr` (Linux).
     - Example: `192.168.1.100`
   - **Port**: Must be `7777`.
   - **Code**: Enter the room code from Player 1 (e.g., `A3X9`).
5. Click **Join Room**.

---

### Step 5: Play!

- As soon as Player 2 joins, the server starts the match.
- Both players will automatically transition from the Lobby to the game world.
- A **3-2-1-Go** countdown plays on both screens.
- Player 1 serves first — kick the Takyan **5 times** to start the rally.
- First player to **5 points** wins the match.
- After a match, either player can click **Play Again** to request a rematch.

---

### Testing on a Single Machine

You can test the whole setup on one computer:

```bash
# Terminal 1: Start the dedicated server
"path/to/Godot_v4.7-stable_mono_win64.exe" --headless --server --path "C:\path\to\Takyan"
```

Then in the **Godot editor**, press **F5** twice to open two game windows. Use one as Player 1 (Create Room) and the other as Player 2 (Join Room with IP `127.0.0.1`).

---

### Troubleshooting

| Problem | Solution |
|---|---|
| Server says `Failed to start server on port 7777` | Another process is using port 7777. Change the port in `NetworkManager.cs` (`DefaultPort`) or close the conflicting process. |
| Player 2 cannot connect | Check the IP address. On a local network, use `ipconfig` on the server machine and use that IP. Try `ping <server-ip>` from Player 2's machine. |
| Connection timeout / refused | Firewall is blocking UDP port 7777. Add an inbound rule in Windows Firewall (or `ufw` on Linux) to allow UDP on port 7777. |
| Players don't see each other / match doesn't start | Ensure both players are using the **same build** of the project. Room codes are case-insensitive. |

---

### Port Forwarding (Internet Play)

To play over the internet (not just LAN), you need to forward UDP port 7777 on the server machine's router to its local IP:

1. Find the server machine's local IP: `ipconfig`
2. Log into the router admin panel (usually `192.168.1.1` or `192.168.0.1`)
3. Add a **Port Forwarding** rule:
   - **Protocol**: UDP
   - **External Port**: 7777
   - **Internal Port**: 7777
   - **Internal IP**: (server machine's local IP)
4. Player 2 enters the **public IP** of the server machine (search "what is my IP") in the Address field.

Alternatively, use a VPN like **Tailscale** or **ZeroTier** to avoid port forwarding entirely.
