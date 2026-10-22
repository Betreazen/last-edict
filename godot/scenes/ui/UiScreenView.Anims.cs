using System;
using System.Collections.Generic;
using Godot;

namespace LastEdict.Godot;

/// <summary>Анимации интерфейса: картинка, чей кадр — имя анимации из описи (<c>_FIREFLY</c>, <c>_WATER</c>, <c>DLG_WAIT_HOURGLASS</c>;
/// кадры — <c>анимация/NNN</c>), крутится по кругу, 66 мс на кадр — как все анимации общего аниматора движка (медленная группа,
/// <c>0x51d871</c>). Сборка без окна (<c>--png</c>) рисует первый кадр.</summary>
public partial class UiScreenView
{
    private const int AnimFrameMs = 66;

    private readonly Dictionary<string, string[]?> _animFrames = new(StringComparer.OrdinalIgnoreCase);
    private int _animClock;

    /// <summary>Кадры анимации по имени кадра элемента; <c>null</c> — это обычный кадр.</summary>
    private string[]? AnimFrames(string frame)
    {
        if (_animFrames.TryGetValue(frame, out var known)) return known;
        string[]? frames = null;
        if (frame.Length > 0 && !_assets.ContainsKey(frame) && _assets.ContainsKey($"{frame}/000"))
        {
            var list = new List<string>();
            for (int i = 0; _assets.ContainsKey($"{frame}/{i:D3}"); i++) list.Add($"{frame}/{i:D3}");
            frames = [.. list];
        }
        _animFrames[frame] = frames;
        return frames;
    }

    /// <summary>Прошло <paramref name="ms"/>: на каждом тике аниматора — следующий кадр у всех видимых анимаций экрана.</summary>
    private void Animate(int ms)
    {
        int before = _animClock / AnimFrameMs;
        _animClock += ms;
        int tick = _animClock / AnimFrameMs;
        if (tick == before) return;
        foreach (var sprite in _sprites.Values)
        {
            if (!sprite.Visible || AnimFrames((string)sprite.GetMeta(FrameMeta)) is not { Length: > 1 } frames) continue;
            if (FrameImage(frames[tick % frames.Length], out _) is { } image) sprite.Texture = Texture(image);
        }
    }
}
