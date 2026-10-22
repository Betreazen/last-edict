using LastEdict.Rules.Battle;

namespace LastEdict.Rules.Units;

/// <summary>Состояние юнита, от которого зависит повышение: тип (<c>MidUnit.TYPE</c>), уровень, опыт и флаг
/// «Остановить продвижение» (<c>DYNLEVEL</c>).</summary>
public sealed record UnitProgress(string TypeId, int Level, int Xp, bool DynLevel);

/// <summary>Итог повышения. <see cref="MissingBuildings"/> непусто — юнит упёрся в <c>XP_NEXT − 1</c>, потому
/// что ни одной постройки наследников у игрока нет (это те здания, что называет свиток тренера).</summary>
public sealed record UpgradeOutcome(UnitProgress Unit, IReadOnlyList<string> MissingBuildings);

/// <summary>
/// Смена типа и уровня юнита (не лидера) при наборе опыта — план Ф7 §1.3, задача 4.
/// <para>Ранг 1: ветка солдата <c>0x5D7FE7</c> — следующий тир по <c>PREV_ID</c>/<c>UPGRADE_B</c>, иначе
/// порождённая реализация; <c>getUpgradeUnitImplCheckXp</c> <c>0x5D8B17</c> — порог <c>currentXp &lt; xpNext</c>;
/// <c>IsUnitUpgradePending</c> <c>0x61E6D9</c> первым делом смотрит <c>CMidUnit::dynLevel</c> (<c>0x61e70f</c>)
/// и при нём наследников не собирает (run3 §3.3).</para>
/// </summary>
public static class UnitUpgrade
{
    /// <summary>Наследники типа — юниты содержимого с <c>PREV_ID == typeId</c>, в порядке содержимого (у базы — порядок
    /// таблицы <c>Gunits</c>; правила берут только их число и открытость, порядок — для окон). Ранг 1: у Archer один (Marksman),
    /// у Squire два (Knight, Witch-Hunter) — run3 §3.2, run4 §7.2.</summary>
    public static IReadOnlyList<UnitType> Successors(IReadOnlyDictionary<string, UnitType> units, string typeId) =>
        units.Values.Where(u => u.PrevId.Equals(typeId, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Наследники юнита уровня <paramref name="level"/>: у выросшего на месте (<see cref="IsGrown"/>) их нет. Ранг 1 —
    /// листинг (capstone, 16.10): поиск наследников <c>0x5d7e07</c> сравнивает <c>PREV_ID</c> строк <c>Gunits</c> с id
    /// <b>реализации</b> юнита (<c>[esi+4]</c>), а у выросшего она порождённая (<c>G000UG…</c>) — совпадений нет, и цель повышения
    /// <c>0x5d7fe7</c> — снова рост на месте. Корпус: <c>20261016b-port-synth</c>, карта 4 — <c>UU0052</c> уровня 8 (поставлен
    /// генератором) при закрытом наследнике <c>UU0053</c> (<c>BB0050</c> не построен): дневной опыт 94 → 95, <c>upgrade_target</c>
    /// <c>G000UG23c4</c> → <c>G000UG23c5</c>, в сохранении следующего дня уровень 9 и опыт 0; ИИ при этом не строит <c>BB0050</c>
    /// «под юнита у порога» (строки <c>ai_build_pick</c>).</summary>
    public static IReadOnlyList<UnitType> Successors(IReadOnlyDictionary<string, UnitType> units, string typeId, int level) =>
        IsGrown(units, typeId, level) ? [] : Successors(units, typeId);

    /// <summary>Вырос ли юнит на месте: уровень выше <c>Gunits.LEVEL</c> своего типа — реализация у него порождённая
    /// (<c>GDynUpgr</c>), а не строка <c>Gunits</c>.</summary>
    public static bool IsGrown(IReadOnlyDictionary<string, UnitType> units, string typeId, int level)
    {
        ArgumentNullException.ThrowIfNull(units);
        return units.TryGetValue(typeId, out var type) && level > type.Level;
    }

    /// <summary>Постройки, открывающие наследников: <c>UPGRADE_B</c> <b>наследника</b>, а не самого юнита
    /// (все 102 строки с <c>PREV_ID</c> несут <c>UPGRADE_B</c>). Ранг 1, run3 §3.2. При <c>DYNLEVEL</c> список
    /// пуст — <c>0x61e70f</c>.</summary>
    public static IReadOnlyList<string> SuccessorBuildings(IReadOnlyDictionary<string, UnitType> units,
                                                           IReadOnlyDictionary<string, string> upgradeBuilding,
                                                           UnitProgress unit) =>
        unit.DynLevel ? [] : Successors(units, unit.TypeId, unit.Level)
            .Select(s => upgradeBuilding.GetValueOrDefault(s.Id, "")).Where(b => b.Length > 0).ToList();

    /// <summary>
    /// Повышение после начисления опыта. <b>Излишек не переносится</b>: после повышения опыт 0 (ранг 1 — план Ф7 §10.6
    /// и корпус: последний тир <c>UU0154</c> с опытом 1099 из 1100 и строкой <c>xp</c> 12 — в сохранении уровень 4 и
    /// опыт 0; вторая большая партия <c>UU0052 → UU0053</c> с опытом 0). <i>Было иначе:</i> «<c>currentXp −= xpNext</c>,
    /// циклом» (levels §2.4 по тулсету).
    /// <list type="bullet">
    /// <item><c>DYNLEVEL</c> или наследников нет — тот же тип, уровень + 1 («рост на месте» по <c>GDynUpgr</c>;
    /// ранг 1 — <c>f7-m9-xpstop</c>: Archer остался <c>UU0006</c> уровня 2);</item>
    /// <item>есть наследник, чья постройка у игрока есть, — его тип и его <c>LEVEL</c> (Squire → Knight L2,
    /// Acolyte → Priest L2; ранг 1). Двух открытых сразу не бывает: постройки ветки взаимоисключающие
    /// (<see cref="LastEdict.Rules.Map.BuildStatus.SiblingUnitBuilding"/>), поэтому берётся первый;</item>
    /// <item>постройки нет ни одной — тип прежний, опыт упирается в <c>XP_NEXT − 1</c> (ранг 1: план Ф7 §1.3; корпус —
    /// <c>UU0007</c> 499/500, <c>UU0011</c> 79/80, <c>UU0009</c> 549/550 держатся на потолке несколько сохранений подряд).
    /// Исключения — <c>UU0057</c> 275/275 первой и <c>UU0093</c> 500/500 второй большой партии: оба дошли до порога
    /// ежедневными строками <c>xp</c> (без боя, сразу за <c>income</c> ИИ; источник не разобран, ранг 3), харнесс
    /// печатает их в <c>unit_upgrade_xp · окно с ежедневной строкой xp</c>.</item>
    /// </list>
    /// ponytail: потолок уровня сценария <c>MAX_UNIT</c> здесь не проверяется — в корпусе в него никто не
    /// упёрся; добавить параметром, когда появится случай.
    /// </summary>
    /// <param name="xpNextOf">Порог по (тип, уровень) — <c>XP_NEXT</c> с ростом <c>GDynUpgr</c>.</param>
    public static UpgradeOutcome Apply(UnitProgress unit, Func<string, int, int> xpNextOf,
                                       IReadOnlyDictionary<string, UnitType> units,
                                       IReadOnlyDictionary<string, string> upgradeBuilding,
                                       IReadOnlySet<string> built)
    {
        for (int guard = 0; guard < 100; guard++)
        {
            int next = xpNextOf(unit.TypeId, unit.Level);
            if (next <= 0 || unit.Xp < next) return new UpgradeOutcome(unit, []);
            IReadOnlyList<UnitType> successors = unit.DynLevel ? [] : Successors(units, unit.TypeId, unit.Level);
            if (successors.Count == 0)
            {
                unit = unit with { Level = unit.Level + 1, Xp = 0 };
                continue;
            }
            var open = successors.FirstOrDefault(s => IsOpen(s, upgradeBuilding, built));
            if (open is null)
                return new UpgradeOutcome(unit with { Xp = next - 1 }, SuccessorBuildings(units, upgradeBuilding, unit));
            unit = unit with { TypeId = open.Id, Level = open.Level, Xp = 0 };
        }
        throw new InvalidOperationException($"повышение юнита {unit.TypeId} не сошлось за 100 шагов — порог не растёт");
    }

    private static bool IsOpen(UnitType successor, IReadOnlyDictionary<string, string> upgradeBuilding,
                               IReadOnlySet<string> built)
    {
        var building = upgradeBuilding.GetValueOrDefault(successor.Id, "");
        return building.Length == 0 || built.Contains(building);
    }

    /// <summary>
    /// Дневной опыт ИИ у лидера (<c>UNIT_CAT</c> 2) — тот же путь <c>0x431c77</c>, но цели повышения у лидера ищет <c>0x5d89c6</c>:
    /// прибавка (<c>0x5d7412</c>) зовёт её без создания типа (<c>push 0</c>, <c>0x5d747d</c>) — если реализации следующего уровня в этом
    /// запуске ещё нет (<paramref name="nextTierCreated"/>), прибавка на 1 меньше и опыт садится на <c>XP_NEXT</c> (<see cref="Experience.LeaderXpAfter"/>), а следом <c>0x4318ad</c> →
    /// <c>0x5d8b17</c> зовёт её уже с созданием (<c>push 1</c>, <c>0x5d8b5d</c>) — и лидер на пороге повышается тут же, если не на
    /// потолке <c>MAX_LEADER</c> (<see cref="Experience.LevelUpLeader"/>). Ранг 1 по листингу и по корпусу: <c>20261016e-av</c>, карта 5
    /// (трудная), <c>S143UN0027</c> G000UU0096 164 + 6 → 165 = XP_NEXT, тут же <c>upgrade_target</c> с возвратом <c>0x5d8b68</c> и целью
    /// G000UG4332 (первое её появление в журнале), <c>skill_pick</c>, уровень 2 опыт 0; так же <c>S143UN000a</c> G000UU0044 224 + 9 → 225.
    /// </summary>
    /// <param name="alive">Здоровье больше 0. Павшему прибавки нет (<c>0x5d7412</c>), но проверка повышения <c>0x4318ad</c> идёт
    /// (<see cref="ApplyDaily"/>).</param>
    /// <param name="nextTierCreated">Реализация следующего уровня уже создана в этом запуске (<c>0x5d89c6(…, 0)</c> её находит) — прибавка
    /// без «−1».</param>
    public static UnitProgress ApplyDailyLeader(UnitProgress leader, int amount, Func<string, int, int> xpNextOf, int maxLeader,
                                                bool alive = true, bool nextTierCreated = false)
    {
        ArgumentNullException.ThrowIfNull(xpNextOf);
        int xp = alive ? Experience.LeaderXpAfter(leader.Xp, amount, xpNextOf(leader.TypeId, leader.Level), nextTierCreated) : leader.Xp;
        var up = Experience.LevelUpLeader(new LeaderProgress(leader.Level, xp, 0), l => xpNextOf(leader.TypeId, l), maxLeader);
        return leader with { Level = up.Level, Xp = up.Xp };
    }

    /// <summary>
    /// Дневной опыт ИИ (<see cref="Experience.AiDailyXp"/>) и повышение за ним — путь <c>0x431c77</c>:
    /// <c>changeUnitXpCheckUpgrade 0x5d7412</c>, затем <c>0x4318ad</c>. Ранг 1 по листингу (capstone, 17.09):
    /// обе функции ищут цель повышения солдата одной и той же <c>0x5d7fe7</c>. Нет цели —
    /// прибавка при переходе порога уменьшается на 1 (<c>0x5D749E</c>), опыт зажимается в <c>[0, XP_NEXT]</c>
    /// (<see cref="Experience.LeaderXpAfter"/> — та же функция) и повышения нет (<c>0x5d8b17</c> отдаёт NULL): юнит
    /// может стоять на самом <c>XP_NEXT</c>, а не на <c>XP_NEXT − 1</c>, как после боя.
    /// <para>Цели нет, когда юнит не «у своей расы» (<see cref="UpgradeRace.Allows"/>: раса на карте со столицей, а хозяин группы —
    /// другой игрок), или когда все наследники закрыты постройками. Без наследников (и при <c>DYNLEVEL</c>) цель — рост на месте
    /// (порождённая реализация уровнем выше, <c>0x5d8514</c>; создаётся тут же, если её ещё нет). Корпус (ранг 1): <c>20260918-probe65</c>
    /// q09, <c>UU0018</c> у нейтрала при Империи со столицей 456 + 19 → 474, 474 + 19 → 475 и шесть дней на 475 без повышения;
    /// <c>20261016e-av</c>, карта 5 — нейтральные <c>UU5017</c> (раса без столицы) 48 + 4 → 50 и рост на месте; закрытые наследники
    /// <c>UU0057</c>/<c>UU0093</c>/<c>UU0053</c> садятся на <c>XP_NEXT</c> (52 строки «−1» и 40 «на пороге» во всём корпусе,
    /// 0 противоречий), открытые — 74 + 1 → 75 и повышение (21 строка). <i>Было иначе</i> (до 17.10): рост на месте — только у
    /// уже выросшего юнита («реализация следующего уровня создана в этом запуске»).</para>
    /// <para>ponytail: категории, для которых <c>0x5d8f84</c> сразу отдаёт NULL (стражи столицы и др.), здесь не
    /// различаются — вызывающий их сюда не передаёт (<c>Experience.GainsXp</c>).</para>
    /// </summary>
    /// <param name="raceAllows">Вход <c>0x5d7fe7</c> пропускает юнита (<see cref="UpgradeRace.Allows"/>).</param>
    /// <param name="alive">Здоровье больше 0. Павшему прибавки нет (<c>0x5d7412</c>: строка xp с xp_after = xp_before при hp 0), но
    /// проверка повышения <c>0x4318ad</c> за ней идёт и у него (<c>0x431d02</c> → <c>0x431d11</c>, здоровье не проверяется): павший на
    /// <c>XP_NEXT</c>, чья цель открылась, повышается. Ранг 1 по листингу и корпусу: <c>20261020-port-synth</c> (карта 23) S143UN0082
    /// G000UU0063 500 = XP_NEXT, hp 0, день 29 — <c>upgrade_target</c> G000UU0064 (seq 4405945), в сохранении t29 ур. 3 опыт 0.</param>
    public static UpgradeOutcome ApplyDaily(UnitProgress unit, int amount, Func<string, int, int> xpNextOf,
                                            IReadOnlyDictionary<string, UnitType> units,
                                            IReadOnlyDictionary<string, string> upgradeBuilding,
                                            IReadOnlySet<string> built, bool raceAllows, bool alive = true)
    {
        IReadOnlyList<UnitType> successors = unit.DynLevel ? [] : Successors(units, unit.TypeId, unit.Level);
        bool found = raceAllows && (successors.Count == 0 || successors.Any(s => IsOpen(s, upgradeBuilding, built)));
        int xp = alive ? Experience.LeaderXpAfter(unit.Xp, amount, xpNextOf(unit.TypeId, unit.Level), found) : unit.Xp;
        var after = unit with { Xp = xp };
        return found ? Apply(after, xpNextOf, units, upgradeBuilding, built) : new UpgradeOutcome(after, []);
    }

    /// <summary>
    /// Опыт боя юниту (строка <c>xp</c> с возвратом <c>0x665788</c>) — та же <c>changeUnitXpCheckUpgrade 0x5d7412</c>, что у дневного опыта:
    /// цель повышения (<c>0x5d7fe7</c>: «у своей расы» и есть открытый наследник, или наследников нет) есть — опыт и повышение как прежде
    /// (<see cref="Apply"/>: последний тир берёт уровни циклом); цели нет — у порога прибавка на 1 меньше и опыт зажат в <c>[0, XP_NEXT]</c>
    /// (<see cref="Experience.LeaderXpAfter"/>), повышения нет. У юнита игрока прибавку закрытого наследника уже урезал <c>upgradeGroup</c>
    /// до <c>XP_NEXT − 1</c> (<see cref="Experience.BattleXp"/>), и порог не переходится; у нейтрала она не урезана («последний тир» в
    /// <c>upgradeGroup</c>) — он садится на сам <c>XP_NEXT</c>. Корпус: <c>20260918-probe65</c> ai-q09 <c>S143UN021a</c>/<c>021b</c>
    /// G000UU0006 27 + 96 → 70 (порог 70); <c>20261016e-av</c> и <c>20261016g-port-synth</c>, карта 8, <c>S143UN0015</c> G000UU0036 0 + 157 →
    /// 130 (порог 130), в сохранении после боя уровень 1. Прежде порт сажал их на <c>XP_NEXT − 1</c> (харнесс unit_upgrade_xp 4 расхождения).
    /// </summary>
    public static UpgradeOutcome ApplyBattle(UnitProgress unit, int amount, Func<string, int, int> xpNextOf,
                                             IReadOnlyDictionary<string, UnitType> units,
                                             IReadOnlyDictionary<string, string> upgradeBuilding,
                                             IReadOnlySet<string> built, bool raceAllows)
    {
        ArgumentNullException.ThrowIfNull(xpNextOf);
        IReadOnlyList<UnitType> successors = unit.DynLevel ? [] : Successors(units, unit.TypeId, unit.Level);
        bool found = raceAllows && (successors.Count == 0 || successors.Any(s => IsOpen(s, upgradeBuilding, built)));
        if (found) return Apply(unit with { Xp = unit.Xp + Math.Max(amount, 0) }, xpNextOf, units, upgradeBuilding, built);
        // Нулевой прибавки upgradeGroup в 0x5d7412 не передаёт (0x665762: jle 0x66579c) — иначе «−1» без цели (0x5d749e) снял бы опыт,
        // уже стоящий на XP_NEXT, до XP_NEXT − 1 (И230).
        int xp = amount > 0 ? Experience.LeaderXpAfter(unit.Xp, amount, xpNextOf(unit.TypeId, unit.Level), false) : unit.Xp;
        return new UpgradeOutcome(unit with { Xp = xp }, raceAllows ? SuccessorBuildings(units, upgradeBuilding, unit) : []);
    }
}
