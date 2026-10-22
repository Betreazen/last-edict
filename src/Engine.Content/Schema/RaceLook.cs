using System.ComponentModel;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Content.Schema;

/// <summary>
/// Вид расы (Э5): имена графики и роликов, по которым её показывают меню и окна партии. У рас оригинала его записывает импорт таблиц
/// (<see cref="RaceLooks.Legacy"/> — соглашения имён файлов игры, в таблицах их нет), раса мода задаёт свой — так её портреты, боги выбора
/// и значки мини-карты берутся из её файлов, а не по роду (<see cref="RaceDef.Kind"/>). Нет вида — по роду (<see cref="RaceLooks.Of"/>).
/// </summary>
public sealed record RaceLook
{
    [Description("Код расы в именах графики и роликов: LORD{code}L01, FACE_{code}_0, GOD{code}, GOD_{code}2{code}, значок мини-карты.")]
    public string Code { get; init; } = "";

    [Description("Полное имя расы в именах графики: DLG_CHOOSE_LORD_{art}_BG.")]
    public string Art { get; init; } = "";

    [Description("Начало имён круглых портретов лорда (Faces: {portraits}LORD1…).")]
    public string Portraits { get; init; } = "";

    [Description("Сколько лиц у лорда расы (портреты LORD{code}L01…); 1 — одно лицо (нейтралы).")]
    public int Faces { get; init; } = 4;

    [Description("Ролик брифинга сценария за эту расу.")]
    public string BriefingVideo { get; init; } = "";

    [Description("Строка полного имени расы в окне выбора расы.")]
    public string TitleText { get; init; } = "";

    [Description("Место в круге выбора расы, меньше — раньше; нет — расы в круге нет (нейтралы).")]
    public int? ChoiceOrder { get; init; }

    [Description("Анимация воды окна выбора расы; нет — без воды.")]
    public string? ChoiceWater { get; init; }

    [Description("Анимация светлячков окна выбора расы; нет — без светлячков.")]
    public string? ChoiceFireflies { get; init; }

    [Description("Сага расы (кампания «Новой саги»); нет — расы в выборе саги нет.")]
    public string? Saga { get; init; }
}

/// <summary>Вид рас оригинала по роду (<c>Grace.RACE_TYPE</c>) — соглашения имён файлов игры и места в коде движка, которых нет в таблицах.</summary>
public static class RaceLooks
{
    /// <summary>Вид расы оригинала рода <paramref name="kind"/> с id оригинала <paramref name="legacyId"/> (<c>G000RR0000</c>; круглые
    /// портреты <c>Faces</c> названы им, <c>0x5c5424</c>).</summary>
    public static RaceLook Legacy(RaceKind kind, string? legacyId) => (int)kind switch
    {
        // Коды и имена — имена файлов (FACE_HU_0, GOD_HU2UN, DLG_CHOOSE_LORD_UNDEAD_BG); ролики брифинга — буква расы в имени файла
        // (brief2h — Империя, brief2u — Нежить, brief2l — Легионы, brief2d — Кланы, brief3el — эльфы); заголовки окна выбора — 0x4ee08f;
        // круг выбора — «Следующая раса»: Империя → Нежить → Эльфы → Кланы → Легионы (со слов пользователя); вода — 0x4ede4e, светлячки —
        // 0x4edcd3 (только Империя); саги — 0x4ee685.
        LegacyRaces.Human => With("HU", "HUMAN", "BRIEF2H", "X005TA0480", 0, "_WATER_HUMAN", "_FIREFLY_HUMAN", "C000CC0002"),
        LegacyRaces.Undead => With("UN", "UNDEAD", "BRIEF2U", "X005TA0481", 1, "_WATER_UNDEAD", null, "C000CC0005"),
        LegacyRaces.Elf => With("EL", "ELF", "BRIEF3EL", "X160TA0003", 2, null, null, "C000CC0006"),
        LegacyRaces.Dwarf => With("DW", "DWARF", "BRIEF2D", "X005TA0482", 3, null, null, "C000CC0003"),
        LegacyRaces.Heretic => With("HE", "HERETIC", "BRIEF2L", "X005TA0483", 4, "_WATER_HERETIC", null, "C000CC0004"),
        _ => new RaceLook { Code = "NE", Faces = 1 },   // нейтралы: портрет LORDNEL01 — одно лицо (0x5c5615)
    } with { Portraits = (legacyId ?? "").ToUpperInvariant() };

    /// <summary>Вид расы: свой, иначе по роду. Раса мода (без id оригинала) берёт по роду только графику — код, имя-картинку и портреты расы
    /// оригинала этого рода, — но не место в круге выбора и не сагу (иначе заняла бы их у расы оригинала); пустые код и портреты своего
    /// вида дополняются так же.</summary>
    public static RaceLook Of(RaceDef race)
    {
        ArgumentNullException.ThrowIfNull(race);
        var kind = Legacy(race.Kind, race.LegacyId ?? LegacyIdOf(race.Kind));
        if (race.Look is { } own)
            return own with
            {
                Code = own.Code.Length > 0 ? own.Code : kind.Code,
                Portraits = own.Portraits.Length > 0 ? own.Portraits : kind.Portraits,
            };
        return race.LegacyId is not null ? kind : kind with { ChoiceOrder = null, Saga = null };
    }

    /// <summary>Id расы оригинала рода (<c>Grace</c>: G000RR0000 Империя, 0001 Кланы, 0002 Легионы, 0003 Нежить, 0004 нейтралы, 0005 эльфы) —
    /// начало имён её круглых портретов.</summary>
    private static string LegacyIdOf(RaceKind kind) => kind switch
    {
        RaceKind.Human => "G000RR0000", RaceKind.Dwarf => "G000RR0001", RaceKind.Heretic => "G000RR0002", RaceKind.Undead => "G000RR0003",
        RaceKind.Elf => "G000RR0005", _ => "G000RR0004",
    };

    private static RaceLook With(string code, string art, string briefing, string title, int order, string? water, string? fireflies,
                                 string saga) => new()
    {
        Code = code, Art = art, BriefingVideo = briefing, TitleText = title, ChoiceOrder = order, ChoiceWater = water,
        ChoiceFireflies = fireflies, Saga = saga,
    };
}
