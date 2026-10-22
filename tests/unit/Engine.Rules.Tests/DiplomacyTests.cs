using LastEdict.Rules.Map;

namespace LastEdict.Rules.Tests;

/// <summary>
/// Дипломатия (<see cref="Diplomacy"/>) на синтетике — без корпуса, без справочников и без игры.
/// Каждая калитка и каждая из девяти причин мутатора <c>changeRelations</c> ранга 1 проверяется
/// поимённо: корпус даёт только шесть вызовов (атака при мире, атака отряда, атака укрепления,
/// снос жезла, действие вора, дань), а союз, отказ от союза, обмен, вечную войну, запрет атаки
/// союзника и запрет жезла на его земле в корпусе не наблюдал никто (план §3 п. 9, 27).
/// Числа на журнале мерит <see cref="DiplomacyHarnessTests"/>.
/// </summary>
public class DiplomacyTests
{
    private static readonly DiplomacyRules Rules = DiplomacyRules.Vanilla;

    /// <summary>Империя (0), Кланы (3), нейтралы (4) — расстановка карты <c>m15-diplo</c>.</summary>
    private const int Empire = 0, Clans = 3, Neutrals = 4;
    private static readonly int[] Players = [Neutrals, Empire, Clans];

    /// <summary>Слово <c>RELATION</c>: текущее в битах 1…7, прошлое в 8…14, ход союза в 15…29.</summary>
    private static uint Word(int current, bool ally = false, int allianceTurn = 0, int previous = 0,
                            bool eternalWar = false) =>
        (ally ? 1u : 0u) | ((uint)current << 1) | ((uint)previous << 8)
        | ((uint)allianceTurn << 15) | (eternalWar ? 1u << 30 : 0u);

    /// <summary>Карта <c>m15-diplo</c>: пара 0-3 на 75, обе пары с нейтралами на нуле (война).</summary>
    private static List<Relation> M15(int pair = 75, bool ally = false, int allianceTurn = 0) =>
    [
        new Relation(Neutrals, Empire, Word(0)),
        new Relation(Neutrals, Clans, Word(0)),
        new Relation(Empire, Clans, Word(pair, ally, allianceTurn)),
    ];

    private static int Cur(IReadOnlyList<Relation> r, int a, int b) => Diplomacy.Current(r, a, b);

    [Fact] public void Change_AddsToTheMainPair_AndGivesHalfToEveryThirdPartyAtWarWithB()
    {
        // Снос чужого жезла m19-diplo2: value −15, пара 0-3 66 → 51, нейтралы воюют с Кланами,
        // поэтому rel(0,4) −= −15/2 = −7 → 0 + 7 = 7 (журнал: changed 4-0 raw 0 → 14).
        var after = Diplomacy.Change(M15(66), Players, Rules, Empire, Clans, Rules.RodDestroyed, false);

        Assert.Equal(51, Cur(after.Relations, Empire, Clans));
        Assert.Equal(7, Cur(after.Relations, Empire, Neutrals));
        Assert.Equal(0, Cur(after.Relations, Clans, Neutrals));     // пара без B слагаемого не получает
        Assert.Equal(2, after.Changed.Count);
    }

    [Fact] public void Change_TruncatesHalfTowardsZero()
    {
        // value −7: −7/2 = −3 (к нулю, не к минус бесконечности) → третьей стороне +3.
        var after = Diplomacy.Change(M15(10), Players, Rules, Empire, Clans, Rules.AttackStack, false);

        Assert.Equal(3, Cur(after.Relations, Empire, Clans));
        Assert.Equal(3, Cur(after.Relations, Empire, Neutrals));
    }

    [Fact] public void Change_ClampsBothEnds_AndDropsUnchangedPairsFromTheList()
    {
        // Атака при мире: присваивание 66, третьей стороне −33 упирается в ноль и в changed не идёт.
        var after = Diplomacy.Change(M15(90), Players, Rules, Empire, Clans, Rules.AttackAtPeace, true);

        Assert.Equal(66, Cur(after.Relations, Empire, Clans));
        Assert.Equal(0, Cur(after.Relations, Empire, Neutrals));
        Assert.Single(after.Changed);

        var up = Diplomacy.Change(M15(98), Players, Rules, Empire, Clans, 50, false);
        Assert.Equal(Diplomacy.Max, Cur(up.Relations, Empire, Clans));
    }

    [Fact] public void Change_TouchesOnlyBits1To7()
    {
        var start = M15(75, ally: true, allianceTurn: 3);
        start[2] = start[2] with { Raw = start[2].Raw | ((uint)75 << 8) };   // прошлое отношение 75

        var after = Diplomacy.Change(start, Players, Rules, Empire, Clans, Rules.BreakAlly, true);
        var pair = after.Relations.First(r => r.Pairs(Empire, Clans));

        Assert.Equal(10, pair.Current);
        Assert.True(pair.Ally);              // бит союза мутатор не гасит — это делает визитор союза
        Assert.Equal(3, pair.AllianceTurn);
        Assert.Equal(75, pair.Previous);     // прошлое пишет только сценарное событие
    }

    [Fact] public void Change_SkipsEternalWarPair_ButThirdPartiesStillGetTheirHalf()
    {
        var start = M15(40);
        start[2] = new Relation(Empire, Clans, Word(40, eternalWar: true));

        var after = Diplomacy.Change(start, Players, Rules, Empire, Clans, Rules.AttackStack, false);

        Assert.Equal(40, Cur(after.Relations, Empire, Clans));       // вечная война — записи нет
        Assert.Equal(3, Cur(after.Relations, Empire, Neutrals));     // цикл идёт дальше
    }

    [Fact] public void Change_SkipsThirdPartyThatIsNotAtWarWithB()
    {
        var start = M15(10);
        start[1] = new Relation(Neutrals, Clans, Word(Rules.War + 1));   // 34 — уже не война

        var after = Diplomacy.Change(start, Players, Rules, Empire, Clans, Rules.AttackStack, false);

        Assert.Equal(3, Cur(after.Relations, Empire, Clans));
        Assert.Equal(0, Cur(after.Relations, Empire, Neutrals));
    }

    /// <summary>§3 п. 48: пары нет — <c>setCurrentRelation 0x5f799c</c> отвечает false и ничего не пишет;
    /// текущее такой пары читается нулём, поэтому третья сторона «воюет» и её пара с A меняется.</summary>
    [Fact] public void Change_OnAMissingPair_CreatesNothing()
    {
        var start = M15().Where(r => !r.Pairs(Empire, Clans)).ToList();
        var after = Diplomacy.Change(start, Players, Rules, Empire, Clans, Rules.AttackStack, false);
        Assert.Equal(2, after.Relations.Count);
        Assert.DoesNotContain(after.Relations, r => r.Pairs(Empire, Clans));
        Assert.DoesNotContain(after.Changed, c => c.Before == uint.MaxValue);
    }

    [Fact] public void Change_OnTheSameRace_DoesNothing()
    {
        var after = Diplomacy.Change(M15(), Players, Rules, Empire, Empire, -50, false);
        Assert.Empty(after.Changed);
    }

    [Fact] public void Attack_ChoosesValueByPeaceAndTargetKind()
    {
        Assert.Equal((66, true), Diplomacy.AttackValue(Rules, 67, BattleTarget.Stack));
        Assert.Equal((-7, false), Diplomacy.AttackValue(Rules, 66, BattleTarget.Stack));
        Assert.Equal((-10, false), Diplomacy.AttackValue(Rules, 66, BattleTarget.Fortification));
        Assert.Equal((0, false), Diplomacy.AttackValue(Rules, 66, BattleTarget.Other));
    }

    [Fact] public void Attack_OnUnplayableRace_DoesNotCallTheMutatorAtAll()
    {
        // Обёртка выходит до вызова: бой с нейтралами не двигает ни пару, ни третьи стороны.
        var after = Diplomacy.Attack(M15(75), Players, Rules, Empire, Neutrals, BattleTarget.Stack,
                                     targetPlayable: false);

        Assert.Empty(after.Changed);
        Assert.Equal(75, Cur(after.Relations, Empire, Clans));
    }

    [Fact] public void Noble_ChangesRelationsOnlyForSuccessfulActionAgainstPlayableStackOrFort()
    {
        Assert.True(Diplomacy.NobleChangesRelations(ThiefTargetKind.Stack, true, true));
        Assert.True(Diplomacy.NobleChangesRelations(ThiefTargetKind.Fortification, true, true));
        Assert.False(Diplomacy.NobleChangesRelations(ThiefTargetKind.Stack, true, false));
        Assert.False(Diplomacy.NobleChangesRelations(ThiefTargetKind.Stack, false, true));
        Assert.False(Diplomacy.NobleChangesRelations(ThiefTargetKind.Ruin, true, true));
        Assert.False(Diplomacy.NobleChangesRelations(ThiefTargetKind.Merchant, true, true));
        Assert.False(Diplomacy.NobleChangesRelations(ThiefTargetKind.MageTower, true, true));

        var after = Diplomacy.NobleAction(M15(66), Players, Rules, Empire, Clans, ThiefTargetKind.Stack,
                                          targetPlayable: true, success: true);
        Assert.Equal(61, Cur(after.Relations, Empire, Clans));
        Assert.Equal(2, Cur(after.Relations, Empire, Neutrals));     // −5/2 = −2 → +2
    }

    [Fact] public void RodDestroyed_OnlyWithABlamedStack()
    {
        Assert.Equal(51, Cur(Diplomacy.RodDestroyed(M15(66), Players, Rules, Empire, Clans, true).Relations,
                             Empire, Clans));
        Assert.Equal(66, Cur(Diplomacy.RodDestroyed(M15(66), Players, Rules, Empire, Clans, false).Relations,
                             Empire, Clans));
    }

    [Fact] public void Tribute_ConvertsGoldWithoutACapOfItsOwn()
    {
        Assert.Equal(24, Diplomacy.TributeValue(Rules, 1200));
        Assert.Equal(104, Diplomacy.TributeValue(Rules, 5200));      // конверсия потолка не знает
        Assert.Equal(0, Diplomacy.TributeValue(Rules, 49));

        var after = Diplomacy.Tribute(M15(44), Players, Rules, Empire, Clans, 5200);
        Assert.Equal(Diplomacy.Max, Cur(after.Relations, Empire, Clans));   // упирается обрезка
    }

    [Fact] public void TradeValue_IsPriceMinusSurchargeOverDGold_ButNeverBelowOnePoint()
    {
        Assert.Equal(24, Diplomacy.TradeValue(Rules, 1400, 200));
        Assert.Equal(0, Diplomacy.TradeValue(Rules, 100, 500));       // max(1, …) / 50 = 0
    }

    [Fact] public void CanMakeAlliance_NeedsPeace_UnlessBothSidesAreHuman()
    {
        Assert.False(Diplomacy.CanMakeAlliance(M15(66), Rules, Empire, Clans, bothHuman: false));
        Assert.True(Diplomacy.CanMakeAlliance(M15(67), Rules, Empire, Clans, bothHuman: false));
        Assert.True(Diplomacy.CanMakeAlliance(M15(0), Rules, Empire, Clans, bothHuman: true));
    }

    [Fact] public void MakeAlliance_SetsTheBit_TheTurn_AndAssignsDMkAlly()
    {
        var after = Diplomacy.MakeAlliance(M15(75), Players, Rules, Empire, Clans, turn: 3);
        var pair = after.Relations.First(r => r.Pairs(Empire, Clans));

        Assert.Equal(100, pair.Current);
        Assert.True(pair.Ally);
        Assert.Equal(3, pair.AllianceTurn);
        Assert.Equal(0, Cur(after.Relations, Empire, Neutrals));     // −50 упёрлось в ноль
    }

    [Fact] public void MakeAlliance_BetweenTwoHumans_OnlyLiftsRelationAboveNeutral()
    {
        var after = Diplomacy.MakeAlliance(M15(20), Players, Rules, Empire, Clans, turn: 7, bothHuman: true);
        Assert.Equal(Rules.Neutral + 1, Cur(after.Relations, Empire, Clans));

        var high = Diplomacy.MakeAlliance(M15(90), Players, Rules, Empire, Clans, turn: 7, bothHuman: true);
        Assert.Equal(90, Cur(high.Relations, Empire, Clans));
    }

    [Fact] public void BreakAlliance_ClearsTheBit_RewritesTheTurn_AndCountsTheBreak()
    {
        var broken = Diplomacy.BreakAlliance(M15(100, ally: true, allianceTurn: 3), Players, Rules,
                                             Empire, Clans, turn: 5, qtyBreaks: 0);
        var pair = broken.Change.Relations.First(r => r.Pairs(Empire, Clans));

        Assert.Equal(10, pair.Current);
        Assert.False(pair.Ally);
        Assert.Equal(5, pair.AllianceTurn);      // ход последней смены состояния, не заключения
        Assert.Equal(1, broken.QtyBreaks);
    }

    /// <summary>Сеттер союза <c>0x5f7a9e</c> собирает слово заново (<c>raw &amp; 0x40007fff</c>): бит 31 «ИИ не разорвёт» := «навсегда»,
    /// у союза и разрыва из дипломатии — 0; «вечная война» (бит 30) остаётся (слияние восьмого круга, пометка сессии В).</summary>
    [Fact] public void AllianceChange_ClearsPermanentBit_KeepsEternalWar()
    {
        var start = M15(100, ally: true, allianceTurn: 3);
        int i = start.FindIndex(r => r.Pairs(Empire, Clans));
        start[i] = start[i] with { Raw = start[i].Raw | (1u << 31) | (1u << 30) };

        var pair = Diplomacy.BreakAlliance(start, Players, Rules, Empire, Clans, turn: 5).Change.Relations.First(r => r.Pairs(Empire, Clans));
        Assert.Equal(0u, pair.Raw & (1u << 31));
        Assert.NotEqual(0u, pair.Raw & (1u << 30));

        var again = Diplomacy.MakeAlliance(start, Players, Rules, Empire, Clans, turn: 6).Relations.First(r => r.Pairs(Empire, Clans));
        Assert.Equal(0u, again.Raw & (1u << 31));
    }

    [Fact] public void RefuseAlliance_AssignsDRefAlly_AndSkipsTwoHumansEntirely()
    {
        Assert.Equal(50, Cur(Diplomacy.RefuseAlliance(M15(75), Players, Rules, Empire, Clans, false).Relations,
                             Empire, Clans));
        Assert.Empty(Diplomacy.RefuseAlliance(M15(75), Players, Rules, Empire, Clans, true).Changed);
    }

    [Fact] public void AttackRefusal_NamesTheAlliance_AndIsSilentOtherwise()
    {
        Assert.Contains("союзника атаковать нельзя",
                        Diplomacy.AttackRefusal(M15(100, ally: true), Empire, Clans)!);
        Assert.Null(Diplomacy.AttackRefusal(M15(100), Empire, Clans));
    }

    [Fact] public void AllyTerrain_RefusesRodOnTheLandOfAnAlly_AndOnlyThere()
    {
        var allied = Diplomacy.AllyTerrain(M15(100, ally: true), Players, Empire);
        Assert.True(allied(LegacyRaces.Terrain(Clans)));
        Assert.False(allied(LegacyRaces.Terrain(Empire)));         // своя земля союзной не бывает
        Assert.False(allied(LegacyRaces.Terrain(Neutrals)));

        var alone = Diplomacy.AllyTerrain(M15(100), Players, Empire);
        Assert.False(alone(LegacyRaces.Terrain(Clans)));
    }

    /// <summary>Задача 8 плюс задача 13: калитка жезла и предикат союзной земли вместе.</summary>
    [Fact] public void Rods_PlaceRefusal_UsesTheAllianceGate()
    {
        var map = new MapState
        {
            Size = 10, Tiles = Tiles(LegacyRaces.Terrain(Clans)), Turn = 1,
            Stacks = new Dictionary<string, MapStack>(), Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(), Objects = new Dictionary<string, MapObject>(),
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(),
            Fog = new Dictionary<string, bool[,]>(),
        };
        var rules = new TreasuryRules(new Dictionary<string, UnitCosts>(), new Bank(150, 0, 0, 0, 0, 0),
                                      new Dictionary<string, int>());
        int[] withRod = [Rods.LeaderAbilityRod];

        Assert.Contains("земля союзника", Rods.PlaceRefusal(map, 5, 5, new Bank(1000, 0, 0, 0, 0, 0), rules,
            withRod, Diplomacy.AllyTerrain(M15(100, ally: true), Players, Empire))!);
        Assert.Null(Rods.PlaceRefusal(map, 5, 5, new Bank(1000, 0, 0, 0, 0, 0), rules, withRod,
            Diplomacy.AllyTerrain(M15(100), Players, Empire)));
    }

    /// <summary>Равнина с террейном расы <paramref name="terrain"/> во всех клетках.</summary>
    private static Tile[,] Tiles(int terrain)
    {
        var tiles = new Tile[10, 10];
        for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                tiles[x, y] = new Tile((byte)terrain);
        return tiles;
    }
}
