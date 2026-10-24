using System.ComponentModel;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Content.Schema;

/// <summary>Постройка столицы (<c>Gbuild.dbf</c>).</summary>
[LegacyTable("Gbuild", "BUILD_ID")]
public sealed record BuildingDef : ContentDef
{
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("REQUIRED"), Ref("building"), Description("Постройка, которая должна стоять раньше.")] public string? Requires { get; init; }
    [Legacy("COST")] public IReadOnlyDictionary<string, long>? Cost { get; init; }
    [Legacy("LEVEL")] public int Level { get; init; }
    [Legacy("CATEGORY")] public BuildingCategory Category { get; init; }
    [Legacy("BRANCH"), Description("Ветка юнитов, которую открывает постройка найма.")] public BranchKind? Branch { get; init; }
    [LegacyGroup, Description("Картинка постройки на экране столицы (у двух построек оригинала).")]
    public BuildingPicture Picture { get; init; } = new();
    [Legacy("COMMENTS")] public string? Comment { get; init; }
    [Description("Место в дереве окна построек (Last Edict). Нет — ветка по категории и ветке юнитов, место — автораскладкой.")]
    public BuildingTreePlace? Tree { get; init; }
}

/// <summary>Место постройки в дереве окна построек (Э6): ветка (вкладка) и клетка сетки дерева. Заданное поле главнее автораскладки.</summary>
public sealed record BuildingTreePlace
{
    [Ref("building-branch"), Description("Ветка окна построек; нет — по категории и ветке юнитов, иначе — ветка требуемой постройки.")]
    public string? Branch { get; init; }
    [Description("Столбец сетки дерева (можно дробный: середина между столбцами); нет — автораскладка.")]
    public double? Column { get; init; }
    [Description("Строка сетки дерева сверху (0 — первая); нет — по уровню и требованию.")]
    public int? Row { get; init; }
}

/// <summary>Ветка окна построек (Э6): вкладка дерева построек столицы. Постройки попадают в ветку по <see cref="BuildingDef.Tree"/>, иначе
/// по категории (<see cref="Categories"/>) или ветке юнитов постройки найма (<see cref="Units"/>), иначе — в ветку требуемой постройки.
/// Порядок вкладок — порядок содержимого. База — пять веток окна оригинала (импорт); моды добавляют свои.</summary>
public sealed record BuildingBranchDef : ContentDef
{
    [Description("Строка имени ветки (подсказка вкладки).")] public string? NameText { get; init; }
    [Description("Переключатель окна построек, который открывает ветку (у веток оригинала — кнопки круга TOG_*); нет — только вкладкой.")]
    public string? Toggle { get; init; }
    [Description("Постройки найма каких веток юнитов идут в ветку.")] public IReadOnlyList<BranchKind> Units { get; init; } = [];
    [Description("Постройки каких категорий идут в ветку (кроме найма — у него ветка юнитов).")]
    public IReadOnlyList<BuildingCategory> Categories { get; init; } = [];
}

/// <summary>Картинка постройки на экране столицы (<c>PIC_1</c>, кадры и место).</summary>
public sealed record BuildingPicture
{
    [Legacy("PIC_1")] public string? Image { get; init; }
    [Legacy("FRAME_1")] public int? Frame1 { get; init; }
    [Legacy("FRAME_2")] public int? Frame2 { get; init; }
    [Legacy("POS_X")] public int? X { get; init; }
    [Legacy("POS_Y")] public int? Y { get; init; }
    [Legacy("POS_CX")] public int? Width { get; init; }
    [Legacy("POS_CY")] public int? Height { get; init; }
    [Legacy("POS_Z")] public int? Z { get; init; }
}

/// <summary>Раса (<c>Grace.dbf</c>). Имена героев (<c>Tleader</c>) и сообщения ИИ (<c>TAiMsg</c>) — в ней же, по порядку таблиц.</summary>
[LegacyTable("Grace", "RACE_ID")]
public sealed record RaceDef : ContentDef
{
    [Legacy("GUARDIAN"), Ref("unit"), Description("Страж столицы.")] public string? Guardian { get; init; }
    [Legacy("NOBLE"), Ref("unit"), Description("Вор расы.")] public string? Noble { get; init; }
    [Legacy("LEADER_1", "LEADER_2", "LEADER_3", "LEADER_4"), Ref("unit"), Description("Лидеры для найма по порядку.")]
    public IReadOnlyList<string> Leaders { get; init; } = [];
    [Legacy("SOLDIER_1", "SOLDIER_2", "SOLDIER_3", "SOLDIER_4", "SOLDIER_5"), Ref("unit"), Description("Солдаты первого уровня веток найма.")]
    public IReadOnlyList<string> Soldiers { get; init; } = [];
    [Legacy("SCOUT"), Description("Обзор столицы.")] public int? Scout { get; init; }
    [Legacy("REGEN_H"), Description("Лечение в столице, процентов в день.")] public int? CapitalRegeneration { get; init; }
    [Legacy("INCOME")] public IReadOnlyDictionary<string, long>? Income { get; init; }
    [Legacy("PROTECTION"), Ref("modifier"), Description("Защита столицы.")] public string? Protection { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("RACE_TYPE")] public RaceKind Kind { get; init; }
    [Legacy("PLAYABLE")] public bool Playable { get; init; }
    [Description("Своя земля: её красят захваты и жезлы расы, на ней лечатся её отряды. Нет — по роду (kind).")]
    public TerrainKind? Land { get; init; }
    [Description("Место в очереди хода, меньше — раньше. Нет — по роду (kind).")] public int? TurnOrder { get; init; }
    [Description("Номер флага расы в событиях сценария (HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF — 0…5); −1 — флага нет. Нет — по роду (kind).")]
    public int? EventFlag { get; init; }
    [Description("Нейтралы: хозяин ничьих отрядов и объектов карты. Нет — по роду (kind).")] public bool? Neutral { get; init; }
    [Description("Имена героев (Tleader) по порядку таблицы.")] public IReadOnlyList<HeroName> HeroNames { get; init; } = [];
    [Description("Сообщения ИИ (TAiMsg) по порядку таблицы.")] public IReadOnlyList<AiMessageLine> AiMessages { get; init; } = [];
    [Description("Вид расы: имена графики и роликов для меню и окон (RaceLook). Нет — по роду (kind).")] public RaceLook? Look { get; init; }
}

/// <summary>Имя героя расы (строка <c>Tleader</c>).</summary>
public sealed record HeroName
{
    public required string Text { get; init; }
    public bool Male { get; init; }
    public bool? Verified { get; init; }
    public string? Context { get; init; }
}

/// <summary>Сообщение ИИ (строка <c>TAiMsg</c>).</summary>
public sealed record AiMessageLine(AiMessageKind Kind, string Text);

/// <summary>Уровень города расы (<c>GcityInf.dbf</c>): обзор, лечение, защита, прирост. Раса — и подрасы нейтралов, которых нет в
/// <c>Grace</c> (<c>g000rr0006…0012</c>): ссылка на них остаётся id оригинала (<c>legacy:…</c>).</summary>
[LegacyTable("GcityInf", "")]
public sealed record CityLevelDef : ContentDef
{
    [Legacy("RACE_ID"), Ref("race")] public string? Race { get; init; }
    [Legacy("SIZE")] public int Size { get; init; }
    [Legacy("SCOUT")] public int Scout { get; init; }
    [Legacy("REGEN_F"), Description("Лечение в городе, процентов в день.")] public int Regeneration { get; init; }
    [Legacy("PROTECTION"), Ref("modifier")] public string? Protection { get; init; }
    [Legacy("GROWTH")] public IReadOnlyDictionary<string, long>? Growth { get; init; }
    [Legacy("RES_M")] public int? ResourceMoney { get; init; }
}

/// <summary>Лорд — класс игрока (<c>Glord.dbf</c>). Постройки (<c>GbuiList</c>) и исследования заклинаний (<c>GspellR</c>) — в нём же.</summary>
[LegacyTable("Glord", "LORD_ID")]
public sealed record LordDef : ContentDef
{
    [Legacy("RACE_ID"), Ref("race")] public string? Race { get; init; }
    [Legacy("CATEGORY")] public LordCategory Category { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("PIC")] public string? Picture { get; init; }
    [Ref("building"), Description("Постройки столицы, доступные лорду (GbuiList), по порядку таблицы.")]
    public IReadOnlyList<string> Buildings { get; init; } = [];
    [Description("Заклинания, которые лорд может исследовать (GspellR), по порядку таблицы.")]
    public IReadOnlyList<SpellResearch> Research { get; init; } = [];
}

/// <summary>Исследование заклинания лордом (строка <c>GspellR</c>).</summary>
public sealed record SpellResearch
{
    [Ref("spell")] public required string Spell { get; init; }
    public IReadOnlyDictionary<string, long>? Cost { get; init; }
}

/// <summary>Навык лидера на уровень (<c>GleaUpg.dbf</c>). Отдельный список, а не часть лорда: порядок таблицы — порядок равенства
/// при выборе навыка ИИ, а строки лордов в ней перемешаны.</summary>
[LegacyTable("GleaUpg", "")]
public sealed record LeaderSkillDef : ContentDef
{
    [Legacy("BELONGS_TO"), Ref("lord")] public string? Lord { get; init; }
    [Legacy("MODIF_ID"), Ref("modifier")] public string? Modifier { get; init; }
    [Legacy("MIN_LEVEL")] public int MinLevel { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("PRIORITY"), Description("Вес выбора навыка ИИ.")] public int Priority { get; init; }
}

/// <summary>Ориентир карты (<c>GLmark.dbf</c>).</summary>
[LegacyTable("GLmark", "LMARK_ID")]
public sealed record LandmarkDef : ContentDef
{
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [Legacy("CX")] public int Width { get; init; }
    [Legacy("CY")] public int Height { get; init; }
    [Legacy("MOUNTAIN"), Description("Клетки ориентира не растит земля.")] public bool? Mountain { get; init; }
    [Legacy("CATEGORY")] public LandmarkCategory Category { get; init; }
}

/// <summary>Действие вора (<c>Gaction.dbf</c>).</summary>
[LegacyTable("Gaction", "ACTION_ID")]
public sealed record ThiefActionDef : ContentDef
{
    [Legacy("CATEGORY")] public ThiefActionKind Kind { get; init; }
    [Legacy("CHANCE"), Description("Шанс успеха в процентах.")] public int Chance { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
}
