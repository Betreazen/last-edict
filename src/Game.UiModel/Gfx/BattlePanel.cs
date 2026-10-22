using D2.Data.Ui;
using D2.Rules.Battle;

namespace D2.Data.Gfx;

/// <summary>Метки модификаторов для значков 13–15: заклинание-усиление, заклинание-ослабление, зелье (id модификаторов, <c>0x5ea500</c>).</summary>
public sealed record BattleMarkSets(IReadOnlySet<string> SpellBoost, IReadOnlySet<string> SpellLower, IReadOnlySet<string> Potion)
{
    public static readonly BattleMarkSets None = new(new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

    private static (object Book, BattleMarkSets Marks)? _last;

    /// <summary>По книге правил (один раз на книгу): зелья-усиления (<c>0x5ea5e7</c>), заклинания усиления и ослабления (<c>0x5ea6c1</c>).
    /// Те же значки — у ячеек воинов в окнах.</summary>
    public static BattleMarkSets Of(D2.Data.Play.GameBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (_last is { } last && ReferenceEquals(last.Book, book)) return last.Marks;
        static HashSet<string> Ids(IEnumerable<string> ids) => ids.Where(m => m.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var marks = new BattleMarkSets(
            Ids(book.Spells.Values.Where(s => s.Category == D2.Rules.Magic.SpellCategory.Boost).Select(s => s.ModifierId)),
            Ids(book.Spells.Values.Where(s => s.Category == D2.Rules.Magic.SpellCategory.Lower).Select(s => s.ModifierId)),
            Ids(book.Items.Values.Where(i => i.Category == D2.Rules.Magic.ItemCategory.PotionBoost).Select(i => i.PotionModifierId)));
        _last = (book, marks);
        return marks;
    }
}

/// <summary>Значок панели: имя кадра в <c>Battle.ff</c> и место в прямоугольнике портрета.</summary>
public readonly record struct BattlePanelIcon(string Name, int X, int Y);

/// <summary>
/// Портрет юнита в боковой панели боя (разбор движка, пятый круг; <c>docs/ui-windows.md</c> §9):
/// <list type="bullet">
/// <item>Доля здоровья — на самом портрете (<c>CFaceImgImpl</c>, <c>0x64bca3</c> → <c>0x5b1b8f</c>): верхние
/// <c>высота · (hp·100/hpMax) / 100</c> строк как есть, нижние смешаны с красным наполовину. Подтверждено пользователем 26.09.</item>
/// <item>Весь портрет по состоянию (<c>0x64c269</c>, цвета <c>0x5b191f</c>): павший — чёрным наполовину без красной доли; паралич — серым,
/// яд — оливковым, обморожение — тёмно-синим, ожог — оранжево-коричневым (и красная доля поверх). Окаменение без паралича не красит —
/// так у движка (второй опрос проверяет паралич повторно).</item>
/// <item>Полоса под портретом (<c>IMG_?METER*</c>) — не заливка, а текст <see cref="MeterText"/> «сейчас/макс» чёрным по центру
/// (<c>CImage2Text</c> <c>0x64bdc8</c>, обновление <c>0x64bf2d</c>); у большого — ширина 150 и подложка <c>SPLITLRG_BATTLE</c>
/// по центру (<c>0x64aa9b</c>).</item>
/// <item>Значки состояний (<c>CBatUnitGroup2</c>, заполнение <c>0x64c000</c>, места <c>0x64abb2</c>…<c>0x64ae5d</c>) — <see cref="Icons"/>.</item>
/// </list>
/// </summary>
public static class BattlePanel
{
    public const string MeterText = "X008TA0006", BigMeterUnder = "SPLITLRG_BATTLE";
    public const int BigWidth = 150;
    public static readonly UiColor Wound = new(255, 0, 0);

    /// <summary>Подкраска всего портрета (<c>Whole</c>, null — нет) и красится ли потерянная доля — по базе состояния юнита
    /// (<see cref="BattleFaces.Of"/>, <c>0x64c269</c>) и таблицам рисования (<see cref="BattleFaces.Look"/>).</summary>
    public static (UiColor? Whole, bool Wounds) Tint(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var look = BattleFaces.Look(BattleFaces.Of(unit));
        return (look.Whole, look.Wounds);
    }

    /// <summary>Маска павшего поверх портрета панели — череп из <c>Faces.ff</c> (<c>MASKDEAD{S|L}</c>, 70×85 и 150×85): <c>CFaceImgImpl</c>
    /// (<c>0x5b149d</c>) строит имя <c>"MASK" + "DEAD" + S|L + ".PNG"</c> (<c>0x5b1698</c>) только для состояния 10 — павший; null — маски нет
    /// (живой). Тот же класс рисует и ячейки воинов в окнах (<c>UnitCellsView</c>).</summary>
    public static string? DeadMask(BattleUnit unit, bool big)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.Hp <= 0 ? big ? "MASKDEADL" : "MASKDEADS" : null;
    }

    /// <summary>Подкраска портрета по точкам RGBA (<c>0x5b1af4</c> → <c>0x5b1c12</c>): непрозрачная точка смешивается наполовину с цветом
    /// состояния <paramref name="whole"/>, а при маске — <b>с точкой маски вместо цвета</b> (у павшего — череп поверх, не чёрная заливка;
    /// пункт 3 прогона 2, снимок 34); строки от <paramref name="kept"/> — ещё и с красным (раны; у павшего их нет). Прозрачные точки не
    /// трогаются. Маска меньше портрета — за её краем цвет состояния.</summary>
    /// <param name="alpha">Сила смешения (<see cref="BattleFaceLook.Alpha"/>): 128 — наполовину, 255 — цвет вместо точки (превращение, призыв,
    /// изгнание: <c>0x5b1add</c>; смешение <c>0x5b1c12</c> — <c>c·a/255 + p·(255−a)/255</c>).</param>
    public static void Shade(byte[] rgba, int width, UiColor? whole, int kept, byte[]? mask = null, int maskWidth = 0, int maskHeight = 0,
                             byte alpha = 128)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0) return;
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            int x = i / 4 % width, y = i / 4 / width;
            if (mask is not null && x < maskWidth && y < maskHeight && ((y * maskWidth) + x) * 4 + 2 < mask.Length)
            {
                int m = ((y * maskWidth) + x) * 4;
                Mix(rgba, i, mask[m], mask[m + 1], mask[m + 2], alpha);
            }
            else if (whole is { } c) Mix(rgba, i, c.R, c.G, c.B, alpha);
            if (y >= kept) Mix(rgba, i, Wound.R, Wound.G, Wound.B, alpha);
        }
    }

    private static void Mix(byte[] data, int i, byte r, byte g, byte b, byte alpha)
    {
        if (alpha == 255)
        {
            (data[i], data[i + 1], data[i + 2]) = (r, g, b);
            return;
        }
        data[i] = UiFrameRef.Blend(data[i], r);
        data[i + 1] = UiFrameRef.Blend(data[i + 1], g);
        data[i + 2] = UiFrameRef.Blend(data[i + 2], b);
    }

    /// <summary>Сколько верхних строк портрета высотой <paramref name="height"/> остаются без красного (доля от <see cref="ShownHpMax"/>).</summary>
    public static int KeptRows(BattleUnit unit, int height)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int max = ShownHpMax(unit);
        int percent = max <= 0 ? 0 : Math.Clamp(unit.Hp * 100 / max, 0, 100);
        return height * percent / 100;
    }

    /// <summary>Потолок здоровья, который показывает экран боя (<c>%MAX%</c> панели — <c>0x64bf2d</c> → <c>0x64bf0b</c>, <c>%HPMAX%</c>
    /// большого портрета, карточка): <c>CMidUnit::getHitPoints</c> <c>0x5e987c</c> — у превращённого с сохранением здоровья (<c>+0x28</c> и
    /// <c>+0x30</c>, <c>0x5ea4cb</c>: превращение другого, <c>push 1</c> на <c>0x66093b</c>) это потолок до первого превращения <c>+0x38</c>
    /// (его пишет <c>CMidUnit::transform</c> <c>0x5ea2c5</c>), у прочих — потолок нынешнего типа. Снимки оригинала 19.10 (карта 17, Бес из
    /// гнома 250 и гнома 150): <c>175/250</c>, <c>124/250</c>, <c>150/150</c>; порт показывал потолок Беса — <c>250/50</c>, <c>150/50</c>.
    /// Модель (<see cref="BattleUnit.HpMax"/>) после такого превращения держит потолок нового типа — это расхождение модели, не вида.</summary>
    public static int ShownHpMax(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return unit.OrigType is not null && unit.TransformKeepHp ? unit.OrigHpMax : unit.HpMax;
    }

    /// <summary>Значки юнита по порядку таблицы движка (<c>0x7ae6c0</c>): индекс и имя кадра <c>Battle.ff</c>. Кадров <c>FIFROSTBITE</c>
    /// (в архиве он «FIFROSTBITE COPY») и <c>FIBLISTER</c> в архиве нет — движок их не находит, значков нет; порт так же.
    /// <paramref name="upgrade"/> — хватает опыта на повышение, <paramref name="highLevel"/> — 1…3 или 0.</summary>
    public static IReadOnlyList<(int Index, string Name)> Icons(BattleUnit unit, bool big, BattleMarkSets marks, bool upgrade, int highLevel)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(marks);
        var icons = new List<(int, string)>();
        if (unit.Hp <= 0) return icons;
        if (unit.BoostDamageLevel is >= 1 and <= 4) icons.Add((unit.BoostDamageLevel - 1, $"FIBOOST{unit.BoostDamageLevel}"));
        if (HasDot(unit, AttackClass.Frostbite)) icons.Add((5, "FIFROSTBITE"));
        if (HasDot(unit, AttackClass.Blister)) icons.Add((6, "FIBLISTER"));
        if (unit.LowerDamageLevel is 1 or 2) icons.Add((6 + unit.LowerDamageLevel, $"FILOWER{unit.LowerDamageLevel}"));
        if (unit.LowerInitiativeLevel > 0) icons.Add((9, "FILOWERINIT"));
        if (unit.Paralyzed) icons.Add((10, "FIPARALYZE"));
        if (unit.Petrified) icons.Add((11, "FIPETRIFY"));
        if (HasDot(unit, AttackClass.Poison)) icons.Add((12, "FIPOISON"));
        // Оберег атаки (Лесная Дева и др., класс BestowWards) у движка ложится в цепочку модификаторов юнита (CMidUnit::addModifier 0x65d5d2),
        // а его модификатор — тот же, что у заклинаний-усилений «Защита от …» (g000um0001/0005/0010/0016 — MODIF_ID g000ss0001/0005/0010/0016),
        // и 0x5ea6c1 видит в цепочке модификатор заклинания-усиления — значок 13. В порте обереги лежат в BattleUnit.Wards (партия 16.10:
        // «нет значка баффа — единица в зелёном кружке»).
        var ids = unit.Modifiers.Select(m => m.Id).Concat(unit.Wards.Select(w => w.Id)).ToList();
        if (ids.Any(marks.SpellBoost.Contains)) icons.Add((13, "FIISOSPELLBOOST"));
        if (ids.Any(marks.SpellLower.Contains)) icons.Add((14, "FIISOSPELLLOWER"));
        if (ids.Any(marks.Potion.Contains)) icons.Add((15, "FIPOTIONBOOST"));
        if (unit.Retreating)
            icons.Add((16, big ? unit.Attacker ? "FIRETREATLA" : "FIRETREATLD" : unit.Attacker ? "FIRETREATSA" : "FIRETREATSD"));
        if (upgrade) icons.Add((21, "FIUPGRADE"));
        if (unit.Defending) icons.Add((22, "FIDEFENDING"));
        if (highLevel is >= 1 and <= 3) icons.Add((22 + highLevel, $"FIHIGHLEVEL{highLevel}"));
        return icons;
    }

    /// <summary>Места значков в прямоугольнике портрета <paramref name="width"/>×<paramref name="height"/> (<c>0x64abb2</c>): 0–12 — стопкой у
    /// правого края снизу вверх (не влез — пропуск); 13 — слева вверху, 14 — слева внизу, 15 — слева посередине; отступление — внизу справа
    /// у группы с флагом (<paramref name="rightRetreat"/>, <c>data+0x39</c>), иначе слева; защита — внизу посередине; высокий уровень —
    /// вверху посередине; повышение — в углу (0, 0). <paramref name="size"/> — размер кадра; кадра нет — значок не рисуется.</summary>
    public static IReadOnlyList<BattlePanelIcon> Place(IReadOnlyList<(int Index, string Name)> icons, int width, int height,
                                                       Func<string, (int W, int H)?> size, bool rightRetreat)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(size);
        var placed = new List<BattlePanelIcon>();
        int stack = height;
        foreach (var (index, name) in icons.OrderBy(i => i.Index))
        {
            if (size(name) is not var (w, h)) continue;
            (int X, int Y)? at = index switch
            {
                <= 12 => stack - h >= 0 ? (width - w, stack -= h) : null,
                13 => (0, 0),
                14 => (0, height - h),
                15 => (0, (height - h) / 2),
                >= 16 and <= 19 => (rightRetreat ? width - w : 0, height - h),
                22 => ((width - w) / 2, height - h),
                >= 23 => ((width - w) / 2, 0),
                _ => (0, 0),
            };
            if (at is { } p) placed.Add(new BattlePanelIcon(name, p.X, p.Y));
        }
        return placed;
    }

    private static bool HasDot(BattleUnit unit, AttackClass kind) => unit.Dots.Any(d => d.Class == kind);
}
