using Godot;

public partial class TitleScreen : Control
{
	private Button _playButton;
	private Button _howToPlayButton;
	private Button _exitButton;

	public override void _Ready()
	{
		_playButton = GetNode<Button>("menuButtons/playButton");
		_howToPlayButton = GetNode<Button>("menuButtons/howToPlayButton");
		_exitButton = GetNode<Button>("menuButtons/exitButton");

		_playButton.Pressed += OnPlayPressed;
		_howToPlayButton.Pressed += OnHowToPlayPressed;
		_exitButton.Pressed += OnExitPressed;
	}

	private void OnPlayPressed()
	{
		GetTree().ChangeSceneToFile("res://ui/main_menu.tscn");
	}

	private void OnHowToPlayPressed()
	{
		GetTree().ChangeSceneToFile("res://ui/howtoplay.tscn");
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}
