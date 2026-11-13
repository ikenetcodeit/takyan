using Godot;
using System;

public partial class World : Node2D
{
	[Export] public PackedScene PlayerScene;
	[Export] public PackedScene PlayerTwoScene;
	[Export] public PackedScene TakyanScene;

	private NetworkManager _net;
	private RoomManager _roomManager;
	private GameManager _gameManager;

	private Node2D _p1Spawn;
	private Node2D _p2Spawn;
	private Node2D _takyanSpawn;

	private Takyan _activeTakyan;
	private PlayerOne _player1;
	private PlayerOne _player2;
	private ServeManager _serveManager;

	[Export]
	public Vector2 TossSpawnOffset { get; set; } = new Vector2(0, -20);

	// ── World.tscn UI nodes ───────────────────────────────────────────────────
	private Label _playerServingIndicator;
	private Label _tossingCountdown;
	private Label _roundEndingIndicator;
	private Label _assigningPoints;
	private Label _matchEndedIndicator;
	private Label _rematchText;
	private Label _winnerText;

	public override void _Ready()
	{
		_net = GetNode<NetworkManager>("/root/NetworkManager");
		_roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
		_gameManager = GetNode<GameManager>("/root/GameManager");

		_p1Spawn = GetNode<Node2D>("Spawns/P1Spawn");
		_p2Spawn = GetNode<Node2D>("Spawns/P2Spawn");
		_takyanSpawn = GetNode<Node2D>("Spawns/TakyanSpawn");

		// Wire the world.tscn indicator nodes
		_playerServingIndicator = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/MarginContainer/playerServingIndicator");
		_tossingCountdown       = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/MarginContainer/tossingCountdown");
		_roundEndingIndicator   = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/MarginContainer/VBoxContainer2/roundEndingIndicator");
		_assigningPoints        = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/assigningPoints");
		_matchEndedIndicator    = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/MarginContainer/VBoxContainer/matchEnded Indicator");
		_rematchText            = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/MarginContainer/rematchText");
		_winnerText             = GetNodeOrNull<Label>("CanvasLayer/ScoreUI/MarginContainer/VBoxContainer/winnerText");
		HideAllIndicators();
		
		_serveManager = GetNodeOrNull<ServeManager>("/root/GameManager/ServeManager");
		if (_serveManager != null)
		{
			_serveManager.TakyanTossed         += OnTakyanTossed;
			_serveManager.ServerChanged        += OnServerChanged;
			_serveManager.TossCountdownUpdated += OnTossCountdownUpdated;
		}

		_gameManager.MatchEnded           += OnMatchEnded;
		_gameManager.RoundEndedAnnounced  += OnRoundEndedAnnounced;

		if (_net.IsServer)
		{
			var existingPlayer = GetNodeOrNull<PlayerOne>("player one");
			if (existingPlayer != null) existingPlayer.QueueFree();
			SpawnServerEntities();
		}
		else if (_net.IsClient)
		{
			var existingPlayer = GetNodeOrNull<PlayerOne>("player one");
			if (existingPlayer != null) existingPlayer.QueueFree();
			SpawnClientEntities();
		}
		else
		{
			var existingPlayer = GetNodeOrNull<PlayerOne>("player one");
			if (existingPlayer != null) existingPlayer.QueueFree();
			SpawnOfflineEntities();
		}
	}

	public override void _ExitTree()
	{
		if (_serveManager != null)
		{
			_serveManager.TakyanTossed         -= OnTakyanTossed;
			_serveManager.ServerChanged        -= OnServerChanged;
			_serveManager.TossCountdownUpdated -= OnTossCountdownUpdated;
		}

		if (_gameManager != null)
		{
			_gameManager.MatchEnded          -= OnMatchEnded;
			_gameManager.RoundEndedAnnounced -= OnRoundEndedAnnounced;
		}
	}

	// ── Indicator helpers ──────────────────────────────────────────────────────

	private void HideAllIndicators()
	{
		_playerServingIndicator?.Hide();
		_tossingCountdown?.Hide();
		_roundEndingIndicator?.Hide();
		_assigningPoints?.Hide();
		_matchEndedIndicator?.Hide();
		_rematchText?.Hide();
		_winnerText?.Hide();
	}

	private void SetIndicatorText(Label node, string text)
	{
		if (node == null) return;
		node.Text = text;
		node.Show();
	}

	private void OnServerChanged(int server)
	{
		int tosser = server == 1 ? 2 : 1;
		HideAllIndicators();
		SetIndicatorText(_playerServingIndicator, $"P{tosser} will toss to P{server}");
	}

	private void OnTossCountdownUpdated(int secondsLeft)
	{
		if (secondsLeft > 0) {
			_playerServingIndicator?.Hide();
			
			// Leave _tossingCountdown anchored to the center as set in the editor!

			SetIndicatorText(_tossingCountdown, $"Tossing in {secondsLeft}...");
		} else {
			SetIndicatorText(_tossingCountdown, "GO!");
			GetTree().CreateTimer(1.0).Timeout += () => _tossingCountdown?.Hide();
		}
	}

	private async void OnRoundEndedAnnounced(int newServerNum)
	{
		HideAllIndicators();
		SetIndicatorText(_roundEndingIndicator, "Round Ends");
		await ToSignal(GetTree().CreateTimer(1.0), "timeout");
		_roundEndingIndicator?.Hide();

		SetIndicatorText(_assigningPoints, $"P{newServerNum} gets a point!");
		int tosser = newServerNum == 1 ? 2 : 1;
		SetIndicatorText(_playerServingIndicator, $"Next: P{newServerNum} serves · P{tosser} tosses");
		await ToSignal(GetTree().CreateTimer(1.5), "timeout");
		_assigningPoints?.Hide();
		_playerServingIndicator?.Hide();
	}

	private void OnMatchEnded(int winner)
	{
		HideAllIndicators();
		SetIndicatorText(_winnerText, $"P{winner} won the match!");
	}

	private void SpawnPlayer(int number, Vector2 position, bool local, bool wireKick)
	{
		var scene = PlayerScene;
		var player = scene.Instantiate<PlayerOne>();
		player.Name = $"Player{number}";
		player.PlayerNumber = number;
		player.IsLocal = local;
		player.Scale = new Vector2(2.5f, 2.5f);
		if (wireKick)
			player.ServerKickRequested += OnPlayerKickRequested;
		AddChild(player);
		player.GlobalPosition = position; // Set position after AddChild to ensure Godot physics/transform doesn't reset it to (0,0)

		if (number == 1) _player1 = player;
		else _player2 = player;
	}

	private void SpawnTakyan(Vector2 position)
	{
		_activeTakyan = TakyanScene.Instantiate<Takyan>();
		_activeTakyan.Name = "Takyan";
		_activeTakyan.GlobalPosition = position;
		AddChild(_activeTakyan);
	}

	private void SpawnServerEntities()
	{
		SpawnPlayer(1, _p1Spawn.GlobalPosition, false, true);
		SpawnPlayer(2, _p2Spawn.GlobalPosition, false, true);
		
		int serverNum = _gameManager.PreservedServer;
		Vector2 tosserPos = (serverNum == 1) ? _p2Spawn.GlobalPosition : _p1Spawn.GlobalPosition;
		SpawnTakyan(tosserPos + TossSpawnOffset);

		NetworkManager.Log("World", "Server entities spawned. Starting game...");

		if (_gameManager.RoundRestartPending)
		{
			// Round restart: restore server role and begin play immediately
			_serveManager.SetServer(_gameManager.PreservedServer);
			NetworkManager.Log("World", $"Round restart: server is P{_gameManager.PreservedServer}");
			_gameManager.ServerBeginPlayRound();
		}
		else
		{
			_gameManager.ServerStartGame();
		}
	}

	private void SpawnClientEntities()
	{
		int myPlayerNum = _net.LocalPlayerNumber;
		SpawnPlayer(1, _p1Spawn.GlobalPosition, myPlayerNum == 1, false);
		SpawnPlayer(2, _p2Spawn.GlobalPosition, myPlayerNum == 2, false);
		SpawnTakyan(_takyanSpawn.GlobalPosition);

		if (_gameManager.RoundRestartPending)
		{
			// Round restart: server already controls the flow, client just waits for RPC sync
			NetworkManager.Log("World", $"Client round restart as Player {myPlayerNum}. Waiting for server sync...");
		}
		else
		{
			NetworkManager.Log("World", $"Client entities spawned as Player {myPlayerNum}. Sending ready...");
			_gameManager.RpcId(1, nameof(GameManager.RpcClientReady));
		}
	}

	private void SpawnOfflineEntities()
	{
		SpawnPlayer(1, _p1Spawn.GlobalPosition, true, true);
		SpawnPlayer(2, _p2Spawn.GlobalPosition, true, true);
		
		int serverNum = _gameManager.PreservedServer;
		Vector2 tosserPos = (serverNum == 1) ? _p2Spawn.GlobalPosition : _p1Spawn.GlobalPosition;
		SpawnTakyan(tosserPos + TossSpawnOffset);

		if (_gameManager.RoundRestartPending)
		{
			// Round restart: restore server role and begin play immediately
			_serveManager.SetServer(_gameManager.PreservedServer);
			NetworkManager.Log("World", $"Local round restart: server is P{_gameManager.PreservedServer}");
			_gameManager.ServerBeginPlayRound();
		}
		else
		{
			_gameManager.ServerStartServeSequence();
		}
	}

	private void OnPlayerKickRequested(int playerNumber, float forcePower, float horizontalInfluence, float spinStrength, bool isMegaKick)
	{
		if (_net.IsClient) return;

		if (_activeTakyan != null)
		{
			// Prevent double hits during Rally phase
			if (_serveManager != null && _serveManager.IsRallyStarted())
			{
				if (_activeTakyan.GetLastPlayerWhoTouched() == playerNumber)
				{
					NetworkManager.Log("World", $"Player {playerNumber} tried to double-kick during Rally! Ignored.");
					return;
				}
			}

			if (isMegaKick)
			{
				// Fully random direction so the receiving player can't predict
				// which way the takyan is about to fly.
				float dirSign = (GD.Randf() > 0.5f) ? 1.0f : -1.0f;

				float randomX = dirSign * (float)GD.RandRange(1000, 2000);
				float randomY = -(float)GD.RandRange(1500, 2500);
				_activeTakyan.LinearVelocity = new Vector2(randomX, randomY);
				_activeTakyan.AngularVelocity = (GD.Randf() > 0.5f ? 1 : -1) * (float)GD.RandRange(20, 50);
			}
			else
			{
				_activeTakyan.ApplyKick(forcePower, horizontalInfluence);
			}
			
			_activeTakyan.OnPlayerKicked(playerNumber);

			if (_serveManager != null && !_serveManager.IsRallyStarted())
				_serveManager.MarkBallSpawned();
		}
	}

	private void OnTakyanTossed()
	{
		if (_net.IsClient || _activeTakyan == null) return;

		int serverNum = _serveManager.GetServer();

		// Use spawn positions to guarantee perfectly clean, symmetric, and lag-free serving trajectories
		Vector2 serverPos = (serverNum == 1) ? _p1Spawn.GlobalPosition : _p2Spawn.GlobalPosition;
		Vector2 tosserPos = (serverNum == 1) ? _p2Spawn.GlobalPosition : _p1Spawn.GlobalPosition;

		Vector2 toTarget = serverPos - tosserPos;
		float xDirection = toTarget.X > 0 ? 1.0f : -1.0f;
		float xInfluence = xDirection * 300.0f;

		// Move Takyan to tosser right as it's tossed (spawn directly on character body with correct offset direction)
		Vector2 takyanStartPos = tosserPos + new Vector2(xDirection * 100.0f, -20);
		_activeTakyan.ResetPosition(takyanStartPos);
		
		_activeTakyan.Visible = true; // Make it visible locally on the server
		if (_net.IsServer)
		{
			var roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
			if (roomManager != null)
			{
				foreach (var room in roomManager.GetPlayingRooms())
				{
					if (room.Player1PeerId != 0)
						_activeTakyan.RpcId(room.Player1PeerId, nameof(Takyan.RpcSetVisible), true);
					if (room.Player2PeerId != 0)
						_activeTakyan.RpcId(room.Player2PeerId, nameof(Takyan.RpcSetVisible), true);
				}
			}
		}

		// Moderate lob toss
		_activeTakyan.ApplyKick(900.0f, xInfluence);
		NetworkManager.Log("World", "Takyan Auto-Tossed to Server!");
	}

	// Called by ServeManager on round end — runs server-side, then broadcasts to all clients
	public void ServerResetForServe()
	{
		if (!_net.IsServer) return;

		// Reset locally on the server first
		RpcResetForServe();

		var roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
		if (roomManager != null)
		{
			foreach (var room in roomManager.GetPlayingRooms())
			{
				if (room.Player1PeerId != 0)
					RpcId(room.Player1PeerId, nameof(RpcResetForServe));
				if (room.Player2PeerId != 0)
					RpcId(room.Player2PeerId, nameof(RpcResetForServe));
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcResetForServe()
	{
		if (_activeTakyan == null) return;

		int serverNum = _serveManager.GetServer();
		PlayerOne serverPlayer = (serverNum == 1) ? _player1 : _player2;
		PlayerOne tosserPlayer = (serverNum == 1) ? _player2 : _player1;

		// Snap both players to their spawn positions on ALL clients
		serverPlayer.GlobalPosition = _p1Spawn.GlobalPosition;
		serverPlayer.Velocity = Vector2.Zero;
		tosserPlayer.GlobalPosition = _p2Spawn.GlobalPosition;
		tosserPlayer.Velocity = Vector2.Zero;

		// Hide Takyan during the wait/toss countdown
		_activeTakyan.Visible = false;

		NetworkManager.Log("World", "All clients: Reset positions for serve.");
	}
}
