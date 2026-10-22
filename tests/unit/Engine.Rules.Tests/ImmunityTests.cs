using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Задача 3 фазы Ф6: иммунитеты и варды. Все юниты, атаки и модификаторы — настоящие, из живой
/// ванили (<c>Gunits</c>, <c>Gattacks</c>, <c>Gimmu</c>, <c>GimmuC</c>, <c>Gmodif</c>/<c>GmodifL</c>);
/// синтетика здесь только там, где надо показать, что «источник не задан» иммунитета не включает.
/// Игра не запускается: поведение иммунитета — ранг 2/3 до прогона задачи 13, здесь проверяется
/// ровно то, что модель делает то, что о ней написано.
/// </summary>
public class ImmunityTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));
    private static readonly Lazy<ModifierTables> Mods = new(() => ModifierLoader.Load(ImportedData.Content));

    // Ванильные участники, названные поимённо (см. отчёт задачи 3):
    private const string RedDragon = "g000uu5019";     // Gimmu: Fire (4) Always
    private const string FlameCaster = "g000uu0028";   // Gimmu: Fire (4) Once; атака g000aa0028 — Fire
    private const string LivingArmor = "g000uu0024";   // GimmuC: Poison (12) Always; Gimmu: Mind (1) Always
    private const string Squire = "g000uu0001";        // ни одной строки ни в Gimmu, ни в GimmuC
    private const string FireBolt = "g000aa0028";      // Damage, SOURCE 4 (Fire), QTY_DAM 35, POWER 80
    private const string PoisonBite = "g000aa9019";    // Poison, SOURCE 3 (Death), QTY_DAM 20, INFINITE
    private const string MindParalyze = "g000aa0078";  // Paralyze, SOURCE 1 (Mind)
    private const string WardsAttack = "g000aa8133";   // BestowWards, QTY_WARDS 4
    private const string FireWard = "g000um0016";      // TYPE 12: IMMUNITY 4 (Fire), IMMUNECAT 2 (Once)

    private static RulesConstants C => Rules.Value.Constants;

    private static BattleUnit Of(string typeId, bool attacker = false)
    {
        var type = Rules.Value.Units[typeId];
        return new BattleUnit
        {
            Id = typeId, Type = type, Attack = Rules.Value.AttackOf(type)!,
            Attack2 = Rules.Value.Attack2Of(type), Attacker = attacker, Slot = 0,
            HpMax = type.HitPoints, Hp = type.HitPoints,
        };
    }

    private static AttackType Attack(string id) => Rules.Value.Attacks[id];

    // ---- долг задачи 2: Source и Wards наконец заполнены из RulesLoader.Load -------------------

    /// <summary>Без этого весь остальной файл проверял бы пустоту: до задачи 3
    /// <c>AttackType.Source</c> был <c>SourceUnset</c> у всех 350 атак, а <c>Wards</c> — null.</summary>
    [SkippableFact]
    public void RulesLoader_FillsAttackSourceAndWards()
    {
        ImportedData.RequireRules();
        var attacks = Rules.Value.Attacks;
        Assert.All(attacks.Values, a => Assert.InRange(a.Source, 0, 7));   // LattS: 0 Weapon … 7 Air
        Assert.Equal(4, Attack(FireBolt).Source);
        Assert.Equal(3, Attack(PoisonBite).Source);

        var withWards = attacks.Values.Where(a => a.Wards is { Count: > 0 }).ToList();
        output.WriteLine($"Gattacks: source заполнен у {attacks.Count}, с вардами атак {withWards.Count}, " +
                         $"вардов всего {withWards.Sum(a => a.Wards!.Count)}");
        Assert.Equal(3, withWards.Count);                                  // ровно те же три, что у LoadExtras
        Assert.Equal(6, withWards.Sum(a => a.Wards!.Count));
        Assert.Equal(new[] { "g000um0001", "g000um0016", "g000um0010", "g000um0005" },
                     Attack(WardsAttack).Wards!.ToArray());
    }

    // ---- иммунитет по источнику (Gimmu) --------------------------------------------------------

    /// <summary>Always: эффекта нет ни разу, сколько ни бей, но бросок точности каждый удар тратит —
    /// движок бросает ДО проверки иммунитета (ранг 1, задача 20 Ф7, см. <c>Attacks.Apply</c>).</summary>
    [SkippableFact]
    public void SourceImmunity_Always_BlocksEveryHit_AfterTheMissRoll()
    {
        ImportedData.RequireRules();
        var attacker = Of(FlameCaster, attacker: true);
        var dragon = Of(RedDragon);
        var rng = new ScriptedRng([0, 0, 0, 0, 0, 0]);   // по два броска точности на удар, и только

        for (int hit = 1; hit <= 3; hit++)
        {
            var r = Attacks.Apply(attacker, Attack(FireBolt), dragon, rng, C, round: 1);
            Assert.True(r.Immune, $"удар {hit}: иммунитет Always не сработал");
            Assert.False(r.Applied);
            Assert.False(r.Miss);
            Assert.Equal(0, r.Damage);
        }
        Assert.Equal(dragon.HpMax, dragon.Hp);
        Assert.Equal(6, rng.Used);
        Assert.Equal(0, dragon.SourceImmunityUsed);   // Always ничего не тратит
        Assert.Equal(ImmunityLevel.Always, Immunity.LevelOf(dragon, Attack(FireBolt)));
    }

    /// <summary>Once: первый удар не проходит и взводит бит, второй проходит и наносит урон.
    /// Flame Caster — Fire Once, и бьёт его такой же Flame Caster: обе стороны из ванили.</summary>
    [SkippableFact]
    public void SourceImmunity_Once_BlocksFirstHitOnly()
    {
        ImportedData.RequireRules();
        var attacker = Of(FlameCaster, attacker: true);
        var target = Of(FlameCaster);
        int hp = target.Hp;

        var first = new ScriptedRng([0, 0]);                  // точность бросается и по иммунной цели
        var r1 = Attacks.Apply(attacker, Attack(FireBolt), target, first, C, round: 1);
        Assert.True(r1.Immune);
        Assert.Equal(hp, target.Hp);
        Assert.Equal(2, first.Used);
        Assert.Equal(1 << 4, target.SourceImmunityUsed);      // бит источника Fire потрачен

        var second = new ScriptedRng([0, 0, 0]);              // 2 броска точности + 1 добавка урона
        var r2 = Attacks.Apply(attacker, Attack(FireBolt), target, second, C, round: 1);
        Assert.False(r2.Immune);
        Assert.True(r2.Applied);
        Assert.Equal(35, r2.Damage);                          // QTY_DAM 35 + rng(6) = 0, броня 0
        Assert.Equal(hp - 35, target.Hp);
        Assert.Equal(3, second.Used);
        output.WriteLine($"{FlameCaster} Fire Once: удар 1 — иммунитет после 2 бросков; удар 2 — {r2.Damage} урона");
    }

    // ---- иммунитет по классу (GimmuC) ----------------------------------------------------------

    /// <summary>Иммунитет по классу берётся отдельно от источника: Living Armor не травится
    /// (<c>GimmuC</c> Poison Always), хотя к источнику Death (3) той же атаки он не неуязвим.
    /// Контроль — Squire, которого нет ни в одной из таблиц: тот же удар кладёт на него DoT.</summary>
    [SkippableFact]
    public void ClassImmunity_Always_BlocksPoison_WhileSameSourceGetsThrough()
    {
        ImportedData.RequireRules();
        var attacker = Of(FlameCaster, attacker: true);
        var poison = Attack(PoisonBite);

        var armor = Of(LivingArmor);
        var r = Attacks.Apply(attacker, poison, armor, new ScriptedRng([0, 0]), C, round: 1);
        Assert.True(r.Immune);
        Assert.Empty(armor.Dots);
        Assert.Equal(0, armor.ClassImmunityUsed);                                           // Always не тратится
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelOf(armor, Attack(FireBolt)));   // Fire ему не страшен, но и не запрещён
        Assert.Equal(ImmunityLevel.Always, Immunity.LevelOf(armor, Attack(MindParalyze)));  // а Mind (Gimmu 1 Always) — да

        var squire = Of(Squire);
        var control = Attacks.Apply(attacker, poison, squire, new ScriptedRng([0, 0]), C, round: 1);
        Assert.False(control.Immune);
        Assert.Equal(AttackClass.Poison, Assert.Single(squire.Dots).Class);
    }

    /// <summary>Иммунитетов по классу в ванили ровно тринадцать — их можно перечислить поимённо,
    /// что и делает этот тест. Восемь юнитов не берёт яд (класс 12), пять не берёт дробление брони
    /// (класс 25), уровень у всех тринадцати — Always. Плановое число 240 было счётчиком заголовка
    /// <c>.dbf</c> вместе с удалёнными записями (измерено задачей 1).</summary>
    [SkippableFact]
    public void VanillaClassImmunities_AreThirteen_NamedOneByOne()
    {
        ImportedData.RequireRules();
        (string Unit, AttackClass Class)[] expected =
        [
            ("g000uu0024", AttackClass.Poison),   // Living Armor
            ("g000uu0025", AttackClass.Poison),   // Golem
            ("g000uu0051", AttackClass.Poison),   // Stone Ancestor
            ("g000uu0055", AttackClass.Poison),   // Gargoyle
            ("g000uu0056", AttackClass.Poison),   // Marble Gargoyle
            ("g000uu0104", AttackClass.Poison),   // Thanatos
            ("g000uu0167", AttackClass.Poison),   // Onyx Gargoyle
            ("g000uu3001", AttackClass.Shatter),  // Myzrael
            ("g000uu3002", AttackClass.Shatter),  // Vithar
            ("g000uu3003", AttackClass.Shatter),  // Ashkael
            ("g000uu3004", AttackClass.Shatter),  // Ashgan
            ("g000uu8040", AttackClass.Shatter),  // Illumielle
            // тринадцатая строка — g000uu5167, юнит из Gunits удалён (висячая ссылка задачи 1),
            // поэтому в правила она не попадает: строк 13, живых носителей 12.
        ];

        var carriers = Rules.Value.Units.Values
            .Where(u => u.ImmunitiesByClass is { Count: > 0 })
            .SelectMany(u => u.ImmunitiesByClass!.Select(g => (u.Id, Class: (AttackClass)g.Category, g.Level)))
            .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();

        foreach (var c in carriers) output.WriteLine($"GimmuC {c.Id} {c.Class} {c.Level}");
        Assert.Equal(expected.OrderBy(e => e.Unit, StringComparer.Ordinal).ToArray(),
                     carriers.Select(c => (c.Id, c.Class)).ToArray());
        Assert.All(carriers, c => Assert.Equal(ImmunityLevel.Always, c.Level));
        Assert.Equal(13, LastEdict.Data.Dbf.GameTables.Read(ImportedData.Tables, "GimmuC.dbf").Rows.Count);   // строк таблицы, с висячей
    }

    // ---- варды ---------------------------------------------------------------------------------

    /// <summary>Вард — это модификатор (<c>Gattacks.WARD1..4</c> → <c>Gmodif</c>), и работает он
    /// через ту же проверку: все шесть вани́льных вардов дают <see cref="ImmunityLevel.Once"/> к
    /// своему источнику. Squire иммунитетов не имеет вовсе, поэтому весь эффект — от варда.</summary>
    [SkippableFact]
    public void Ward_GivesOnceImmunity_AndIsRemovedAtGiversTurn()
    {
        ImportedData.RequireRules();
        var attacker = Of(FlameCaster, attacker: true);
        var squire = Of(Squire);
        var giver = Of(FlameCaster);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelOf(squire, Attack(FireBolt)));

        Assert.Equal(1, Immunity.Bestow(squire, [Mods.Value.Modifiers[FireWard]], giver));
        Assert.Equal(ImmunityLevel.Once, Immunity.LevelOf(squire, Attack(FireBolt)));

        var r1 = Attacks.Apply(attacker, Attack(FireBolt), squire, new ScriptedRng([0, 0]), C, round: 1);
        Assert.True(r1.Immune);
        Assert.Equal(squire.HpMax, squire.Hp);

        // Снятие вардов зашито в начало хода РАЗДАВШЕГО (BattleSim.BeginTurn, beforeBattleTurn
        // 0x62673f) — проверяем через него, а не прямым вызовом ClearWards. Ход носителя их не снимает.
        var state = new BattleState { Units = [attacker, squire, giver], AttackerGroup = "G1", DefenderGroup = "G2", Round = 1 };
        var sim = new BattleSim(state, new ScriptedRng([]), C);
        Assert.True(sim.BeginTurn(squire));
        Assert.Single(squire.Wards);
        Assert.True(sim.BeginTurn(giver));
        Assert.Empty(squire.Wards);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelOf(squire, Attack(FireBolt)));

        var r2 = Attacks.Apply(attacker, Attack(FireBolt), squire, new ScriptedRng([0, 0, 0]), C, round: 2);
        Assert.False(r2.Immune);
        Assert.Equal(35, r2.Damage);
    }

    /// <summary>Повтор того же варда отсеивается — <b>ранг 1</b>, задача 8: <c>onHit</c>
    /// <c>0x65d528</c> перед каждым вардом зовёт <c>0x65d448</c>, а та ищет id варда в списке вардов
    /// цели (<c>BattleMsgData::getUnitWards</c> <c>0x621caf</c>) и при находке даёт «пропустить».
    /// Прежде повторное наложение занимало вторые четыре слота из восьми — находка ревью задачи 3,
    /// закрыта здесь.
    /// <para><b>Следствие: вани́льный потолок восемь недостижим.</b> Ward-модификаторов на всю игру
    /// четыре (<c>Gattacks.WARD1..4</c> у трёх атак; в <c>Gspells.WARD1..4</c> ни одного), а повторы
    /// теперь не кладутся — значит больше четырёх вардов на юните не бывает. Потолок остаётся в коде:
    /// он ранга 1 (массив из восьми, <c>0x6218bb</c>) и нужен модам.</para></summary>
    [SkippableFact]
    public void Ward_IsNotBestowedTwice_SoVanillaNeverReachesTheLimitOfEight()
    {
        ImportedData.RequireRules();
        var squire = Of(Squire);
        var wards = Attack(WardsAttack).Wards!.Select(id => Mods.Value.Modifiers[id]).ToList();
        Assert.Equal(4, wards.Count);

        Assert.Equal(4, Immunity.Bestow(squire, wards));
        Assert.Equal(0, Immunity.Bestow(squire, wards));   // те же четыре — повтор, слотов не занимает
        Assert.Equal(4, squire.Wards.Count);
        Assert.True(squire.Wards.Count < Immunity.WardLimit);

        // Потолок сам по себе жив: восемь РАЗНЫХ модификаторов лягут, девятый — нет.
        var other = Of(Squire);
        var eight = Mods.Value.Modifiers.Values.Take(9).ToList();
        Assert.Equal(Immunity.WardLimit, Immunity.Bestow(other, eight));
        Assert.Equal(Immunity.WardLimit, other.Wards.Count);
        output.WriteLine($"варды {WardsAttack}: {string.Join(", ", wards.Select(w => w.Id))}; " +
                         $"повтор кладёт 0, потолок {Immunity.WardLimit}");
    }

    /// <summary>Свежий вард <b>возвращает</b> потраченный заряд <see cref="ImmunityLevel.Once"/> по
    /// своему источнику — ранг 1, задача 8: <c>BattleMsgData::addUnitToWards</c> (<c>0x62180a</c>)
    /// после укладки варда берёт его источник, переводит в номер 0..7 (<c>0x62198e</c>) и делает
    /// <c>unitInfo-&gt;[0x24] &amp;= ~(1 &lt;&lt; n)</c>; байт <c>+0x24</c> — та самая маска, которую
    /// читает <c>0x622906</c> из <c>isUnitImmuneToAttack</c>.
    /// <para>Без этого вард, наложенный после сработавшего иммунитета того же источника, не давал бы
    /// ничего — и это ровно тот случай, ради которого вард в бою и накладывают.</para></summary>
    [SkippableFact]
    public void Ward_RestoresSpentOnceCharge_OfItsOwnSource()
    {
        ImportedData.RequireRules();
        var attacker = Of(FlameCaster, attacker: true);
        var caster = Of(FlameCaster);                       // Gimmu: Fire (4) Once

        Assert.True(Attacks.Apply(attacker, Attack(FireBolt), caster, new ScriptedRng([0, 0]), C, round: 1).Immune);
        Assert.Equal(1 << 4, caster.SourceImmunityUsed);     // заряд по Fire потрачен

        Assert.Equal(1, Immunity.Bestow(caster, [Mods.Value.Modifiers[FireWard]]));
        Assert.Equal(0, caster.SourceImmunityUsed);          // вард Fire вернул его

        var r = Attacks.Apply(attacker, Attack(FireBolt), caster, new ScriptedRng([0, 0]), C, round: 1);
        Assert.True(r.Immune);
        Assert.Equal(caster.HpMax, caster.Hp);
        output.WriteLine($"вард {FireWard} вернул заряд Once по источнику 4 (Fire)");
    }

    // ---- то, что не должно было измениться -----------------------------------------------------

    /// <summary>Юнит без единой строки в <c>Gimmu</c>/<c>GimmuC</c> ходит по прежнему пути: два
    /// броска точности плюс добавка урона, ровно как в Ф4. Это и есть страховка от того, что
    /// врезка иммунитета сдвинет поток бросков и уронит С2.</summary>
    [SkippableFact]
    public void NoImmunity_KeepsRollFlowUnchanged()
    {
        ImportedData.RequireRules();
        var attacker = Of(FlameCaster, attacker: true);
        var squire = Of(Squire);
        Assert.Empty(squire.Type.ImmunitiesBySource!);
        Assert.Empty(squire.Type.ImmunitiesByClass!);

        var rng = new ScriptedRng([0, 0, 0]);
        var r = Attacks.Apply(attacker, Attack(FireBolt), squire, rng, C, round: 1);
        Assert.False(r.Immune);
        Assert.Equal(3, rng.Used);
        Assert.Equal(35, r.Damage);
    }

    /// <summary>Заряд <c>Once</c> списывается, даже когда удар остановил <c>Always</c> другой категории:
    /// <c>battleTurn</c> после истинного <c>isImmune</c> взводит бит у каждой категории уровня <c>Once</c>
    /// (<c>0x624ec2</c>–<c>0x625078</c>, <c>0x622c79</c>/<c>0x622cc0</c>), ранг 1. Living Armor — класс Poison
    /// Always; оберег огня — источник 4 Once; синтетический яд огнём.</summary>
    [SkippableFact]
    public void OnceWard_IsSpent_EvenWhenOtherCategoryIsAlways()
    {
        ImportedData.RequireRules();
        var armor = Of(LivingArmor);
        Immunity.Bestow(armor, [Rules.Value.Modifiers![FireWard]]);
        var firePoison = new AttackType("g000aa0001", 0, AttackClass.Poison, 0, 0, 0, 0, false, 0, Source: 4);
        Assert.True(Immunity.Blocks(armor, firePoison));
        Assert.Equal(1 << 4, armor.SourceImmunityUsed);
        Assert.True(Immunity.Blocks(armor, firePoison));   // Always класса держит и дальше
    }

    /// <summary>Атака, собранная в тесте без колонки <c>SOURCE</c> (<c>SourceUnset</c> = −1),
    /// иммунитета не включает и сдвигом на отрицательное число не падает — от этого зависят все
    /// тесты Ф4, где атаки синтетические.</summary>
    [SkippableFact]
    public void SourceUnset_NeverMatchesAnyImmunity()
    {
        ImportedData.RequireRules();
        var dragon = Of(RedDragon);
        var synthetic = new AttackType("g000aa0001", 50, AttackClass.Damage, 80, 3, 25, 0, false, 0);
        Assert.Equal(AttackType.SourceUnset, synthetic.Source);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelOf(dragon, synthetic));
        Assert.False(Immunity.Blocks(dragon, synthetic));
        Assert.Equal(0, dragon.SourceImmunityUsed);
    }

    /// <summary>Счётчик, без которого «зелёный фильтр» ничего не доказывает: сколько носителей
    /// иммунитета вообще есть в ванили и сколько из них Always против Once.</summary>
    [SkippableFact]
    public void VanillaImmunities_Counted()
    {
        ImportedData.RequireRules();
        var grants = Rules.Value.Units.Values
            .SelectMany(u => (u.ImmunitiesBySource ?? []).Concat(u.ImmunitiesByClass ?? [])).ToList();
        int once = grants.Count(g => g.Level == ImmunityLevel.Once);
        int always = grants.Count(g => g.Level == ImmunityLevel.Always);
        int carriers = Rules.Value.Units.Values
            .Count(u => u.ImmunitiesBySource is { Count: > 0 } || u.ImmunitiesByClass is { Count: > 0 });
        output.WriteLine($"иммунитетов на юнитах: {grants.Count} (Once {once}, Always {always}); носителей {carriers}");
        // 227 строк Gimmu минус заглушка g000uu0000 + 13 строк GimmuC минус осиротевший g000uu5167:
        // 226 + 12 = 238. Once только из Gimmu (GimmuC целиком Always).
        Assert.Equal(238, grants.Count);
        Assert.Equal(123, once);
        Assert.Equal(115, always);        // 103 из Gimmu + 12 из GimmuC
        Assert.Equal(129, carriers);
    }
}
