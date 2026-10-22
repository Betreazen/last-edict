using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEdict.Data.Sound;

/// <summary>Затухания и нарастания переходов музыки, мс (<c>CMusicFader</c>). Журнал зонда 7.3 (<c>corpus/20261016e-av</c>, <c>-g</c>): основная
/// (карта, бриф) и временная (бой, столица) начинаются сразу полной громкостью — 64 и 25 стартов без строки <c>fade in</c>; нарастание
/// 1000 мс — только у продолжения основной (35 из 35, <c>0x5aa388</c> из <c>endTemp</c> <c>0x5aa30e</c>), и меню при входе в него открывается
/// так же — основной с продолжением (<c>menuIn</c>).</summary>
public sealed record MusicFades
{
    [JsonPropertyName("menuOut")] public int MenuOut { get; init; } = 500;
    [JsonPropertyName("menuIn")] public int MenuIn { get; init; } = 1000;
    [JsonPropertyName("briefingOut")] public int BriefingOut { get; init; } = 700;
    [JsonPropertyName("tempIn")] public int TempIn { get; init; }
    [JsonPropertyName("battleOut")] public int BattleOut { get; init; } = 1000;
    [JsonPropertyName("battleEnd")] public int BattleEnd { get; init; } = 700;
    [JsonPropertyName("capitalOut")] public int CapitalOut { get; init; } = 1500;
    [JsonPropertyName("capitalEnd")] public int CapitalEnd { get; init; } = 700;
    [JsonPropertyName("resumeIn")] public int ResumeIn { get; init; } = 1000;
}

/// <summary>
/// Музыка по ситуациям (<c>data/music/playlists.json</c>; разбор <c>CMidMusic</c> <c>0x5a9b76…0x5aa3f8</c>, <c>docs/ai-screen.md</c> §12,
/// ранг 1 по листингу): меню — <c>MENUTRCK</c>; карта — два списка <c>ImgData/imusic.dbf</c> (игровой и фоновый), первый выбирается
/// случайно, дальше списки чередуются, дорожка в списке — случайная, но не та же, что в прошлый раз (<c>0x5b92a8</c>); столица — тема
/// расы владельца, 1→2→3 по кругу с общим счётчиком на всю игру (<c>0x838508</c>); бой — <c>BATTLE01–04</c>, первая случайная, дальше
/// по кругу (<c>0x7ae178</c>). Данные — отдельно от кода: мод подменяет файл.
/// </summary>
public sealed class MusicPlan
{
    [JsonPropertyName("menu")] public IReadOnlyList<string> Menu { get; init; } = [];
    [JsonPropertyName("briefing")] public IReadOnlyList<string> Briefing { get; init; } = [];
    [JsonPropertyName("credits")] public IReadOnlyList<string> Credits { get; init; } = [];
    [JsonPropertyName("map")] public IReadOnlyList<IReadOnlyList<string>> Map { get; init; } = [];
    [JsonPropertyName("battle")] public IReadOnlyList<string> Battle { get; init; } = [];
    [JsonPropertyName("capital")] public IReadOnlyDictionary<string, IReadOnlyList<string>> Capital { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    [JsonPropertyName("fades")] public MusicFades Fades { get; init; } = new();

    public static MusicPlan Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<MusicPlan>(File.ReadAllText(path)) ?? new MusicPlan() : new MusicPlan();
}

/// <summary>Состояние выбора дорожек на весь запуск: какой список карты сейчас, последняя дорожка, счётчики столицы и боя.</summary>
public sealed class MusicRotation(MusicPlan plan, Random random)
{
    private int _mapList = -1;
    private string _lastMap = "";
    private int _capital;
    private int _battle = -1;

    /// <summary>Следующая дорожка карты: списки чередуются (первый — случайный), в списке — случайная, кроме прошлой.</summary>
    public string? NextMap()
    {
        var lists = plan.Map.Where(l => l.Count > 0).ToList();
        if (lists.Count == 0) return null;
        _mapList = _mapList < 0 ? random.Next(lists.Count) : (_mapList + 1) % lists.Count;
        var list = lists[_mapList];
        var fresh = list.Where(t => !t.Equals(_lastMap, StringComparison.OrdinalIgnoreCase)).ToList();
        var choices = fresh.Count > 0 ? fresh : [.. list];   // список из одной дорожки (или одних повторов) — повтор неизбежен
        _lastMap = choices[random.Next(choices.Count)];
        return _lastMap;
    }

    /// <summary>Тема столицы расы (<c>RaceType</c>): 1→2→3 по кругу, счётчик общий для всех рас.</summary>
    public string? NextCapital(int raceType)
    {
        if (!plan.Capital.TryGetValue(raceType.ToString(System.Globalization.CultureInfo.InvariantCulture), out var tracks) || tracks.Count == 0)
            return null;
        string track = tracks[_capital % tracks.Count];
        _capital++;
        return track;
    }

    /// <summary>Музыка боя: первая — случайная, дальше по кругу.</summary>
    public string? NextBattle()
    {
        if (plan.Battle.Count == 0) return null;
        _battle = _battle < 0 ? random.Next(plan.Battle.Count) : (_battle + 1) % plan.Battle.Count;
        return plan.Battle[_battle];
    }
}
