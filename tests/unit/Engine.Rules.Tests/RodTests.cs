using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>
/// Команды жезла (<see cref="Rods"/>) на синтетике — без корпуса, без справочников и без игры.
/// Проверяется каждая калитка постановки поимённо (их семь, все ранга 1 по дизассемблеру — см.
/// описание <see cref="Rods"/>), цена, трата хода, отсутствие штрафа за свой жезл и запрет
/// «своим заклинанием свой жезл». Корпус те же правила мерит на сохранениях
/// (<see cref="RodHarnessTests"/>), но отказов в нём нет ни одного — калитки проверяются только здесь.
/// </summary>
public class RodTests
{
    private const string Me = "S001PL0000";
    private const string Foe = "S001PL0001";
    private static readonly Bank Rich = new(1000, 0, 0, 0, 0, 0);
    private static readonly TreasuryRules Rules =
        new(new Dictionary<string, UnitCosts>(), new Bank(150, 0, 0, 0, 0, 0),
            new Dictionary<string, int>());
    private static readonly int[] WithRod = [Rods.LeaderAbilityRod];

    [Fact] public void Place_PaysRodCost_AndEatsTheWholeTurn()
    {
        var stack = Stack(Me, move: 17);
        var done = Rods.Place(Map(), stack, "S001RD0000", 5, 5, Rich, Rules, WithRod);

        Assert.True(done.Done);
        Assert.Equal(new Bank(850, 0, 0, 0, 0, 0), done.Purse);
        Assert.Equal(Movement.WholeTurnSpent, done.MoveLeft);
        Assert.Equal(new MapRod("S001RD0000", Me, 5, 5), done.Placed);
        Assert.False(done.Blamed);
    }

    [Fact] public void Place_Refused_LeavesPurseAndMoveAlone()
    {
        var stack = Stack(Me, move: 17);
        var no = Rods.Place(Map(), stack, "S001RD0000", 5, 5, Rich, Rules, leaderAbilities: []);

        Assert.False(no.Done);
        Assert.Contains("L_ROD", no.Refused);
        Assert.Equal(Rich, no.Purse);
        Assert.Equal(17, no.MoveLeft);
        Assert.Null(no.Placed);
    }

    [Fact] public void Place_NeedsPlainGround()
    {
        Assert.Contains("равнину", Refusal(Map(forest: (5, 5)), 5, 5)!);
        Assert.Null(Refusal(Map(forest: (6, 5)), 5, 5));       // лес рядом не мешает
    }

    [Fact] public void Place_NeedsAnEmptyCell_ButLocationDoesNotCount()
    {
        Assert.Contains("S001KC0007", Refusal(Map(plan: ((5, 5), "S001KC0007")), 5, 5)!);
        Assert.Null(Refusal(Map(plan: ((5, 5), "S001LO0003")), 5, 5));
    }

    /// <summary>Укрепление — коробка 3×3: Чебышёв 1 отказ, 2 уже можно.</summary>
    [Fact] public void Place_KeepsOneCellFromForts()
    {
        Assert.Contains("S001FT0000", Refusal(Map(plan: ((6, 6), "S001FT0000")), 5, 5)!);
        Assert.Null(Refusal(Map(plan: ((7, 7), "S001FT0000")), 5, 5));
    }

    /// <summary>Жезл — коробка 5×5: Чебышёв 2 отказ, 3 уже можно, и чужой жезл тоже мешает.</summary>
    [Fact] public void Place_KeepsTwoCellsFromOtherRods()
    {
        Assert.Contains("S001RD0009", Refusal(Map(plan: ((7, 7), "S001RD0009")), 5, 5)!);
        Assert.Null(Refusal(Map(plan: ((8, 8), "S001RD0009")), 5, 5));
        Assert.Contains("S001RD0009", Refusal(Map(plan: ((5, 3), "S001RD0009")), 5, 5)!);
    }

    /// <summary>ai-q27 (ранг 1): соседний жезл (код 9) движок называет раньше грунта (7) и объекта —
    /// 7 строк из 7.</summary>
    [Fact] public void Place_NearbyRodIsReportedBeforeGround()
    {
        var refusal = Refusal(Map(forest: (5, 5), plan: ((6, 6), "S001RD0009")), 5, 5)!;
        Assert.Contains("S001RD0009", refusal);
        Assert.DoesNotContain("равнину", refusal);
    }

    [Fact] public void Place_RefusedOnAllyLand_AndOnlyWhenAsked()
    {
        var map = Map();
        Assert.Contains("союзника", Rods.PlaceRefusal(map, 5, 5, Rich, Rules, WithRod, _ => true)!);
        Assert.Null(Rods.PlaceRefusal(map, 5, 5, Rich, Rules, WithRod, _ => false));
        Assert.Null(Rods.PlaceRefusal(map, 5, 5, Rich, Rules, WithRod));
    }

    [Fact] public void Place_RefusedWhenTreasuryIsShort_AndOutsideTheMap()
    {
        Assert.Contains("казны не хватает", Rods.PlaceRefusal(Map(), 5, 5, new Bank(149, 0, 0, 0, 0, 0),
                                                              Rules, WithRod)!);
        Assert.Contains("вне карты", Rods.PlaceRefusal(Map(), 11, 5, Rich, Rules, WithRod)!);
    }

    /// <summary>Снос: ход съеден целиком, казна не тронута, штраф только за чужой жезл.</summary>
    [Fact] public void Break_CostsTheWholeTurnAndNoMoney()
    {
        var stack = Stack(Me, move: 20);
        var own = Rods.Break(stack, new MapRod("S001RD0000", Me, 5, 5), Rich, WithRod);
        Assert.Equal(Movement.WholeTurnSpent, own.MoveLeft);
        Assert.Equal(Rich, own.Purse);
        Assert.Equal("S001RD0000", own.Removed);
        Assert.False(own.Blamed);

        var theirs = Rods.Break(stack, new MapRod("S001RD0001", Foe, 5, 5), Rich, WithRod);
        Assert.True(theirs.Blamed);                 // источник D_ROD −15, значение — задача 13
        Assert.Equal(Rich, theirs.Purse);
    }

    /// <summary>§3 п. 28: <c>0x5d9eea</c> — внутри укрепления, жезл союзника, лидер без <c>L_ROD</c>.
    /// Отказ ничего не тратит: <c>0x5d9fab</c> выходит до траты хода и штрафа.</summary>
    [Fact] public void Break_NeedsRodAbility_AndRefusesInsideFortOrAlliedRod()
    {
        var foeRod = new MapRod("S001RD0001", Foe, 5, 5);
        var plain = Stack(Me, move: 20);

        var noAbility = Rods.Break(plain, foeRod, Rich, leaderAbilities: []);
        Assert.False(noAbility.Done);
        Assert.Contains("L_ROD", noAbility.Refused);
        Assert.Equal(20, noAbility.MoveLeft);
        Assert.Equal("", noAbility.Removed);
        Assert.False(noAbility.Blamed);

        var inside = new MapStack
        {
            Id = plain.Id, Owner = plain.Owner, Subrace = "", LeaderId = plain.LeaderId, LeaderAlive = true,
            X = 5, Y = 6, Move = 20, Inside = "S001FT0000", UnitIds = [], Slots = [],
        };
        Assert.Contains("внутри укрепления", Rods.Break(inside, foeRod, Rich, WithRod).Refused);
        Assert.Contains("союзник", Rods.Break(plain, foeRod, Rich, WithRod, rodOwnerIsAlly: true).Refused);
        // свой жезл союзником не считается (0x5f7ebd: тот же игрок -> false)
        Assert.True(Rods.Break(plain, new MapRod("S001RD0000", Me, 5, 5), Rich, WithRod, rodOwnerIsAlly: true).Done);
    }

    [Fact] public void Sinkhole_RefusesOwnRod_WithoutSpendingMana()
    {
        var mana = new Bank(0, 0, 200, 200, 200, 500);
        var no = SpellCast.RemoveRod(Sinkhole(), mana, new MapRod("S001RD0000", Me, 5, 5), Me);

        Assert.False(no.Done);
        Assert.Equal(mana, no.Purse);               // мана не убыла — ровно то, что видно в корпусе
        Assert.Equal("", no.Removed);
    }

    [Fact] public void Sinkhole_TakesForeignRod_WithoutRelationPenalty()
    {
        var mana = new Bank(0, 0, 200, 200, 200, 500);
        var done = SpellCast.RemoveRod(Sinkhole(), mana, new MapRod("S001RD0001", Foe, 5, 5), Me);

        Assert.True(done.Done);
        Assert.Equal("S001RD0001", done.Removed);
        Assert.False(done.Blamed);                   // через заклинание виновника нет — штрафа нет
        Assert.Equal(new Bank(0, 0, 120, 140, 90, 250), done.Purse);
    }

    [Fact] public void RemoveRod_ThrowsOnAnotherCategory()
    {
        var heal = Sinkhole() with { Category = SpellCategory.Heal };
        Assert.Throws<ArgumentException>(() =>
            SpellCast.RemoveRod(heal, Rich, new MapRod("S001RD0001", Foe, 5, 5), Me));
    }

    // ------------------------------------------------------------------ синтетика

    private static string? Refusal(MapState map, int x, int y) =>
        Rods.PlaceRefusal(map, x, y, Rich, Rules, WithRod);

    /// <summary>«Sinkhole» в объёме, который нужен снятию жезла: категория и цена применения.</summary>
    private static Spell Sinkhole() => new("g000ss0119", SpellCategory.RemoveRod, 5,
        new Bank(0, 0, 80, 60, 110, 250), Bank.Zero, 1, "", "", 0, -1, 0, 0, -1, []);

    private static MapStack Stack(string owner, int move) => new()
    {
        Id = "S001KC0000", Owner = owner, Subrace = "", LeaderId = "S001UN0000", LeaderAlive = true,
        X = 5, Y = 6, Move = move, Inside = "", UnitIds = [], Slots = [],
    };

    /// <summary>Карта 10×10 из равнины своей земли; <paramref name="forest"/> — одна лесная клетка,
    /// <paramref name="plan"/> — один объект в занятости карты.</summary>
    private static MapState Map((int X, int Y)? forest = null,
                                ((int X, int Y) At, string Id)? plan = null)
    {
        const int size = 10;
        var tiles = new Tile[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                var ground = forest is { } f && f.X == x && f.Y == y ? Ground.Forest : Ground.Plain;
                tiles[x, y] = new Tile((byte)(((int)ground << 3) | 1));
            }
        var occupied = new Dictionary<(int X, int Y), IReadOnlyList<string>>();
        if (plan is { } p) occupied[p.At] = [p.Id];
        return new MapState
        {
            Size = size, Tiles = tiles, Turn = 1,
            Stacks = new Dictionary<string, MapStack>(), Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(), Objects = new Dictionary<string, MapObject>(),
            Plan = occupied, Fog = new Dictionary<string, bool[,]>(),
        };
    }
}
