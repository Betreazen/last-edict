using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Data.Map;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Rules.Tests;

/// <summary>Правила сложности партии, перенесённые по листингам 16.09.2026 (решение пользователя: лёгкая сложность —
/// режим игры). Числа — из журналов <c>20260914-smoke</c> и <c>20260915-passive</c> (обе партии на лёгкой).</summary>
public class DifficultyRulesTests
{
    /// <summary>Точность ИИ: −15 на лёгкой (журнал 65 при <c>POWER</c> 80), +5 и +10 на трудных, но
    /// <c>Percent</c> вне [−100, 100] не принимается — 100 + 5 остаётся 100.</summary>
    [Fact]
    public void AiPower_ShiftsByDifficulty_WithinPercentRange()
    {
        Assert.Equal(65, Formulas.ShiftPower(80, Formulas.AiPowerShift(Difficulty.Easy)));
        Assert.Equal(80, Formulas.ShiftPower(80, Formulas.AiPowerShift(Difficulty.Average)));
        Assert.Equal(85, Formulas.ShiftPower(80, Formulas.AiPowerShift(Difficulty.Hard)));
        Assert.Equal(90, Formulas.ShiftPower(80, Formulas.AiPowerShift(Difficulty.VeryHard)));
        Assert.Equal(100, Formulas.ShiftPower(100, Formulas.AiPowerShift(Difficulty.Hard)));
    }

    /// <summary>Сдвиг точности доходит до броска промаха: Оруженосец (ваниль) бьёт с точностью <c>POWER</c>; средний
    /// бросок <c>POWER − 10</c> при сдвиге −15 — промах, без сдвига — попадание.</summary>
    [SkippableFact]
    public void AiPower_ShiftReachesTheMissRoll()
    {
        ImportedData.RequireRules();
        var rules = RulesLoader.Load(ImportedData.Content);
        var type = rules.Units["g000uu0001"];
        var attack = rules.AttackOf(type)!;
        BattleUnit Unit(string id, bool attacker) => new()
        {
            Id = id, Type = type, Attack = attack, Attacker = attacker, Slot = 0, HpMax = type.HitPoints, Hp = type.HitPoints,
        };
        var ai = Unit("ai", attacker: true);
        var target = Unit("t", attacker: false);
        BattleState State(int shift) => new()
        {
            Units = [ai, target], AttackerGroup = "a", DefenderGroup = "d", Round = 1, Rules = rules, AttackersPowerShift = shift,
        };
        int roll = attack.Power - 10;
        Assert.True(Attacks.Apply(ai, attack, target, new Rng.ScriptedRng([roll, roll]), rules.Constants, 1, State(-15)).Miss);
        Assert.False(Attacks.Apply(ai, attack, target, new Rng.ScriptedRng([roll, roll, 0]), rules.Constants, 1, State(0)).Miss);
    }

    /// <summary>Опыт группы: человек на лёгкой ⌊acc·1,2⌋ (seq 1118348: 30 → 36, seq 1146410: 7 → 8), пол 1 (ИИ,
    /// acc 0 → 1), Инструктор до надбавки, бонус ИИ только на карте SUGG_LVL ≥ 10 и не на лёгкой.</summary>
    [Fact]
    public void GroupXp_HumanOnEasy_Floor_AndAiBonus()
    {
        var easy = new XpBoost(true, 100);
        Assert.Equal(36, Experience.GroupAmount(30, 0, easy));
        Assert.Equal(8, Experience.GroupAmount(7, 0, easy));
        Assert.Equal(1, Experience.GroupAmount(0, 0, XpBoost.None));
        Assert.Equal(32, Experience.GroupAmount(22, 25, easy));          // 22 + 5 = 27, затем 27 + 27·20/100 = 32
        Assert.Equal(100, Experience.AiXpPercent(Difficulty.Average, 9));
        Assert.Equal(125, Experience.AiXpPercent(Difficulty.Average, 10));
        Assert.Equal(100, Experience.AiXpPercent(Difficulty.Easy, 12));
        Assert.Equal(200, Experience.AiXpPercent(Difficulty.VeryHard, 10));
        Assert.Equal(15, Experience.GroupAmount(10, 0, new XpBoost(false, 150)));
        Assert.Equal(36, Experience.Amount(30, 0, 1000, Experience.UpgradePath.LastTier, boost: easy));
    }

    /// <summary>Ловушка лидера (seq 9491685): 578 + 72 при пороге 650 и несозданном типе следующего уровня — 649,
    /// повышения нет; следующий бой (seq 10016809) доводит до 650 и повышает. Созданный тип урезает строку до
    /// порога и повышает сразу.</summary>
    [Fact]
    public void LeaderTrap_ExactGainStopsOneShortOfTheThreshold()
    {
        Assert.Equal(649, Experience.LeaderXpAfter(578, 72, 650, nextTierCreated: false));
        Assert.Equal(650, Experience.LeaderXpAfter(578, 73, 650, nextTierCreated: false));
        Assert.Equal(650, Experience.LeaderXpAfter(578, 72, 650, nextTierCreated: true));
        Assert.Equal(71, Experience.BattleXp(72, 578, 650, Experience.UpgradePath.Leader));
        Assert.Equal(72, Experience.BattleXp(72, 578, 650, Experience.UpgradePath.Leader, nextTierCreated: true));
        Assert.Equal(6, Experience.Amount(62, 94, 100, Experience.UpgradePath.Leader, nextTierCreated: true));
        Assert.Equal(62, Experience.Amount(62, 94, 100, Experience.UpgradePath.Leader));

        int Next(int level) => level == 2 ? 650 : 1000;
        var trapped = Experience.LevelUpLeader(new LeaderProgress(2, Experience.LeaderXpAfter(578, 72, 650, false), 0), Next, 99);
        Assert.Equal((2, 649), (trapped.Level, trapped.Xp));
        var next = Experience.LevelUpLeader(trapped with { Xp = Experience.LeaderXpAfter(649, 5, 650, false) }, Next, 99);
        Assert.Equal((3, 0), (next.Level, next.Xp));
    }

    /// <summary>Чит ресурсов ИИ: на лёгкой (70 %) — smoke seq 52816 (595/0,48 при доходе 195/0,97 → 536/0,67) и
    /// 20260915 seq 1272608 (494/…169 при 260/…97 → 416/…139); на средней (100 %) — «мана доливается до дохода».</summary>
    [Fact]
    public void AiResourceCheat_ScalesIncomeByDifficulty()
    {
        Bank B(long gold, long life) => new(gold, 0, life, 0, 0, 0);
        Assert.Equal(B(536, 67), Treasury.AiResourceCheat(B(595, 48), B(195, 97), 70));
        Assert.Equal(B(416, 139), Treasury.AiResourceCheat(B(494, 169), B(260, 97), 70));
        Assert.Equal(B(595, 97), Treasury.AiResourceCheat(B(595, 48), B(195, 97), 100));
    }

    /// <summary>Дневной опыт ИИ (<c>0x431d07</c>, отчёт A §3, ai-q57/q02): ⌊XP_NEXT·p/100⌋, у гарнизона укрепления
    /// удваивается процент — 9995 на трудной (4 %) даёт 799, а не 2·399 = 798. На средней (2 %) — 150 → 3, 650 → 13.
    /// Деревня — тоже укрепление (<c>0x473733</c>): ai-q14 seq 9612852, S143UN0070 в S143FT0003, 2550 → 102.</summary>
    [Fact]
    public void AiDailyXp_IsAPercentOfXpNext_DoubledForAnyFortGarrison()
    {
        Assert.Equal(799, Experience.AiDailyXp(9995, 4, fortGarrison: true));
        Assert.Equal(399, Experience.AiDailyXp(9995, 4, fortGarrison: false));
        Assert.Equal(3, Experience.AiDailyXp(150, 2, fortGarrison: false));
        Assert.Equal(13, Experience.AiDailyXp(650, 2, fortGarrison: false));
        Assert.Equal(0, Experience.AiDailyXp(650, 0, fortGarrison: true));
        Assert.Equal(102, Experience.AiDailyXp(2550, 2, fortGarrison: true));
    }

    /// <summary>Лидер на MAX_LEADER (0x6656E6): в бою прибавка 0 и повышения нет; вне боя опыт садится на порог
    /// (на порог − 1 при ровной прибавке), выше не бывает.</summary>
    [Fact]
    public void LeaderAtMaxLevel_GetsNoBattleXp_AndDailyXpStopsAtTheThreshold()
    {
        Assert.Equal(0, Experience.Amount(62, 10, 100, Experience.UpgradePath.Leader, atMaxLevel: true));
        Assert.Equal(0, Experience.BattleXp(62, 10, 100, Experience.UpgradePath.Leader, atMaxLevel: true));
        Assert.Equal(62, Experience.Amount(62, 10, 100, Experience.UpgradePath.LastTier, atMaxLevel: true));
        Assert.Equal(100, Experience.LeaderXpAfter(90, 20, 100, nextTierCreated: false));
        Assert.Equal(99, Experience.LeaderXpAfter(90, 10, 100, nextTierCreated: false));
        var capped = Experience.LevelUpLeader(new LeaderProgress(5, 100, 0), _ => 100, maxLevel: 5);
        Assert.Equal((5, 100, 0), (capped.Level, capped.Xp, capped.PendingSkills));
    }

    [SkippableFact]
    public void AiCheats_FromGai()
    {
        ImportedData.RequireRules();
        var cheats = EconomyLoader.LoadAiCheats(ImportedData.Content);
        Assert.Equal([70, 100, 130, 145], cheats.ResourcePercent);
        Assert.Equal([0, 2, 4, 6], cheats.UnitXpPercent);
        Assert.Equal(70, cheats.Resource(Difficulty.Easy));
        Assert.Equal(4, cheats.UnitXp(Difficulty.Hard));
    }
}
