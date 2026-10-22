using D2.Rules.Battle;

namespace D2.Rules.Map;

/// <summary>Кандидат охраны (<c>CAiGuard</c> <c>0x457317</c>) — другой отряд карты.</summary>
/// <param name="SameLeaderCategory">Одинаковый вид расы юнита-лидера по <c>0x5edb1a</c> (раса из <c>IUsSoldier</c>,
/// поле <c>+8</c> её записи — <c>Grace.RACE_TYPE</c>, ранг 2; <c>AiTactics.Guard</c>).</param>
public sealed record AiGuardCandidate(StackOrder EffectiveOrder, int Distance, bool SameSubrace, bool Inside,
                                      bool OnWater, bool Invisible, bool Allied, bool SameLeaderCategory);

/// <summary>Шаг к своей крепости: оценщики <c>DefendCity</c> <c>0x4548b1</c>, <c>HireUnit</c> <c>0x45a11e</c>,
/// <c>HealRevive</c> <c>0x4578d8</c> — один и тот же выбор действия, счёт у всех один.</summary>
public enum AiFortStep { Stay, KickOut, Enter, Move }

/// <summary>Шаг <c>SecureCity</c> (<c>0x46304d</c>).</summary>
public enum AiSecureStep { None, Stay, Path, AttackCity, Enter, KickOut }

/// <summary>Факты лечения в крепости (<c>0x457afa</c>).</summary>
/// <param name="Healthy">Обе проверки дали истину (ранг 1): <c>0x457d39</c> — у каждого юнита группы здоровье <c>hp·100/max</c> не ниже
/// <c>GAI.HP_HEALMIN</c> (поле <c>+0xcc</c> строки, <c>0x58f7c5</c>), <c>0x457e58</c> — у группы в целом (суммы) тоже; иначе счёт ×2.
/// До 25.09 читалось как «казны хватает» (ранг 3), и порт всегда давал истину.</param>
/// <param name="HasDead"><c>0x457cbf</c>: в группе есть мёртвый (ранг 2).</param>
public sealed record AiHealFacts(bool SameSubrace, int StatusToday, bool OccupiedByOther, bool Healthy,
                                 double Distance, bool HasDead, bool LeaderAlive, bool CapturedByOther);

/// <summary>Факты знатного против отряда (<c>0x45de91</c>). <paramref name="HasSpyRiotSteal"/> — в списке
/// доступных действий (<c>0x5d32b8</c>) есть <c>L_SPY</c>, <c>L_RIOT_CITY</c> или <c>L_STEAL_ITEM</c>;
/// <paramref name="OnlyDuelWithRod"/> — у лидера цели способность <c>L_ROD</c>, а список — один <c>L_DUEL</c>.</summary>
public sealed record AiNobleStackFacts(bool Invisible, bool Inside, bool Neutral, bool IsMoveTarget,
                                       bool SummonLeader, bool Allied, bool Human, bool HasSpyRiotSteal,
                                       bool OnlyDuelWithRod, int Influence, double Distance, int StatusToday,
                                       int StatusYesterday);

/// <summary>Кристалл в переборе <c>0x461347</c>: чужие жезлы в восьми соседях (<c>0x461525</c>, порядок таблицы
/// <c>0x6d1660</c>, затем порядок объектов клетки), клетка жезла (<see cref="AiTacticScore.RodCell"/>) и
/// Чебышёв от отряда до неё.</summary>
public sealed record AiCrystal(IReadOnlyList<string> ForeignRods, (int X, int Y)? Cell, int CellDistance);

/// <summary>Кандидат перебора кристаллов: жезл для атаки или кристалл для постановки (индекс во входе).</summary>
public readonly record struct AiRodCandidate(AiActionType Type, string Rod, int Crystal)
{
    public override string ToString() => Type == AiActionType.AttackRod ? $"attack:{Rod}" : $"plant:{Crystal}";
}

/// <summary>
/// Условия выдачи и счёт оценщиков тактики ИИ (слот 2 списка <c>CAi*::build</c> — кандидаты, слот 4 оценщика —
/// действие и счёт). Ранг 1 — по листингам GOG 3.01, если в описании не сказано иное. <c>d</c> — Чебышёв (слот 12
/// обёртки = <c>fild</c> слота 13; у отряда В КРЕПОСТИ слот 3 <c>0x5ed9b2</c> отдаёт не клетку отряда, а угол
/// крепости <c>0x5e94eb</c>), <c>4g</c> — <see cref="AiTacticScore.Influence"/>. Статусы — память
/// <see cref="AiTacticMemo"/> на сегодня и вчера. Внешние проверки (путь, казна, списки) приходят готовыми
/// булевыми — их смысл описан у параметра.
/// <para>Проверок «применять только к ненулевому счёту» здесь нет: движок сравнивает счёт не с нулём, а с флагом
/// <see cref="AiTacticScore.NoScore"/> (<c>fcomp [0x835a28]</c>), поэтому шаги счёта идут всегда — разница видна
/// при нулевом весе <c>GAI</c> (строки 4–6). «Нет кандидата» — это <see cref="AiTactic.NoScore"/>: настоящий 0
/// движок вставляет как обычный счёт.</para>
/// </summary>
public static class AiTacticEval
{
    /// <summary>«Рядом» (<c>0x6d2150</c>, <c>0x462ddb</c>): 7 клеток — у бегства и расстояния счёта.</summary>
    public const double Near = 7.0;
    private const double DistanceWeight = 3.0;
    private const double Captured = 0.1;
    private const double Progress = 1.25;

    // ------------------------------------------------------------------ постоянные действия

    /// <summary>Порог силы бегства (<c>0x6d1818</c>). Ранг 1.</summary>
    public const double RetreatStrength = 0.8;

    /// <summary>
    /// <c>Retreat</c> (<c>0x462cda</c>, счёт 2000 <c>0x6d1820</c>): отряд не в крепости; перебор списка отрядов
    /// тактики (<c>CAiTactic +0x51f8</c>): первый отряд ЧЕЛОВЕКА (<c>CMidPlayer +0x88</c>) ближе 7 клеток, при
    /// расстоянии от нас до точки бегства ≥ 7 и силе его атаки на нас (<c>0x5dda98</c>, <see
    /// cref="AiTacticScore.Strength"/>) ≥ 0,8 — даёт одно действие. Точка бегства — угол столицы хозяина
    /// (<c>0x5e9450</c> + <c>0x5e9563</c>).
    /// </summary>
    public static bool RetreatBuilt(bool inside, int homeDistance,
                                    IEnumerable<(bool Human, int Distance, double Strength)> stacks)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        return !inside && homeDistance >= Near
               && stacks.Any(s => s.Human && s.Distance < Near && s.Strength >= RetreatStrength);
    }

    /// <summary><c>Guard</c> (<c>0x457317</c>, счёт 150): кандидат — любой отряд карты с действующим приказом не
    /// <c>L_STAND</c>, не дальше <c>GU_RANGE / 2</c> (<c>0x5879fd</c>, <c>GVars +0x120</c>; деление к нулю),
    /// другой подрасы, не в крепости, на воде для водного, видимый, не союзник (<c>0x5f7ebd</c>) и с другой
    /// категорией лидера.</summary>
    public static bool GuardAdmits(AiGuardCandidate c, bool selfWaterOnly, int guardRange)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.EffectiveOrder != StackOrder.Stand && c.Distance <= guardRange / 2 && !c.SameSubrace
               && !c.Inside && (!selfWaterOnly || c.OnWater) && !c.Invisible && !c.Allied
               && !c.SameLeaderCategory;
    }

    /// <summary>Своя крепость для <c>DefendCity</c> (<c>0x4546e4</c>), <c>HealRevive</c> (<c>0x4576dd</c>):
    /// подраса крепости (<c>+0x88</c>) — наша (у найма <c>0x459f0d</c> — хозяин <c>+0x84</c>), не деревня с
    /// бунтом (<c>0x5e9582</c>), не столица с захваченным хозяином.</summary>
    public static bool OwnFortAdmits(bool sameSide, bool riot, bool capitalOfCapturedOwner) =>
        sameSide && !riot && !capitalOfCapturedOwner;

    /// <summary>Счёт <c>DefendCity</c> (<c>0x454afd</c>): 225, если в крепости стоит сам отряд (<c>+0x8c</c>),
    /// иначе 150.</summary>
    public static double DefendCityScore(bool selfIsVisitor) => selfIsVisitor ? 225 : 150;

    /// <summary>Действие к своей крепости: отряд уже в ней — остаться; рядом и занята другим — выгнать
    /// (<c>CAiKickOutStackAction</c>); рядом и пусто — войти; не рядом — идти.</summary>
    public static AiFortStep FortStep(bool selfIsVisitor, bool adjacent, bool occupied) =>
        selfIsVisitor ? AiFortStep.Stay
        : !adjacent ? AiFortStep.Move
        : occupied ? AiFortStep.KickOut
        : AiFortStep.Enter;

    /// <summary>Кольцо клеток <c>AssistMove</c> — таблица <c>0x6cfc10</c> (пары dx, dy).</summary>
    public static readonly IReadOnlyList<(int Dx, int Dy)> AssistRing =
        [(0, -2), (0, 2), (-2, 0), (2, 0), (-2, -2), (2, 2), (2, -2), (-2, 2)];

    /// <summary>
    /// <c>AssistMove</c> (<c>0x45011b</c>, счёт 150): цель — отряд цели действующего приказа. Из кольца
    /// <see cref="AssistRing"/> достижимых клеток (<c>CMidPathFinder</c>) берётся ближайшая к нам (строго меньше,
    /// начало 999). Лучшая — наша клетка: ничего. Нашлась — идти к ней (<c>Direct</c> = ложь, режим 0). Не нашлась —
    /// идти к самой цели (режим 2), но только если до неё больше двух.
    /// </summary>
    public static (int X, int Y, bool Direct)? AssistMoveTarget(int tx, int ty, int sx, int sy,
                                                                Func<int, int, bool> reachable)
    {
        ArgumentNullException.ThrowIfNull(reachable);
        (int X, int Y) best = (tx, ty);
        int bestD = 999;   // 0x3e7
        foreach (var (dx, dy) in AssistRing)
        {
            int x = tx + dx, y = ty + dy;
            if (!reachable(x, y)) continue;
            int d = Cheb(x - sx, y - sy);
            if (d < bestD) (best, bestD) = ((x, y), d);
        }
        if (best == (sx, sy)) return null;
        if (best != (tx, ty)) return (best.X, best.Y, false);
        return Cheb(tx - sx, ty - sy) > 2 ? (tx, ty, true) : null;
    }

    /// <summary>Счёт смены приказа <c>CAiChangeOrderEval</c> (слот 4 <c>0x44fcb5</c>, константа <c>0x6cfb70</c> = 1000):
    /// оценщик ставят построители <c>AttackRod</c> (<c>0x450dfb</c>), <c>DefendParty</c> (<c>0x45589c</c>) и
    /// <c>NobleStack</c> (<c>0x45db6b</c>), когда приказ отряда потерял смысл; действие — <c>CAiChangeOrderAction</c>
    /// без точки (слот 1 зонд читает как 0x1b). Ранг 1; корпус 6.5 — две вставки с <c>ret_addr 0x44ffe0</c>.</summary>
    public const double ChangeOrderScore = 1000;

    /// <summary><c>GiveItems</c> (<c>0x456fea</c>, счёт 500): действующий приказ <c>L_ASSIST</c>, в отряде есть
    /// предмет, цель приказа задана.</summary>
    public static bool GiveItemsBuilt(StackOrder effectiveOrder, int itemCount, bool hasTarget) =>
        effectiveOrder == StackOrder.Assist && itemCount >= 1 && hasTarget;

    /// <summary>
    /// <c>EnterCityRoam</c> (построитель <c>0x455cdb</c>, ранг 1 по листингу): хозяин отряда не захвачен, отряд не в
    /// крепости (<c>0x455cf4</c>: <c>+0x94</c>), не водный (слот <c>0x14</c> обёртки, <c>0x455d16</c>), лидер не призыв и не иллюзия (<c>0x455ec0</c>:
    /// <c>IUsSummon</c>/<c>IUsIllusion</c>); крепость — не деревня с бунтом (<c>0x5e9582</c>: вид деревни и
    /// <c>+0x98</c> ≠ 0), нашей подрасы (<c>+0x88</c> = <c>+0x90</c> хозяина), пустая (<c>+0x8c</c> пуст), не
    /// столица с захваченным хозяином. Точка — угол крепости.
    /// <para>Обе проверки есть (28.09, листинг <c>0x455cf1</c>…<c>0x455d1b</c>): «внутри» — <c>+0x94</c> отряда через слот 8 обёртки,
    /// «только по воде» — слот <c>0x14</c>; прежний комментарий («проверки внутри нет») ошибался.</para>
    /// </summary>
    public static bool EnterCityRoamAdmits(bool ownerCaptured, bool waterOnly, bool summonOrIllusionLeader,
                                           bool riot, bool sameSubrace, bool occupied, bool capitalOfCapturedOwner, bool inside = false) =>
        !ownerCaptured && !inside && !waterOnly && !summonOrIllusionLeader && !riot && sameSubrace && !occupied
        && !capitalOfCapturedOwner;

    /// <summary>Счёт <c>EnterCityRoam</c> (<c>0x455f61</c>, ранг 1): 250 (<c>0x6d0918</c>) минус расстояние до
    /// крепости (<c>fsub st(1)</c> у <c>0x455f90</c>), а НЕ постоянные 250 первого переноса.</summary>
    public static double EnterCityRoam(double distance) => EnterCityRoamBase - distance;

    /// <summary>База счёта <c>EnterCityRoam</c> — <c>0x6d0918</c>. Ранг 1.</summary>
    public const double EnterCityRoamBase = 250.0;

    /// <summary><c>MoveToStack</c> (<c>0x45b610</c>, счёт 150): цель — отряд из плана тактики
    /// (<c>CAiTactic +0x5158</c> → <c>0x473049</c>; план у движка всегда пуст — построитель мёртв, 28.09); можно атаковать (<c>0x5deb24</c>), водному — на воде, сила
    /// ≥ 0,7 (<c>0x6d1298</c>), не союзник. Дальше одной клетки — идти, иначе действие у цели.</summary>
    public static bool MoveToStackAdmits(bool hasTarget, bool canAttack, bool selfWaterOnly, bool onWater,
                                         double strength, bool allied) =>
        hasTarget && canAttack && (!selfWaterOnly || onWater) && strength >= 0.7 && !allied;

    /// <summary>
    /// <c>MoveToLocation</c> (<c>0x45afe8</c>/<c>0x45b1fe</c>, 150): локация — цель действующего приказа, водному
    /// — на воде; на месте — действие локации, иначе путь (<c>0x460d27</c>; нет пути — нет вставки).
    /// <c>DefendLocation</c> (<c>0x454c78</c>/<c>0x45511b</c>, 150): враги в радиусе локации (<c>+0x28</c>) —
    /// см. <see cref="DefendEnemyAdmits"/>; первый, до кого есть путь (<c>0x5ccf7a</c>), — цель; иначе локация,
    /// если мы не на ней. <c>DefendParty</c> (<c>0x45541d</c>, 150): центр — охраняемый отряд, радиус
    /// <c>PA_RANGE / 2</c> (<c>GVars +0x118</c>); охраняемый добавляется последним и берётся, только если до него
    /// больше двух; цель — первый с путём длиннее одного шага; охраняемого нет или подраса сменилась —
    /// <c>CAiChangeOrderEval</c>.
    /// </summary>
    public static bool DefendEnemyAdmits(bool isSelf, bool inside, int distance, int radius, bool selfWaterOnly,
                                         bool onWater, bool invisible, bool canAttack, bool allied) =>
        !isSelf && !inside && distance <= radius && (!selfWaterOnly || onWater) && !invisible && canAttack
        && !allied;

    /// <summary><c>SecureCity</c> (<c>0x462edf</c>/<c>0x46304d</c>, 150): цель приказа — крепость, не столица с
    /// захваченным хозяином и не деревня с бунтом. Шаг: мы внутри — остаться; не рядом — путь; чужая — атака,
    /// кроме призыва у пустой крепости без гарнизона (<c>0x5f509a</c> = 0); своя пустая — войти; своя занятая —
    /// выгнать, если урон постояльца (<see cref="DamageRatio"/>) меньше 0,1.</summary>
    public static AiSecureStep SecureCityStep(bool selfInside, bool adjacent, bool ownSubrace, bool occupied,
                                              bool summonLeader, int garrisonCount, double occupantDamage) =>
        selfInside ? AiSecureStep.Stay
        : !adjacent ? AiSecureStep.Path
        : !ownSubrace ? (summonLeader && !occupied && garrisonCount == 0 ? AiSecureStep.None : AiSecureStep.AttackCity)
        : !occupied ? AiSecureStep.Enter
        : occupantDamage < 0.1 ? AiSecureStep.KickOut
        : AiSecureStep.None;

    // ------------------------------------------------------------------ шаблонные оценщики

    /// <summary>
    /// <c>TakeBag</c>/<c>TakeBagRoam</c> (<c>0x46951a</c>): <c>S = I_BAGS + 4g</c>; ближе 7 — ещё <c>+I_BAGS</c>
    /// (без <c>−3d</c>); иначе <c>−3d</c>, ×0,75 и ×0,25 у лидера со способностью <c>L_ROD</c>; затем
    /// <c>max(S, 5)</c> (пол есть и у нулевого веса) и ×0,1 за захват. Статусов в счёте нет.
    /// Корпус 6.5: 2085 из 2085 вставок сошлись (<c>AiTacticEvalHarnessTests</c>).
    /// </summary>
    public static double TakeBag(AiWeights w, int influence, double distance, bool rodLeader, bool capturedByOther)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = w.Bags + influence;
        if (distance < Near)
        {
            s += w.Bags;
        }
        else
        {
            s -= DistanceWeight * distance;
            s *= 0.75;
            if (rodLeader) s *= 0.25;
        }
        s = 5.0 < s ? s : 5.0;   // 0x46966e: fld 5; fcomp s
        return capturedByOther ? s * Captured : s;
    }

    /// <summary><c>TakeBagRoam</c> (<c>0x4698cb</c>, ранг 1): мешок не дальше 3 по Чебышёву. У обычного мешка
    /// фильтра нет (<c>0x496d3d</c> — <c>mov al, 1</c>). Общий построитель <c>0x469418</c> сперва бросает весь
    /// список, когда хозяин захвачен (слот 6, <c>0x4676e1</c>); бродяга <c>0x469889</c> до него проверяет, что
    /// отряд не в крепости (<c>CMidStack +0x94</c> пуст). Прежняя запись (наоборот) была ошибкой прочтения
    /// слотов — разбор 23.09, <c>docs/ai-tactic-candidates.md</c> §7.</summary>
    public static bool TakeBagRoamAdmits(int distance) => distance <= 3;

    /// <summary><c>VisitRuin</c> (<c>0x46c02a</c>): сила против охраны руин <c>p &lt; 0,7</c> — 0; шаблон
    /// <c>I_RUINS</c>, ×p, ×0,1 захват, вчера статус 5 — ×1,25. Построитель <c>0x46bdb6</c>: хозяин не захвачен,
    /// лидер категории юнита <c>L_LEADER</c> (<c>0x838188</c>), группа может драться (<c>0x5ddf35</c>: есть живой юнит,
    /// годный по <c>0x5ddf70</c>); порт — <c>AiTactics.VisitRuins</c>.</summary>
    public static double VisitRuin(AiWeights w, double strength, int influence, double distance,
                                   bool capturedByOther, int statusYesterday)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (strength < 0.7) return AiTactic.NoScore;
        double s = AiTacticScore.Generic(w.Ruins, influence, distance) * strength;
        return Tail(s, capturedByOther, statusYesterday);
    }

    /// <summary><c>VisitMage</c> (<c>0x46a31d</c>): число заклинаний игрока равно среднему по незахваченным
    /// (<c>0x46a6c6</c> = 0) — 0; статус сегодня 3 или 1 — 0; купить нечего (<c>0x46a589</c>) — 0; шаблон
    /// <c>I_TOWERS</c>, ×0,1, вчера 5 — ×1,25. Рядом — нужен остаток хода ≥ 1.</summary>
    public static double VisitMage(AiWeights w, int influence, double distance, bool capturedByOther,
                                   int statusToday, int statusYesterday, bool canBuy, bool spellCountOffAverage)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (!spellCountOffAverage || statusToday is AiTactic.StatusFailed or 1 || !canBuy) return AiTactic.NoScore;
        return Tail(AiTacticScore.Generic(w.Towers, influence, distance), capturedByOther, statusYesterday);
    }

    /// <summary><c>VisitMerchant</c> (<c>0x46b218</c>): байт <c>+0x88</c> площадки — 0 (<c>MISSION</c> торговца, чтение <c>0x601542</c>); купить
    /// нечего (<c>0x46b428</c>) — 0; шаблон <c>I_MERCHANT</c>, ×(0,1·n + 1), где n — продаваемое (<c>0x46b0e0</c>);
    /// статус сегодня 3, 4, 1 — 0; вчера 5 — ×1,25. Захвата в счёте нет.</summary>
    public static double VisitMerchant(AiWeights w, int influence, double distance, bool siteFlag88, bool canBuy,
                                       int sellable, int statusToday, int statusYesterday)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (siteFlag88 || !canBuy) return AiTactic.NoScore;
        double s = AiTacticScore.Generic(w.Merchant, influence, distance);
        s *= sellable * 0.1 - -1.0;   // 0x46b320: fmul 0.1; fsub −1
        if (statusToday is AiTactic.StatusFailed or AiTactic.StatusHalf or 1) return AiTactic.NoScore;
        return Tail(s, false, statusYesterday);
    }

    /// <summary><c>VisitTrainer</c> (<c>0x46d09f</c>): шаблон <c>I_TRAINER</c>, ×0,1, ×2 при
    /// <c>suggestedLevel ≥ 10</c> (<c>0x61eb31</c>, <c>CScenarioInfo +0x70</c>, ранг 2), статус сегодня 3 — 0.
    /// Построитель <c>0x46ccef</c> при уровне ≥ 10 требует 500 золота в казне или <c>0x4692bf</c>.</summary>
    public static double VisitTrainer(AiWeights w, int influence, double distance, bool capturedByOther,
                                      bool suggestedLevel10, int statusToday)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = AiTacticScore.Generic(w.Trainer, influence, distance);
        if (capturedByOther) s *= Captured;
        if (suggestedLevel10) s += s;
        return statusToday == AiTactic.StatusFailed ? AiTactic.NoScore : s;
    }

    /// <summary>
    /// <c>VisitTemple</c> (<c>0x46c5c5</c>, кандидат при уроне ≥ 0,3): лечить нельзя (<c>0x46c755</c>) — 0;
    /// <c>S = I_HEAL</c>, урон &gt; 0,9 — ×2, иначе ×урон; <c>−2d</c> (не 3!); код <c>0x5de06e</c> = 6 — ×1,1
    /// (ранг 3); статус сегодня 3 — 0. Влияния и захвата нет.
    /// </summary>
    public static double VisitTemple(AiWeights w, double damage, double distance, bool canHeal, bool code6,
                                     int statusToday)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (!canHeal) return AiTactic.NoScore;
        double s = w.Heal;
        s = damage > 0.9 ? s + s : s * damage;
        s -= distance + distance;
        if (code6) s *= 1.1;
        return statusToday == AiTactic.StatusFailed ? AiTactic.NoScore : s;
    }

    /// <summary>Урон отряда (<c>0x468969</c>): максимум <c>1 − hp/maxHp</c> по юнитам группы; при признаке
    /// <c>0x468a4c</c> (ранг 3) и уроне больше 0,5 — 0,99. Пустая группа — 0.</summary>
    public static double DamageRatio(IEnumerable<(int Hp, int MaxHp, bool Flag)> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        double best = 0;
        foreach (var (hp, max, flag) in units)
        {
            double r = 1.0 - (double)hp / max;
            if (flag && r > 0.5) r = 0.99;
            if (best < r) best = r;
        }
        return best;
    }

    /// <summary><c>HireUnit</c> (<c>0x45a34c</c>): <c>S = I_H_UNITS</c>; крепость занята кем угодно (<c>+0x8c</c>,
    /// и собой тоже) — ×0,5; ×(свободных мест + 1); <c>−3d</c> до позиции крепости (<c>0x5e94eb</c>); нанять
    /// некого (<c>0x45a498</c>) — 0; ×0,1 захват. Влияния и статусов нет. Построитель <c>0x459f0d</c>: хозяин не
    /// захвачен, есть место, отряд не в крепости; крепости своего игрока.</summary>
    public static double HireUnit(AiWeights w, bool occupied, int free, double distance, bool canHire,
                                  bool capturedByOther)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = w.HireUnits;
        if (occupied) s *= 0.5;
        s *= free + 1;
        s -= DistanceWeight * distance;
        if (!canHire) return AiTactic.NoScore;
        return capturedByOther ? s * Captured : s;
    }

    /// <summary><c>HireMercs</c> (<c>0x4592cc</c>): <c>S = I_H_MERCS + 4g</c> (геттер <c>0x56ee29</c>,
    /// <c>GAI +0x88</c>); дальше 20 — 0; <c>−3d</c>; ×свободных мест; ×0,1 захват; при <c>suggestedLevel ≥ 10</c>
    /// — 0, если место есть, иначе ×2 (то есть всегда 0: буквально по листингу); статус сегодня 3 или 2 — 0.</summary>
    public static double HireMercs(AiWeights w, int influence, double distance, int free, bool capturedByOther,
                                   bool suggestedLevel10, int statusToday)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = w.HireMercs;
        s += influence;
        if (distance > 20) return AiTactic.NoScore;
        s -= DistanceWeight * distance;
        s *= free;
        if (capturedByOther) s *= Captured;
        if (suggestedLevel10)
        {
            if (free != 0) return AiTactic.NoScore;
            s += s;
        }
        return statusToday is AiTactic.StatusFailed or 2 ? AiTactic.NoScore : s;
    }

    /// <summary>
    /// <c>StayInside</c> (<c>0x464b8f</c>): <c>S = I_H_UNITS</c>; мест нет — <paramref name="hireGap"/>
    /// (<c>0x46886e</c>: <c>0x4688a0 − 0x46890b</c>, ранг 3) меньше 1 — нет кандидата, иначе <c>S + 0,5·h</c>;
    /// места есть — <c>S·(места + 1)</c>. <paramref name="notReady"/> — юнитов минус <c>0x5f50fd</c> (ранг 3):
    /// 0, или 1 при мёртвом лидере — ×2, иначе −1.
    /// </summary>
    public static double? StayInside(AiWeights w, int free, double hireGap, int notReady, bool leaderAlive)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = w.HireUnits;
        if (free == 0)
        {
            if (hireGap < 1) return null;
            s = hireGap * 0.5 + s;
        }
        else s *= free + 1;
        return notReady == 0 || notReady == 1 && !leaderAlive ? s + s : s - 1;
    }

    /// <summary><c>HealRevive</c> (<c>0x457afa</c>, кандидат при уроне ≥ 0,1): чужая подраса или статус сегодня 4
    /// — 0; <c>S = I_HEAL</c>; занята другим — ×0,9; кто-то ниже <c>HP_HEALMIN</c> — ×2; <c>−2d</c> до позиции крепости; есть
    /// мёртвые — ×2; лидер мёртв — ×2; ×0,1 захват.</summary>
    public static double HealRevive(AiWeights w, AiHealFacts f)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(f);
        if (!f.SameSubrace || f.StatusToday == AiTactic.StatusHalf) return AiTactic.NoScore;
        double s = w.Heal;
        if (f.OccupiedByOther) s *= 0.9;
        if (!f.Healthy) s += s;
        s -= f.Distance + f.Distance;
        if (f.HasDead) s += s;
        if (!f.LeaderAlive) s += s;
        return f.CapturedByOther ? s * Captured : s;
    }

    /// <summary><c>SummonAttack</c> (<c>0x46517a</c> + <c>0x465042</c>): союзник — 0; вес
    /// <c>I_STACKS_N·0,5</c>/<c>_P</c>/<c>_R</c> (без влияния), <c>−3d</c>; ближе 8 — <c>+(8 − d)·80</c>; ближе 4 —
    /// ×2. Кандидаты <c>0x464dcb</c>: отряды карты (водному — на воде), нейтралы — <see cref="SummonNeutral"/>,
    /// прочие — другой подрасы, видимые, другой подрасы типа лидера (<c>0x464eff</c> → <c>0x5edb9c</c>).</summary>
    public static double SummonAttack(AiWeights w, bool targetNeutral, bool targetHuman, bool allied, int distance)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (allied) return AiTactic.NoScore;
        double s = targetNeutral ? w.StacksN : targetHuman ? w.StacksP : w.StacksR;
        if (targetNeutral) s *= 0.5;
        s -= DistanceWeight * distance;
        if (distance < 8) s += (8 - distance) * 80;
        if (distance < 4) s += s;
        return s;
    }

    /// <summary><c>CAiSummonNeutralActionEval</c> (<c>0x465322</c>): база — 1 у НЕЙТРАЛЬНОЙ цели (<c>0x5e5358</c>)
    /// и 0 у прочих (<c>fldz</c> у <c>0x46535a</c>); ближе 8 — плюс <c>(8 − d)·80</c>, иначе
    /// <see cref="AiTactic.NoScore"/> (<c>0x465451</c>). Ранг 1.</summary>
    public static double SummonNeutral(bool targetNeutral, int distance) =>
        distance < 8 ? (targetNeutral ? 1 : 0) + (8 - distance) * 80 : AiTactic.NoScore;

    /// <summary><c>NobleCity</c> (<c>0x45cea1</c>): союзник или нейтрал — 0; в списке действий нет шпионажа, бунта
    /// или кражи предмета — 0; шаблон <c>I_N_CITIES</c> (в <see cref="AiWeights"/> — <c>NeutralCities</c>; это вес
    /// знатного, а не нейтральных городов) до позиции крепости; статус сегодня 3, 4, 1 — 0; вчера 5 — ×1,1.</summary>
    public static double NobleCity(AiWeights w, bool allied, bool ownerNeutral, bool hasSpyRiotSteal, int influence,
                                   double distance, int statusToday, int statusYesterday)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (allied || ownerNeutral || !hasSpyRiotSteal) return AiTactic.NoScore;
        return NobleTail(AiTacticScore.Generic(w.NeutralCities, influence, distance), statusToday, statusYesterday);
    }

    /// <summary><c>NobleStack</c> (<c>0x45de91</c>): невидимый, в крепости, нейтрал — 0; лидер цели призыв и цель не
    /// из плана (<c>0x473049</c>) — 0; союзник — 0; вес <c>I_STACKS_P</c>/<c>_R</c> ×1,5; нет нужных действий — 0;
    /// лидер цели с жезлом и единственная дуэль — 0; <c>+4g − 3d</c>; статусы как у города.</summary>
    public static double NobleStack(AiWeights w, AiNobleStackFacts f)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Invisible || f.Inside || f.Neutral || f.SummonLeader && !f.IsMoveTarget || f.Allied) return AiTactic.NoScore;
        double s = f.Human ? w.StacksP : w.StacksR;
        s *= 1.5;
        if (!f.HasSpyRiotSteal || f.OnlyDuelWithRod) return AiTactic.NoScore;
        s += f.Influence;
        s -= DistanceWeight * f.Distance;
        return NobleTail(s, f.StatusToday, f.StatusYesterday);
    }

    /// <summary><c>NobleMerchant</c> (<c>0x45d50c</c>), ветка передачи предметов (<c>[+0x14]</c> ≠ 0, <c>0x45d63e</c>):
    /// действие <c>CAiGiveItemsAction</c> (тип 0x22), шаблон <c>I_N_STACK</c> до своего отряда; статусы как у
    /// города. Корпус 6.5 — <c>ret_addr 0x45d637</c>, тип 0x22.</summary>
    public static double NobleMerchantGive(AiWeights w, int influence, double distance, int statusToday,
                                           int statusYesterday)
    {
        ArgumentNullException.ThrowIfNull(w);
        return NobleTail(AiTacticScore.Generic(w.NeutralStacks, influence, distance), statusToday, statusYesterday);
    }

    /// <summary><c>NobleMerchant</c>, ветка торговца (<c>0x45d7b9</c>, действие тип 0x21): <c>0x6012c5</c> = 0 или
    /// пустой список действий знатного у площадки (<c>0x5d32b8</c>) — 0; шаблон <c>I_N_CITIES</c> в точке площадки
    /// (<c>0x601181</c>); статусы как у города. Ранг 1 — ход листинга; смысл <c>0x6012c5</c> — ранг 3.</summary>
    public static double NobleMerchantTrade(AiWeights w, bool hasActions, int influence, double distance,
                                            int statusToday, int statusYesterday)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (!hasActions) return AiTactic.NoScore;
        return NobleTail(AiTacticScore.Generic(w.NeutralCities, influence, distance), statusToday, statusYesterday);
    }

    /// <summary><c>AttackRod</c> (<c>0x450f8b</c>): свой или союзный жезл — 0; шаблон <c>I_A_ROD</c> в точке жезла
    /// (<c>+0xc</c>), ×0,1 захват, статус сегодня 3 — 0.</summary>
    public static double AttackRod(AiWeights w, bool ownRod, bool allied, int influence, double distance,
                                   bool capturedByOther, int statusToday)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (ownRod || allied) return AiTactic.NoScore;
        if (w.AttackRod == 0) return AiTactic.NoScore;   // 0x450ffb: вес сравнивается с настоящим нулём 0x6cff88
        double s = AiTacticScore.Generic(w.AttackRod, influence, distance);
        if (capturedByOther) s *= Captured;
        return statusToday == AiTactic.StatusFailed ? AiTactic.NoScore : s;
    }

    /// <summary>
    /// Перебор кристаллов (<c>0x461347</c>): хозяин захвачен — ничего. По кристаллам списка: есть чужие жезлы
    /// рядом — при живом лидере <c>AttackRod</c> на каждый; нет — клетка жезла: есть — <c>PlantRod</c>, и если до
    /// неё не больше клетки — перебор кончается.
    /// </summary>
    public static IReadOnlyList<AiRodCandidate> RodPlan(bool ownerCaptured, bool leaderAlive,
                                                        IEnumerable<AiCrystal> crystals)
    {
        ArgumentNullException.ThrowIfNull(crystals);
        var r = new List<AiRodCandidate>();
        if (ownerCaptured) return r;
        int i = -1;
        foreach (var c in crystals)
        {
            i++;
            if (c.ForeignRods.Count > 0)
            {
                if (leaderAlive) r.AddRange(c.ForeignRods.Select(rod => new AiRodCandidate(AiActionType.AttackRod, rod, i)));
                continue;
            }
            if (c.Cell is null) continue;
            r.Add(new AiRodCandidate(AiActionType.PlantRod, "", i));
            if (c.CellDistance <= 1) break;
        }
        return r;
    }

    // ------------------------------------------------------------------ путь

    /// <summary>
    /// Проверка пути (слот 6 действия, <c>0x45ab0b</c>, ранг 1 по листингу; у атак <c>0x4506fb</c> — только без
    /// плана перед атакой, у выселения <c>0x45a7aa</c> — после <c>0x5e78a9</c>; «простые» действия
    /// <c>0x467ab3</c> счёт не трогают). Порядок ровно такой:
    /// <list type="number">
    /// <item>кэш: уже проверяли (<c>+0x51</c>) — вернуть счёт при <c>+0x52</c>, иначе
    /// <see cref="AiTactic.NoScore"/>;</item>
    /// <item><c>0x468f81(map, отряд, точка)</c>: лидер ходит только по воде (слот <c>0x70</c> реализации лидера)
    /// и земля точки не вода (<c>0x5e5c78</c> ≠ <c>0x837328</c>) — <see cref="AiTactic.NoScore"/>; у не-водного
    /// лидера проверка всегда истинна;</item>
    /// <item>маршрут уже есть (<c>+0x3c</c> непуст или <c>+0x34</c> ≠ −1) — счёт прежний, поиска нет;</item>
    /// <item>иначе поиск <c>0x46903e</c>; не нашёл — <see cref="AiTactic.NoScore"/>.</item>
    /// </list>
    /// </summary>
    public static double PathRescore(double score, bool waterOnlyLeader, bool targetOnWater, bool routeKnown,
                                     bool pathFound) =>
        waterOnlyLeader && !targetOnWater ? AiTactic.NoScore
        : routeKnown || pathFound ? score : AiTactic.NoScore;

    /// <summary>Тип id отряда (<c>cmp eax, 0x23</c> у <c>0x46909a</c>, <c>0x4690f4</c>). Ранг 1.</summary>
    public const int StackIdType = 0x23;

    /// <summary>
    /// Отряд-преграда (<c>0x46903e</c>, ранг 1): поиск пути не дошёл (<c>0x5ccf7a</c> = 0) — кто помешал
    /// (<c>0x5ccf88</c>), и если тип его id <see cref="StackIdType"/>, он попадает в <c>CAiBlockingStacks</c>
    /// (<c>0x472d74</c>). Когда аргумент <c>+0x18</c> равен нулю, движок повторяет поиск вторым набором флагов
    /// (<c>0x4690c3</c>) и записывает преграду ещё раз — список принимает повтор. Возвращает id преграды,
    /// которую надо записать, или пусто.
    /// </summary>
    public static string BlockingStack(bool pathFound, int blockerIdType, string blockerId)
    {
        ArgumentNullException.ThrowIfNull(blockerId);
        return !pathFound && blockerIdType == StackIdType ? blockerId : "";
    }

    /// <summary>Радиус <c>DefendParty</c> — <c>PA_RANGE</c> (<c>GVars +0x118</c>, геттер <c>0x5879eb</c>),
    /// делённый на два целочисленно со срезом к нулю (<c>cdq; sub eax, edx; sar eax, 1</c> у <c>0x455503</c>).
    /// Ранг 1. Радиус приказа <c>L_DEFEND_LOCATION</c> — соседнее поле <c>GVars +0x11c</c> (геттер
    /// <c>0x5879f4</c>), без деления: см. <see cref="AiTactic.Admits"/>.</summary>
    public static int DefendPartyRadius(int paRange) => paRange / 2;

    private static double Tail(double s, bool captured, int statusYesterday)
    {
        if (captured) s *= Captured;
        return statusYesterday == AiTactic.StatusProgress ? s * Progress : s;
    }

    private static double NobleTail(double s, int statusToday, int statusYesterday)
    {
        if (statusToday is AiTactic.StatusFailed or AiTactic.StatusHalf or 1) return AiTactic.NoScore;
        return statusYesterday == AiTactic.StatusProgress ? s * 1.1 : s;
    }

    private static int Cheb(int dx, int dy) => Math.Max(Math.Abs(dx), Math.Abs(dy));
}
