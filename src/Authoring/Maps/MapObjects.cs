using System.Globalization;
using System.Text.Json.Nodes;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Authoring.Maps;

/// <summary>Вид объекта палитры редактора карт.</summary>
public enum MapObjectKind { Capital, City, Ruin, Merchant, Mage, Mercenaries, Trainer, Crystal, Bag, Landmark, Stack }

/// <summary>Объект карты для вида и выбора: id, вид, угол и сторона клетки (квадрат; у ориентира — его размер), хозяин, подпись.</summary>
public sealed record MapObject(string Id, MapObjectKind Kind, int X, int Y, int W, int H, string Owner, string Title);

/// <summary>Что поставить: хозяин (игрок; нет — нейтралы), имя, юниты (типы: отряд — лидер первым; охрана), предметы (типы), золото,
/// ресурс кристалла (0…5), ориентир (id содержимого), заклинания мага, наёмники.</summary>
public sealed record MapPlacement
{
    public string? Owner { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string> Units { get; init; } = [];
    public IReadOnlyList<string> Items { get; init; } = [];
    public int Gold { get; init; }
    public int Resource { get; init; }
    public string? Landmark { get; init; }
    public IReadOnlyList<string> Spells { get; init; } = [];
}

/// <summary>
/// Объекты карты в сценарии (Студия 2, задача 5; вехи E2–E3 <c>docs/tools/map-editor/plan.md</c>): столица, город, руина, лавка, маг,
/// наёмники, тренер, кристалл, мешок, ориентир, отряд. Объект живёт в нескольких местах сценария сразу — <c>Map.Objects</c> (вид и клетка),
/// <c>Map.Plan</c> (кто на клетке), <c>Map.Sites</c> (поля места), <c>Looks.*</c> (вид), отряды и юниты, экземпляры предметов — и правка здесь
/// держит их согласованными (что проверяет <c>MapInvariants</c>). Работает над JSON сценария (как <see cref="MapFactory"/>); id — класс
/// оригинала с приставкой карты (<c>S9NNKC0003</c>). Отказ — <see cref="InvalidOperationException"/> с текстом.
/// </summary>
public sealed partial class MapObjects(JsonObject root, ContentSet content, Func<int, int, int>? ground = null)
{
    /// <summary>Грунт клетки (0 равнина, 1 лес, 3 вода, 4 горы) — от карты редактора (<see cref="MapWorkspace.Tiles"/>); нет — не проверяется.</summary>
    private int GroundAt(int x, int y) => ground?.Invoke(x, y) ?? 0;

    /// <summary>Сторона квадрата объекта: столица 5, город 4, руина и места 3, прочие 1 (ориентир — по размеру из содержимого).</summary>
    public static int SideOf(MapObjectKind kind) => kind switch
    {
        MapObjectKind.Capital => 5, MapObjectKind.City => 4,
        MapObjectKind.Ruin or MapObjectKind.Merchant or MapObjectKind.Mage or MapObjectKind.Mercenaries or MapObjectKind.Trainer => 3,
        _ => 1,
    };

    private JsonObject LooksNode => (JsonObject)root["Looks"]!;

    private JsonObject MapNode => (JsonObject)LooksNode["Map"]!;

    private int MapSide => MapNode["Size"]!.GetValue<int>();

    private JsonObject Section(string name) => (JsonObject)MapNode[name]!;

    private JsonArray List(JsonObject parent, string name) => parent[name] as JsonArray ?? (JsonArray)(parent[name] = new JsonArray());

    /// <summary>Приставка id карты (<c>S9NN</c>) — по любому объекту; пустая карта — по игрокам.</summary>
    private string Prefix =>
        Section("Objects").Select(p => p.Key).Concat(Section("Players").Select(p => p.Key)).FirstOrDefault(k => k.Length >= 4)?[..4] ?? "S900";

    /// <summary>Новый id класса (<c>KC</c>, <c>UN</c>…): наибольший номер этого класса на карте + 1 (шестнадцатеричный, как у движка).</summary>
    private string NewId(string cls)
    {
        string prefix = Prefix + cls;
        int max = -1;
        foreach (string id in AllIds())
            if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(id[prefix.Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int n))
                max = Math.Max(max, n);
        return $"{prefix}{max + 1:x4}";
    }

    private IEnumerable<string> AllIds() =>
        Section("Objects").Select(p => p.Key).Concat(Section("Units").Select(p => p.Key)).Concat(Section("Stacks").Select(p => p.Key))
            .Concat(Section("ItemTypes").Select(p => p.Key));

    // ---- что на карте

    /// <summary>Все объекты карты (отряды внутри укреплений — тоже: их видно в свойствах укрепления, на карте — укрепление).</summary>
    public IReadOnlyList<MapObject> All()
    {
        var sites = List(MapNode, "Sites").OfType<JsonObject>().ToDictionary(s => Str(s, "Id"), StringComparer.Ordinal);
        var result = new List<MapObject>();
        foreach (var (id, node) in Section("Objects"))
        {
            if (node is not JsonObject o) continue;
            int x = o["X"]!.GetValue<int>(), y = o["Y"]!.GetValue<int>();
            string cls = Str(o, "Kind");
            var site = sites.GetValueOrDefault(id);
            MapObjectKind? kind = cls switch
            {
                "FT" => site is not null && Str(site, "Kind") == "Capital" ? MapObjectKind.Capital : MapObjectKind.City,
                "RU" => MapObjectKind.Ruin, "CR" => MapObjectKind.Crystal, "BG" => MapObjectKind.Bag, "MM" => MapObjectKind.Landmark,
                "KC" => MapObjectKind.Stack,
                "SI" => site is null ? null : Str(site, "Kind") switch
                {
                    "Merchant" => MapObjectKind.Merchant, "Mage" => MapObjectKind.Mage, "Mercenaries" => MapObjectKind.Mercenaries,
                    "Trainer" => MapObjectKind.Trainer, _ => null,
                },
                _ => null,
            };
            if (kind is not { } k) continue;
            var (w, h) = k == MapObjectKind.Landmark && content.Find(Str(o, "TypeId")) is LandmarkDef lm ? (Math.Max(1, lm.Width), Math.Max(1, lm.Height))
                : (SideOf(k), SideOf(k));
            string owner = k == MapObjectKind.Stack ? Str((JsonObject)Section("Stacks")[id]!, "Owner") : site is null ? "" : Str(site, "Owner");
            string title = site is not null && Str(site, "Name") is { Length: > 0 } name ? name
                : k == MapObjectKind.Stack && Section("Stacks")[id] is JsonObject st && Section("Units")[Str(st, "LeaderId")] is JsonObject leader
                    ? Str(leader, "TypeId") : k == MapObjectKind.Landmark ? Str(o, "TypeId") : k.ToString();
            result.Add(new MapObject(id, k, x, y, w, h, owner, title));
        }
        return result;
    }

    /// <summary>Объект под клеткой (верхний — отряд, потом прочие); нет — null. <paramref name="all"/> — уже собранный список (кэш окна).</summary>
    public MapObject? At(int x, int y, IReadOnlyList<MapObject>? all = null)
    {
        all ??= All();
        return all.FirstOrDefault(o => o.Kind == MapObjectKind.Stack && o.X == x && o.Y == y && !Inside(o.Id))
               ?? all.FirstOrDefault(o => o.Kind != MapObjectKind.Stack && x >= o.X && y >= o.Y && x < o.X + o.W && y < o.Y + o.H);
    }

    private bool Inside(string stackId) => Section("Stacks")[stackId] is JsonObject s && Str(s, "Inside").Length > 0;

    /// <summary>Клетки заняты (план) или вне карты — причина; свободно — null.</summary>
    public string? Blocked(int x, int y, int w, int h, string? except = null)
    {
        if (x < 0 || y < 0 || x + w > MapSide || y + h > MapSide) return $"объект {w}×{h} в {x},{y} выходит за карту {MapSide}×{MapSide}";
        var taken = Taken(except);
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                if (taken.Contains((xx, yy))) return $"клетка {xx},{yy} занята";
        return null;
    }

    private HashSet<(int, int)> Taken(string? except)
    {
        var taken = new HashSet<(int, int)>();
        foreach (var cell in List(MapNode, "Plan").OfType<JsonObject>())
            if (cell["Ids"] is JsonArray ids && ids.Any(i => i?.GetValue<string>() is { } s && s != except && !OwnedBy(s, except)))
                taken.Add((cell["X"]!.GetValue<int>(), cell["Y"]!.GetValue<int>()));
        return taken;
    }

    /// <summary>Отряд-визитёр внутри укрепления <paramref name="fort"/> (переносится с ним).</summary>
    private bool OwnedBy(string id, string? fort) =>
        fort is not null && Section("Stacks")[id] is JsonObject s && Str(s, "Inside") == fort;

    // ---- поставить

    /// <summary>Поставить объект палитры в клетку (угол — левый верхний). Возвращает id.</summary>
    public string Place(MapObjectKind kind, int x, int y, MapPlacement p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var (w, h) = kind == MapObjectKind.Landmark ? LandmarkSize(p.Landmark) : (SideOf(kind), SideOf(kind));
        if (Blocked(x, y, w, h) is { } why) throw new InvalidOperationException(why);
        // Вода — ни под что; горы — только под ориентир (он сам бывает горой).
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                if (GroundAt(xx, yy) == 3 || (GroundAt(xx, yy) == 4 && kind != MapObjectKind.Landmark))
                    throw new InvalidOperationException($"клетка {xx},{yy} — {(GroundAt(xx, yy) == 3 ? "вода" : "горы")}: сюда {kind} не ставится");
        return kind switch
        {
            MapObjectKind.Capital => Fort(x, y, capital: true, p),
            MapObjectKind.City => Fort(x, y, capital: false, p),
            MapObjectKind.Ruin => Ruin(x, y, p),
            MapObjectKind.Merchant or MapObjectKind.Mage or MapObjectKind.Mercenaries or MapObjectKind.Trainer => Shop(kind, x, y, p),
            MapObjectKind.Crystal => Crystal(x, y, p),
            MapObjectKind.Bag => Bag(x, y, p),
            MapObjectKind.Landmark => Landmark(x, y, w, h, p),
            MapObjectKind.Stack => Stack(OwnerOf(p), p.Units, x, y, "", 2),
            _ => throw new InvalidOperationException($"вид {kind} не ставится"),
        };
    }

    private (int, int) LandmarkSize(string? id) =>
        id is not null && content.Find(id) is LandmarkDef lm ? (Math.Max(1, lm.Width), Math.Max(1, lm.Height))
            : throw new InvalidOperationException($"ориентира «{id}» нет в содержимом");

    /// <summary>Хозяин места: игрок из размещения или нейтралы.</summary>
    private string OwnerOf(MapPlacement p)
    {
        if (p.Owner is { Length: > 0 } owner)
            return Section("Players").ContainsKey(owner) ? owner : throw new InvalidOperationException($"игрока «{owner}» на карте нет");
        return NeutralPlayer();
    }

    private string NeutralPlayer() =>
        Section("Players").FirstOrDefault(p => p.Value is JsonObject pl && content.Find(Str(pl, "RaceId")) is RaceDef { Neutral: true }).Key
        ?? Section("Players").First().Key;

    /// <summary>Подраса игрока (флаг отрядов): из <c>SubraceOwners</c>; нет — новая.</summary>
    private string SubraceOf(string player)
    {
        var events = root["Events"] as JsonObject ?? (JsonObject)(root["Events"] = new JsonObject());
        var owners = events["SubraceOwners"] as JsonObject ?? (JsonObject)(events["SubraceOwners"] = new JsonObject());
        if (owners.FirstOrDefault(p => p.Value?.GetValue<string>() == player).Key is { } sub) return sub;
        string id = NewSubraceId();
        var race = content.Find(Str((JsonObject)Section("Players")[player]!, "RaceId")) as RaceDef;
        Section("Subraces")[id] = SubraceCode(race);
        owners[id] = player;
        if (LooksNode["Banners"] is JsonObject banners) banners[id] = banners.Count;
        return id;
    }

    /// <summary>Код подрасы (флаг отряда) по роду расы.</summary>
    private static int SubraceCode(RaceDef? race) => race?.Kind switch
    {
        RaceKind.Human => 1, RaceKind.Undead => 2, RaceKind.Heretic => 3, RaceKind.Dwarf => 4, RaceKind.Elf => 14, _ => 5,
    };

    private string NewSubraceId()
    {
        var existing = Section("Subraces").Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; ; i++)
            if (existing.Add($"{Prefix}SR{i:x4}")) return $"{Prefix}SR{i:x4}";
    }

    private string Unit(string type)
    {
        if (content.Find(type) is not UnitDef u) throw new InvalidOperationException($"юнита «{type}» нет в содержимом");
        string id = NewId("UN");
        Section("Units")[id] = new JsonObject
        {
            ["Id"] = id, ["TypeId"] = type, ["Level"] = Math.Max(1, u.Level), ["Hp"] = u.HitPoints, ["Xp"] = 0, ["Modifiers"] = new JsonArray(),
            ["DynLevel"] = false,
        };
        return id;
    }

    private void Cover(string id, int x, int y, int w, int h)
    {
        var plan = List(MapNode, "Plan");
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
            {
                var cell = plan.OfType<JsonObject>().FirstOrDefault(c => c["X"]!.GetValue<int>() == xx && c["Y"]!.GetValue<int>() == yy);
                if (cell is null) plan.Add(cell = new JsonObject { ["X"] = xx, ["Y"] = yy, ["Ids"] = new JsonArray() });
                ((JsonArray)cell["Ids"]!).Add(id);
            }
    }

    private void Uncover(string id)
    {
        var plan = List(MapNode, "Plan");
        foreach (var cell in plan.OfType<JsonObject>().ToList())
        {
            var ids = (JsonArray)cell["Ids"]!;
            foreach (var node in ids.Where(i => i?.GetValue<string>() == id).ToList()) ids.Remove(node);
            if (ids.Count == 0) plan.Remove(cell);
        }
    }

    private void Object(string id, string cls, int x, int y, int w, int h, string typeId = "", int resource = 0)
    {
        Section("Objects")[id] = new JsonObject { ["Id"] = id, ["Kind"] = cls, ["X"] = x, ["Y"] = y, ["Resource"] = resource, ["TypeId"] = typeId };
        Cover(id, x, y, w, h);
    }

    private static JsonObject Zero() => new() { ["Gold"] = 0, ["Infernal"] = 0, ["Life"] = 0, ["Death"] = 0, ["Runic"] = 0, ["Grove"] = 0 };

    private static JsonObject Site(string id, string kind, string raw, int x, int y, string name, string owner) => new()
    {
        ["Id"] = id, ["Kind"] = kind, ["RawKind"] = raw, ["X"] = x, ["Y"] = y, ["Name"] = name, ["Description"] = "", ["Owner"] = owner, ["Cash"] = Zero(),
        ["ItemId"] = "", ["LooterId"] = "", ["Items"] = new JsonArray(), ["Guards"] = new JsonArray(), ["GuardSlots"] = new JsonArray(),
        ["Resource"] = 0, ["VisitorId"] = "", ["Gone"] = false, ["StackId"] = "", ["Subrace"] = "", ["Size"] = null, ["GrowthTurn"] = null,
        ["RiotTurn"] = null, ["RegenBonus"] = null, ["ProtectBonus"] = "", ["Morale"] = null, ["Visitors"] = new JsonArray(), ["Stock"] = new JsonArray(),
        ["Buys"] = null, ["Mission"] = false, ["Roster"] = new JsonArray(), ["Spells"] = new JsonArray(), ["AiPriority"] = 3,
    };

    /// <summary>Клетки группы по порядку заполнения (передний ряд, затем задний).</summary>
    private static readonly int[] Cells = [2, 0, 4, 3, 1, 5];

    /// <summary>Построение группы: клетка → номер юнита (−1 — пусто). Большой юнит — обе клетки колонки (как у карт оригинала: <c>[2,3,0,0,1,-1]</c>),
    /// сначала большие (колонки 1, 0, 2), затем малые — передний ряд, задний. Не влезает в шесть клеток — отказ с текстом.</summary>
    private JsonArray Slots(IReadOnlyList<string> types)
    {
        var slots = new int[6];
        Array.Fill(slots, -1);
        int[] columns = [2, 0, 4];
        int column = 0;
        for (int i = 0; i < types.Count; i++)
            if (content.Find(types[i]) is UnitDef { Small: false })
            {
                while (column < columns.Length && slots[columns[column]] >= 0) column++;
                if (column >= columns.Length) throw new InvalidOperationException("больших юнитов больше, чем колонок (три)");
                slots[columns[column]] = slots[columns[column] + 1] = i;
            }
        for (int i = 0; i < types.Count; i++)
        {
            if (content.Find(types[i]) is UnitDef { Small: false }) continue;
            int cell = Array.FindIndex(Cells, c => slots[c] < 0);
            if (cell < 0) throw new InvalidOperationException("юниты не помещаются в шесть клеток группы");
            slots[Cells[cell]] = i;
        }
        return Arr(slots);
    }

    private string Fort(int x, int y, bool capital, MapPlacement p)
    {
        string owner = capital ? p.Owner ?? throw new InvalidOperationException("у столицы нужен игрок-хозяин") : OwnerOf(p);
        if (!Section("Players").ContainsKey(owner)) throw new InvalidOperationException($"игрока «{owner}» на карте нет");
        if (capital && content.Find(Str((JsonObject)Section("Players")[owner]!, "RaceId")) is RaceDef { Neutral: true })
            throw new InvalidOperationException("у нейтралов столицы нет — выберите игрока-хозяина");
        if (capital && List(MapNode, "Sites").OfType<JsonObject>().Any(s => Str(s, "Kind") == "Capital" && Str(s, "Owner") == owner))
            throw new InvalidOperationException("у этого игрока столица уже есть");
        string id = NewId("FT"), sub = SubraceOf(owner);
        int side = capital ? 5 : 4;
        Object(id, "FT", x, y, side, side);
        // Стража столицы по умолчанию — страж расы; города — данная (не больше трёх).
        IReadOnlyList<string> guardTypes = capital && p.Units.Count == 0
            && content.Find(Str((JsonObject)Section("Players")[owner]!, "RaceId")) is RaceDef { Guardian: { } g } ? [g] : p.Units;
        if (guardTypes.Count > (capital ? 1 : 3)) throw new InvalidOperationException(capital ? "у столицы один страж" : "у города не больше трёх стражей");
        var guards = guardTypes.Select(Unit).ToList();
        var site = Site(id, capital ? "Capital" : "Village", capital ? "Capital" : "MidVillage", x, y, p.Name ?? (capital ? "Столица" : "Город"), owner);
        site["Guards"] = Arr(guards);
        site["GuardSlots"] = Slots(guardTypes);
        site["Subrace"] = sub;
        if (!capital)
        {
            site["Size"] = 1;
            site["GrowthTurn"] = 0;
            site["RiotTurn"] = 0;
            site["RegenBonus"] = 0;
            site["Morale"] = 0;
        }
        List(MapNode, "Sites").Add(site);
        List(LooksNode, "Forts").Add(new JsonObject
        {
            ["Id"] = id, ["Capital"] = capital, ["X"] = x, ["Y"] = y, ["Owner"] = owner, ["Level"] = capital ? 0 : 1, ["Riot"] = false, ["Garrison"] = "",
        });
        return id;
    }

    private string Ruin(int x, int y, MapPlacement p)
    {
        string id = NewId("RU");
        Object(id, "RU", x, y, 3, 3);
        var slots = Slots(p.Units);
        var guards = p.Units.Select(Unit).ToList();
        var site = Site(id, "Ruin", "MidRuin", x, y, p.Name ?? "Руины", "");
        site["Guards"] = Arr(guards);
        site["GuardSlots"] = slots;
        ((JsonObject)site["Cash"]!)["Gold"] = p.Gold;
        if (p.Items.FirstOrDefault() is { } item) site["ItemId"] = Item(item, instance: false);
        List(MapNode, "Sites").Add(site);
        List(LooksNode, "Ruins").Add(new JsonObject { ["Id"] = id, ["X"] = x, ["Y"] = y, ["Looted"] = false, ["Image"] = 0 });
        return id;
    }

    private string Shop(MapObjectKind kind, int x, int y, MapPlacement p)
    {
        string id = NewId("SI");
        Object(id, "SI", x, y, 3, 3);
        var (siteKind, raw, type, title) = kind switch
        {
            MapObjectKind.Merchant => ("Merchant", "MidSiteMerchant", "MERH", "Лавка"),
            MapObjectKind.Mage => ("Mage", "MidSiteMage", "MAGE", "Башня мага"),
            MapObjectKind.Mercenaries => ("Mercenaries", "MidSiteMercs", "MERC", "Лагерь наёмников"),
            _ => ("Trainer", "MidSiteTrainer", "TRAI", "Тренер"),
        };
        var site = Site(id, siteKind, raw, x, y, p.Name ?? title, "");
        if (kind == MapObjectKind.Merchant)
        {
            site["Stock"] = new JsonArray([.. p.Items.Select(i => (JsonNode)new JsonObject { ["Item"] = Item(i, instance: false), ["Count"] = 1 })]);
            site["Buys"] = new JsonObject
            {
                ["Armor"] = true, ["Jewel"] = true, ["Weapon"] = true, ["Banner"] = true, ["Potion"] = true, ["Scroll"] = true, ["Wand"] = true, ["Value"] = true,
            };
        }
        if (kind == MapObjectKind.Mage)
            site["Spells"] = Arr(p.Spells.Select(s => content.Find(s) is SpellDef ? s : throw new InvalidOperationException($"заклинания «{s}» нет")));
        if (kind == MapObjectKind.Mercenaries)
            site["Roster"] = new JsonArray([.. p.Units.Select(u => content.Find(u) is UnitDef def
                ? (JsonNode)new JsonObject { ["UnitType"] = u, ["Level"] = Math.Max(1, def.Level), ["Unique"] = false }
                : throw new InvalidOperationException($"юнита «{u}» нет"))]);
        List(MapNode, "Sites").Add(site);
        List(LooksNode, "Sites").Add(new JsonObject { ["Id"] = id, ["X"] = x, ["Y"] = y, ["Type"] = type, ["Image"] = 0 });
        return id;
    }

    private string Crystal(int x, int y, MapPlacement p)
    {
        string id = NewId("CR");
        int resource = Math.Clamp(p.Resource, 0, 5);
        Object(id, "CR", x, y, 1, 1, resource: resource);
        var site = Site(id, "Crystal", "MidCrystal", x, y, "", "");
        site["Name"] = "";
        site["Resource"] = resource;
        List(MapNode, "Sites").Add(site);
        List(LooksNode, "Crystals").Add(new JsonObject { ["Id"] = id, ["X"] = x, ["Y"] = y, ["Resource"] = resource });
        return id;
    }

    /// <summary>Предмет: тип из содержимого; экземпляр (<c>IM</c>) — с записью в <c>ItemTypes</c>.</summary>
    private string Item(string type, bool instance)
    {
        if (content.Find(type) is not ItemDef) throw new InvalidOperationException($"предмета «{type}» нет в содержимом");
        if (!instance) return type;
        string id = NewId("IM");
        Section("ItemTypes")[id] = type;
        return id;
    }

    private string Bag(int x, int y, MapPlacement p)
    {
        string id = NewId("BG");
        Object(id, "BG", x, y, 1, 1);
        var site = Site(id, "Bag", "MidBag", x, y, "", "");
        site["Name"] = "";
        site["Items"] = Arr(p.Items.Select(i => Item(i, instance: true)).ToList());
        ((JsonObject)site["Cash"]!)["Gold"] = p.Gold;
        List(MapNode, "Sites").Add(site);
        List(LooksNode, "Bags").Add(new JsonObject { ["Id"] = id, ["X"] = x, ["Y"] = y, ["Image"] = 0 });
        return id;
    }

    private string Landmark(int x, int y, int w, int h, MapPlacement p)
    {
        string id = NewId("MM");
        Object(id, "MM", x, y, w, h, typeId: p.Landmark!);
        List(LooksNode, "Landmarks").Add(new JsonObject { ["Id"] = id, ["X"] = x, ["Y"] = y, ["Type"] = p.Landmark });
        return id;
    }

    /// <summary>Отряд: хозяин, юниты (лидер — первый), клетка; внутри укрепления — <paramref name="inside"/>.</summary>
    private string Stack(string owner, IReadOnlyList<string> types, int x, int y, string inside, int order)
    {
        if (types.Count == 0) throw new InvalidOperationException("в отряде нужен хотя бы лидер");
        var slots = Slots(types);
        string id = NewId("KC"), sub = SubraceOf(owner);
        var units = types.Select(Unit).ToList();
        Section("Stacks")[id] = new JsonObject
        {
            ["Id"] = id, ["Owner"] = owner, ["Subrace"] = sub, ["LeaderId"] = units[0], ["LeaderAlive"] = true, ["X"] = x, ["Y"] = y, ["Move"] = 20,
            ["Inside"] = inside, ["UnitIds"] = Arr(units), ["Slots"] = slots, ["Items"] = new JsonArray(), ["TemplateId"] = "",
            ["Equipment"] = Arr(Enumerable.Repeat("", 7)), ["Order"] = order, ["OrderTarget"] = "", ["AiOrder"] = 1, ["AiOrderTarget"] = "", ["AiIgnore"] = false,
            ["UpgradeCount"] = 0, ["BattlesWon"] = 0, ["CreationLevel"] = 1, ["AiPriority"] = 3,
        };
        Object(id, "KC", x, y, 1, 1);
        List(LooksNode, "Stacks").Add(new JsonObject
        {
            ["Id"] = id, ["X"] = x, ["Y"] = y, ["Owner"] = owner, ["Subrace"] = sub, ["LeaderType"] = types[0], ["LeaderAlive"] = true, ["AnyAlive"] = true,
            ["Facing"] = 0, ["Inside"] = inside, ["Invisible"] = false, ["LeaderName"] = "",
        });
        return id;
    }

    // ---- убрать, сдвинуть

    /// <summary>Убрать объект со всем, что ему принадлежит (охрана, юниты отряда, визитёр укрепления, экземпляры предметов мешка).</summary>
    public void Remove(string id)
    {
        if (!Section("Objects").ContainsKey(id)) throw new InvalidOperationException($"объекта «{id}» на карте нет");
        if (EventsUsing(id) is { Count: > 0 } events)
            throw new InvalidOperationException($"на «{id}» ссылаются события ({string.Join(", ", events)}) — сначала поправить их");
        var site = List(MapNode, "Sites").OfType<JsonObject>().FirstOrDefault(s => Str(s, "Id") == id);
        if (site is not null)
        {
            foreach (string unit in Strings(site["Guards"])) Section("Units").Remove(unit);
            foreach (string item in Strings(site["Items"])) Section("ItemTypes").Remove(item);
            List(MapNode, "Sites").Remove(site);
        }
        foreach (var visitor in Section("Stacks").Where(s => s.Value is JsonObject st && Str(st, "Inside") == id).Select(s => s.Key).ToList()) Remove(visitor);
        if (Section("Stacks")[id] is JsonObject stack)
        {
            foreach (string unit in Strings(stack["UnitIds"])) Section("Units").Remove(unit);
            foreach (string item in Strings(stack["Items"])) Section("ItemTypes").Remove(item);
            Section("Stacks").Remove(id);
            foreach (var fort in List(LooksNode, "Forts").OfType<JsonObject>().Where(f => Str(f, "Garrison") == id)) fort["Garrison"] = "";
            foreach (var s in List(MapNode, "Sites").OfType<JsonObject>().Where(s => Str(s, "StackId") == id)) s["StackId"] = "";
        }
        Section("Objects").Remove(id);
        Uncover(id);
        foreach (string list in new[] { "Forts", "Ruins", "Sites", "Crystals", "Bags", "Landmarks", "Stacks" })
            foreach (var entry in List(LooksNode, list).OfType<JsonObject>().Where(e => Str(e, "Id") == id).ToList()) List(LooksNode, list).Remove(entry);
    }

    /// <summary>Сдвинуть объект (угол — в клетку); визитёр укрепления — с ним.</summary>
    public void Move(string id, int x, int y)
    {
        var obj = All().FirstOrDefault(o => o.Id == id) ?? throw new InvalidOperationException($"объекта «{id}» на карте нет");
        if (Blocked(x, y, obj.W, obj.H, except: id) is { } why) throw new InvalidOperationException(why);
        int dx = x - obj.X, dy = y - obj.Y;
        Uncover(id);
        Shift(Section("Objects")[id] as JsonObject, dx, dy);
        Cover(id, x, y, obj.W, obj.H);
        foreach (var s in List(MapNode, "Sites").OfType<JsonObject>().Where(s => Str(s, "Id") == id)) Shift(s, dx, dy);
        foreach (string list in new[] { "Forts", "Ruins", "Sites", "Crystals", "Bags", "Landmarks", "Stacks" })
            foreach (var e in List(LooksNode, list).OfType<JsonObject>().Where(e => Str(e, "Id") == id)) Shift(e, dx, dy);
        Shift(Section("Stacks")[id] as JsonObject, dx, dy);
        foreach (var visitor in Section("Stacks").Where(s => s.Value is JsonObject st && Str(st, "Inside") == id).Select(s => s.Key).ToList())
        {
            Shift(Section("Stacks")[visitor] as JsonObject, dx, dy);
            Shift(Section("Objects")[visitor] as JsonObject, dx, dy);
            Uncover(visitor);   // визитёр стоит в клетке укрепления — план переносится с ним
            Cover(visitor, Section("Stacks")[visitor]!["X"]!.GetValue<int>(), Section("Stacks")[visitor]!["Y"]!.GetValue<int>(), 1, 1);
            foreach (var e in List(LooksNode, "Stacks").OfType<JsonObject>().Where(e => Str(e, "Id") == visitor)) Shift(e, dx, dy);
        }
    }

    private static void Shift(JsonObject? node, int dx, int dy)
    {
        if (node is null) return;
        node["X"] = node["X"]!.GetValue<int>() + dx;
        node["Y"] = node["Y"]!.GetValue<int>() + dy;
    }

    // ---- свойства

    /// <summary>Поле места (<c>Map.Sites</c>): имя, описание, золото, ресурс… — сырое значение JSON; нет места — отказ.</summary>
    public void SetSite(string id, string field, JsonNode? value)
    {
        var site = List(MapNode, "Sites").OfType<JsonObject>().FirstOrDefault(s => Str(s, "Id") == id)
                   ?? throw new InvalidOperationException($"у «{id}» нет полей места");
        switch (field)
        {
            case "Name" or "Description":
                site[field] = value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String ? v.DeepClone()
                    : throw new InvalidOperationException($"{field} — текст");
                break;
            case "Gold":
                ((JsonObject)site["Cash"]!)["Gold"] = Math.Clamp(Int(value, field), 0, 9999);
                break;
            case "Resource" when Str(site, "Kind") == "Crystal":
                int r = Math.Clamp(Int(value, field), 0, 5);
                site["Resource"] = r;
                if (Section("Objects")[id] is JsonObject o) o["Resource"] = r;
                foreach (var c in List(LooksNode, "Crystals").OfType<JsonObject>().Where(c => Str(c, "Id") == id)) c["Resource"] = r;
                break;
            default:
                throw new InvalidOperationException($"поле места {field} Студия так не правит (можно: Name, Description, Gold, Resource кристалла)");
        }
    }

    /// <summary>Отряд: новый состав (лидер — первый), приказ (номер приказа движка) и хозяин.</summary>
    public void SetStack(string id, IReadOnlyList<string>? units = null, int? order = null, string? owner = null)
    {
        if (Section("Stacks")[id] is not JsonObject stack) throw new InvalidOperationException($"отряда «{id}» нет");
        if (units is not null)
        {
            if (units.Count == 0) throw new InvalidOperationException("в отряде нужен хотя бы лидер");
            _ = Slots(units);   // не влезает — отказ до правки
            foreach (string old in Strings(stack["UnitIds"])) Section("Units").Remove(old);
            var ids = units.Select(Unit).ToList();
            stack["UnitIds"] = Arr(ids);
            stack["Slots"] = Slots(units);
            stack["LeaderId"] = ids[0];
            stack["LeaderAlive"] = true;
            foreach (var e in List(LooksNode, "Stacks").OfType<JsonObject>().Where(e => Str(e, "Id") == id)) e["LeaderType"] = units[0];
        }
        if (order is { } o) stack["Order"] = o;
        if (owner is not null)
        {
            if (!Section("Players").ContainsKey(owner)) throw new InvalidOperationException($"игрока «{owner}» на карте нет");
            string sub = SubraceOf(owner);
            stack["Owner"] = owner;
            stack["Subrace"] = sub;
            foreach (var e in List(LooksNode, "Stacks").OfType<JsonObject>().Where(e => Str(e, "Id") == id)) (e["Owner"], e["Subrace"]) = (owner, sub);
        }
    }

    /// <summary>Поля места в виде JSON (для формы свойств); нет — null.</summary>
    public JsonObject? SiteOf(string id) => List(MapNode, "Sites").OfType<JsonObject>().FirstOrDefault(s => Str(s, "Id") == id);

    /// <summary>Отряд в виде JSON; нет — null.</summary>
    public JsonObject? StackOf(string id) => Section("Stacks")[id] as JsonObject;

    /// <summary>Типы юнитов отряда по порядку (лидер — первый).</summary>
    public IReadOnlyList<string> UnitTypes(string stackId) =>
        StackOf(stackId) is { } s ? [.. Strings(s["UnitIds"]).Select(u => Section("Units")[u] is JsonObject unit ? Str(unit, "TypeId") : u)] : [];

    /// <summary>Игроки карты: id, раса, имя.</summary>
    public IReadOnlyList<(string Id, string Race, string Name)> Players() =>
        [.. Section("Players").Select(p => p.Value as JsonObject).OfType<JsonObject>().Select(p => (Str(p, "Id"), Str(p, "RaceId"), Str(p, "Name")))];

    private static int Int(JsonNode? value, string field) =>
        value is JsonValue v && v.TryGetValue<int>(out int n) ? n : throw new InvalidOperationException($"{field} — целое число");

    /// <summary>События, в условиях и действиях которых есть строка <paramref name="id"/> (ссылка на объект, игрока, область).</summary>
    public IReadOnlyList<string> EventsUsing(string id) =>
        root["Events"]?["Events"] is JsonArray events
            ? [.. events.OfType<JsonObject>().Where(e => Mentions(e["Conditions"], id) || Mentions(e["Effects"], id) || Mentions(e["PlayerIds"], id))
                  .Select(e => Str(e, "Id"))]
            : [];

    private static bool Mentions(JsonNode? node, string id) => node switch
    {
        JsonObject o => o.Any(p => Mentions(p.Value, id)),
        JsonArray a => a.Any(v => Mentions(v, id)),
        JsonValue v => v.GetValueKind() == System.Text.Json.JsonValueKind.String && v.GetValue<string>() == id,
        _ => false,
    };

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node is JsonArray a ? a.Select(v => v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null).OfType<string>() : [];

    private static JsonArray Arr<T>(IEnumerable<T> values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v)!)]);
}
