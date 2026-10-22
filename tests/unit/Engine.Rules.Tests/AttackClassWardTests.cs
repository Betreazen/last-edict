using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Rng;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Задача 8 фазы Ф6: четвёртый и последний круг классов атак — <c>BestowWards</c> (24) и
/// <c>Shatter</c> (25). Юниты, атаки и модификаторы настоящие, из живой ванили; синтетики нет.
///
/// <para><b>Счёт бросков утверждается у каждого класса</b> — величина несущая: С2 воспроизводит бой
/// шаг в шаг по журнальным броскам, и одна лишняя кость сдвигает весь поток. У обоих классов он
/// <b>нулевой</b> и это <b>ранг 1</b>: слот 6 vftable (<c>canMiss</c>) у обоих — общая заглушка
/// <c>0x6598f5</c>, а в телах <c>onHit</c> (<c>0x65d528</c> и <c>0x65d8dc</c>, прочитаны до
/// <c>ret</c>) боевой генератор <c>0x54851f</c> не зовётся ни разу.</para>
///
/// <para><b>Корпус к этому кругу слеп целиком</b> — сплошной проход по всем 74 файлам
/// <c>corpus/**/*.jsonl</c>: двадцать типов юнитов, ни одного из семи вани́льных носителей классов
/// 24/25, ни одного события с их id атак, все 3612 событий <c>armor</c> дают 0/40/50/60 (следа
/// дробления брони нет). Ноль — законный результат, но он ничего не подтверждает: молчание журнала
/// не отличает «формула верна» от «случай не встретился». Поэтому всё здесь опирается на
/// дизассемблер GOG, а настоящую сверку даст только бой задачи 13.</para>
///
/// <para>Игра не запускалась.</para>
/// </summary>
public class AttackClassWardTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));

    // Вани́льные носители: перебор Gunits × Gattacks по классу атаки даёт ровно семь типов.
    private const string WardGiver = "base:unit/grovemaiden-elves-2";      // L1 BestowWards, hp 75
    private const string WardsFour = "base:attack/grovemaiden-elves";      // BestowWards, REACH 2, SOURCE 2, все четыре варда
    private const string AreaWardGiver = "base:unit/sun-dancer";  // L4 BestowWards, площадной (REACH 1)
    private const string WardsFire = "base:attack/sun-dancer";      // BestowWards, REACH 1 — один вард, огонь
    private const string Shatterer = "base:unit/theurgist";      // L3, ATTACK2 — Shatter
    private const string ShatterAir = "base:attack/theurgist-2nd";     // Shatter, SOURCE 7 (Air), REACH 2, QTY_DAM 15
    private const string ShatterWpn = "base:attack/sir-allemon-neutral-2nd";     // Shatter, SOURCE 0, REACH 3, QTY_DAM 15
    private const string Squire = "base:unit/squire-empire";         // L1, hp 100, ARMOR 0, иммунитетов нет
    private const string Imp = "base:unit/sir-allemon-neutral";            // ARMOR 20 — на нём видно нижнюю границу брони
    private const string Myzrael = "base:unit/myzrael";        // GimmuC: класс 25 Always — не крошится
    private const string FlameCaster = "base:unit/flame-caster";    // Gimmu: Fire (4) Once
    private const string FireBolt = "base:attack/flame-caster";       // Damage, SOURCE 4 (Fire), QTY_DAM 35
    private const string FireWard = "base:modifier/fire-ward";       // TYPE 12: IMMUNITY 4 (Fire), IMMUNECAT 2 (Once)
    private const string AirWard = "base:modifier/air-ward";        // TYPE 12: IMMUNITY 7 (Air)

    private static RulesConstants C => Rules.Value.Constants;

    private static BattleUnit Of(string typeId, bool attacker = false, int slot = 0, string? id = null) =>
        new()
        {
            Id = id ?? typeId, Type = Rules.Value.Units[typeId],
            Attack = Rules.Value.AttackOf(Rules.Value.Units[typeId])!,
            Attack2 = Rules.Value.Attack2Of(Rules.Value.Units[typeId]),
            Attacker = attacker, Slot = slot,
            HpMax = Rules.Value.Units[typeId].HitPoints, Hp = Rules.Value.Units[typeId].HitPoints,
        };

    private static AttackType Attack(string id) => Rules.Value.Attacks[id];

    private static BattleState State(params BattleUnit[] units) => new()
    {
        Units = [.. units], AttackerGroup = "A", DefenderGroup = "D", Round = 1, Rules = Rules.Value,
    };

    // ---- ванильные числа, на которых стоят утверждения ниже -------------------------------------

    [SkippableFact] public void VanillaData_IsWhatTheTestsAssume()
    {
        ImportedData.RequireRules();
        Assert.Equal(AttackClass.BestowWards, Attack(WardsFour).Class);
        Assert.Equal(AttackClass.Shatter, Attack(ShatterAir).Class);
        // Вани́льных атак класса 24 — три, класса 25 — две; больше их взять неоткуда.
        Assert.Equal(3, Rules.Value.Attacks.Values.Count(a => a.Class == AttackClass.BestowWards));
        Assert.Equal(2, Rules.Value.Attacks.Values.Count(a => a.Class == AttackClass.Shatter));
        // Обе атаки дробления несут QTY_DAM 15 — именно отсюда взялось «−15 фиксировано» у B.
        Assert.Equal(15, Attack(ShatterAir).QtyDamage);
        Assert.Equal(15, Attack(ShatterWpn).QtyDamage);
        // Четыре варда у g000aa8133, один у g000aa8134.
        Assert.Equal(4, Attack(WardsFour).Wards!.Count);
        Assert.Single(Attack(WardsFire).Wards!);
        // Броня мишеней: у Оруженосца ноль, у Imp двадцать — на нём и видна нижняя граница.
        Assert.Equal(0, Rules.Value.Units[Squire].Armor);
        Assert.Equal(20, Rules.Value.Units[Imp].Armor);
    }

    /// <summary>Ward-модификаторов на всю ваниль ровно четыре, и все четыре несут
    /// <c>TYPE 12 Immunity</c> уровня <see cref="ImmunityLevel.Once"/> к своей стихии. Это входное
    /// условие всего файла: если бы хоть один был <c>Always</c>, поведение варда было бы другим.</summary>
    [SkippableFact] public void VanillaWards_AreFourOnceImmunities()
    {
        ImportedData.RequireRules();
        var ids = Rules.Value.Attacks.Values.SelectMany(a => a.Wards ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(["base:modifier/air-ward", "base:modifier/earth-ward", "base:modifier/fire-ward", "base:modifier/water-ward"], ids);

        foreach (var m in ids.Select(id => Rules.Value.Modifiers![id]))
        {
            var e = Assert.Single(m.Elements);
            Assert.Equal(ModifierElementType.Immunity, e.Type);
            Assert.Equal(ImmunityLevel.Once, (ImmunityLevel)e.ImmuneCat!.Value);
            output.WriteLine($"{m.Id}: источник {e.Immunity}, уровень {(ImmunityLevel)e.ImmuneCat!.Value}");
        }
    }

    // ---- Shatter (25) ---------------------------------------------------------------------------

    /// <summary>Формула ранга 1, прочитанная в движке: <c>shattered = min(shattered + QTY_DAM, 100)</c>.
    /// Величина — один вызов <c>attack-&gt;getQtyDamage()</c> (<c>0x65d930</c>), кламп — в
    /// <c>setUnitShatteredArmor</c> (<c>0x621e67</c>). Ни урона по HP, ни единого броска.
    /// <para>Это опровергает <b>оба</b> чужих источника: A считает четыре <c>min</c> (два из них —
    /// его собственные настройки), B фиксирует −15. Здесь второй удар по той же цели даёт 30, и
    /// именно на нём оба и расходятся с движком.</para></summary>
    [SkippableFact] public void Shatter_AccumulatesQtyDamage_WithoutDamageOrRolls()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Shatterer, attacker: true), Of(Squire));
        var rng = new ScriptedRng([]);              // ни одной кости в запасе — и не понадобится

        var r1 = Attacks.Apply(a, Attack(ShatterAir), t, rng, C, round: 1);
        Assert.True(r1.Applied);
        Assert.Equal(0, r1.Damage);                 // по HP не бьёт вовсе
        Assert.Equal(t.HpMax, t.Hp);
        Assert.Equal(15, t.ShatteredArmor);

        var r2 = Attacks.Apply(a, Attack(ShatterAir), t, rng, C, round: 1);
        Assert.Equal(30, t.ShatteredArmor);          // накапливается: у B здесь осталось бы 15
        Assert.Equal(0, r2.Damage);
        Assert.Equal(0, rng.Used);
        output.WriteLine($"Shatter ×2: снято {t.ShatteredArmor}, урон {r1.Damage}/{r2.Damage}, бросков {rng.Used}");
    }

    /// <summary>Потолок 100 стоит на НАКОПЛЕННОМ, а не на ударе: <c>0x621ea0</c> сравнивает поле
    /// <c>+0x9c</c> с <c>0x64</c> уже после сложения. Семь ударов по 15 дают 100, а не 105.</summary>
    [SkippableFact] public void Shatter_ClampsAccumulatedAtHundred()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Shatterer, attacker: true), Of(Squire));
        var rng = new ScriptedRng([]);
        for (int i = 0; i < 7; i++) Attacks.Apply(a, Attack(ShatterAir), t, rng, C, round: 1);

        Assert.Equal(100, t.ShatteredArmor);          // 7 × 15 = 105, но потолок 100
        Assert.Equal(0, rng.Used);
        output.WriteLine($"семь ударов по 15: снято {t.ShatteredArmor}");
    }

    /// <summary>Снятое и правда вычитается из брони цели, но <b>ниже фортификации броня не падает</b>
    /// — <c>computeArmor</c> (<c>0x5d2c8f</c>) считает <c>max(база − снятое, 0) + фортификация</c>
    /// (<c>0x5d2d02</c>–<c>0x5d2d18</c>), а вне города фортификация нулевая, то есть нижняя граница
    /// ноль. Значит снятое сверх базовой брони пропадает: у цели с бронёй 20 второй удар по 15
    /// счётчик поднимает, а результат — нет.
    /// <para>Первая редакция этого теста ждала −10 и упала. Правильный ответ 0, и он тоже ранга 1:
    /// про эту нижнюю границу не говорят ни A, ни B.</para>
    /// <para>Задача 16 уточнила формулу по корпусу — укрепление прибавляется, а не служит полом
    /// (<see cref="Formulas.Armor"/>), — но ни одно утверждение ЭТОГО теста не изменилось: когда
    /// снятого больше базы, обе редакции дают ровно фортификацию.</para></summary>
    [SkippableFact] public void Shatter_ArmorNeverFallsBelowFortification()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(Shatterer, attacker: true), Of(Imp));
        Assert.Equal(20, Formulas.Armor(t, C));

        var rng = new ScriptedRng([]);
        Attacks.Apply(a, Attack(ShatterAir), t, rng, C, round: 1);
        Assert.Equal(5, Formulas.Armor(t, C));            // 20 − 15

        Attacks.Apply(a, Attack(ShatterAir), t, rng, C, round: 1);
        Assert.Equal(30, t.ShatteredArmor);               // счётчик растёт дальше базовой брони
        Assert.Equal(0, Formulas.Armor(t, C));            // но результат упирается в фортификацию 0
        Assert.Equal(25, Formulas.ComputeDamage(25, Formulas.Armor(t, C)));

        // Фортификация и есть эта граница, а не слагаемое: в городе она держит броню на своём числе.
        t.FortificationArmor = 40;
        Assert.Equal(40, Formulas.Armor(t, C));
        Assert.Equal(0, rng.Used);
        output.WriteLine($"броня {Rules.Value.Units[Imp].Armor} при снятом {t.ShatteredArmor}: " +
                         "вне города 0, при фортификации 40 — 40");
    }

    /// <summary>Иммунитет по классу (<c>GimmuC</c>) Shatter спрашивает — в отличие от вардов: у него
    /// своя <c>isImmune</c> <c>0x65d899</c> с прямым вызовом <c>isUnitImmuneToAttack</c>. Пять
    /// драконоподобных юнитов ванили не крошатся вовсе, и это единственная причина, по которой те
    /// пять строк данных вообще что-то значат.</summary>
    [SkippableFact] public void Shatter_IsBlockedByClassImmunity_UnlikeWards()
    {
        ImportedData.RequireRules();
        Assert.True(Immunity.Checked(AttackClass.Shatter));
        Assert.False(Immunity.Checked(AttackClass.BestowWards));

        var (a, t) = (Of(Shatterer, attacker: true), Of(Myzrael));
        var r = Attacks.Apply(a, Attack(ShatterAir), t, new ScriptedRng([]), C, round: 1);

        Assert.True(r.Immune);
        Assert.False(r.Miss);                       // промах и иммунитет — разные исходы
        Assert.Equal(0, t.ShatteredArmor);
        Assert.Equal(0, t.ClassImmunityUsed);       // Always заряда не тратит
        output.WriteLine($"{Myzrael}: GimmuC класс 25 Always — брони не снято");
    }

    // ---- BestowWards (24) -----------------------------------------------------------------------

    /// <summary>Четыре варда одной атакой, ноль бросков, и каждый даёт <c>Once</c> к своему
    /// источнику. Ложатся на СВОЕГО — класс в <see cref="Attacks.TargetsAllies"/> (слот 2 vftable,
    /// <c>getAllyOrEnemyGroupId(..., push 1)</c>).</summary>
    [SkippableFact] public void BestowWards_PlacesAllFourWards_WithoutRolls()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(WardGiver, attacker: true), Of(Squire, attacker: true, slot: 1));
        var rng = new ScriptedRng([]);

        var r = Attacks.Apply(a, Attack(WardsFour), t, rng, C, round: 1, State(a, t));

        Assert.True(r.Applied);
        Assert.Equal(0, rng.Used);
        Assert.Equal(4, t.Wards.Count);
        Assert.True(Attacks.TargetsAllies(AttackClass.BestowWards));
        foreach (var source in new[] { 7, 5, 6, 4 })       // Air, Water, Earth, Fire
            Assert.Equal(ImmunityLevel.Once,
                         Immunity.LevelOf(t, Attack(WardsFour) with { Source = source }));
        output.WriteLine($"{WardsFour}: легло {t.Wards.Count} вардов, бросков {rng.Used}");
    }

    /// <summary>Вард и правда работает: Оруженосец без единой строки в <c>Gimmu</c> после варда огня
    /// не берёт огненный удар — один раз, потому что уровень <c>Once</c>. Второй удар проходит и
    /// тратит обычные три кости.</summary>
    [SkippableFact] public void BestowWards_FireWard_BlocksExactlyOneFireHit()
    {
        ImportedData.RequireRules();
        var giver = Of(WardGiver, attacker: true);
        var squire = Of(Squire, attacker: true, slot: 1);
        var caster = Of(FlameCaster);

        Attacks.Apply(giver, Attack(WardsFire), squire, new ScriptedRng([]), C, round: 1, State(giver, squire));
        Assert.Equal(FireWard, Assert.Single(squire.Wards).Id);

        var first = new ScriptedRng([0, 0]);               // точность бросается и по иммунной цели
        Assert.True(Attacks.Apply(caster, Attack(FireBolt), squire, first, C, round: 1).Immune);
        Assert.Equal(squire.HpMax, squire.Hp);
        Assert.Equal(2, first.Used);

        var second = new ScriptedRng([0, 0, 0]);
        var r = Attacks.Apply(caster, Attack(FireBolt), squire, second, C, round: 1);
        Assert.False(r.Immune);
        Assert.Equal(35, r.Damage);
        Assert.Equal(3, second.Used);
        output.WriteLine($"вард {FireWard}: первый огненный удар — иммунитет, второй — {r.Damage} урона");
    }

    /// <summary>Два шага одного <c>onHit</c>, и порядок между ними решает всё.
    /// <list type="number">
    /// <item>Пока вард на цели <b>стоит</b>, повтор пропускается целиком (<c>0x65d448</c> →
    /// <c>jne</c> к следующему варду) — ни слота, ни возврата заряда: потраченный <c>Once</c> так
    /// и остаётся потраченным, и следующий огненный удар проходит.</item>
    /// <item>После границы хода вард снят, и повтор уже <b>кладётся</b> — вот тогда
    /// <c>addUnitToWards</c> (<c>0x62196c</c>) обнуляет бит источника и защита работает снова.</item>
    /// </list>
    /// Без второго шага вард во втором раунде не давал бы ничего; без первого — давал бы бесконечно.
    /// Оба шага ранга 1.</summary>
    [SkippableFact] public void BestowWards_RepeatIsSkipped_ButAfterTurnBoundaryTheChargeReturns()
    {
        ImportedData.RequireRules();
        var giver = Of(WardGiver, attacker: true);
        var squire = Of(Squire, attacker: true, slot: 1);
        var caster = Of(FlameCaster);
        var state = State(giver, squire, caster);

        Attacks.Apply(giver, Attack(WardsFire), squire, new ScriptedRng([]), C, round: 1, state);
        Assert.True(Attacks.Apply(caster, Attack(FireBolt), squire, new ScriptedRng([0, 0]), C, round: 1).Immune);
        Assert.Equal(1 << 4, squire.SourceImmunityUsed);

        // Шаг 1: вард ещё стоит — повтор не делает вообще ничего.
        var again = Attacks.Apply(giver, Attack(WardsFire), squire, new ScriptedRng([]), C, round: 1, state);
        Assert.False(again.Applied);
        Assert.Single(squire.Wards);
        Assert.Equal(1 << 4, squire.SourceImmunityUsed);   // заряд НЕ вернулся
        var through = new ScriptedRng([0, 0, 0]);
        Assert.Equal(35, Attacks.Apply(caster, Attack(FireBolt), squire, through, C, round: 1).Damage);

        // Шаг 2: ход раздавшего снял варды — тот же вард ложится заново и возвращает заряд.
        new BattleSim(state, new ScriptedRng([]), C).BeginTurn(giver);
        Assert.Empty(squire.Wards);
        Assert.Equal(1 << 4, squire.SourceImmunityUsed);   // сама маска на границе хода не чистится

        var fresh = Attacks.Apply(giver, Attack(WardsFire), squire, new ScriptedRng([]), C, round: 2, state);
        Assert.True(fresh.Applied);
        Assert.Equal(0, squire.SourceImmunityUsed);
        Assert.True(Attacks.Apply(caster, Attack(FireBolt), squire, new ScriptedRng([0, 0]), C, round: 2).Immune);
        output.WriteLine("повтор при стоящем варде — ничего; после границы хода — заряд Once возвращён");
    }

    /// <summary>Иммунитета <c>BestowWards</c> не спрашивает вовсе (слот 8 — общая заглушка
    /// <c>0x659913</c>): варды ложатся и на союзника, у которого <c>Always</c> к источнику самой
    /// атаки (у всех трёх вани́льных атак класса SOURCE 2, Life), и заряд <c>Once</c> при этом не
    /// тратится. Цена прежней ошибки была не косметической — заряд уходил бы на наложении варда.</summary>
    [SkippableFact] public void BestowWards_IgnoresImmunityEntirely()
    {
        ImportedData.RequireRules();
        var attack = Attack(WardsFour);
        Assert.Equal(2, attack.Source);                    // Life — иначе тест ничего не проверяет

        var giver = Of(WardGiver, attacker: true);
        var target = Of(Squire, attacker: true, slot: 1);
        target.Type = target.Type with { ImmunitiesBySource = [new ImmunityGrant(2, ImmunityLevel.Always)] };

        var r = Attacks.Apply(giver, attack, target, new ScriptedRng([]), C, round: 1, State(giver, target));

        Assert.True(r.Applied);
        Assert.False(r.Immune);
        Assert.Equal(4, target.Wards.Count);
        Assert.Equal(0, target.SourceImmunityUsed);
    }

    /// <summary>Варды снимает начало хода РАЗДАВШЕГО, а не носителя (<c>BattleSim.BeginTurn</c> →
    /// <c>Immunity.ClearWards</c>) — ранг 1, задача 21 Ф7: <c>beforeBattleTurn</c> <c>0x62673f</c>
    /// читает массив целей самого ходящего (<c>+0x3c</c>, <c>0x621bee</c>). Права вики. Проверяем через
    /// <c>BeginTurn</c>, а не прямым вызовом, чтобы утверждение было о вызове.</summary>
    [SkippableFact] public void BestowWards_AreClearedAtGiversTurn_NotAtBearersTurn()
    {
        ImportedData.RequireRules();
        var giver = Of(WardGiver, attacker: true);
        var squire = Of(Squire, attacker: true, slot: 1);
        // Враг в составе нужен: без него бой уже выигран и BeginTurn отдаёт ход только лекарю.
        var state = State(giver, squire, Of(Squire, id: "enemy"));
        Attacks.Apply(giver, Attack(WardsFour), squire, new ScriptedRng([]), C, round: 1, state);
        Assert.Equal(4, squire.Wards.Count);

        var sim = new BattleSim(state, new ScriptedRng([]), C);
        Assert.True(sim.BeginTurn(squire));
        Assert.Equal(4, squire.Wards.Count);           // ход носителя вардов не снимает
        Assert.True(sim.BeginTurn(giver));
        Assert.Empty(squire.Wards);
        Assert.Empty(giver.BestowedWards);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelOf(squire, Attack(FireBolt)));
    }

    /// <summary>Варды не переживают бой — ранг 1, <c>20260917-synth</c> q59: во втором бою оберега нет (seq 250645),
    /// в сохранении после боя ward-модификаторов нет. В порте вард живёт только в <see cref="BattleUnit.Wards"/>:
    /// конец боя его не переносит в постоянную цепочку (<see cref="BattleUnit.Modifiers"/> — то, что есть у юнита
    /// карты), а следующий бой строит юнит заново — без вардов и с целыми зарядами <c>Once</c> (q52b).</summary>
    [SkippableFact] public void BestowWards_DoNotSurviveTheBattle()
    {
        ImportedData.RequireRules();
        var giver = Of(WardGiver, attacker: true);
        var squire = Of(Squire, attacker: true, slot: 1);
        var state = State(giver, squire, Of(Squire, id: "enemy"));
        Attacks.Apply(giver, Attack(WardsFire), squire, new ScriptedRng([]), C, round: 1, state);
        Assert.True(Attacks.Apply(Of(FlameCaster), Attack(FireBolt), squire, new ScriptedRng([0, 0]), C, round: 1).Immune);
        Assert.NotEqual(0, squire.SourceImmunityUsed);         // заряд Once потрачен в этом бою

        new BattleSim(state, new ScriptedRng([]), C).AfterBattle();

        Assert.DoesNotContain(squire.Modifiers, m => m.Id.Equals(FireWard, StringComparison.OrdinalIgnoreCase));
        var nextBattle = Of(Squire, attacker: true, slot: 1);
        Assert.Empty(nextBattle.Wards);
        Assert.Equal(0, nextBattle.SourceImmunityUsed);
        Assert.Equal(ImmunityLevel.NotImmune, Immunity.LevelOf(nextBattle, Attack(FireBolt)));
    }

    /// <summary>После гибели врага раунд лекарей ещё идёт, и раздающий (его вторая атака — Heal) кладёт вард —
    /// ранг 1, <c>20260917-synth</c> q59 seq 247257 (<c>G000AA8133</c> по своему, раунд 7, врагов нет).
    /// Не-лекарь в том же положении хода не получает и заканчивает бой.</summary>
    [SkippableFact] public void BestowWards_HealerRoundAfterVictory_StillLaysWards()
    {
        ImportedData.RequireRules();
        var giver = Of(WardGiver, attacker: true);
        Assert.True(Attacks.IsHealer(giver));
        var squire = Of(Squire, attacker: true, slot: 1);
        var enemy = Of(Squire, id: "enemy");
        enemy.Hp = 0;
        var sim = new BattleSim(State(giver, squire, enemy), new ScriptedRng([]), C);

        Assert.True(sim.BeginTurn(giver));
        sim.Act(round: 1, giver, squire);
        Assert.Equal(4, squire.Wards.Count);
        Assert.False(sim.Finished);

        Assert.False(sim.BeginTurn(squire));
        Assert.True(sim.Finished);
    }

    /// <summary>Площадная атака вардов (REACH 1) кроет свой отряд, и перемешивание целей стоит
    /// ровно <c>n − 1</c> бросков — тот же <c>Attacks.Shuffle</c>, что у площадного лечения. Больше
    /// сам класс не тратит ни одной кости.</summary>
    [SkippableFact] public void BestowWards_AreaVariant_SpendsOnlyShuffleRolls()
    {
        ImportedData.RequireRules();
        Assert.Equal(AttackType.ReachAll, Attack(WardsFire).Reach);

        var giver = Of(AreaWardGiver, attacker: true);     // его ПЕРВАЯ атака и есть g000aa8134
        Assert.Equal(WardsFire, giver.Attack.Id);
        BattleUnit[] allies = [giver, Of(Squire, attacker: true, slot: 1, id: "a1"),
                                      Of(Squire, attacker: true, slot: 2, id: "a2"),
                                      Of(Squire, attacker: true, slot: 3, id: "a3")];
        var state = State([.. allies, Of(Squire, slot: 0, id: "enemy")]);
        var rng = new ScriptedRng([0, 0, 0]);              // перемешивание четверых: n − 1 = 3

        new BattleSim(state, rng, C).Act(round: 1, giver);

        Assert.Equal(3, rng.Used);
        Assert.All(allies, u => Assert.Equal(FireWard, Assert.Single(u.Wards).Id));
        Assert.Empty(state.Unit("enemy")!.Wards);          // по врагу класс не бьёт
        output.WriteLine($"площадные варды: четверо своих, бросков {rng.Used}");
    }

    /// <summary>Без справочников класс падает с внятным текстом, а не молча ничего не кладёт:
    /// <c>Gattacks.WARD1..4</c> несут только id строк <c>Gmodif</c>, и без таблицы модификаторов
    /// накладывать нечего. Тихое «ничего» испортило бы сверку С2 незаметно.</summary>
    [SkippableFact] public void BestowWards_WithoutRules_FailsLoudly()
    {
        ImportedData.RequireRules();
        var (a, t) = (Of(WardGiver, attacker: true), Of(Squire, attacker: true, slot: 1));
        var ex = Assert.Throws<InvalidOperationException>(
            () => Attacks.Apply(a, Attack(WardsFour), t, new ScriptedRng([]), C, round: 1));
        Assert.Contains("BattleState.Rules", ex.Message);
    }

    /// <summary>Вард — обычный модификатор, поэтому он виден и цепочке эффективных статов
    /// (<see cref="BattleUnit.Chain"/> = постоянные модификаторы плюс варды). Проверяем, что
    /// иммунитет из цепочки и правда приходит через <c>EffectiveStats</c>, а не мимо неё.</summary>
    [SkippableFact] public void Ward_ReachesEffectiveStatsThroughTheChain()
    {
        ImportedData.RequireRules();
        var squire = Of(Squire);
        Assert.Same(squire.Modifiers, squire.Chain);       // без вардов — та же ссылка, без аллокации

        Immunity.Bestow(squire, [Rules.Value.Modifiers![AirWard]]);
        var grants = EffectiveStats.Of(squire.Type, squire.Chain).Immunities;
        Assert.Equal(new ImmunityGrant(7, ImmunityLevel.Once), Assert.Single(grants));
    }
}
