using Godot;
using System;

public partial class Takyan : RigidBody2D
{
	private NetworkManager _net;
	private ServeManager _serveManager;

	private Vector2 _targetPosition;
	private float _targetRotation;
	private Vector2 _targetLinearVelocity;
	private float _targetAngularVelocity;

	private int _lastPlayerWhoTouched = 0;

	public override void _Ready()
	{
		_net = GetNode<NetworkManager>("/root/NetworkManager");
		_serveManager = GetNodeOrNull<ServeManager>("/root/GameManager/ServeManager");
		_targetPosition = GlobalPosition;
		_targetRotation = Rotation;

		// Hide the Takyan and freeze it until the toss sequence starts
		Visible = false;

		bool isAuthority = !_net.IsClient;

		if (isAuthority)
		{
			Freeze = true; // Wait for ApplyKick to unfreeze
			ContactMonitor = true;
			MaxContactsReported = 5;
			BodyEntered += OnBodyEntered;
			GD.Print("[Takyan] Ready with physics frozen (waiting for toss).");
		}
		else
		{
			Freeze = true;
			FreezeMode = FreezeModeEnum.Kinematic;
			// Keep original CollisionLayer so KickZone detects it
			CollisionMask = 0;
			GD.Print("[Takyan] Ready on Client (Physics Disabled, Puppet Mode).");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		bool isAuthority = !_net.IsClient;

		if (isAuthority)
		{
			if (_net.IsServer)
			{
				var roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
				if (roomManager != null)
				{
					foreach (var room in roomManager.GetPlayingRooms())
					{
						if (room.Player1PeerId != 0)
							RpcId(room.Player1PeerId, nameof(RpcSyncState), GlobalPosition, Rotation, LinearVelocity, AngularVelocity);
						if (room.Player2PeerId != 0)
							RpcId(room.Player2PeerId, nameof(RpcSyncState), GlobalPosition, Rotation, LinearVelocity, AngularVelocity);
					}
				}
			}
		}
		else
		{
			// Predict trajectory using velocity between state updates
			GlobalPosition += _targetLinearVelocity * (float)delta;
			Rotation += _targetAngularVelocity * (float)delta;

			// Soft correction toward authoritative position
			GlobalPosition = GlobalPosition.Lerp(_targetPosition, 0.15f);
			Rotation = Mathf.LerpAngle(Rotation, _targetRotation, 0.15f);
		}
	}

	private void OnBodyEntered(Node body)
	{
		if (_net.IsClient) return;

		if (body.Name.ToString() == "Floor")
		{
			GD.Print("Takyan hit the floor! Round over.");

			if (_serveManager != null)
				_serveManager.TakyanHitGround(_lastPlayerWhoTouched);

			// GameManager is notified via ServeManager.PointScored signal instead
		}
		else
		{
			GD.Print($"Bounced off: {body.Name}");
		}
	}

	public void OnPlayerKicked(int playerNumber)
	{
		_lastPlayerWhoTouched = playerNumber;

		if (_serveManager != null && !_serveManager.IsRallyStarted())
			_serveManager.ServerKick(playerNumber);

		GD.Print($"Player {playerNumber} kicked the Takyan");
	}

	public int GetLastPlayerWhoTouched() => _lastPlayerWhoTouched;

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void RpcSyncState(Vector2 position, float rotation, Vector2 linearVelocity, float angularVelocity)
	{
		if (!_net.IsClient) return;
		_targetPosition = position;
		_targetRotation = rotation;
		_targetLinearVelocity = linearVelocity;
		_targetAngularVelocity = angularVelocity;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcTeleport(Vector2 position)
	{
		GlobalPosition = position;
		_targetPosition = position;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcSetVisible(bool isVisible)
	{
		Visible = isVisible;
	}

	public void ApplyKick(float force, float direction)
	{
		if (_net.IsClient) return;

		Freeze = false;
		LinearVelocity = new Vector2(direction, -force);
		AngularVelocity = (direction > 0 ? 1.0f : -1.0f) * 18.0f;
		GD.Print($"[Takyan] Applied kick: force={force}, dir={direction}");
	}

	public void ResetPosition(Vector2 spawnPosition)
	{
		if (_net.IsClient) return;

		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0;
		Freeze = true;
		GlobalPosition = spawnPosition;
		Rotation = 0;

		if (_net.IsServer)
		{
			var roomManager = GetNodeOrNull<RoomManager>("/root/RoomManager");
			if (roomManager != null)
			{
				foreach (var room in roomManager.GetPlayingRooms())
				{
					if (room.Player1PeerId != 0)
					{
						RpcId(room.Player1PeerId, nameof(RpcTeleport), GlobalPosition);
						RpcId(room.Player1PeerId, nameof(RpcSyncState), GlobalPosition, Rotation, Vector2.Zero, 0);
					}
					if (room.Player2PeerId != 0)
					{
						RpcId(room.Player2PeerId, nameof(RpcTeleport), GlobalPosition);
						RpcId(room.Player2PeerId, nameof(RpcSyncState), GlobalPosition, Rotation, Vector2.Zero, 0);
					}
				}
			}
		}
	}
}
