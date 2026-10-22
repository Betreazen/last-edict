using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Map;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Файл настроек разделами с версией схемы (<see cref="SettingsFile"/>): перенос плоского файла схемы 1, круг «записал → прочитал»,
/// неизвестные ключи и битый файл, зажим значений, запись только своих полей (<see cref="MapSettings.Update"/>,
/// <see cref="MapOptionsLogic.Adopt"/>).</summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "d2-settings-file-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    public SettingsFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Каждый ключ файла схемы 1 (плоский, как его писал <c>JsonSerializer.Serialize(MapSettings)</c> до 21.10) — не по умолчанию,
    /// раздел и ключ схемы 2, куда он переходит.</summary>
    private static readonly (string Old, object Value, string Section, string Key)[] V1Keys =
    [
        ("PlayerSpeed", 2, "game", "playerSpeed"), ("OpponentSpeed", 3, "game", "opponentSpeed"),
        ("PlayAiMoves", false, "game", "showAiMoves"), ("FxVolume", 41, "audio", "fx"), ("MusicVolume", 25, "audio", "music"),
        ("Acknowledgements", 33, "audio", "voice"), ("AudioRegions", 2, "audio", "ambientSources"),
        ("ShowTurnSummary", true, "game", "turnSummary"), ("FlippedBattle", true, "battle", "flipped"),
        ("AnimatedIso", false, "display", "animatedMap"), ("IsoHighlight", false, "display", "cellHighlight"),
        ("IsoClouds", false, "display", "clouds"), ("BattleSpeed", 4, "battle", "speed"), ("BattleAnim", false, "battle", "animation"),
        ("Banners", true, "display", "banners"), ("Scaling", "integer", "display", "scaling"), ("AutoSave", false, "game", "autoSave"),
        ("ConfirmEndOfTurn", false, "game", "confirmEndOfTurn"), ("SmoothScroll", false, "game", "smoothScroll"),
        ("StackSmoothScroll", false, "game", "stackSmoothScroll"), ("LowEndVideoCard", true, "display", "lowEndVideoCard"),
        ("HighAlpha", false, "display", "highAlpha"), ("PathBattle", false, "game", "pathBattle"),
        ("InversedPalMap", true, "game", "invertedMinimap"), ("ScrollSpeed", 50, "game", "scrollSpeed"), ("Intro", false, "game", "intro"),
        ("QueueStrip", false, "battle", "queueStrip"),   // плоский файл с полосой очереди (main 21.10, до разделов)
    ];

    private static JsonValue Node(object value) => value switch
    {
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        string t => JsonValue.Create(t),
        _ => throw new ArgumentException(value.GetType().Name),
    };

    [Fact]
    public void V1File_EveryKeyMovesToItsSection_WithSameValue()
    {
        // Файл прошлой версии: все 27 ключей плоско, плюс ключ, которого порт не знает.
        var old = new JsonObject();
        foreach (var (key, value, _, _) in V1Keys) old[key] = Node(value);
        old["SomethingRemoved"] = 7;
        File.WriteAllText(FilePath, old.ToJsonString());

        var read = MapSettings.Load(FilePath);
        var expected = new MapSettings(PlayerSpeed: 2, OpponentSpeed: 3, PlayAiMoves: false, FxVolume: 41, MusicVolume: 25,
            Acknowledgements: 33, AudioRegions: 2, ShowTurnSummary: true, FlippedBattle: true, AnimatedIso: false, IsoHighlight: false,
            IsoClouds: false, BattleSpeed: 4, BattleAnim: false, Banners: true, Scaling: "integer", AutoSave: false, ConfirmEndOfTurn: false,
            SmoothScroll: false, StackSmoothScroll: false, LowEndVideoCard: true, HighAlpha: false, PathBattle: false, InversedPalMap: true,
            ScrollSpeed: 50, Intro: false, QueueStrip: false);
        Assert.Equal(expected, read);
        // Новые поля схемы 2 у старого файла — умолчания: звучание и окно как прежде.
        Assert.Equal((100, 100, "windowed", true, 0), (read.MasterVolume, read.AmbientVolume, read.WindowMode, read.VSync, read.MaxFps));

        // Запись — новой схемой: каждый прежний ключ в своём разделе с тем же значением, плоских ключей нет.
        read.Save(FilePath);
        var written = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.Equal(SettingsFile.Version, (int)written["version"]!);
        foreach (var (key, value, section, newKey) in V1Keys)
        {
            Assert.False(written.ContainsKey(key), key);
            Assert.Equal(Node(value).ToJsonString(), written[section]![newKey]!.ToJsonString());
        }
        Assert.Equal(expected, MapSettings.Load(FilePath));
    }

    /// <summary>Раскладка покрывает каждое поле <see cref="MapSettings"/> ровно раз: новое поле без строки в <see cref="SettingsFile.Layout"/>
    /// не потерялось бы молча при записи. Ключи разделов не повторяются.</summary>
    [Fact]
    public void Layout_CoversEveryFieldOnce()
    {
        var fields = JsonSerializer.SerializeToNode(new MapSettings())!.AsObject().Select(p => p.Key).Order().ToList();
        var laid = SettingsFile.Layout.Select(s => s.Field).Order().ToList();
        Assert.Equal(fields, laid);
        Assert.Equal(SettingsFile.Layout.Count, SettingsFile.Layout.Select(s => (s.Section, s.Key)).Distinct().Count());
        Assert.Equal(["audio", "battle", "controls", "display", "game", "interface"], SettingsFile.Layout.Select(s => s.Section).Distinct().Order());
        Assert.Contains(SettingsFile.Layout, s => s is { Section: "battle", Key: "queueStrip" });   // место полосы очереди ходов
    }

    [Fact]
    public void NewSchema_RoundTrip_VersionInFile()
    {
        Assert.Equal(new MapSettings(), MapSettings.Load(FilePath));   // нет файла — умолчания
        var settings = new MapSettings(PlayerSpeed: 3, FxVolume: 127, Banners: true, Scaling: "nearest", MasterVolume: 40, AmbientVolume: 0,
                                       WindowMode: "borderless", VSync: false, MaxFps: 144, BattleSpeed: 1);
        settings.Save(FilePath);
        Assert.False(File.Exists(FilePath + ".tmp"));
        Assert.Equal(settings, MapSettings.Load(FilePath));
        var written = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.Equal(2, (int)written["version"]!);
        Assert.Equal(["version", "display", "audio", "game", "battle", "interface"], written.Select(p => p.Key));
        Assert.Equal(("borderless", false, 144), ((string)written["display"]!["windowMode"]!, (bool)written["display"]!["vsync"]!,
                                                  (int)written["display"]!["maxFps"]!));
        Assert.Equal((40, 0, 127), ((int)written["audio"]!["master"]!, (int)written["audio"]!["ambient"]!, (int)written["audio"]!["fx"]!));
    }

    [Fact]
    public void UnknownKeysAreSkipped_BrokenFileGivesDefaults()
    {
        File.WriteAllText(FilePath, """
            { "version": 3, "display": { "windowMode": "fullscreen", "hdr": true }, "audio": 5,
              "game": { "playerSpeed": 2, "showAiMoves": null }, "network": { "port": 1 }, "PlayerSpeed": 3 }
            """);
        // Более новая версия: известное читается, неизвестные раздел и ключи, раздел не того вида и пустое значение — пропуск;
        // плоский ключ схемы 1 рядом с разделами не читается.
        Assert.Equal(new MapSettings(PlayerSpeed: 2, WindowMode: "fullscreen"), MapSettings.Load(FilePath));
        foreach (string broken in new[] { "{ не json", "[1, 2]", "\"строка\"", """{"version": 2, "game": {"playerSpeed": "быстро"}}""" })
        {
            File.WriteAllText(FilePath, broken);
            Assert.Equal(new MapSettings(), MapSettings.Load(FilePath));
        }
        File.WriteAllText(FilePath, "null");
        Assert.Equal(new MapSettings(), MapSettings.Load(FilePath));
    }

    [Fact]
    public void ValuesAreClampedOnRead()
    {
        File.WriteAllText(FilePath, """
            { "version": 2, "display": { "windowMode": "Окно", "maxFps": 10 },
              "audio": { "master": 150, "ambient": -4, "fx": 500, "music": -1, "voice": 101, "ambientSources": 9 },
              "battle": { "speed": 0 } }
            """);
        var read = MapSettings.Load(FilePath);
        Assert.Equal(("windowed", MapSettings.MinFps, 100, 0, 127, 0, 100, 4, 1),
                     (read.WindowMode, read.MaxFps, read.MasterVolume, read.AmbientVolume, read.FxVolume, read.MusicVolume,
                      read.Acknowledgements, read.AudioRegions, read.BattleSpeed));
        Assert.Equal([0, 0, 30, 30, 144, 360, 360], new[] { -5, 0, 1, 30, 144, 360, 1000 }.Select(f => new MapSettings(MaxFps: f).Clamped().MaxFps));
        Assert.Equal(["windowed", "borderless", "fullscreen", "fullscreen", "windowed"],
                     new[] { "windowed", "borderless", "FullScreen", " fullscreen ", "exclusive" }
                         .Select(m => new MapSettings(WindowMode: m).Clamped().WindowMode));
        Assert.Equal(DisplayMode.Borderless, new MapSettings(WindowMode: "BORDERLESS").Display);
    }

    /// <summary>Хозяева файла пишут только своё: окно настроек — свои флажки и счётчики, <c>Alt+Enter</c> — режим окна; прочее в файле
    /// остаётся, даже если у хозяина в памяти оно старое.</summary>
    [Fact]
    public void Update_And_Adopt_WriteOnlyOwnFields()
    {
        new MapSettings(Banners: true, MasterVolume: 50).Save(FilePath);
        var screenCopy = MapSettings.Load(FilePath);   // копия окна настроек, прочитанная при старте
        MapSettings.Update(FilePath, s => s with { WindowMode = "fullscreen" });   // Alt+Enter
        var window = screenCopy with { OpponentSpeed = 3, AutoSave = false, FxVolume = 0 };
        var written = MapSettings.Update(FilePath, onDisk => MapOptionsLogic.Adopt(window, onDisk));
        Assert.Equal(new MapSettings(OpponentSpeed: 3, AutoSave: false, FxVolume: 0, Banners: true, MasterVolume: 50, WindowMode: "fullscreen"),
                     written);
        Assert.Equal(written, MapSettings.Load(FilePath));
        // Поля не окна копия окна не переносит: знамёна, громкость общая, режим окна — с диска.
        var adopted = MapOptionsLogic.Adopt(new MapSettings(Banners: false, MasterVolume: 1, WindowMode: "borderless", PlayerSpeed: 2),
                                            new MapSettings(Banners: true, MasterVolume: 70, WindowMode: "fullscreen"));
        Assert.Equal(new MapSettings(Banners: true, MasterVolume: 70, WindowMode: "fullscreen", PlayerSpeed: 2), adopted);
    }

    /// <summary>Файл не прочитался (занят другим процессом) — запись не идёт и файл цел; испорченный файл — умолчания плюс изменение.</summary>
    [Fact]
    public void Update_UnreadableFileIsKept_BrokenFileIsReplaced()
    {
        new MapSettings(Banners: true).Save(FilePath);
        string before = File.ReadAllText(FilePath);
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => MapSettings.Update(FilePath, s => s with { WindowMode = "fullscreen" }));
        Assert.Equal(before, File.ReadAllText(FilePath));
        File.WriteAllText(FilePath, "{ не json");
        Assert.Equal(new MapSettings(WindowMode: "fullscreen"), MapSettings.Update(FilePath, s => s with { WindowMode = "fullscreen" }));
        Assert.Equal(new MapSettings(WindowMode: "fullscreen"), MapSettings.Load(FilePath));
    }
}
