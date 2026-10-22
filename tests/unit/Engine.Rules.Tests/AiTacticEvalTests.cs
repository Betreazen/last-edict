using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using T = LastEdict.Rules.Map.AiActionType;

namespace LastEdict.Rules.Tests;

/// <summary>Построители списков действий, оценщики и приказы тактики ИИ на карте (<see cref="AiTacticLists"/>,
/// <see cref="AiTacticEval"/>, <see cref="AiTacticOrders"/>) на синтетике — формулы из листингов GOG 3.01.</summary>
public class AiTacticEvalTests
{
    private static readonly AiWeights W = new(0, CitiesN: 110, CitiesR: 110, CitiesP: 120, StacksN: 100,
        StacksR: 115, StacksP: 120, Bags: 95, Ruins: 100, Towers: 100, Merchant: 90, HireUnits: 120,
        HireMercs: 120, Heal: 130, PlantRod: 120, AttackRod: 105, NeutralCities: 100, NeutralStacks: 100,
        Trainer: 100);

    private static readonly AiStackFlags Free = new(RiotCity: false, OwnerCaptured: false, InjuredInside: false,
        WaterOnly: false, LeaderAlive: true, Inside: false);

    // ------------------------------------------------------------------ класс отряда и списки

    [Fact] public void Classify_NeutralFirst_ThenLeaderCategory_ThenRod()
    {
        Assert.Equal(AiStackKind.Neutral, AiTacticLists.Classify(true, AiLeaderKind.Noble, true));
        Assert.Equal(AiStackKind.Thief, AiTacticLists.Classify(false, AiLeaderKind.Noble, true));
        Assert.Equal(AiStackKind.Summon, AiTacticLists.Classify(false, AiLeaderKind.Illusion, true));
        Assert.Equal(AiStackKind.Rod, AiTacticLists.Classify(false, AiLeaderKind.Other, true));
        Assert.Equal(AiStackKind.Fighter, AiTacticLists.Classify(false, AiLeaderKind.Other, false));
    }

    [Fact] public void Fighter_FullList_InBuilderOrder() =>
        Assert.Equal([T.Retreat, T.AttackStack, T.MoveToStack, T.AttackCity, T.VisitRuin, T.HireUnit, T.HireMercs,
                      T.VisitMage, T.VisitMerchant, T.VisitTemple, T.VisitTrainer, T.AttackStackBezerk,
                      T.AttackParty, T.AttackStackRoam, T.MoveToLocation, T.Guard, T.DefendParty,
                      T.DefendLocation, T.AssistMove, T.GiveItems, T.AttackCityBezerk, T.AttackCityRoam,
                      T.SecureCity, T.HealRevive, T.DefendCity, T.TakeBag],
                     AiTacticLists.Build(AiStackKind.Fighter, Free));

    [Fact] public void Fighter_InsideInjured_KeepsOnlyOrderAndStayInside() =>
        Assert.Equal([T.AttackStackBezerk, T.AttackParty, T.AttackStackRoam, T.MoveToLocation, T.Guard,
                      T.DefendParty, T.DefendLocation, T.AssistMove, T.GiveItems, T.AttackCityBezerk,
                      T.AttackCityRoam, T.SecureCity, T.StayInside],
                     AiTacticLists.Build(AiStackKind.Fighter, Free with { InjuredInside = true, Inside = true }));

    [Fact] public void Riot_BuildsNothing_ExceptSummon()
    {
        foreach (var k in new[] { AiStackKind.Fighter, AiStackKind.Neutral, AiStackKind.Rod, AiStackKind.Thief })
            Assert.Empty(AiTacticLists.Build(k, Free with { RiotCity = true }));
        Assert.NotEmpty(AiTacticLists.Build(AiStackKind.Summon, Free with { RiotCity = true }));
    }

    [Fact] public void Fighter_CapturedOwner_StopsAfterBezerkCity() =>
        Assert.Equal([T.AttackStackBezerk, T.AssistMove, T.GiveItems, T.AttackCityBezerk],
                     AiTacticLists.Build(AiStackKind.Fighter, Free with { OwnerCaptured = true, Inside = true }));

    [Fact] public void Neutral_List() =>
        Assert.Equal([T.AttackCity, T.AttackStack, T.MoveToStack, T.AttackStackBezerk, T.AttackParty,
                      T.AttackStackRoam, T.MoveToLocation, T.Guard, T.DefendParty, T.DefendLocation,
                      T.EnterCityRoam, T.AttackCityBezerk, T.AttackCityRoam, T.SecureCity, T.DefendCity,
                      T.TakeBag, T.TakeBagRoam],
                     AiTacticLists.Build(AiStackKind.Neutral, Free));

    [Fact] public void Rod_List_HasNoAttackParty() =>
        Assert.Equal([T.Retreat, T.PlantRod, T.AttackRod, T.HireUnit, T.VisitMage, T.VisitTemple,
                      T.AttackStackBezerk, T.AttackStackRoam, T.MoveToLocation, T.Guard, T.DefendParty,
                      T.DefendLocation, T.AssistMove, T.GiveItems, T.AttackCityBezerk, T.AttackCityRoam,
                      T.SecureCity, T.HealRevive],
                     AiTacticLists.Build(AiStackKind.Rod, Free));

    [Fact] public void Summon_WaterOnly_KeepsMoveToStack()
    {
        Assert.Equal([T.AttackParty, T.AttackStackBezerk, T.AttackStackRoam, T.MoveToLocation, T.Guard,
                      T.DefendParty, T.DefendLocation, T.SummonAttack, T.MoveToStack],
                     AiTacticLists.Build(AiStackKind.Summon, Free with { WaterOnly = true }));
        Assert.Equal(14, AiTacticLists.Build(AiStackKind.Summon, Free).Count);
    }

    [Fact] public void Thief_StealOrder_AddsMerchantAndBag()
    {
        var plain = AiTacticLists.Build(AiStackKind.Thief, Free);
        Assert.DoesNotContain(T.NobleMerchant, plain);
        Assert.DoesNotContain(T.TakeBag, plain);
        Assert.Equal([T.Retreat, T.NobleStack, T.NobleCity, T.NobleMerchant, T.VisitTemple],
                     AiTacticLists.Build(AiStackKind.Thief, Free with { Steal = true }).Take(5));
        Assert.Equal(T.TakeBag, AiTacticLists.Build(AiStackKind.Thief, Free with { Steal = true })[^1]);
    }

    [Fact] public void EffectiveOrder_NormalDefersToAiOrder()
    {
        Assert.Equal(StackOrder.Steal, AiTacticLists.EffectiveOrder(StackOrder.Normal, StackOrder.Steal));
        Assert.Equal(StackOrder.Guard, AiTacticLists.EffectiveOrder(StackOrder.Guard, StackOrder.Steal));
    }

    // ------------------------------------------------------------------ постоянные действия

    [Fact] public void Retreat_NeedsHumanNear_StrongAndFarFromHome()
    {
        (bool, int, double)[] one = [(true, 6, 0.8)];
        Assert.True(AiTacticEval.RetreatBuilt(false, 7, one));
        Assert.False(AiTacticEval.RetreatBuilt(true, 7, one));
        Assert.False(AiTacticEval.RetreatBuilt(false, 6, one));
        Assert.False(AiTacticEval.RetreatBuilt(false, 7, [(true, 7, 0.9)]));
        Assert.False(AiTacticEval.RetreatBuilt(false, 7, [(false, 1, 1.0)]));
        Assert.False(AiTacticEval.RetreatBuilt(false, 7, [(true, 1, 0.79)]));
    }

    [Fact] public void Guard_RadiusIsHalfRange_AndFilters()
    {
        var c = new AiGuardCandidate(StackOrder.Normal, 2, SameSubrace: false, Inside: false, OnWater: false,
                                     Invisible: false, Allied: false, SameLeaderCategory: false);
        Assert.True(AiTacticEval.GuardAdmits(c, false, guardRange: 5));
        Assert.False(AiTacticEval.GuardAdmits(c with { Distance = 3 }, false, 5));
        Assert.False(AiTacticEval.GuardAdmits(c with { EffectiveOrder = StackOrder.Stand }, false, 5));
        Assert.False(AiTacticEval.GuardAdmits(c with { SameSubrace = true }, false, 5));
        Assert.False(AiTacticEval.GuardAdmits(c, selfWaterOnly: true, 5));
        Assert.True(AiTacticEval.GuardAdmits(c with { OnWater = true }, true, 5));
        Assert.False(AiTacticEval.GuardAdmits(c with { Allied = true }, false, 5));
        Assert.False(AiTacticEval.GuardAdmits(c with { SameLeaderCategory = true }, false, 5));
    }

    [Fact] public void DefendCity_ScoreAndApproach()
    {
        Assert.Equal(225.0, AiTacticEval.DefendCityScore(selfIsVisitor: true));
        Assert.Equal(150.0, AiTacticEval.DefendCityScore(false));
        Assert.True(AiTacticEval.OwnFortAdmits(sameSide: true, riot: false, capitalOfCapturedOwner: false));
        Assert.False(AiTacticEval.OwnFortAdmits(true, true, false));
        Assert.False(AiTacticEval.OwnFortAdmits(false, false, false));
        Assert.Equal(AiFortStep.Stay, AiTacticEval.FortStep(true, false, true));
        Assert.Equal(AiFortStep.KickOut, AiTacticEval.FortStep(false, true, true));
        Assert.Equal(AiFortStep.Enter, AiTacticEval.FortStep(false, true, false));
        Assert.Equal(AiFortStep.Move, AiTacticEval.FortStep(false, false, true));
    }

    private static bool Any(int x, int y) => true;

    [Fact] public void AssistMove_NearestRingCell_ElseTargetWhenFar()
    {
        Assert.Equal((10, 8, false), AiTacticEval.AssistMoveTarget(10, 10, 10, 0, Any));
        Assert.Equal((8, 10, false), AiTacticEval.AssistMoveTarget(10, 10, 0, 10, Any));
        Assert.Null(AiTacticEval.AssistMoveTarget(10, 10, 10, 8, Any));             // уже на лучшей клетке
        Assert.Equal((10, 10, true), AiTacticEval.AssistMoveTarget(10, 10, 13, 10, (_, _) => false));
        Assert.Null(AiTacticEval.AssistMoveTarget(10, 10, 12, 10, (_, _) => false)); // d = 2 — не дальше двух
    }

    [Fact] public void GiveItems_And_EnterCityRoam_And_MoveToStack()
    {
        Assert.True(AiTacticEval.GiveItemsBuilt(StackOrder.Assist, 1, true));
        Assert.False(AiTacticEval.GiveItemsBuilt(StackOrder.Steal, 1, true));
        Assert.False(AiTacticEval.GiveItemsBuilt(StackOrder.Assist, 0, true));
        Assert.True(AiTacticEval.EnterCityRoamAdmits(false, false, false, false, true, false, false));
        Assert.False(AiTacticEval.EnterCityRoamAdmits(false, false, true, false, true, false, false));
        Assert.False(AiTacticEval.EnterCityRoamAdmits(false, false, false, false, true, true, false));
        // Захват хозяина закрывает построитель, «отряд в крепости» — нет (первый аргумент сменил смысл).
        Assert.False(AiTacticEval.EnterCityRoamAdmits(true, false, false, false, true, false, false));
        Assert.False(AiTacticEval.EnterCityRoamAdmits(false, false, false, false, true, false, false, inside: true));   // 0x455cf4
        Assert.True(AiTacticEval.MoveToStackAdmits(true, true, false, false, 0.7, false));
        Assert.False(AiTacticEval.MoveToStackAdmits(true, true, false, false, 0.69, false));
        Assert.False(AiTacticEval.MoveToStackAdmits(true, true, true, false, 0.9, false));
    }

    [Theory]
    [InlineData(true, true, true, false, 0, 0.0, AiSecureStep.Stay)]
    [InlineData(false, false, true, false, 0, 0.0, AiSecureStep.Path)]
    [InlineData(false, true, false, false, 3, 0.0, AiSecureStep.AttackCity)]
    [InlineData(false, true, false, true, 0, 0.0, AiSecureStep.None)]
    [InlineData(false, true, true, false, 0, 0.0, AiSecureStep.Enter)]
    public void SecureCity_Step(bool inside, bool adjacent, bool own, bool summon, int garrison, double dmg,
                                AiSecureStep step) =>
        Assert.Equal(step, AiTacticEval.SecureCityStep(inside, adjacent, own, occupied: false, summon, garrison, dmg));

    [Fact] public void SecureCity_KicksOnlyHealthyOccupant()
    {
        Assert.Equal(AiSecureStep.KickOut, AiTacticEval.SecureCityStep(false, true, true, true, false, 0, 0.09));
        Assert.Equal(AiSecureStep.None, AiTacticEval.SecureCityStep(false, true, true, true, false, 0, 0.1));
    }

    // ------------------------------------------------------------------ шаблонные оценщики

    [Fact] public void TakeBag_NearAddsWeightTwice_FarPenalised_FloorFive()
    {
        Assert.Equal(95 + 8 + 95.0, AiTacticEval.TakeBag(W, 8, 6, false, false));
        Assert.Equal((95 + 8 - 21) * 0.75, AiTacticEval.TakeBag(W, 8, 7, false, false), 9);
        Assert.Equal((95 + 8 - 21) * 0.75 * 0.25, AiTacticEval.TakeBag(W, 8, 7, true, false), 9);
        Assert.Equal(8.0, AiTacticEval.TakeBag(W with { Bags = 0 }, 8, 1, false, false));   // 0 + 4g, пол не сработал
        Assert.Equal(0.5, AiTacticEval.TakeBag(W, 0, 40, false, true), 9);       // пол 5, затем ×0,1
        Assert.True(AiTacticEval.TakeBagRoamAdmits(3));
        Assert.False(AiTacticEval.TakeBagRoamAdmits(4));
    }

    [Fact] public void VisitRuin_StrengthGate_AndScale()
    {
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitRuin(W, 0.69, 0, 1, false, 0));
        Assert.Equal((100 - 3) * 0.7, AiTacticEval.VisitRuin(W, 0.7, 0, 1, false, 0), 9);
        Assert.Equal((100 - 3) * 0.8 * 0.1 * 1.25, AiTacticEval.VisitRuin(W, 0.8, 0, 1, true, 5), 9);
    }

    [Fact] public void Mage_Merchant_Trainer()
    {
        Assert.Equal(97.0, AiTacticEval.VisitMage(W, 0, 1, false, 0, 0, canBuy: true, spellCountOffAverage: true));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitMage(W, 0, 1, false, 1, 0, true, true));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitMage(W, 0, 1, false, 0, 0, true, false));
        Assert.Equal(97 * 1.25, AiTacticEval.VisitMage(W, 0, 1, false, 0, 5, true, true));
        Assert.Equal(87 * 1.3, AiTacticEval.VisitMerchant(W, 0, 1, false, true, sellable: 3, 0, 0), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitMerchant(W, 0, 1, true, true, 3, 0, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitMerchant(W, 0, 1, false, true, 3, 4, 0));
        Assert.Equal(97 * 2.0, AiTacticEval.VisitTrainer(W, 0, 1, false, suggestedLevel10: true, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitTrainer(W, 0, 1, false, false, 3));
    }

    [Fact] public void Temple_DamageShape_AndTwoPerCell()
    {
        Assert.Equal(130 * 2 - 2.0, AiTacticEval.VisitTemple(W, 0.95, 1, true, false, 0));
        Assert.Equal((130 * 0.5 - 2) * 1.1, AiTacticEval.VisitTemple(W, 0.5, 1, true, true, 0), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitTemple(W, 0.5, 1, false, false, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.VisitTemple(W, 0.5, 1, true, false, 3));
        Assert.Equal(0.5, AiTacticEval.DamageRatio([(5, 10, false), (10, 10, false)]));
        Assert.Equal(0.99, AiTacticEval.DamageRatio([(4, 10, true)]));
        Assert.Equal(0.0, AiTacticEval.DamageRatio([]));
    }

    [Fact] public void HireUnit_Mercs_StayInside()
    {
        Assert.Equal(120 * 0.5 * 3 - 6, AiTacticEval.HireUnit(W, occupied: true, free: 2, 2, canHire: true, false));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireUnit(W, false, 2, 2, false, false));
        Assert.Equal((120 + 4 - 30) * 2.0, AiTacticEval.HireMercs(W, 4, 10, free: 2, false, false, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 21, 2, false, false, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 10, 2, false, suggestedLevel10: true, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 10, 2, false, false, 2));
        Assert.Equal(120 * 3 - 1.0, AiTacticEval.StayInside(W, free: 2, hireGap: 0, notReady: 1, leaderAlive: true));
        Assert.Equal(120 * 3 * 2.0, AiTacticEval.StayInside(W, 2, 0, notReady: 1, leaderAlive: false));
        Assert.Equal((120 + 1.5) * 2, AiTacticEval.StayInside(W, 0, hireGap: 3, notReady: 0, true));
        Assert.Null(AiTacticEval.StayInside(W, 0, hireGap: 0.5, 0, true));
    }

    [Fact] public void HealRevive_Factors()
    {
        var f = new AiHealFacts(SameSubrace: true, StatusToday: 0, OccupiedByOther: false, Healthy: true,
                                Distance: 2, HasDead: false, LeaderAlive: true, CapturedByOther: false);
        Assert.Equal(126.0, AiTacticEval.HealRevive(W, f));
        Assert.Equal((130 * 0.9 * 2 - 4) * 2, AiTacticEval.HealRevive(W, f with
            { OccupiedByOther = true, Healthy = false, HasDead = true }), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HealRevive(W, f with { StatusToday = 4 }));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HealRevive(W, f with { SameSubrace = false }));
        Assert.Equal(252.0, AiTacticEval.HealRevive(W, f with { LeaderAlive = false }));
    }

    [Fact] public void Summon_NearBonus_AndNeutral()
    {
        Assert.Equal((115 - 9 + 5 * 80) * 2.0, AiTacticEval.SummonAttack(W, false, false, false, 3));
        Assert.Equal(100 * 0.5 - 24.0, AiTacticEval.SummonAttack(W, true, false, false, 8));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.SummonAttack(W, false, false, allied: true, 1));
        Assert.Equal(1 + 80.0, AiTacticEval.SummonNeutral(true, 7));
        Assert.Equal(80.0, AiTacticEval.SummonNeutral(false, 7));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.SummonNeutral(true, 8));
    }

    [Fact] public void Noble_CityAndStack()
    {
        Assert.Equal(97 * 1.1, AiTacticEval.NobleCity(W, false, false, true, 0, 1, 0, 5), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticEval.NobleCity(W, false, false, false, 0, 1, 0, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.NobleCity(W, false, false, true, 0, 1, 4, 0));
        var s = new AiNobleStackFacts(Invisible: false, Inside: false, Neutral: false, IsMoveTarget: false,
            SummonLeader: false, Allied: false, Human: true, HasSpyRiotSteal: true, OnlyDuelWithRod: false,
            Influence: 0, Distance: 1, StatusToday: 0, StatusYesterday: 0);
        Assert.Equal(120 * 1.5 - 3, AiTacticEval.NobleStack(W, s));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.NobleStack(W, s with { SummonLeader = true }));
        Assert.Equal(115 * 1.5 - 3, AiTacticEval.NobleStack(W, s with { SummonLeader = true, IsMoveTarget = true, Human = false }));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.NobleStack(W, s with { OnlyDuelWithRod = true }));
    }

    [Fact] public void AttackRod_And_CrystalLoop()
    {
        Assert.Equal(105 - 3.0, AiTacticEval.AttackRod(W, false, false, 0, 1, false, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.AttackRod(W, ownRod: true, false, 0, 1, false, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.AttackRod(W, false, false, 0, 1, false, AiTactic.StatusFailed));
        AiCrystal[] crystals =
        [
            new(["R1", "R2"], null, 0),          // чужие жезлы → AttackRod по каждому
            new([], (3, 3), 4),                  // клетка далеко → PlantRod и дальше
            new([], null, 0),                    // клетки нет — ничего
            new([], (7, 7), 1),                  // рядом → PlantRod и стоп
            new(["R3"], null, 0),
        ];
        Assert.Equal(["attack:R1", "attack:R2", "plant:1", "plant:3"],
                     AiTacticEval.RodPlan(false, true, crystals).Select(c => c.ToString()));
        Assert.Equal(["plant:1", "plant:3"], AiTacticEval.RodPlan(false, false, crystals).Select(c => c.ToString()));
        Assert.Empty(AiTacticEval.RodPlan(true, true, crystals));
    }

    // ------------------------------------------------------------------ путь

    [Fact] public void Path_WaterLeaderOnLand_OrNoPath_HasNoScore()
    {
        Assert.Equal(7.0, AiTacticEval.PathRescore(7, false, false, routeKnown: false, pathFound: true));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.PathRescore(7, true, false, true, true));
        Assert.Equal(7.0, AiTacticEval.PathRescore(7, true, true, false, true));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.PathRescore(7, false, false, false, false));
        Assert.Equal(7.0, AiTacticEval.PathRescore(7, false, false, true, false));
    }

    // ------------------------------------------------------------------ приказы

    [Fact] public void CityOrder_FarthestFirst_TwoFlagged_TiesReversed()
    {
        var order = AiTacticOrders.CityOrder([("A", 3), ("B", 9), ("C", 5), ("D", 5)]);
        Assert.Equal([("B", true), ("D", true), ("C", false), ("A", false)], order);
    }

    [Fact] public void Operational_LastFreeDefenderGoesToFarthestCity()
    {
        var cities = AiTacticOrders.CityOrder([("A", 3), ("B", 9), ("C", 5)]);
        AiDefender[] stacks =
        [
            new("S1", StackOrder.DefendCity, Target: "B"),        // защищает дальний — занят
            new("S2", StackOrder.DefendCity, Target: "A"),        // свой, не из двух дальних — свободен
            new("S3", StackOrder.Normal, Target: ""),             // не защитник
            new("S4", StackOrder.DefendCity, Target: "X", TargetOwn: false),
            new("S5", StackOrder.DefendCity, Target: ""),
        ];
        var r = AiTacticOrders.Operational(cities, stacks);
        Assert.Equal(new AiOrderChange("S5", StackOrder.DefendCity, "B"), Assert.Single(r));
        Assert.Equal([new AiOrderChange("S2", StackOrder.Normal, ""), new AiOrderChange("S5", StackOrder.Normal, "")],
                     AiTacticOrders.Operational([], [stacks[1], stacks[4]]));
        Assert.Empty(AiTacticOrders.Operational(cities, [stacks[0], stacks[2]]));
    }

    /// <summary>Раздача <c>0x44e29e</c> → <c>0x44e518</c> по листингу: 1–3 отряда — (1, 0, 0), (1, 1, 0), (1, 1, 1) без
    /// долей; от 4 — <c>a = max(1, ⌊n·pA/100⌋)</c>, <c>b = max(1, ⌊n·pB/100⌋)</c>, остальные — третий; обычный не пишется.</summary>
    [Fact] public void StrategicOrders_ByListing()
    {
        static string[] Ids(int n) => [.. Enumerable.Range(1, n).Select(i => $"S{i}")];
        static string Of(IReadOnlyList<AiOrderChange> r) => string.Join(" ", r.Select(c => $"{c.Stack}:{c.Order}"));
        Assert.Empty(AiTacticOrders.Strategic([], Ids(1), assist: true, defendCity: true, steal: true));
        Assert.Equal("S2:DefendCity", Of(AiTacticOrders.Strategic([], Ids(2), false, false, false)));
        Assert.Equal("S2:DefendCity S3:Assist", Of(AiTacticOrders.Strategic([], Ids(3), false, false, false)));
        Assert.Empty(AiTacticOrders.Strategic([], Ids(4), false, false, false));            // a = 4
        Assert.Equal("S3:DefendCity S4:Assist", Of(AiTacticOrders.Strategic([], Ids(4), false, true, false)));   // 67/33: a 2, b 1, c 1
        Assert.Equal("S3:DefendCity S4:Assist S5:Assist S6:Assist",
                     Of(AiTacticOrders.Strategic([], Ids(6), true, true, false)));          // 34/33: a 2, b 1, c 3
        // Воры: (обычный, помощь, кража); 2 — второму помощь, от 4 при помощи и краже — 30/50.
        Assert.Equal("S2:Assist", Of(AiTacticOrders.Strategic(Ids(2), [], false, false, false)));
        Assert.Equal("S2:Assist S3:Assist S4:Steal", Of(AiTacticOrders.Strategic(Ids(4), [], true, false, true)));
        Assert.Equal("S5:Assist", Of(AiTacticOrders.Strategic(Ids(5), [], false, false, true)));   // 80/0: a 4, b — не меньше 1
    }

    [Fact] public void StackOrder_GroupsSortedByKey_AssistsFollowTheirTarget()
    {
        AiQueued[] s =
        [
            new("O2", 20, AiStackGroup.Other),
            new("A1", 5, AiStackGroup.Assist, AssistTarget: "O1"),
            new("D1", 30, AiStackGroup.DefendCity),
            new("O1", 10, AiStackGroup.Other),
            new("R1", 1, AiStackGroup.Rod),
            new("N1", 2, AiStackGroup.Noble),
            new("M1", 3, AiStackGroup.Summon),
            new("A2", 6, AiStackGroup.Assist, AssistTarget: "Z"),
        ];
        Assert.Equal(["D1", "M1", "N1", "R1", "O1", "A1", "O2", "A2"], AiTacticOrders.TurnQueue(s, null));
        Assert.Equal(["O2", "D1", "M1", "N1", "R1", "O1", "A1", "A2"], AiTacticOrders.TurnQueue(s, "O2"));
    }

    [Fact] public void SpellBeforeAttack_Categories()
    {
        Assert.Equal([SpellCategory.Attack, SpellCategory.Summon, SpellCategory.Lower, SpellCategory.Boost],
                     AiBeforeAttack.SpellCategories(0.9, passed: true, targetStackOutside: true, attackerOutside: true));
        Assert.Equal([SpellCategory.Attack], AiBeforeAttack.SpellCategories(0.5, passed: true, true, true));
        Assert.Empty(AiBeforeAttack.SpellCategories(0.5, passed: true, targetStackOutside: false, true));
        Assert.Equal([SpellCategory.Summon], AiBeforeAttack.SpellCategories(0.5, passed: false, false, true));
        Assert.Equal([SpellCategory.Attack, SpellCategory.Summon], AiBeforeAttack.SpellCategories(0.9, passed: false, true, true));
    }

    [Fact] public void BeforeAttack_ThresholdsAndTail()
    {
        Assert.True(AiBeforeAttack.Prepares(0.74, targetHuman: true, targetRaceNeutral: false));
        Assert.False(AiBeforeAttack.Prepares(0.7, targetHuman: false, targetRaceNeutral: false));
        Assert.True(AiBeforeAttack.Prepares(0.7, targetHuman: false, targetRaceNeutral: true));
        // Хвост 0x4720a4: порог не взят — атака остаётся; взят у человека при p < 0,6 — нет.
        Assert.True(AiBeforeAttack.AttackAlone(0.3, passed: false, targetHuman: true, targetRaceNeutral: false));
        Assert.False(AiBeforeAttack.AttackAlone(0.55, passed: true, targetHuman: true, targetRaceNeutral: false));
        Assert.True(AiBeforeAttack.AttackAlone(0.6, passed: true, targetHuman: true, targetRaceNeutral: true));
        Assert.False(AiBeforeAttack.AttackAlone(0.65, passed: true, targetHuman: false, targetRaceNeutral: true));
        Assert.Equal((1, 2), AiBeforeAttack.Fallback(3, passed: true));
        Assert.Null(AiBeforeAttack.Fallback(1, passed: true));
        Assert.Null(AiBeforeAttack.Fallback(3, passed: false));
    }

    // ------------------------------------------------------------------ сверка с листингами 18.09.2026

    /// <summary><c>0x455f61</c>: 250 минус расстояние, а не постоянные 250.</summary>
    [Fact] public void EnterCityRoam_Score_IsBaseMinusDistance()
    {
        Assert.Equal(250.0, AiTacticEval.EnterCityRoam(0));
        Assert.Equal(243.5, AiTacticEval.EnterCityRoam(6.5));
        Assert.Null(AiTacticScore.Constant(AiActionType.EnterCityRoam));
    }

    /// <summary><c>0x4592cc</c>: влияние, порог 20, −3d, ×мест, ×0,1 захват, уровень 10 и статусы 2/3.</summary>
    [Fact] public void HireMercs_ListingOrder()
    {
        // 120 + 4 − 3·2 = 118, ×2 места.
        Assert.Equal(236.0, AiTacticEval.HireMercs(W, 4, 2, 2, false, false, 0));
        Assert.Equal(23.6, AiTacticEval.HireMercs(W, 4, 2, 2, true, false, 0), 9);
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 21, 2, false, false, 0));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 2, 2, false, false, AiTactic.StatusFailed));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 2, 2, false, false, 2));
        // suggestedLevel ≥ 10: есть места — кандидата нет; мест нет — счёт и так ноль (×0 мест), ×2 его не спасает.
        Assert.Equal(AiTactic.NoScore, AiTacticEval.HireMercs(W, 4, 2, 2, false, true, 0));
        Assert.Equal(0.0, AiTacticEval.HireMercs(W, 4, 2, 0, false, true, 0));
    }

    /// <summary><c>0x46517a</c> + <c>0x465042</c> и <c>0x465322</c>.</summary>
    [Fact] public void Summon_BaseAndNeutralEval()
    {
        Assert.Equal(AiTactic.NoScore, AiTacticEval.SummonAttack(W, false, false, allied: true, 3));
        // Нейтрал: 100·0,5 − 3·9 = 23; ближе 8 нет, ближе 4 нет.
        Assert.Equal(23.0, AiTacticEval.SummonAttack(W, true, false, false, 9));
        // Человек: 120 − 3·5 = 105, +(8−5)·80 = 345, d < 4 нет.
        Assert.Equal(345.0, AiTacticEval.SummonAttack(W, false, true, false, 5));
        // d = 3: 115 − 9 = 106, +(8−3)·80 = 506, ×2 = 1012.
        Assert.Equal(1012.0, AiTacticEval.SummonAttack(W, false, false, false, 3));
        Assert.Equal(1.0 + 80 * 5, AiTacticEval.SummonNeutral(true, 3));
        Assert.Equal(80.0 * 5, AiTacticEval.SummonNeutral(false, 3));
        Assert.Equal(AiTactic.NoScore, AiTacticEval.SummonNeutral(true, 8));
    }

    /// <summary><c>0x46903e</c>: преграду-отряд записывают, только когда пути нет и тип id — отряд.</summary>
    [Fact] public void BlockingStack_OnlyWhenPathFailedAndBlockerIsStack()
    {
        Assert.Equal("S1", AiTacticEval.BlockingStack(false, AiTacticEval.StackIdType, "S1"));
        Assert.Equal("", AiTacticEval.BlockingStack(true, AiTacticEval.StackIdType, "S1"));
        Assert.Equal("", AiTacticEval.BlockingStack(false, 0x1d, "F1"));
    }

    /// <summary><c>0x455503</c>: <c>PA_RANGE</c> пополам со срезом к нулю.</summary>
    [Fact] public void DefendPartyRadius_IsHalfOfPaRange()
    {
        Assert.Equal(3, AiTacticEval.DefendPartyRadius(7));
        Assert.Equal(4, AiTacticEval.DefendPartyRadius(8));
        Assert.Equal(0, AiTacticEval.DefendPartyRadius(1));
    }
    /// <summary>Смена приказа (<c>0x44fcb5</c>): L_ATTACK_STACK → берсерк; L_NORMAL — только знатный/жезлоносец с приказом ИИ
    /// L_NORMAL или приказ ИИ L_STEAL → приказ ИИ L_ASSIST; прочие — L_NORMAL, нейтрал — L_ROAM.</summary>
    [Fact]
    public void ChangeOrder_ByListing()
    {
        Assert.Equal((StackOrder.Bezerk, false), AiTacticOrders.ChangeOrder(StackOrder.AttackStack, StackOrder.Normal, false, false));
        Assert.Equal((StackOrder.Assist, true), AiTacticOrders.ChangeOrder(StackOrder.Normal, StackOrder.Normal, true, false));
        Assert.Null(AiTacticOrders.ChangeOrder(StackOrder.Normal, StackOrder.Normal, false, false));
        Assert.Equal((StackOrder.Assist, true), AiTacticOrders.ChangeOrder(StackOrder.Normal, StackOrder.Steal, false, false));
        Assert.Null(AiTacticOrders.ChangeOrder(StackOrder.Normal, StackOrder.AttackStack, true, false));
        Assert.Equal((StackOrder.Normal, false), AiTacticOrders.ChangeOrder(StackOrder.DefendStack, StackOrder.Normal, false, false));
        Assert.Equal((StackOrder.Roam, false), AiTacticOrders.ChangeOrder(StackOrder.Guard, StackOrder.Normal, false, true));
    }
}
