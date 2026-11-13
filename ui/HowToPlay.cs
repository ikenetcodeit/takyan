using Godot;
using System;

public partial class HowToPlay : Control {
	public override void _Ready() {
		var backBtn = GetNodeOrNull<Button>("ColorRect/backButton");
		if (backBtn != null) {
			backBtn.Pressed += OnBackPressed;
		}
	}

	private void OnBackPressed() {
		GetTree().ChangeSceneToFile("res://ui/test.tscn");
	}
}
