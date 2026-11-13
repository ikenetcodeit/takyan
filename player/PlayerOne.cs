using Godot;
using System;

public partial class PlayerOne : CharacterBody2D
{
	[Export] public PackedScene TakyanScene;

	// Multiplayer variables
	[Export] public int PlayerNumber = 1;
	[Export] public bool IsLocal = true;

	[Export] public float Speed = 500.0f;
	[Export] public float JumpVelocity = -350.0f;

	[Export] public float MinKickForce = 500.0f;  
	[Export] public float MaxKickForce = 2000.0f;  
	[Export] public float ChargeSpeed = 2000.0f; 
	[Export] public float FlipSpinStrength = 5.0f; 
	[Export] public float KickCooldown = 0.5f;
	[Export] public float ScorePopScale = 6.5f;

	// Dash properties
	[Export] public float DashSpeed = 900.0f;
	[Export] public float DashDuration = 0.2f;
	[Export] public float DashCooldown = 1.0f;

	private bool _isDashing = false;
	private float _dashTimeLeft = 0.0f;
	private float _dashCooldownLeft = 0.0f;

	public float gravity = ProjectSettings.GetSetting("physics/2d/default_gravity").AsSingle();
	// SFX
	private AudioStreamPlayer _runSfxPlayer;
	private AudioStreamPlayer _jumpSfxPlayer;
	private AudioStreamPlayer _kickSfxPlayer;
	private AudioStreamPlayer _landSfxPlayer;
	private AudioStreamPlayer _dashSfxPlayer;
	private CpuParticles2D _runParticles;
	
	private bool _wasInAir = false;

	private AnimatedSprite2D _animatedSprite;
	private Area2D _kickZone;
	private bool _isKicking = false;

	// Charging variables
	private bool _isChargingKick = false;
	private float _currentKickForce = 0.0f;
	private float _kickCooldownLeft = 0.0f;
	private float _scoreResetTimer = 0.0f;

	private bool _isBallInZone = false;
	private RigidBody2D _trackedBall = null;

	// Serve state tracking
	private bool _hasServed = false;
	private RigidBody2D _activeTakyan = null; // Tracked ball

	// Kick tracking
	private int _kickCount = 0;
	[Export] public RichTextLabel MyWorldScoreLabel;
	private RichTextLabel _playerScoreTracker;
	private float _scoreFadeTimer = 0.0f;
	
	// Camera
	private GameCamera _gameCamera;
	private Polygon2D _takyanTracker;
	private RichTextLabel _jugglerLabel;

	// Multiplayer network
	private NetworkManager _net;
	private ServeManager _serveManager;
	private Vector2 _targetPosition;
	private Vector2 _targetVelocity;
	private string _targetAnimation = "default";
	private bool _targetFlipH = false;

	[Signal] public delegate void ServerKickRequestedEventHandler(int playerNumber, float forcePower, float horizontalInfluence, float spinStrength, bool isMegaKick);

	private bool _offlineMode => !_net.IsClient && !_net.IsServer;

	private string _moveLeftAction => (_offlineMode && PlayerNumber == 2) ? "move_left_p2" : "move_left";
	private string _moveRightAction => (_offlineMode && PlayerNumber == 2) ? "move_right_p2" : "move_right";
	private string _jumpAction => (_offlineMode && PlayerNumber == 2) ? "jump_p2" : "jump";
	private string _kickAction => (_offlineMode && PlayerNumber == 2) ? "kick_p2" : "kick";

	public override void _Ready()
	{
		var p1Sprite = GetNodeOrNull<AnimatedSprite2D>("Player1");
		var p2Sprite = GetNodeOrNull<AnimatedSprite2D>("Player2");

		if (p1Sprite != null && p2Sprite != null) {
			if (PlayerNumber == 1) {
				_animatedSprite = p1Sprite;
				p1Sprite.Show();
				p2Sprite.Hide();
			} else {
				_animatedSprite = p2Sprite;
				p2Sprite.Show();
				p1Sprite.Hide();
			}
		} else {
			_animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		}

		_kickZone = GetNode<Area2D>("KickZone");

		_animatedSprite.AnimationFinished += OnAnimationFinished;

		_kickZone.BodyEntered += OnKickZoneBodyEntered;
		_kickZone.BodyExited += OnKickZoneBodyExited;

		_net = GetNode<NetworkManager>("/root/NetworkManager");
		_serveManager = GetNodeOrNull<ServeManager>("/root/GameManager/ServeManager");
		_targetPosition = GlobalPosition;

		// Initialize SFX
		_runSfxPlayer = new AudioStreamPlayer();
		_runSfxPlayer.Stream = GD.Load<AudioStream>("res://sfx/Running.mp3");
		AddChild(_runSfxPlayer);

		_jumpSfxPlayer = new AudioStreamPlayer();
		_jumpSfxPlayer.Stream = GD.Load<AudioStream>("res://sfx/Jump.mp3");
		AddChild(_jumpSfxPlayer);

		_kickSfxPlayer = new AudioStreamPlayer();
		_kickSfxPlayer.Stream = GD.Load<AudioStream>("res://sfx/Kick.mp3");
		AddChild(_kickSfxPlayer);
		
		_landSfxPlayer = new AudioStreamPlayer();
		_landSfxPlayer.Stream = GD.Load<AudioStream>("res://sfx/Landing.mp3");
		AddChild(_landSfxPlayer);

		_dashSfxPlayer = new AudioStreamPlayer();
		_dashSfxPlayer.Stream = GD.Load<AudioStream>("res://sfx/Dash.mp3");
		AddChild(_dashSfxPlayer);

		_runParticles = GetNodeOrNull<CpuParticles2D>("runParticles");
		if (_runParticles != null) _runParticles.Emitting = false;

		// Link existing score tracker
		_playerScoreTracker = GetNodeOrNull<RichTextLabel>("playerScoreTracker");
		if (_playerScoreTracker != null)
		{
			_playerScoreTracker.Text = "0";
			_playerScoreTracker.Modulate = new Color(1, 1, 1, 0); // Start hidden
			_playerScoreTracker.PivotOffset = _playerScoreTracker.Size / 5.0f;
		}

		// Update Player Indicator (P1/P2)
		var playerIndicator = GetNodeOrNull<RichTextLabel>("playerIndicator");
		if (playerIndicator != null)
		{
			playerIndicator.Text = $"P{PlayerNumber}";
			playerIndicator.SelfModulate = PlayerNumber == 1 ? Color.FromHtml("#3b82f6") : Color.FromHtml("#ef4444");
		}

		// Create Juggler Label at the bottom (4px margin under the 64x64 sprite)
		_jugglerLabel = new RichTextLabel();
		_jugglerLabel.Name = "JugglerLabel";
		_jugglerLabel.BbcodeEnabled = true;
		_jugglerLabel.Text = "[center]Juggler[/center]";
		_jugglerLabel.Position = new Vector2(-25, 36); // Centered horizontally under the sprite at Y=36 (32 bottom + 4 margin)
		_jugglerLabel.Size = new Vector2(100, 30);
		_jugglerLabel.Scale = new Vector2(0.5f, 0.5f); // Same scale as playerIndicator (0.5)
		
		_jugglerLabel.AddThemeFontSizeOverride("normal_font_size", 22); // Same font size as playerIndicator
		_jugglerLabel.AddThemeColorOverride("default_color", Color.FromHtml("#fbbf24")); // Amber/Gold color
		_jugglerLabel.AddThemeConstantOverride("outline_size", 3); // Same outline size as playerIndicator
		
		var font = GD.Load<Font>("res://art/VCR_OSD_MONO_1.001.ttf");
		if (font != null)
		{
			_jugglerLabel.AddThemeFontOverride("normal_font", font);
		}
		_jugglerLabel.Hide();
		AddChild(_jugglerLabel);

		if (_serveManager != null)
		{
			_serveManager.ServerChanged += OnServeServerChanged;
			_serveManager.PhaseChanged += OnServePhaseChanged;
			UpdateJugglerLabelVisibility();
		}

		// Initialize tracker UI
		CreateTrackerUI();
		
		var camNode = GetNodeOrNull<Camera2D>("Camera2D");
		if (camNode != null)
		{
			camNode.Enabled = IsLocal;
			if (IsLocal)
			{
				_gameCamera = new GameCamera(camNode);
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsLocal)
		{
			LocalPhysicsProcess(delta);
		}
		else if (!_net.IsServer)
		{
			RemotePhysicsProcess(delta);
		}
	}

	private void LocalPhysicsProcess(double delta)
	{
		Vector2 velocity = Velocity;

		bool canMove = true;
		var serveManager = GetNodeOrNull<ServeManager>("/root/GameManager/ServeManager");
		if (serveManager != null)
		{
			int phase = (int)serveManager.Phase;
			if (phase == 0) // Waiting
			{
				_kickCount = 0;
				UpdateKickCounterUI();
				if (_playerScoreTracker != null) _playerScoreTracker.Modulate = new Color(1, 1, 1, 0); // Hide score
			}
			
			if (phase == 0 || phase == 1) // Waiting or Tossing
			{
				canMove = false;
			}
		}

		if (!canMove)
		{
			velocity.X = 0;
			if (!IsOnFloor()) velocity.Y += gravity * (float)delta;
			
			_isDashing = false;
			if (_runSfxPlayer != null && _runSfxPlayer.Playing) _runSfxPlayer.Stop();
			if (_runParticles != null) _runParticles.Emitting = false;

			Velocity = velocity;
			MoveAndSlide();
			UpdateAnimations(velocity.X);
			
			// Sync state to server even when frozen so the server knows where we are
			if (!_offlineMode && _net.IsClient)
			{
				RpcId(1, nameof(CmdSendPlayerState), GlobalPosition, velocity, _animatedSprite.Animation, _animatedSprite.FlipH);
			}
			return; // Skip reading inputs!
		}

		if (_dashCooldownLeft > 0) _dashCooldownLeft -= (float)delta;
		if (_kickCooldownLeft > 0) _kickCooldownLeft -= (float)delta;
		
		if (_scoreResetTimer > 0)
		{
			_scoreResetTimer -= (float)delta;
			if (_scoreResetTimer <= 0)
			{
				_kickCount = 0;
				if (_playerScoreTracker != null) _playerScoreTracker.Text = "0";
				if (MyWorldScoreLabel != null) MyWorldScoreLabel.Text = "0";
			}
		}

		if (_isDashing)
		{
			_dashTimeLeft -= (float)delta;
			if (_dashTimeLeft <= 0)
			{
				_isDashing = false;
			}
		}

		if (Input.IsPhysicalKeyPressed(Key.Alt) && _dashCooldownLeft <= 0 && !_isDashing)
		{
			_isDashing = true;
			_dashTimeLeft = DashDuration;
			_dashCooldownLeft = DashCooldown;
			_dashSfxPlayer?.Play();
		}

		if (_isDashing)
		{
			velocity.Y = 0; // Pause gravity and vertical velocity
			velocity.X = (_animatedSprite.FlipH ? -1 : 1) * DashSpeed;
			if (_runSfxPlayer != null && _runSfxPlayer.Playing) _runSfxPlayer.Stop();
			if (_runParticles != null) _runParticles.Emitting = false;
		}
		else
		{
			if (!IsOnFloor())
			{
				velocity.Y += gravity * (float)delta;
			}

			if (Input.IsActionJustPressed(_jumpAction) && IsOnFloor())
			{
				velocity.Y = JumpVelocity;
				_jumpSfxPlayer?.Play();
			}

			float direction = Input.GetAxis(_moveLeftAction, _moveRightAction);
			if (direction != 0)
			{
				velocity.X = direction * Speed;
				if (!_isKicking)
				{
					_animatedSprite.FlipH = direction < 0;
					if (IsOnFloor())
					{
						if (_runSfxPlayer != null && !_runSfxPlayer.Playing) _runSfxPlayer.Play();
						if (_runParticles != null) _runParticles.Emitting = true;
					}
				}
			}
			else
			{
				velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
				if (_runSfxPlayer != null && _runSfxPlayer.Playing) _runSfxPlayer.Stop();
				if (_runParticles != null) _runParticles.Emitting = false;
			}
			
			// Always stop running sound and particles if we are jumping/falling
			if (!IsOnFloor())
			{
				if (_runSfxPlayer != null && _runSfxPlayer.Playing) _runSfxPlayer.Stop();
				if (_runParticles != null) _runParticles.Emitting = false;
			}
		}

		HandleKickInput(delta);

		Velocity = velocity;
		MoveAndSlide();

		if (_wasInAir && IsOnFloor())
		{
			_landSfxPlayer?.Play();
		}
		_wasInAir = !IsOnFloor();

		if (!_isKicking && !_isDashing)
		{
			float direction = Input.GetAxis(_moveLeftAction, _moveRightAction);
			UpdateAnimations(direction);
		}

		UpdateTakyanTracker();

		if (_playerScoreTracker != null)
		{
			if (_scoreFadeTimer > 0)
			{
				_scoreFadeTimer -= (float)delta * 0.75f; // Fades out over ~1.3 seconds
				float alpha = Mathf.Clamp(_scoreFadeTimer, 0.0f, 1.0f);
				
				Color currentColor = _playerScoreTracker.Modulate;
				currentColor.A = alpha;
				_playerScoreTracker.Modulate = currentColor;
			}
			
			// Smoothly animate scale back to normal size
			_playerScoreTracker.Scale = _playerScoreTracker.Scale.Lerp(Vector2.One, (float)delta * 12.0f);
		}
		
		_gameCamera?.ProcessShake(delta);

		if (_net.IsServer)
		{
			if (IsInsideTree()) Rpc(nameof(RpcReceivePlayerState), GlobalPosition, Velocity, _animatedSprite.Animation, _animatedSprite.FlipH);
		}
		else if (_net.IsClient)
		{
			if (IsInsideTree()) RpcId(1, nameof(CmdSendPlayerState), GlobalPosition, Velocity, _animatedSprite.Animation, _animatedSprite.FlipH);
		}
	}

	private void RemotePhysicsProcess(double delta)
	{
		if (_net.IsServer)
		{
			return; // Handled by authoritative state syncing
		}

		GlobalPosition += _targetVelocity * (float)delta;
		GlobalPosition = GlobalPosition.Lerp(_targetPosition, 0.15f);

		Velocity = _targetVelocity;
		UpdateRemoteAnimations();
	}

	private void UpdateRemoteAnimations()
	{
		bool isKicking = _animatedSprite.Animation == "kick" && _animatedSprite.IsPlaying();
		
		if (!isKicking && _animatedSprite.Animation != _targetAnimation)
		{
			_animatedSprite.Play(_targetAnimation);
		}
		
		_animatedSprite.FlipH = _targetFlipH;

		// Deduce particle state
		if (_runParticles != null)
		{
			_runParticles.Emitting = (_targetAnimation == "run");
		}
	}

	private void HandleKickInput(double delta)
	{
		// A. PRESS DOWN: Start charging power immediately, anywhere on the screen!
		if (Input.IsActionJustPressed(_kickAction) && _kickCooldownLeft <= 0)
		{
			if (!_offlineMode && _serveManager != null && !_serveManager.IsRallyStarted() && _serveManager.GetServer() != PlayerNumber)
			{
				// Not our turn to serve online
				return;
			}

			_isChargingKick = true;
			_isKicking = false; 
			_currentKickForce = MinKickForce;
		}

		// B. HOLDING DOWN: Accumulate power while holding the key
		if (_isChargingKick && Input.IsActionPressed(_kickAction))
		{
			_currentKickForce += ChargeSpeed * (float)delta;
			_currentKickForce = Mathf.Min(_currentKickForce, MaxKickForce);
		}

		// C. RELEASE BUTTON: ALWAYS execute the kick animation no matter what!
		if (_isChargingKick && Input.IsActionJustReleased(_kickAction))
		{
			_isChargingKick = false;
			_isKicking = true;
			_kickCooldownLeft = KickCooldown;
			
			_animatedSprite.Play("kick");
			_kickSfxPlayer?.Play();

			if (!_isBallInZone)
			{
				return; // Empty kick, missed the ball
			}

			bool isMegaKick = false;
			var serveManager = GetNodeOrNull<ServeManager>("/root/GameManager/ServeManager");

			if (serveManager != null)
			{
				int phase = (int)serveManager.Phase;
				int server = serveManager.GetServer();

				if (phase == 0 || phase == 1) // Waiting or Tossing
				{
					return; // No kicks allowed
				}
				else if (phase == 2) // Juggling
				{
					if (PlayerNumber != server) return; // Tosser can't interfere

					_kickCount++;
					UpdateKickCounterUI();
					isMegaKick = _kickCount >= 5;
				}
				else if (phase == 3) // Rally
				{
					isMegaKick = true; // ALL kicks are mega kicks during rally!
				}
			}
			else
			{
				// Offline juggling logic fallback
				if (_scoreResetTimer > 0)
				{
					_scoreResetTimer = 0.0f;
					_kickCount = 0;
				}
				_kickCount++;
				UpdateKickCounterUI();
				isMegaKick = _kickCount >= 5;
			}

			if (isMegaKick)
			{
				_gameCamera?.AddShake(25.0f); // BIG shake!
				_scoreResetTimer = 2.0f; // Wait 2 seconds before resetting to 0
			}
			else
			{
				_gameCamera?.AddShake(8.0f); // Small shake
			}

			if (_offlineMode) 
			{
				if (_trackedBall != null)
				{
					if (isMegaKick)
					{
						float direction = (PlayerNumber == 1) ? 1.0f : -1.0f;
						float xVel = direction * (float)GD.RandRange(600.0f, 1000.0f);
						float yVel = -(float)GD.RandRange(700.0f, 1100.0f);
						_trackedBall.LinearVelocity = new Vector2(xVel, yVel);
						_trackedBall.AngularVelocity = -direction * 18.0f;
					}
					else
					{
						_trackedBall.LinearVelocity = CalculateKickVector(_currentKickForce);
						_trackedBall.AngularVelocity = CalculateSpinValue();
					}

					// Let the ball know who kicked it so the serve flow and scoring can track hits
					if (_trackedBall is Takyan localTakyan)
					{
						localTakyan.OnPlayerKicked(PlayerNumber);
					}
				}
			} 
			else 
			{
				// ONLINE MODE KICK REQUEST
				float horizontalInfluence = CalculateKickVector(_currentKickForce).X;
				
				if (_net.IsClient)
				{
					RpcId(1, nameof(CmdSendKickAction), _currentKickForce, horizontalInfluence, FlipSpinStrength, isMegaKick);
				}
				else
				{
					EmitSignal(SignalName.ServerKickRequested, PlayerNumber, _currentKickForce, horizontalInfluence, FlipSpinStrength, isMegaKick);
				}
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void CmdSendPlayerState(Vector2 position, Vector2 velocity, string animation, bool flipH)
	{
		if (!_net.IsServer) return;

		// Set accurate position for takyan physics on the server
		GlobalPosition = position;
		Velocity = velocity;

		// Update targets so RemotePhysicsProcess doesn't lerp away from the real position
		_targetPosition = position;
		_targetVelocity = velocity;
		_targetAnimation = animation;
		_targetFlipH = flipH;

		_animatedSprite.Animation = animation;
		_animatedSprite.FlipH = flipH;

		if (IsInsideTree())
		{
			long senderId = Multiplayer.GetRemoteSenderId();
			var roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
			if (roomManager != null)
			{
				var room = roomManager.GetRoomForPeer(senderId);
				if (room != null)
				{
					long opponentId = room.GetOpponentPeerId(senderId);
					if (opponentId != 0)
					{
						RpcId(opponentId, nameof(RpcReceivePlayerState), position, velocity, animation, flipH);
					}
				}
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcTeleport(Vector2 position)
	{
		GlobalPosition = position;
		Velocity = Vector2.Zero;
		_targetPosition = position;
		_targetVelocity = Vector2.Zero;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void RpcReceivePlayerState(Vector2 position, Vector2 velocity, string animation, bool flipH)
	{
		if (IsLocal) return;

		_targetPosition = position;
		_targetVelocity = velocity;
		_targetAnimation = animation;
		_targetFlipH = flipH;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void CmdSendKickAction(float forcePower, float horizontalInfluence, float spinStrength, bool isMegaKick)
	{
		if (!_net.IsServer) return;
		EmitSignal(SignalName.ServerKickRequested, PlayerNumber, forcePower, horizontalInfluence, spinStrength, isMegaKick);
		
		long senderId = Multiplayer.GetRemoteSenderId();
		var roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
		if (roomManager != null)
		{
			var room = roomManager.GetRoomForPeer(senderId);
			if (room != null)
			{
				long opponentId = room.GetOpponentPeerId(senderId);
				if (opponentId != 0)
				{
					RpcId(opponentId, nameof(RpcPlayKickVisual));
				}
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcPlayKickVisual()
	{
		if (IsLocal) return; // Already played locally
		
		_animatedSprite.Play("kick");
		_kickSfxPlayer?.Play();
	}

	private Vector2 CalculateKickVector(float forcePower)
	{
		float currentDirection = Input.GetAxis(_moveLeftAction, _moveRightAction);
		bool isFacingLeft = _animatedSprite.FlipH;
		
		// If standing still, throw it slightly forward and way up high
		float horizontalInfluence = currentDirection != 0 ? currentDirection * 150.0f : (isFacingLeft ? -80.0f : 80.0f);
		
		return new Vector2(horizontalInfluence, -forcePower);
	}

	private float CalculateSpinValue()
	{
		float currentDirection = Input.GetAxis(_moveLeftAction, _moveRightAction);
		float horizontalInfluence = currentDirection != 0 ? currentDirection * 150.0f : (_animatedSprite.FlipH ? -80.0f : 80.0f);
		return (horizontalInfluence > 0 ? 1.0f : -1.0f) * FlipSpinStrength;
	}

	private void OnKickZoneBodyEntered(Node2D body)
	{
		if (body is RigidBody2D ball)
		{
			_isBallInZone = true;
			_trackedBall = ball;
		}
	}

	private void OnKickZoneBodyExited(Node2D body)
	{
		if (body is RigidBody2D ball)
		{
			_isBallInZone = false;
			if (_trackedBall == ball)
			{
				_trackedBall = null;
			}
		}
	}

	private void UpdateAnimations(float direction)
	{
		if (!IsOnFloor())
		{
			_animatedSprite.Play("jump");
		}
		else if (direction != 0 || Mathf.Abs(Velocity.X) > 0.1f)
		{
			_animatedSprite.Play("run");
		}
		else
		{
			_animatedSprite.Play("default");
		}
	}

	private void OnAnimationFinished()
	{
		if (_animatedSprite.Animation == "kick")
		{
			_isKicking = false;
		}
	}

	private void UpdateKickCounterUI()
	{
		if (_playerScoreTracker != null)
		{
			_playerScoreTracker.Text = _kickCount.ToString();
			
			if (_kickCount >= 5)
			{
				_scoreFadeTimer = 1.0f; // Keep it visible longer on 5
				_playerScoreTracker.Modulate = new Color(1, 0, 0, 1); // Red on 5th
			}
			else
			{
				_scoreFadeTimer = 1.0f; // Alpha reset
				_playerScoreTracker.Modulate = new Color(1, 1, 1, 1);
			}
			
			// Make the number pop in size!
			_playerScoreTracker.Scale = new Vector2(ScorePopScale, ScorePopScale);
		}

		if (MyWorldScoreLabel != null)
		{
			MyWorldScoreLabel.Text = _kickCount.ToString();
		}
	}

	private void CreateTrackerUI()
	{
		_takyanTracker = new Polygon2D();
		
		// Create a simple triangle pointing right
		var points = new Vector2[] {
			new Vector2(-10, -10),
			new Vector2(10, 0),
			new Vector2(-10, 10)
		};
		_takyanTracker.Polygon = points;
		_takyanTracker.Color = new Color(1, 0.5f, 0, 0.8f); // Orange
		_takyanTracker.Visible = false;
		
		// Make it draw over other things
		_takyanTracker.ZIndex = 100;
		
		AddChild(_takyanTracker);
	}

	private void UpdateTakyanTracker()
	{
		if (_takyanTracker == null) return;
		
		Node parent = GetParent();
		RigidBody2D foundTakyan = null;
		
		if (IsInstanceValid(_activeTakyan))
		{
			foundTakyan = _activeTakyan;
		}
		else if (_trackedBall != null && IsInstanceValid(_trackedBall))
		{
			foundTakyan = _trackedBall;
		}
		else if (parent != null)
		{
			// Try to find any takyan in the scene
			foreach (Node child in parent.GetChildren())
			{
				if (child is RigidBody2D && child.Name.ToString().Contains("Takyan", StringComparison.OrdinalIgnoreCase))
				{
					foundTakyan = (RigidBody2D)child;
					break;
				}
			}
		}

		if (foundTakyan != null && IsInstanceValid(foundTakyan))
		{
			float dist = GlobalPosition.DistanceTo(foundTakyan.GlobalPosition);
			if (dist > 400.0f) // Threshold to show tracker
			{
				_takyanTracker.Visible = true;
				Vector2 dir = (foundTakyan.GlobalPosition - GlobalPosition).Normalized();
				_takyanTracker.Position = dir * 150.0f; // radius from player
				_takyanTracker.Rotation = dir.Angle();
			}
			else
			{
				_takyanTracker.Visible = false;
			}
		}
		else
		{
			_takyanTracker.Visible = false;
		}
	}

	public override void _ExitTree()
	{
		if (_serveManager != null)
		{
			_serveManager.ServerChanged -= OnServeServerChanged;
			_serveManager.PhaseChanged -= OnServePhaseChanged;
		}
	}

	private void UpdateJugglerLabelVisibility()
	{
		if (_jugglerLabel == null || _serveManager == null) return;

		int currentServer = _serveManager.GetServer();
		int currentPhase = (int)_serveManager.Phase;

		// Show Juggler label during Waiting (0), Tossing (1), and Juggling (2) phases
		bool showJuggler = (currentPhase == 0 || currentPhase == 1 || currentPhase == 2) && PlayerNumber == currentServer;
		
		_jugglerLabel.Visible = showJuggler;
	}

	private void OnServeServerChanged(int server)
	{
		UpdateJugglerLabelVisibility();
	}

	private void OnServePhaseChanged(int phase)
	{
		UpdateJugglerLabelVisibility();
	}
}
