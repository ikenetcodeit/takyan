using Godot;
using System;
using System.Linq;
using System.Text;

/// <summary>
/// Autoload singleton. Manages ENet connection lifecycle and local player identity.
/// Both server and client use this node; behaviour differs based on role.
/// </summary>
public partial class NetworkManager : Node {

	// ── Signals ──────────────────────────────────────────────────────────────

	[Signal] public delegate void ConnectedToServerEventHandler();
	[Signal] public delegate void ConnectionFailedEventHandler(string reason);
	[Signal] public delegate void ServerStartedEventHandler();
	[Signal] public delegate void PeerConnectedEventHandler(long peerId);
	[Signal] public delegate void PeerDisconnectedEventHandler(long peerId);

	// ── Constants ─────────────────────────────────────────────────────────────

	public const int DefaultPort = 7777;
	public const int MaxClients  = 64; // server can hold many rooms; 2 players per room

	// ── State ─────────────────────────────────────────────────────────────────

	public enum NetworkRole { None, Server, Client }

	public NetworkRole Role        { get; private set; } = NetworkRole.None;
	public bool        IsServer    => Role == NetworkRole.Server;
	public bool        IsClient    => Role == NetworkRole.Client;

	/// Player number assigned by the server after joining a room (1 or 2). -1 = not yet assigned.
	public int  LocalPlayerNumber  { get; set; } = -1;

	/// The room code the local client is currently in.
	public string CurrentRoomId    { get; set; } = "";

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready() {
		// Wire up Godot's multiplayer signals so we can re-emit them as our own.
		Multiplayer.ConnectedToServer      += OnConnectedToServer;
		Multiplayer.ConnectionFailed       += OnConnectionFailed;
		Multiplayer.PeerConnected          += OnPeerConnected;
		Multiplayer.PeerDisconnected       += OnPeerDisconnected;

		// Headless server mode check
		if (System.Environment.GetCommandLineArgs().Contains("--server")) {
			GD.Print("[NetworkManager] --server flag detected. Starting Dedicated Server.");
			StartServer();
			// StartServer doesn't change scenes, so we just run in the background.
		}
	}

	// ── Public API ────────────────────────────────────────────────────────────

	/// <summary>Start an ENet server on the given port.</summary>
	public Error StartServer(int port = DefaultPort) {
		var peer = new ENetMultiplayerPeer();
		Error err = peer.CreateServer(port, MaxClients);

		if (err != Error.Ok) {
			GD.PrintErr($"[NetworkManager] Failed to start server on port {port}: {err}");
			return err;
		}

		Multiplayer.MultiplayerPeer = peer;
		Role = NetworkRole.Server;

		GD.Print($"[NetworkManager] Server started on port {port}.");
		EmitSignal(SignalName.ServerStarted);
		return Error.Ok;
	}

	/// <summary>Connect as a client to the given host and port.</summary>
	public Error ConnectToServer(string host, int port = DefaultPort) {
		var peer = new ENetMultiplayerPeer();
		Error err = peer.CreateClient(host, port);

		if (err != Error.Ok) {
			GD.PrintErr($"[NetworkManager] Failed to connect to {host}:{port} — {err}");
			return err;
		}

		Multiplayer.MultiplayerPeer = peer;
		Role = NetworkRole.Client;

		GD.Print($"[NetworkManager] Connecting to {host}:{port}…");
		return Error.Ok;
	}

	/// <summary>Disconnect and reset state.</summary>
	public void Disconnect() {
		if (Multiplayer.MultiplayerPeer != null) {
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}
		Role              = NetworkRole.None;
		LocalPlayerNumber = -1;
		CurrentRoomId     = "";
		GD.Print("[NetworkManager] Disconnected.");
	}

	/// <summary>Unique peer ID of this instance. 1 = server, >1 = clients.</summary>
	public long GetMyPeerId() => Multiplayer.GetUniqueId();

	/// <summary>Structured server log with timestamp and component prefix.</summary>
	public static void Log(string component, string message)
	{
		var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
		GD.Print($"[{timestamp}] [{component}] {message}");
	}

	// ── Multiplayer signal handlers ───────────────────────────────────────────

	private void OnConnectedToServer() {
		GD.Print($"[NetworkManager] Connected to server. My peer ID: {GetMyPeerId()}");
		EmitSignal(SignalName.ConnectedToServer);
	}

	private void OnConnectionFailed() {
		GD.PrintErr("[NetworkManager] Connection failed.");
		Role = NetworkRole.None;
		EmitSignal(SignalName.ConnectionFailed, "Could not reach server.");
	}

	private void OnPeerConnected(long id) {
		GD.Print($"[NetworkManager] Peer connected: {id}");
		EmitSignal(SignalName.PeerConnected, id);
	}

	private void OnPeerDisconnected(long id) {
		GD.Print($"[NetworkManager] Peer disconnected: {id}");
		EmitSignal(SignalName.PeerDisconnected, id);
	}
}
