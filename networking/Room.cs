using System;

public enum RoomState {
	Waiting,
	Playing,
	Finished
}

public class Room {

	public string RoomId { get; set; }

	// Peer IDs assigned by ENet (0 = not yet assigned)
	public long Player1PeerId { get; set; } = 0;
	public long Player2PeerId { get; set; } = 0;

	public RoomState State { get; set; } = RoomState.Waiting;

	public Room(string roomId, long player1PeerId) {
		RoomId = roomId;
		Player1PeerId = player1PeerId;
		State = RoomState.Waiting;
	}

	public bool IsFull => Player1PeerId != 0 && Player2PeerId != 0;

	public bool HasPeer(long peerId) =>
		Player1PeerId == peerId || Player2PeerId == peerId;

	// Returns the player number (1 or 2) for a given peer, or -1 if not in room
	public int GetPlayerNumber(long peerId) {
		if (Player1PeerId == peerId) return 1;
		if (Player2PeerId == peerId) return 2;
		return -1;
	}

	// Returns the peer ID of the opponent of the given peer, or 0 if not found
	public long GetOpponentPeerId(long peerId) {
		if (Player1PeerId == peerId) return Player2PeerId;
		if (Player2PeerId == peerId) return Player1PeerId;
		return 0;
	}

	// Removes a peer from the room and returns to Waiting state
	public void RemovePeer(long peerId) {
		if (Player1PeerId == peerId) {
			// If P1 leaves and P2 is still here, promote P2 to P1
			Player1PeerId = Player2PeerId;
			Player2PeerId = 0;
		} else if (Player2PeerId == peerId) {
			Player2PeerId = 0;
		}
		State = RoomState.Waiting;
	}
}
