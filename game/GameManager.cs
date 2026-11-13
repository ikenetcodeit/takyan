using Godot;
using System;

public enum GameState { Waiting, Countdown, Playing, MatchOver }

public partial class GameManager : Node {

	[Export] public int PointsToWin = 5;

	public GameState State { get; private set; } = GameState.Waiting;

	private int _p1Score = 0;
	private int _p2Score = 0;
	private bool _p1Ready = false;
	private bool _p2Ready = false;

	private ServeManager _serveManager;
	private NetworkManager _net;
	private RoomManager _roomManager;

	// Preserved between rounds (survives scene reload since GameManager is an Autoload)
	public bool RoundRestartPending { get; private set; } = false;
	public int PreservedServer { get; private set; } = 1;
	private bool _roundRestartSent = false;

	private bool _p1RematchReady = false;
	private bool _p2RematchReady = false;

	[Signal] public delegate void ScoreUpdatedEventHandler(int p1Score, int p2Score);
	[Signal] public delegate void MatchEndedEventHandler(int winnerPlayerNumber);
	[Signal] public delegate void CountdownUpdatedEventHandler(int secondsLeft);
	[Signal] public delegate void RematchReadyEventHandler();
	[Signal] public delegate void RematchStatusUpdatedEventHandler(bool p1Ready, bool p2Ready);
	[Signal] public delegate void RoundEndedAnnouncedEventHandler(int newServerNum);

	public override void _Ready() {
		_net = GetNode<NetworkManager>("/root/NetworkManager");
		_roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");

		_serveManager = new ServeManager();
		_serveManager.Name = "ServeManager";
		AddChild(_serveManager);
		_serveManager.PointScored += OnPointScored;

		NetworkManager.Log("GameManager", "Ready. Awaiting ServerStartGame...");
	}

	// ── Server-only: called once at the very start of a fresh match ───────────

	public void ServerStartGame() {
		if (_net.IsClient) return;

		_p1Score = 0;
		_p2Score = 0;
		_roundRestartSent = false;
		State = GameState.Waiting;

		NetworkManager.Log("GameManager", "Fresh match. Waiting for players to ready up...");
	}

	// ── Client ready handshake (fresh match only) ─────────────────────────────

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcClientReady() {
		if (!_net.IsServer) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		int playerNum = -1;

		if (_roomManager != null) {
			var room = _roomManager.GetRoomForPeer(senderId);
			if (room != null) playerNum = room.GetPlayerNumber(senderId);
		}

		if (playerNum == 1) _p1Ready = true;
		else if (playerNum == 2) _p2Ready = true;

		NetworkManager.Log("GameManager", $"Player {playerNum} ready. P1={_p1Ready}, P2={_p2Ready}");

		if (_p1Ready && _p2Ready)
			ServerStartServeSequence();
	}

	// ── Start the toss sequence (used by both fresh match and round restart) ───

	public void ServerStartServeSequence() {
		if (_net.IsClient) return;
		State = GameState.Playing;
		_serveManager.StartServe();
	}

	// ── Called by World after scene reload if RoundRestartPending ─────────────

	public void ServerBeginPlayRound() {
		if (_net.IsClient) return;
		RoundRestartPending = false;
		_roundRestartSent = false;
		State = GameState.Playing;
		Rpc(nameof(RpcUpdateScore), _p1Score, _p2Score);
		_serveManager.StartServe();
	}

	// ── Scoring ───────────────────────────────────────────────────────────────

	private void OnPointScored(int playerNumber) {
		AwardPoint(playerNumber);
	}

	public void AwardPoint(int playerNumber) {
		if (_net.IsClient) return;
		if (State != GameState.Playing) return;

		if (playerNumber == 1) _p1Score++;
		else if (playerNumber == 2) _p2Score++;

		NetworkManager.Log("GameManager", $"P{playerNumber} scored! Scores: {_p1Score}-{_p2Score}");

		if (_p1Score >= PointsToWin) {
			State = GameState.MatchOver;
			if (_net.IsServer)
				Rpc(nameof(RpcMatchEnded), 1);
			else
				RpcMatchEnded(1);
		}
		else if (_p2Score >= PointsToWin) {
			State = GameState.MatchOver;
			if (_net.IsServer)
				Rpc(nameof(RpcMatchEnded), 2);
			else
				RpcMatchEnded(2);
		}
		else {
			if (_net.IsServer)
				Rpc(nameof(RpcUpdateScore), _p1Score, _p2Score);
			else
				RpcUpdateScore(_p1Score, _p2Score);
			ServerRoundRestart();
		}
	}

	// ── Round Restart: show "ROUND ENDED" for 3s then reload world ────────────

	public void ServerRoundRestart() {
		if (_net.IsClient) return;
		if (_roundRestartSent) return; // already triggered, ignore duplicates
		_roundRestartSent = true;
		PreservedServer = _serveManager.GetServer();
		RoundRestartPending = true;
		if (_net.IsServer)
			Rpc(nameof(RpcRoundRestart), PreservedServer);
		else
			RpcRoundRestart(PreservedServer);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcRoundRestart(int serverNum) {
		PreservedServer = serverNum;
		RoundRestartPending = true;
		EmitSignal(SignalName.RoundEndedAnnounced, serverNum);
		// -1 signals "ROUND ENDED" to ScoreUI
		EmitSignal(SignalName.CountdownUpdated, -1);
		GetTree().CreateTimer(3.0).Timeout += () => {
			GetTree().ChangeSceneToFile("res://world.tscn");
		};
	}

	// ── RPCs: server → clients ────────────────────────────────────────────────

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcUpdateScore(int p1, int p2) {
		_p1Score = p1;
		_p2Score = p2;
		EmitSignal(SignalName.ScoreUpdated, p1, p2);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcMatchEnded(int winnerNumber) {
		State = GameState.MatchOver;
		EmitSignal(SignalName.MatchEnded, winnerNumber);
		NetworkManager.Log("GameManager", $"Match over — P{winnerNumber} wins!");
	}

	// ── Rematch ───────────────────────────────────────────────────────────────

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void CmdRequestRematch() {
		if (!_net.IsServer) return;
		if (State != GameState.MatchOver) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		int playerNum = -1;

		if (_roomManager != null) {
			var room = _roomManager.GetRoomForPeer(senderId);
			if (room != null) playerNum = room.GetPlayerNumber(senderId);
		}

		if (playerNum == 1) _p1RematchReady = true;
		else if (playerNum == 2) _p2RematchReady = true;

		NetworkManager.Log("GameManager", $"Player {playerNum} requested rematch. P1={_p1RematchReady} P2={_p2RematchReady}");
		Rpc(nameof(RpcUpdateRematchStatus), _p1RematchReady, _p2RematchReady);

		if (_p1RematchReady && _p2RematchReady) {
			_serveManager.ResetState();
			_p1RematchReady = false;
			_p2RematchReady = false;
			_p1Ready = false;
			_p2Ready = false;

			PreservedServer = _p1Score > _p2Score ? 2 : 1;
			_p1Score = 0;
			_p2Score = 0;
			RoundRestartPending = true;

			Rpc(nameof(RpcRematchAccepted));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcUpdateRematchStatus(bool p1Ready, bool p2Ready) {
		EmitSignal(SignalName.RematchStatusUpdated, p1Ready, p2Ready);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcRematchAccepted() {
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	public int GetPlayer1Score() => _p1Score;
	public int GetPlayer2Score() => _p2Score;
	public ServeManager GetServeManager() => _serveManager;
}
