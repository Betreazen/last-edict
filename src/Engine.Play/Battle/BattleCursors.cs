using LastEdict.Rules.Battle;

namespace LastEdict.Data.Battle;

/// <summary>
/// Курсор экрана боя (<c>updateCursor</c> <c>0x633a1f</c>, <c>docs/battle-screen.md</c> §2.3): вне цели и без права бить — <c>CUDEFAULT</c>;
/// над юнитом или позицией при разрешённом ударе — пара по классу атаки ходящего (<c>0x633d7d</c>, «лечебные» классы — <c>0x630890</c>),
/// из пары — «можно» или «нельзя» (<c>N</c>) по цели под курсором. Картинки — <c>Battle.ff</c> (<c>0x5b343c</c>).
/// </summary>
public static class BattleCursors
{
    public const string Archive = "Battle", Default = "CUDEFAULT";

    /// <summary>Пара курсоров класса атаки: лечение, снятие эффектов, усиление, воскрешение, обереги, дар атаки — <c>CUHEAL</c>; двойник и
    /// превращения — <c>CUTRANSF</c>; прочее (урон, яды, паралич, призыв…) — <c>CUATTACK</c>.</summary>
    public static string Pair(AttackClass attack) => attack switch
    {
        AttackClass.Heal or AttackClass.Cure or AttackClass.BoostDamage or AttackClass.Revive
            or AttackClass.BestowWards or AttackClass.GiveAttack => "CUHEAL",
        AttackClass.Doppelganger or AttackClass.TransformSelf or AttackClass.TransformOther => "CUTRANSF",
        _ => "CUATTACK",
    };

    /// <summary>Курсор над целью: <paramref name="allowed"/> — цель годится (флаг записи цели <c>+0x1d</c> или позиция в карте целей,
    /// <c>0x6338ef</c>).</summary>
    public static string Of(AttackClass attack, bool allowed) => Pair(attack) + (allowed ? "" : "N");
}
