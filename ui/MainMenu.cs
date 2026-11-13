using Godot;
using System;

/// <summary>
/// Main menu screen. Lets the player enter server details and either
/// create a new room or join an existing one by code.
/// </summary>
public partial class MainMenu : Control {

	// ── Node references (wired in .tscn via unique names %) ───────────────────

	private LineEdit _hostInput;
	private LineEdit _portInput;
	private LineEdit _joinCodeInput;
	private Button   _createBtn;
	private Button   _joinBtn;
	private Button   _localBattleBtn;
	private Button   _backBtn;
	private Label    _statusLabel;

	// ── Cached singletons ─────────────────────────────────────────────────────

	private NetworkManager _net;
	private RoomManager    _roomManager;

	// ── Pending action ────────────────────────────────────────────────────────

	private enum PendingAction { None, Create, Join }
	private PendingAction _pendingAction   = PendingAction.None;
	private string        _pendingRoomCode = "";

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready() {
		_net         = GetNode<NetworkManager>("/root/NetworkManager");
		_roomManager = GetNode<RoomManager>("/root/RoomManager");

		_hostInput     = GetNode<LineEdit>("%HostInput");
		_portInput     = GetNode<LineEdit>("%PortInput");
		_joinCodeInput = GetNode<LineEdit>("%JoinCodeInput");
		_createBtn     = GetNode<Button>("%CreateBtn");
		_joinBtn       = GetNode<Button>("%JoinBtn");
		_localBattleBtn = GetNode<Button>("%LocalBattleBtn");
		_backBtn       = GetNode<Button>("%BackBtn");
		_statusLabel   = GetNode<Label>("%StatusLabel");

		_hostInput.Text = "26.205.78.70";
		_portInput.Text = NetworkManager.DefaultPort.ToString();

		_createBtn.Pressed       += OnCreatePressed;
		_joinBtn.Pressed         += OnJoinPressed;
		_localBattleBtn.Pressed  += OnLocalBattlePressed;
		_backBtn.Pressed         += OnBackPressed;

		_net.ConnectedToServer += OnConnectedToServer;
		_net.ConnectionFailed  += OnConnectionFailed;

		_roomManager.RoomCreatedSignal += OnRoomCreated;
		_roomManager.JoinResultSignal  += OnJoinResult;

		SetStatus("");
	}

	public override void _ExitTree() {
		if (_net != null) {
			_net.ConnectedToServer -= OnConnectedToServer;
			_net.ConnectionFailed  -= OnConnectionFailed;
		}
		if (_roomManager != null) {
			_roomManager.RoomCreatedSignal -= OnRoomCreated;
			_roomManager.JoinResultSignal  -= OnJoinResult;
		}
	}

	// ── Button handlers ───────────────────────────────────────────────────────

	private void OnCreatePressed() {
		if (!ValidateInputs()) return;
		_pendingAction = PendingAction.Create;
		SetStatus("Connecting…");
		SetButtonsEnabled(false);
		ConnectToServer();
	}

	private void OnJoinPressed() {
		string code = _joinCodeInput.Text.Trim().ToUpperInvariant();
		if (code.Length == 0) { SetStatus("Enter a room code to join."); return; }
		if (!ValidateInputs()) return;
		_pendingAction   = PendingAction.Join;
		_pendingRoomCode = code;
		SetStatus("Connecting…");
		SetButtonsEnabled(false);
		ConnectToServer();
	}

	private void OnLocalBattlePressed() {
		_net.Disconnect();
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void OnBackPressed() {
		_net.Disconnect();
		GetTree().ChangeSceneToFile("res://ui/test.tscn");
	}

	// ── Networking ────────────────────────────────────────────────────────────

	private void ConnectToServer() {
		string host = _hostInput.Text.Trim();
		int    port  = int.TryParse(_portInput.Text.Trim(), out int p) ? p : NetworkManager.DefaultPort;
		Error  err   = _net.ConnectToServer(host, port);
		if (err != Error.Ok) {
			SetStatus($"Connection error: {err}");
			SetButtonsEnabled(true);
			_pendingAction = PendingAction.None;
		}
	}

	private void OnConnectedToServer() {
		SetStatus("Connected. Setting up room…");
		switch (_pendingAction) {
			case PendingAction.Create:
				_roomManager.RpcId(1, nameof(RoomManager.RequestCreateRoom));
				break;
			case PendingAction.Join:
				_roomManager.RpcId(1, nameof(RoomManager.RequestJoinRoom), _pendingRoomCode);
				break;
		}
	}

	private void OnConnectionFailed(string reason) {
		SetStatus($"Could not connect: {reason}");
		SetButtonsEnabled(true);
		_pendingAction = PendingAction.None;
	}

	// ── RoomManager signal handlers ───────────────────────────────────────────

	private void OnRoomCreated(string roomId) {
		_net.CurrentRoomId     = roomId;
		_net.LocalPlayerNumber = 1;
		GetTree().ChangeSceneToFile("res://ui/lobby.tscn");
	}

	private void OnJoinResult(bool success, string reason) {
		if (!success) {
			SetStatus($"Join failed: {reason}");
			SetButtonsEnabled(true);
			_net.Disconnect();
			_pendingAction = PendingAction.None;
			return;
		}
		// Player number arrives via StartMatch in Lobby
		GetTree().ChangeSceneToFile("res://ui/lobby.tscn");
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private bool ValidateInputs() {
		if (_hostInput.Text.Trim().Length == 0) { SetStatus("Enter a server address."); return false; }
		if (!int.TryParse(_portInput.Text.Trim(), out int port) || port < 1 || port > 65535) {
			SetStatus("Enter a valid port (1–65535).");
			return false;
		}
		return true;
	}

	private void SetStatus(string msg)          => _statusLabel.Text = msg;
	private void SetButtonsEnabled(bool enabled) {
		_createBtn.Disabled      = !enabled;
		_joinBtn.Disabled        = !enabled;
		_localBattleBtn.Disabled = !enabled;
	}
}
