using D2.Data.Gfx;
using D2.Rules.Battle;

namespace D2.Data.Battle;

/// <summary>Вспышки удара на портретах целей панели боя (вид к надписям <see cref="StrikeNotes"/>): цвета портретов — модель интерфейса
/// (<see cref="BattleFaces"/>), надписи и их снимок до удара — ядро.</summary>
public static class StrikeFaces
{
    /// <summary>Вспышки удара на портретах целей (<c>0x63b391</c>, хвост <c>0x63bcc2…0x63bfa8</c> → <c>0x64cee8</c>; <see cref="BattleFaces.Strike"/>)
    /// по записям хода — и там, где надписи нет (урон 0 первой атаки, лечение 0, призыв): промах второй атаки — ничего (выход
    /// <c>0x63b402</c>), защита (иммунитет, щит) — раньше промаха, как у надписи. Защита — та, что до удара (<see cref="StrikeNotes.Before"/>).</summary>
    public static IReadOnlyList<(string Target, BattleFlash Flash)> Faces(this StrikeNotes notes, IReadOnlyList<TurnRecord> records, BattleState state)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(state);
        var faces = new List<(string, BattleFlash)>();
        foreach (var record in records)
        {
            if (record.Miss && record.Attack != 1) continue;
            var attacker = state.Units.FirstOrDefault(u => u.Id.Equals(record.Unit, StringComparison.OrdinalIgnoreCase));
            var attack = attacker is null ? null : notes.Item is { } item && record.Attack == 1 ? item : record.Attack == 2 ? attacker.Attack2 : attacker.Attack;
            bool shielded = notes.Shielded(record.Target, record.Attack);
            if (BattleFaces.Strike(attack?.Class, record.Miss, shielded) is { } flash) faces.Add((record.Target, flash));
        }
        return faces;
    }

    /// <summary>Вспышки атак по своим (<see cref="AllyNote"/>, тот же хвост <c>0x63bcc2…</c>): по классу на каждой цели.</summary>
    public static IReadOnlyList<(string Target, BattleFlash Flash)> AllyFaces(IEnumerable<AllyNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var faces = new List<(string, BattleFlash)>();
        foreach (var note in notes)
            if (BattleFaces.Strike(note.Class, false, false) is { } flash) faces.Add((note.Target, flash));
        return faces;
    }

    /// <summary>Вспышки вампиризма (<c>0x63c27d</c>: после «+N» — <c>0x64cee8</c> с состоянием 9, <c>0x63c34f</c>): у кого прибавка за удар
    /// больше 0, как у надписи (<see cref="StrikeNotes.Drains"/>).</summary>
    public static IReadOnlyList<(string Target, BattleFlash Flash)> DrainFaces(IEnumerable<DrainHeal> heals)
    {
        ArgumentNullException.ThrowIfNull(heals);
        return [.. heals.Where(h => h.Amount > 0).Select(h => h.Target).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(t => (t, new BattleFlash(BattleFace.DrainHealed, false)))];
    }
}
