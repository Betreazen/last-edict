using System.Collections.Generic;
using System.IO;
using D2.Data.Sound;
using Godot;

namespace D2.Godot;

/// <summary>Звуки игры в рантайме (<c>docs/ai-screen.md</c> §9): опись <c>data/sound</c> (<see cref="SoundAssets"/>), файлы —
/// <c>assets/sound</c> (<c>tools/sound-assets</c>): MP3 — <see cref="AudioStreamMP3"/>, PCM — <see cref="AudioStreamWav"/>. Поток
/// читается один раз на звук.</summary>
public static class GameSound
{
    private static SoundAssets? _assets;
    private static readonly Dictionary<string, AudioStream?> Streams = new();

    public static SoundAssets Assets =>
        _assets ??= SoundAssets.Load(Path.Combine(GameData.GameDataDir, "sound"));

    private static UnitSounds? _units;

    /// <summary>Звуки юнитов (<c>data/sound/UnitSounds.json</c>): бой и шаг на карте.</summary>
    public static UnitSounds Units =>
        _units ??= UnitSounds.Load(Path.Combine(GameData.GameDataDir, "sound", "UnitSounds.json"));

    private static string Root => Path.Combine(GameData.AssetsRoot, "sound");

    /// <summary>Поток и длительность звука (по описи); нет в описи или на диске — (null, 0).</summary>
    public static (AudioStream? Stream, double Seconds) Load(string archive, string name)
    {
        if (Assets.Find(archive, name) is not { } info) return (null, 0);
        string path = SoundAssets.PathOf(info, Root);
        if (!Streams.TryGetValue(path, out var stream))
        {
            stream = !File.Exists(path) ? null
                : info.Codec == "mp3" ? AudioStreamMP3.LoadFromFile(path)
                : AudioStreamWav.LoadFromFile(path);
            Streams[path] = stream;
        }
        return (stream, stream is null ? 0 : info.Seconds);
    }
}
