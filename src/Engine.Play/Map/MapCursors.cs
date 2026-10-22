using LastEdict.Data.Play;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>
/// Курсор над клеткой карты (<c>docs/ai-screen.md</c> §10; разбор <c>Discipl2.exe</c>, ранг 1 по листингу, игрой не проверено):
/// задача по умолчанию (<c>0x48d46d</c>) — выбран свой отряд: <c>CTaskSelectUnit</c> (кэш восьми курсоров <c>0x4cf8fa</c>, выбор
/// <c>0x4d0807</c>), выбрано своё укрепление: <c>CTaskSelectCity</c> (<c>0x4cf62e</c>), ничего: <c>CTaskStrat</c> (<c>0x4d3e63</c>); чужой
/// ход — <c>CTaskWait</c>, всегда <see cref="Default"/>. Имена — картинки <c>IsoCursr</c>, горячая точка — середина картинки
/// (<c>0x5b27ee</c>).
/// </summary>
public static class MapCursors
{
    public const string Default = "DEFAULT", SelectUnit = "SELECTUNIT", SelectCity = "SELECTCITY", ManageStack = "MANAGESTK",
        ViewCity = "VIEWCITY", Move = "MOVE", Alliance = "ALLIANCE", Nego = "NEGO", Battle = "BATTLE", EnterCity = "ENTERCITY",
        Visit = "VISIT", TakeBag = "TAKEBAG", BreakRod = "BREAKROD";

    /// <summary>Что под курсором и чей ход: карта, кто смотрит, что выбрано, скрыта ли клетка.</summary>
    public sealed record Context(MapLooks Looks, GameBook? Book, string? Viewer, string? SelectedStack, string? SelectedFort,
                                 Func<int, int, bool> Hidden, Func<MapStack, int, int, bool> CanMove);

    /// <summary>Курсор над клеткой <paramref name="x"/>,<paramref name="y"/>.</summary>
    public static string At(Context c, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(c);
        var map = c.Looks.Map;
        if (c.SelectedStack is { } id && map.Stacks.TryGetValue(id, out var selected) && Own(c, selected.Owner))
            return UnitTask(c, selected, x, y);
        if (c.SelectedFort is { } fort && FortAt(map, x, y) is { } site && site.Id.Equals(fort, StringComparison.OrdinalIgnoreCase))
            return ViewCity;
        return Strat(c, x, y);
    }

    /// <summary><c>CTaskStrat</c> (<c>0x4d3e63</c>): свой отряд — выбранный <see cref="ManageStack"/>, другой <see cref="SelectUnit"/>
    /// (пустая клетка — отряд с клетки (x+1, y+1)); своё укрепление — <see cref="SelectCity"/>, а в его воротах — только при госте;
    /// прочее и туман — <see cref="Default"/>.</summary>
    private static string Strat(Context c, int x, int y)
    {
        var map = c.Looks.Map;
        if (!map.Contains(x, y) || c.Hidden(x, y)) return Default;
        var stack = StackAt(map, x, y) ?? (Empty(map, x, y) ? StackAt(map, x + 1, y + 1) : null);
        if (stack is not null && Own(c, stack.Owner) && stack.Inside.Length == 0)
            return stack.Id.Equals(c.SelectedStack, StringComparison.OrdinalIgnoreCase) ? ManageStack : SelectUnit;
        if (FortAt(map, x, y) is { } fort && Own(c, fort.Owner))
            return !InGate(map, fort, x, y) || fort.StackId.Length > 0 ? SelectCity : Default;
        return Default;
    }

    /// <summary><c>CTaskSelectUnit</c> (<c>0x4d0807</c>): туман — как <see cref="Strat"/>; пусто — <see cref="Move"/>, если отряд туда
    /// может пойти (<c>CVisitorMoveStack::canApply</c>); враждебный отряд с (x+1, y+1) у пустой клетки — как его клетка.</summary>
    private static string UnitTask(Context c, MapStack selected, int x, int y)
    {
        var map = c.Looks.Map;
        if (!map.Contains(x, y) || c.Hidden(x, y)) return Strat(c, x, y);
        string type = map.Units.TryGetValue(selected.LeaderId, out var leader) ? leader.TypeId : "";
        int? category = c.Book?.CategoryOf(type);
        bool illusion = category == UnitCategory.Illusion, thief = category == UnitCategory.Noble;
        bool alive = selected.LeaderAlive;
        var stack = StackAt(map, x, y);
        if (stack is null && ClickCell(map, selected.Owner, x, y, c.Hidden, Invisible(c)) is var aim && aim != (x, y))
            stack = StackAt(map, aim.X, aim.Y);
        if (stack is not null)
        {
            if (Own(c, stack.Owner)) return Strat(c, x, y);
            if (c.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)) is { Invisible: true }) return Move;
            if (Allied(c, stack.Owner)) return Alliance;
            if (thief) return alive ? Nego : Strat(c, x, y);
            if (illusion) return Strat(c, x, y);
            return alive || stack.Inside.Length == 0 ? Battle : Strat(c, x, y);
        }
        foreach (var site in SitesAt(map, x, y))
        {
            switch (site.Kind)
            {
                case SiteKind.Capital or SiteKind.Village when Own(c, site.Owner):
                    bool enter = InGate(map, site, x, y) && site.StackId.Length == 0 && !illusion && category != UnitCategory.Summon;
                    return enter ? EnterCity : Strat(c, x, y);
                case SiteKind.Capital when map.Players.TryGetValue(site.Owner, out var owner) && owner.CapturedBy.Length > 0:
                    return Strat(c, x, y);
                case SiteKind.Capital or SiteKind.Village when Allied(c, site.Owner):
                    return Alliance;
                case SiteKind.Capital or SiteKind.Village:
                    return thief ? Nego : alive && !illusion ? Battle : Strat(c, x, y);
                case SiteKind.Bag when !site.Gone:
                    return illusion ? Strat(c, x, y) : TakeBag;
                case SiteKind.Merchant or SiteKind.Mage or SiteKind.Mercenaries or SiteKind.Trainer:
                    return !alive || illusion ? Strat(c, x, y) : thief ? Nego : Visit;
                case SiteKind.Ruin when site.LooterId.Length == 0:
                    return !alive || illusion ? Strat(c, x, y) : thief ? Nego : Battle;
            }
        }
        if (RodAt(map, x, y) is { } rod && c.Book?.LeaderCategoryOf(type) == RodPlanter)
            return Allied(c, rod.Owner) ? Alliance : BreakRod;
        if (Empty(map, x, y)) return c.CanMove(selected, x, y) ? Move : Strat(c, x, y);
        return Strat(c, x, y);
    }

    /// <summary>
    /// Клетка, на которую нацелен щелчок (и курсор) выбранного отряда хозяина <paramref name="owner"/> по клетке <paramref name="x"/>,<paramref name="y"/>
    /// — <c>0x4d04c3</c> (его зовут курсор <c>0x4d0807</c>, показ пути <c>0x4d1590</c> и щелчок <c>0x4d0e2c</c>): в клетке нет объектов плана
    /// (<c>0x5f5797</c>; дорога — тоже объект плана), а клетка (x+1, y+1) — на карте, не в тумане (<c>0x4d07e2</c> → <c>0x5f3eac</c>), занята
    /// (<c>0x5f5436</c>), и в ней отряд чужого хозяина (тип 0x23, <c>0x5deb24(…, видеть невидимых 0, внутри 0, без рас 1)</c>: не свой, не в
    /// крепости, не невидимый; союз не проверяется) — щелчок целит в этот отряд: его картинка закрывает клетку над ним. Иначе — сама клетка.
    /// Журнал 20261017-port-synth, карта 10, VA1: щелчок по пустой (10,20) над Гоблином (11,21) — путь к Гоблину (<c>path</c> seq 3131872:
    /// цель (11,21)), курсор <c>BATTLE</c>; по корпусу — <c>ClickAimHarnessTests</c>.
    /// </summary>
    public static (int X, int Y) ClickCell(MapState map, string owner, int x, int y, Func<int, int, bool> hidden, Func<string, bool> invisible)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(invisible);
        int qx = x + 1, qy = y + 1;
        if (!Empty(map, x, y) || !map.Contains(qx, qy) || hidden(qx, qy)) return (x, y);
        return StackAt(map, qx, qy) is { } near && !near.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase) && near.Inside.Length == 0
               && !invisible(near.Id) ? (qx, qy) : (x, y);
    }

    private static Func<string, bool> Invisible(Context c) =>
        id => c.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { Invisible: true };

    /// <summary>Категория лидера «жезлоносец» (<c>L_ROD</c>, умение «Водрузить жезл»).</summary>
    private const int RodPlanter = 4;

    private static bool Own(Context c, string owner) =>
        c.Viewer is null || owner.Equals(c.Viewer, StringComparison.OrdinalIgnoreCase);

    /// <summary>Хозяин — союзник зрителя (<c>0x5f7f5a</c>): союз рас в отношениях карты.</summary>
    private static bool Allied(Context c, string owner)
    {
        var map = c.Looks.Map;
        if (c.Viewer is null || c.Book is null || !map.Players.TryGetValue(c.Viewer, out var me) || !map.Players.TryGetValue(owner, out var other))
            return false;
        return Diplomacy.PartyOf(me, c.Book.Economy.Races) is { } a && Diplomacy.PartyOf(other, c.Book.Economy.Races) is { } b && a != b
               && Diplomacy.AreAllies(map.Relations, a, b);
    }

    private static IEnumerable<string> Ids(MapState map, int x, int y) =>
        map.Plan.TryGetValue((x, y), out var ids) ? ids : [];

    private static MapStack? StackAt(MapState map, int x, int y) =>
        Ids(map, x, y).Select(id => map.Stacks.GetValueOrDefault(id)).OfType<MapStack>().FirstOrDefault();

    private static IEnumerable<MapSite> SitesAt(MapState map, int x, int y) =>
        Ids(map, x, y).Select(id => map.Sites.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).OfType<MapSite>();

    private static MapSite? FortAt(MapState map, int x, int y) =>
        SitesAt(map, x, y).FirstOrDefault(s => s.Kind is SiteKind.Capital or SiteKind.Village);

    private static MapRod? RodAt(MapState map, int x, int y) =>
        Ids(map, x, y).Select(id => map.Rods.GetValueOrDefault(id)).OfType<MapRod>().FirstOrDefault();

    /// <summary>В клетке нет объектов плана (<c>CMidgardPlan</c> из сохранения: дорога там тоже есть — щелчки корпуса с <c>RA</c> в
    /// <c>objects</c>).</summary>
    private static bool Empty(MapState map, int x, int y) => !Ids(map, x, y).Any();

    /// <summary>Ворота укрепления (<c>0x5e948d</c>): квадрат 2×2 в углу входа (позиция + размер − 1). Ранг 3 — какой угол квадрата.</summary>
    private static bool InGate(MapState map, MapSite fort, int x, int y) =>
        PathFinder.EntranceOf(map, fort.Id) is { } gate && x >= gate.X - 1 && x <= gate.X && y >= gate.Y - 1 && y <= gate.Y;
}
