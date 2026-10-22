using LastEdict.Rules.Magic;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Рамки допустимости для предметов при перетаскивании: слоты куклы лидера и списки предметов (сумка, склад). Как у движка:
/// <list type="bullet">
/// <item>Слот куклы (<c>CDDBaseEquip</c>, конструктор <c>0x5713c9</c>): кадры те же, что у ячейки воина — <c>DRAGDROP_SMALL_VALID</c>/<c>INVALID</c>
/// (<c>0x5c9615(допустимо, 0)</c>), по середине слота (<c>0x5717f7</c>: середина прямоугольника минус половина картинки; слот 70×85, рамка
/// 74×89). Начало тащения (<c>0x571b20</c>): слот-источник — жёлтая, прочие — по <c>vt+0x24</c> (<c>CDDEquipment</c> <c>0x504782</c> →
/// <c>0x571c80</c>), конец (<c>0x571b78</c>) — нет. Правило — <see cref="Slot"/>.</item>
/// <item>Список предметов (<c>CDDFoldedInv</c>, кадры <c>0x56cd57</c>): только <c>DRAGDROP_INVENT_VALID</c> 275×93 в точке списка
/// (<c>0x56cff7</c> рисует лишь при флаге <c>+0x50</c>; «нельзя» не рисуется). Флаг — «примет ли список тащимое» (<c>0x5056f7</c>, одно
/// у сумки отряда и склада): любой предмет (<c>IMidItemExt</c>), кроме взятого из этого же списка.</item>
/// </list>
/// Картинки — элементы <c>{слот}_DROP</c> и <c>{список}_DROP</c> сразу за слотом и списком (<see cref="Insert"/>).
/// </summary>
public static class ItemFrames
{
    public const string ListValid = "DRAGDROP_INVENT_VALID";

    /// <summary>Элемент рамки слота или списка.</summary>
    public static string Element(string slotOrList) => slotOrList + "_DROP";

    /// <summary>Вставить элементы рамок сразу за слотами и списками (рамка слота — в прямоугольнике слота по середине, рамка списка — в
    /// точке списка). Чего нет в форме — пропускается.</summary>
    public static void Insert(List<UiElement> elements, IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(ids);
        foreach (string id in ids)
        {
            int at = elements.FindIndex(e => e.Id == id);
            if (at < 0 || elements.Any(e => e.Id == Element(id))) continue;
            elements.Insert(at + 1, new UiElement { Id = Element(id), Kind = UiKind.Image, Rect = elements[at].Rect });
        }
    }

    /// <summary>
    /// Состояние слота <paramref name="slot"/> куклы лидера отряда <paramref name="stackId"/>, когда тащат предмет <paramref name="itemId"/>
    /// (<c>0x571c80</c>): слот не того рода (<c>0x57201e</c> = <see cref="Equipment.SlotAccepts"/>; не надевается вовсе) — нет; тащат из этого
    /// же слота — жёлтая (<c>0x571b20</c>); из другого слота — жёлтая при живом лидере (<c>0x5e98ad</c>), иначе красная; из списка — проверка
    /// надевания (<see cref="Play.GameSession.EquipCheck"/>: лидер жив, навык <c>0x5ec14c</c>, «ничего не даст» <c>0x5ec48f</c>) — можно жёлтая,
    /// нельзя красная. ponytail: ветка «из слота в слот» (<c>0x571c80</c>, <c>IMidItemEqpExt</c>) прочитана по псевдокоду — ранг 2.
    /// </summary>
    public static DropFrame Slot(Play.GameSession session, string stackId, string itemId, int slot, int? fromSlot)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.ItemOf(itemId) is not { } item || !Equipment.Equippable(item) || !Equipment.SlotAccepts(item.Category, slot)) return DropFrame.None;
        if (fromSlot == slot) return DropFrame.Valid;
        if (fromSlot is not null)
            return session.Map.Stacks.TryGetValue(stackId, out var stack) && session.Map.Units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0
                ? DropFrame.Valid : DropFrame.Invalid;
        return session.EquipCheck(stackId, itemId, slot) == EquipRefusal.Ok ? DropFrame.Valid : DropFrame.Invalid;
    }

    /// <summary>Рамки слотов куклы: <paramref name="slotImages"/> по номерам слотов, <paramref name="state"/> null — рамок нет.</summary>
    public static void Slots(UiScreenState ui, IReadOnlyList<string> slotImages, Func<int, DropFrame>? state)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(slotImages);
        for (int s = 0; s < slotImages.Count; s++)
        {
            string id = Element(slotImages[s]);
            if (!ui.Screen.Elements.Any(e => e.Id == id)) continue;
            string? frame = state is null ? null : DropFrames.Image(state(s), large: false);
            if (frame is null) ui.SetImage(id, null);
            else ui.SetImage(id, frame, UiImageAlign.Center);
        }
    }

    /// <summary>Рамка списка: <paramref name="valid"/> — <c>DRAGDROP_INVENT_VALID</c>, иначе нет.</summary>
    public static void List(UiScreenState ui, string list, bool valid)
    {
        ArgumentNullException.ThrowIfNull(ui);
        string id = Element(list);
        if (ui.Screen.Elements.Any(e => e.Id == id)) ui.SetImage(id, valid ? ListValid : null);
    }

    /// <summary>Строка рамок слотов для проверок: <c>v</c> жёлтая, <c>i</c> красная, <c>.</c> нет.</summary>
    public static string Line(UiScreenState ui, IReadOnlyList<string> slotImages)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(slotImages);
        return new([.. slotImages.Select(s => ui.Frame(Element(s)) switch
        {
            DropFrames.SmallValid => 'v',
            DropFrames.SmallInvalid => 'i',
            _ => '.',
        })]);
    }
}
