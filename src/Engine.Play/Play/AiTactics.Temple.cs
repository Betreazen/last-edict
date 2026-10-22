using D2.Rules.Magic;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>
/// «Храм» ИИ (<c>VisitTemple</c>, тип 8; <c>docs/ai-tactic-candidates.md</c> §14) — торговец-миссия (<c>MidSiteMerchant.MISSION</c>,
/// <c>+0x88</c>), у которого раненый отряд покупает зелья. Ранг 1 по листингам 25.09, если не сказано иное.
/// </summary>
public static partial class AiTactics
{
    /// <summary>Урон отряда, с которого храм оценивается (<c>0x46c445</c>, константа <c>0x6d25a8</c>).</summary>
    public const double TempleDamage = 0.3;

    /// <summary>Код <c>0x5de06e</c> «у хозяина нет храма» — счёт храма ×1,1.</summary>
    private const int NoTempleCode = 6;

    /// <summary>
    /// <c>VisitTemple</c>: построитель <c>0x46c351</c> (оценщик vtable <c>0x6d2594</c>, слот <c>0x10</c> <c>0x46c445</c>): столица хозяина
    /// взята — нет; по местам кэша (<see cref="PlaceTargets"/>, до трёх ближайших площадок любого класса) класса торговца
    /// (<c>0x798520</c>) с флагом миссии: урон отряда (<see cref="Damage"/>, <c>0x468969</c>) меньше 0,3 — нет; счёт <c>0x46c5c5</c>
    /// (<see cref="AiTacticEval.VisitTemple"/>: помочь можно — <see cref="TempleHelps"/>; код воскрешения лидера
    /// <see cref="LeaderReviveCode"/> = 6 — ×1,1), NoScore — нет. Не вплотную ко входу (слот <c>0x2c</c> обёртки) — перемещение
    /// <c>0x45a915</c> к входу (режим 1, N = 0, статус 5); вплотную — посещение <c>CAiVisitTempleAction</c> (<c>0x46c906</c>, vtable
    /// <c>0x6d263c</c>; <see cref="AiTacticOption.Direct"/>): покупки <see cref="BuyAtTemple"/>, статус всегда 1. Очки хода вплотную не
    /// проверяются (оценщик дважды зовёт только слот <c>0x2c</c>; у торговца и мага есть ещё <c>+0x98</c> ≥ 1).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> VisitTemples(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        var map = session.Map;
        if (map.Players.TryGetValue(stack.Owner, out var owner) && owner.CapturedBy.Length > 0) yield break;
        var self = Position(map, stack);
        foreach (var site in PlaceTargets(session, turn, stack))
        {
            if (site.Kind != SiteKind.Merchant || !site.Mission) continue;
            double damage = Damage(session, stack);
            if (damage < TempleDamage) continue;
            var at = SiteEntrance(site);
            double score = AiTacticEval.VisitTemple(turn.Weights, damage, Cheb(self, at), TempleHelps(session, turn.Purse, stack, site),
                                                    LeaderReviveCode(session, stack) == NoTempleCode,
                                                    turn.Memo.Status(stack.Id, site.Id, at.X, at.Y, turn.Day));
            if (score == AiTactic.NoScore) continue;
            yield return (new AiTacticOption(AiActionType.VisitTemple, stack.Id, site.Id, at.X, at.Y, Direct: Cheb(self, at) <= 1), score);
        }
    }

    /// <summary>
    /// Храм поможет (<c>0x46c755</c>): на складе есть зелье, чью цену (<c>0x5dfd5f</c>: <c>VALUE</c> со скидкой лидера) кошелёк тактики покрывает, —
    /// воскрешения (<c>[0x837668]</c>), когда в отряде есть павший, или лечения (<c>[0x8376f8]</c>), когда есть живой раненый
    /// (здоровье ниже максимума с модификаторами — слот <c>0x24</c> реализации).
    /// </summary>
    public static bool TempleHelps(GameSession session, Bank purse, MapStack stack, MapSite site)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(purse);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(site);
        var map = session.Map;
        var units = stack.UnitIds.Select(u => map.Units.GetValueOrDefault(u)).OfType<MapUnit>().ToList();
        bool dead = units.Any(u => u.Hp <= 0);
        bool injured = units.Any(u => u.Hp > 0 && u.Hp < session.HpMax(u));
        var items = session.Book!.Items;
        return site.Stock.Any(r => r.Count > 0 && items.TryGetValue(r.Item, out var item)
                                   && !Economy.NotEnough(purse, session.ItemPrice(stack, item))
                                   && (item.Category == ItemCategory.PotionRevive && dead || item.Category == ItemCategory.PotionHeal && injured));
    }

    /// <summary>
    /// Код воскрешения лидера отряда (<c>0x5de06e</c> от <c>CMidStack +0x28</c> и хозяина <c>+0x8c</c>; ранг 2 на смысл полей): жив —
    /// 5; казна хозяина не покрывает <c>REVIVE_C</c> (слот <c>0x58</c>) — 2; у хозяина нет храма (постройка категории
    /// <c>[0x8379f0]</c>, <see cref="Healing.HasTemple"/>) — 6; отряд не в крепости (<c>+0x94</c>) — 7; иначе 0.
    /// </summary>
    public static int LeaderReviveCode(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        if (!map.Units.TryGetValue(stack.LeaderId, out var leader) || leader.Hp > 0) return 5;
        if (!map.Players.TryGetValue(stack.Owner, out var owner) || session.ReviveRate(leader.Id) is not { } price
            || Economy.NotEnough(owner.Bank, price)) return 2;
        if (!Healing.HasTemple(owner.Buildings, session.Book!.Economy.Buildings)) return NoTempleCode;
        return stack.Inside.Length == 0 ? 7 : 0;
    }

    /// <summary>
    /// Решение ИИ в храме (колбэк посещения <c>0x46c9af</c> → <c>0x46c9f8</c>): по юнитам отряда по порядку — каждому павшему по
    /// зелью воскрешения, пока покупка удаётся; затем каждому юниту со здоровьем ниже максимума (павшим тоже) — <b>снова зелье
    /// воскрешения</b>: оба прохода передают <c>0x46cb21</c> категорию <c>0x837660</c> (воскрешение), а построитель ждёт для раненых
    /// зелье лечения. Ошибка оригинала — повторяем. Покупка <c>0x46cb21</c>: первая строка склада этой категории (<c>0x46cc03</c>),
    /// цена кошельку не по силам — отказ (проход кончается); иначе команда покупки (<c>0x41e575</c> → <see cref="GameSession.BuyItem"/>)
    /// и цена из кошелька тактики — что бы ни ответил сервер. Возвращает купленные типы.
    /// </summary>
    public static IReadOnlyList<string> BuyAtTemple(GameSession session, AiTacticTurn? turn, string stackId, string siteId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        if (!map.Stacks.TryGetValue(stackId, out var stack)
            || map.Sites.FirstOrDefault(s => Same(s.Id, siteId)) is not { Kind: SiteKind.Merchant }) return [];
        var purse = turn?.Purse ?? map.Players[stack.Owner].Bank;
        var bought = new List<string>();
        bool Buy()
        {
            var site = session.Map.Sites.First(s => Same(s.Id, siteId));
            var items = session.Book!.Items;
            var row = site.Stock.FirstOrDefault(r => r.Count > 0 && items.TryGetValue(r.Item, out var i) && i.Category == ItemCategory.PotionRevive);
            if (row is null) return false;
            var price = session.ItemPrice(stack, items[row.Item]);
            if (Economy.NotEnough(purse, price)) return false;
            if (session.BuyItem(stackId, siteId, row.Item) is null) bought.Add(row.Item);
            purse = Treasury.Pay(purse, price);
            turn?.Spend(price);
            return true;
        }
        foreach (string unitId in stack.UnitIds)
            if (map.Units.TryGetValue(unitId, out var unit) && unit.Hp <= 0 && !Buy()) break;
        foreach (string unitId in stack.UnitIds)
            if (map.Units.TryGetValue(unitId, out var unit) && unit.Hp < session.HpMax(unit) && !Buy()) break;
        return bought;
    }
}
