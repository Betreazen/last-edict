using System.Text.Json;
using System.Text.Json.Serialization;

namespace LastEdict.Data.Sound;

/// <summary>Звуки юнита (<c>SNDINFO.DAT</c> в <c>Battle.wdt</c>): замах и его кадр, кадр удара, эффект атакующего и его кадр, удар по цели,
/// «больно» цели, шаг на карте. Имя с <c>.LST</c> — список вариантов (<see cref="UnitSounds.Pick"/>).</summary>
public sealed record UnitSoundInfo
{
    [JsonPropertyName("attack")] public string Attack { get; init; } = "";
    [JsonPropertyName("attackFrame")] public int AttackFrame { get; init; }
    [JsonPropertyName("hitFrame")] public int HitFrame { get; init; }
    [JsonPropertyName("effect")] public string Effect { get; init; } = "";
    [JsonPropertyName("effectFrame")] public int EffectFrame { get; init; }
    [JsonPropertyName("hit")] public string Hit { get; init; } = "";
    [JsonPropertyName("hurt")] public string Hurt { get; init; } = "";
    [JsonPropertyName("walk")] public string Walk { get; init; } = "";
}

/// <summary>
/// Звуки юнитов (<c>data/sound/UnitSounds.json</c>, импорт <c>tools/sound-assets</c>; разбор <c>0x5c0be8</c>/<c>0x5c0fcc</c>,
/// <c>docs/ai-screen.md</c> §12, ранг 1): по id юнита — имена звуков боя и шага; имя с расширением <c>.LST</c> — список вариантов из
/// <c>Battle.wdt</c>, вариант — <c>rand() % n</c> в момент запроса. Юнита нет — пусто.
/// </summary>
public sealed class UnitSounds
{
    [JsonPropertyName("units")] public IReadOnlyDictionary<string, UnitSoundInfo> Units { get; init; } = new Dictionary<string, UnitSoundInfo>();
    [JsonPropertyName("lists")] public IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public static UnitSounds Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<UnitSounds>(File.ReadAllText(path)) ?? new UnitSounds() : new UnitSounds();

    /// <summary>Звуки юнита (регистр id не значим); нет — null.</summary>
    public UnitSoundInfo? Of(string unitId) =>
        Units.TryGetValue(unitId.ToUpperInvariant(), out var info) ? info : null;

    /// <summary>Имя звука из поля: пусто — null; <c>X.LST</c> — вариант списка <c>X</c> (<paramref name="random"/>: <c>rand() % n</c>);
    /// иначе — само имя.</summary>
    public string? Pick(string field, Func<int, int> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (string.IsNullOrWhiteSpace(field)) return null;
        if (!field.EndsWith(".LST", StringComparison.OrdinalIgnoreCase)) return field.ToUpperInvariant();
        return Lists.TryGetValue(field[..^4].ToUpperInvariant(), out var list) && list.Count > 0 ? list[random(list.Count)] : null;
    }

    /// <summary>Все имена поля: <c>X.LST</c> — список <c>X</c>, иначе само имя; пусто — ничего.</summary>
    public IEnumerable<string> NamesOf(string field)
    {
        if (string.IsNullOrWhiteSpace(field)) return [];
        if (!field.EndsWith(".LST", StringComparison.OrdinalIgnoreCase)) return [field.ToUpperInvariant()];
        return Lists.TryGetValue(field[..^4].ToUpperInvariant(), out var list) ? list.Select(n => n.ToUpperInvariant()) : [];
    }

    /// <summary>Роли звука <paramref name="name"/> у юнита <paramref name="unitType"/>: <c>attack</c> (замах), <c>effect</c> (эффект
    /// ходящего), <c>hit</c> (удар по цели) — по его спискам; не его звук — пусто. Одно имя бывает в нескольких списках.</summary>
    public IEnumerable<string> RolesOf(string unitType, string name)
    {
        if (Of(unitType) is not { } info) yield break;
        string n = name.ToUpperInvariant();
        if (NamesOf(info.Attack).Contains(n)) yield return "attack";
        if (NamesOf(info.Effect).Contains(n)) yield return "effect";
        if (NamesOf(info.Hit).Contains(n)) yield return "hit";
    }

    /// <summary>Имя — звук какого-нибудь юнита (замах, эффект, удар, «больно»): так отличаются звуки юнитов от звуков боя без юнита
    /// (<c>DEATH</c>, классы атак, уход призванного).</summary>
    public bool IsUnitSound(string name)
    {
        _unitNames ??= [.. Units.Values.SelectMany(u => NamesOf(u.Attack).Concat(NamesOf(u.Effect)).Concat(NamesOf(u.Hit)).Concat(NamesOf(u.Hurt)))];
        return _unitNames.Contains(name.ToUpperInvariant());
    }

    private HashSet<string>? _unitNames;

    /// <summary>Имя — из списков шага какого-нибудь юнита (<c>walk</c>: шаги лидера на карте).</summary>
    public bool IsWalkSound(string name)
    {
        _walkNames ??= [.. Units.Values.SelectMany(u => NamesOf(u.Walk))];   // как Pick: без .LST — само имя
        return _walkNames.Contains(name.ToUpperInvariant());
    }

    private HashSet<string>? _walkNames;
}
