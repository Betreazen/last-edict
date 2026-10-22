using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;

namespace LastEdict.Data.Battle;

/// <summary>
/// Применение боевого предмета или зелья в бою (действие 6) — для показа: шаг 12 просмотра движка (<c>showAttackEffect</c> → <c>0x6365f2</c>).
/// Подготовка шага (<c>0x634e0b</c> с признаком «предмет» = 1) берёт «юнита» шага из предмета (<c>0x6498f9</c>, <c>GItem.UNIT_ID</c>): эффект на
/// цели <c>TUCH</c> — его (<c>0x6483b1</c>, по каждой цели без промаха и иммунитета), удар цели <c>HHIT</c> — по классу атаки (<c>0x635914</c>
/// → <c>0x6517e1</c>), звуки — замах и <c>TUCH</c> его списков (<c>SNDINFO</c> +4 и +0x5c). Журналы оригинала: <c>20261017-port-synth</c> BI2
/// (Сфера грома <c>G000IG9027</c>, юнит <c>G000UU0008</c>: <c>ITEM</c> 30 кадров у Баньши, в тот же миг <c>TUCH</c> на гноме, <c>mage1</c> и
/// <c>unit08a</c>, через кадр <c>HHIT</c> и «−103»), <c>20261016e-av</c> (зелья: <c>TUCH</c>, <c>heal</c> + <c>unit161c</c>, «Сила» и «+100»).
/// </summary>
/// <param name="Shown">Атака, по которой показывается применение (класс — удар цели и надпись): у сферы и талисмана — атака предмета
/// (<c>GItem.ATTACK_ID</c>), у зелья — условная по его виду (<see cref="ShownAttack"/>).</param>
/// <param name="Outcome">Исход удара атакой предмета; у зелья — null.</param>
/// <param name="Notes">Надписи удара по состоянию до применения (<see cref="StrikeNotes.Before"/> с атакой <paramref name="Shown"/>).</param>
public sealed record BattleItemUse(int Round, string Bearer, Item Item, string Target, AttackType? Shown, AttackOutcome? Outcome,
                                   int TargetHpBefore, int TargetHpAfter, StrikeNotes Notes)
{
    /// <summary>Запись хода применения (модель её не пишет): носитель → цель, промах — из исхода; <c>Damage</c> — потеря здоровья цели (не больше
    /// её здоровья), надпись по ней же считает <see cref="StrikeNotes"/>.</summary>
    public TurnRecord Record => new(Round, Bearer, Target, Outcome?.Miss ?? false, Math.Max(0, TargetHpBefore - TargetHpAfter), TargetHpAfter,
                                    Crit: Outcome?.CritDamage ?? 0);

    /// <summary>Цель устояла: иммунитет или оберег (<c>[rec+9]</c> — у движка без <c>TUCH</c> и удара).</summary>
    public bool Immune => Outcome?.Immune ?? false;

    /// <summary>Атака показа: у сферы и талисмана — атака предмета; у зелья (атаки нет) — условная атака класса по виду зелья: силы и вечное
    /// (<c>MOD_POTION</c>) — усиление, надпись «Сила» <c>X008TA0003</c> (журнал <c>20261016e-av</c>: Эликсир силы <c>G000IG0003</c>, хотя атака его
    /// юнита <c>G000UU0161</c> — «дар атаки»), лечения — лечение, «+N» (там же, «+100»), жизни — воскрешение (ранг 2: в корпусе не показано).
    /// Нет справочника — null.</summary>
    public static AttackType? ShownAttack(Item item, GameRules? rules)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (rules is null) return null;
        if (!item.IsPotion) return rules.Attacks.GetValueOrDefault(item.AttackId);
        var cls = item.Category switch
        {
            ItemCategory.PotionHeal => AttackClass.Heal,
            ItemCategory.PotionRevive => AttackClass.Revive,
            _ => AttackClass.BoostDamage,
        };
        return new AttackType(item.Id, 0, cls, 0, Reach: 2, 0, 0, false, 1);   // одна цель (Gattacks.REACH 2)
    }
}
