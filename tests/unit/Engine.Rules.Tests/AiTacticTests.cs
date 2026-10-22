using D2.Data;
using D2.Data.Map;
using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>Тактика ИИ на карте (<see cref="AiTactic"/>, <see cref="AiTacticScore"/>) на синтетике: фильтр приказа,
/// сетка влияния, формулы счёта, вставка и выбор, клетка жезла. Корпусная сверка клетки жезла —
/// <see cref="AiTacticHarnessTests"/>.</summary>
public class AiTacticTests
{
    private static readonly AiWeights W = new(0, CitiesN: 110, CitiesR: 110, CitiesP: 110, StacksN: 100,
        StacksR: 115, StacksP: 120, Bags: 95, Ruins: 100, Towers: 100, Merchant: 90, HireUnits: 120,
        HireMercs: 120, Heal: 130, PlantRod: 120, AttackRod: 105, NeutralCities: 100, NeutralStacks: 100,
        Trainer: 100);

    // ------------------------------------------------------------------ фильтр приказа

    [Theory]
    [InlineData(AiActionType.PlantRod, true)]
    [InlineData(AiActionType.TakeBag, true)]
    [InlineData(AiActionType.TakeBagRoam, true)]
    [InlineData(AiActionType.Retreat, true)]
    [InlineData(AiActionType.DefendCity, false)]
    [InlineData(AiActionType.SecureCity, false)]
    [InlineData(AiActionType.DefendLocation, false)]
    [InlineData(AiActionType.AttackStackBezerk, false)]
    [InlineData(AiActionType.AttackCityRoam, false)]
    public void Normal_AdmitsAllButOrderOnlyTypes(AiActionType type, bool admitted) =>
        Assert.Equal(admitted, AiTactic.Admits(StackOrder.Normal, type));

    [Fact] public void Stand_AdmitsNothing() =>
        Assert.DoesNotContain(Enum.GetValues<AiActionType>(), t => AiTactic.Admits(StackOrder.Stand, t, true));

    [Fact] public void TargetedOrders_NeedTheOrderTarget()
    {
        Assert.True(AiTactic.Admits(StackOrder.AttackStack, AiActionType.AttackParty, targetIsOrderTarget: true));
        Assert.False(AiTactic.Admits(StackOrder.AttackStack, AiActionType.AttackParty));
        Assert.False(AiTactic.Admits(StackOrder.AttackStack, AiActionType.AttackStack, true));
        Assert.True(AiTactic.Admits(StackOrder.Assist, AiActionType.GiveItems, true));
        Assert.False(AiTactic.Admits(StackOrder.Assist, AiActionType.AssistMove));
        Assert.True(AiTactic.Admits(StackOrder.MoveToLocation, AiActionType.MoveToLocation, true));
        Assert.True(AiTactic.Admits(StackOrder.SecureCity, AiActionType.SecureCity, true));
    }

    [Fact] public void DefendCity_AttacksOnlyWithinSeven()
    {
        Assert.True(AiTactic.Admits(StackOrder.DefendCity, AiActionType.DefendCity, true));
        Assert.False(AiTactic.Admits(StackOrder.DefendCity, AiActionType.DefendCity, false));
        Assert.True(AiTactic.Admits(StackOrder.DefendCity, AiActionType.AttackStack, orderPointDistance: 7));
        Assert.False(AiTactic.Admits(StackOrder.DefendCity, AiActionType.AttackStack, orderPointDistance: 8));
        Assert.True(AiTactic.Admits(StackOrder.DefendLocation, AiActionType.DefendLocation, false, 3, defendRadius: 3));
        Assert.False(AiTactic.Admits(StackOrder.DefendLocation, AiActionType.DefendLocation, false, 4, defendRadius: 3));
    }

    [Fact] public void Steal_Roam_Bezerk_Sets()
    {
        AiActionType[] steal = [AiActionType.NobleMerchant, AiActionType.TakeBag, AiActionType.GiveItems, AiActionType.Retreat];
        Assert.Equal(steal.Order(), Enum.GetValues<AiActionType>().Where(t => AiTactic.Admits(StackOrder.Steal, t)).Order());
        Assert.Equal(4, Enum.GetValues<AiActionType>().Count(t => AiTactic.Admits(StackOrder.Roam, t)));
        Assert.Equal(2, Enum.GetValues<AiActionType>().Count(t => AiTactic.Admits(StackOrder.Bezerk, t)));
        Assert.True(AiTactic.Admits(StackOrder.Guard, AiActionType.Guard));
        Assert.True(AiTactic.Admits(StackOrder.DefendStack, AiActionType.DefendParty));
    }

    // ------------------------------------------------------------------ сетка влияния

    [Fact] public void Grid_DecaysByTruncatedEuclid_UpperBoundExclusive()
    {
        var g = AiTacticScore.InfluenceGrid(144, [(20, 20, AiTacticScore.CapitalRadius)]);
        Assert.Equal(13, g[20, 20]);
        Assert.Equal(8, g[23, 24]);                          // √(9+16) = 5
        Assert.Equal(1, g[8, 20]);                           // dx = 12
        Assert.Equal(1, g[32, 20]);
        Assert.Equal(0, g[34, 20]);                          // вне коробки
        Assert.Equal(4, g[29, 21]);                          // √(81+1) = 9.05 → 9
        // Табличный корень движка (0x51f35e) занижает точные квадраты: √289 = 17, а он даёт 16.
        Assert.Equal(16, AiTacticScore.FastSqrt(289));
        Assert.Equal(15, AiTacticScore.FastSqrt(226));
        Assert.Equal(0, AiTacticScore.FastSqrt(0));
        // Он же без отсечения — эвристика A* пути (0x680a6b): целая часть совпадает, дробь — 7 бит мантиссы.
        foreach (int n in (int[])[1, 2, 25, 226, 289, 530, 4095, 9216])
            Assert.Equal(AiTacticScore.FastSqrt(n), (int)MathF.Floor(AiTacticScore.FastSqrtF(n)));
        Assert.Equal(16.875f, AiTacticScore.FastSqrtF(289));   // (128 + 7)·2⁴/128: грубее точного 17
        Assert.Equal(0f, AiTacticScore.FastSqrtF(0));
        Assert.Equal(52, AiTacticScore.Influence(g, 20, 20));
    }

    [Fact] public void Grid_SumsSourcesUpTo255_AndClipsToMap()
    {
        var g = AiTacticScore.InfluenceGrid(10, [(5, 5, 200), (5, 5, 100), (0, 0, 13)]);
        Assert.Equal(255, g[5, 5]);
        Assert.Equal(0, g[5, 10]);                           // за краем карты 10×10
        Assert.Equal(255, g[0, 0]);                          // 193 + 93 + 13 → потолок
        Assert.Equal(0, AiTacticScore.InfluenceGrid(10, [(0, 0, 3)])[5, 0]);
        Assert.Equal(10, g.Side);                            // сетка — по карте, не 144² движка
    }

    [Fact] public void Anchor_IsBottomRightCell_AndRadius()
    {
        Assert.Equal((14, 14), AiTacticScore.AnchorCell(10, 10, 5));
        Assert.Equal(13, AiTacticScore.FortRadius(capital: true, cityField90: 4));
        Assert.Equal(15, AiTacticScore.FortRadius(capital: false, cityField90: 2));
    }

    // ------------------------------------------------------------------ счёт

    /// <summary>Шаги шаблона идут всегда: движок сравнивает счёт не с нулём, а с флагом
    /// <see cref="AiTacticScore.NoScore"/> (<c>0x835a28</c>), и нулевой вес его не даёт.</summary>
    [Fact] public void Generic_AddsInfluenceAndDistanceEvenAtZeroWeight()
    {
        Assert.Equal(122.0, AiTacticScore.Generic(120, 8, 2));
        Assert.Equal(2.0, AiTacticScore.Generic(0, 8, 2));
        Assert.Equal(-6.0, AiTacticScore.Generic(-8, 8, 2));
    }

    [Fact] public void PlantRod_GoldCaptureAndStatus()
    {
        Assert.Equal(122.0, AiTacticScore.PlantRod(W, 8, 2, false, false, 0));
        Assert.Equal(152.5, AiTacticScore.PlantRod(W, 8, 2, true, false, 0));
        Assert.Equal(12.2, AiTacticScore.PlantRod(W, 8, 2, false, true, 0), 9);
        Assert.Equal(61.0, AiTacticScore.PlantRod(W, 8, 2, false, false, AiTactic.StatusHalf));
        Assert.Equal(152.5, AiTacticScore.PlantRod(W, 8, 2, false, false, AiTactic.StatusProgress));
        Assert.Equal(3.125, AiTacticScore.PlantRod(W with { PlantRod = 0 }, 8, 2, true, false, 5), 9);
    }

    [Theory]
    [InlineData(10, 10, 5, 1.0 / 3)]   // A ≥ B/2 → X = A + C
    [InlineData(2, 10, 5, 1.0)]        // X = max(A, C) = 5 → 1
    [InlineData(2, 10, 8, 0.625)]
    [InlineData(1, 100, 1, 1.0)]       // потолок
    public void Strength_HalfRatio(double a, double b, double c, double p) =>
        Assert.Equal(p, AiTacticScore.Strength(a, b, c, canDamage: true), 9);

    [Fact] public void Strength_ZeroWhenCannotDamage() => Assert.Equal(0.0, AiTacticScore.Strength(10, 10, 5, false));

    /// <summary>Сила против крепости (<c>0x5ddcb3</c>): <c>B / D · 0,5</c>, потолок 1; пустая защита — 1, 0/0 — NaN.</summary>
    [Fact]
    public void CityStrength_HalfRatioCappedAtOne()
    {
        Assert.Equal(0.25, AiTacticScore.CityStrength(50, 100), 12);
        Assert.Equal(1.0, AiTacticScore.CityStrength(300, 100));
        Assert.Equal(1.0, AiTacticScore.CityStrength(10, 0));
        Assert.True(double.IsNaN(AiTacticScore.CityStrength(0, 0)));
    }

    private static AttackStackFacts Facts(bool neutral = true, bool flag88 = false, double d = 2, double p = 0.8,
                                          bool near = true, int count = 0, int status = 0) =>
        new(status, neutral, flag88, TargetRaceNeutral: true, Influence: 0, Distance: d, Adjust: 1.0,
            Strength: p, NeutralNear: near, NeutralCount: count);

    [Fact] public void AttackStack_Neutral_DoublesClose_ScalesByStrength()
    {
        Assert.Equal((100 - 6) * 2 * 0.8, AiTacticScore.AttackStack(W, Facts()), 9);
        Assert.Equal((100 - 6) * 2 * 0.95 * 1.3, AiTacticScore.AttackStack(W, Facts(p: 0.95)), 9);
        Assert.Equal((100 - 30) * 0.95, AiTacticScore.AttackStack(W, Facts(d: 10, p: 0.95)), 9);  // d ≥ 10 — без ×1,3
        // p < 0,7 у нейтральной цели — ×0,01 (порог 0x45137e смотрит на нейтральность АТАКУЮЩЕГО).
        Assert.Equal((100 - 6) * 2 * 0.69 * 0.01, AiTacticScore.AttackStack(W, Facts(p: 0.69)), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackStack(W, Facts(p: 0.69) with { AttackerNeutral = true }));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackStack(W, Facts(status: AiTactic.StatusFailed)));
    }

    /// <summary>Вид записи 1 вне плана (<c>0x453cf7</c>) роняет весь счёт атаки в «нет счёта»: все шаги после
    /// поправок (сила, порог, хвост нейтрала) сравнивают счёт с флагом и пропускают его.</summary>
    [Fact] public void AttackStack_RecordKind1_OutsidePlan_DropsCandidate()
    {
        var mods = new AttackModifierFacts(TargetOwnerIsTacticEnemy: false, InOperationTargets: false,
            IsMoveToTarget: false, RecordKind1: true, TargetInside: false, TargetNeutral: false,
            CapturedByOther: false, StatusToday: 0, StatusYesterday: 0);
        double adjust = AiTacticScore.AttackModifiers(1, mods);
        Assert.Equal(AiTactic.NoScore, adjust);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackStack(W, Facts() with { Adjust = adjust }));
        Assert.Equal(AiTactic.NoScore,
                     AiTacticScore.AttackStack(W, Facts(neutral: false, d: 10, p: 0.4) with { Adjust = adjust }));
    }

    /// <summary>Множество +100: три ближайших по евклиду каждого вида (<c>0x45f793(3, …)</c>,
    /// <c>0x45eadf</c>).</summary>
    [Fact] public void NearestTargets_KeepsThreeClosest()
    {
        Assert.Equal(3, AiTacticScore.TargetSetPerKind);
        Assert.Equal(5.0, AiTacticScore.SetDistance(3, 4), 9);
        Assert.Equal(["A", "C", "D"], AiTacticScore.NearestTargets(
            [("B", 9.0), ("A", 1.0), ("E", 12.0), ("C", 2.5), ("D", 4.0)]));
        // Крепость — цель приказа идёт с ключом 0 и всегда первая.
        Assert.Equal(["F", "A"], AiTacticScore.NearestTargets([("A", 1.0), ("F", 0.0)]).Take(2));
    }

    /// <summary>Отбор трёх (<c>0x45f793</c>): цели множества целей тактики (<c>+0x51dc</c>) идут в вектор, но счётчик не тратят;
    /// после третьей не из множества — стоп, даже если дальше цель из множества.</summary>
    [Fact] public void PickThree_SetTargetsGoBeyondThree()
    {
        string[] ordered = ["FT0002", "FT0007", "FT0006", "FT000C", "FT0009", "FT0004"];
        var set = new HashSet<string> { "FT0007", "FT0004" };

        Assert.Equal(["FT0002", "FT0007", "FT0006", "FT000C"], ordered.PickThree(set.Contains));
        Assert.Equal(["FT0002", "FT0007", "FT0006"], ordered.PickThree(_ => false));
        Assert.Equal(["FT0007", "FT0004"], new[] { "FT0007", "FT0004" }.PickThree(set.Contains));
    }

    /// <summary>Порог дня множества целей тактики (<c>0x4662d7</c>) по сложности партии <c>DIFFGAME</c>.</summary>
    [Theory]
    [InlineData(Difficulty.Easy, 60)]
    [InlineData(Difficulty.Average, 50)]
    [InlineData(Difficulty.Hard, 40)]
    [InlineData(Difficulty.VeryHard, 30)]
    [InlineData((Difficulty)7, 0)]
    public void WinTargetsDay_ByGameDifficulty(Difficulty difficulty, int day) =>
        Assert.Equal(day, AiTacticScore.WinTargetsDay(difficulty));

    [Fact] public void AttackStack_Neutral_CrowdBranch()
    {
        Assert.Equal((100 - 6) * 2 * 0.75, AiTacticScore.AttackStack(W, Facts(p: 1.0, near: false, count: 2)), 9);
        Assert.Equal((100 - 6) * 2.0, AiTacticScore.AttackStack(W, Facts(p: 1.0, near: false, count: 1)), 9);
    }

    [Fact] public void AttackStack_Player_SubtractsWeakness_AndThresholds()
    {
        // I_STACKS_P 120, d = 10: 120 − 30 = 90, не ближе 7; p = 0.4: 90 − 6 = 84; ниже 0,5 → ×0,01.
        Assert.Equal(0.84, AiTacticScore.AttackStack(W, Facts(neutral: false, flag88: true, d: 10, p: 0.4)), 9);
        Assert.Equal(90 - 3.0, AiTacticScore.AttackStack(W, Facts(neutral: false, flag88: true, d: 10, p: 0.7)), 9);
        // I_STACKS_R 115, d = 5 → ×2: (115 − 15)·2 = 200; p = 0.65 < 0,7 (играбельная раса) → ×0,01.
        Assert.Equal((200 - 3.5) * 0.01, AiTacticScore.AttackStack(W, Facts(neutral: false, d: 5, p: 0.65)), 9);
        Assert.Equal(200 - 3.5, AiTacticScore.Threshold(200 - 3.5, 0.65, AttackEvalKind.Normal, false, false,
                                                        targetRaceNeutral: false), 9);
    }

    [Fact] public void Threshold_Kinds()
    {
        Assert.Equal(10.0001, AiTacticScore.Threshold(10, 0.1, AttackEvalKind.Party, true, true, true), 9);
        Assert.Equal(0.0001, AiTacticScore.Threshold(0, 0.1, AttackEvalKind.Party, false, false, false), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.Threshold(10, 0.0, AttackEvalKind.Bezerk, false, false, false));
        Assert.Equal(10.0, AiTacticScore.Threshold(10, 0.01, AttackEvalKind.Bezerk, true, false, false));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.Threshold(10, 0.69, AttackEvalKind.Roam, false, true, false));
        Assert.Equal(0.1, AiTacticScore.CityThreshold(10, 0.65, AttackEvalKind.Normal, true, true, false), 9);
        Assert.Equal(10.0, AiTacticScore.CityThreshold(10, 0.65, AttackEvalKind.Normal, false, true, false));
        Assert.Equal(0.1, AiTacticScore.CityThreshold(10, 0.65, AttackEvalKind.Normal, false, false, true), 9);
    }

    /// <summary>Байт «порог взят» — тот же порог, что ×0,01 (<c>0x45137e</c>, <c>0x4505c9</c>), ровно порог проходит.</summary>
    [Fact] public void ThresholdPassed_MatchesWeakFactor()
    {
        Assert.True(AiTacticScore.ThresholdPassed(0.5, attackerNeutral: false, targetHuman: true, targetRaceNeutral: false));
        Assert.False(AiTacticScore.ThresholdPassed(0.65, attackerNeutral: false, targetHuman: false, targetRaceNeutral: true));
        Assert.True(AiTacticScore.ThresholdPassed(0.6, attackerNeutral: false, targetHuman: false, targetRaceNeutral: false));
        Assert.False(AiTacticScore.ThresholdPassed(0.69, attackerNeutral: true, targetHuman: true, targetRaceNeutral: false));
        Assert.False(AiTacticScore.CityThresholdPassed(0.65, attackerRaceNeutral: true, targetHuman: true, targetRaceNeutral: false));
        Assert.True(AiTacticScore.CityThresholdPassed(0.6, attackerRaceNeutral: false, targetHuman: true, targetRaceNeutral: true));
    }

    private static AttackCityFacts City(bool human = false, int g = 0, double d = 1, bool occupied = true,
                                        double p = 1.0) =>
        new(Hostile: true, TargetHuman: human, MaxInfluence: g, Distance: d, Occupied: occupied,
            GarrisonCount: 1, Strength: p, CapturedByOther: false, StatusToday: 0, StatusYesterday: 0);

    [Fact] public void AttackCity_WeightNeverNeutral_InfluenceDoublesWeight()
    {
        var w = W with { CitiesN = 10, CitiesP = 20, CitiesR = 30 };
        Assert.Equal(27 * 1.5, AiTacticScore.AttackCity(w, City()), 9);           // нейтрал тоже берёт I_CITIES_R
        Assert.Equal(17 * 1.5, AiTacticScore.AttackCity(w, City(human: true)), 9);
        Assert.Equal(65 * 1.5, AiTacticScore.AttackCity(w, City(g: 8)), 9);      // 30 + 30 + 8 − 3
        Assert.Equal(54 * 1.5, AiTacticScore.AttackCity(w, City() with { Occupied = false, GarrisonCount = 0 }), 9);
        var empty = City() with { Occupied = false, GarrisonCount = 0 };
        Assert.Equal(66 * 1.5, AiTacticScore.AttackCity(w with { CitiesR = 60 }, empty with { Distance = 9 }), 9);
        Assert.Equal(30 * 1.5, AiTacticScore.AttackCity(w with { CitiesR = 60 }, empty with { Distance = 10 }), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackCity(w, City() with { Hostile = false }));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackCity(w with { CitiesR = 0 }, City()));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackCity(w, City(p: 0)));
    }

    [Fact] public void AttackCity_StrengthStatusAndSummon()
    {
        var w = W with { CitiesR = 30 };
        Assert.Equal(0.135, AiTacticScore.AttackCity(w, City(p: 0.5)), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackCity(w, City(p: 0.5) with { Kind = AttackEvalKind.Roam }));
        Assert.Equal(13.5, AiTacticScore.AttackCity(w, City(p: 0.5) with { Kind = AttackEvalKind.Bezerk }), 9);
        Assert.Equal(40.5 * 0.25, AiTacticScore.AttackCity(w, City() with { StatusToday = 4, StatusYesterday = 4 }), 9);
        Assert.Equal(40.5, AiTacticScore.AttackCity(w, City() with { StatusYesterday = 5 }), 9);
        Assert.Equal(4.05, AiTacticScore.AttackCity(w, City() with { CapturedByOther = true }), 9);
        Assert.Equal(5 * 40.5 * 0.16, AiTacticScore.AttackCity(w, City() with { SummonAttacker = true }), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.AttackCity(w, City(d: 6) with { SummonAttacker = true }));
    }

    /// <summary>
    /// Границы порога крепости (<c>0x4505c9</c>) и хвоста атаки крепости (<c>0x45275f</c>) — ранг 1, сверено с
    /// листингом построчно: раса хозяина АТАКУЮЩЕГО неиграбельна — порог 0,7 и он важнее «цель — человек»
    /// (<c>0x4505f3</c> проверяется первым); хозяин крепости человек — 0,6 (<c>0x6cfcc8</c>, не 0,5, как у отряда);
    /// раса хозяина крепости неиграбельна — 0,7; прочие — 0,6. Сравнение строгое (<c>jb</c>/<c>jae</c>): ровно порог
    /// проходит. Ниже порога — ×0,01 (<c>0x6cfcc0</c>), а не ноль. Берсерк <c>0x450825</c>: только <c>p == 0</c>;
    /// бродяга <c>0x4509ce</c>: ниже 0,7 — <c>NoScore</c>. Множитель 1,5 — при <c>p &gt; 0,9</c> строго
    /// (<c>0x452a43</c> <c>jbe</c>); призыв — <c>d &gt; 5</c> строго (<c>0x452af2</c> <c>jbe</c>), на самой пятёрке
    /// счёт <c>(6 − 5)·S·0,16</c>.
    /// </summary>
    [Fact] public void AttackCity_ThresholdAndTailBoundaries()
    {
        const AttackEvalKind normal = AttackEvalKind.Normal;
        Assert.Equal(10.0, AiTacticScore.CityThreshold(10, 0.7, normal, true, false, false));
        Assert.Equal(0.1, AiTacticScore.CityThreshold(10, 0.69, normal, true, true, false), 9);
        Assert.Equal(10.0, AiTacticScore.CityThreshold(10, 0.6, normal, false, true, false));
        Assert.Equal(0.1, AiTacticScore.CityThreshold(10, 0.59, normal, false, false, false), 9);
        Assert.Equal(10.0, AiTacticScore.CityThreshold(10, 0.7, normal, false, false, true));
        Assert.Equal(0.1, AiTacticScore.CityThreshold(10, 0.69, normal, false, false, true), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticScore.CityThreshold(10, 0, AttackEvalKind.Bezerk, false, false, false));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.CityThreshold(10, 0.69, AttackEvalKind.Roam, false, false, false));
        Assert.Equal(10.0, AiTacticScore.CityThreshold(10, 0.7, AttackEvalKind.Roam, false, false, false));

        var w = W with { CitiesR = 30, CitiesP = 30 };
        Assert.Equal(24.3, AiTacticScore.AttackCity(w, City(p: 0.9)), 9);            // 27·0,9, ×1,5 не берётся
        Assert.Equal(16.2, AiTacticScore.AttackCity(w, City(p: 0.6)), 9);            // ровно порог — без ×0,01
        Assert.Equal(27 * 0.59 * 0.01, AiTacticScore.AttackCity(w, City(p: 0.59)), 9);
        Assert.Equal(27 * 0.7, AiTacticScore.AttackCity(w, City(p: 0.7) with { AttackerRaceNeutral = true }), 9);
        Assert.Equal(27 * 0.65 * 0.01,
                     AiTacticScore.AttackCity(w, City(human: true, p: 0.65) with { AttackerRaceNeutral = true }), 9);
        Assert.Equal((30 - 15) * 1.5 * 0.16, AiTacticScore.AttackCity(w, City(d: 5) with { SummonAttacker = true }), 9);
    }

    [Fact] public void AttackModifiers_Chain()
    {
        var f = new AttackModifierFacts(TargetOwnerIsTacticEnemy: true, InOperationTargets: true, IsMoveToTarget: true,
            RecordKind1: false, TargetInside: false, TargetNeutral: false, CapturedByOther: false, StatusToday: 0,
            StatusYesterday: 0);
        Assert.Equal(2.1, AiTacticScore.AttackModifiers(1, f), 9);
        Assert.Equal(1.15, AiTacticScore.AttackModifiers(1, f with { TargetOwnerIsTacticEnemy = false, IsMoveToTarget = false }), 9);
        // 0x453cf7 кладёт в счёт сам флаг «нет счёта», а не ноль: кандидат выбрасывается при вставке.
        Assert.Equal(AiTactic.NoScore,
                     AiTacticScore.AttackModifiers(1, f with { InOperationTargets = false, RecordKind1 = true }));
        Assert.Equal(1.5, AiTacticScore.AttackModifiers(1, f with { InOperationTargets = false }), 9);
        Assert.Equal(2.1 * 0.75, AiTacticScore.AttackModifiers(1, f with { TargetInside = true }), 9);
        Assert.Equal(2.1, AiTacticScore.AttackModifiers(1, f with { TargetInside = true, TargetNeutral = true }), 9);
        Assert.Equal(2.1 * 0.5 * 1.25 * 0.1, AiTacticScore.AttackModifiers(1, f with
            { StatusToday = 4, StatusYesterday = 5, CapturedByOther = true }), 9);
    }

    [Fact] public void GuardsBag_OrderAndBox()
    {
        Assert.True(AiTacticScore.GuardsBag(StackOrder.DefendLocation, true, false));
        Assert.True(AiTacticScore.GuardsBag(StackOrder.DefendLocation, false, true));
        Assert.False(AiTacticScore.GuardsBag(StackOrder.Stand, true, true));
        Assert.False(AiTacticScore.GuardsBag(StackOrder.Guard, false, true));
        Assert.True(AiTacticScore.GuardsBag(StackOrder.Normal, false, true));
        Assert.Equal((0, 7, 136, 144), AiTacticScore.BagBox(2, 140, 144));
        var g = AiTacticScore.InfluenceGrid(144, [(20, 20, 13)]);
        Assert.Equal(52, AiTacticScore.MaxInfluence(g, 18, 18, 3, 3));
        Assert.Equal(0, AiTacticScore.MaxInfluence(g, 60, 60, 3, 3));
    }

    [Fact] public void Constants()
    {
        Assert.Equal(150.0, AiTacticScore.Constant(AiActionType.DefendCity));
        Assert.Equal(225.0, AiTacticScore.Constant(AiActionType.DefendCity, defendCityHigh: true));
        Assert.Equal(150.0, AiTacticScore.Constant(AiActionType.AssistMove));
        Assert.Null(AiTacticScore.Constant(AiActionType.EnterCityRoam));
        Assert.Equal(247.0, AiTacticEval.EnterCityRoam(3));
        Assert.Equal(500.0, AiTacticScore.Constant(AiActionType.GiveItems));
        Assert.Equal(2000.0, AiTacticScore.Constant(AiActionType.Retreat));
        Assert.Null(AiTacticScore.Constant(AiActionType.PlantRod));
    }

    [Theory]
    [InlineData(null, false, 100)]
    [InlineData(0, false, AiTactic.NoScore)]
    [InlineData(0, true, 100)]
    [InlineData(1, false, 34)]
    [InlineData(2, false, 67)]
    [InlineData(3, false, 100)]
    [InlineData(4, false, 133)]
    [InlineData(5, false, 166)]
    [InlineData(6, false, 200)]
    [InlineData(7, false, 100)]
    public void EditorPriority_Table(int? priority, bool orderTarget, double expected) =>
        Assert.Equal(expected, AiTacticScore.EditorPriority(100, priority, orderTarget));

    [Fact] public void EditorPriority_AndSetBonus_LeaveNoScoreAlone_ButMoveZero()
    {
        Assert.Equal(AiTactic.NoScore, AiTacticScore.EditorPriority(AiTactic.NoScore, 6, false));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.EditorPriority(AiTactic.NoScore, 0, false));
        Assert.Equal(AiTactic.NoScore, AiTacticScore.TargetSetBonus(AiTactic.NoScore, true));
        Assert.Equal(100.0, AiTacticScore.EditorPriority(0, 6, false));
        Assert.Equal(100.0, AiTacticScore.TargetSetBonus(0, true));
        Assert.Equal(150.0, AiTacticScore.TargetSetBonus(50, true));
        Assert.Equal(50.0, AiTacticScore.TargetSetBonus(50, false));
    }

    // ------------------------------------------------------------------ вставка и выбор

    [Fact] public void Insert_DropsFailedAndNoScore_KeepsZero_BoostsYesterdayProgress()
    {
        IReadOnlyList<AiScored<string>> list = [];
        list = AiTactic.Insert(list, "a", 10, 0, 0);
        list = AiTactic.Insert(list, "failed", 50, AiTactic.StatusFailed, 0);
        list = AiTactic.Insert(list, "none", AiTactic.NoScore, 0, AiTactic.StatusProgress);
        list = AiTactic.Insert(list, "zero", 0, 0, 0);
        list = AiTactic.Insert(list, "b", 10, 0, AiTactic.StatusProgress);
        Assert.Equal([new("a", 10), new("zero", 0), new("b", 12.5)], list);
    }

    [Fact] public void Top_StableDescending_TiesKeepInsertionOrder()
    {
        AiScored<string>[] c = [new("a", 5), new("b", 9), new("c", 9), new("d", 1), new("e", 7)];
        var top = AiTactic.Top(c, (_, s) => s);
        Assert.Equal(["b", "c", "e", "a"], top.Select(x => x.Action));
        Assert.Equal("b", AiTactic.Pick(c, (_, s) => s)?.Action);
    }

    [Fact] public void Top_RescoresOnlyUntilFourSurvive_NoScoreDropsAndNextEnters()
    {
        AiScored<string>[] c = [new("a", 6), new("b", 5), new("c", 4), new("d", 3), new("e", 2), new("f", 1)];
        var asked = new List<string>();
        var top = AiTactic.Top(c, (a, s) => { asked.Add(a); return a == "b" ? AiTactic.NoScore : a == "e" ? 100 : s; });
        Assert.Equal(["a", "b", "c", "d", "e"], asked);        // f не проверялся
        Assert.Equal(["e", "a", "c", "d"], top.Select(x => x.Action));
    }

    [Fact] public void Pick_NothingWhenAllRescoredToNoScore_ButZeroStays()
    {
        Assert.Null(AiTactic.Pick<string>([new("a", 1)], (_, _) => AiTactic.NoScore));
        Assert.Equal(0.0, AiTactic.Pick<string>([new("a", 1)], (_, _) => 0)?.Score);
    }

    [Fact] public void Memo_IsImmutable_AndCaseInsensitive()
    {
        var m = AiTacticMemo.Empty.With("S143KC0001", "S143CR0002", 3, 4, 7, AiTactic.StatusProgress);
        Assert.Equal(5, m.Status("s143kc0001", "s143cr0002", 3, 4, 7));
        Assert.Equal(0, m.Status("S143KC0001", "S143CR0002", 3, 4, 6));
        Assert.Equal(0, AiTacticMemo.Empty.Status("S143KC0001", "S143CR0002", 3, 4, 7));
    }

    [Fact] public void Memo_KeepsOneDayPerStackTargetPoint()
    {
        var m = AiTacticMemo.Empty.With("S1", "T1", 3, 4, 6, AiTactic.StatusProgress)
                                  .With("S1", "T1", 3, 5, 6, AiTactic.StatusProgress);
        Assert.Equal(5, m.Status("S1", "T1", 3, 4, 6));
        m = m.With("S1", "T1", 3, 4, 7, 2);                    // сегодняшняя запись затирает вчерашнюю
        Assert.Equal(0, m.Status("S1", "T1", 3, 4, 6));
        Assert.Equal(2, m.Status("S1", "T1", 3, 4, 7));
        Assert.Equal(5, m.Status("S1", "T1", 3, 5, 6));        // другая точка — свой узел
    }

    // ------------------------------------------------------------------ клетка жезла

    private static bool No(int x, int y) => false;

    [Fact] public void RodCell_NearestNeighbour_FirstOnTie()
    {
        Assert.Equal((4, 4), AiTacticScore.RodCell(5, 5, 0, 0, No, No, No));
        Assert.Equal((4, 4), AiTacticScore.RodCell(5, 5, 5, 0, No, No, No));   // (4,4),(5,4),(6,4) — все 4
        Assert.Equal((6, 6), AiTacticScore.RodCell(5, 5, 9, 9, No, No, No));
        Assert.Equal((4, 4), AiTacticScore.RodCell(5, 5, 0, 5, No, No, No));   // (4,4),(4,5),(4,6) — все 4
    }

    [Fact] public void RodCell_SkipsEachRefusal_NullWhenNoneLeft()
    {
        Assert.Equal((5, 4), AiTacticScore.RodCell(5, 5, 5, 0, (x, y) => (x, y) == (4, 4), No, No));
        Assert.Equal((6, 4), AiTacticScore.RodCell(5, 5, 5, 0, (x, y) => x == 4, (x, y) => x == 5, No));
        Assert.Equal((4, 5), AiTacticScore.RodCell(5, 5, 5, 0, No, No, (x, y) => y == 4));
        Assert.Null(AiTacticScore.RodCell(5, 5, 5, 0, No, No, (_, _) => true));
    }

    // ------------------------------------------------------------------ GAI.dbf

    [SkippableFact] public void Weights_LoadFromGai()
    {
        ImportedData.RequireRules();
        var w = EconomyLoader.LoadAiWeights(ImportedData.Content);
        Assert.Equal(7, w.Count);
        Assert.Equal(W with { Category = 0 }, w[0]);
        Assert.Equal(0, w[4].PlantRod);
        Assert.Equal(0, w[4].CitiesN);
        Assert.Equal(0, w[6].Ruins);
    }
}
