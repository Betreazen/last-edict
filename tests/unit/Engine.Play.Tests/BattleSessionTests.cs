using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Бой по шагам (<see cref="BattleSession"/>) — тот же бой, что у модели целиком: ход за ходом экран ведёт <see cref="BattleSim"/> в
/// порядке <see cref="BattleSim.Run"/>. Бой — самая людная пара отрядов своей карты <c>twin-fields</c> (<c>content/test-pack</c>, пары — как у
/// боёв снимков <see cref="PortSnapshot.Battles"/>): идёт в CI без данных игры. Предметам оригинала (сферы, зелье воскрешения, призыв) нужен
/// импорт — их бой собирается так же, на карте <c>Mercantile</c>. В эталоне бой брался из журнала зонда (<c>corpus/sample/one_battle.jsonl</c>);
/// здесь его нет, и проверки, что «в образце нет хода человека», не нужны: бой своей карты всегда доходит до хода нападающего (человека).
/// <para>Генератор боя — свой поток (<see cref="RngStreams.Derive"/>, <c>docs/own-rules.md</c> §2), инициатива бросается на раунд вперёд (§3):
/// числа бросков у эталона другие, поэтому тесты сверяют не броски, а свойства шагов — тот же бой целиком и по шагам, команды хода, цели.</para>
/// <para>Уже покрыто соседями и не повторяется: «Ждать» — ход в конце того же раунда, раз за раунд (<c>BattleSessionQueueTests</c>,
/// <c>BattleSessionOrderTests</c>, <c>BattleSessionHoldTests</c>).</para></summary>
public class BattleSessionTests(ITestOutputHelper output)
{
    private const string PackMap = "twin-fields";
    private const ulong Seed = 1;

    private static readonly Lazy<GameBook> Pack = new(() => GameBook.Load(TestPackTests.Dir));

    private static readonly Lazy<MapState> PackField = new(() =>
        ScenarioFile.Read(ScenarioFile.PathOf(Path.Combine(TestPackTests.Dir, "scenarios"), PackMap)).Looks.Map);

    private static GameRules PackRules => Pack.Value.Rules;

    /// <summary>Бой пары отрядов карты, в которой больше всего юнитов: отряды — непустые, в поле, с живым лидером, парами по id, как у
    /// <see cref="PortSnapshot.Battles"/> (первый против второго, третий против четвёртого…). Новый на каждый вызов.</summary>
    private static BattleState MapFight(MapState map, GameBook book)
    {
        var stacks = map.Stacks.Values.Where(s => s.Inside.Length == 0 && s.UnitIds.Count > 0 && s.LeaderAlive)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        Assert.True(stacks.Count >= 2, "на карте нет пары отрядов");
        var (attacker, defender) = Enumerable.Range(0, stacks.Count / 2).Select(i => (stacks[2 * i], stacks[(2 * i) + 1]))
            .MaxBy(p => p.Item1.UnitIds.Count + p.Item2.UnitIds.Count);
        return MapBattle.Build(map, attacker.Id, defender.Id, book, difficulty: 1, attackerMove: 0);
    }

    private static BattleState PackFight() => MapFight(PackField.Value, Pack.Value);

    private static BattleSession Session(BattleState state, GameRules rules) =>
        new(state, new BattleSim(state, RngStreams.Derive(Seed, RngStream.Battle), rules.Constants), rules.Constants);

    /// <summary>Сессия боя своей карты, доведённая ИИ до первого хода человека (нападающего).</summary>
    private static BattleSession AtPlayerTurn(BattleState state, GameRules rules)
    {
        var session = Session(state, rules);
        while (!session.Finished && !session.WaitsPlayer) session.AiStep();
        Assert.False(session.Finished, "бой кончился до хода человека");
        return session;
    }

    /// <summary>Ход без удара — для показа надписью и вспышкой (<see cref="BattleSession.TurnShows"/>, шаги просмотра 9 и 10): «Защищаться» и
    /// «Ждать» человека записываются ходящему.</summary>
    [Fact]
    public void TurnShows_DefendAndWait()
    {
        var session = AtPlayerTurn(PackFight(), PackRules);
        var defender = session.Current!;
        Assert.True(session.Defend());
        Assert.Equal(new BattleTurnNote(defender.Id, BattleTurnShow.Defend), session.TurnShows[^1]);
        while (!session.Finished && !(session.WaitsPlayer && session.Allowed.Contains(BattleCommand.Wait))) session.AiStep();
        Assert.False(session.Finished, "бой кончился до «Ждать»");
        var waiter = session.Current!;
        Assert.True(session.Wait());
        Assert.Equal(new BattleTurnNote(waiter.Id, BattleTurnShow.Wait), session.TurnShows[^1]);
    }

    /// <summary>Быстрый бой по шагам (<see cref="BattleSession.Resolve"/>) и бой модели целиком (<see cref="BattleSim.Run"/>) на одном зерне —
    /// один и тот же бой: те же записи ходов в том же порядке и тот же победитель.</summary>
    [Fact]
    public void Session_PlaysTheSameBattleAsRun()
    {
        var rules = PackRules;
        var whole = new BattleSim(PackFight(), RngStreams.Derive(Seed, RngStream.Battle), rules.Constants);
        var expected = whole.Run().ToList();

        var session = Session(PackFight(), rules);
        var got = session.Resolve(200).ToList();

        output.WriteLine($"ходов целиком {expected.Count}, по шагам {got.Count}, победитель {session.Winner ?? "-"}");
        Assert.NotEmpty(expected);
        Assert.Equal(whole.Winner, session.Winner);
        Assert.Equal(expected.Count, got.Count);
        for (int i = 0; i < expected.Count; i++) Assert.Equal(expected[i], got[i]);
    }

    /// <summary>На ходу игрока экран ждёт команду, и доступны те действия, что и в игре: удар, защита, пропуск, отступление
    /// (<c>docs/battle-screen.md</c> §1); цели — те, по которым удар принимается.</summary>
    [Fact]
    public void PlayerTurn_OffersEngineActions()
    {
        var session = AtPlayerTurn(PackFight(), PackRules);

        var allowed = session.Allowed;
        output.WriteLine($"ходит {session.Current!.Id}, доступно {string.Join(", ", allowed)}, целей {session.Targets().Count}");
        Assert.Contains(BattleCommand.Defend, allowed);
        Assert.Contains(BattleCommand.Skip, allowed);
        Assert.Contains(BattleCommand.Retreat, allowed);
        Assert.Contains(BattleCommand.Attack, allowed);
        Assert.NotEmpty(session.Targets());
        Assert.All(session.Targets(), t => Assert.True(session.CanTarget(t)));
    }

    /// <summary>Щелчок по негодной цели (<c>0x63cce6</c>): воскрешение по павшему своему — <c>X008TA0031</c>, двойник по стражу —
    /// <c>X008TA0032</c>, прочее — ничего. Атак воскрешения и двойника в test-pack нет — они собраны здесь; страж — свой (<c>test:unit/…</c>
    /// с ролью «страж»).</summary>
    [Fact]
    public void RefusalText_ReviveOnceAndGuardianCopy()
    {
        var rules = PackRules;
        var state = PackFight();
        var session = AtPlayerTurn(state, rules);
        var me = session.Current!;
        var enemy = state.Units.First(u => u.Attacker != me.Attacker);
        // Своих рядом может не быть: свой юнит — на свободной клетке (сторона цели — то, что проверяет движок).
        var other = state.Units.FirstOrDefault(u => !ReferenceEquals(u, me) && u.Attacker == me.Attacker);
        if (other is null)
        {
            other = new BattleUnit
            {
                Id = "ALLY", Type = me.Type, Attack = me.Attack, Attack2 = me.Attack2, Attacker = me.Attacker,
                Slot = Enumerable.Range(0, 6).First(s => !state.Units.Any(u => u.Attacker == me.Attacker && u.Slot == s)),
                HpMax = me.HpMax, Hp = me.HpMax,
            };
            state.Units.Add(other);
        }
        Assert.Null(session.RefusalText(other));

        // Движок признак «воскрешён» не читает: павший своей стороны, негодный для воскрешения, — сообщение; чужой павший — ничего
        // (0x63e370 = +0xfa1, 0x5e98ad).
        me.Attack = new AttackType("test:attack/revive", 10, AttackClass.Revive, 100, 2, 0, 0, false, 1);
        Assert.Null(session.RefusalText(other));
        other.Hp = 0;
        other.Revived = true;
        Assert.Equal(BattleSession.RevivedOnce, session.RefusalText(other));
        enemy.Hp = 0;
        enemy.Revived = true;
        Assert.Null(session.RefusalText(enemy));

        me.Attack = new AttackType("test:attack/doppelganger", 10, AttackClass.Doppelganger, 100, 2, 0, 0, false, 1);
        other.Hp = other.HpMax;
        Assert.Null(session.RefusalText(other));
        other.Type = rules.Units.Values.First(u => u.Category == UnitCategory.Guardian);
        Assert.Equal(BattleSession.NoGuardianCopy, session.RefusalText(other));
    }

    /// <summary>Бой двух людей «горячего стула»: автобой у каждой стороны свой (<c>data2+0x38</c>/<c>+0x39</c>) — включённый у защитника,
    /// он не ведёт нападающего: их юнитам команду ждёт экран (партия 28.09 эталона, <c>corpus/20260929-hotseat</c>).</summary>
    [Fact]
    public void Auto_BothHuman_EachSideOwnFlag()
    {
        var rules = PackRules;
        var state = PackFight();
        var session = new BattleSession(state, new BattleSim(state, RngStreams.Derive(Seed, RngStream.Battle), rules.Constants), rules.Constants)
        {
            BothHuman = true,
        };
        session.SetAuto(attacker: false, on: true);

        int attackers = 0;
        for (int i = 0; i < 500 && !session.Finished; i++)
        {
            var unit = session.Current!;
            Assert.Equal(unit.Attacker, session.WaitsPlayer);   // защитники идут сами, нападающие ждут команду
            if (!session.WaitsPlayer)
            {
                session.AiStep();
                continue;
            }
            attackers++;
            session.Defend();
        }
        output.WriteLine($"ходов нападающих вручную {attackers}, автобой: нап={session.AutoAttackers} защ={session.AutoDefenders}");
        Assert.True(attackers > 0);
        Assert.False(session.Auto);
        session.Auto = true;
        Assert.True(session.AutoOf(true) && session.AutoOf(false));
    }

    /// <summary>Защита кончает ход и ставит признак защиты — до начала следующего своего хода (его снимает <see cref="BattleSim"/> в начале хода
    /// юнита). С удержанием начала хода, как на экране, признак виден сразу после команды; следующий ходящий — по
    /// <see cref="BattleSession.StartTurn"/>: другой юнит — признак держится; сам защитник (последний в раунде и первый в следующем) — снят
    /// началом его хода. В эталоне защитник образца не ходил дважды подряд, и тест ждал «следующий — другой»; бой своей карты такой случай
    /// даёт, поэтому проверяются оба.</summary>
    [Fact]
    public void Defend_EndsTurn_DefendingUntilOwnNextTurn()
    {
        var rules = PackRules;
        var state = PackFight();
        var session = new BattleSession(state, new BattleSim(state, RngStreams.Derive(Seed, RngStream.Battle), rules.Constants), rules.Constants)
        {
            HoldTurnStart = true,
        };
        while (!session.Finished && !session.WaitsPlayer) session.AiStep();
        Assert.False(session.Finished, "бой кончился до хода человека");

        var unit = session.Current!;
        Assert.True(session.Defend());
        Assert.True(unit.Defending);
        Assert.True(session.TurnStartPending);
        Assert.Null(session.Current);

        session.StartTurn();
        output.WriteLine($"защищался {unit.Id}, следующий {session.Current?.Id ?? "-"}, раунд {session.Round}");
        Assert.NotNull(session.Current);
        Assert.Equal(!ReferenceEquals(unit, session.Current), unit.Defending);
    }

    /// <summary>Свой бой: боец (ранен, если <paramref name="woundedFighter"/>), лекарь (лечит любого своего) против одного врага с 10 ОЗ.</summary>
    private static (BattleState State, BattleUnit Fighter, BattleUnit Healer, BattleUnit Enemy) HealerBattle(bool woundedFighter)
    {
        static BattleUnit Unit(string id, bool attacker, int slot, int hp, int hpMax, AttackType attack) => new()
        {
            Id = id, Attacker = attacker, Slot = slot, Hp = hp, HpMax = hpMax, Attack = attack,
            Type = new UnitType("test:unit/" + id, 1, hpMax, 0, attack.Id, "", false, true, 10, 100),
        };
        var strike = new AttackType("test:attack/strike", 90, AttackClass.Damage, 100, 2, 100, 0, false, 1);
        var heal = new AttackType("test:attack/heal", 10, AttackClass.Heal, 100, 2, 0, 20, false, 1);
        var weak = new AttackType("test:attack/weak", 50, AttackClass.Damage, 100, 2, 1, 0, false, 1);
        var fighter = Unit("F", true, 0, woundedFighter ? 50 : 100, 100, strike);
        var healer = Unit("H", true, 1, 60, 60, heal);
        var enemy = Unit("E", false, 0, 10, 10, weak);
        return (new BattleState { Units = [fighter, healer, enemy], AttackerGroup = "A", DefenderGroup = "D" }, fighter, healer, enemy);
    }

    /// <summary>Замечание 18 прогона 11.10 (переход раунда 0x62689c, 0x626d49): боец добил последнего врага, у победителя есть раненый —
    /// лекарь-человек ходит ещё раз, цели — только раненые свои, и из команд — только лечение (ответ пользователя 11.10: ни «Защищаться», ни
    /// «Пропустить», ни «Ждать», ни «Отступить», ни предметов). Начало хода лекаря при удержании и его полосу проверяют
    /// <c>BattleSessionHoldTests</c> и <c>BattleSessionQueueTests</c>; здесь — полный набор команд и целей.</summary>
    [Fact]
    public void Healer_TurnAfterVictory_WhenWoundedAlly()
    {
        var (state, fighter, healer, enemy) = HealerBattle(woundedFighter: true);
        var session = Session(state, PackRules);
        for (int guard = 0; guard < 100 && session.Winner is null; guard++)
        {
            if (!session.WaitsPlayer) session.AiStep();
            else if (ReferenceEquals(session.Current, fighter)) session.Attack(enemy);
            else session.Defend();
        }
        Assert.False(enemy.Alive);

        Assert.True(session.AfterBattle);
        Assert.False(session.Finished);
        Assert.Same(healer, session.Current);
        Assert.True(session.WaitsPlayer);
        Assert.Equal([fighter], session.Targets());
        Assert.Equal([BattleCommand.Attack, BattleCommand.Auto, BattleCommand.Resolve], session.Allowed.Order());
        int hp = fighter.Hp;
        session.Attack(fighter);

        Assert.True(fighter.Hp > hp);
        Assert.True(session.Finished);
        Assert.False(session.AfterBattle);
    }

    /// <summary>Раненых у победителя нет — ходов лекарей нет, бой окончен сразу после удара (корпус эталона: 3 таких боя без хода лекаря).</summary>
    [Fact]
    public void Healer_NoTurnAfterVictory_WhenNobodyWounded()
    {
        var (state, fighter, _, enemy) = HealerBattle(woundedFighter: false);
        var session = Session(state, PackRules);
        session.Auto = true;
        session.PlayOut(100);

        Assert.False(enemy.Alive);
        Assert.True(session.Finished);
        Assert.False(session.AfterBattle);
        Assert.Equal(fighter.HpMax, fighter.Hp);
        Assert.DoesNotContain(session.Log, r => r.Unit == "H" && r.Target == "F");
    }

    // --- предметы оригинала: импорт ---

    /// <summary>Правила и бой самой людной пары отрядов импортированного сценария <c>Mercantile</c>, доведённый до первого хода человека; ходящий —
    /// лидер (предметы боя носит лидер).</summary>
    private static (GameBook Book, BattleState State, BattleSession Session, BattleUnit Leader) ImportedFight()
    {
        ImportedData.Require("scenarios");
        var book = GameBook.Load(ImportedData.Content);
        var map = ScenarioFile.Read(ScenarioFile.PathOf(ImportedData.PathOf("scenarios"), "Mercantile")).Looks.Map;
        var state = MapFight(map, book);
        var session = AtPlayerTurn(state, book.Rules);
        var leader = session.Current!;
        leader.Type = leader.Type with { IsLeader = true };
        return (book, state, session, leader);
    }

    /// <summary>Цели боевого предмета (0x62425b): зелье лечения — живые раненые своей группы, воскрешения — павшие свои,
    /// сфера — как атака лидера этим классом; брошенное мимо цели не применяется; применённый предмет второй раз целей не даёт.</summary>
    [SkippableFact]
    public void ItemTargets_PotionsAndOrb()
    {
        var (book, state, session, leader) = ImportedFight();
        var rules = book.Rules;
        var items = book.Items.Values;
        var heal = items.First(i => i.Category == ItemCategory.PotionHeal);
        var revive = items.First(i => i.Category == ItemCategory.PotionRevive);
        leader.BattleItems.Add(heal);
        leader.BattleItems.Add(revive);

        var own = state.Units.Where(u => u.Attacker == leader.Attacker && !u.Retreated && !u.Hidden).ToList();
        leader.Hp = Math.Max(1, leader.HpMax / 2);   // на первом ходу раненых и павших нет — сделать: раненый лидер, павший воин (если есть)
        if (own.FirstOrDefault(u => !ReferenceEquals(u, leader)) is { } fallen) fallen.Hp = 0;
        Assert.Contains(leader, session.ItemTargets(0));
        Assert.Equal(own.Where(u => u.Alive && u.Hp < u.HpMax), session.ItemTargets(0));
        Assert.Equal(own.Where(u => !u.Alive), session.ItemTargets(1));
        Assert.Equal(session.ItemTargets(0).Count + session.ItemTargets(1).Count > 0, session.Allowed.Contains(BattleCommand.UseItem));

        var enemy = state.Units.First(u => u.Attacker != leader.Attacker && u.OnField);
        Assert.False(session.UseItemOn(0, enemy));   // мимо цели — ничего
        Assert.Same(leader, session.Current);

        leader.BattleItems.Clear();
        var orb = items.First(i => i.Category == ItemCategory.Orb && rules.Attacks.TryGetValue(i.AttackId, out var a)
                                   && a.Class == AttackClass.Damage);   // по врагам — цели есть всегда
        leader.BattleItems.Add(orb);
        var expected = BattleTargets.Possible(state, leader, rules.Attacks[orb.AttackId]);
        output.WriteLine($"лидер {leader.Id}: сфера {orb.Id} — целей {expected.Count}");
        Assert.NotEmpty(expected);
        Assert.Equal(expected, session.ItemTargets(0));
        leader.AppliedItems.Add(0);
        Assert.Empty(session.ItemTargets(0));
    }

    /// <summary>Предмет-призыв: цели — свободные позиции своей группы (EMPTYSELSMALL), призванный встаёт на выбранную.</summary>
    [SkippableFact]
    public void ItemSummon_IntoChosenSlot()
    {
        var (book, state, session, leader) = ImportedFight();
        var rules = book.Rules;
        var orb = book.Items.Values.First(i => rules.Attacks.TryGetValue(i.AttackId, out var a) && a.Class == AttackClass.Summon
                                               && rules.SummonCandidates(a.Id, smallSlot: true).Count == 1);
        leader.BattleItems.Add(orb);
        var slots = session.ItemSummonSlots(0);
        output.WriteLine($"сфера {orb.Id}: свободные позиции {string.Join(",", slots)}");
        Skip.If(slots.Count == 0, "группа полна");
        Assert.Empty(session.ItemTargets(0));
        Assert.Contains(BattleCommand.UseItem, session.Allowed);
        int chosen = slots[^1];
        int before = state.Units.Count;
        Assert.True(session.UseItemAt(0, chosen));
        var summoned = state.Units[before];
        Assert.Equal(chosen, summoned.Slot);
        Assert.Equal(leader.Attacker, summoned.Attacker);
        Assert.Null(state.SummonSlot);
    }

    /// <summary>Применение предмета ложится в <see cref="BattleSession.ItemUses"/> для показа (шаг 12, <c>0x6365f2</c>): Сфера грома (<c>g000ig9027</c>)
    /// — атака предмета (урон, «−N» на цели, а не строка класса носителя), юнит предмета — Ученик (<c>g000uu0008</c>, его <c>TUCH</c> — молния на
    /// цели); зелье лечения — атака своего юнита (<c>GItem.UNIT_ID</c>), «+N». Записей хода (<see cref="BattleSession.Log"/>) применение не даёт.
    /// Ключи — id содержимого (<see cref="LegacyIds.Base"/>), строки интерфейса — id оригинала.</summary>
    [SkippableFact]
    public void ItemUse_RecordedForShow_OrbAndPotion()
    {
        var (book, state, session, leader) = ImportedFight();
        var rules = book.Rules;
        var items = book.Items;
        var orb = items[Key("g000ig9027")];
        leader.BattleItems.Add(orb);
        var target = session.ItemTargets(0).First();
        int log = session.Log.Count, hp = target.Hp;
        Assert.True(session.UseItemOn(0, target));

        var use = Assert.Single(session.ItemUses);
        Assert.Equal(log, session.Log.Count);
        Assert.Equal((leader.Id, target.Id, hp, target.Hp), (use.Bearer, use.Target, use.TargetHpBefore, use.TargetHpAfter));
        Assert.Equal(AttackClass.Damage, use.Shown?.Class);
        Assert.Equal(Key("g000uu0008"), use.Item.UnitId, ignoreCase: true);
        Assert.Equal(hp - target.Hp, use.Record.Damage);
        var note = Assert.Single(use.Notes.After([use.Record], state, id => id == "X008TA0004" ? "%DAMAGE%" : id));
        Assert.Equal(use.Immune ? "X008TA0002" : $"-{hp - target.Hp}", note.Markup);

        var heal = items.Values.First(i => i.Category == ItemCategory.PotionHeal);
        Assert.Equal(AttackClass.Heal, BattleItemUse.ShownAttack(heal, rules)?.Class);
        // «Сила» зелья силы g000ig0003 (журнал эталона 20261016e-av), хотя атака его юнита g000uu0161 — «дар атаки».
        var strength = BattleItemUse.ShownAttack(items[Key("g000ig0003")], rules);
        Assert.Equal(AttackClass.BoostDamage, strength?.Class);
        var boost = Assert.Single(StrikeNotes.Before(state, leader, strength)
            .After([new TurnRecord(state.Round, leader.Id, leader.Id, false, 0, leader.Hp)], state, id => id));
        Assert.Equal("X008TA0003", boost.Markup);
    }

    private static string Key(string legacyId) => LegacyIds.Base.IdOf(legacyId) ?? throw new KeyNotFoundException(legacyId);
}
