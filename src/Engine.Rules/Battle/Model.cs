using LastEdict.Rules.Magic;
using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Классы атак, значения — Globals/LattC.dbf (id не сплошные).</summary>
public enum AttackClass
{
    Damage = 1, Drain = 2, Paralyze = 3, Heal = 6, Fear = 7, BoostDamage = 8, Petrify = 9,
    LowerDamage = 10, LowerInitiative = 11, Poison = 12, Frostbite = 13, Revive = 14,
    DrainOverflow = 15, Cure = 16, Summon = 17, DrainLevel = 18, GiveAttack = 19,
    Doppelganger = 20, TransformSelf = 21, TransformOther = 22, Blister = 23,
    BestowWards = 24, Shatter = 25,
}

/// <summary>Строка Gattacks.dbf. Reach: 1 — все (площадная), 2 — любой, 3 — соседний.
/// Infinite — INFINITE: у DoT это «длинный» эффект (PoisonLong и т. п.), который снимается броском
/// checkLongEffectDuration, иначе — один тик (Attacks.TickDots).</summary>
public sealed record AttackType(string Id, int Initiative, AttackClass Class, int Power, int Reach,
                                int QtyDamage, int QtyHeal, bool CritHit, int Level, bool Infinite = false,
                                int Source = -1, IReadOnlyList<string>? Wards = null,
                                string AltAttackId = "")
{
    public const int ReachAll = 1;

    /// <summary>Gattacks.SOURCE — источник атаки, LattS.dbf: 0 Weapon, 1 Mind, 2 Life, 3 Death,
    /// 4 Fire, 5 Water, 6 Earth, 7 Air. По нему проверяется иммунитет из Gimmu (Immunity.Check).
    /// Заполняет LastEdict.Data.RulesLoader.Load (задача 3); SourceUnset (−1) остаётся у атак, собранных
    /// в тестах вручную, и это видно, а не выглядит как Weapon.
    /// В параметре стоит литерал: на собственную константу первичный конструктор ссылаться не может.</summary>
    public const int SourceUnset = -1;

    /// <summary>Есть ли у атаки запасная — <c>Gattacks.ALT_ATTACK</c> (пустая строка означает «нет»:
    /// <c>RulesLoader.Ref</c> сводит сюда и <c>"g000000000"</c>). Дочитана задачей 8 по долгу задачи 7.
    /// <para><b>Ранг 1.</b> Два класса из двадцати трёх делают своё дело не безусловно, а ветвятся, и
    /// вторая ветвь целиком отдаёт удар запасной атаке. У <c>Doppelganger</c> (<c>onHit</c>
    /// <c>0x6621cd</c>) это происходит, когда <c>cannotUseDoppelgangerAttack</c> истинна (атакующий
    /// уже превращён) или сброшен флаг <c>this+0x14</c>: управление уходит на <c>0x662310</c>, где
    /// <c>this+0x18</c> — объект запасной атаки и вызывается её <c>onHit</c> (<c>call [vft+0x30]</c>,
    /// слот 12) с ТЕМИ ЖЕ четырьмя аргументами. У <c>TransformSelf</c> (<c>onHit</c>
    /// <c>0x6613e1</c>) — то же на <c>0x6614bf</c>, и условие прочитано до конца: сперва
    /// <c>0x62ac90(targetId, this+8)</c> (id различаются), затем сравнение
    /// <c>getAllyOrEnemyGroupId(..., 1)</c> цели и себя, — то есть <b>запасная атака берётся, только
    /// когда цель враг</b>; по себе и по союзнику класс превращает как обычно (превращается при этом
    /// ЦЕЛЬ: <c>getUnit(objectMap, targetUnitId)</c> на <c>0x6614e9</c>).</para>
    /// <para>Слоты <c>canMiss</c> и <c>isImmune</c> у обоих классов ветвятся тем же условием, поэтому
    /// запасная атака приходит целиком — со своей точностью и своим иммунитетом. В модели это
    /// рекурсивный <c>Attacks.Apply</c> по запасной атаке.</para>
    /// <para>В ванили ссылка непуста ровно у двух атак, и обе ведут в класс <c>Damage</c>:
    /// <c>g000aa0164</c> (TransformSelf Волчьего лорда) → <c>g000aa1164</c> (REACH 1, QTY_DAM 40) и
    /// <c>g000aa0171</c> (Doppelganger) → <c>g000aa1171</c> (REACH 3, QTY_DAM 30). Выбор
    /// однозначен — потому ветвь и смоделирована, а не оставлена падать.</para></summary>
    public bool HasAltAttack => AltAttackId.Length > 0;

    private readonly Effects.AttackEffect? _effect;

    /// <summary>Эффект атаки — что делает удар (<see cref="Effects.AttackEffects"/>, этап Э2): признаки движка, тело <c>onHit</c>,
    /// выбор цели и числа ИИ. Задан явно (эффект мода с своими данными) — он, пока класс атаки тот же; иначе — встроенный эффект
    /// <see cref="Class"/> (так <c>attack with { Class = … }</c> не оставляет атаке чужой эффект). В сохранения и журналы не пишется —
    /// он выводится из данных.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Effects.AttackEffect Effect
    {
        get => _effect is { } effect && effect.LegacyClass == Class ? effect : Effects.AttackEffects.Of(Class);
        init => _effect = value;
    }
}

/// <summary>Тип юнита (содержимое unit, в оригинале строка Gunits.dbf). Attack2Id пуст — второй атаки нет.
/// <para>ImmunitiesBySource — строки Gimmu.dbf этого юнита (227 живых на всю ваниль),
/// ImmunitiesByClass — строки GimmuC.dbf (13 живых). Врождённые, в отличие от выданных цепочкой
/// модификаторов (EffectiveStats.Immunities); проверяет и те и другие Immunity.Check. null —
/// таблицы не читались (юнит собран в тесте), пустой список — юнит в них не назван.</para>
/// <para>PrevId — Gunits.PREV_ID, тип предыдущего уровня. В него превращает цель класс DrainLevel
/// (IUsRacialSoldier::getPrevUnitImplId, A drainlevelhooks.cpp:120-135); "g000000000" — предыдущего
/// нет, и тогда понижение уровня не делает ничего (движок выходит из onHit).</para></summary>
public sealed record UnitType(string Id, int Level, int HitPoints, int Armor, string AttackId,
                              string Attack2Id, bool AttackTwice, bool SizeSmall, int XpKilled, int XpNext,
                              IReadOnlyList<ImmunityGrant>? ImmunitiesBySource = null,
                              IReadOnlyList<ImmunityGrant>? ImmunitiesByClass = null,
                              string PrevId = "")
{
    private readonly string _globalId = "";

    /// <summary>Глобальный (<c>UU</c>) id этой реализации: у строки <c>Gunits</c> — она сама, у
    /// порождённой прокачкой (<c>UG</c>, <see cref="Units.UnitGenerator"/>) — база, из которой её
    /// вырастили. Это ровно то сведение, которое движок делает сам —
    /// <c>CUnitGenerator::getGlobalUnitImplId</c> (слот 4 vftable, вызов <c>call [edx+0x10]</c>,
    /// например GOG <c>0x597c61</c>), — и без которого <c>getUnitImplDamageMax</c> не узнал бы
    /// прокачанного лидера: в прошитом списке десяти <c>typeIndex</c> лежат <c>UU</c>, а не <c>UG</c>.
    /// <para><b>Ранг 1</b>, журнал <c>corpus/20260910-passive/passive-004.jsonl</c>: у
    /// <c>S073UN01fa</c> (King's Guard <c>G000UU0044</c>, в бою <c>G000UG1e06</c> после повышения)
    /// событие <c>dmg_max</c> и на первом, и на втором уровне даёт <b>400</b>, а не 300.</para></summary>
    public string GlobalId
    {
        get => _globalId.Length > 0 ? _globalId : Id;
        init => _globalId = value;
    }

    /// <summary><c>Gunits.UNIT_CAT</c> (<see cref="UnitCategory"/>). Нужна бегству ИИ: вор и призванный
    /// отряд не бегут (<c>0x431d8c</c>, <c>0x41b399</c>).</summary>
    public int Category { get; init; }

    /// <summary><c>Gunits.BRANCH</c> (<see cref="UnitBranch"/>). Нужна перестановке отряда ИИ (<c>0x5d5a34</c>).</summary>
    public int Branch { get; init; }

    /// <summary>Лидер — у реализации заполнен <c>Gunits.LEADER_CAT</c> (Lua тулсета <c>isLeader</c>:
    /// <c>leaderType ~= -1</c>). Сюда входят и воры.</summary>
    public bool IsLeader { get; init; }
}

/// <summary><c>LunitC.dbf</c> — категории юнитов (<c>Gunits.UNIT_CAT</c>).</summary>
public static class UnitCategory
{
    public const int Soldier = 0, Noble = 1, Leader = 2, Summon = 4, Illusion = 5, Guardian = 8;

    /// <summary>Реализация юнита — призыв (<c>IUsSummon</c>, приведение <c>0x431d8c</c>): <c>L_SUMMON</c> (<c>0x5a1aa9</c>) и
    /// <c>L_ILLUSION</c> — её наследник (<c>0x5a192e</c> зовёт конструктор призыва). Ранг 1 по листингу (И47).</summary>
    public static bool IsSummonImpl(int? category) => category is Summon or Illusion;
}

/// <summary><c>LunitB.dbf</c> — ветки юнитов (<c>Gunits.BRANCH</c>). Глобали категорий — загрузчик <c>0x5901f0</c>:
/// <c>L_ARCHER</c> <c>0x837f58</c>, <c>L_SIDESHOW</c> <c>0x837f28</c> (сравнение по <c>+8</c>).</summary>
public static class UnitBranch
{
    public const int Fighter = 0, Archer = 1, Mage = 2, Special = 3, Sideshow = 4, Hero = 5, Noble = 6, Summon = 7;
}

/// <summary><c>LOrder.dbf</c> — приказ отряда (<c>MidStack.ORDER</c>/<c>AIORDER</c>).</summary>
public enum StackOrder
{
    None = 0, Normal = 1, Stand = 2, Guard = 3, AttackStack = 4, DefendStack = 5, SecureCity = 6, Roam = 7,
    MoveToLocation = 8, DefendLocation = 9, Bezerk = 10, Assist = 11, Steal = 13, DefendCity = 14,
}

/// <summary>Статус бегства стороны в <c>BattleMsgData</c> (разбор §3).</summary>
public enum RetreatStatus { NoRetreat, Full, Cover }

/// <summary>Сведения о стороне боя, которые журнал не пишет, а бегство ИИ читает (<c>0x5d2779</c>): игрок,
/// раса, приказы отряда (<c>CMidStack</c> <c>+0xa8</c> приказ, <c>+0xac</c> его цель, <c>+0xb8</c> приказ ИИ),
/// остаток хода (<c>+0x98</c>, <c>0x5d2a12</c>). Источник — сохранения рядом с журналом и <c>stack_move</c>
/// (<c>CorpusBattleLoader</c>). <see cref="Known"/> = false — сведений нет, и бежать сторона не может: без них
/// политика ИИ давала бы ложные бегства (разбор §3: 25 на корпусе).</summary>
public sealed record BattleSide
{
    public bool Known { get; init; }
    public bool IsHuman { get; init; }
    /// <summary>Раса играбельна (<c>0x5e53a6</c>): не нейтралы.</summary>
    public bool PlayableRace { get; init; }
    /// <summary>Группа — отряд (<c>KC</c>), а не крепость или руины.</summary>
    public bool IsStack { get; init; }
    public StackOrder Order { get; init; }
    public string OrderTarget { get; init; } = "";
    public StackOrder AiOrder { get; init; }
    /// <summary>Остаток очков хода отряда на момент боя — у нападающего уже после списания <c>ceil(MOVE/2)</c> за
    /// атаку (<c>CEffectChangeAP</c> <c>0x43588C</c> идёт перед <c>CEffectBattle</c> <c>0x4338BA</c>).</summary>
    public int Movement { get; init; }
}

/// <summary>BatInit/BatDamage/DefendBonus/BatBreak/BatBmodif — из GVars.dbf (DFENDBONUS = 50: надбавка
/// брони за защиту, GlobalVariables+0x124; BAT_BREAK = 90 и BAT_BMODIF = 10 — GlobalVariables+0x44/+0x48,
/// порог снятия длинного эффекта: Attacks.LongEffectBreaks). MaxDamage/MaxArmor в таблицах
/// отсутствуют: это ограничения движка — unitMaxDamage 300 и restrictions.unitArmor = {0, 90}
/// (.rdata 0x6e88f4, GOG; читают ветка защиты computeArmor 0x5d2d6f и CUmUnit::getArmor 0x5a468e —
/// одна ячейка, поэтому значение по умолчанию и есть EffectiveStats.ArmorMax; Formulas.Armor).
/// LeaderBonusMaxDamage — fighterExplorerLeaderBonusMaxDamage (100, .rdata 0x6e9720), надбавка к
/// потолку десяти прошитых лидеров: docs/re-damage-max.md, Formulas.DamageMax.</summary>
public sealed record RulesConstants(int BatInit, int BatDamage, int MaxDamage = 300,
                                    int MaxArmor = EffectiveStats.ArmorMax,
                                    int LeaderBonusMaxDamage = 100, int DefendBonus = 50,
                                    int BatBreak = 90, int BatBmodif = 10,
                                    IReadOnlyList<int>? BatBoostDamage = null,
                                    IReadOnlyList<int>? BatLowerDamage = null, int BatLowerIni = 50,
                                    int BatRound = 10)
{
    /// <summary>BATBOOSTD1..4 и BATLOWERD1..2 из GVars.dbf — ранг 1, таблица самой игры (в ванили
    /// 25/50/75/100 и 50/33; BATLOWERI1 = 50). null означает «константы собраны в тесте вручную» и
    /// подставляет вани́льные числа, ровно как это уже делают MaxDamage и MaxArmor, которых в
    /// таблицах нет вовсе. Заполняет LastEdict.Data.RulesLoader.Load.</summary>
    private static readonly int[] VanillaBoostDamage = [25, 50, 75, 100];
    private static readonly int[] VanillaLowerDamage = [50, 33];

    /// <summary>Процент буста урона по уровню статуса BoostDamageLvl1..4 — getBoostDamage
    /// (A attackutils.cpp:96-105): уровень вне 1..N даёт 0, а не крайний элемент.</summary>
    public int BoostDamagePercent(int level) => Percent(BatBoostDamage ?? VanillaBoostDamage, level);

    /// <summary>getLowerDamage (A attackutils.cpp:107-116).</summary>
    public int LowerDamagePercent(int level) => Percent(BatLowerDamage ?? VanillaLowerDamage, level);

    /// <summary>getLowerInitiative (A attackutils.cpp:118-129): уровень, отличный от первого, не
    /// снижает инициативу вовсе.</summary>
    public int LowerInitiativePercent(int level) => level == 1 ? BatLowerIni : 0;

    private static int Percent(IReadOnlyList<int> values, int level) =>
        level > 0 && level <= values.Count ? values[level - 1] : 0;
}

/// <summary>Юнит в бою. Slot и Attacker — из UnitFlags корпуса: биты 0-2 indexInGroup (0..5),
/// бит 3 attacker (battlemsgdata.h:140-159).</summary>
public sealed class BattleUnit
{
    public required string Id { get; init; }
    /// <summary>Тип, атаки и потолок HP — set, а не init, ровно из-за одного класса: DrainLevel
    /// подменяет реализацию юнита прямо в бою (visitors.transformUnit, A drainlevelhooks.cpp:146).
    /// Больше их никто не меняет; задачи 7 и 8 (превращения) переиспользуют этот же путь —
    /// <see cref="LifeAttacks.Retype"/>.</summary>
    public required UnitType Type { get; set; }
    public required AttackType Attack { get; set; }
    /// <summary>Вторая атака (ATTACK2_ID Gunits), null — нет. Применяется после первой к целям,
    /// по которым первая попала и которые остались живы — Attacks.Turn.</summary>
    public AttackType? Attack2 { get; set; }
    public required bool Attacker { get; init; }
    public required int Slot { get; init; }

    /// <summary>Стоит ли юнит на позиции группы <paramref name="position"/>: малый — на своей, большой — на паре (<c>Slot ^ 1</c>): у движка
    /// большой занимает обе клетки группы (<c>CMidUnitGroup::positions</c> — его id в обеих, <c>0x5f4537</c>; toolset
    /// <c>groupview.cpp</c>, размещение большого — <c>positions[p]</c> и <c>positions[p + 1]</c>).</summary>
    public bool Covers(int position) => Slot == position || (!Type.SizeSmall && Formation.Pair(Slot) == position);

    /// <summary>Индекс юнита в его группе (<c>CMidUnitGroup</c>, порядок хранения — <c>UnitIds</c> группы карты); −1 — неизвестен (бой из
    /// журнала, призванный). Им обходит группу раздача излишка <see cref="AttackClass.DrainOverflow"/> (<c>0x65fbb8</c>: <c>0x5f44b9(i)</c>).</summary>
    public int GroupIndex { get; init; } = -1;
    public required int HpMax { get; set; }
    public int Hp { get; set; }
    public int Xp { get; set; }
    public int ShatteredArmor { get; set; }
    public int FortificationArmor { get; set; }
    /// <summary>BattleStatus::Defend (бит 31): юнит защищался и статус ещё не снят — снимает
    /// beforeBattleTurn самого юнита (BattleSim.BeginTurn). Броня — Formulas.Armor.</summary>
    public bool Defending { get; set; }
    /// <summary>BattleStatus::Paralyze (бит 2): пропускает свой ход; короткий снимается в нём же,
    /// длинный (<see cref="DisableLong"/>) — броском (BattleSim.BeginTurn). Ранг 1: corpus/20260906-c, Ghost → Shamaness, st 4 в turn_before и 0
    /// в turn_after без battle_turn и бросков между ними.</summary>
    public bool Paralyzed { get; set; }

    /// <summary>BattleStatus::Petrify (бит 3) — ОТДЕЛЬНЫЙ статус, а не паралич. Ранг 2 (A
    /// battlemsgdata.h:46-47: Paralyze и Petrify — разные биты; B сводит окаменение к параличу,
    /// battle_env.py:4052-4057, и в этом с движком расходится). Корпус слеп: бит 3 не встретился ни
    /// разу ни в одном из восьми каталогов corpus/20260906-* (замер задачи 5), поэтому выбор сделан
    /// по данным движка, а не по журналу. Срок — общий с параличом (<see cref="DisableLong"/>,
    /// обработчик 0x626200 проверяет биты 2 и 3 одинаково; BattleSim.BeginTurn).</summary>
    public bool Petrified { get; set; }

    /// <summary>BattleStatus::DisableLong (бит 4): паралич или окаменение от атаки с INFINITE (onHit
    /// Paralyze 0x65af4c, Petrify 0x65b2ce: <c>getInfinite</c> → бит 4) — держится, пока не сломает бросок
    /// в ход жертвы (<see cref="BattleSim.BeginTurn"/>). Ранг 1: 20260920-gaps [393470].</summary>
    public bool DisableLong { get; set; }

    /// <summary>Раунд наложения паралича/окаменения (onHit 0x65af2d → 0x623369, текущий раунд): от него
    /// считается roundsPassed броска длинного эффекта.</summary>
    public int DisableAppliedRound { get; set; }

    /// <summary>BattleStatus::Retreat (бит 29): жертва Fear собралась бежать; в свой ход уходит с
    /// поля (<see cref="Retreated"/>). Ранг 1: corpus/20260906-c, seq 53143-53147 — попадание Fear
    /// (power 30) ставит цели st = 0x20000000 и больше ничего; бит 4 DisableLong НЕ взводится, хотя
    /// у самой атаки g000aa8146 INFINITE = T.</summary>
    public bool Retreating { get; set; }

    /// <summary>BattleStatus::Retreated (бит 28): юнит ушёл с поля. Жив, но не ходит, не считается
    /// при определении победителя и не тратит броска очереди — см. <see cref="OnField"/>. Ранг 1:
    /// corpus/20260906-c, seq 53197 turn_before → 53198 turn_after той же цели: 0x20000000 →
    /// 0x10000000 без battle_turn и без единого броска, из order она исчезает; в следующем раунде
    /// (seq 53208-53210) очередь строится тремя бросками rng(10) на трёх оставшихся, а отступивший
    /// с hp 118 среди них не значится.</summary>
    public bool Retreated { get; set; }

    /// <summary>BattleStatus::Hidden (бит 30): юнит в бою не участвует (дуэль вора). Ранг 1: построение
    /// очереди <c>0x62457c</c> пропускает юнита по <c>getUnitStatus</c> (<c>0x62271c</c>) с битом 0x1e
    /// (<c>0x624658</c>) раньше мёртвого (1) и отступившего (0x1c); корпус — 13 построений с такими юнитами,
    /// число <c>rng(BAT_INIT)</c> сходится 13/0 с этим правилом и 4/9 без него
    /// (<c>20260910-passive/passive-004 [375338]</c>: три защитника с битом весь бой, два броска на раунд).
    /// Ставится при загрузке из <c>battle_start</c>; за бой не снимается (0 снятий по корпусу).</summary>
    public bool Hidden { get; set; }

    /// <summary>BattleStatus::Summon (бит 27): юнит призван в бою классом Summon, а не стоял в
    /// отряде. Ранг 1 в том, что бит существует и его номер 27 (A battlemsgdata.h:70); ранг 2 в
    /// последствии — призванный не получает опыта (B summon_actions.py, и B сам называет это
    /// отклонением). Опыт Ф6 не считает вовсе, поэтому бит пока только хранится.</summary>
    public bool Summoned { get; set; }

    /// <summary>Кто призвал (<see cref="Summoned"/>): id призывателя — у движка запись пары «призванный — призыватель» в
    /// <c>BattleMsgData</c>, её читает <c>0x62351d</c>. Призыватель пал, ушёл с поля или его нет в бою — призванный изгнан в начале
    /// следующего хода (<see cref="BattleSim.ClearFallenSummons"/>). null — не знаем (бой собран без призыва): не изгоняется.</summary>
    public string? SummonerId { get; set; }

    /// <summary>Изгнан посреди боя (<see cref="BattleSim.ClearFallenSummons"/>): жив, но в бою его больше нет — ни на поле, ни в очереди.</summary>
    public bool Unsummoned { get; set; }

    /// <summary>BattleStatus::TransformDrainLevel (бит 26): юнит понижен в уровне классом DrainLevel.
    /// Ставится вместе с подменой <see cref="Type"/> (GOG <c>0x66298e</c>, <c>push 0x1a</c>). Откат
    /// после боя — <see cref="TransformAttacks.AfterBattle"/> (задача 7).</summary>
    public bool LevelDrained { get; set; }

    /// <summary>BattleStatus::Transform (22) и TransformLong (23) — цель превратил КТО-ТО ДРУГОЙ
    /// (класс TransformOther). Длинный бит ставится, когда у атаки INFINITE: <c>onHit</c>
    /// <c>0x66096e</c> ставит 0x16 всегда, а 0x17 (<c>0x66099e</c>) только под
    /// <c>attack-&gt;getInfinite()</c> (слот 13 IAttackVftable, <c>call [vft+0x34]</c>). Ранг 1.
    /// <para>Все пять вани́льных атак класса 22 несут INFINITE = T, поэтому короткое превращение в
    /// ванили не встречается вовсе и снятие его на границе хода не смоделировано — <b>ранг 3</b>,
    /// снимает мод или бой задачи 13 с атакой класса 22 и INFINITE = F.</para></summary>
    public bool Transformed { get; set; }
    public bool TransformedLong { get; set; }

    /// <summary>Раунд наложения превращения чужим — <c>onHit</c> пишет его безусловно
    /// (<c>0x6609b6</c> → <c>0x6234ed</c>, раунд из <c>0x621fb8</c>); читает его проверка срока
    /// длинного превращения в начале хода жертвы (<c>0x628821</c> → <c>0x6234bc</c>) —
    /// <see cref="BattleSim.BeginTurn"/>. Ранг 1.</summary>
    public int TransformAppliedRound { get; set; }

    /// <summary>BattleStatus::TransformSelf (24) — юнит превратил сам себя (класс TransformSelf,
    /// <c>onHit</c> <c>0x6615e0</c>, <c>push 0x18</c>). Ранг 1.</summary>
    public bool TransformedSelf { get; set; }

    /// <summary>BattleStatus::TransformDoppelganger (25) — юнит скопировал цель (класс
    /// Doppelganger, <c>onHit</c> <c>0x6622b7</c>, <c>push 0x19</c>). Ранг 1.</summary>
    public bool TransformedDoppelganger { get; set; }

    /// <summary>Тип до ПЕРВОГО превращения; null — юнит не превращён. Это пара полей
    /// <c>CMidUnit::transformed</c> (+0x28) и <c>CMidUnit::origTypeId</c> (+0x2c), которые
    /// заполняет <c>CMidUnit::transform</c> (GOG <c>0x5ea292-0x5ea2a2</c>) и только при первом
    /// превращении: второе поверх первого исходный тип не затирает. Их же разбирает парсер
    /// сохранений (<c>MidUnit.TRANSF</c> / <c>OrigTypeId</c>, Sg.g.cs:3804). Откат после боя —
    /// <see cref="TransformAttacks.AfterBattle"/>. Ранг 1.</summary>
    public UnitType? OrigType { get; set; }

    /// <summary>Атаки и потолок HP до ПЕРВОГО превращения — то, что откат возвращает вместе с
    /// <see cref="OrigType"/>. Движок откатывает к исходной реализации юнита (<c>undoTransform</c>
    /// <c>0x5ea3bd</c>), а у прокачанного или порождённого юнита это НЕ строка <c>Gunits</c>: пересчёт
    /// из <see cref="OrigType"/> через <c>Gattacks</c> отдавал новобранца (20260919-human [7958576]:
    /// S073UN01fb после отката бил атакой G000DA4332 — точность 81, урон 69, — а модель давала 80/57).</summary>
    public AttackType? OrigAttack { get; set; }
    public AttackType? OrigAttack2 { get; set; }
    public int OrigHpMax { get; set; }

    /// <summary><c>CMidUnit</c> +0x30 — четвёртый аргумент <c>visitors.transformUnit</c>
    /// (<c>keepHp</c>). Ложь — текущее HP пересчитывается пропорционально новому потолку и при
    /// превращении, и при откате; истина — HP не трогают вовсе. В ванили истина ровно у одного
    /// класса, TransformOther (<c>push 1</c> на <c>0x66093b</c>); TransformSelf, Doppelganger и
    /// DrainLevel передают ноль. Ранг 1.</summary>
    public bool TransformKeepHp { get; set; }

    /// <summary>Юниту выдан внеочередной ход классом GiveAttack: <c>BattleMsgData::giveAttack</c>
    /// (GOG <c>0x62266e</c>) вдвигает его в <c>turnsOrder</c> сразу за ходящим. Флаг живёт до
    /// ближайшей вставки в очередь — <see cref="BattleSim.Run"/>.</summary>
    public bool ExtraTurn { get; set; }

    /// <summary><c>removeTransformStatuses</c> (GOG <c>0x6609fb</c>): все четыре <c>onHit</c>
    /// превращений зовут её ПЕРЕД тем, как поставить свой бит, — то есть биты 22..26 взаимно
    /// исключающи и последнее превращение вытесняет прежнее. Ранг 1.</summary>
    public void ClearTransformStatuses() =>
        Transformed = TransformedLong = TransformedSelf = TransformedDoppelganger = LevelDrained = false;

    /// <summary>Юнит превращён каким угодно из четырёх способов — то самое
    /// <c>CMidUnit::transformed</c>, по которому движок решает, откатывать ли его после боя
    /// (A batlogichooks.cpp:108-113: сперва Summon → unsummon, иначе transformed → untransform).</summary>
    public bool IsTransformed => OrigType is not null;

    /// <summary>BattleStatus::Cured (бит 21), который ставит класс Cure. Что бит делает дальше —
    /// неизвестно (A battlemsgdata.h:64 подписан со знаком вопроса), ранг 3; здесь он только
    /// хранится, чтобы сверка статусов с журналом задачи 13 была возможна.</summary>
    public bool Cured { get; set; }

    /// <summary>BattleStatus::BoostDamageLvl1..4 (биты 5-8) — уровень 1..4, 0 = буста нет; процент
    /// даёт <see cref="RulesConstants.BoostDamagePercent"/> (BATBOOSTD1..4 = 25/50/75/100).
    /// BoostDamageLong (бит 9) — «до конца боя»; конечный снимается в конце хода носителя
    /// (BattleSim.EndTurn).</summary>
    public int BoostDamageLevel { get; set; }
    public bool BoostDamageLong { get; set; }

    /// <summary>BattleStatus::LowerDamageLvl1..2 (биты 10-11) — уровень 1..2 (BATLOWERD1..2 = 50/33),
    /// LowerDamageLong — бит 12.</summary>
    public int LowerDamageLevel { get; set; }
    public bool LowerDamageLong { get; set; }

    /// <summary>BattleStatus::LowerInitiative (бит 13), LowerInitiativeLong — бит 14. Значащий
    /// уровень только 1: getLowerInitiative возвращает 0 для остальных (A attackutils.cpp:118-129),
    /// и других уровней в ванили нет — единственная атака класса, g000aa1163, имеет LEVEL 1.</summary>
    public int LowerInitiativeLevel { get; set; }
    public bool LowerInitiativeLong { get; set; }

    /// <summary>Раунд, в котором BattleSim.BeginTurn уже отработал для этого юнита: ход человека и
    /// автобой стороны игрока зовут beforeBattleTurn дважды за ход (docs/corpus-schema.md, «Ход
    /// человека — два вызова battleTurn»), а тик DoT и снятие паралича — один раз.</summary>
    public int TurnBegunRound { get; set; } = -1;
    public List<DotEffect> Dots { get; } = [];
    /// <summary>Цепочка модификаторов поверх базового типа, в порядке наложения (для сохранённой
    /// игры — порядок хранения MidUnit.Modifiers, Sg.g.cs:3804). Свёртка — EffectiveStats.Of и
    /// EffectiveStats.OfAttack. Пустая цепочка = поведение Ф4/Ф5 бит в бит.</summary>
    public IReadOnlyList<Modifier> Modifiers { get; init; } = [];

    /// <summary>Варды, наложенные в бою (класс BestowWards — Gattacks.WARD1..4 → Gmodif). Тоже
    /// модификаторы, но живут отдельно от Modifiers, потому что снимаются в бою, а цепочка юнита —
    /// нет. Снимает их ход <b>наложившего</b>, а не носителя — <see cref="BestowedWards"/>.</summary>
    public List<Modifier> Wards { get; } = [];

    /// <summary>Варды, которые ЭТОТ юнит раздал: пары «кому — что». Это массив
    /// <c>UnitInfo.modifiedUnits</c> (<c>+0x3c + i*8</c>, восемь пар): его заполняет
    /// <c>BattleMsgData::addUnitToWards</c> (GOG <c>0x62180a</c>, цикл <c>0x62184a</c>), а читает
    /// <c>beforeBattleTurn</c> (<c>0x62673f</c>) — в начале хода раздавшего:
    /// <c>0x621bee</c> собирает цели из <c>+0x3c</c>, <c>CMidUnit::removeModifier</c> (<c>0x5e9984</c>)
    /// снимает вард с каждой, <c>0x621d64</c> чистит запись цели, <c>0x621dfe</c> — массив самого
    /// раздавшего. Ранг 1 (задача 21 Ф7). Снятие — <see cref="Immunity.ClearWards"/>.</summary>
    public List<(BattleUnit Target, Modifier Ward)> BestowedWards { get; } = [];

    /// <summary><c>UnitFlags.waited</c> (бит 4 байта <c>+0x30</c>): юнит в этом раунде уже ждал.
    /// Ставит <c>CBatAttackWait::onHit</c> (GOG <c>0x659e3e</c> → <c>0x622e6e</c>), снимает
    /// <c>beforeBattleRound</c> (<c>0x622078</c>, <c>push 0</c>), читает <c>updateBattleActions</c>
    /// (<c>0x623ef6</c> → <c>0x622eaf</c>): ждавший ждать больше не может. Ранг 1 (задача 24 Ф7),
    /// <see cref="BattleSim.Wait"/>.</summary>
    public bool Waited { get; set; }

    /// <summary><c>UnitFlags.retreatingAfterWait</c> (бит 7 байта <c>+0x30</c>): юнит нажал «Бежать» в отложенный ход.
    /// Ставит <c>CBatAttackRetreat::onHit</c> (GOG <c>0x659fee</c>: при <c>waited</c> — <c>0x622eaf</c> — зовёт сеттер
    /// <c>0x6236d0</c> с 1) раньше выбора «ушёл / уходит»; читает только построение очереди <c>0x62457c</c> (геттер
    /// <c>0x62369c</c> на <c>0x624637</c>): юнит с битом в очередь раунда не попадает, броска инициативы не тратит, а бит тут же
    /// снимается (<c>0x62464e</c>, <c>push 0</c>). Значит, ждавший беглец пропускает весь следующий раунд — на поле и под
    /// ударами (бит 29 остаётся) — и уходит в свой ход раунда после него. Ранг 1 по листингу и журналу 20261016-port-synth
    /// passive-001: WR seq 584032 (<c>S143UN003a</c>: flags 138 в раунде 2, нет в очереди <c>[3c 3b 3d]</c>, бит 28 в раунде 3),
    /// A1 seq 512841/512905 (<c>S143UN000a</c>, <c>000b</c>: нет в очереди раунда 12, <c>000b</c> в нём ранен 82 → 42).
    /// <see cref="BattleSim.Retreat"/>, <see cref="TurnOrder.Build"/>.</summary>
    public bool RetreatingAfterWait { get; set; }

    /// <summary><c>UnitFlags.revived</c> (бит 6 байта <c>+0x30</c>): юнита уже воскрешали в этом бою — второй раз
    /// нельзя. Читает <c>0x623633</c> из <c>0x65c3a7</c> (годность воскрешения у Revive <c>0x65f408</c> и у лечения с
    /// второй атакой Revive <c>0x65c086</c>). Ранг 1; журнал map-001 [1281528]: flags 8 → 72 после воскрешения.</summary>
    public bool Revived { get; set; }

    /// <summary>Предметы в боевых слотах снаряжения носителя. Движок обходит по два слота
    /// снаряжения у предводителя каждой стороны и часть предметов после боя уничтожает —
    /// <see cref="BattleSim.AfterBattle"/>, <c>checkAndDestroyEquippedBattleItems</c> (GOG
    /// <c>0x629755</c>). Применение — <see cref="BattleSim.UseBattleItem"/> (сфера, талисман) и
    /// <see cref="BattleSim.DrinkPotion"/> (зелье); в ход оно не встроено — кто и когда применяет,
    /// решает вызывающий.
    /// <para>Пустой список — поведение Ф4/Ф5 бит в бит: предметы сюда кладёт только вызывающий,
    /// загрузчик боя их не заполняет.</para></summary>
    public List<Item> BattleItems { get; } = [];

    /// <summary>Номера предметов <see cref="BattleItems"/>, применённых в этом бою (задача 10 Ф7).
    /// Применение — удар атакой предмета по цели (<c>BattleSim.UseBattleItem</c>), а не построение
    /// <c>attack_new</c>: его движок пишет на каждом ходу носителя (ранг 1, П§16.4). Повтор в том же
    /// бою ничего не добавляет — это множество, и такова же мера движка: талисман тратит один заряд
    /// за бой независимо от числа применений (<see cref="Charges.TalismanAfterBattle"/>).
    /// <para>Пустое множество — поведение Ф4/Ф5 бит в бит: без применений не исчезает ничто.</para></summary>
    public HashSet<int> AppliedItems { get; } = [];

    /// <summary><c>MOD_POTION</c> зелий, выпитых в этом бою (<see cref="BattleSim.DrinkPotion"/>).
    /// Живут до конца боя: ход их не снимает (ранг 1, ai-q33 — броня +15 в раундах 2 и 3).</summary>
    public List<Modifier> Potions { get; } = [];

    /// <summary>Цепочка, которую видят EffectiveStats: постоянные модификаторы плюс варды и выпитые
    /// зелья. Без них возвращается та же самая ссылка на Modifiers — Ф4/Ф5 не платят ни аллокации.</summary>
    public IReadOnlyList<Modifier> Chain =>
        Wards.Count == 0 && Potions.Count == 0 ? Modifiers : [.. Modifiers, .. Wards, .. Potions];

    /// <summary>battlemsgdata attackSourceImmunityStatuses / attackClassImmunityStatuses: биты
    /// потраченных иммунитетов уровня Once. Номер бита — категория (источник LattS 0..7, класс
    /// LattC 1..25); оба помещаются в 32 бита, как в движке. Расход — Immunity.Blocks.</summary>
    public int SourceImmunityUsed { get; set; }
    public int ClassImmunityUsed { get; set; }

    public bool Alive => Hp > 0;

    /// <summary>Юнит на поле: жив и не отступил. Отступивший (<see cref="Retreated"/>) жив, но для
    /// боя его нет — он не ходит, не бывает целью, не строит очередь и не держит сторону в живых.
    /// Пока никто не отступал, это то же самое, что <see cref="Alive"/>, поэтому Ф4/Ф5 не меняются.
    /// Скрытый (<see cref="Hidden"/>) для боя тоже не существует.</summary>
    public bool OnField => Alive && !Retreated && !Hidden && !Unsummoned;
}

/// <summary>Poison/Frostbite/Blister на юните. AppliedRound — currentRound при наложении
/// (poisonAppliedRound, battlemsgdata.h:210); Long — бит PoisonLong: снимается броском
/// checkLongEffectDuration(round − AppliedRound), иначе после первого тика.</summary>
public sealed class DotEffect
{
    public required AttackClass Class { get; init; }
    public required int Damage { get; init; }
    public required int AppliedRound { get; init; }
    public required bool Long { get; init; }
}

/// <summary>Тик яда, мороза или ожога (<see cref="BattleSim.DotLog"/>): раунд, юнит, класс, урон и здоровье после.</summary>
public sealed record DotTick(int Round, string Unit, AttackClass Class, int Damage, int HpAfter);

/// <summary>Что случилось с юнитом в бою без удара — сообщения боя 13–16 движка, для показа и звука (<c>docs/ai-screen.md</c> §11:
/// <c>UPARALYZE</c>/<c>UPETRIFY</c> — файлов нет, молчат; <c>UNTRANSF</c>, <c>UNSUMMON</c>, <c>UPGRADE{раса}</c>).</summary>
public enum BattleNoteKind
{
    /// <summary>Снят паралич (в свой ход, короткий — сразу, длинный — по броску).</summary>
    Unparalyzed,

    /// <summary>Снято окаменение.</summary>
    Unpetrified,

    /// <summary>Превращение откатилось (длинное — в свой ход по броску, прочие — в конце боя).</summary>
    Untransformed,

    /// <summary>Призванный ушёл в конце боя.</summary>
    Unsummoned,

    /// <summary>Юнит вырос по опыту боя (уровень или тип) — начисление после боя (<c>BattleAftermath.Award</c>).</summary>
    LevelUp,

    /// <summary>Призванный изгнан посреди боя — в начале чьего-то хода (призыватель пал, ушёл или его нет в бою) или в свой ход вместо
    /// бегства (<see cref="BattleSim.ClearFallenSummons"/>). Звук тот же, что у <see cref="Unsummoned"/>.</summary>
    UnsummonedInBattle,
}

/// <summary>Запись для показа (<see cref="BattleState.Notes"/>): раунд, юнит, что случилось. Модель её не читает.</summary>
public sealed record BattleNote(int Round, string UnitId, BattleNoteKind Kind);

/// <summary>Смерть от тика яда, мороза или ожога в начале хода (<see cref="BattleSim.BeginTurn"/>): юнит и место в журнале
/// модели — сколько записей <see cref="BattleSim.Log"/> было к тику. Опыт за неё раздаётся в конце следующего действия
/// (<c>BattleAftermath.Kills</c>).</summary>
public sealed record DotDeath(string UnitId, int LogIndex);

/// <summary>Уход юнита с поля (бит 28 <c>Retreated</c>): юнит и сколько записей <see cref="BattleSim.Log"/> было к уходу. Ушедший
/// опыта за следующие смерти не делит (<c>0x625a8e</c>: делитель без бита 28, <c>0x625bd0</c>; <c>BattleAftermath.Kills</c>).</summary>
public sealed record RetreatMark(string UnitId, int LogIndex);

/// <summary>Применённый в бою предмет: носитель и номер предмета в его боевых слотах.</summary>
public sealed record UsedItem(string UnitId, int Index);

/// <summary>Применение атаки (<see cref="BattleState.Actions"/>): раунд, кто, по кому, номер атаки (1 — основная или запасная, 2 — вторая) и
/// класс, которым ударили на деле (у превращения себя по врагу и двойника без копий — класс запасной атаки, <c>0x6613e1</c>/<c>0x662336</c>).
/// Только для показа и зонда — модель и харнессы её не читают.</summary>
public sealed record ActionNote(int Round, string Unit, string Target, int Attack, AttackClass Class);

/// <summary>Лечение вампиризмом (<see cref="BattleState.DrainHeals"/>): раунд, кто ударил (<c>Drain</c>/<c>DrainOverflow</c>), кого полечило
/// (сам бьющий или союзник излишком) и сколько легло. <c>DrainOverflow::onHit</c> (<c>0x65f81b</c>) кладёт в <c>BattleAttackInfo</c>
/// (<c>0x62c462</c>) запись цели, затем запись бьющего с его прибавкой здоровья (<c>0x65f9f5</c>, и при нуле) и по записи на каждого
/// полеченного излишком (<c>0x65fb4a</c>); экран по ним играет на группе бьющего <c>TUCH</c> класса «лечение» (<c>0x63570a</c>) и пишет «+N»
/// (<c>0x63c27d</c>, только при N &gt; 0, <c>0x63b11d</c>). Только для показа — модель и харнессы её не читают.</summary>
public sealed record DrainHeal(int Round, string Unit, string Target, int Amount);

public sealed class BattleState
{
    /// <summary>Список, а не IReadOnlyList: класс Summon добавляет юнита в бой на ходу
    /// (LifeAttacks.Summon). Ничто другое состав боя не меняет — смерть только обнуляет Hp.</summary>
    public required List<BattleUnit> Units { get; init; }

    /// <summary>Сообщения боя без удара (снятие паралича и превращения, уход призванного, рост) — только для показа и звука
    /// (сессия Г); модель их не читает.</summary>
    public List<BattleNote> Notes { get; } = [];

    /// <summary>Смерти от тиков DoT по порядку (<see cref="DotDeath"/>) — для дележа опыта после боя.</summary>
    public List<DotDeath> DotDeaths { get; } = [];

    /// <summary>Уходы с поля по порядку (бит 28 Retreated, <see cref="RetreatMark"/>) — для дележа опыта после боя.</summary>
    public List<RetreatMark> Retreats { get; } = [];
    public required string AttackerGroup { get; init; }
    public required string DefenderGroup { get; init; }
    public int Round { get; set; }

    /// <summary>Справочники Gunits/Gattacks/Gtransf. Нужны двум классам, которые меняют или создают
    /// юнита прямо в бою: DrainLevel (тип PREV_ID) и Summon (кого призвать). Остальному бою не нужны
    /// вовсе, поэтому поле необязательное: null — «бой собран в тесте без справочников», и эти два
    /// класса тогда падают с внятным текстом, а не тихо ничего не делают.</summary>
    public GameRules? Rules { get; init; }

    /// <summary>Сторона, чей игрок — человек при лёгкой сложности сценария: движок удваивает ей
    /// диапазон случайной добавки к урону (<see cref="Formulas.DamageWithBuffs"/>, <c>0x5d2def</c>).
    /// Признак считает <c>computeDamage</c> по игроку стороны атакующего юнита (<c>0x65b790</c>–
    /// <c>0x65b88b</c>), ранг 1. По умолчанию ложь — средняя сложность или ИИ.</summary>
    public bool AttackersHumanOnEasy { get; init; }
    public bool DefendersHumanOnEasy { get; init; }

    public bool HumanOnEasy(BattleUnit unit) => unit.Attacker ? AttackersHumanOnEasy : DefendersHumanOnEasy;

    /// <summary>Сдвиг точности стороны — <see cref="Formulas.AiPowerShift"/> у стороны ИИ, 0 у человека.
    /// По умолчанию 0 — средняя сложность или сторона не опознана (<c>CorpusBattleLoader</c>).</summary>
    public int AttackersPowerShift { get; init; }
    public int DefendersPowerShift { get; init; }

    public int PowerShift(BattleUnit unit) => unit.Attacker ? AttackersPowerShift : DefendersPowerShift;

    /// <summary>Может ли защищающаяся сторона бежать с поля. Ложь — гарнизон города
    /// (<c>defenderGroupId</c> типа крепости), страж руин или отряд, стоящий в городе.
    /// <para><b>Ранг 1 по дизассемблеру</b> — <c>0x5f51bf</c> (зовётся из <c>0x65c85b</c>, условия
    /// Fear, и из <c>updateBattleActions</c> <c>0x6240e9</c>): группа-крепость или группа-руины —
    /// «нельзя»; отряд с непустым <c>insideId</c> (<c>+0x94</c>) — «нельзя», если он не нападающий
    /// (сравнение с <c>BattleMsgData.attackerGroupId</c> <c>+0xed8</c>, <c>0x6215df</c>). Нападающий
    /// бежать может всегда. Как это значение собрано из журнала — см. <c>CorpusBattleLoader</c>.</para></summary>
    public bool DefendersCanRetreat { get; init; } = true;

    /// <summary>Сведения сторон для бегства ИИ (<see cref="AiRetreat"/>); по умолчанию неизвестны.</summary>
    public BattleSide AttackerSide { get; init; } = new();
    public BattleSide DefenderSide { get; init; } = new();

    public BattleSide SideOf(BattleUnit unit) => unit.Attacker ? AttackerSide : DefenderSide;

    /// <summary>Бой ограничен <c>BAT_ROUND</c> раундами (<see cref="RulesConstants.BatRound"/>, <see cref="BattleSim.BeginRound"/>):
    /// в бою нет человека или включён быстрый бой (<c>0x626b2c</c>). Ставят сборщик боя с карты и быстрый бой; по умолчанию
    /// ложь — бои из журнала сторон не знают.</summary>
    public bool LimitRounds { get; set; }

    /// <summary>Позиция призыва: выбранная человеком (боевой предмет брошен на пустую позицию, цель — позиция из набора
    /// сервера) или ИИ (<c>0x5d1079</c>, <c>AiRareTargets</c>). null — первая свободная (<see cref="LifeAttacks.Summon"/>);
    /// призыв её сбрасывает.</summary>
    public int? SummonSlot { get; set; }

    /// <summary>Id призванного юнита (<see cref="LifeAttacks.Summon"/>). Движок берёт его из счётчика карты: <c>CBatAttackSummon::onHit</c>
    /// <c>0x6631eb</c> (vftable <c>0x6f3584</c>, слот 12) → <c>0x663507</c> addUnitToGroup <c>0x5e78f7</c> → <c>0x61235c</c> → слот 9 карты
    /// <c>0x5ee8bd</c>, тип UN; призванный в сохранение не попадает, номер не возвращается (ai-q09 день 5: <c>UN0274</c>…<c>UN0278</c>, следом
    /// найм PL0002 — <c>UN0279</c>). Ставит бой с карты (<c>GameSession.StartBattle</c>); null — бой без карты (журнал, тест), id вида
    /// <c>{призыватель}-summon{N}</c>.</summary>
    public Func<string>? NewUnitId { get; set; }

    /// <summary>MSVCRT <c>rand()</c> процесса (<c>generateRandomNumberStd</c> <c>0x548576</c>) — им движок выбирает, кого призвать или в кого
    /// превратить, когда кандидатов после фильтра по размеру больше одного (<see cref="LifeAttacks.Pick"/>). Ставит бой с карты (поток CRT
    /// партии, <c>GameSession.Crt</c>). null — бой без карты (журнал С2, тест): поток процесса неизвестен, неоднозначный выбор падает с
    /// текстом, а не угадывает — иначе сверка испортилась бы молча.</summary>
    public LastEdict.Rules.Rng.CrtRand? ProcessRand { get; set; }

    /// <summary>Применённые за бой предметы — носитель и номер в его <see cref="BattleUnit.BattleItems"/> на момент применения
    /// (<c>BattleMsgData.usedItemIds</c> <c>+0xf20</c>, пополняет <c>0x6230ab</c>). В отличие от
    /// <see cref="BattleUnit.AppliedItems"/> конец боя (<see cref="BattleSim.AfterBattle()"/>) его не чистит: по нему хозяин
    /// тратит предметы на карте.</summary>
    public List<UsedItem> UsedItems { get; } = [];

    /// <summary>Все применения атак по порядку (<see cref="ActionNote"/>): и с записью хода, и без неё (атаки по своим, призыв). Для
    /// показа (класс удара) и для зонда (<c>battle_end classes</c>).</summary>
    public List<ActionNote> Actions { get; } = [];

    /// <summary>Лечения вампиризмом по порядку (<see cref="DrainHeal"/>) — только для показа.</summary>
    public List<DrainHeal> DrainHeals { get; } = [];

    /// <summary>Павшие призванные, снятые с поля (<see cref="BattleSim.ClearFallenSummons"/>): в бою их больше нет — ни в очереди, ни в
    /// списках целей и перемешивания, ни трупом на поле; <see cref="Unit"/> их ещё находит (опыт за убийство, показ смерти).</summary>
    public List<BattleUnit> Gone { get; } = [];

    /// <summary>Статус бегства сторон и флаг «решение о прикрытии принято» (<c>BattleMsgData</c>, <c>0x5d2a4d</c>).</summary>
    public RetreatStatus AttackersRetreat { get; set; }
    public RetreatStatus DefendersRetreat { get; set; }
    public bool RetreatDecided { get; set; }

    /// <summary>Может ли юнит бежать с поля (<see cref="DefendersCanRetreat"/>).</summary>
    public bool CanRetreat(BattleUnit unit) => unit.Attacker || DefendersCanRetreat;

    public BattleUnit? Unit(string id) =>
        Units.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Gone.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<BattleUnit> Enemies(BattleUnit of) => Units.Where(u => u.Attacker != of.Attacker);
    public IEnumerable<BattleUnit> Allies(BattleUnit of) => Units.Where(u => u.Attacker == of.Attacker);

    /// <summary>
    /// Позиции группы <paramref name="summoner"/> (0..5, по возрастанию), куда можно призвать: цели призыва (<c>fillTargetsList</c>
    /// <c>0x662ac6</c>: юниты группы — <c>0x65a1a5</c>, пустые позиции — <c>0x662ca6</c>/<c>0x662d1c</c>), то есть позиции, где
    /// <c>canPerform</c> призыва (<c>0x662f80</c>) истинен: клетка пуста или на ней павший либо ушедший (статусы 1 и <c>0x1c</c>). Живой
    /// юнит держит свою клетку, большой — обе (<see cref="BattleUnit.Covers"/>): у движка его id в обеих позициях группы, и ни одна не пуста;
    /// скрытый (дуэль вора) — тоже держит. Изгнанный призванный клетку освобождает. Прежде порт считал занятой только <c>Slot</c> большого,
    /// и ИИ ставил призванного во вторую клетку тролля (бой у города орков, партия 17.10: Лиф призвал четверых в полный отряд).
    /// </summary>
    public IReadOnlyList<int> SummonCells(BattleUnit summoner)
    {
        ArgumentNullException.ThrowIfNull(summoner);
        var holders = Allies(summoner).Where(u => u.Alive && !u.Retreated && !u.Unsummoned).ToList();
        return [.. Enumerable.Range(0, Formation.Cells).Where(p => !holders.Any(u => u.Covers(p)))];
    }
}
