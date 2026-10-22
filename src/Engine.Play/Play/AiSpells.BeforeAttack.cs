using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Rng;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>Заклинание ИИ перед атакой — выбор <c>0x472142</c> (<c>docs/ai-spells.md</c> §5).</summary>
public static partial class AiSpells
{
    /// <summary>
    /// Выбор заклинания перед атакой (<c>0x472142</c>): категории — <see cref="AiBeforeAttack.SpellCategories"/>; уровни —
    /// по ярусу цели (<see cref="AiSpellRules.TierPlan"/> без подъёма первого яруса); годные (<see cref="FitBeforeAttack"/>);
    /// нет — запасной заход ударом (<see cref="AiBeforeAttack.Fallback"/>). Первый бросок R250 — категория среди
    /// встреченных годных (<c>0x47234b</c>), но отбор берёт первую встреченную (итератор броска не используется), из годных остаются её заклинания, сортировка по уровню (<c>0x472618</c>),
    /// спереди снимаются младшие, второй бросок — одно из старших (<c>0x472410</c>). Число ярусного плана не
    /// используется: пары «отряд — цель» в <c>CAiTactic +0x51c8</c>, с которыми его сравнивают (<c>0x46a034</c>), никто
    /// не добавляет (только конструктор и снятие <c>0x469fb6</c>) — счётчик всегда 0.
    /// </summary>
    /// <param name="targetUnits">Юниты цели: группа отряда или гарнизон крепости.</param>
    /// <param name="point">Точка атаки: позиция отряда-цели или вход крепости.</param>
    public static AiPrep? BeforeAttack(GameSession session, MapStack attacker, string targetId, IReadOnlyList<string> targetUnits,
                                       bool targetStackOutside, (int X, int Y) point, double p, bool passed, bool spellBank,
                                       IRng rng)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(targetUnits);
        ArgumentNullException.ThrowIfNull(rng);
        var kinds = AiBeforeAttack.SpellCategories(p, passed, targetStackOutside, attacker.Inside.Length == 0);
        if (kinds.Count == 0) return null;
        int tier = AiSpellRules.Tier([.. Group(session, targetUnits).Select(u => u.XpKilled)]);
        var (_, min, max) = AiSpellRules.TierPlan(tier, raiseFirst: false);
        var fit = FitBeforeAttack(session, attacker, targetId, targetUnits, point, kinds, min, max, spellBank);
        if (fit.Count == 0)
        {
            if (AiBeforeAttack.Fallback(min, passed) is not { } fallback) return null;
            fit = FitBeforeAttack(session, attacker, targetId, targetUnits, point, [SpellCategory.Attack],
                                  fallback.MinLevel, fallback.MaxLevel, spellBank);
            if (fit.Count == 0) return null;
        }
        var met = fit.Select(s => s.Category).Distinct().ToList();
        // Бросок категории (0x47234b) сдвигает копию итератора (0x5cdc8f → 0x472cce, [ebp-0x30]), а отбор годных (0x472372 → 0x47258b)
        // берёт категорию из нового begin() списка ([ebp-0x3c], 0x4c7e28 → 0x4fb13f) — всегда первую встреченную. Бросок тратится впустую.
        // Карта 19 (20261019b-port-synth): у Кланов граница второго броска 3 (три усиления) при любом выпавшем r из трёх категорий.
        rng.Next(met.Count);
        var kind = met[0];
        var best = AiSpellRules.ListSort([.. fit.Where(s => s.Category == kind)], (a, b) => a.Level < b.Level).ToList();
        while (best[0].Level < best[^1].Level) best.RemoveAt(0);
        var spell = best[rng.Next(best.Count)];
        var (castTarget, cell) = CastPoint(session, attacker, targetId, point, spell);
        return new AiPrep(AiPrepKind.Spell, spell.Id, castTarget, cell.X, cell.Y, spellBank);
    }

    /// <summary>
    /// Годные заклинания перед атакой (<c>0x47263e</c>) в порядке известных: категория из списка, на одну клетку, призыв
    /// не иллюзии (<c>0x472840</c>); уровень в диапазоне; по иммунитетам годится против того, в кого бросается
    /// (<see cref="CastPoint"/>, <c>0x476c55</c>); бросить можно (<c>0x5dbd75</c>, второй кошелёк — по
    /// <paramref name="spellBank"/>).
    /// </summary>
    private static List<Spell> FitBeforeAttack(GameSession session, MapStack attacker, string targetId, IReadOnlyList<string> targetUnits,
                                               (int X, int Y) point, IReadOnlyList<SpellCategory> kinds, int min, int max,
                                               bool spellBank)
    {
        var book = session.Book!;
        var fit = new List<Spell>();
        foreach (var spell in Known(session, attacker.Owner))
        {
            if (!kinds.Contains(spell.Category) || spell.Area != 1) continue;
            if (spell.Category == SpellCategory.Summon && book.CategoryOf(spell.SummonUnitId) == UnitCategory.Illusion) continue;
            if (spell.Level < min || spell.Level > max) continue;
            var (castTarget, cell) = CastPoint(session, attacker, targetId, point, spell);
            var units = Same(castTarget, attacker.Id) ? attacker.UnitIds : targetUnits;
            if (!WorksOn(session, units, spell)) continue;
            if (session.SpellRefusal(attacker.Owner, spell.Id, cell.X, cell.Y, spellBank) is not null) continue;
            fit.Add(spell);
        }
        return fit;
    }

    /// <summary>
    /// Куда бросать (<c>0x4724b1</c>): удар — в цель, её точку; призыв — клетка постановки у точки цели
    /// (<c>0x5edded</c>); ослабление и усиление — в <b>сам атакующий отряд</b>, его позицию (так в листинге: ослабление
    /// своего отряда проверка <c>0x5dac42</c> отвергает, поэтому на деле его не бывает).
    /// </summary>
    private static (string Target, (int X, int Y) Cell) CastPoint(GameSession session, MapStack attacker, string targetId,
                                                                  (int X, int Y) point, Spell spell) => spell.Category switch
    {
        SpellCategory.Attack => (targetId, point),
        SpellCategory.Summon => (targetId, session.SummonCell(point.X, point.Y, spell.SummonUnitId) ?? (-1, -1)),
        SpellCategory.Lower or SpellCategory.Boost => (attacker.Id, AiTactics.Position(session.Map, attacker)),
        _ => throw new System.Diagnostics.UnreachableException($"категория {spell.Category} не бывает перед атакой"),
    };
}
