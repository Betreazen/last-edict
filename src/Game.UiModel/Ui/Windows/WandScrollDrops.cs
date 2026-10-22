using D2.Rules.Magic;

namespace D2.Data.Ui.Windows;

/// <summary>
/// Свиток или посох, брошенный на воина в окне с группой (<c>CDDStackGroup</c>, ответ пользователя 17.10: «в инвентаре отряда перетянуть
/// свиток или посох на лидера — и появлялось наведение на цель»). Та же ветка броска предмета <c>IMidItemExt</c>, что у зелий
/// (<see cref="PotionDrops"/>), — по категории предмета: посох (<c>[0x8376d8]</c>) и свиток (<c>[0x8376c8]</c>) идут в ворота
/// <c>0x5de6dc</c> (рамка — <c>0x500d03</c>, бросок — <c>0x4ff396</c>).
/// <para>Ворота <c>0x5de6dc(карта, юнит, держатель, предмет)</c>: предмета нет у держателя (<c>0x5de66a</c>) или юнит не лидер
/// (<c>IUsStackLeader</c> типа, <c>0x40a41f</c>) — 1; лидер павший — 0xD посох / 0xC свиток, если навык <c>L_WAND_SCROLL_USE</c> у него есть,
/// иначе 0x10; живой с навыком — 0, без навыка — 0xF посох / 0xE свиток. Рамка (<c>0x500d03</c>): 0 — жёлтая; 0xC…0x10 — красная;
/// прочее — нет. Бросок (<c>0x4ff396</c>, при рамке не «нет»): 0xD <c>X005TA0644</c>, 0xC <c>X005TA0645</c>, 0xF <c>X005TA0591</c>,
/// 0xE <c>X005TA0592</c>, 0x10 <c>X005TA0858</c>; 0 — отказы <c>0x5dbbe8</c> по отряду группы (<c>0x4ff96b</c>: мана <c>X005TA0214</c>,
/// посох сегодня <c>X005TA0579</c>/<c>0580</c>) и литьё <c>0x40710a</c> — то же, что «Применить» окна посоха
/// (<see cref="WandScrollLogic.Begin"/>).</para>
/// <para>Как у движка, ворота не спрашивают, в отряде ли лидера предмет: свиток со склада крепости или из сумки соседа даёт лидеру с навыком
/// жёлтую рамку, а бросок отказывает уже в <c>0x5dbbe8</c> (код 1; у движка — сообщение «[Error 2 in MidStackGroup]», у порта — молча).</para>
/// </summary>
public static class WandScrollDrops
{
    /// <summary>Посох или свиток — предмет этой ветки.</summary>
    public static bool Handles(Item item) => item is { Category: ItemCategory.Wand or ItemCategory.Scroll };

    /// <summary>Код ворот <c>0x5de6dc</c> (см. описание класса).</summary>
    public static int Gate(WindowContext c, string holderId, string itemId, string unitId)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!c.Session.Holds(holderId, itemId)) return 1;
        if (c.Session.ItemOf(itemId) is not { } item || !Handles(item)) return 8;
        if (!c.Map.Units.TryGetValue(unitId, out var unit) || StackOf(c, unitId) is not { } stack) return 1;
        bool wand = item.Category == ItemCategory.Wand;
        bool skill = c.Session.LeaderAbilitiesOf(stack).Contains(Play.GameSession.WandScrollAbility);
        if (unit.Hp <= 0) return skill ? (wand ? 0xD : 0xC) : 0x10;
        return skill ? 0 : wand ? 0xF : 0xE;
    }

    /// <summary>Рамка позиции (<c>0x500d03</c>): 0 — жёлтая, 0xC…0x10 — красная, прочее — нет.</summary>
    public static DropFrame Frame(WindowContext c, string holderId, string itemId, string unitId) =>
        Gate(c, holderId, itemId, unitId) switch
        {
            0 => DropFrame.Valid,
            >= 0xC and <= 0x10 => DropFrame.Invalid,
            _ => DropFrame.None,
        };

    /// <summary>Бросок (<c>0x4ff396</c>): отказ ворот — сообщение и его имя; иначе литьё (<see cref="WandScrollLogic.Begin"/>, отряд —
    /// тот, чей лидер принял бросок). null — литьё началось.</summary>
    public static string? Cast(WindowContext c, UiRequests requests, string holderId, string itemId, string unitId)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(requests);
        int gate = Gate(c, holderId, itemId, unitId);
        string? text = gate switch
        {
            0xD => "X005TA0644",
            0xC => "X005TA0645",
            0xF => "X005TA0591",
            0xE => "X005TA0592",
            0x10 => "X005TA0858",
            _ => null,
        };
        if (text is not null) requests.Add(new UiTell(c.Text(text)));
        if (gate != 0) return $"посох или свиток не принят (код {gate:X})";
        return WandScrollLogic.Begin(c, requests, StackOf(c, unitId)!.Id, itemId);
    }

    /// <summary>Отряд, чей лидер — этот юнит (<c>IUsStackLeader</c>: в порте лидер — <see cref="D2.Rules.Map.MapStack.LeaderId"/>).</summary>
    private static D2.Rules.Map.MapStack? StackOf(WindowContext c, string unitId) =>
        c.Map.Stacks.Values.FirstOrDefault(s => s.LeaderId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
}
