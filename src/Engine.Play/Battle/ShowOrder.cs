using D2.Rules.Battle;

namespace D2.Data.Battle;

/// <summary>Порядок показа записей одного шага боя: записи хода модели (<see cref="TurnRecord"/>) и атаки по своим без записи
/// (<see cref="AllyNote"/> — обереги, снятие эффектов, усиление, дар атаки, превращение себя, двойник).</summary>
public static class ShowOrder
{
    /// <summary>
    /// Записи хода, дополненные атаками по своим без записи — каждая на своём месте по номеру атаки: после последней записи того же юнита с
    /// номером атаки не больше её, а если такой нет — перед первой записью этого юнита (у движка шаг показа идёт по номерам атак,
    /// <c>battleTurn</c> <c>0x62494d</c>, и основная атака показывается раньше второй — <c>0x63513a</c>). Нет записей юнита — в конец.
    /// Прежде атака 1 без записи (обереги Лесной Девы) при записи атаки 2 (её лечение) уходила в конец: лечение показывалось первым, с
    /// картинкой «лечение» вместо своей картинки оберегов (партия 15.10, пометки 3 и 5).
    /// </summary>
    /// <param name="records">Записи хода по порядку.</param>
    /// <param name="notes">Атаки по своим, ещё не показанные, по порядку.</param>
    /// <param name="hpOf">Здоровье цели сейчас (поле <see cref="TurnRecord.TargetHpAfter"/> добавленной записи).</param>
    public static List<TurnRecord> WithAllies(IReadOnlyList<TurnRecord> records, IEnumerable<AllyNote> notes, Func<string, int> hpOf)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(hpOf);
        var all = records.ToList();
        foreach (var note in notes)
        {
            if (all.Any(r => Same(r.Unit, note.Unit) && Same(r.Target, note.Target) && r.Attack == note.Attack)) continue;
            int after = all.FindLastIndex(r => Same(r.Unit, note.Unit) && r.Attack <= note.Attack);
            int at = after >= 0 ? after + 1 : all.FindIndex(r => Same(r.Unit, note.Unit)) is var first and >= 0 ? first : all.Count;
            all.Insert(at, new TurnRecord(note.Round, note.Unit, note.Target, false, 0, hpOf(note.Target), Attack: note.Attack));
        }
        return all;
    }

    /// <summary>
    /// Шаг без записей хода (призыв, усиление и прочие атаки по своим без записи, превращение себя): что показать — по одной записи на
    /// основную атаку (<see cref="ActionNote.Attack"/> = 1) каждой пары «юнит → цель» применений шага, без урона. Движок показывает такой ход
    /// так же, как ход с записями, и от хозяина хода показ не зависит (шаг 0 — <c>0x634b78</c>, <c>HEFF</c> — <c>0x63a30f</c>): автобой за
    /// Травницу (журнал 20261016e-av, 15978146…15978288) — <c>bat_show step 0</c>, замах <c>HMOV</c>, звуки <c>unit33a</c> и <c>heal</c>, как у её
    /// хода человеком (15977623). У порта ход человека показывал замах сам (<c>BattleScreen.Attack</c>), а ход ИИ — только записи модели:
    /// призыв ИИ был без замаха и звука, усиление — без звука.
    /// </summary>
    /// <param name="actions">Применения атак этого шага (<c>BattleState.Actions</c> после шага), по порядку.</param>
    /// <param name="hpOf">Здоровье цели сейчас (поле <see cref="TurnRecord.TargetHpAfter"/>).</param>
    public static List<TurnRecord> Unrecorded(IEnumerable<ActionNote> actions, Func<string, int> hpOf)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(hpOf);
        var all = new List<TurnRecord>();
        foreach (var a in actions)
        {
            if (a.Attack != 1 || all.Any(r => Same(r.Unit, a.Unit) && Same(r.Target, a.Target))) continue;
            all.Add(new TurnRecord(a.Round, a.Unit, a.Target, false, 0, hpOf(a.Target), Attack: a.Attack));
        }
        return all;
    }

    /// <summary>
    /// Цель показа призыва — призванный, а не призыватель. Модель целит призыв формально в самого ходящего (<c>BattleSim.Act</c>: движок
    /// целится в позицию), а движок показывает шаг по новому юниту: <c>bat_show step 0</c> призывателя с целью — id призванного, <c>TUCH</c>
    /// призывателя — на клетке призванного, там же пауза кода 9 (<c>0x63c902</c> → <c>0x652299</c> → <c>playEffect</c> <c>0x65255b</c>). Журналы
    /// 20261016e-av (seq 15948422…15948452 и ещё 8) и 20261016g-port-synth (8 призывов, с seq 519049): 17 из 17. Прежде у порта <c>TUCH</c>
    /// шёл на месте призывателя и обрывал его замах, а призванный появлялся только пересборкой поля в конце шага.
    /// </summary>
    /// <param name="shown">Записи показа шага.</param>
    /// <param name="added">Юниты, которых в бою до шага не было.</param>
    public static List<TurnRecord> SummonTargets(IReadOnlyList<TurnRecord> shown, IEnumerable<BattleUnit> added)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(added);
        var fresh = added.Where(u => u.Summoned && u.SummonerId is not null).ToList();
        var all = new List<TurnRecord>(shown.Count);
        foreach (var r in shown)
        {
            var summoned = Same(r.Unit, r.Target) ? fresh.FirstOrDefault(u => Same(u.SummonerId!, r.Unit)) : null;
            all.Add(summoned is null ? r : r with { Target = summoned.Id, TargetHpAfter = summoned.Hp });
        }
        return all;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
