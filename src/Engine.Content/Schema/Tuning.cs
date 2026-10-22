using System.ComponentModel;

namespace D2.Data.Content.Schema;

/// <summary>Настройка ИИ для одного отношения (<c>GAI.dbf</c>, строка на <see cref="AiAttitude"/>): бюджет дня, доли лидеров, веса
/// тактики, читы сложности, пороги лечения, заклинания.</summary>
[LegacyTable("GAI", "")]
public sealed record AiAttitudeDef : ContentDef
{
    [Legacy("CATEGORY")] public AiAttitude Attitude { get; init; }
    [Legacy("NAME_TXT")] public string? NameText { get; init; }
    [Legacy("DESC_TXT")] public string? DescriptionText { get; init; }
    [LegacyGroup] public AiBudgetTuning Budget { get; init; } = new();
    [LegacyGroup] public AiLeaderMix Leaders { get; init; } = new();
    [LegacyGroup] public AiTacticWeights Weights { get; init; } = new();
    [LegacyGroup] public AiCheats Cheats { get; init; } = new();
    [Legacy("HP_STAYMIN"), Description("Юнит в крепости ниже этого процента здоровья держит отряд на лечении.")] public int StayMinHp { get; init; }
    [Legacy("HP_HEALMIN"), Description("Ниже этого процента здоровья юнит считается раненым.")] public int HealMinHp { get; init; }
    [LegacyGroup] public AiSpellTuning Spells { get; init; } = new();
}

/// <summary>Базы бюджета дня ИИ (<c>GAI.B_*</c>).</summary>
public sealed record AiBudgetTuning
{
    [Legacy("B_UNITS")] public int Units { get; init; }
    [Legacy("B_LEADERS")] public int Leaders { get; init; }
    [Legacy("B_CAP")] public int Capital { get; init; }
    [Legacy("B_CITIES")] public int Cities { get; init; }
    [Legacy("B_SPELL_C")] public int SpellCast { get; init; }
    [Legacy("B_SPELL_R")] public int SpellResearch { get; init; }
    [Legacy("B_ISO"), Description("Доля казны в кошелёк тактики.")] public int TacticPurse { get; init; }
}

/// <summary>Доли и пределы лидеров ИИ: воины, воры, жезлоносцы (<c>GAI.L_*, T_*, R_*, PCT_*</c>).</summary>
public sealed record AiLeaderMix
{
    [Legacy("L_MIN")] public int FighterMin { get; init; }
    [Legacy("L_MAX")] public int FighterMax { get; init; }
    [Legacy("PCT_FIGHTR")] public int FighterPercent { get; init; }
    [Legacy("T_MIN")] public int ThiefMin { get; init; }
    [Legacy("T_MAX")] public int ThiefMax { get; init; }
    [Legacy("PCT_THIEF")] public int ThiefPercent { get; init; }
    [Legacy("R_MIN")] public int RodMin { get; init; }
    [Legacy("R_MAX")] public int RodMax { get; init; }
    [Legacy("PCT_ROD")] public int RodPercent { get; init; }
}

/// <summary>Веса целей тактики ИИ (<c>GAI.I_*</c>): N — нейтральные, R — соперники, P — игрок.</summary>
public sealed record AiTacticWeights
{
    [Legacy("I_CITIES_N")] public int CitiesNeutral { get; init; }
    [Legacy("I_CITIES_R")] public int CitiesRival { get; init; }
    [Legacy("I_CITIES_P")] public int CitiesPlayer { get; init; }
    [Legacy("I_STACKS_N")] public int StacksNeutral { get; init; }
    [Legacy("I_STACKS_R")] public int StacksRival { get; init; }
    [Legacy("I_STACKS_P")] public int StacksPlayer { get; init; }
    [Legacy("I_BAGS")] public int Bags { get; init; }
    [Legacy("I_RUINS")] public int Ruins { get; init; }
    [Legacy("I_TOWERS")] public int Towers { get; init; }
    [Legacy("I_MERCHANT")] public int Merchants { get; init; }
    [Legacy("I_H_UNITS")] public int HireUnits { get; init; }
    [Legacy("I_H_MERCS")] public int HireMercenaries { get; init; }
    [Legacy("I_HEAL")] public int Heal { get; init; }
    [Legacy("I_P_ROD")] public int PlantRod { get; init; }
    [Legacy("I_A_ROD")] public int AttackRod { get; init; }
    [Legacy("I_N_CITIES")] public int NeutralCities { get; init; }
    [Legacy("I_N_STACK")] public int NeutralStacks { get; init; }
    [Legacy("I_TRAINER")] public int Trainers { get; init; }
    [Legacy("I_OBJ_M")] public int ObjectMove { get; init; }
}

/// <summary>Читы ИИ по сложности (<c>GAI.D_*_M</c>, <c>D_*_XP</c>), по порядку: лёгкая, средняя, трудная, очень трудная.</summary>
public sealed record AiCheats
{
    [Legacy("D_EASY_M", "D_AVG_M", "D_HARD_M", "D_VHARD_M"), Description("Доход ИИ в процентах.")]
    public IReadOnlyList<int> ResourcePercent { get; init; } = [];
    [Legacy("D_EASY_XP", "D_AVG_XP", "D_HARD_XP", "D_VHARD_XP"), Description("Прибавка опыта юнитам ИИ в процентах.")]
    public IReadOnlyList<int> XpPercent { get; init; } = [];
}

/// <summary>Заклинания ИИ (<c>GAI.CST*, SPL*, CRITSPL*</c>).</summary>
public sealed record AiSpellTuning
{
    [Legacy("CSTCHNCE")] public int CastChance { get; init; }
    [Legacy("CSTCHNCE_2")] public int CastChance2 { get; init; }
    [Legacy("CSTDBL_MIN")] public int CastDoubleMin { get; init; }
    [Legacy("SPL1_CHNCE", "SPL2_CHNCE", "SPL3_CHNCE")] public IReadOnlyList<int> LevelChance { get; init; } = [];
    [Legacy("SPL_H_W")] public int HighWeight { get; init; }
    [Legacy("SPL_H_C")] public int HighChance { get; init; }
    [Legacy("SPL_M_W")] public int MediumWeight { get; init; }
    [Legacy("SPL_M_C")] public int MediumChance { get; init; }
    [Legacy("SPL_L_W")] public int LowWeight { get; init; }
    [Legacy("SPL_L_C")] public int LowChance { get; init; }
    [Legacy("SPL_L5_POW", "SPL_L4_POW", "SPL_L3_POW", "SPL_L2_POW"), Description("Сила по уровням заклинания, от пятого ко второму.")]
    public IReadOnlyList<int> LevelPower { get; init; } = [];
    [Legacy("CRITSPL_W1", "CRITSPL_W2", "CRITSPL_W3", "CRITSPL_W4")] public IReadOnlyList<int> CriticalWeight { get; init; } = [];
    [Legacy("CRITSPL_C1", "CRITSPL_C2", "CRITSPL_C3", "CRITSPL_C4")] public IReadOnlyList<int> CriticalChance { get; init; } = [];
}

/// <summary>Глобальные величины правил (<c>GVars.dbf</c>, одна строка): бой, вор, земля, дипломатия, сложность, ИИ. Одна сущность на
/// набор правил (id <c>base:globals</c>); мод меняет отдельные поля.</summary>
[LegacyTable("GVars", "")]
public sealed record GlobalsDef : ContentDef
{
    [LegacyGroup] public BattleGlobals Battle { get; init; } = new();
    [LegacyGroup] public ThiefGlobals Thieves { get; init; } = new();
    [LegacyGroup] public LandGlobals Land { get; init; } = new();
    [LegacyGroup] public DiplomacyGlobals Diplomacy { get; init; } = new();
    [LegacyGroup] public DifficultyGlobals Difficulty { get; init; } = new();
    [LegacyGroup] public AiGlobals Ai { get; init; } = new();
    [LegacyGroup] public EconomyGlobals Economy { get; init; } = new();
    [LegacyGroup] public HeroGlobals Heroes { get; init; } = new();
    [LegacyGroup] public SpellGlobals Spells { get; init; } = new();
    [Legacy("TUTORIAL")] public string? Tutorial { get; init; }
}

public sealed record BattleGlobals
{
    [Legacy("BAT_INIT"), Description("Случайная прибавка к инициативе (0…).")] public int InitiativeRandom { get; init; }
    [Legacy("BAT_DAMAGE"), Description("Случайная прибавка к урону.")] public int DamageRandom { get; init; }
    [Legacy("BAT_ROUND")] public int Rounds { get; init; }
    [Legacy("BAT_BREAK")] public int Break { get; init; }
    [Legacy("BAT_BMODIF")] public int BreakModifier { get; init; }
    [Legacy("BATBOOSTD1", "BATBOOSTD2", "BATBOOSTD3", "BATBOOSTD4"), Description("Усиление урона по уровням, %.")]
    public IReadOnlyList<int> BoostDamagePercent { get; init; } = [];
    [Legacy("BATLOWERD1", "BATLOWERD2"), Description("Ослабление урона по уровням, %.")] public IReadOnlyList<int> LowerDamagePercent { get; init; } = [];
    [Legacy("BATLOWERI1"), Description("Замедление инициативы, %.")] public int LowerInitiativePercent { get; init; }
    [Legacy("DFENDBONUS"), Description("Прибавка брони при защите, %.")] public int DefendBonus { get; init; }
}

public sealed record ThiefGlobals
{
    [Legacy("MORALE_1", "MORALE_2", "MORALE_3", "MORALE_4", "MORALE_5", "MORALE_6")] public IReadOnlyList<int> Morale { get; init; } = [];
    [Legacy("SPY_DISCOV"), Description("Шанс раскрыть шпиона в начале хода, %.")] public int SpyDiscovery { get; init; }
    [Legacy("POISON_S")] public int PoisonStack { get; init; }
    [Legacy("POISON_C")] public int PoisonCity { get; init; }
    [Legacy("BRIBE")] public int Bribe { get; init; }
    [Legacy("STEAL_RACE")] public int StealRace { get; init; }
    [Legacy("STEAL_NEUT")] public int StealNeutral { get; init; }
    [Legacy("RIOT_MIN")] public int RiotMin { get; init; }
    [Legacy("RIOT_MAX")] public int RiotMax { get; init; }
    [Legacy("RIOT_DMG")] public int RiotDamage { get; init; }
}

public sealed record LandGlobals
{
    [Legacy("T_CAPTURE")] public int CapturePercent { get; init; }
    [Legacy("T_CAPITAL")] public int Capital { get; init; }
    [Legacy("T_CITY1", "T_CITY2", "T_CITY3", "T_CITY4", "T_CITY5")] public IReadOnlyList<int> Cities { get; init; } = [];
    [Legacy("ROD_COST")] public IReadOnlyDictionary<string, long>? RodCost { get; init; }
    [Legacy("ROD_RANGE")] public int RodRange { get; init; }
}

public sealed record DiplomacyGlobals
{
    [Legacy("D_PEACE")] public int Peace { get; init; }
    [Legacy("D_WAR")] public int War { get; init; }
    [Legacy("D_NEUTRAL")] public int Neutral { get; init; }
    [Legacy("D_GOLD")] public int GoldPerPoint { get; init; }
    [Legacy("D_MK_ALLY")] public int MakeAlly { get; init; }
    [Legacy("D_ATTAK_SC")] public int AttackStack { get; init; }
    [Legacy("D_ATTAK_FO")] public int AttackFort { get; init; }
    [Legacy("D_ATTAK_PC")] public int AttackAtPeace { get; init; }
    [Legacy("D_ROD")] public int RodDestroyed { get; init; }
    [Legacy("D_REF_ALLY")] public int RefuseAlly { get; init; }
    [Legacy("D_BK_ALLY")] public int BreakAlly { get; init; }
    [Legacy("D_NOBLE")] public int NobleAction { get; init; }
    [Legacy("D_BKA_CHNC")] public int AllianceBreakChance { get; init; }
    [Legacy("D_BKA_TURN")] public int AllianceBreakTurn { get; init; }
}

/// <summary>По сложности, по порядку: лёгкая, средняя, трудная, очень трудная.</summary>
public sealed record DifficultyGlobals
{
    [Legacy("BONUS_E", "BONUS_A", "BONUS_H", "BONUS_V"), Description("Стартовая казна человека, %.")]
    public IReadOnlyList<int> StartBankPercent { get; init; } = [];
    [Legacy("INCOME_E", "INCOME_A", "INCOME_H", "INCOME_V"), Description("Надбавка дохода, %.")]
    public IReadOnlyList<int> IncomePercent { get; init; } = [];
}

public sealed record AiGlobals
{
    [Legacy("PROT_1", "PROT_2", "PROT_3", "PROT_4", "PROT_5"), Description("Броня деревень по уровням для оценки силы.")]
    public IReadOnlyList<int> CityArmor { get; init; } = [];
    [Legacy("PROT_CAP")] public int CapitalArmor { get; init; }
    [Legacy("GU_RANGE")] public int GuardRange { get; init; }
    [Legacy("PA_RANGE")] public int PartyRange { get; init; }
    [Legacy("LO_RANGE")] public int LocationRange { get; init; }
    [Legacy("CONST_URG")] public int ConstructionUrgency { get; init; }
}

public sealed record EconomyGlobals
{
    [Legacy("SELL_RATIO")] public int SellRatio { get; init; }
    [Legacy("CRYSTAL_P")] public int CrystalProfit { get; init; }
    [Legacy("REGEN_RUIN")] public int RuinRegeneration { get; init; }
}

public sealed record HeroGlobals
{
    [Legacy("WEAPN_MSTR")] public int WeaponMaster { get; init; }
    [Legacy("LDRMAXABIL")] public int MaxAbilities { get; init; }
    [Legacy("REGEN_LWAR"), Description("Прибавка лечения лорда-воина.")] public int WarlordRegeneration { get; init; }
    [Legacy("TALIS_CHRG")] public int TalismanCharges { get; init; }
}

public sealed record SpellGlobals
{
    [Legacy("SPLPWR_0", "SPLPWR_1", "SPLPWR_2", "SPLPWR_3", "SPLPWR_4"), Description("Сила по расстоянию от центра, %.")]
    public IReadOnlyList<int> AreaPowerPercent { get; init; } = [];
    [Legacy("GAIN_SPELL"), Description("Доля заклинаний, получаемых со взятой столицей, %.")] public int GainOnCapture { get; init; }
}
