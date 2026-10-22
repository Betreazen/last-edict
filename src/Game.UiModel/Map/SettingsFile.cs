using System.Text.Json;
using System.Text.Json.Nodes;

namespace LastEdict.Data.Map;

/// <summary>
/// Файл настроек порта (<c>user://settings.json</c>) — слой файла над <see cref="MapSettings"/>: в памяти настройки остаются одной плоской
/// записью (её берут окно настроек, звук, карта и меню), в файле — разделы с версией схемы:
/// <code>
/// { "version": 2,
///   "display": { "windowMode": "windowed", "vsync": true, "maxFps": 0, "scaling": "sharp", "animatedMap": true, … },
///   "audio":   { "master": 100, "music": 80, "fx": 83, "voice": 66, "ambient": 100, "ambientSources": 3 },
///   "game":    { "playerSpeed": 1, "opponentSpeed": 1, "showAiMoves": true, "autoSave": true, … },
///   "battle":  { "speed": 2, "animation": true, "flipped": false, "queueStrip": true },
///   "interface": { "language": "ru" } }
/// </code>
/// Разделы — будущие вкладки окна настроек (<c>docs/own-version-roadmap.md</c> §3.3): «Экран» (<c>display</c>: окно, кадры, вписывание
/// и графика карты), «Звук» (<c>audio</c>: громкости шин 0…127 у музыки и звуков, как <c>Disciple.ini</c>, голоса 0…100, общая и фоны мест
/// 0…100 %, число фонов мест), «Игра» (<c>game</c>: скорости, показ ходов ИИ, итоги хода, автосохранение, прокрутка, путь через бой,
/// мини-карта, ролик вступления), «Бой» (<c>battle</c>: скорость, анимация, стороны, полоса очереди ходов). Где какое поле — таблица
/// <see cref="Layout"/>; умолчания — у <see cref="MapSettings"/> (ключа нет — умолчание поля).
///
/// <para>Схемы: 1 — плоский файл без <c>version</c> (до 21.10.2026: ключи — имена полей <see cref="MapSettings"/>, <c>PlayerSpeed</c>,
/// <c>FxVolume</c>…) — переносится без потерь, каждый ключ в свой раздел; 2 — разделы. Читается любая; запись — всегда схемой
/// <see cref="Version"/>. Неизвестные ключи и разделы (в том числе от более новой версии) при чтении пропускаются и при записи не
/// сохраняются, значения — в границах
/// (<see cref="MapSettings.Clamped"/>). Не JSON, не объект или значение не того типа — исключение <see cref="JsonException"/>
/// (<see cref="MapSettings.Load"/> даёт умолчания).</para>
/// </summary>
public static class SettingsFile
{
    /// <summary>Версия схемы, которой файл пишется.</summary>
    public const int Version = 2;

    public const string VersionKey = "version", Display = "display", Audio = "audio", Game = "game", Battle = "battle", Interface = "interface";

    /// <summary>Место поля в файле: раздел, ключ в разделе и поле <see cref="MapSettings"/> (имя ключа схемы 1).</summary>
    public sealed record Slot(string Section, string Key, string Field);

    /// <summary>Раскладка файла по разделам. Новое поле <see cref="MapSettings"/> — новая строка здесь (тест сверяет, что строк ровно по
    /// полю; поле без строки при записи потерялось бы).</summary>
    public static readonly IReadOnlyList<Slot> Layout =
    [
        new(Display, "windowMode", nameof(MapSettings.WindowMode)),
        new(Display, "vsync", nameof(MapSettings.VSync)),
        new(Display, "maxFps", nameof(MapSettings.MaxFps)),
        new(Display, "scaling", nameof(MapSettings.Scaling)),
        new(Display, "animatedMap", nameof(MapSettings.AnimatedIso)),
        new(Display, "cellHighlight", nameof(MapSettings.IsoHighlight)),
        new(Display, "clouds", nameof(MapSettings.IsoClouds)),
        new(Display, "banners", nameof(MapSettings.Banners)),
        new(Display, "highAlpha", nameof(MapSettings.HighAlpha)),
        new(Display, "lowEndVideoCard", nameof(MapSettings.LowEndVideoCard)),
        new(Audio, "master", nameof(MapSettings.MasterVolume)),
        new(Audio, "music", nameof(MapSettings.MusicVolume)),
        new(Audio, "fx", nameof(MapSettings.FxVolume)),
        new(Audio, "voice", nameof(MapSettings.Acknowledgements)),
        new(Audio, "ambient", nameof(MapSettings.AmbientVolume)),
        new(Audio, "ambientSources", nameof(MapSettings.AudioRegions)),
        new(Game, "playerSpeed", nameof(MapSettings.PlayerSpeed)),
        new(Game, "opponentSpeed", nameof(MapSettings.OpponentSpeed)),
        new(Game, "showAiMoves", nameof(MapSettings.PlayAiMoves)),
        new(Game, "turnSummary", nameof(MapSettings.ShowTurnSummary)),
        new(Game, "autoSave", nameof(MapSettings.AutoSave)),
        new(Game, "confirmEndOfTurn", nameof(MapSettings.ConfirmEndOfTurn)),
        new(Game, "scrollSpeed", nameof(MapSettings.ScrollSpeed)),
        new(Game, "smoothScroll", nameof(MapSettings.SmoothScroll)),
        new(Game, "stackSmoothScroll", nameof(MapSettings.StackSmoothScroll)),
        new(Game, "pathBattle", nameof(MapSettings.PathBattle)),
        new(Game, "invertedMinimap", nameof(MapSettings.InversedPalMap)),
        new(Game, "intro", nameof(MapSettings.Intro)),
        new(Battle, "speed", nameof(MapSettings.BattleSpeed)),
        new(Battle, "animation", nameof(MapSettings.BattleAnim)),
        new(Battle, "flipped", nameof(MapSettings.FlippedBattle)),
        new(Battle, "queueStrip", nameof(MapSettings.QueueStrip)),
        new(Interface, "language", nameof(MapSettings.Language)),
    ];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Настройки из текста файла любой схемы, в границах.</summary>
    public static MapSettings Parse(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is null) return new MapSettings();   // «null» — как пустой файл схемы 1
        if (node is not JsonObject root) throw new JsonException("файл настроек — не объект JSON");
        var flat = root.ContainsKey(VersionKey) ? Flatten(root) : root;
        return (flat.Deserialize<MapSettings>() ?? new MapSettings()).Clamped();
    }

    /// <summary>Текст файла схемы <see cref="Version"/>: разделы в порядке <see cref="Layout"/>, значения в границах.</summary>
    public static string Format(MapSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fields = JsonSerializer.SerializeToNode(settings.Clamped())!.AsObject();
        var root = new JsonObject { [VersionKey] = Version };
        foreach (var slot in Layout)
        {
            if (!fields.TryGetPropertyValue(slot.Field, out var value)) continue;
            if (root[slot.Section] is not JsonObject section) root[slot.Section] = section = new JsonObject();
            section[slot.Key] = value?.DeepClone();
        }
        return root.ToJsonString(Indented);
    }

    /// <summary>Разделы → плоский объект с именами полей; чего нет в <see cref="Layout"/> и пустые (<c>null</c>) значения — пропускаются.</summary>
    private static JsonObject Flatten(JsonObject root)
    {
        var flat = new JsonObject();
        foreach (var slot in Layout)
            if (root[slot.Section] is JsonObject section && section.TryGetPropertyValue(slot.Key, out var value) && value is not null)
                flat[slot.Field] = value.DeepClone();
        return flat;
    }
}
