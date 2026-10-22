using LastEdict.Data.Battle;
using LastEdict.Data.Gfx;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Tests;

/// <summary>Надписи удара на портретах (<see cref="StrikeNotes"/>, <c>0x63b391</c>): защита раньше промаха, промах только первой атаки,
/// строка класса, урон и крит по потерянному здоровью, лечение — по полученному.</summary>
public class StrikeNotesTests
{
    private static AttackType Hit(AttackClass cls, int source = 0) => new("A", 50, cls, 100, 3, 30, 30, false, 1, Source: source);

    private static BattleUnit Unit(string id, bool attacker, AttackType attack, int hp = 100,
                                   IReadOnlyList<ImmunityGrant>? bySource = null, IReadOnlyList<ImmunityGrant>? byClass = null) =>
        new()
        {
            Id = id, Type = new UnitType(id, 1, 100, 0, "A", "", false, true, 10, 100, bySource, byClass),
            Attack = attack, Attacker = attacker, Slot = 0, HpMax = 100, Hp = hp,
        };

    /// <summary>Строка — её id и подстановки, чтобы видно было, что куда легло.</summary>
    private static string Text(string id) => $"{id} %DAMAGE% %CRITICAL%";

    private static IReadOnlyList<StrikeNote> Notes(BattleUnit striker, BattleUnit target, params TurnRecord[] records)
    {
        var state = new BattleState { Units = [striker, target], AttackerGroup = "A", DefenderGroup = "D" };
        return StrikeNotes.Before(state, striker).After(records, state, Text);
    }

    /// <summary>Вампиризм (<c>0x63c27d</c>): «+N» по юниту группы бьющего — прибавки одного юнита за удар по нескольким целям складываются
    /// (<c>0x63b1b6</c>), нулевые без надписи (<c>0x63b11d</c>). Партия 15.10, пометки 1 и 2.</summary>
    [Fact]
    public void Drains_SumPerUnit_SkipZero()
    {
        var notes = StrikeNotes.Drains([new DrainHeal(1, "v", "v", 30), new DrainHeal(1, "v", "v", 25), new DrainHeal(1, "v", "w", 0),
                                        new DrainHeal(1, "v", "k", 6)], Text);
        Assert.Equal([("v", "X008TA0004 +55 %CRITICAL%"), ("k", "X008TA0004 +6 %CRITICAL%")], notes.Select(n => (n.Target, n.Markup)));
    }

    /// <summary>Тик в начале хода (<c>0x635ecc</c>): яд, мороз, ожог — своя строка с уроном со знаком «−» (<c>0x636419</c>); сообщения начала
    /// хода — «Паралич прошел!» и «Оживает!».</summary>
    [Fact]
    public void Ticks_AndTurnStartNotes()
    {
        Assert.Equal("X008TA0014 -5 %CRITICAL%", StrikeNotes.TickText(new DotTick(1, "u", AttackClass.Poison, 5, 95), Text));
        Assert.Equal("X008TA0015 -7 %CRITICAL%", StrikeNotes.TickText(new DotTick(1, "u", AttackClass.Frostbite, 7, 93), Text));
        Assert.Equal("X160TA0022 -3 %CRITICAL%", StrikeNotes.TickText(new DotTick(1, "u", AttackClass.Blister, 3, 97), Text));
        Assert.Equal("X008TA0024 %DAMAGE% %CRITICAL%", StrikeNotes.NoteText(BattleNoteKind.Unparalyzed, Text));
        Assert.Equal("X008TA0025 %DAMAGE% %CRITICAL%", StrikeNotes.NoteText(BattleNoteKind.Unpetrified, Text));
        Assert.Null(StrikeNotes.NoteText(BattleNoteKind.Untransformed, Text));
    }

    [Fact]
    public void Damage_CritAndKillFollowHpLost()
    {
        var a = Unit("a", true, Hit(AttackClass.Damage));
        Assert.Equal("X160TA0021 25 15", Notes(a, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", false, 25, 60, Crit: 15)).Single().Markup);
        Assert.Equal("X008TA0004 -30 %CRITICAL%", Notes(a, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", false, 30, 70)).Single().Markup);
        // Добил: потеряно 10 меньше крита 15 — урон 0, первая атака — надписи нет.
        Assert.Empty(Notes(a, Unit("t", false, Hit(AttackClass.Damage), hp: 10), new TurnRecord(1, "a", "t", false, 30, 0, Crit: 15)));
        // Добил без крита: число — потерянное здоровье (10), а не урон расчёта (30).
        Assert.Equal("X008TA0004 -10 %CRITICAL%", Notes(a, Unit("t", false, Hit(AttackClass.Damage), hp: 10), new TurnRecord(1, "a", "t", false, 30, 0)).Single().Markup);
    }

    [Fact]
    public void Miss_OnlyFirstAttack_ShieldBeforeMiss()
    {
        var a = Unit("a", true, Hit(AttackClass.Damage, source: 2));
        Assert.StartsWith("X008TA0001", Notes(a, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", true, 0, 100)).Single().Markup);
        Assert.Empty(Notes(a, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", true, 0, 100, Attack: 2)));
        // Always к источнику — «Иммунитет», и при промахе; живой заряд Once к классу — «Щит»; потраченный — обычный удар.
        var always = Unit("t", false, Hit(AttackClass.Damage), bySource: [new ImmunityGrant(2, ImmunityLevel.Always)]);
        Assert.StartsWith("X008TA0002", Notes(a, always, new TurnRecord(1, "a", "t", true, 0, 100)).Single().Markup);
        var ward = Unit("t", false, Hit(AttackClass.Damage), byClass: [new ImmunityGrant((int)AttackClass.Damage, ImmunityLevel.Once)]);
        Assert.StartsWith("X008TA0011", Notes(a, ward, new TurnRecord(1, "a", "t", false, 0, 100)).Single().Markup);
        ward.ClassImmunityUsed = 1 << (int)AttackClass.Damage;
        Assert.StartsWith("X008TA0004", Notes(a, ward, new TurnRecord(1, "a", "t", false, 20, 80)).Single().Markup);
    }

    [Fact]
    public void ClassTextAndHeal()
    {
        var p = Unit("a", true, Hit(AttackClass.Paralyze));
        Assert.StartsWith("X008TA0008", Notes(p, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", false, 0, 100)).Single().Markup);
        var healer = Unit("h", true, Hit(AttackClass.Heal));
        Assert.Equal("X008TA0004 +30 %CRITICAL%", Notes(healer, Unit("t", true, Hit(AttackClass.Damage), hp: 50), new TurnRecord(1, "h", "t", false, 0, 80)).Single().Markup);
        Assert.Null(StrikeNotes.ClassText(AttackClass.Summon));
        Assert.Equal("X008TA0022", StrikeNotes.ClassText(AttackClass.TransformOther));
    }

    /// <summary>Вспышки удара (<see cref="StrikeNotes.Faces"/>, <c>0x63bcc2…0x63bfa8</c>): и где надписи нет — добил без урона первой
    /// атакой (красным), промах второй атаки — ничего, щит — «защита» (без заливки), лечение — синим; атаки по своим и вампиризм — свои.</summary>
    [Fact]
    public void Faces_FlashEvenWithoutText()
    {
        var a = Unit("a", true, Hit(AttackClass.Damage));
        IReadOnlyList<(string, BattleFlash)> Faces(BattleUnit striker, BattleUnit target, TurnRecord record)
        {
            var state = new BattleState { Units = [striker, target], AttackerGroup = "A", DefenderGroup = "D" };
            return StrikeNotes.Before(state, striker).Faces([record], state);
        }
        var killed = new TurnRecord(1, "a", "t", false, 30, 0, Crit: 15);
        Assert.Empty(Notes(a, Unit("t", false, Hit(AttackClass.Damage), hp: 10), killed));
        Assert.Equal([("t", new BattleFlash(BattleFace.Hit, false))], Faces(a, Unit("t", false, Hit(AttackClass.Damage), hp: 10), killed));
        Assert.Empty(Faces(a, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", true, 0, 100, Attack: 2)));
        Assert.Equal(BattleFace.Missed, Faces(a, Unit("t", false, Hit(AttackClass.Damage)), new TurnRecord(1, "a", "t", true, 0, 100)).Single().Item2.Face);
        var ward = Unit("t", false, Hit(AttackClass.Damage), byClass: [new ImmunityGrant((int)AttackClass.Damage, ImmunityLevel.Once)]);
        Assert.Equal(BattleFace.Shielded, Faces(a, ward, new TurnRecord(1, "a", "t", false, 0, 100)).Single().Item2.Face);
        var healer = Unit("h", true, Hit(AttackClass.Heal));
        Assert.Equal(BattleFace.Healed, Faces(healer, Unit("t", true, Hit(AttackClass.Damage), hp: 50), new TurnRecord(1, "h", "t", false, 0, 80)).Single().Item2.Face);
        // Призыв записей хода не даёт — показ подставляет запись на призванного (ShowOrder.SummonTargets): чёрным целиком, держится.
        Assert.Equal([("t", new BattleFlash(BattleFace.Summoned, true))],
                     Faces(Unit("s", true, Hit(AttackClass.Summon)), Unit("t", true, Hit(AttackClass.Damage)), new TurnRecord(1, "s", "t", false, 0, 100)));
        Assert.Equal([("x", new BattleFlash(BattleFace.Boosted, false)), ("y", new BattleFlash(BattleFace.Transformed, true))],
                     StrikeFaces.AllyFaces([new AllyNote(1, "a", "x", AttackClass.BoostDamage), new AllyNote(1, "a", "y", AttackClass.TransformSelf)]));
        Assert.Equal([("v", new BattleFlash(BattleFace.DrainHealed, false))],
                     StrikeFaces.DrainFaces([new DrainHeal(1, "v", "v", 30), new DrainHeal(1, "v", "v", 5), new DrainHeal(1, "v", "w", 0)]));
    }

    /// <summary>«Страх» по группе, которая бежать не может (гарнизон, руина), — «Паралич» (<c>0x63b35a</c> → <c>0x5f51bf</c>); может — «Страх».</summary>
    [Fact]
    public void Fear_AsParalyze_WhenTheGroupCannotRetreat()
    {
        var a = Unit("a", true, Hit(AttackClass.Fear));
        foreach (bool canRetreat in new[] { true, false })
        {
            var t = Unit("t", false, Hit(AttackClass.Damage));
            var state = new BattleState { Units = [a, t], AttackerGroup = "A", DefenderGroup = "D", DefendersCanRetreat = canRetreat };
            var note = StrikeNotes.Before(state, a).After([new TurnRecord(1, "a", "t", false, 0, 100)], state, Text).Single();
            Assert.StartsWith(canRetreat ? "X008TA0007" : "X008TA0008", note.Markup);
        }
    }

    /// <summary>Атаки по своим (<see cref="AllyNote"/>) — строка класса на каждой цели по порядку; у призыва — ничего.</summary>
    [Fact]
    public void Allies_ClassTextPerTarget()
    {
        var notes = StrikeNotes.Allies([new AllyNote(1, "a", "x", AttackClass.BoostDamage), new AllyNote(1, "a", "y", AttackClass.Revive),
                                        new AllyNote(1, "a", "z", AttackClass.Summon), new AllyNote(1, "a", "a", AttackClass.TransformSelf)], Text);
        Assert.Equal([("x", "X008TA0003"), ("y", "X008TA0016"), ("a", "X008TA0022")], notes.Select(n => (n.Target, n.Markup.Split(' ')[0])));
    }
}
