using D2.Rules.Units;

namespace D2.Rules.Map;

/// <summary>
/// Ответ проверки роста города. Значения — коды отказа движка, ранг 1 (cities §2.4, план Ф7 §1.4):
/// 11 — бунт, 4 — максимум, 7 — уже рос сегодня, 3 — нет денег. Порядок проверок тот же, в каком
/// они перечислены разбором; отдельного замера порядка нет (ранг 2 на порядок).
/// </summary>
public enum CityGrowCheck
{
    Allowed = 0,
    NotEnoughBank = 3,
    MaxSize = 4,
    GrewToday = 7,
    Riot = 11,
}

/// <summary>
/// Укрепления — рост деревни, её защита и захват (план Ф7 §1.4, задача 5). Регенерация — не сюда,
/// она в <see cref="Regeneration"/> (задача 6).
///
/// <para><b>Видов два: столица и деревня</b> (<c>LFort.dbf</c>: 2 <c>L_CAPITAL</c>, 4
/// <c>L_VILLAGE</c>). У деревни есть <c>SIZE</c> (уровень 1…5), <c>GROWTH_T</c> (ход последнего
/// роста) и <c>RIOT_T</c> (ход окончания бунта); у столицы все три в журнале равны −1
/// (<c>corpus-schema.md</c>, «Укрепления»). Ранг 1.</para>
///
/// <para><b>Защита — модификатор брони, а не отдельная величина.</b> <c>GcityInf.PROTECTION</c>
/// деревни и <c>Grace.PROTECTION</c> столицы — идентификаторы строк <c>Gmodif</c>, у которых один
/// элемент <c>GmodifL TYPE 5</c> (<see cref="ModifierElementType.Armor"/>) с полем <c>NUMBER</c>.
/// В ванили это <c>g000um3001…3005</c> = 10/15/20/25/30 и <c>g000um6001</c> = 50 (ранг 1, таблицы;
/// проверено <see cref="Protection"/> по <c>Globals</c>). <c>PROT_*</c> из <c>GVars</c> не читает
/// никто (cities §1.2). Как эта величина ложится на броню — <see cref="Battle.Formulas.Armor"/>
/// (отдельный слой <c>CUmUnit</c>, потолок 90, задача 29 Ф7).</para>
/// </summary>
public static class Fortifications
{
    /// <summary>Потолок уровня деревни: по пять строк <c>GcityInf</c> на расу, <c>SIZE</c> 1…5
    /// (<c>GROWTH</c> у пятой нулевой — расти дальше некуда). Ранг 1 — таблица.</summary>
    public const int MaxSize = 5;

    /// <summary><c>LunitC</c> 4 <c>L_SUMMON</c> — призванный юнит (<c>Gunits.UNIT_CAT</c>).</summary>
    public const int SummonCategory = 4;

    /// <summary><c>LunitC</c> 5 <c>L_ILLUSION</c> — иллюзия: её реализация (<c>0x5a192e</c>) наследует призыв (<c>IUsSummon</c>,
    /// конструктор <c>0x5a1aa9</c>), и проверки «призванный» (<c>0x431d8c</c>) отвечают на неё так же.</summary>
    public const int IllusionCategory = 5;

    /// <summary><c>LleadC</c> 5 <c>L_NOBLE</c> — вор (<c>Gunits.LEADER_CAT</c>, план Ф7 §1.7
    /// «Вор — <c>LEADER_CAT = 5</c>»). Ранг 1 — таблица.</summary>
    public const int ThiefLeaderCategory = 5;

    /// <summary>
    /// Можно ли отстроить деревню на уровень. Коды в порядке 11 → 4 → 7 → 3 (<c>growCity</c>
    /// <c>0x5da01e</c>, cities §2.4): рост мгновенный и не чаще раза в день <b>на город</b> —
    /// сравнивается <c>GROWTH_T</c> самой деревни, а не «строил ли игрок сегодня».
    /// </summary>
    /// <param name="riotTurn"><c>RIOT_T</c>; бунт — <see cref="Rioting"/> (до сброса <see cref="EndRiot"/>,
    /// даже когда ход уже достиг <c>RIOT_T</c>).</param>
    /// <param name="growthTurn"><c>GROWTH_T</c> — ход последнего роста.</param>
    /// <param name="price"><see cref="Treasury.CityGrowthPrice"/> (со скидкой лорда-Дипломата).</param>
    public static CityGrowCheck CheckGrow(int size, int riotTurn, int growthTurn, int turn, Bank purse, Bank price)
    {
        if (Rioting(riotTurn)) return CityGrowCheck.Riot;
        if (size >= MaxSize) return CityGrowCheck.MaxSize;
        // 0x5dc62a: рост можно, только если [+0x94] GROWTH_T < ход; иначе код 7 — и при GROWTH_T > ход (порт сравнивал «==»).
        if (growthTurn >= turn) return CityGrowCheck.GrewToday;
        if (Economy.NotEnough(purse, price)) return CityGrowCheck.NotEnoughBank;
        return CityGrowCheck.Allowed;
    }

    /// <summary>
    /// Ход конца мятежа: <c>RIOT_T = ход + RIOT_MIN + 1 + rand(RIOT_MAX − RIOT_MIN)</c> — 2…4 хода в ванили
    /// (<c>0x41E0A5…0x41E110</c>; ai-q11 <c>riot_duration_in_2_4</c> 6/0). Ранг 1. <i>Было иначе:</i>
    /// «длительность 4» (план §1.7, ранг 2).
    /// ponytail: бросок — MSVCRT <c>rand() % n</c> (<c>0x548576</c>), а не R250; модели MSVCRT в порте нет,
    /// поэтому генератор даёт вызывающий (<see cref="Rng.IRng"/> той же семантики: <c>Next(0) = 0</c>).
    /// Подставлять <see cref="Rng.EngineRng"/> нельзя — R250 этот бросок не видит. Снимет модель MSVCRT <c>rand</c>.
    /// </summary>
    public static int RiotEnd(ThiefRules rules, int turn, Rng.IRng msvcrtRand)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(msvcrtRand);
        return turn + rules.RiotMin + 1 + msvcrtRand.Next(rules.RiotMax - rules.RiotMin);
    }

    /// <summary>Бунтует ли укрепление: <c>RIOT_T &gt; 0</c> (<c>0x5D37E8</c>: <c>0 &lt; [fort+0x98]</c>). У
    /// столицы −1. Ранг 1 — ai-q11 <c>riot_active_until_reset</c> 4/0 (предикат «<c>RIOT_T</c> &gt; ход» дал 0/4).</summary>
    public static bool Rioting(int? riotTurn) => riotTurn > 0;

    /// <summary>Кончается ли мятеж в начале хода владельца: <c>ход ≥ RIOT_T</c>. До этого момента город
    /// бунтует и на ходах других игроков (ai-q11: ход 4 при <c>RIOT_T</c> 4 — пустой список у человека).</summary>
    public static bool RiotEndsAt(int riotTurn, int turn) => Rioting(riotTurn) && turn >= riotTurn;

    /// <summary>Начало хода владельца: при <see cref="RiotEndsAt"/> <c>RIOT_T</c> обнуляется до лечения — деревня в этот день
    /// уже лечит (<see cref="Regeneration.Tick"/>), — а разовый <see cref="Regeneration.RiotDamage"/> гарнизону и визитёру (от HP до
    /// лечения) и запас хода визитёра в 0 (<c>ap_effect cause riot_turn_start</c> 30 → 0) идут после лечения и роста земли.
    /// Урон и запас двигает вызывающий; иначе запись возвращается как есть.</summary>
    public static MapSite EndRiot(MapSite fort, int turn)
    {
        ArgumentNullException.ThrowIfNull(fort);
        return fort.RiotTurn is { } riot && RiotEndsAt(riot, turn) ? fort with { RiotTurn = 0 } : fort;
    }

    /// <summary>Деревня после роста: уровень на единицу выше, <c>GROWTH_T</c> — текущий ход. Казну
    /// двигает вызывающий (<see cref="Treasury.Pay"/>) — здесь только состояние объекта.</summary>
    /// <exception cref="ArgumentException">Объект не деревня (у столицы уровня нет).</exception>
    public static MapSite Grow(MapSite village, int turn)
    {
        ArgumentNullException.ThrowIfNull(village);
        if (village.Size is not { } size)
            throw new ArgumentException($"у объекта {village.Id} ({village.Kind}) нет SIZE — расти нечему",
                                        nameof(village));
        return village with { Size = Math.Min(size + 1, MaxSize), GrowthTurn = turn };
    }

    /// <summary>Защита укрепления для брони юнита внутри: у столицы <c>Grace.PROTECTION</c> расы
    /// владельца, у деревни <c>GcityInf.PROTECTION</c> её уровня плюс <c>PROTECT_B</c> записи
    /// <c>.sg</c>. Действует на всех внутри, в том числе на захватчика сразу после входа
    /// (план Ф7 §1.4).</summary>
    /// <param name="protectBonusId"><c>PROTECT_B</c> деревни — id модификатора; пусто — нет прибавки.
    /// Во всех 370 сохранениях корпуса поле пустое (<c>G000000000</c>), поэтому сама прибавка
    /// живьём не мерилась — ранг 2 на единицу (задача 2, <c>task2-state.md</c>).</param>
    /// <exception cref="ArgumentException">Нет строки <c>GcityInf</c> расы и размера, расы в
    /// <c>Grace</c> или модификатора в <c>Gmodif</c>.</exception>
    public static int Protection(EconomyRules rules, IReadOnlyDictionary<string, Modifier> modifiers,
                                 bool capital, int size, string protectBonusId, string raceId)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (capital)
        {
            if (!rules.Races.TryGetValue(raceId, out var race))
                throw new ArgumentException($"расы {raceId} нет в Grace.dbf", nameof(raceId));
            return ArmorOf(modifiers, race.ProtectionId);
        }
        int clamped = Math.Clamp(size, 1, MaxSize);
        var city = rules.Cities.FirstOrDefault(c =>
                       c.RaceId.Equals(raceId, StringComparison.OrdinalIgnoreCase) && c.Size == clamped)
                   ?? throw new ArgumentException($"в GcityInf нет строки расы {raceId} размера {clamped}",
                                                  nameof(raceId));
        return ArmorOf(modifiers, city.ProtectionId) + ArmorOf(modifiers, protectBonusId);
    }

    /// <inheritdoc cref="Protection(EconomyRules, IReadOnlyDictionary{string, Modifier}, bool, int, string, string)"/>
    /// <param name="fort">Укрепление из <c>.sg</c>.</param>
    /// <param name="raceId">Раса ВЛАДЕЛЬЦА (<c>MapPlayer.RaceId</c>), а не подраса записи.</param>
    public static int Protection(EconomyRules rules, IReadOnlyDictionary<string, Modifier> modifiers,
                                 MapSite fort, string raceId)
    {
        ArgumentNullException.ThrowIfNull(fort);
        return Protection(rules, modifiers, fort.Kind == SiteKind.Capital, fort.Size ?? 1,
                          fort.ProtectBonus, raceId);
    }

    /// <summary>Броня модификатора: сумма элементов <see cref="ModifierElementType.Armor"/>
    /// (<c>GmodifL TYPE 5</c>, колонка <c>NUMBER</c>). Пустой id — нет модификатора, ноль;
    /// незнакомый — исключение, а не тихий ноль: молчаливая защита 0 у города прошла бы сверку
    /// там, где её нет.</summary>
    private static int ArmorOf(IReadOnlyDictionary<string, Modifier> modifiers, string id)
    {
        if (id.Length == 0) return 0;
        ArgumentNullException.ThrowIfNull(modifiers);
        if (!modifiers.TryGetValue(id, out var modifier))
            throw new ArgumentException($"модификатора {id} нет в Gmodif.dbf", nameof(id));
        return modifier.Elements.Where(e => e.Type == ModifierElementType.Armor).Sum(e => e.Number ?? 0);
    }

    /// <summary>
    /// Деревня после захвата: <c>OWNER</c> и <c>SUBRACE</c> — от вошедшего отряда, гарнизон стёрт
    /// (павшие удаляются из <c>.sg</c>), отряд входит сам и становится визитёром (<c>City.STACK</c>).
    /// Ранг 1 — план Ф7 §1.4, cities §1.4; сверяет харнесс <c>capture_village</c>. Раскладка гарнизона стирается вместе с ним: подкуп
    /// берёт деревню с живым гарнизоном, и места прежних юнитов оставались (партия 15.10, «нарушение slots» у деревни подкупа FT0009).
    /// </summary>
    public static MapSite Capture(MapSite fort, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(fort);
        ArgumentNullException.ThrowIfNull(stack);
        return fort with { Owner = stack.Owner, Subrace = stack.Subrace, Guards = [], GuardSlots = [], StackId = stack.Id };
    }

    /// <summary>Захватывает ли город этот лидер: призванный (<see cref="SummonCategory"/>), иллюзия (<see cref="IllusionCategory"/> —
    /// тот же <c>IUsSummon</c> для <c>CEffectCapture</c> <c>0x433e2e</c> → <c>0x431d8c</c>; И47, ранг 1 по листингу) и вор
    /// (<see cref="ThiefLeaderCategory"/>) — нет (план Ф7 §1.4, cities §6 п. 31).
    /// Призванный — <b>ранг 1 живьём</b> (<c>20260917-synth</c> q22b): отряд призванного KC0089 зашёл в город,
    /// <c>fort_capture</c> seq 8597564 оставил владельца PL0000, свиток «Призванные существа и воры не могут
    /// захватывать города.». Вор — ранг 2 (тот же свиток, живого захода вора не было).
    /// Это про <b>заход</b>: подкуп захватывает город вором без проверки лидера (<see cref="Thieves.Bribe"/>).</summary>
    public static bool CanCapture(int unitCategory, int leaderCategory) =>
        !D2.Rules.Battle.UnitCategory.IsSummonImpl(unitCategory) && leaderCategory != ThiefLeaderCategory;

    /// <summary>
    /// Сколько юнитов влезает в гарнизон деревни. <b>Ранг 3 — открытый вопрос плана §3 п. 1:</b>
    /// окно города показывало вместимость, равную уровню (1 / 3 / 4 наблюдались), а вызов
    /// <c>getMaxUnitsInGarrison</c> отдаёт 5/5/5/6/6 (поле <c>capacity</c> события <c>fort_day</c>),
    /// и какое из двух чисел ограничивает, не проверено. Здесь взято меньшее — уровень: оно не
    /// противоречит ни одному сохранению корпуса (харнесс <c>garrison_capacity</c>), тогда как
    /// 5/5/5/6/6 корпус не проверяет вовсе. Снимет второй юнит в гарнизоне деревни 1-го уровня.
    /// <para><b>Подтверждено 18.09.2026 корпусом 20260918-probe65</b> (<c>AiStrategicHarnessTests.UnitPick…</c>): свободные
    /// места найма ИИ (<c>0x5f4485</c>) сходятся с «уровень деревни минус занятые клетки» на всех 116 выборках, а с
    /// <c>capacity</c> события <c>fort_day</c> (5/5/5/6/6) — нет. У столицы предел — <c>capacity</c> (6).</para>
    /// </summary>
    public static int GarrisonCapacity(int size) => Math.Clamp(size, 1, MaxSize);
}
