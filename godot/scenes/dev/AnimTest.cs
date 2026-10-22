using Godot;

namespace D2.Godot;

public partial class AnimTest : AnimatedSprite2D
{
	public override void _Ready()
	{
		string animsRoot = $"{GameData.GfxRoot}/BatUnits/anims";
		string? animDir = FindIdleAnimDir(animsRoot);

		var frames = new SpriteFrames();
		frames.AddAnimation("idle");
		frames.SetAnimationLoopMode("idle", SpriteFrames.LoopMode.Linear);
		frames.SetAnimationSpeed("idle", 10);

		int n = animDir != null ? LoadFrames(animDir, frames) : 0;

		if (n == 0)
		{
			GD.PrintErr($"AnimTest: no frames in {animDir ?? animsRoot}");
			GetTree().Quit(1);
			return;
		}

		SpriteFrames = frames;
		Play("idle");
		GD.Print($"frames: {n}");

		Position = GetViewportRect().Size / 2;
		Scale = new Vector2(2, 2);

		bool show = false;
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg == "--show")
			{
				show = true;
			}
		}

		if (!show)
		{
			GetTree().Quit();
		}
	}

	private static string? FindIdleAnimDir(string animsRoot)
	{
		using DirAccess? dir = DirAccess.Open(animsRoot);
		if (dir == null)
		{
			return null;
		}

		dir.ListDirBegin();
		for (string name = dir.GetNext(); name != ""; name = dir.GetNext())
		{
			if (dir.CurrentIsDir() && name.Contains("IDLEA1A"))
			{
				dir.ListDirEnd();
				return $"{animsRoot}/{name}";
			}
		}

		dir.ListDirEnd();
		return null;
	}

	// Кадры — по описи (data/gfx): файл сборки может быть только занятой частью холста (Г19), холст восстанавливает GfxTextures.Canvas.
	private static int LoadFrames(string animDir, SpriteFrames frames)
	{
		if (GameData.Content.Gfx.Anim("BatUnits", System.IO.Path.GetFileName(animDir)) is not { } anim)
		{
			return 0;
		}

		foreach (string path in D2.Data.Gfx.GfxAssets.FramePaths(anim, GameData.GfxRoot))
		{
			if (System.IO.File.Exists(path))
			{
				frames.AddFrame("idle", ImageTexture.CreateFromImage(GfxTextures.Canvas(path)));
			}
		}

		return frames.GetFrameCount("idle");
	}
}
