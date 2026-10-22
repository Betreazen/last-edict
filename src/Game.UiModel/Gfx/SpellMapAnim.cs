namespace LastEdict.Data.Gfx;

/// <summary>Одна картинка литья: анимация из <c>IsoSpell</c>, слой карты и ключевой кадр.</summary>
/// <param name="Front">Поверх объектов карты (слои <c>FRONT</c>, <c>OVER</c>, <c>TOP</c>); иначе под ними (<c>GROUND</c>, <c>UNDER</c>).</param>
/// <param name="Layer">Номер слоя движка (<c>SPELLS_GROUND1</c> 12 … <c>SPELLS_TOP2</c> 1539, <c>docs/map-render.md</c>).</param>
/// <param name="KeyFrame">Кадр, на котором движок применяет изменения карты команды (999 — никогда).</param>
public sealed record SpellMapPart(GfxAnim Anim, int Layer, bool Front, int KeyFrame);

/// <summary>
/// Литьё заклинания на карте (<c>docs/ai-screen.md</c> §6; разбор <c>Discipl2.exe</c>, ранг 1 по листингу): задача
/// <c>CMidAnim2Spell</c> (<c>0x4f31d0</c>) берёт из <c>IsoSpell</c> все анимации, чьё имя — id заклинания и ещё 5 знаков
/// (<c>0x5c6d85</c>): знаки 10–11 — слой (<c>0x5b5f7c</c>: <c>G1/G2</c> — <c>SPELLS_GROUND1/2</c>, <c>U</c> — <c>UNDER</c>,
/// <c>F</c> — <c>FRONT</c>, <c>O</c> — <c>OVER</c>, <c>T</c> — <c>TOP</c>, прочее — <c>SPELLS_TOP2</c>), знаки 12–14 — ключевой кадр
/// (<c>0x5b61cb</c>). Больше 4 имён — анимации нет (<c>0x5b5a3a</c>). Все картинки ставятся в центр клетки литья без сдвига,
/// играют один раз с кадра 0; задача длится наибольшее число кадров (около 66 мс на кадр), движок ждёт её конца.
/// </summary>
public static class SpellMapAnim
{
    public const string Archive = "IsoSpell";
    private const int MaxParts = 4;
    private const int IdLength = 10;

    private static readonly Dictionary<string, int> Layers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G1"] = 12, ["G2"] = 13, ["U1"] = 338, ["U2"] = 339, ["F1"] = 488, ["F2"] = 489,
        ["O1"] = 1031, ["O2"] = 1032, ["T1"] = 1538, ["T2"] = 1539,
    };

    private const int DefaultLayer = 1539;
    private const int FrontFrom = 488;

    /// <summary>Картинки литья заклинания; нет анимаций или их больше четырёх — пусто (заглушку <c>CBlankSpellImg</c> порт не
    /// рисует: у всех заклинаний оригинала анимации есть).</summary>
    public static IReadOnlyList<SpellMapPart> Parts(GfxAssets gfx, string spellId)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        if (spellId is not { Length: IdLength } || gfx.Archive(Archive) is not { } archive) return [];
        var anims = archive.Anims.Where(a => a.Name.Length == IdLength + 5 && a.Name.StartsWith(spellId, StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (anims.Count is 0 or > MaxParts) return [];
        return [.. anims.Select(a =>
        {
            int layer = Layers.GetValueOrDefault(a.Name.Substring(IdLength, 2), DefaultLayer);
            int key = int.TryParse(a.Name.AsSpan(IdLength + 2, 3), System.Globalization.NumberStyles.None,
                                   System.Globalization.CultureInfo.InvariantCulture, out int k) ? k : 0;
            return new SpellMapPart(gfx.Anim(Archive, a.Name) ?? a, layer, layer >= FrontFrom, key);
        })];
    }

    /// <summary>Сколько кадров длится литьё: наибольшее число кадров картинок, не меньше одного.</summary>
    public static int Length(IReadOnlyList<SpellMapPart> parts) =>
        Math.Max(1, parts.Count == 0 ? 0 : parts.Max(p => p.Anim.Frames.Count));

    /// <summary>Клетки, из которых видно литьё чужого игрока (<c>0x4f4ce9</c>: хоть одна открыта у зрителя) — сдвиги от точки
    /// литья.</summary>
    public static IReadOnlyList<(int Dx, int Dy)> SeenFrom { get; } =
        [(-1, -2), (0, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (-2, 0), (-1, 0), (0, 0)];
}
