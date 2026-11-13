using Godot;
using System;

public class GameCamera
{
	private Camera2D _camera;
	private float _shakeIntensity = 0.0f;

	public GameCamera(Camera2D camera)
	{
		_camera = camera;
		
		// Zoom the camera in so it actually has room to pan back and forth!
		_camera.Zoom = new Vector2(1.1f, 1.1f);
		
		// Restrict camera view to the world bounds
		_camera.LimitLeft = -15;
		_camera.LimitRight = 1980;
		_camera.LimitBottom = 550;
		_camera.LimitTop = -150;
	}

	public void ProcessShake(double delta)
	{
		if (_camera == null) return;

		if (_shakeIntensity > 0.1f)
		{
			_shakeIntensity = Mathf.Lerp(_shakeIntensity, 0.0f, (float)delta * 8.0f);
			_camera.Offset = new Vector2((float)GD.RandRange(-_shakeIntensity, _shakeIntensity), (float)GD.RandRange(-_shakeIntensity, _shakeIntensity));
		}
		else
		{
			_camera.Offset = Vector2.Zero;
			_shakeIntensity = 0.0f;
		}
	}

	public void AddShake(float intensity)
	{
		_shakeIntensity = intensity;
	}
}
