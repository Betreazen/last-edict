using D2.Data.Sound;

namespace D2.Godot;

/// <summary>Звуки окон карты (<see cref="WindowSounds"/>, <c>docs/ai-screen.md</c> §12): открытие и закрытие — по стеку окон.</summary>
public partial class MapScreen
{
    private void WindowSound(string screen, bool opened)
    {
        if ((opened ? WindowSounds.Open(screen) : WindowSounds.Close(screen)) is { } name)
            GameAudio.Of(this).PlayFx(WindowSounds.Archive, name);
    }
}
