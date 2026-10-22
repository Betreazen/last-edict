using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Опыт за бой и повышения после него — по правилам, сверенным харнессом опыта с журналами
/// (<see cref="Experience"/>, <see cref="UnitUpgrade"/>): опыт копится за каждое действие, убившее врагов,
/// делится между делящими в этот момент и начисляется выжившим по пути повышения, затем уровни.
///
/// <para>Действие собирается из журнала модели (<see cref="BattleSim.Log"/>): подряд идущие удары одного юнита
/// одной атакой в одном раунде. Смерть от тика яда, мороза, ожога в начале хода журнал модели не пишет — её место
/// даёт <see cref="BattleState.DotDeaths"/>; опыт за неё движок раздаёт в конце следующего действия, одной суммой с
/// убитыми этим действием юнитами той же стороны, и сам умерший в этой раздаче ещё делит (<see cref="Experience.Receives"/>).
/// Ранг 1 по корпусу — харнесс опыта (<c>ExperienceHarnessTests</c>, бой seq 2669822 20261014-replay ai-q57: ⌊(55 + 20)/3⌋
/// = 25, а не 18 + 6); прежде такие смерти делились в конце боя отдельно (ранг 3), тут давало 50 вместо 51. Смерть от
/// тика без следующего действия (бой кончился) — в конце боя, как раньше (ранг 3). Не сделано: Инструктор (<c>WEAPN_MSTR</c>).
/// «Тип следующего уровня уже создан в этом запуске» у лидера — <see cref="GeneratedImpls"/> (И241).</para></summary>
public static class BattleAftermath
{
    /// <summary>Действия, убившие юнитов: сумма <c>XP_KILLED</c> убитых и кто делит опыт в этот момент.</summary>
    /// <param name="hpBefore">Здоровье юнитов до боя.</param>
    public static IReadOnlyList<Kill> Kills(BattleState state, IReadOnlyDictionary<string, int> hpBefore,
                                            IReadOnlyList<TurnRecord> log, GameBook book)
    {
        var hp = new Dictionary<string, int>(hpBefore, StringComparer.OrdinalIgnoreCase);
        var killed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kills = new List<Kill>();
        var dots = state.DotDeaths;
        int d = 0;
        for (int i = 0; i < log.Count;)
        {
            // Умершие от тика до этого действия: опыт за них ещё не роздан (XpCounted снят) — раздаст это действие.
            var uncounted = new List<BattleUnit>();
            for (; d < dots.Count && dots[d].LogIndex <= i; d++)
                if (state.Unit(dots[d].UnitId) is { } v && killed.Add(v.Id)) { hp[v.Id] = 0; uncounted.Add(v); }
            var first = log[i];
            var gone = Gone(state, i);
            var dead = new List<BattleUnit>();
            for (; i < log.Count && log[i].Round == first.Round && log[i].Unit == first.Unit && log[i].Attack == first.Attack; i++)
            {
                var r = log[i];
                bool wasAlive = hp.GetValueOrDefault(r.Target, 1) > 0;
                hp[r.Target] = r.TargetHpAfter;
                if (wasAlive && r.TargetHpAfter <= 0 && state.Unit(r.Target) is { } victim && killed.Add(victim.Id)) dead.Add(victim);
                if (r.TargetHpAfter > 0) killed.Remove(r.Target);   // воскрешён — его новая смерть снова даёт опыт
            }
            var sharing = uncounted.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var side in dead.Concat(uncounted).GroupBy(u => u.Attacker))
                kills.Add(KillOf(state, side, hp, book, sharing, gone));
        }
        // Смерти без удара и без следующего действия — делим в конце боя. Павший ещё до боя (в бою лежит костями, MapBattle.Build) —
        // не смерть этого боя.
        foreach (var side in state.Units.Where(u => u.Hp <= 0 && !u.Summoned && !killed.Contains(u.Id) && hpBefore.GetValueOrDefault(u.Id) > 0)
                                        .GroupBy(u => u.Attacker))
            kills.Add(KillOf(state, side, EndHp(state), book, gone: Gone(state, log.Count)));
        return kills;
    }

    /// <summary>Ушедшие с поля к записи <paramref name="logIndex"/> журнала модели (бит 28): делитель опыта <c>0x625a8e</c> их не считает
    /// (<c>0x625bd0</c>), и доли они не получают. Карта 23, день 48 (бой seq 8187805): S143UN0091 бежал от страха G000UU0073 в раунде 2,
    /// и 50 за убитого в раунде 3 делят четверо — по 12 (<c>xp_group</c> acc 55 у них, 43 у бежавшего); прежде порт делил на пятерых (И230).</summary>
    private static HashSet<string>? Gone(BattleState state, int logIndex) =>
        state.Retreats.Count == 0 ? null
        : state.Retreats.Where(r => r.LogIndex <= logIndex).Select(r => r.UnitId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int> EndHp(BattleState state) =>
        state.Units.ToDictionary(u => u.Id, u => u.Hp, StringComparer.OrdinalIgnoreCase);

    /// <param name="sharing">Мёртвые, чей опыт раздаётся этим же действием: они ещё делят (<see cref="Experience.Receives"/>).</param>
    /// <param name="gone">Ушедшие с поля к этому действию (<see cref="Gone"/>) — не делят.</param>
    private static Kill KillOf(BattleState state, IEnumerable<BattleUnit> dead, IReadOnlyDictionary<string, int> hp, GameBook book,
                               IReadOnlySet<string>? sharing = null, IReadOnlySet<string>? gone = null)
    {
        var victims = dead.ToList();
        bool victimsAttack = victims[0].Attacker;
        // XP_KILLED убитого — его исходной реализации: сумма 0x625a8e берёт солдата 0x5ea4dd (превращённый — origTypeId; 0x625b93).
        // Карта 17 (19.10, [5062040]): Бес из Гнома S143UN000d убит последним — 35 (G000UU5236), а не 10 Беса; опыт выживших 157.
        int xpKilled = victims.Sum(v => (v.OrigType ?? v.Type).XpKilled);
        var receivers = state.Units.Where(u => u.Attacker != victimsAttack && gone?.Contains(u.Id) != true
                                               && Experience.Receives(book.CategoryOf(u.Type.Id) ?? u.Type.Category,
                                                                      hp.GetValueOrDefault(u.Id, u.Hp) > 0
                                                                      || sharing?.Contains(u.Id) == true, u.Hidden))
                                   .ToList();
        return new Kill(xpKilled, [.. receivers.Select(u => u.Id)], receivers.Count(u => u.Summoned));
    }

    /// <summary>Начислить опыт выжившим и повысить их. <paramref name="map"/> — карта уже после
    /// <see cref="MapBattle.Apply"/> (здоровье и потери записаны).</summary>
    /// <param name="generated">Порождённые реализации, созданные в этом запуске (<see cref="GeneratedImpls"/>): дополняется юнитами карты и
    /// повышениями боя. null — только юниты карты.</param>
    public static MapState Award(MapState map, BattleState state, IReadOnlyList<Kill> kills, GameBook book, int difficulty,
                                 int suggestedLevel, IReadOnlyDictionary<string, string>? subraceOwners = null, ISet<string>? generated = null)
    {
        generated ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        GeneratedImpls.Observe(generated, map, book);
        var accumulated = Experience.Accumulate(kills);
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        // Опыт получает каждый живой участник боя, а не только деливший убитых: upgradeGroup (0x665217) идёт по всем занятым слотам стороны
        // (xp_group), и прибавка не меньше 1 (Experience.GroupAmount, 0x665675) — и у бежавшего с поля. Карта 5 (20261016e-av, день 12):
        // S143UN002a бежал из боя с S143KC0005 — acc 0, applied 1, опыт 111 → 112, и дневной чит поднимает лидера до порога (UpgradeLeader
        // того же хода); павший S143UN003d и скрытые в дуэли (карта 23, день 55) — 0. Прежде порт давал опыт только по долям убитых (И154).
        var fighters = accumulated.Keys.Concat(state.Units.Where(u => u.Alive && !u.Hidden && !u.Unsummoned).Select(u => u.Id))
                                  .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stackOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stacks.Values)
            foreach (string u in s.UnitIds) stackOf.TryAdd(u, s.Id);
        foreach (string id in fighters)
        {
            int acc = accumulated.GetValueOrDefault(id);
            if (acc < 0 || !units.TryGetValue(id, out var unit) || unit.Hp <= 0) continue;
            if (!stackOf.TryGetValue(id, out var stackId)) continue;
            var stack = stacks[stackId];
            int category = book.CategoryOf(unit.TypeId) ?? 0;
            if (!Experience.GainsXp(category)) continue;
            var owner = map.Players.GetValueOrDefault(stack.Owner);
            bool neutral = owner is null || book.Economy.NeutralRaces.Contains(owner.RaceId);
            var built = owner?.Buildings.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var successors = UnitUpgrade.Successors(book.Rules.Units, unit.TypeId, unit.Level);
            bool open = successors.Any(s => book.UpgradeBuilding.GetValueOrDefault(s.Id, "") is var b && (b.Length == 0 || built.Contains(b)));
            var path = Experience.PathOf(category, unit.DynLevel, successors.Count, open, neutralOwner: neutral);
            var boost = owner is { IsHuman: true } ? new XpBoost(difficulty == (int)Difficulty.Easy, 100)
                      : neutral ? XpBoost.None
                      : new XpBoost(false, Experience.AiXpPercent((Difficulty)difficulty, suggestedLevel));
            int xpNext = book.XpNextOf(unit.TypeId, unit.Level);
            int maxLeader = map.Limits?.MaxLeader ?? 0;
            bool atMax = path == Experience.UpgradePath.Leader && maxLeader > 0 && unit.Level >= maxLeader;
            // Лидеру — создан ли тип следующего уровня в этом запуске (IsUnitTierMax 0x5d8d58 и 0x5d7412 через 0x5d89c6(…, 0), И230).
            bool created = path == Experience.UpgradePath.Leader
                           && GeneratedImpls.NextTierCreated(generated, unit.TypeId, unit.Level, maxLeader, book);
            int xp = unit.Xp + Experience.BattleXp(acc, unit.Xp, xpNext, path, boost, created, atMax);
            // В changeUnitXpCheckUpgrade 0x5d7412 идёт прибавка строки xp (Experience.Amount), а не применённая: у нейтрала она не урезана, и
            // «−1» без цели (0x5d749e) поглощается зажимом addXp — опыт садится на XP_NEXT (И230: replay-legions, S073UN004a 59 + 13 → 70).
            // У закрытого наследника движок передаёт XP_NEXT − 1 − опыт, Amount — XP_NEXT − опыт: итог тот же, «−1» без цели даёт LeaderXpAfter.
            int amount = Experience.Amount(acc, unit.Xp, xpNext, path, 0, boost, created, atMax);
            var (grown, pending) = Advance(map, unit, stack, xp, book, subraceOwners, amount);
            units[id] = grown;
            GeneratedImpls.AddUpTo(generated, unit, grown, book);   // повышение создало реализации (0x5d8b17 → 0x5d8514)
            // Полученный опыт — и в юнит боя: экран показывает «Опыт: +N» (X008TA0005) до «Закрыть»; модель боя его уже не читает.
            var fighter = state.Units.FirstOrDefault(u => u.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (fighter is not null) fighter.Xp += xp - unit.Xp;
            if (grown.Level != unit.Level || !grown.TypeId.Equals(unit.TypeId, StringComparison.OrdinalIgnoreCase))
            {
                state.Notes.Add(new BattleNote(state.Round, id, BattleNoteKind.LevelUp));   // показ и звук (сессия Г)
                // Облик и портрет в бою — уже нового типа: экран меняет облик на половине анимации роста (BattleScreen.PlayEnd), и движок
                // показывает до «Закрыть» новый (замечание пользователя 14.10: анимация была, облик и портрет остались старыми). Модель боя
                // к этому моменту закончена — меняется только показ.
                if (fighter is not null && !grown.TypeId.Equals(unit.TypeId, StringComparison.OrdinalIgnoreCase)
                    && book.Rules.Units.TryGetValue(grown.TypeId, out var newType))
                {
                    fighter.Type = newType;
                    fighter.HpMax = Math.Max(fighter.HpMax, grown.Hp);
                    fighter.Hp = grown.Hp;
                }
            }
            if (pending != stack.UpgradeCount) stacks[stack.Id] = stack with { UpgradeCount = pending };
        }
        return LastEdict.Data.Play.GameSession.With(map, stacks: stacks, units: units);
    }

    /// <summary>
    /// Опыт юнита стал <paramref name="xp"/>: лидер растёт уровнями (<see cref="Experience.LevelUpLeader"/>, копит
    /// выборы навыка), прочие — по пути повышения (<see cref="UnitUpgrade.Apply"/>); повышение лечит до полного.
    /// Возвращает юнит и число невыбранных навыков лидера отряда.
    /// </summary>
    /// <param name="subraceOwners">Хозяева подрас карты (<c>CMidSubRace</c>) — для входа поиска цели повышения (<see cref="UpgradeRace.Allows"/>);
    /// null — вход пропускает всех.</param>
    /// <param name="amount">Не лидеру: прибавка строки <c>xp</c> — та, что идёт в <c>0x5d7412</c> (<see cref="Experience.Amount"/>); у нейтрала и
    /// последнего тира она больше применённой. null — <c>xp − опыт</c>.</param>
    public static (MapUnit Unit, int PendingSkills) Advance(MapState map, MapUnit unit, MapStack stack, int xp, GameBook book,
                                                            IReadOnlyDictionary<string, string>? subraceOwners = null, int? amount = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(book);
        int category = book.CategoryOf(unit.TypeId) ?? 0;
        var owner = map.Players.GetValueOrDefault(stack.Owner);
        bool neutral = owner is null || book.Economy.NeutralRaces.Contains(owner.RaceId);
        var built = owner?.Buildings.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var successors = UnitUpgrade.Successors(book.Rules.Units, unit.TypeId, unit.Level);
        bool open = successors.Any(s => book.UpgradeBuilding.GetValueOrDefault(s.Id, "") is var b && (b.Length == 0 || built.Contains(b)));
        var path = Experience.PathOf(category, unit.DynLevel, successors.Count, open, neutralOwner: neutral);
        if (path == Experience.UpgradePath.Leader)
        {
            var leader = Experience.LevelUpLeader(new LeaderProgress(unit.Level, xp, stack.UpgradeCount),
                                                  level => book.XpNextOf(unit.TypeId, level), map.Limits?.MaxLeader ?? 0);
            return (Healed(unit with { Level = leader.Level, Xp = leader.Xp }, unit, book), leader.PendingSkills);
        }
        bool raceAllows = subraceOwners is null || UpgradeRace.Allows(map, subraceOwners, stack.Owner, book.SubraceOf(unit.TypeId));
        var up = UnitUpgrade.ApplyBattle(new UnitProgress(unit.TypeId, unit.Level, unit.Xp, unit.DynLevel), amount ?? xp - unit.Xp, book.XpNextOf,
                                         book.Rules.Units, book.UpgradeBuilding, built, raceAllows).Unit;
        return (Healed(unit with { TypeId = up.TypeId, Level = up.Level, Xp = up.Xp }, unit, book), stack.UpgradeCount);
    }

    /// <summary>Повышение (новый уровень или тип) восстанавливает здоровье до полного — нового максимума с цепочкой
    /// модификаторов (со слов пользователя 23.09, по игре). Без повышения здоровье остаётся.</summary>
    internal static MapUnit Healed(MapUnit after, MapUnit before, GameBook book)
    {
        if (after.Level == before.Level && after.TypeId.Equals(before.TypeId, StringComparison.OrdinalIgnoreCase)) return after;
        if (book.Grown(after.TypeId, after.Level) is not { } grown) return after;
        var chain = after.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        return after with { Hp = EffectiveStats.Of(grown.Type, chain).HitPoints };
    }
}
