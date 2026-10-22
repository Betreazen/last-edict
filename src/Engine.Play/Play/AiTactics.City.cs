using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>
/// Атака крепости ИИ из карты (срез 6, <c>docs/ai-attack.md</c> §6): кэш крепостей-целей, сила против крепости и
/// построитель <c>AttackCity</c>/<c>AttackCityRoam</c>. Ранг 1 по листингам 23.09, если не сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>
    /// Кэш крепостей-целей (<c>0x45eb14</c>, вектор <c>+0x1c</c> <c>CMidServerLogicData2</c>) с фильтром <c>0x45ec77</c>:
    /// не своей подрасы (<c>+0x90</c> отряда против <c>+0x88</c> крепости), не союзника (<c>0x5f7ebd</c>); в пустую
    /// крепость (без визитёра <c>+0x8c</c>) не идёт отряд с лидером-призывом (<c>0x431d8c</c>) или вором (<c>0x41b399</c>);
    /// не столица игрока, чья столица взята (<c>0x5e4e73</c>: <c>CAPT_BY</c>); не бунтующая деревня (<c>0x5e9582</c>);
    /// не пустая крепость под охраной (<see cref="Guarded"/>). Расстояние: цель приказа — 0, прочие — евклид от позиции
    /// отряда до входа (<c>0x45eadf</c>, <c>0x5e94eb</c>); радиус у чужих столиц и исключения — как у прочих векторов; до трёх.
    /// </summary>
    public static IReadOnlyList<MapSite> FortTargets(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        var book = session.Book!;
        var self = Position(map, stack);
        var (_, orderTarget) = OrderOf(stack);
        string leaderType = map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        bool barredFromEmpty = UnitCategory.IsSummonImpl(book.CategoryOf(leaderType)) || book.CategoryOf(leaderType) == UnitCategory.Noble;
        // Радиус у чужих столиц (0x45f5f1) — только у «главного» отряда (+0x51d9, turn.Radius), как у прочих векторов кэша
        // (docs/ai-tactic-candidates.md §6). До 28.09 порт ставил его всем (лишних кандидатов тогда было меньше: human 308 → 222),
        // но со слабыми атаками только во втором проходе лишних нет, а у probe65 недоставало 26 атак крепости (FT0009 у KC00a3).
        int radius = turn.Radius;
        var capitals = Forts(map).Where(f => f.Kind == SiteKind.Capital && !Same(f.Owner, stack.Owner) && !NeutralOwner(session, f.Owner))
                                 .Select(Entrance).ToList();
        return [.. Forts(map)
            .Where(f => !Same(f.Subrace, stack.Subrace) && !Allied(session, stack.Owner, f.Owner))
            .Where(f => f.StackId.Length > 0 || !barredFromEmpty)
            .Where(f => !(f.Kind == SiteKind.Capital && map.Players.TryGetValue(f.Owner, out var o) && o.CapturedBy.Length > 0))
            .Where(f => !(f.Kind == SiteKind.Village && f.RiotTurn > 0))
            .Where(f => !Guarded(session, stack, f))
            .Where(f => !turn.Excluded.Contains(f.Id))
            .Select(f => (Fort: f, Pos: Entrance(f)))
            .Where(p => radius == 0 || turn.WinTargets.Contains(p.Fort.Id) || !capitals.Any(c => Cheb(c, p.Pos) <= radius))
            .OrderBy(p => Same(p.Fort.Id, orderTarget) ? 0.0 : AiTacticScore.SetDistance(p.Pos.X - self.X, p.Pos.Y - self.Y))
            .ThenBy(p => p.Fort.Id, StringComparer.OrdinalIgnoreCase)
            .PickThree(p => turn.WinTargets.Contains(p.Fort.Id))
            .Select(p => p.Fort)];
    }

    /// <summary>Сторона квадрата охраны пустой крепости от входа (<c>0x45ee12</c>: <c>[вход − 3, вход + 4)</c>).</summary>
    private const int GuardReach = 3;

    /// <summary>
    /// Пустая крепость под охраной (<c>0x45ed86</c>): визитёра нет, хозяин не нейтрал (<c>0x5e5358</c>), и в квадрате ±3
    /// от входа (объекты клеток <c>0x5f5797</c>, тип <c>0x23</c>) стоит отряд подрасы крепости, не водный (<c>0x45e78c</c>) и
    /// годный в цели атакующему — тот же фильтр <c>0x45ea2e</c>, что у кэша отрядов (<see cref="StackTargetOk"/>). Отряд-гость
    /// соседней крепости тоже в счёт: он стоит объектом в клетке угла своей крепости (probe72, дни 4–6: <c>S143FT0010</c> под
    /// охраной гостя столицы <c>S143KC0097</c>). Такую крепость ИИ не берёт в кэш — бьёт охрану (она в кэше отрядов).
    /// </summary>
    private static bool Guarded(GameSession session, MapStack attacker, MapSite fort)
    {
        if (fort.StackId.Length > 0 || NeutralOwner(session, fort.Owner)) return false;
        var (ex, ey) = Entrance(fort);
        return session.Map.Stacks.Values.Any(s => Same(s.Subrace, fort.Subrace)
                                                  && s.X >= ex - GuardReach && s.X <= ex + GuardReach
                                                  && s.Y >= ey - GuardReach && s.Y <= ey + GuardReach
                                                  && !session.WaterOnly(s) && StackTargetOk(session, attacker, s));
    }

    /// <summary>Подмога крепости считается, только если отряд дальше этого от входа (<c>0x5dddee</c>: строго больше 10).</summary>
    private const double CityHelpFrom = 10;

    /// <summary>Подмога крепости — отряды её подрасы ближе этого к входу (<c>0x5dde80</c>: строго меньше 8).</summary>
    private const double CityHelpReach = 8;

    /// <summary>
    /// Сила против крепости (<c>0x5ddcb3</c>, ранг 1 по листингу): своя группа не может ранить гарнизон
    /// (<c>0x5dcd53</c>) — 0. Защита D — визитёр и гарнизон «с городом» (<c>0x5dcec7</c>, флаг 0: броня укрепления) против
    /// числа своих юнитов; если отряд дальше 10 от входа — ещё отряды подрасы крепости с приказом не <c>L_STAND</c> (<c>0x5dde8b</c>: <c>sete/neg/sbb/inc</c> — единица при неравенстве) вне
    /// крепостей ближе 8 к входу. Своя сила B — «без города» против числа юнитов визитёра, а без него — гарнизона.
    /// Итог — <see cref="AiTacticScore.CityStrength"/>.
    /// </summary>
    public static double CityStrength(GameSession session, MapStack own, MapSite fort)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(fort);
        var map = session.Map;
        var mine = Fighters(session, own.UnitIds).ToList();
        if (!AiStrength.CanDamage(mine, Fighters(session, fort.Guards).ToList())) return 0;
        int attackers = own.UnitIds.Count;
        double d = 0;
        int defenders = 0;
        if (fort.StackId.Length > 0 && map.Stacks.TryGetValue(fort.StackId, out var visitor))
        {
            d = GroupStrength(session, visitor, ignoreCity: false, attackers);
            defenders = visitor.UnitIds.Count;
        }
        d += UnitsStrength(session, fort.Guards, ignoreCity: false, attackers, AiFortArmor(session, fort.Id));
        if (defenders == 0) defenders = fort.Guards.Count;
        double b = GroupStrength(session, own, ignoreCity: true, defenders);
        var gate = Entrance(fort);
        if (AiTacticScore.SetDistance(own.X - gate.X, own.Y - gate.Y) > CityHelpFrom)
            foreach (var s in map.Stacks.Values.Where(s => Same(s.Subrace, fort.Subrace) && s.Order != (int)StackOrder.Stand
                                                           && s.Inside.Length == 0
                                                           && AiTacticScore.SetDistance(s.X - gate.X, s.Y - gate.Y) < CityHelpReach))
                d += GroupStrength(session, s, ignoreCity: false, attackers);
        return AiTacticScore.CityStrength(b, d);
    }

    /// <summary>
    /// Атака крепости (<c>AttackCity</c> и <c>AttackCityRoam</c>): построитель <c>0x452636</c> — по кэшу крепостей
    /// (<see cref="FortTargets"/>), крепость с визитёром мимо (её защитник — цель атаки отряда); точка — вход; счёт
    /// <c>0x45275f</c> → <see cref="AiTacticScore.AttackCity"/>. ponytail: предусловие построителя — слот <c>0x14</c>
    /// отряда — не проверяется; «группа может драться» — <see cref="GroupCanFight"/>. Заклинание и подмога
    /// перед атакой (<c>0x452f60</c>, <c>0x452fe5</c>) — <see cref="WithPreparations"/>, без второго кошелька.
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> AttackCities(GameSession session, AiTacticTurn turn, MapStack stack,
                                                                      AiActionType type)
    {
        var map = session.Map;
        var self = Position(map, stack);
        var kind = type switch
        {
            AiActionType.AttackCityRoam => AttackEvalKind.Roam,
            AiActionType.AttackCityBezerk => AttackEvalKind.Bezerk,
            _ => AttackEvalKind.Normal,
        };
        if (!GroupCanFight(session, stack)) yield break;   // 0x452658 → 0x4526fc
        bool summon = KindOf(session, stack) == AiStackKind.Summon;
        foreach (var fort in FortTargets(session, turn, stack).Where(f => f.StackId.Length == 0))
        {
            var at = Entrance(fort);
            int size = fort.Kind == SiteKind.Capital ? 5 : 4;
            var owner = map.Players.GetValueOrDefault(fort.Owner);
            double p = CityStrength(session, stack, fort);
            bool human = owner?.IsHuman ?? false, attackerRace = NeutralOwner(session, stack.Owner), targetRace = NeutralOwner(session, fort.Owner);
            var facts = new AttackCityFacts(
                Hostile: !Allied(session, stack.Owner, fort.Owner), TargetHuman: human,
                MaxInfluence: AiTacticScore.MaxInfluence(turn.Grid, fort.X, fort.Y, size, size), Distance: Cheb(self, at),
                Occupied: false, GarrisonCount: fort.Guards.Count, Strength: p,
                CapturedByOther: turn.CapturedByOther(stack.Id, fort.Id),
                StatusToday: turn.Memo.Status(stack.Id, fort.Id, at.X, at.Y, turn.Day),
                StatusYesterday: turn.Memo.Status(stack.Id, fort.Id, at.X, at.Y, turn.Day - 1),
                SummonAttacker: summon, Kind: kind, AttackerRaceNeutral: attackerRace, TargetRaceNeutral: targetRace);
            var side = new AttackSide(p, AiTacticScore.CityThresholdPassed(p, attackerRace, human, targetRace), human, targetRace,
                                      fort.Guards, StackOutside: false, SpellBank: false);
            var attack = new AiTacticOption(type, stack.Id, fort.Id, at.X, at.Y);
            if (kind == AttackEvalKind.Normal)
            {
                if (Made(session, turn, stack, attack, side.Passed) is not { } made) continue;
                attack = made;
            }
            foreach (var c in WithPreparations(session, turn, stack, attack, AiTacticScore.AttackCity(turn.Weights, facts), side))
                yield return c;
        }
    }
}
