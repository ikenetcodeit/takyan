using Godot;
using System;

/// <summary>
/// Lobby screen shown after connecting to the server.
/// - Host (Player 1): displays the room code for the other player to share.
/// - Joiner (Player 2): displays a "Waiting for host…" message.
/// Both transition to world.tscn once the server fires StartMatch.
/// </summary>
public partial class Lobby : Control {

	// ── Node references ───────────────────────────────────────────────────────

	private Label  _roomCodeLabel;
	private Label  _statusLabel;
	private Label  _player1Label;
	private Label  _player2Label;
	private Button _leaveBtn;

	// ── Singletons ────────────────────────────────────────────────────────────

	private NetworkManager _net;
	private RoomManager    _roomManager;

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready() {
		_net         = GetNode<NetworkManager>("/root/NetworkManager");
		_roomManager = GetNode<RoomManager>("/root/RoomManager");

		_roomCodeLabel = GetNode<Label>("%RoomCodeLabel");
		_statusLabel   = GetNode<Label>("%StatusLabel");
		_player1Label  = GetNode<Label>("%Player1Label");
		_player2Label  = GetNode<Label>("%Player2Label");
		_leaveBtn      = GetNode<Button>("%LeaveBtn");

		_leaveBtn.Pressed += OnLeavePressed;

		_roomManager.StartMatchSignal           += OnStartMatch;
		_roomManager.OpponentDisconnectedSignal += OnOpponentDisconnected;
		_net.PeerDisconnected                   += OnPeerDisconnected;

		RefreshUI();
	}

	public override void _ExitTree() {
		if (_roomManager != null) {
			_roomManager.StartMatchSignal           -= OnStartMatch;
			_roomManager.OpponentDisconnectedSignal -= OnOpponentDisconnected;
		}
		if (_net != null) {
			_net.PeerDisconnected -= OnPeerDisconnected;
		}
	}

	// ── UI ────────────────────────────────────────────────────────────────────

	private void RefreshUI() {
		bool isHost = _net.LocalPlayerNumber == 1;

		if (isHost && _net.CurrentRoomId.Length > 0) {
			// Host sees the room code to share
			_roomCodeLabel.Text = $"Room Code:  {_net.CurrentRoomId}";
			_statusLabel.Text   = "Waiting for Player 2…";
			_player1Label.Text  = "Player 1:  You  (ready)";
			_player2Label.Text  = "Player 2:  waiting…";
		} else {
			// Joiner sees a waiting message; player number arrives with StartMatch
			_roomCodeLabel.Text = "";
			_statusLabel.Text   = "Joined! Waiting for match to start…";
			_player1Label.Text  = "Player 1:  (host)";
			_player2Label.Text  = "Player 2:  You  (ready)";
		}
	}

	// ── Signal handlers ───────────────────────────────────────────────────────

	private void OnStartMatch(int playerNumber) {
		_net.LocalPlayerNumber = playerNumber;
		GD.Print($"[Lobby] Starting match as Player {playerNumber}.");

		// Both players load the game world
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void OnOpponentDisconnected() {
		_statusLabel.Text  = "Opponent disconnected. Waiting for a new player…";
		_player2Label.Text = "Player 2:  waiting…";

		// Reset to waiting state so a new player can join
		if (_net.LocalPlayerNumber == 2) {
			// We were Player 2 and the host left — return to menu
			ShowError("The host disconnected.");
			ReturnToMenu();
		}
	}

	private void OnPeerDisconnected(long peerId) {
		// If the server itself disconnects (peer 1), bail out
		if (peerId == 1) {
			ShowError("Lost connection to server.");
			ReturnToMenu();
		}
	}

	private void OnLeavePressed() {
		_net.Disconnect();
		ReturnToMenu();
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private void ShowError(string msg) => _statusLabel.Text = msg;

	private void ReturnToMenu() {
		_net.CurrentRoomId     = "";
		_net.LocalPlayerNumber = -1;
		GetTree().ChangeSceneToFile("res://ui/main_menu.tscn");
	}
}
