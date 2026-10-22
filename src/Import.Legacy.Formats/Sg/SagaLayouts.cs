using LastEdict.Data.Dbf;

namespace LastEdict.Data.Sg;

/// <summary>Шаг раскладки тела объекта потока <c>.sg</c>.</summary>
internal abstract record Op;

/// <summary>Строка: маркер, длина с нулём, байты cp1251, ноль. Значение — <c>Tscen</c> объекта, иначе колонка.</summary>
internal sealed record Str(string Marker, string? Column = null) : Op;

internal sealed record Int(string Marker, string? Column = null) : Op;

/// <summary>Казна строкой (<c>G0000:R0000:Y0000:E0000:W0000:B0000</c>); у глав до «Восстания эльфов» в таблице пять валют — шестая 0.</summary>
internal sealed record Money(string Marker) : Op;

internal sealed record Bool(string Marker) : Op;

/// <summary>Двоичное поле: маркер, длина, байты (из текста колонки, <see cref="SagaSg.Decode"/>).</summary>
internal sealed record Binary(string Marker) : Op;

/// <summary>Поля из строки другой таблицы с ключом = id объекта (группа юнитов города, отряда, руины).</summary>
internal sealed record From(string Table, string Key, Op[] Ops) : Op;

/// <summary>Список строк подтаблицы: ключ = id объекта (null — все строки сценария). Число — id объекта и u32 (<c>f_count</c>),
/// либо поле <paramref name="CountMarker"/>.</summary>
internal sealed record ListOf(string Table, string? Key, Op[] Item, string? CountMarker = null, string? OrderBy = null) : Op;

/// <summary>Голое число с id объекта без элементов (размер карты у карты и плана, пустая сводка хода).</summary>
internal sealed record Count(Func<ScensTables, string, SagaSg.Ctx, int> Value) : Op;

internal sealed record When(Func<SagaSg.Ctx, bool> Condition, Op[] Ops) : Op;

/// <summary>Поле-число, от которого зависит продолжение (категория условия и эффекта события).</summary>
internal sealed record Switch(string Marker, IReadOnlyDictionary<int, Op[]> Cases, Op[]? Then = null) : Op;

/// <summary>Раскладка класса: таблица строки объекта и её ключ (null — у объекта своей строки нет), шаги.</summary>
internal sealed record ClassLayout(string? Table, string? Key, Op[] Ops);

/// <summary>
/// Раскладки тел объектов по <c>sg.ksy</c> (формат <c>D2EESFISIG</c>). Проверены разбором всех объектов 94 файлов нового формата
/// (штатные карты и сохранения корпуса): тело читается до конца, маркеры сходятся.
/// </summary>
internal static class SagaLayouts
{
    private static Op S(string m) => new Str(m);

    private static Op I(string m) => new Int(m);

    private static Op B(string m) => new Bool(m);

    private static Op[] Group =>
    [
        new From("SUGroup", "GROUP_ID",
        [
            S("GROUP_ID"), S("UNIT_0"), S("UNIT_1"), S("UNIT_2"), S("UNIT_3"), S("UNIT_4"), S("UNIT_5"),
            I("POS_0"), I("POS_1"), I("POS_2"), I("POS_3"), I("POS_4"), I("POS_5"),
        ]),
    ];

    private static Op[] City => [S("CITY_ID"), S("NAME_TXT"), S("DESC_TXT"), S("OWNER"), S("SUBRACE"), S("STACK"), I("POS_X"), I("POS_Y"), .. Group];

    private static Op Items => new ListOf("SInventr", "INV_ID", [S("ITEM_ID")]);

    private static Op[] Site => [S("SITE_ID"), I("IMG_ISO"), S("IMG_INTF"), S("TXT_TITLE"), S("TXT_DESC"), I("POS_X"), I("POS_Y"), S("VISITER"), I("AIPRIORITY")];

    private static Op Visitors => new ListOf("SSiteV", "SITE_ID", [S("SITE_ID"), S("VISITER")]);

    private static Op[] Cast => [S("PLAYER_ID"), S("ORIGIN"), I("TURN_STOP")];

    private static int MapSize(ScensTables t, string scen) => t.Rows("Sinfo", scen).Single().GetInt("MAP_SIZE");

    private static readonly Dictionary<int, Op[]> Conditions = new()
    {
        [0] = [I("FREQUENCY")], [2] = [S("ID_LOC")], [3] = [S("ID_CITY")], [4] = [S("ID_CITY")], [5] = [S("ID_STACK")],
        [6] = [S("TYPE_ITEM")], [7] = [S("TYPE_ITEM"), S("ID_STACK")], [8] = [S("ID_PLAYER1"), S("ID_PLAYER2"), I("DIPLOMACY")],
        [9] = [S("ID_PLAYER1"), S("ID_PLAYER2")], [10] = [S("ID_RUIN")], [11] = [I("PCT_LAND")], [12] = [S("ID_SITE")],
        [14] = [S("ID_STACK"), S("ID_LOC")], [15] = [S("ID_STACK"), S("ID_CITY")], [16] = [S("TYPE_ITEM"), S("ID_LOC")],
        [17] = [S("ID_STACK"), I("MISC_INT")],
        [18] = [I("MISC_INT"), I("MISC_INT2"), I("MISC_INT3"), I("MISC_INT4"), I("MISC_INT5"), I("MISC_INT6"), I("MISC_INT7")],
    };

    private static readonly Dictionary<int, Op[]> Effects = new()
    {
        [0] = [B("WIN_SCEN"), S("ID_PLAYER1")], [1] = [S("ID_STKTEMP"), S("ID_LOC")], [2] = [S("TYPE_SPELL"), S("ID_PLAYER1")],
        [3] = [S("TYPE_SPELL"), S("ID_LOC"), S("ID_PLAYER1")], [4] = [S("ID_STACK"), S("ID_PLAYER1"), B("FIRST_ONLY"), B("PLAY_ANIM")],
        [5] = [S("ID_STACK")], [6] = [S("ID_STACK"), B("FIRST_ONLY")], [7] = [S("ID_EVENT"), B("ENABLE")], [8] = [S("TYPE_SPELL")],
        [9] = [I("GIVETO"), S("TYPE_ITEM")], [10] = [S("ID_STKTEMP"), S("ID_LOC"), B("BOOLVALUE")],
        [11] = [S("ID_PLAYER1"), S("ID_PLAYER2"), B("PERMALLI")],   // маркер движка с опечаткой: 0x616384 (поток L_ALLY)
        [12] = [S("ID_PLAYER1"), S("ID_PLAYER2"), I("DIPLOMACY"), B("ENABLE")],
        [13] = [S("ID_LOC"), new ListOf("SEvUnfog", "EVENT_ID", [S("EVENT_ID"), S("PLAYER")]), B("ENABLE"), I("NUMVALUE")],
        [14] = [S("ID_LOC")], [15] = [S("ID_LMARK"), B("BOOLVALUE")], [16] = [S("OBJECT_TXT")],
        [17] = [S("POPUP_TXT"), S("MUSIC"), S("SOUND"), S("IMAGE"), S("IMAGE2"), B("LEFT_SIDE"), S("POPUP_SHOW"), B("BOOLVALUE")],
        [18] = [S("ID_STACK"), S("ORDER_TARG"), B("FIRST_ONLY"), I("ORDER")], [19] = [S("TYPE_ITEM"), B("TRIG_ONLY")],
        [20] = [S("ID_STACK"), B("FIRST_ONLY")], [21] = [S("ID_LMARK"), S("TYPE_LMARK")],
        [22] = [S("ID_LOC"), I("LOOKUP"), I("NUMVALUE")], [23] = [I("LOOKUP"), I("NUMVALUE"), I("NUMVALUE2")],
    };

    public static readonly IReadOnlyDictionary<string, ClassLayout> Classes = new Dictionary<string, ClassLayout>
    {
        ["ScenarioInfo"] = new("Sinfo", "INFO_ID",
        [
            S("INFO_ID"), S("CAMPAIGN"), B("SOURCE_M"), I("QTY_CITIES"), S("NAME"), S("DESC"), S("BRIEFING"), S("DEBUNKW"), S("DEBUNKW2"),
            S("DEBUNKW3"), S("DEBUNKW4"), S("DEBUNKW5"), S("DEBUNKL"), S("BRIEFLONG1"), S("BRIEFLONG2"), S("BRIEFLONG3"), S("BRIEFLONG4"),
            S("BRIEFLONG5"), B("O"), I("CUR_TURN"), I("MAX_UNIT"), I("MAX_SPELL"), I("MAX_LEADER"), I("MAX_CITY"), I("MAP_SIZE"),
            I("DIFFSCEN"), I("DIFFGAME"), S("CREATOR"),
            .. Enumerable.Range(1, 13).Select(k => I($"PLAYER_{k}")), I("SUGG_LVL"), I("MAP_SEED"),
        ]),
        ["MidMountains"] = new(null, null, [new ListOf("SMounts", null, [I("ID_MOUNT"), I("SIZE_X"), I("SIZE_Y"), I("POS_X"), I("POS_Y"), I("IMAGE"), I("RACE")])]),
        ["MidPlayer"] = new("Splayer", "PLAYER_ID",
        [
            S("PLAYER_ID"), S("NAME_TXT"), S("DESC_TXT"), S("LORD_ID"), S("RACE_ID"), S("FOG_ID"), S("KNOWN_ID"), S("BUILDS_ID"), I("FACE"),
            I("QTY_BREAKS"), new Money("BANK"), B("IS_HUMAN"), new Money("SPELL_BANK"), I("ATTITUDE"), I("RESEAR_T"), I("CONSTR_T"), S("SPY_1"), S("SPY_2"),
            S("SPY_3"), S("CAPT_BY"), B("ALWAYSAI"), S("EXMAPID1"), I("EXMAPTURN1"), S("EXMAPID2"), I("EXMAPTURN2"), S("EXMAPID3"), I("EXMAPTURN3"),
        ]),
        ["MidUnit"] = new("SUnit", "UNIT_ID",
        [
            S("UNIT_ID"), S("TYPE"), I("LEVEL"), new ListOf("SUnitM", "UNIT_ID", [S("MODIF_ID")]), I("CREATION"), S("NAME_TXT"), B("TRANSF"),
            new When(c => c.Row?.GetBool("TRANSF") == true, [S("ORIGTYPEID"), B("KEEP_HP"), I("ORIG_XP"), I("HP_BEFORE"), I("HP_BEF_MAX")]),
            B("DYNLEVEL"), I("HP"), I("XP"),
        ]),
        ["MidgardMapBlock"] = new("SMapBlck", "BLOCKID", [S("BLOCKID"), new Binary("BLOCKDATA")]),
        ["Capital"] = new("SFort", "CITY_ID", [.. City, Items, I("AIPRIORITY")]),
        ["MidVillage"] = new("SFort", "CITY_ID",
        [
            .. City, Items, I("AIPRIORITY"), S("PROTECT_B"), I("REGEN_B"), I("MORALE"), I("GROWTH_T"), I("SIZE"),
            B("P_O_UN"), B("P_O_HE"), B("P_O_HU"), B("P_O_DW"), B("P_O_EL"), I("RIOT_T"),
        ]),
        ["MidgardMapFog"] = new(null, null, [new ListOf("Sfog", "FOG_ID", [I("POS_Y"), new Binary("FOG")])]),
        ["MidDiplomacy"] = new(null, null, [new ListOf("SRel", null, [I("RACE_1"), I("RACE_2"), I("RELATION")])]),
        ["MidgardPlan"] = new(null, null,
        [
            new Count((t, scen, _) => MapSize(t, scen)),
            new ListOf("SMapEl", null, [I("POS_X"), I("POS_Y"), S("ELEMENT")]),
        ]),
        ["MidScenVariables"] = new(null, null, [new ListOf("SVars", null, [I("ID"), S("NAME"), I("VALUE")])]),
        ["MidStack"] = new("Sstack", "STACK_ID",
        [
            .. Group, Items, S("STACK_ID"), S("SRCTMPL_ID"), S("LEADER_ID"), B("LEADR_ALIV"), I("POS_X"), I("POS_Y"), I("MORALE"), I("MOVE"),
            I("FACING"), S("BANNER"), S("TOME"), S("BATTLE1"), S("BATTLE2"), S("ARTIFACT1"), S("ARTIFACT2"), S("BOOTS"), S("OWNER"), S("INSIDE"),
            S("SUBRACE"), B("INVISIBLE"), B("AI_IGNORE"), I("UPGCOUNT"), I("ORDER"), S("ORDER_TARG"), I("AIORDER"), S("AIORDERTAR"),
            I("AIPRIORITY"), I("CREAT_LVL"), I("NBBATTLE"),
        ]),
        ["MidStackTemplate"] = new("SStkTemp", "ID",
        [
            S("ID"), S("OWNER"), S("LEADER"), I("LEADER_LVL"), S("NAME_TXT"), S("ORDER_TARG"), S("SUBRACE"), I("ORDER"),
            .. Enumerable.Range(0, 6).SelectMany(k => new[] { S($"UNIT_{k}"), I($"UNIT_{k}_LVL") }),
            .. Enumerable.Range(0, 6).Select(k => I($"POS_{k}")), B("USE_FACING"), I("FACING"),
            new ListOf("SStkTemM", "TEMPLAT_ID", [I("UNIT_POS"), S("MODIF_ID")]), I("AIPRIORITY"),
        ]),
        ["MidgardMap"] = new(null, null, [new Count((t, scen, _) => MapSize(t, scen))]),
        ["MidItem"] = new("SItem", "ITEM_ID", [S("ITEM_ID"), S("ITEM_TYPE")]),
        ["MidTalismanCharges"] = new(null, null, [new ListOf("STalisCh", null, [S("ID_TALIS"), I("CHARGES")])]),
        ["MidSpellEffects"] = new(null, null, [new ListOf("SSEffec", null, [S("UNIT_ID"), S("ORIGIN_ID"), S("MODIF_ID"), S("CASTER_ID"), I("TURN_STOP")])]),
        ["MidSubRace"] = new("SSubRace", "SUBRACE_ID", [S("SUBRACE_ID"), I("SUBRACE"), S("PLAYER_ID"), I("NUMBER"), S("NAME_TXT"), I("BANNER")]),
        ["PlayerKnownSpells"] = new(null, null, [new ListOf("Sspell", "KSPELLS_ID", [S("SPELL_ID")])]),
        ["PlayerBuildings"] = new(null, null, [new ListOf("SBuild", "BLIST_ID", [S("BUILD_ID")])]),
        ["TurnSummary"] = new(null, null, [new Count((_, _, _) => 0)]),
        ["MidRoad"] = new("SRoad", "ROAD_ID", [S("ROAD_ID"), I("INDEX"), I("VAR"), I("POS_X"), I("POS_Y")]),
        ["MidSiteMage"] = new("SSite", "SITE_ID", [.. Site, new ListOf("SMage", "SITE_ID", [S("SPELL_ID")], "QTY_SPELL"), Visitors]),
        ["MidSiteMercs"] = new("SSite", "SITE_ID",
            [.. Site, new ListOf("SMercs", "SITE_ID", [S("UNIT_ID"), I("UNIT_LEVEL"), B("UNIT_UNIQ")], "QTY_UNIT"), Visitors]),
        ["MidSiteMerchant"] = new("SSite", "SITE_ID",
        [
            .. Site, B("BUY_ARMOR"), B("BUY_JEWEL"), B("BUY_WEAPON"), B("BUY_BANNER"), B("BUY_POTION"), B("BUY_SCROLL"), B("BUY_WAND"),
            B("BUY_VALUE"), new ListOf("SMerch", "SITE_ID", [S("ITEM_ID"), I("ITEM_COUNT")], "QTY_ITEM"), B("MISSION"), Visitors,
        ]),
        ["MidSiteTrainer"] = new("SSite", "SITE_ID", [.. Site, Visitors]),
        ["MidRuin"] = new("SRuin", "RUIN_ID",
        [
            S("RUIN_ID"), S("TITLE"), S("DESC"), I("IMAGE"), I("POS_X"), I("POS_Y"), new Money("CASH"), S("ITEM"), S("LOOTER"), I("AIPRIORITY"),
            new ListOf("SRuinV", "RUIN_ID", [S("RUIN_ID"), S("VISITER")]), .. Group,
        ]),
        ["MidCrystal"] = new("SCrys", "CRYSTAL_ID", [S("CRYSTAL_ID"), I("RESOURCE"), I("POS_X"), I("POS_Y"), I("AIPRIORITY")]),
        ["MidLandmark"] = new("SLmark", "LMARK_ID", [S("LMARK_ID"), S("TYPE"), I("POS_X"), I("POS_Y"), S("DESC_TXT")]),
        ["MidBag"] = new("SBag", "BAG_ID", [S("BAG_ID"), I("POS_X"), I("POS_Y"), I("IMAGE"), I("AIPRIORITY"), Items]),
        ["MidLocation"] = new("SLoc", "LOC_ID", [S("LOC_ID"), I("POS_X"), I("POS_Y"), S("NAME_TXT"), I("RADIUS")]),
        ["MidEvent"] = new("SEvent", "ID",
        [
            S("ID"), S("NAME_TXT"),
            .. new[] { "HUMAN", "DWARF", "UNDEAD", "HERETIC", "NEUTRAL", "ELF", "VERHUMAN", "VERDWARF", "VERUNDEAD", "VERHERETIC", "VERNEUTRAL",
                       "VERELF", "ENABLED", "OCCUR_ONCE" }.Select(B),
            I("CHANCE"), I("ORDER"),
            new ListOf("SEvCond", "ID", [new Switch("CATEGORY", Conditions)], "COND_QTY"),
            new ListOf("SEvEffec", "ID", [new Switch("CATEGORY", Effects, [I("NUM")])], "EFFECT_QTY", "NUM"),
        ]),
        ["MidTomb"] = new("STomb", "TOMB_ID",
            [S("TOMB_ID"), I("POS_X"), I("POS_Y"), new ListOf("STombE", "TOMB_ID", [S("STACK_OWNR"), S("KILLER"), I("TURN"), S("STACK_NAME")], "QTY_EP")]),
        ["MidStackDestroyed"] = new(null, null, [new ListOf("SStkDstr", null, [S("ID_STACK"), S("ID_KILLER"), S("SRCTMPL_ID")])]),
        ["MidRod"] = new("SRod", "ROD_ID", [S("ROD_ID"), S("OWNER"), I("POS_X"), I("POS_Y")]),
        ["MidQuestLog"] = new(null, null,
            [new ListOf("SQLog", null, [S("ID_PLAYER"), I("SEQ_NUM"), I("CUR_TURN"), I("TYPE"), S("ID_EVENT"), I("EFF_NUM")])]),
        ["MidSpellCast"] = new(null, null, [new ListOf("SCSpell", null, Cast), new ListOf("SCWand", null, Cast)]),
    };
}
