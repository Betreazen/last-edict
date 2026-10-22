using System.Globalization;
using D2.Rules.Battle;
using D2.Rules.Units;

namespace D2.Data.Battle;

/// <summary>Надпись на портрете цели в панели: юнит и разметка строки.</summary>
public sealed record StrikeNote(string Target, string Markup);

/// <summary>
/// Надписи удара на портретах целей (<c>0x63b391</c>, ранг 1), по записям хода и по состоянию до удара (<see cref="Before"/>):
/// <list type="number">
/// <item>промах второй атаки — ничего;</item>
/// <item>удар остановлен защитой — «Иммунитет» <c>X008TA0002</c>, если хоть одна категория (источник, класс) — <c>Always</c>, иначе
/// «Щит» <c>X008TA0011</c> (сработавший заряд <c>Once</c>); раньше промаха;</item>
/// <item>промах первой атаки — «Промах» <c>X008TA0001</c> на каждой промахнувшейся цели;</item>
/// <item>классы-состояния — своя строка (<see cref="ClassText"/>);</item>
/// <item>урон, вампиризм, лечение: число — по потерянному (у лечения — полученному) здоровью, а не по выходу <c>computeDamage</c>
/// (<c>0x65b5dc…0x65b645</c>): потеряно не меньше крита — урон = потеряно − крит, иначе урон 0, крит = потеряно; урон 0 у первой атаки —
/// ничего; крит — «Крит удар» <c>X160TA0021</c> голыми числами, иначе <c>X008TA0004</c> «−N» / «+N».</item>
/// </list>
/// Защиту модель в записи хода не несёт — она выводится до удара теми же правилами, что решают удар (<see cref="Immunity"/>): уровень
/// <c>Always</c> держит всегда, живой заряд <c>Once</c> — тоже. Атаки по своим (усиление, воскрешение, снятие эффектов, дар атаки, обереги,
/// превращение себя, двойник) записей хода не дают — их строки по журналу показа <see cref="BattleSim.AllyLog"/> (<see cref="Allies"/>).
/// «Страх» у цели, чья группа бежать не может (гарнизон, руина; отряд в городе — если обороняется), — «Паралич» (<c>0x63aca2</c> →
/// <c>0x63b35a</c> → <c>0x5f51bf</c>: по группе первой цели удара, у всех целей).
/// </summary>
public sealed class StrikeNotes
{
    private enum Shield { None, Immune, Ward }

    private readonly Dictionary<string, int> _hp;
    private readonly Dictionary<(string Target, int Attack), Shield> _shields;
    private readonly AttackType? _item;

    private StrikeNotes(Dictionary<string, int> hp, Dictionary<(string, int), Shield> shields, AttackType? item) =>
        (_hp, _shields, _item) = (hp, shields, item);

    /// <summary>Состояние до удара юнита <paramref name="striker"/>: здоровье всех и защита каждой цели от его атак (заряды ещё не
    /// потрачены). <paramref name="item"/> — удар боевым предметом или зельем (<see cref="BattleItemUse.Shown"/>): защита и надпись первой
    /// атаки — по нему, а не по атаке носителя (у Баньши с Сферой грома — «−103», а не строка паралича).</summary>
    public static StrikeNotes Before(BattleState state, BattleUnit? striker, AttackType? item = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var hp = state.Units.ToDictionary(u => u.Id, u => u.Hp, StringComparer.OrdinalIgnoreCase);
        var shields = new Dictionary<(string, int), Shield>();
        if (striker is not null)
            foreach (var target in state.Units.Where(u => u.OnField))
            {
                shields[(target.Id.ToUpperInvariant(), 1)] = ShieldOf(target, striker, item ?? striker.Attack);
                if (item is null && striker.Attack2 is { } second) shields[(target.Id.ToUpperInvariant(), 2)] = ShieldOf(target, striker, second);
            }
        return new StrikeNotes(hp, shields, item);
    }

    /// <summary>Строка класса-состояния (<c>0x63b391</c>): паралич, окаменение, страх, ослабления, яды, пузырь, разрушение брони,
    /// понижение уровня, превращение; у урона, вампиризма и лечения — число, у призыва — ничего.</summary>
    public static string? ClassText(AttackClass cls) => cls switch
    {
        AttackClass.Paralyze => "X008TA0008",
        AttackClass.Petrify => "X008TA0009",
        AttackClass.Fear => "X008TA0007",
        AttackClass.BoostDamage => "X008TA0003",
        AttackClass.LowerDamage => "X008TA0012",
        AttackClass.LowerInitiative => "X008TA0013",
        AttackClass.Poison => "X008TA0026",
        AttackClass.Frostbite => "X008TA0027",
        AttackClass.Blister => "X160TA0011",
        AttackClass.BestowWards => "X160TA0013",
        AttackClass.Shatter => "X160TA0019",
        AttackClass.Revive => "X008TA0016",
        AttackClass.Cure => "X008TA0017",
        AttackClass.DrainLevel => "X008TA0018",
        AttackClass.GiveAttack => "X008TA0019",
        AttackClass.Doppelganger or AttackClass.TransformSelf or AttackClass.TransformOther => "X008TA0022",
        _ => null,
    };

    /// <summary>Надписи по записям хода этого удара, по порядку.</summary>
    public IReadOnlyList<StrikeNote> After(IReadOnlyList<TurnRecord> records, BattleState state, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(text);
        var notes = new List<StrikeNote>();
        bool fearParalyzes = records.Count > 0 && state.Unit(records[0].Target) is { } first && !state.CanRetreat(first);
        foreach (var record in records)
        {
            var attacker = state.Units.FirstOrDefault(u => u.Id.Equals(record.Unit, StringComparison.OrdinalIgnoreCase));
            var attack = attacker is null ? null : _item is not null && record.Attack == 1 ? _item : record.Attack == 2 ? attacker.Attack2 : attacker.Attack;
            int before = _hp.GetValueOrDefault(record.Target, record.TargetHpAfter);
            _hp[record.Target] = record.TargetHpAfter;
            if (attack is null || Markup(record, attack, before, text, fearParalyzes) is not { } markup) continue;
            notes.Add(new StrikeNote(record.Target, markup));
        }
        return notes;
    }

    /// <summary>Атака-предмет удара (<see cref="Before"/>), если удар — предметом.</summary>
    internal AttackType? Item => _item;

    /// <summary>Защита цели от атаки <paramref name="attack"/> (1, 2) до удара: иммунитет или щит.</summary>
    internal bool Shielded(string target, int attack) => _shields.GetValueOrDefault((target.ToUpperInvariant(), attack)) != Shield.None;

    /// <summary>Надписи атак по своим (<see cref="AllyNote"/>): строка класса на каждой цели, по порядку.</summary>
    public static IReadOnlyList<StrikeNote> Allies(IEnumerable<AllyNote> notes, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(text);
        return [.. notes.Select(n => ClassText(n.Class) is { } id ? new StrikeNote(n.Target, text(id)) : null).OfType<StrikeNote>()];
    }

    /// <summary>Надписи вампиризма (<see cref="DrainHeal"/>, <c>0x63af1e…0x63b2a3</c> → <c>0x63c27d</c>, ранг 1): на группе бьющего прибавки
    /// здоровья складываются по юниту (<c>0x63b1b6</c>), «+N» (<c>X008TA0004</c>, <c>+%d</c>) — у кого N &gt; 0 (<c>0x63b11d</c>).</summary>
    public static IReadOnlyList<StrikeNote> Drains(IEnumerable<DrainHeal> heals, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(heals);
        ArgumentNullException.ThrowIfNull(text);
        return [.. heals.Where(h => h.Amount > 0).GroupBy(h => h.Target, StringComparer.OrdinalIgnoreCase)
                        .Select(g => new StrikeNote(g.First().Target, Amount(text, "+", g.Sum(h => h.Amount))))];
    }

    /// <summary>Надпись тика в начале хода (<c>0x635ecc</c> → <c>0x636419</c> со знаком «−», ранг 1): яд — «Яд(−N)» <c>X008TA0014</c>
    /// (<c>0x635ce2</c>), мороз — «Мороз (−N)» <c>X008TA0015</c> (<c>0x635d93</c>), ожог — «Пузырь (−N)» <c>X160TA0022</c> (<c>0x635e44</c>).</summary>
    public static string? TickText(DotTick tick, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(text);
        string? id = tick.Class switch
        {
            AttackClass.Poison => "X008TA0014",
            AttackClass.Frostbite => "X008TA0015",
            AttackClass.Blister => "X160TA0022",
            _ => null,
        };
        return id is null ? null : text(id).Replace("%DAMAGE%", "-" + Number(tick.Damage), StringComparison.Ordinal);
    }

    /// <summary>Надпись сообщения начала хода (<see cref="BattleNoteKind"/>): снят паралич — «Паралич прошел!» <c>X008TA0024</c>
    /// (<c>0x635bb2</c>), снято окаменение — «Оживает!» <c>X008TA0025</c> (<c>0x635c5e</c>); прочие — без надписи.</summary>
    public static string? NoteText(BattleNoteKind kind, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return kind switch
        {
            BattleNoteKind.Unparalyzed => text("X008TA0024"),
            BattleNoteKind.Unpetrified => text("X008TA0025"),
            _ => null,
        };
    }

    private string? Markup(TurnRecord record, AttackType attack, int before, Func<string, string> text, bool fearParalyzes)
    {
        if (record.Miss && record.Attack != 1) return null;
        switch (_shields.GetValueOrDefault((record.Target.ToUpperInvariant(), record.Attack)))
        {
            case Shield.Immune: return text("X008TA0002");
            case Shield.Ward: return text("X008TA0011");
        }
        if (record.Miss) return text("X008TA0001");
        if (attack.Class == AttackClass.Fear && fearParalyzes) return text("X008TA0008");
        if (ClassText(attack.Class) is { } id) return text(id);
        if (attack.Class is not (AttackClass.Damage or AttackClass.Drain or AttackClass.DrainOverflow or AttackClass.Heal)) return null;
        if (attack.Class == AttackClass.Heal)
        {
            int healed = Math.Max(0, record.TargetHpAfter - before);
            return healed == 0 && record.Attack == 1 ? null : Amount(text, "+", healed);
        }
        int lost = Math.Max(0, before - record.TargetHpAfter);
        var (damage, crit) = lost >= record.Crit ? (lost - record.Crit, record.Crit) : (0, lost);
        if (damage == 0 && record.Attack == 1) return null;
        return crit > 0
            ? text("X160TA0021").Replace("%DAMAGE%", Number(damage), StringComparison.Ordinal).Replace("%CRITICAL%", Number(crit), StringComparison.Ordinal)
            : Amount(text, "-", damage);
    }

    private static string Amount(Func<string, string> text, string sign, int value) =>
        text("X008TA0004").Replace("%DAMAGE%", sign + Number(value), StringComparison.Ordinal);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Защита цели от атаки (<c>isImmune</c> до удара): <c>Always</c> источника или класса — иммунитет, живой заряд <c>Once</c> —
    /// щит. Атака — с цепочкой модификаторов атакующего, как у <see cref="Attacks.Apply"/>.</summary>
    private static Shield ShieldOf(BattleUnit target, BattleUnit striker, AttackType attack)
    {
        var effective = EffectiveStats.OfAttack(attack, striker.Chain).Attack;
        if (!effective.Effect.ChecksImmunity) return Shield.None;
        var (bySource, byClass) = Immunity.LevelsFor(target, effective.Source, effective.Class);
        if (bySource == ImmunityLevel.Always || byClass == ImmunityLevel.Always) return Shield.Immune;
        return (bySource == ImmunityLevel.Once && Immunity.OnceLive(target, effective.Source))
               || (byClass == ImmunityLevel.Once && Immunity.OnceLive(target, effective.Class)) ? Shield.Ward : Shield.None;
    }
}
