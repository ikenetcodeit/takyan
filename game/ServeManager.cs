using Godot;
using System;

public enum ServePhase { Waiting, Tossing, Juggling, Rally }

public partial class ServeManager : Node
{
	[Signal] public delegate void PhaseChangedEventHandler(int phase);
	[Signal] public delegate void TossCountdownUpdatedEventHandler(int secondsLeft);
	[Signal] public delegate void ServerChangedEventHandler(int server);
	[Signal] public delegate void PointScoredEventHandler(int player);
	[Signal] public delegate void TakyanTossedEventHandler();

	public ServePhase Phase { get; private set; } = ServePhase.Waiting;

	private int server = 1;
	private int receiver = 2;

	private int serveKicks = 0;
	private const int RequiredServeKicks = 5;

	private bool ballSpawned = false;
	private NetworkManager _net;

	// Guard: once a round result is determined, nothing else can fire until scene reloads
	private bool _roundEnded = false;

	public override void _Ready()
	{
		_net = GetNode<NetworkManager>("/root/NetworkManager");
	}

	public void ResetState()
	{
		server = 1;
		receiver = 2;
		serveKicks = 0;
		Phase = ServePhase.Waiting;
		ballSpawned = false;
		_roundEnded = false;
	}

	// ── Called by GameManager to begin a serve sequence ──────────────────────
	public async void StartServe()
	{
		_roundEnded = false;
		serveKicks = 0;
		ballSpawned = false;
		Phase = ServePhase.Tossing;

		bool isServerOrOffline = _net.IsServer || (!_net.IsServer && !_net.IsClient);

		if (_net.IsServer)
			Rpc(nameof(RpcSyncPhase), (int)Phase);
		else if (isServerOrOffline)
			EmitSignal(SignalName.PhaseChanged, (int)Phase);

		NetworkManager.Log("ServeManager", $"Toss countdown starting. Server=P{server}, Tosser=P{receiver}.");

		if (isServerOrOffline)
		{
			// 3-second countdown before auto-toss
			for (int i = 3; i > 0; i--)
			{
				if (_roundEnded) return; // abort if round ended mid-countdown
				if (_net.IsServer)
					Rpc(nameof(RpcUpdateTossCountdown), i);
				else
					EmitSignal(SignalName.TossCountdownUpdated, i);

				await ToSignal(GetTree().CreateTimer(1.0), "timeout");
				if (_roundEnded || Phase != ServePhase.Tossing) return; // abort
			}

			if (_roundEnded) return;

			// Hide countdown label, then auto-toss
			if (_net.IsServer)
				Rpc(nameof(RpcUpdateTossCountdown), 0);
			else
				EmitSignal(SignalName.TossCountdownUpdated, 0);

			Phase = ServePhase.Juggling;
			if (_net.IsServer)
				Rpc(nameof(RpcSyncPhase), (int)Phase);
			else
				EmitSignal(SignalName.PhaseChanged, (int)Phase);

			EmitSignal(SignalName.TakyanTossed);
		}
	}

	// ── Called on server when the serving player kicks ───────────────────────
	public void ServerKick(int player)
	{
		if (_roundEnded) return;
		if (Phase != ServePhase.Juggling || player != server) return;

		serveKicks++;
		NetworkManager.Log("ServeManager", $"Serve kick {serveKicks}/{RequiredServeKicks}");

		if (serveKicks >= RequiredServeKicks)
		{
			Phase = ServePhase.Rally;
			if (_net.IsServer)
				Rpc(nameof(RpcSyncPhase), (int)Phase);
			else
				EmitSignal(SignalName.PhaseChanged, (int)Phase);

			NetworkManager.Log("ServeManager", "RALLY STARTED!");
		}
	}

	// ── Called by Takyan when it hits the ground ─────────────────────────────
	public void TakyanHitGround(int lastPlayerWhoTouched)
	{
		// Hard guard: ignore all subsequent bounces after the round is decided
		if (_roundEnded) return;

		if (Phase == ServePhase.Waiting)
		{
			// Nothing to do yet
			return;
		}

		if (Phase == ServePhase.Tossing)
		{
			// Ball fell before toss completed — do nothing, countdown will re-toss
			return;
		}

		if (Phase == ServePhase.Juggling)
		{
			// Server failed to juggle 5 times — receiver gets the point and becomes server
			_roundEnded = true;

			int winner = receiver;
			NetworkManager.Log("ServeManager", $"Server dropped it! P{winner} gets the point and becomes server.");

			server = winner;
			receiver = (server == 1) ? 2 : 1;

			if (_net.IsServer)
				Rpc(nameof(RpcSyncRoles), server, receiver);

			EmitSignal(SignalName.ServerChanged, server);

			Phase = ServePhase.Waiting;
			if (_net.IsServer)
				Rpc(nameof(RpcSyncPhase), (int)Phase);
			else
				EmitSignal(SignalName.PhaseChanged, (int)Phase);

			EmitSignal(SignalName.PointScored, winner); // GameManager handles score + restart
			return;
		}

		if (Phase == ServePhase.Rally)
		{
			// The takyan hit the floor. It was the OTHER player's turn to
			// return it and they failed to, so the last player who
			// successfully hit it wins the point.
			_roundEnded = true;

			int winner = (lastPlayerWhoTouched == 0) ? server : lastPlayerWhoTouched;
			NetworkManager.Log("ServeManager", $"P{winner} returned it last, P{winner} scores!");

			server = winner;
			receiver = (server == 1) ? 2 : 1;

			if (_net.IsServer)
				Rpc(nameof(RpcSyncRoles), server, receiver);

			EmitSignal(SignalName.ServerChanged, server);

			Phase = ServePhase.Waiting;
			if (_net.IsServer)
				Rpc(nameof(RpcSyncPhase), (int)Phase);
			else
				EmitSignal(SignalName.PhaseChanged, (int)Phase);

			EmitSignal(SignalName.PointScored, winner); // GameManager handles score + restart
		}
	}

	public int GetServer() => server;
	public int GetReceiver() => receiver;
	public bool IsRallyStarted() => Phase == ServePhase.Rally;
	public bool IsBallSpawned() => ballSpawned;
	public void MarkBallSpawned() => ballSpawned = true;

	public void SetServer(int s)
	{
		server = s;
		receiver = (s == 1) ? 2 : 1;
		EmitSignal(SignalName.ServerChanged, server);
	}

	// ── RPCs ─────────────────────────────────────────────────────────────────

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncRoles(int newServer, int newReceiver)
	{
		server = newServer;
		receiver = newReceiver;
		EmitSignal(SignalName.ServerChanged, server);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void RpcSyncPhase(int newPhase)
	{
		Phase = (ServePhase)newPhase;
		EmitSignal(SignalName.PhaseChanged, newPhase);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcUpdateTossCountdown(int secondsLeft)
	{
		EmitSignal(SignalName.TossCountdownUpdated, secondsLeft);
	}
}
