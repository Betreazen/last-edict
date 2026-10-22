using D2.Data;
using D2.Data.Battle;
using D2.Rules.Battle;
using D2.Rules.Rng;

/// <summary>Правила боя, найденные разбором пропусков С2 (16.10.2026): раунд лекарей после гибели стороны, внеочередной ход дара
/// атаки, изгнание призванного в начале хода, место призыва ИИ, сила бегства через цепочку и решения человека в
/// <see cref="BattleSim.Run"/>. Юниты и атаки — ванильные (<c>Gunits</c>, <c>Gattacks</c>); корпусные доказательства — в
/// <c>C2HarnessTests</c> и в описаниях методов модели.</summary>
public class BattleEndAndTurnRulesTests
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));
    private static RulesConstants C => Rules.Value.Constants;

    private const string Squire = "g000uu0001";        // L1, hp 100, Damage
    private const string OracleElf = "g000uu5006";     // L1 площадной лекарь (Heal), hp 125
    private const string Occultist = "g000uu6013";     // L1 Summon
    private const string Alchemist = "g000uu0161";     // L3 GiveAttack
    private const string RuneBlade = "g000um2301";     // MOD_EQUIP «Рунный клинок»: +10 % урона

    private static BattleUnit Of(string typeId, string id, bool attacker, int slot)
    {
        var type = Rules.Value.Units[typeId];
        return new BattleUnit
        {
            Id = id, Type = type, Attack = Rules.Value.AttackOf(type)!, Attack2 = Rules.Value.Attack2Of(type),
            Attacker = attacker, Slot = slot, HpMax = type.HitPoints, Hp = type.HitPoints,
        };
    }

    private static BattleState State(params BattleUnit[] units) =>
        new() { Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Rules = Rules.Value };

    /// <summary>Сторона пала: следующее построение очереди тратит броски на всех на поле, а ходят в нём только лекари победителя с
    /// раненым своим; яды снимаются со всех; ещё одно построение — бой окончен (20260906-b [52975], 20260918-probe65 [236010]).</summary>
    [SkippableFact] public void HealerRound_OnlyHealersWithWoundedAlly_ThenNextRoundEndsTheBattle()
    {
        ImportedData.RequireRules();
        var squire = Of(Squire, "s", attacker: true, slot: 0);
        var oracle = Of(OracleElf, "o", attacker: true, slot: 1);
        var enemy = Of(Squire, "e", attacker: false, slot: 0);
        enemy.Hp = 0;
        squire.Hp = 40;
        squire.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = true });
        var rng = new ScriptedRng([0, 0, 0, 0]);
        var sim = new BattleSim(State(squire, oracle, enemy), rng, C);

        var queue = sim.BeginRound(2);

        Assert.Equal(["o"], queue.Select(u => u.Id));
        Assert.Equal(2, rng.Used);                 // броски очереди — на обоих живых
        Assert.Empty(squire.Dots);                 // 0x627142: яды сняты
        Assert.False(sim.Finished);
        sim.BeginRound(3);
        Assert.True(sim.Finished);
        Assert.Equal(4, rng.Used);
    }

    /// <summary>Раненых нет — лекарям ходить незачем: броски нового раунда потрачены, бой окончен (20260909-arena2 [313563]).</summary>
    [SkippableFact] public void HealerRound_NobodyWounded_EndsTheBattleRightAway()
    {
        ImportedData.RequireRules();
        var enemy = Of(Squire, "e", attacker: false, slot: 0);
        enemy.Hp = 0;
        var rng = new ScriptedRng([0, 0]);
        var sim = new BattleSim(State(Of(Squire, "s", true, 0), Of(OracleElf, "o", true, 1), enemy), rng, C);

        Assert.Empty(sim.BeginRound(2));
        Assert.True(sim.Finished);
        Assert.Equal(2, rng.Used);
    }

    /// <summary>Внеочередной ход дара атаки — полный ход: начало хода (тик яда) идёт заново, хотя юнит уже ходил в раунде
    /// (20261016-port-synth [502965]).</summary>
    [SkippableFact] public void GiveAttack_ExtraTurnTicksDotsAgain()
    {
        ImportedData.RequireRules();
        var target = Of(Squire, "t", attacker: true, slot: 0);
        var alchemist = Of(Alchemist, "al", attacker: true, slot: 1);
        target.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = false });
        var state = State(target, alchemist, Of(Squire, "e", attacker: false, slot: 0));
        state.Round = 1;
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        Assert.True(sim.BeginTurn(target));
        Assert.Equal(90, target.Hp);

        sim.Act(1, alchemist, target);             // GiveAttack: ход сразу за ходящим
        Assert.True(target.ExtraTurn);
        target.Dots.Add(new DotEffect { Class = AttackClass.Poison, Damage = 10, AppliedRound = 1, Long = false });
        Assert.True(sim.BeginTurn(target));
        Assert.Equal(80, target.Hp);
    }

    /// <summary>Призванный, потерявший призывателя, бросает инициативу нового раунда и изгоняется лишь в начале своего хода; если
    /// этим пала сторона — бой окончен без нового раунда (20261014-replay [245844], 20260922-ui [379898]).</summary>
    [SkippableFact] public void OrphanSummon_RollsInitiative_ThenUnsummonEndsBattleWithoutNewRound()
    {
        ImportedData.RequireRules();
        var summoner = Of(Occultist, "sum", attacker: false, slot: 1);
        summoner.Hp = 0;
        var summoned = Of(Squire, "x", attacker: false, slot: 0);
        summoned.Summoned = true;
        summoned.SummonerId = "sum";
        var rng = new ScriptedRng([0, 0]);
        var sim = new BattleSim(State(Of(Squire, "a", true, 0), summoner, summoned), rng, C);

        var queue = sim.BeginRound(3);
        Assert.Contains(summoned, queue);
        Assert.Equal(2, rng.Used);
        Assert.Null(sim.Winner);

        Assert.False(sim.BeginTurn(summoned));
        Assert.True(sim.Finished);
        Assert.Equal("A", sim.Winner);
    }

    /// <summary>Место призыва ИИ (<c>0x5d1079</c>): свободные позиции по возрастанию, старт — бросок, от него по кругу первая
    /// чётная (первый ряд); 20261014-replay [524286] seq 525076: места 0, 2, 3, 5, бросок 2 → 0.</summary>
    [SkippableTheory]
    [InlineData(2, 0)]   // 3 → 5 → 0
    [InlineData(1, 2)]
    [InlineData(0, 0)]
    public void AiSummon_TakesFirstFrontPositionFromRolledStart(int roll, int slot)
    {
        ImportedData.RequireRules();
        var occultist = Of(Occultist, "occ", attacker: false, slot: 1);
        var state = State(Of(Squire, "a", true, 0), occultist, Of(Squire, "d4", attacker: false, slot: 4));
        state.Round = 1;

        var decision = BattleAi.Choose(state, occultist, new ScriptedRng([roll]), C);

        Assert.Equal(AiAction.Attack, decision.Action);
        Assert.Equal(slot, decision.SummonSlot);
        Assert.Null(state.SummonSlot);             // решение состояние не трогает — позицию кладёт исполнитель
    }

    /// <summary>Сила бегства в бою читает юнита через цепочку модификаторов, как ИИ на карте: «Рунный клинок» +10 % урона растит
    /// силу вместе с уроном (20260919-human [12429316]: 64,152 → 70,567, как ai_strength движка).</summary>
    [SkippableFact] public void Strength_ReadsUnitThroughModifierChain()
    {
        ImportedData.RequireRules();
        Skip.If(Rules.Value.Modifiers?.ContainsKey(RuneBlade) != true, "нет модификатора");
        var plain = Of(Squire, "p", attacker: true, slot: 0);
        var type = Rules.Value.Units[Squire];
        var armed = new BattleUnit
        {
            Id = "r", Type = type, Attack = plain.Attack, Attacker = true, Slot = 1, HpMax = 100, Hp = 100,
            Modifiers = [Rules.Value.Modifiers![RuneBlade]],
        };

        double bare = AiStrength.Unit(AiStrength.Effective(plain), ignoreCity: true, enemies: 1, C);
        double withBlade = AiStrength.Unit(AiStrength.Effective(armed), ignoreCity: true, enemies: 1, C);

        int damage = D2.Rules.Units.EffectiveStats.OfAttack(plain.Attack, armed.Chain).Attack.QtyDamage;   // 25 → 27 (+10 %, целое)
        Assert.True(damage > plain.Attack.QtyDamage);
        Assert.Equal(bare * damage / plain.Attack.QtyDamage, withBlade, 6);
        Assert.Same(plain, AiStrength.Effective(plain));   // без цепочки — тот же юнит
    }

    /// <summary>Решение человека в <see cref="BattleSim.Run"/> исполняется вместо ИИ и бросков решения не тратит; оно же — в
    /// <see cref="BattleSim.Choices"/>.</summary>
    [SkippableFact] public void Run_ExecutesHumanChoiceInsteadOfAi()
    {
        ImportedData.RequireRules();
        var human = Of(Squire, "h", attacker: true, slot: 0);
        var enemy = Of(Squire, "e", attacker: false, slot: 0);
        enemy.Hp = 1;
        // Раунд 1: две очереди (по 2 броска); человек защищается; враг бьёт (2 + 1); раунд 2: очередь; человек бьёт и убивает.
        var rng = new ScriptedRng([9, 0, 9, 0, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0]);
        var sim = new BattleSim(State(human, enemy), rng, C);
        int calls = 0;
        sim.Run(maxRounds: 2, choose: u => u == human && calls++ == 0 ? new TurnChoice(TurnChoiceKind.Defend) : null);

        Assert.Equal(new ChoiceNote(1, "h", TurnChoiceKind.Defend), sim.Choices[0]);
        Assert.Equal(TurnChoiceKind.Attack, sim.Choices[1].Kind);
        Assert.Equal("A", sim.Winner);
    }
}
