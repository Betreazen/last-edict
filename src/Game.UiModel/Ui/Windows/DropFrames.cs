using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Ui.Windows;

/// <summary>Состояние места группы при перетаскивании (<c>CDDUnitGroup</c>, массив <c>+0x68</c> по шести позициям, <c>0x56eaef</c>):
/// 0 — сюда можно (жёлтая рамка), 1 — нельзя (красная), 2 — рамки нет (так же после броска, <c>0x56eb4b</c>).</summary>
public enum DropFrame
{
    Valid = 0,
    Invalid = 1,
    None = 2,
}

/// <summary>
/// Рамки допустимости в окнах с группами юнитов. Как у движка:
/// <list type="bullet">
/// <item>Когда: начало перетаскивания (<c>CDragAndDropInterf</c> <c>0x56c58a</c>) обходит все места броска окна — каждая группа
/// (<c>CDDUnitGroup</c>, своя и чужая) считает состояние шести позиций один раз (<c>0x56eaef</c>: точка — угол прямоугольника позиции
/// + (1, 1), признак — <c>vt+0x24</c>, у <c>CDDStackGroup</c> <c>0x500d03</c>, у гарнизона <c>CDDReinfGroup</c> <c>0x502186</c>);
/// конец (<c>0x56c606</c>) возвращает всем 2. Инструменты (лечение, воскрешение, отпуск, «Остановить продвижение», обучение) — тоже
/// перетаскивание (<c>CMidDropAction</c>), рамки видны, пока инструмент нажат. Пустая половина окна армии (<c>CDDEmptyGroup</c>) рамок
/// не рисует.</item>
/// <item>Что: <c>0x56e02d</c> идёт по позициям 0…5; у большого юнита — одна рамка колонки (<c>DRAGDROP_LARGE_*</c> 152×85) по
/// состоянию его передней позиции, иначе рамка ячейки (<c>DRAGDROP_SMALL_*</c> 74×89) у каждой позиции, пустой тоже
/// (<c>0x5c9615</c>: первый флаг — допустимо, второй — большая).</item>
/// </list>
/// Правила состояний — ниже по видам тащимого; «можно» у воина — тот же бросок, что делает окно (<see cref="Play.GameSession.Within"/>,
/// <see cref="Play.GameSession.SwapRefusal"/>), поэтому рамка и бросок не расходятся.
/// </summary>
public static class DropFrames
{
    public const string SmallValid = "DRAGDROP_SMALL_VALID", SmallInvalid = "DRAGDROP_SMALL_INVALID";
    public const string LargeValid = "DRAGDROP_LARGE_VALID", LargeInvalid = "DRAGDROP_LARGE_INVALID";

    /// <summary>Кадр рамки (<c>0x5c9615</c>); рамки нет — null.</summary>
    public static string? Image(DropFrame frame, bool large) => frame switch
    {
        DropFrame.Valid => large ? LargeValid : SmallValid,
        DropFrame.Invalid => large ? LargeInvalid : SmallInvalid,
        _ => null,
    };

    /// <summary>
    /// Тащат воина с позиции <paramref name="fromPos"/> группы <paramref name="fromGroup"/> (<c>0x500d03</c> → <c>0x5df4fb</c> и
    /// <c>0x56e75e</c>): только «можно» или «нет рамки», красной у воина не бывает. Можно — обмен клеток пройдёт
    /// (<c>CVisitorSwapUnitPosition::canApply</c> <c>0x61111f</c>): своя группа — любая позиция, с которой есть что переставить, и своя же
    /// клетка или колонка (обмен сам с собой — <c>canApply</c> 1; журнал human: в своей группе <c>swap_check</c> 0 у всех шести позиций);
    /// чужая — <see cref="Play.GameSession.SwapRefusal"/> (лидер и страж свою группу не покидают — коды 3 и 2 <c>0x5df4fb</c>, лидер на месте
    /// цели — <c>0x56e839</c>; полная группа не берёт в пустую клетку — <c>0x56e7db</c>).
    /// </summary>
    public static DropFrame Unit(Play.GameSession session, string fromGroup, int fromPos, string toGroup, int toPos)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!string.Equals(fromGroup, toGroup, StringComparison.OrdinalIgnoreCase))
            return session.SwapRefusal(fromGroup, fromPos, toGroup, toPos) is null ? DropFrame.Valid : DropFrame.None;
        if (session.GroupCells(fromGroup) is not { } cells || cells.Cells[fromPos].Length == 0) return DropFrame.None;
        return SameColumnOrCell(cells, fromPos, toPos) || Play.GameSession.Within(cells, fromPos, toPos) is not null ? DropFrame.Valid : DropFrame.None;
    }

    /// <summary>Та же клетка, а с большим юнитом — та же колонка (<c>0x6116ba</c> сводит позиции к передним клеткам).</summary>
    private static bool SameColumnOrCell(UnitCells cells, int fromPos, int toPos)
    {
        bool column = IsBig(cells, cells.Cells[fromPos]) || (cells.Cells[toPos].Length > 0 && IsBig(cells, cells.Cells[toPos]));
        return column ? Formation.ColumnFront(fromPos) == Formation.ColumnFront(toPos) : fromPos == toPos;
    }

    private static bool IsBig(UnitCells cells, string id)
    {
        int p = cells.PositionOf(id);
        return p >= 0 && string.Equals(cells.Cells[Formation.Pair(p)], id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Воин позиции <paramref name="position"/> группы; пусто — null.</summary>
    private static MapUnit? UnitAt(WindowContext c, UnitCells cells, int position) =>
        cells.Cells[position] is { Length: > 0 } id && c.Map.Units.TryGetValue(id, out var unit) ? unit : null;

    /// <summary>
    /// Инструмент лечения или воскрешения (<c>0x500d03</c>, ветки <c>IMidHealExt</c>/<c>IMidReviveExt</c>): у хозяина нет храма
    /// (<c>0x5dcc28</c> ≠ 0) — рамок нет нигде; пустая позиция — нет. Лечение (<c>0x5de32f</c>): павший (4), здоровый (3), казны не
    /// хватает ни на одно очко (2) — красная, иначе жёлтая. Воскрешение (<c>0x5de06e</c>): павший — жёлтая и при нехватке казны (код 2),
    /// живой (5) — красная.
    /// </summary>
    public static DropFrame Care(WindowContext c, UnitCells cells, int position, string owner, bool revive)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(cells);
        var book = c.Session.RequireBook();
        if (!c.Map.Players.TryGetValue(owner, out var player) || !Healing.HasTemple(player.Buildings, book.Economy.Buildings)) return DropFrame.None;
        if (UnitAt(c, cells, position) is not { } unit) return DropFrame.None;
        if (revive) return unit.Hp <= 0 ? DropFrame.Valid : DropFrame.Invalid;
        int max = c.Session.HpMax(unit);
        if (unit.Hp <= 0 || unit.Hp >= max) return DropFrame.Invalid;
        bool affordable = book.Treasury.Units.TryGetValue(unit.TypeId, out var costs) && Treasury.Heal(costs.Heal, max - unit.Hp, player.Bank).Points > 0;
        return affordable ? DropFrame.Valid : DropFrame.Invalid;
    }

    /// <summary>
    /// Инструмент отпуска (<c>IMidDismissExt</c>): можно отпустить (<see cref="Play.GameSession.DismissCheck"/> 0, у движка проверка удаления
    /// <c>0x5e7a1d</c>/<c>0x5e7b10</c>) — жёлтая, иначе рамки нет; у гарнизона страж (<c>0x5df266</c> код 2) — красная (<c>0x502186</c>).
    /// Герой задания (<c>X005TA0883</c>) рамку не меняет — его отказ говорит бросок.
    /// </summary>
    public static DropFrame Dismiss(WindowContext c, string groupId, UnitCells cells, int position, bool garrison)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Cells[position] is not { Length: > 0 } id) return DropFrame.None;
        int check = c.Session.DismissCheck(groupId, id);
        return check == 0 ? DropFrame.Valid : garrison && check == 2 ? DropFrame.Invalid : DropFrame.None;
    }

    /// <summary>«Остановить продвижение» (<c>IMidDynLevelExt</c>, <c>0x56e75e</c>): лидер (<c>IUsStackLeader</c>) или страж — красная,
    /// любой другой воин — жёлтая, даже уже остановленный (отказ <c>X005TA0939</c> говорит бросок).</summary>
    public static DropFrame StopGrowth(WindowContext c, UnitCells cells, int position)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Cells[position] is not { Length: > 0 } id) return DropFrame.None;
        return c.Session.DynLevelCheck(id) is 0x12 or 0x11 ? DropFrame.Invalid : DropFrame.Valid;
    }

    /// <summary>Обучение (<c>IMidTrainingExt</c>): воина можно учить (<c>0x5e0372</c> = 0, <see cref="Play.GameSession.TrainRefusal"/>) —
    /// жёлтая, нельзя — красная.</summary>
    public static DropFrame Train(WindowContext c, UnitCells cells, int position, string owner)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Cells[position] is not { Length: > 0 } id) return DropFrame.None;
        return c.Session.TrainRefusal(owner, id) is null ? DropFrame.Valid : DropFrame.Invalid;
    }

    /// <summary>
    /// Зелье на воина (<c>IMidItemExt</c>, <c>0x501255</c> → <c>0x5de495</c>): пустая позиция — нет; можно (0) и воскрешение живого (5) —
    /// жёлтая; уже действует (9), здоров (3), павшего не лечат (11) — красная; прочее (усиление павшему 10, не зелье) — рамки нет.
    /// Держатель <paramref name="holderId"/> — отряд или склад укрепления (<see cref="Play.GameSession.Holds"/>); воин — любой группы окна
    /// (<c>0x500d03</c> берёт держателя из тащимого, юнита — из своей группы). Бросок движок принимает при любой рамке, кроме «нет»
    /// (<c>0x56eace</c>), — его делает <see cref="PotionDrops.Drink"/>. Посох и свиток в той же ветке идут в свои ворота <c>0x5de6dc</c> —
    /// <see cref="WandScrollDrops.Frame"/> (прежде здесь ошибочно считалось, что <c>0x5de6dc</c> — сферы и талисманы: категории
    /// <c>[0x8376d8]</c>/<c>[0x8376c8]</c> — посох и свиток, по сообщениям <c>X005TA0591</c>/<c>0592</c> броска <c>0x4ff396</c>). Сферы и
    /// талисманы на воина рамки не дают (<c>0x500d03</c>: прочие категории — 2).
    /// </summary>
    public static DropFrame Potion(WindowContext c, string holderId, string itemId, UnitCells cells, int position)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(cells);
        if (UnitAt(c, cells, position) is not { } unit || c.Session.ItemOf(itemId) is not { } item) return DropFrame.None;
        if (WandScrollDrops.Handles(item)) return WandScrollDrops.Frame(c, holderId, itemId, unit.Id);
        if (!item.IsPotion) return DropFrame.None;
        bool applied = item.PotionModifierId.Length > 0 && unit.Modifiers.Contains(item.PotionModifierId, StringComparer.OrdinalIgnoreCase);
        var gate = ItemUse.CanApplyPotion(item, c.Session.Holds(holderId, itemId), unit.Hp, c.Session.HpMax(unit), applied);
        return gate switch
        {
            PotionResult.Ok => DropFrame.Valid,
            PotionResult.AlreadyAlive when item.Category == ItemCategory.PotionRevive => DropFrame.Valid,
            PotionResult.AlreadyApplied or PotionResult.AlreadyAtFullHp or PotionResult.CannotHealDead => DropFrame.Invalid,
            _ => DropFrame.None,
        };
    }

    /// <summary>
    /// Наёмник из списка на позицию отряда (<c>IMidGlobalUnitExt</c>, хвост <c>0x500d03</c>): малый — пустая позиция при свободных клетках
    /// (<c>0x5f4485</c> &gt; 0); большой — свободных клеток больше одной и пуста вся колонка позиции; иначе рамки нет. Красной нет; цену
    /// проверяет бросок.
    /// </summary>
    public static DropFrame Mercenary(bool big, int freeCells, UnitCells cells, int position)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Cells[position].Length > 0) return DropFrame.None;
        if (!big) return freeCells > 0 ? DropFrame.Valid : DropFrame.None;
        return freeCells > 1 && cells.Cells[Formation.Pair(position)].Length == 0 ? DropFrame.Valid : DropFrame.None;
    }

    /// <summary>Строка рамок группы для проверок: по позициям <c>v</c> жёлтая, <c>i</c> красная, <c>.</c> нет; колонка большого — заглавными
    /// в обеих её позициях (<c>VV</c>, <c>II</c>, <c>..</c>).</summary>
    public static string Line(UiScreenState ui, string group)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var line = new char[UnitGroupLayout.Positions];
        for (int row = 0; row < UnitGroupLayout.Positions / 2; row++)
        {
            string? big = ui.Frame(UnitGroupLayout.BigDropImage(group, row));
            for (int p = row * 2; p < (row * 2) + 2; p++)
                line[p] = big is not null ? (big == LargeValid ? 'V' : 'I') : ui.Frame(UnitGroupLayout.DropImage(group, p)) switch
                {
                    SmallValid => 'v',
                    SmallInvalid => 'i',
                    _ => '.',
                };
        }
        return new string(line);
    }
}
