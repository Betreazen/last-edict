using LastEdict.Rules.Magic;

namespace LastEdict.Rules.Map;

/// <summary>Вид производного кандидата атаки (<c>docs/ai-spells.md</c> §5): заклинание перед атакой
/// (<c>CAiCastSpellBeforeAttack{Stack,City}Action</c>, элемент плана с id заклинания), подмога
/// (<c>CAiAssistBeforeAttack{Stack,City}Action</c>, элемент с id своего отряда) и итоговая атака
/// (<c>CAiTacticActionRefCount&lt;CAiBasicAttack{Stack,City}Action&gt;</c>).</summary>
public enum AiPrepKind { Spell, Assist, Attack }

/// <summary>
/// Подготовка ИИ к атаке — планировщик <c>0x471ec7</c> и выбор заклинания <c>0x472142</c>, которые зовут построители
/// атаки отряда (<c>0x453d15</c>) и крепости (<c>0x452baf</c>). Чистые правила; сборку из карты делает
/// <c>LastEdict.Data.Play.AiTactics</c>. Ранг 1 — листинг <c>Discipl2.exe</c> (GOG 3.01).
/// </summary>
public static class AiBeforeAttack
{
    /// <summary>Ниже этой силы готовиться, если хозяин цели — человек или раса хозяина цели неиграбельна
    /// (<c>[0x6d2830]</c>).</summary>
    public const double PrepareBelowGuarded = 0.75;

    /// <summary>Ниже этой силы готовиться у прочих целей (<c>[0x6d2838]</c>).</summary>
    public const double PrepareBelow = 0.7;

    /// <summary>Сила «одной атакой не взять» (<c>[0x6d2840]</c>): у <see cref="SpellCategories"/> и хвоста
    /// <see cref="AttackAlone"/>.</summary>
    public const double WeakStrength = 0.6;

    /// <summary>Шаг счёта производных кандидатов: каждый следующий на столько меньше (<c>fsub [0x6d0530]</c> у отряда,
    /// <c>[0x6d0388]</c> у крепости). Константа — float 0,0001, записанный как double.</summary>
    public const double ScoreStep = 9.999999747378752e-05;

    /// <summary>Голова <c>0x471ec7</c>: сила ниже порога — готовиться; иначе атака идёт без подготовки.</summary>
    public static bool Prepares(double p, bool targetHuman, bool targetRaceNeutral) =>
        p < (targetHuman || targetRaceNeutral ? PrepareBelowGuarded : PrepareBelow);

    /// <summary>
    /// Хвост <c>0x471ec7</c> (<c>0x4720a4</c>), когда подготовиться нечем: порог силы оценщика не взят
    /// (<paramref name="passed"/> ложно) — атака остаётся; взят — при действующем <c>L_NORMAL</c> (другого приказа
    /// у построителя здесь не бывает) атака остаётся, если <c>p ≥ 0,6</c>, а у цели неиграбельной расы (не человека)
    /// — <c>p ≥ 0,7</c>. Нет — кандидата атаки нет вовсе.
    /// </summary>
    public static bool AttackAlone(double p, bool passed, bool targetHuman, bool targetRaceNeutral) =>
        !passed || p >= (!targetHuman && targetRaceNeutral ? PrepareBelow : WeakStrength);

    /// <summary>
    /// Категории заклинаний перед атакой (<c>0x472142</c>): сила ниже <see cref="WeakStrength"/> при взятом пороге —
    /// только удар и только если цель — отряд вне крепости; иначе удар (цель — отряд вне крепости), призыв, а при
    /// атакующем вне крепости и взятом пороге — ослабление и усиление. Порядок записей роли не играет: годные ищутся
    /// по списку известных заклинаний (<c>0x47263e</c>).
    /// </summary>
    public static IReadOnlyList<SpellCategory> SpellCategories(double p, bool passed, bool targetStackOutside,
                                                               bool attackerOutside)
    {
        if (p < WeakStrength && passed) return targetStackOutside ? [SpellCategory.Attack] : [];
        var r = new List<SpellCategory>();
        if (targetStackOutside) r.Add(SpellCategory.Attack);
        r.Add(SpellCategory.Summon);
        if (attackerOutside && passed) r.AddRange([SpellCategory.Lower, SpellCategory.Boost]);
        return r;
    }

    /// <summary>
    /// Запасной заход <c>0x472142</c>, когда годных нет: наименьший уровень плана не ниже 2 и порог взят — только удар
    /// уровней 1…(наименьший − 1). Иначе запасного захода нет.
    /// </summary>
    public static (int MinLevel, int MaxLevel)? Fallback(int minLevel, bool passed) =>
        minLevel >= 2 && passed ? (1, minLevel - 1) : null;
}
