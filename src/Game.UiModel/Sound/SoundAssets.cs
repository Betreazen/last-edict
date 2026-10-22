using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEdict.Data.Sound;

/// <summary>Звук из архива игры: имя, файл (от <c>assets/sound</c>), кодек (<c>mp3</c> — байты MP3, <c>pcm16</c> — WAV),
/// частота, каналы, число сэмплов, длительность в секундах и хеш исходной записи.</summary>
public sealed record SoundInfo
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("codec")] public string Codec { get; init; } = "";
    [JsonPropertyName("rate")] public int Rate { get; init; }
    [JsonPropertyName("channels")] public int Channels { get; init; }
    [JsonPropertyName("samples")] public int Samples { get; init; }
    [JsonPropertyName("seconds")] public double Seconds { get; init; }
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
}

/// <summary>Опись одного звукового архива (<c>data/sound/&lt;Архив&gt;.json</c>, собирает <c>tools/sound-assets</c>).</summary>
public sealed record SoundArchive
{
    [JsonPropertyName("archive")] public required string Archive { get; init; }
    [JsonPropertyName("sounds")] public IReadOnlyList<SoundInfo> Sounds { get; init; } = [];

    /// <summary>Имя архива без расширения (<c>Midgard</c>, <c>Battle</c>).</summary>
    [JsonIgnore] public string Name => Path.GetFileNameWithoutExtension(Archive);
}

/// <summary>Звуки игры: описи архивов <c>Sounds/*.wdb</c> (<c>docs/ai-screen.md</c> §9). Звук опознаётся парой «архив + имя»,
/// регистр не значим. Файлы — в <c>assets/sound</c> (вне git).</summary>
public sealed class SoundAssets
{
    private readonly Dictionary<(string, string), SoundInfo> _sounds = [];

    private SoundAssets(IEnumerable<SoundArchive> archives)
    {
        foreach (var a in archives)
            foreach (var s in a.Sounds) _sounds[Key(a.Name, s.Name)] = s;
    }

    public static SoundAssets Empty { get; } = new([]);

    public int Count => _sounds.Count;

    public SoundInfo? Find(string archive, string name) => _sounds.GetValueOrDefault(Key(archive, name));

    /// <summary>Путь к файлу звука: корень <c>assets/sound</c> плюс <see cref="SoundInfo.File"/>.</summary>
    public static string PathOf(SoundInfo sound, string root)
    {
        ArgumentNullException.ThrowIfNull(sound);
        return Path.Combine(root, sound.File.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Все описи каталога (<c>*.json</c> с полем <c>archive</c>; прочие файлы каталога — данные звука, как
    /// <c>UnitSounds.json</c>, — пропускаются); нет каталога — пусто.</summary>
    public static SoundAssets Load(string dir) =>
        new(Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal)
                       .Select(p => File.ReadAllText(p))
                       .Where(text => JsonDocument.Parse(text).RootElement.TryGetProperty("archive", out _))
                       .Select(text => JsonSerializer.Deserialize<SoundArchive>(text) ?? throw new FormatException("пустая опись звука"))
            : []);

    private static (string, string) Key(string archive, string name) => (archive.ToUpperInvariant(), name.ToUpperInvariant());
}
