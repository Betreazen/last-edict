using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Tests;

/// <summary>Ауры юнитов мода (Студия 2, задача 3): модификатор от живого юнита своему отряду, соседям, врагу или себе — вживую в цепочке
/// цели; погиб источник — ауры нет. Мод собирается Студией на test-pack.</summary>
public sealed class AuraTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-aura-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private GameRules Rules()
    {
        var session = new StudioSession(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null);
        var units = new UnitWorkspace(session);
        Assert.True(units.CreateFrom("test:unit/sun-footman", "banner", "Знаменосец"));
        Assert.True(session.Do(new SetField("towers:unit/banner", "auras", JsonNode.Parse(
            """[{"modifier":"test:modifier/sun-ward","reach":"adjacent"},{"modifier":"test:modifier/moon-ward","reach":"enemies"}]"""))));
        Assert.Equal(0, session.Report.Errors);
        return RulesLoader.Load(session.Report.Content!);
    }

    private static BattleUnit Unit(GameRules rules, string id, string type, bool attacker, int slot)
    {
        var t = rules.Units[type];
        return new BattleUnit { Id = id, Type = t, Attack = rules.Attacks[t.AttackId], Attacker = attacker, Slot = slot, HpMax = t.HitPoints, Hp = t.HitPoints };
    }

    [Fact]
    public void Aura_GoesToNeighboursAndEnemies_LiveInTheChain_AndLeavesWithTheSource()
    {
        var rules = Rules();
        Assert.Equal(2, rules.Units["towers:unit/banner"].Auras.Count);
        Assert.Empty(rules.Units["test:unit/sun-footman"].Auras);
        var banner = Unit(rules, "B", "towers:unit/banner", attacker: true, slot: 0);
        var near = Unit(rules, "N", "test:unit/sun-footman", attacker: true, slot: 2);
        var far = Unit(rules, "F", "test:unit/sun-footman", attacker: true, slot: 5);
        var enemy = Unit(rules, "E", "test:unit/moon-footman", attacker: false, slot: 0);
        var state = new BattleState { Units = [banner, near, far, enemy], AttackerGroup = "A", DefenderGroup = "D", Rules = rules };
        int armor = EffectiveStats.Of(near.Type, near.Chain).Armor;
        Auras.Refresh(state);
        Assert.Equal(["test:modifier/sun-ward"], near.Auras.Select(m => m.Id));
        Assert.Empty(far.Auras);      // не сосед (другая колонка и ряд)
        Assert.Empty(banner.Auras);   // «соседи» — не сам
        Assert.Equal(["test:modifier/moon-ward"], enemy.Auras.Select(m => m.Id));
        Assert.Equal(armor + 20, EffectiveStats.Of(near.Type, near.Chain).Armor);
        // Источник пал — аур нет.
        banner.Hp = 0;
        Auras.Refresh(state);
        Assert.Empty(near.Auras);
        Assert.Empty(enemy.Auras);
        Assert.Equal(armor, EffectiveStats.Of(near.Type, near.Chain).Armor);
    }

    [Fact]
    public void BattleWithAuras_Differs_BattleWithoutThem_IsTheSame()
    {
        var rules = Rules();
        IReadOnlyList<TurnRecord> Fight(string leader)
        {
            var state = new BattleState
            {
                Units = [Unit(rules, "A0", leader, true, 0), Unit(rules, "A2", "test:unit/sun-footman", true, 2),
                         Unit(rules, "D0", "test:unit/moon-footman", false, 0), Unit(rules, "D2", "test:unit/moon-footman", false, 2)],
                AttackerGroup = "A", DefenderGroup = "D", Rules = rules,
            };
            return new BattleSim(state, new EngineRng(7), rules.Constants).Run();
        }
        var plain = Fight("test:unit/sun-footman");
        Assert.Equal(plain, Fight("test:unit/sun-footman"));   // без аур — бой как был, повторяемо
        Assert.NotEqual(plain, Fight("towers:unit/banner"));   // знаменосец: свой отряд крепче, враг — тоже (ауры обеим сторонам)
    }
}
