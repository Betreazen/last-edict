using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Призыв рядом с большими юнитами (партия 17.10, бой у города орков: тёмный эльф Лиф призвал злого духа во вторую клетку тролля).
/// Движок (дизасм 17.10): цели призыва — позиции группы, где <c>canPerform</c> призыва (<c>0x662f80</c>) истинен — пусто, павший или
/// ушедший; большой держит обе клетки (его id в обеих позициях группы). Кого призвать — <c>0x6635dc</c>: пара позиции годна — сначала
/// большие кандидаты, большой встаёт на чётную клетку; один кандидат — без <c>rand()</c> (<c>0x660aed</c>).
/// </summary>
public class SummonBigUnitTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    private const string Lif = "base:unit/dark-elf-lyf-neutral";          // Тёмный эльф Лиф, малый, призыв g000aa6007 (REACH «все»)
    private const string LifSummon = "base:attack/dark-elf-lyf-neutral";    // кандидаты: Гигантский чёрный паук (большой), Злой дух, Скелет-воин, Зомби
    private const string Spider = "base:unit/giant-black-spider-neutral";       // Гигантский чёрный паук — единственный большой кандидат
    private const string Troll = "base:unit/troll-neutral";        // Тролль, большой
    private const string Orc = "base:unit/orc-champion-neutral";          // Орк-багатур, малый
    private const string Occultist = "base:unit/occultist-neutral";    // призыв g000aa6013 — один кандидат, малый
    private const string Squire = "base:unit/squire-empire";

    private static RulesConstants C => Rules.Value.Constants;

    private static BattleUnit Of(string typeId, int slot, string id, bool attacker = false)
    {
        var type = Rules.Value.Units[typeId];
        return new BattleUnit
        {
            Id = id, Type = type, Attack = Rules.Value.AttackOf(type)!, Attack2 = Rules.Value.Attack2Of(type),
            Attacker = attacker, Slot = slot, HpMax = type.HitPoints, Hp = type.HitPoints,
        };
    }

    private static BattleState State(params BattleUnit[] units) => new()
    {
        Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Round = 1, Rules = Rules.Value,
    };

    [SkippableFact] public void VanillaData_IsWhatTheTestsAssume()
    {
        ImportedData.RequireRules();
        Assert.False(Rules.Value.Units[Troll].SizeSmall);
        Assert.True(Rules.Value.Units[Lif].SizeSmall);
        Assert.Equal(AttackClass.Summon, Rules.Value.AttackOf(Rules.Value.Units[Lif])!.Class);
        Assert.Equal([Spider], Rules.Value.SummonCandidates(LifSummon, smallSlot: false).Select(t => t.Id));
        Assert.Equal(3, Rules.Value.SummonCandidates(LifSummon, smallSlot: true).Count);
    }

    /// <summary>Живой большой держит обе клетки; павший и ушедший — ни одной (на клетках павшего и ушедшего призыв годен, <c>0x663151</c>,
    /// <c>0x663180</c>); скрытый (дуэль вора) — держит.</summary>
    [SkippableTheory]
    [InlineData("alive", new[] { 1, 4, 5 })]
    [InlineData("dead", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("retreated", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("hidden", new[] { 1, 4, 5 })]
    public void SummonCells_BigUnitHoldsBothCellsWhileOnPosition(string trollState, int[] cells)
    {
        ImportedData.RequireRules();
        var lif = Of(Lif, 0, "lif");
        var troll = Of(Troll, 2, "troll");
        switch (trollState)
        {
            case "dead": troll.Hp = 0; break;
            case "retreated": troll.Retreated = true; break;
            case "hidden": troll.Hidden = true; break;
        }
        var state = State(lif, troll, Of(Squire, 0, "enemy", attacker: true));

        Assert.Equal(cells, state.SummonCells(lif));
        Assert.True(troll.Covers(2) && troll.Covers(3) && !troll.Covers(4));
    }

    /// <summary>Бой у города орков: Лиф, орк и два тролля — отряд полон, призывать некуда (прежде модель ставила призванных во вторые клетки
    /// троллей — у ИИ их было четверо). ИИ места не выбирает, призыв без места не идёт.</summary>
    [SkippableFact] public void FullGroupWithTwoTrolls_HasNoSummonPlace()
    {
        ImportedData.RequireRules();
        var lif = Of(Lif, 0, "lif");
        var state = State(lif, Of(Orc, 1, "orc"), Of(Troll, 2, "troll1"), Of(Troll, 4, "troll2"), Of(Squire, 0, "enemy", attacker: true));

        Assert.Empty(state.SummonCells(lif));
        var decision = BattleAi.Choose(state, lif, new ScriptedRng([0, 0, 0, 0]), C);
        output.WriteLine($"ИИ: {decision.Action} цель={decision.Target?.Id ?? "-"} место={decision.SummonSlot}");
        Assert.Equal(AiAction.Defend, decision.Action);   // целей нет — Attack в наборе действий нет (0x623d93), ИИ защищается
        Assert.Equal(-1, decision.SummonSlot);

        state.SummonSlot = 3;   // и навязанное место во второй клетке тролля не годно
        var r = Attacks.Apply(lif, lif.Attack, lif, new ScriptedRng([]), C, round: 1, state);
        Assert.False(r.Applied);
        Assert.DoesNotContain(state.Units, u => u.Summoned);
    }

    /// <summary>ИИ (<c>0x5d1079</c>) берёт место из целей призыва: вторая клетка тролля в них не входит. Орки на 0 и 4, Оккультист на 1,
    /// тролль на 2: место одно — 5 (прежде было ещё 3, и бросок 0 ставил призванного в тролля: чётных мест нет — берётся стартовое).</summary>
    [SkippableFact] public void AiSummon_NeverPicksTrollCell()
    {
        ImportedData.RequireRules();
        var occultist = Of(Occultist, 1, "occ");
        var state = State(Of(Orc, 0, "orc1"), occultist, Of(Troll, 2, "troll"), Of(Orc, 4, "orc2"), Of(Squire, 0, "enemy", attacker: true));

        var decision = BattleAi.Choose(state, occultist, new ScriptedRng([0]), C);

        Assert.Equal([5], state.SummonCells(occultist));
        Assert.Equal(5, decision.SummonSlot);
    }

    /// <summary>Пара позиции годна — призывается большой (<c>0x6635dc</c>: большие кандидаты раньше малых) и встаёт на чётную клетку пары
    /// (<c>0x6633cd…0x6633e6</c>); один большой кандидат — без броска процесса (<c>0x660aed</c>).</summary>
    [SkippableTheory]
    [InlineData(5, 4)]
    [InlineData(4, 4)]
    public void Summon_PairFree_SummonsBigOnEvenCell(int chosen, int placed)
    {
        ImportedData.RequireRules();
        var lif = Of(Lif, 0, "lif");
        var state = State(lif, Of(Troll, 2, "troll"), Of(Squire, 0, "enemy", attacker: true));
        state.ProcessRand = new CrtRand(7);
        var mirror = new CrtRand(7);
        state.SummonSlot = chosen;

        var r = Attacks.Apply(lif, lif.Attack, lif, new ScriptedRng([]), C, round: 1, state);

        Assert.True(r.Applied);
        var summoned = Assert.Single(state.Units, u => u.Summoned);
        Assert.Equal(Spider, summoned.Type.Id);
        Assert.Equal(placed, summoned.Slot);
        Assert.Equal(mirror.State, state.ProcessRand.State);   // один кандидат — rand() не зовётся
        Assert.Empty(state.SummonCells(lif).Intersect([4, 5]));
    }

    /// <summary>Пара занята живым — большого не будет: малый из трёх кандидатов по потоку CRT партии (<c>rand() % 3</c>), на выбранной клетке.</summary>
    [SkippableFact] public void Summon_PairHeld_SummonsSmallByProcessRand()
    {
        ImportedData.RequireRules();
        var lif = Of(Lif, 0, "lif");
        var state = State(lif, Of(Troll, 2, "troll"), Of(Squire, 0, "enemy", attacker: true));
        state.ProcessRand = new CrtRand(12345);
        var mirror = new CrtRand(12345);
        var small = Rules.Value.SummonCandidates(LifSummon, smallSlot: true);
        state.SummonSlot = 1;   // пара — клетка 0, на ней сам Лиф

        Attacks.Apply(lif, lif.Attack, lif, new ScriptedRng([]), C, round: 1, state);

        var summoned = Assert.Single(state.Units, u => u.Summoned);
        Assert.Equal(1, summoned.Slot);
        Assert.True(summoned.Type.SizeSmall);
        Assert.Equal(small[mirror.Next() % small.Count].Id, summoned.Type.Id);
        Assert.Equal(mirror.State, state.ProcessRand.State);
    }
}
