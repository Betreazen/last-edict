using LastEdict.Rules.Rng;

namespace LastEdict.Data.Play;

/// <summary>
/// Оперативный слой ИИ в ходе игрока (<c>CAiOperational</c>, <c>docs/ai-spells.md</c>): драйвер <c>0x46d602</c> — до тактики
/// лечение, смена приказов, защита заклинаниями; после тактики — снова лечение. Выбор — <see cref="AiSpells"/>, литьё —
/// <see cref="CastSpell"/>.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Броски R250 последнего оперативного слоя (для харнессов: окно между стратегией и тактикой в журнале).</summary>
    public int LastOperationalDraws { get; private set; }

    /// <summary>Броски оперативного слоя до тактики вместо генератора партии (харнессы: строки <c>ai_rng</c> журнала схемы 7.5 —
    /// «натиск», <c>op_spell_*</c>, перемешивания слоя, <c>JournalRolls</c> (LastEdict.Verification)); null — генератор партии.</summary>
    public IRng? OperationalRolls { get; set; }

    /// <summary>Оперативный слой до тактики (состояния 1–4): лечение (<c>0x46d691</c>), смена приказов (<c>0x46dee2</c>),
    /// «натиск» при <c>SUGG_LVL ≥ 10</c> (<c>0x46fe35</c>), защита (<c>0x46e322</c>). Списки — с начала хода
    /// (<paramref name="forces"/>).</summary>
    private void OperationalBefore(string playerId, AiForces forces, List<string> report)
    {
        var rng = new CountingRng(OperationalRolls ?? AiRng);
        AiSpells.Heal(this, playerId, forces, rng, report);
        OperationalOrders(playerId, report);
        AiSpells.Harass(this, playerId, forces, rng, report);
        AiSpells.Defend(this, playerId, forces, rng, report);
        LastOperationalDraws = rng.Calls;
    }

    /// <summary>Оперативный слой после тактики (состояние 5): только лечение (<c>0x46d691</c>).</summary>
    private void OperationalAfter(string playerId, List<string> report)
    {
        if (_forces is not { } forces || !Same(_forcesOf, playerId) || !OperatesSpells(playerId)) return;
        AiSpells.Heal(this, playerId, forces, AiRng, report);
    }

    /// <summary>Ведёт ли игрок оперативный слой: не нейтрал и столица не взята (<c>0x46d5b0</c>, <c>0x46d65c</c>).</summary>
    private bool OperatesSpells(string playerId) =>
        !AiTactics.NeutralOwner(this, playerId) && Map.Players.TryGetValue(playerId, out var p) && p.CapturedBy.Length == 0;

    /// <summary>Списки сил начала хода (<c>0x4760b0</c>) игрока, который сейчас ходит; другого — null.</summary>
    public AiForces? ForcesOf(string playerId) => _forces is { } f && Same(_forcesOf, playerId) ? f : null;

    private AiForces? _forces;
    private string _forcesOf = "";

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Счётчик бросков поверх генератора: сколько раз позвали <see cref="IRng.Next"/> с ненулевым пределом.</summary>
public sealed class CountingRng(IRng inner) : IRng
{
    public int Calls { get; private set; }

    public int Next(int maxValue)
    {
        if (maxValue != 0) Calls++;
        return inner.Next(maxValue);
    }
}
