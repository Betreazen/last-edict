using LastEdict.Rules.Map;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Data.Play;

/// <summary>Команда хода отряда человека по сегодняшней части пути после упреждающей проверки событиями (<c>docs/map-path.md</c> §7):
/// <see cref="Today"/> — клетки, которые отряд пройдёт; <see cref="Fired"/> — события, найденные проверкой (их применит
/// <see cref="GameSession.MoveStack"/>); <see cref="Cut"/> — событие оборвало путь раньше; <see cref="Attack"/> — враждебный отряд, на который
/// команда нападёт по приходу (<see cref="GameSession.CommandPath"/>; невидимый нападает сам — <see cref="GameSession.StartBattle"/>).</summary>
public sealed record PlayerMove(IReadOnlyList<PathPoint> Today, IReadOnlyList<EventFiring> Fired, bool Cut)
{
    /// <summary>Объект действия команды хода — враждебный отряд в клетке цели или у последней достижимой (<c>0x5cd38a</c>, <c>0x5cd11f</c>):
    /// по приходу бой с ним (<c>0x43ff9d</c> → <c>0x440667</c>). null — нападения нет (нет врага, путь оборван событием, очков хода не было).</summary>
    public string? Attack { get; init; }

    /// <summary>Списание хода (<see cref="StackPath.ChargeFor"/>): команда, вставшая перед невидимым раньше последней достижимой
    /// показанного пути, списывает цену до той (клетки с отрядами — 0, <c>0x5cc3d2</c>, <c>0x5cd0e9</c>); иначе null — цена клетки, где встал.</summary>
    public int? Charge { get; init; }

    /// <summary>
    /// Ход человека, как у движка (<c>0x43fc3c</c> → <c>0x440284</c>): команда хода сначала проверяет клетки сегодняшней части пути
    /// событиями без игрока хода (<see cref="GameSession.PathEvents"/>; броски R250 — в момент команды, до анимации). Сработало —
    /// отряд встаёт на этой клетке; у человека безвредные эффекты (смена приказа) путь не обрывают — правило в
    /// <see cref="GameSession.PathEvents"/>.
    /// <para>Сначала — докуда команда дойдёт, видя невидимых (<see cref="GameSession.CommandPath"/>, <c>0x5cc120(…, 1, 1)</c>): путь сквозь
    /// невидимого врага, показанный интерфейсом, кончается перед ним; <see cref="Attack"/> — враг в клетке щелчка <paramref name="target"/>
    /// (если команда до неё дошла) или вокруг последней достижимой, видимый или невидимый. Обрыв событием отменяет нападение (<c>0x43ff9d</c>).</para>
    /// </summary>
    public static PlayerMove Plan(GameSession session, string stackId, StackPath path, (int X, int Y)? target = null,
                                  MoveOptions? opt = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(path);
        string? attack = null;
        if (session.Map.Stacks.TryGetValue(stackId, out var stack) && path.Points.Count > 0)
        {
            var o = opt ?? session.MoveOptionsOf(stack);
            var goal = target is { } t ? PathFinder.Target(session.Map, stack, t.X, t.Y, o, MovementRules.Vanilla)
                : (path.Points[^1].X, path.Points[^1].Y);
            (path, attack) = session.CommandPath(stack, path, goal, session.HumanReachHostile(stack, seeInvisible: true), o);
        }
        var today = path.Today;
        // Шагов нет, а команда встала перед невидимым у самого старта: CEffectMove всё равно списывает цену конца присланного списка (без клетки
        // невидимки) — журнал 20261018-port-synth, карта 13, CH2 (второй ход): с (11,34) к (15,34) мимо невидимки (12,34) — ap_restore seq 651785:
        // списано 6 (14 → 8), затем 10 за нападение; засада.
        if (today.Count == 0) return new PlayerMove(today, [], false) { Attack = attack, Charge = path.Charge };
        // Оборвано и на последней клетке — тоже обрыв (выход 0x440284 = 1): ни нападения, ни входа (0x43ff9d, 0x43fc13).
        var planned = session.Run<PlannedMove>(new PlanMoveCommand(stackId, today));   // броски R250 — командой (docs/game-commands.md)
        var (walk, fired, cut) = (planned.Walk, planned.Fired.Select(f => f.Resolve(session.State)).ToList(), planned.Cut);
        return new PlayerMove(walk < today.Count ? [.. today.Take(walk)] : today, fired, cut)
        {
            Attack = cut ? null : attack,
            Charge = !cut && walk == today.Count && path.Charge is { } charge ? charge : null,
        };
    }
}
