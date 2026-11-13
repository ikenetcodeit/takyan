using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Autoload node. On the SERVER it manages room creation, joining, and teardown.
/// On the CLIENT its RPC stubs emit C# signals that UI scenes (MainMenu, Lobby) subscribe to.
/// </summary>
public partial class RoomManager : Node {

	// ── Signals (emitted on client side when the server sends an RPC) ──────────

	[Signal] public delegate void RoomCreatedSignalEventHandler(string roomId);
	[Signal] public delegate void JoinResultSignalEventHandler(bool success, string reason);
	[Signal] public delegate void StartMatchSignalEventHandler(int playerNumber);
	[Signal] public delegate void OpponentDisconnectedSignalEventHandler();

	// ── Constants ─────────────────────────────────────────────────────────────

	private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0, I/1 ambiguity
	private const int    CodeLength = 4;

	// ── Server-side state ─────────────────────────────────────────────────────

	// roomId -> Room
	private Dictionary<string, Room> _rooms = new();

	// peerId -> roomId (reverse lookup)
	private Dictionary<long, string> _peerToRoom = new();

	private NetworkManager _net;
	private Random         _rng = new();

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready() {
		_net = GetNode<NetworkManager>("/root/NetworkManager");

		if (_net.IsServer) {
			_net.PeerDisconnected += OnPeerDisconnected;
			GD.Print("[RoomManager] Ready on server.");
		}
	}

	// ── RPC: Client -> Server ─────────────────────────────────────────────────

	/// <summary>Client calls this to create a new room.</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestCreateRoom() {
		if (!_net.IsServer) return;

		long peerId = Multiplayer.GetRemoteSenderId();

		if (_peerToRoom.ContainsKey(peerId)) {
			GD.PrintErr($"[RoomManager] Peer {peerId} is already in a room.");
			RpcId(peerId, nameof(RpcJoinResult), false, "Already in a room.");
			return;
		}

		string code = GenerateUniqueCode();
		var    room  = new Room(code, peerId);

		_rooms[code]        = room;
		_peerToRoom[peerId] = code;

		GD.Print($"[RoomManager] Room {code} created by peer {peerId}.");
		RpcId(peerId, nameof(RpcRoomCreated), code);
	}

	/// <summary>Client calls this to join an existing room by code.</summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestJoinRoom(string roomId) {
		if (!_net.IsServer) return;

		long peerId = Multiplayer.GetRemoteSenderId();
		roomId = roomId.ToUpperInvariant().Trim();

		if (_peerToRoom.ContainsKey(peerId)) {
			RpcId(peerId, nameof(RpcJoinResult), false, "Already in a room.");
			return;
		}
		if (!_rooms.TryGetValue(roomId, out Room room)) {
			RpcId(peerId, nameof(RpcJoinResult), false, "Room not found.");
			return;
		}
		if (room.IsFull) {
			RpcId(peerId, nameof(RpcJoinResult), false, "Room is full.");
			return;
		}
		if (room.State != RoomState.Waiting) {
			RpcId(peerId, nameof(RpcJoinResult), false, "Game already in progress.");
			return;
		}

		room.Player2PeerId  = peerId;
		_peerToRoom[peerId] = roomId;

		GD.Print($"[RoomManager] Peer {peerId} joined room {roomId} as Player 2.");

		// Confirm join to the joiner, then start match for both
		RpcId(peerId, nameof(RpcJoinResult), true, "");
		ServerStartMatch(room);
	}

	// ── RPC: Server -> Client ─────────────────────────────────────────────────
	// These are received on the client. They emit signals that UI scenes listen to.

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcRoomCreated(string roomId) {
		GD.Print($"[RoomManager] Client — room created: {roomId}");
		EmitSignal(SignalName.RoomCreatedSignal, roomId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcJoinResult(bool success, string reason) {
		GD.Print($"[RoomManager] Client — join result: success={success}, reason={reason}");
		EmitSignal(SignalName.JoinResultSignal, success, reason);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcStartMatch(int assignedPlayerNumber) {
		GD.Print($"[RoomManager] Client — start match as Player {assignedPlayerNumber}");
		EmitSignal(SignalName.StartMatchSignal, assignedPlayerNumber);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcOpponentDisconnected() {
		GD.Print("[RoomManager] Client — opponent disconnected.");
		EmitSignal(SignalName.OpponentDisconnectedSignal);
	}

	// ── Server-side helpers ───────────────────────────────────────────────────

	private void ServerStartMatch(Room room) {
		room.State = RoomState.Playing;

		GD.Print($"[RoomManager] Starting match in room {room.RoomId} " +
				 $"(P1={room.Player1PeerId}, P2={room.Player2PeerId})");

		// Change scene to World on the server so physics runs
		GetTree().ChangeSceneToFile("res://world.tscn");

		RpcId(room.Player1PeerId, nameof(RpcStartMatch), 1);
		RpcId(room.Player2PeerId, nameof(RpcStartMatch), 2);
	}

	private void OnPeerDisconnected(long peerId) {
		if (!_peerToRoom.TryGetValue(peerId, out string roomId)) return;

		if (!_rooms.TryGetValue(roomId, out Room room)) {
			_peerToRoom.Remove(peerId);
			return;
		}

		long opponentId = room.GetOpponentPeerId(peerId);
		GD.Print($"[RoomManager] Peer {peerId} disconnected from room {roomId}.");

		if (opponentId != 0) {
			RpcId(opponentId, nameof(RpcOpponentDisconnected));
			room.RemovePeer(peerId);
			if (room.Player1PeerId == 0) {
				_rooms.Remove(roomId);
				GD.Print($"[RoomManager] Room {roomId} deleted (empty).");
			}
		} else {
			_rooms.Remove(roomId);
			GD.Print($"[RoomManager] Room {roomId} deleted (empty).");
		}

		_peerToRoom.Remove(peerId);
	}

	// ── Utilities ─────────────────────────────────────────────────────────────

	private string GenerateUniqueCode() {
		string code;
		do {
			var chars = new char[CodeLength];
			for (int i = 0; i < CodeLength; i++)
				chars[i] = CodeChars[_rng.Next(CodeChars.Length)];
			code = new string(chars);
		} while (_rooms.ContainsKey(code));
		return code;
	}

	public Room GetRoomForPeer(long peerId) {
		if (_peerToRoom.TryGetValue(peerId, out string roomId) &&
			_rooms.TryGetValue(roomId, out Room room))
			return room;
		return null;
	}

	public List<Room> GetPlayingRooms() {
		var playing = new List<Room>();
		foreach (var room in _rooms.Values) {
			if (room.State == RoomState.Playing) {
				playing.Add(room);
			}
		}
		return playing;
	}
}
