using D2.Rules.Magic;
using D2.Data.Play.Commands;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Зелье, брошенное на воина в окне с группами (<c>CDDStackGroup</c>, бросок <c>0x4ff396</c>): окна отряда, армии, обмена и торговца —
/// где есть и группа, и сумка (склад) с предметами <c>IMidItemExt</c> (<c>CDDStackInventoryDisplay</c>, <c>CDDFortInventoryDisplay</c>,
/// слоты куклы <c>CDDEquipment</c>). Бросок принимается, если рамка позиции не «нет» (<c>0x56eace</c>, <see cref="DropFrames.Potion"/>).
/// Ворота <c>0x5de495</c>: 0 — звук зелья по категории и команда <c>0x41f172</c> (<see cref="Play.GameSession.DrinkPotion"/>); иначе
/// сообщение: уже действует (9) — <c>X005TA0593</c>, усиление павшему (10) — <c>X005TA0643</c>, лечение павшему (0xB) — <c>X005TA0596</c>,
/// здоров (3) — <c>X005TA0594</c>, воскрешение живому (5, рамка жёлтая) — <c>X005TA0595</c>; прочие отказы — молча. Посох и свиток —
/// литьё через лидера (<see cref="WandScrollDrops"/>).
/// </summary>
public static class PotionDrops
{
    /// <summary>Бросок зелья <paramref name="itemId"/> держателя <paramref name="holderId"/> на воина <paramref name="unitId"/>: отказ —
    /// его имя (и сообщение, если оно у движка есть), выпито — null. Не зелье — «не зелье», ничего не происходит.</summary>
    public static string? Drink(WindowContext c, UiRequests requests, string holderId, string itemId, string unitId)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(requests);
        if (c.Session.ItemOf(itemId) is { } scroll && WandScrollDrops.Handles(scroll))
            return WandScrollDrops.Cast(c, requests, holderId, itemId, unitId);   // посох и свиток — та же ветка броска (0x4ff396)
        if (c.Session.ItemOf(itemId) is not { IsPotion: true } item) return "не зелье";
        string? refusal = c.Session.Run<string?>(new DrinkPotionCommand(holderId, itemId, unitId));
        if (refusal is null) c.Announce(Play.TurnSightKind.PotionDrunk, item.Id);   // звук зелья — показ (сессия Г)
        else if (RefusalText(refusal) is { } text) requests.Add(new UiTell(c.Text(text)));
        return refusal;
    }

    /// <summary>Сообщение отказа ворот (<see cref="PotionResult"/> по имени, как его возвращает сессия); без сообщения — null.</summary>
    public static string? RefusalText(string refusal) => Enum.TryParse<PotionResult>(refusal, out var gate) ? gate switch
    {
        PotionResult.AlreadyApplied => "X005TA0593",
        PotionResult.CannotBoostDead => "X005TA0643",
        PotionResult.CannotHealDead => "X005TA0596",
        PotionResult.AlreadyAtFullHp => "X005TA0594",
        PotionResult.AlreadyAlive => "X005TA0595",
        _ => null,
    } : null;
}
