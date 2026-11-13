using Godot;
using System;

public partial class ScoreUI : Control {

	private Label _scoreLabel;
	private Label _roundLabel;
	private Button _playAgainBtn;
	private Button _returnBtn;
	private VBoxContainer _postMatchVBox;

	private GameManager _gameManager;
	private NetworkManager _net;
	private ServeManager _serveManager;

	public override void _Ready() {
		_scoreLabel = GetNode<Label>("%ScoreLabel");
		_roundLabel = GetNode<Label>("%RoundLabel");
		_playAgainBtn = GetNode<Button>("%PlayAgainBtn");
		_returnBtn = GetNode<Button>("%ReturnBtn");
		_postMatchVBox = GetNode<VBoxContainer>("%VBox");

		// Forcefully remove old labels if they still exist in the scene
		GetNodeOrNull<Label>("%WinnerLabel")?.QueueFree();
		GetNodeOrNull<Label>("%CountdownLabel")?.QueueFree();

		_net = GetNode<NetworkManager>("/root/NetworkManager");
		_gameManager = GetNode<GameManager>("/root/GameManager");

		_gameManager.ScoreUpdated += OnScoreUpdated;
		_gameManager.MatchEnded += OnMatchEnded;
		_gameManager.RematchStatusUpdated += OnRematchStatusUpdated;

		_serveManager = _gameManager.GetServeManager();
		if (_serveManager != null)
		{
			_serveManager.ServerChanged += OnServerChanged;
		}

		_playAgainBtn.Pressed += OnPlayAgainPressed;
		_returnBtn.Pressed += OnReturnPressed;

		_postMatchVBox.Hide();

		// Fetch the real current score (don't hardcode to 0)
		OnScoreUpdated(_gameManager.GetPlayer1Score(), _gameManager.GetPlayer2Score());
		_roundLabel.Text = "First to 5";
	}

	public override void _ExitTree() {
		if (_gameManager != null) {
			_gameManager.ScoreUpdated -= OnScoreUpdated;
			_gameManager.MatchEnded -= OnMatchEnded;
			_gameManager.RematchStatusUpdated -= OnRematchStatusUpdated;
		}
		if (_serveManager != null) {
			_serveManager.ServerChanged -= OnServerChanged;
		}
	}

	private void OnScoreUpdated(int p1, int p2) {
		_scoreLabel.Text = $"{p1} - {p2}";
	}

	private void OnServerChanged(int server) {
		if (_gameManager != null)
			OnScoreUpdated(_gameManager.GetPlayer1Score(), _gameManager.GetPlayer2Score());
	}

	private void OnMatchEnded(int winner) {
		_postMatchVBox.Show();
		_playAgainBtn.Show();
		_returnBtn.Show();
		_playAgainBtn.Text = "REMATCH?";
		_playAgainBtn.Disabled = false;
		_returnBtn.Disabled = false;

		// Auto-hide buttons after rematch is accepted (if scene reloads)
	}

	private void OnRematchStatusUpdated(bool p1Ready, bool p2Ready) {
		int myPlayerNum = _net.LocalPlayerNumber;
		bool amIReady = (myPlayerNum == 1 && p1Ready) || (myPlayerNum == 2 && p2Ready);

		if (amIReady) {
			_playAgainBtn.Text = "Waiting for other player...";
		}
	}

	private void OnPlayAgainPressed() {
		NetworkManager.Log("ScoreUI", "Play Again requested.");
		_playAgainBtn.Disabled = true;

		_gameManager.RpcId(1, nameof(GameManager.CmdRequestRematch));
	}

	private void OnReturnPressed() {
		NetworkManager.Log("ScoreUI", "Returning to menu.");
		_net.Disconnect();
		GetTree().ChangeSceneToFile("res://ui/main_menu.tscn");
	}
}
