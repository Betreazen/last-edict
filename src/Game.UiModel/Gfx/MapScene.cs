using System.Globalization;
using D2.Data.Map;
using D2.Rules.Map;

namespace D2.Data.Gfx;

/// <summary>Картинка карты: где лежит (архив, имя, анимация или кадр), куда кладётся левым верхним углом
/// (<see cref="X"/>, <see cref="Y"/> — мировые точки), слой и якорная клетка — ключ порядка. <see cref="Shadow"/> —
/// тень (сплошной почти чёрный силуэт, как движок её смешивает, не разобрано).</summary>
public sealed record MapSprite(string Archive, string Name, bool Anim, int Frames, int X, int Y, int Width, int Height,
                               int Layer, int AnchorX, int AnchorY, string Source, bool Shadow = false)
{
    /// <summary>Ключ движка (<c>0x545a58</c>): <c>((L>>9)·256 + ax + ay)·512 + L</c>.</summary>
    public long Key => ((long)((Layer >> 9) * 256 + AnchorX + AnchorY) * 512) + Layer;
}

/// <summary>Цели сценария для круга <c>SYM_OBJECTIVE</c>: id объектов (<c>GameSession.Objectives</c>) и области сценария (у области нет
/// облика на карте — место и радиус из событий).</summary>
public sealed record MapObjectives(IReadOnlyCollection<string> Ids, IReadOnlyDictionary<string, EventZone> Zones);

/// <summary>Найденная картинка: архив, анимация ли, число кадров, размер холста.</summary>
public readonly record struct ArtRef(string Archive, string Name, bool Anim, int Frames, int Width, int Height);

/// <summary>Картинки карты для вида: список <see cref="MapSprite"/> в порядке отрисовки движка (над землёй). Правила —
/// <c>docs/map-render.md</c> (объекты, якорь, порядок) и <c>docs/map-render-stacks.md</c> (отряды, знамёна).
///
/// <para>Вид — глазами игрока <c>viewer</c>: его туман (<see cref="MapFog"/>) закрывает скрытые клетки и прячет
/// объекты, чужие невидимые отряды не рисуются, свои — со значком. Без игрока — как в редакторе: тумана нет, все
/// невидимые со значком. Локации (круги редактора) и цели сценария пока не рисуются, прозрачность кадров
/// (<c>opacity</c> описи) не учитывается.</para></summary>
public sealed class MapScene
{
    /// <summary>Коды рас в именах картинок, индекс — номер расы в <c>Grace</c> (<c>G000RR000n</c>): HU, DW, HE, UN,
    /// NE, EL (<c>0x5c2ffb</c>).</summary>
    private static readonly string[] RaceCodes = ["HU", "DW", "HE", "UN", "NE", "EL"];

    /// <summary>Код расы земли клетки (<c>Lterrain</c> 1..6): HU, DW, HE, UN, NE, EL.</summary>
    private static readonly string[] TerrainCodes = ["", "HU", "DW", "HE", "UN", "NE", "EL"];

    /// <summary>Коды ресурса кристалла (<c>Lres</c> 0..5, <c>0x5c4ce7</c>).</summary>
    private static readonly string[] CrystalCodes = ["GL", "RD", "YE", "RG", "WH", "GR"];

    private const string NeutralRace = "G000RR0004";

    private readonly MapLooks _looks;
    private readonly MapArtData _art;
    private readonly GfxAssets _gfx;
    private readonly bool _animated;
    private readonly List<MapSprite> _sprites = [];
    private readonly List<string> _missing = [];
    private readonly string? _viewer;
    private string? _hidden;
    private readonly bool[,]? _fog;

    private MapScene(MapLooks looks, MapArtData art, GfxAssets gfx, bool animated, string? viewer, bool[,]? fog)
    {
        _looks = looks;
        _art = art;
        _gfx = gfx;
        _animated = animated;
        _viewer = viewer;
        if (viewer is not null && looks.Map.Players.TryGetValue(viewer, out var player) &&
            looks.Map.Fog.TryGetValue(player.FogId, out var mask))
            _fog = fog is not null && fog.GetLength(0) == mask.GetLength(0) && fog.GetLength(1) == mask.GetLength(1) ? fog : mask;
    }

    /// <summary>Скрыта ли клетка для игрока (за картой — скрыта).</summary>
    public bool Hidden(int x, int y) =>
        _fog is not null && (!_looks.Map.Contains(x, y) || _fog[x, y]);

    /// <summary>Картинки в порядке отрисовки: по ключу, затем по <c>Y</c>, затем по <c>X</c> (<c>0x66e90f</c>).</summary>
    public IReadOnlyList<MapSprite> Sprites { get; private set; } = [];

    /// <summary>Имена, которых нет в графике (объект без картинки, как у движка при ненайденном имени).</summary>
    public IReadOnlyList<string> Missing => _missing;

    /// <param name="animated">Настройка <c>AnimatedIso</c>: картинки из IsoAnim, иначе из IsoStill.</param>
    /// <param name="viewer">Игрок, чьими глазами смотрим (id <c>PL</c>); null — вид редактора, без тумана.</param>
    /// <param name="hiddenStack">Отряд, который сейчас идёт: его картинки в сцену не попадают, их рисует сцена
    /// отдельно и двигает по пикселям (<c>docs/map-render-stacks.md</c> §6).</param>
    /// <param name="objectives">Цели сценария глазами зрителя (<c>GameSession.Objectives</c>) — круг <c>SYM_OBJECTIVE</c>; null — без них.</param>
    /// <param name="fog">Туман, который сейчас показан зрителю, вместо тумана модели (смена тумана идёт кольцами, <see cref="D2.Data.Map.FogReveal"/>);
    /// null или другого размера — туман модели.</param>
    public static MapScene Build(MapLooks looks, MapArtData art, GfxAssets gfx, bool animated = true, string? viewer = null,
                                 string? hiddenStack = null, MapObjectives? objectives = null, bool[,]? fog = null)
    {
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(gfx);
        var scene = new MapScene(looks, art, gfx, animated, viewer, fog) { _hidden = hiddenStack };
        scene.AddAll();
        if (objectives is not null) scene.AddObjectives(objectives);
        scene.Sprites = [.. scene._sprites.OrderBy(s => s.Key).ThenBy(s => s.Y).ThenBy(s => s.X)];
        return scene;
    }

    /// <summary>Якорь и сдвиг следа <c>sx×sy</c> с углом <c>(x, y)</c> (<c>0x5ac99c</c>, <c>0x5acb3d</c>).</summary>
    public static (int Ax, int Ay, int Dx) Anchor(int x, int y, int sx, int sy)
    {
        bool even = sx % 2 == 0;
        return (x + (sx / 2) - (even ? 1 : 0), y + (sy / 2), even ? 32 : 0);
    }

    /// <summary>Левый верхний угол картинки <c>W×H</c> (<c>0x545b22</c>; деление к нулю, как <c>W/(−2)</c>).</summary>
    public static (int X, int Y) Place(int ax, int ay, int width, int height, int dx = 0, int dy = 0) =>
        (((ax - ay) * 32) + 32 + (width / -2) + dx, ((ax + ay) * 16) + 16 + (height / -2) + dy);

    private void AddAll()
    {
        AddTrees();
        foreach (var r in _looks.Roads) AddRoad(r);
        foreach (var m in _looks.Mountains)
            Add("IsoTerrn", $"MOM{MountainLook.RaceCode(m.Race)}{m.Width:00}{m.Image:00}", "MOUNTAINS", m.X, m.Y, m.Width, m.Height, $"гора {m.Id}");
        foreach (var f in _looks.Forts) AddFort(f);
        foreach (var r in _looks.Ruins)
            Obj($"G000RU0000{(r.Looted ? 1 : 0)}{r.Image:00}", "RUINS", r.X, r.Y, 3, 3, r.Id);
        foreach (var s in _looks.Sites)
            Obj($"G000SI0000{s.Type}{s.Image:00}", "SITES", s.X, s.Y, 3, 3, s.Id);
        foreach (var c in _looks.Crystals)
            if (c.Resource >= 0 && c.Resource < CrystalCodes.Length)
                Obj($"G000CR0000{CrystalCodes[c.Resource]}", "CRYSTALS", c.X, c.Y, 1, 1, c.Id);
        foreach (var b in _looks.Bags)
            Obj($"G000BG0000{(IsWater(b.X, b.Y) ? 0 : 1)}{(b.Image < 0 ? "" : b.Image.ToString("00", CultureInfo.InvariantCulture))}", "BAGS", b.X, b.Y, 1, 1, b.Id);
        foreach (var t in _looks.Tombs)
            Obj($"G000TB0000{(IsWater(t.X, t.Y) ? "W" : "G")}", "TOMBS", t.X, t.Y, 1, 1, t.Id);
        foreach (var r in _looks.Rods)
            Obj($"{PlayerRace(r.Owner)}RROD8", "RODS", r.X, r.Y, 1, 1, r.Id);
        foreach (var l in _looks.Landmarks)
        {
            var (cx, cy) = _art.LandmarkFootprint(l.Type);
            Obj(l.Type, "LANDMARKS", l.X, l.Y, cx, cy, l.Id);
        }
        foreach (var s in _looks.Stacks) AddStack(s);
        AddFog();
    }

    /// <summary>Круг цели сценария (<c>docs/scenario-events.md</c> §8, <c>docs/map-render-stacks.md</c>): у объекта из набора целей зрителя
    /// рисовальщик его вида кладёт <c>OBJECTIVE{размер следа}</c> (IsoCmon, 320×320, <c>opacity</c> 300 — сложение) на слой
    /// <c>SYM_OBJECTIVE</c> (20) по следу объекта (<c>0x5bbad3</c> → <c>0x5c5a57</c>): столица 5, деревня 4, руина и площадки 3, отряд вне
    /// крепости, мешок, кристалл, жезл — 1; область — квадрат <c>2R+1</c> с центром в её клетке (ранг 3 — место области по разбору не
    /// сверено). Картинки есть для 1, 3, 4, 5 — прочих размеров нет, круга нет. Туманом прячется, как объект.</summary>
    private void AddObjectives(MapObjectives objectives)
    {
        var ids = new HashSet<string>(objectives.Ids, StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return;
        void Circle(string id, int x, int y, int size)
        {
            if (ids.Contains(id)) Obj($"OBJECTIVE{size}", "SYM_OBJECTIVE", x, y, size, size, $"цель {id}", quiet: true);
        }
        foreach (var f in _looks.Forts) Circle(f.Id, f.X, f.Y, f.Capital ? 5 : 4);
        foreach (var r in _looks.Ruins) Circle(r.Id, r.X, r.Y, 3);
        foreach (var s in _looks.Sites) Circle(s.Id, s.X, s.Y, 3);
        foreach (var c in _looks.Crystals) Circle(c.Id, c.X, c.Y, 1);
        foreach (var b in _looks.Bags) Circle(b.Id, b.X, b.Y, 1);
        foreach (var r in _looks.Rods) Circle(r.Id, r.X, r.Y, 1);
        foreach (var s in _looks.Stacks)
        {
            if (s.Inside.Length > 0 || (_hidden is { } hidden && s.Id.Equals(hidden, StringComparison.OrdinalIgnoreCase))) continue;
            if (s.Invisible && _viewer is not null && !s.Owner.Equals(_viewer, StringComparison.OrdinalIgnoreCase)) continue;
            Circle(s.Id, s.X, s.Y, 1);
        }
        foreach (var z in objectives.Zones.Values) Circle(z.Id, z.X - z.Radius, z.Y - z.Radius, (2 * z.Radius) + 1);
    }

    /// <summary>Туман: на скрытую клетку — <c>FOG{nn}{vv}</c> (слой <c>FOG</c>), у краевой — ещё рамка
    /// (<c>FOG_BORDER</c>) (<c>0x5ac5f3</c>). Вариант сплошного тумана у движка случаен — здесь от клетки.</summary>
    private void AddFog()
    {
        if (_fog is null) return;
        int n = _looks.Map.Size;
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            {
                if (!_fog[x, y]) continue;
                int variant = (int)((uint)((x * 73856093) ^ (y * 19349663)) % 5);
                Add("IsoTerrn", MapFog.Name(MapFog.Mask(Hidden, x, y, n), variant), "FOG", x, y, 1, 1, $"туман {x},{y}", fog: true);
                if (MapFog.Border(x, y, n) is { } border)
                    Add("IsoTerrn", border, "FOG_BORDER", x, y, 1, 1, $"рамка {x},{y}", fog: true);
            }
    }

    /// <summary>Лес: на клетку-лес одно дерево <c>{раса земли}F{A:02}{B:02}</c> (IsoTerrn), якорь — сама клетка
    /// (<c>0x5ac20d</c>).</summary>
    private void AddTrees()
    {
        var map = _looks.Map;
        for (int x = 0; x < map.Size; x++)
            for (int y = 0; y < map.Size; y++)
            {
                var tile = map.Tiles[x, y];
                if (tile.Ground != Ground.Forest || tile.Terrain is < 1 or > 6) continue;
                Add("IsoTerrn", $"{TerrainCodes[tile.Terrain]}F{_looks.TreeGroup[x, y]:00}{_looks.TreeImage[x, y]:00}",
                    "TREES", x, y, 1, 1, $"лес {x},{y}");
            }
    }

    /// <summary>Дорога: все картинки с префиксом <c>ROAD{INDEX:02}</c> длиной 6–8 по порядку, берётся <c>VAR % n</c>.</summary>
    private void AddRoad(RoadLook r)
    {
        string prefix = $"ROAD{r.Index:00}";
        var names = (_gfx.Archive("IsoTerrn")?.Images.Select(i => i.Name) ?? [])
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.Length is >= 6 and <= 8)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            _missing.Add(prefix);
            return;
        }
        Add("IsoTerrn", names[((r.Variant % names.Count) + names.Count) % names.Count], "ROADS", r.X, r.Y, 1, 1, r.Id);
    }

    private void AddFort(FortLook f)
    {
        int size = f.Capital ? 5 : 4;
        if (f.Capital)
        {
            var owner = _looks.Map.Players.GetValueOrDefault(f.Owner);
            string captured = owner is { CapturedBy.Length: > 0 } ? "C" : "";
            string capital = $"G000FT0000{Race(RaceIndex(PlayerRace(f.Owner)))}{captured}0";
            Obj(capital, "CAPITALS", f.X, f.Y, size, size, f.Id);
            // Тень — то же имя с «S» на слое STACKS_SHADOW (0x5c3120, 0x5c2ffb; ранг 1); в ванили теней столиц нет в архивах.
            Obj(capital + "S", "STACKS_SHADOW", f.X, f.Y, size, size, f.Id, quiet: true);
        }
        else
        {
            Obj($"G000FT0000NE{f.Level}", "VILLAGES", f.X, f.Y, size, size, f.Id);
            // Тень деревни (0x5c3215 → 0x5bbc37, ранг 1): в ванили есть только у уровня 2 — G000FT0000NE2S (смешение 128).
            Obj($"G000FT0000NE{f.Level}S", "STACKS_SHADOW", f.X, f.Y, size, size, f.Id, quiet: true);
            string race = PlayerRace(f.Owner);
            if (f.Owner.Length > 0 && !race.Equals(NeutralRace, StringComparison.OrdinalIgnoreCase))
                // Флага эльфов (G000RR0005FLAG8) в архивах нет: имени нет — флага нет, как у движка.
                Obj($"{race}FLAG8", "FLAGS", f.X, f.Y, size, size, f.Id, quiet: true);
            if (f.Riot) Obj("G000FT0000RIOT", "RIOTS", f.X, f.Y, size, size, f.Id);
        }
        // Щит: в крепости стоит отряд; раса — владельца отряда, у нейтралов общий щит (0x5bbb37).
        if (f.Garrison.Length > 0 && _looks.Map.Stacks.TryGetValue(f.Garrison, out var garrison))
        {
            string race = PlayerRace(garrison.Owner);
            string shield = race.Equals(NeutralRace, StringComparison.OrdinalIgnoreCase) ? "G000RR8888SHLV8"
                          : f.Capital ? $"{race}SHLC8" : $"{race}SHLV8";
            Obj(shield, "SHIELDS", f.X, f.Y, size, size, f.Id);
        }
    }

    /// <summary>Отряд (<c>0x5bbe1d</c>): в крепости или руинах не рисуется; на воде без умения плавать — лодка расы
    /// владельца; иначе лидер, стоя <c>STOP</c> (летун — <c>MOVE</c>), с тенью, вторым слоем и значком мёртвого лидера;
    /// знамя подрасы — отдельным слоем.</summary>
    /// <summary>Картинки одного отряда (тень, лидер, знамя) — для отряда в движении: их сцена двигает сама.</summary>
    public IReadOnlyList<MapSprite> StackSprites(StackLook stack, int facing, bool moving)
    {
        ArgumentNullException.ThrowIfNull(stack);
        int kept = _sprites.Count;
        AddStack(stack with { Facing = facing, Inside = "" }, moving);
        var own = _sprites.Skip(kept).OrderBy(m => m.Key).ThenBy(m => m.Y).ThenBy(m => m.X).ToList();
        _sprites.RemoveRange(kept, _sprites.Count - kept);
        return own;
    }

    private void AddStack(StackLook s, bool moving = false)
    {
        if (s.Inside.Length > 0) return;
        if (!moving && _hidden is { } hidden && s.Id.Equals(hidden, StringComparison.OrdinalIgnoreCase)) return;
        bool own = _viewer is null || s.Owner.Equals(_viewer, StringComparison.OrdinalIgnoreCase);
        if (s.Invisible && !own) return;
        int f = Math.Clamp(s.Facing, 0, 7);
        bool swims = _art.IsFlyer(s.LeaderType) || SwimsBonus(s.LeaderType);
        bool boat = IsWater(s.X, s.Y) && (!swims || (!s.LeaderAlive && s.AnyAlive));
        if (boat)
        {
            // Идущая лодка — BTMV/SBTM (0x5c43ae, 0x5c4725); нет картинки — стоящая BOAT/SBOA (рекурсивный вызов с флагом 0, 0x5c45ec).
            string race = PlayerRace(s.Owner);
            bool sailing = moving && Find(Unit(), $"{race}BTMV{f}") is not null;
            bool sailingShadow = moving && Find(Unit(), $"{race}SBTM{f}") is not null;
            Add(Unit(), $"{race}{(sailingShadow ? "SBTM" : "SBOA")}{f}", "STACKS_SHADOW", s.X, s.Y, 1, 1, s.Id, shadow: true, quiet: true);
            Add(Unit(), $"{race}{(sailing ? "BTMV" : "BOAT")}{f}", "STACKS", s.X, s.Y, 1, 1, s.Id);
        }
        else if (s.LeaderType.Length > 0)
        {
            bool flyer = _art.IsFlyer(s.LeaderType);
            string pose = flyer || moving ? "MOVE" : "STOP";
            // Тени есть не у всех (у Архимага G000UU0021 нет SSTO): имени нет — тени нет, как у движка.
            Add(Unit(), $"{s.LeaderType}S{pose[..3]}{f}", "STACKS_SHADOW", s.X, s.Y, 1, 1, s.Id, shadow: true, quiet: true);
            Add(Unit(), $"{s.LeaderType}{pose}{f}", "STACKS", s.X, s.Y, 1, 1, s.Id);
            Add(Unit(), $"{s.LeaderType}{pose[..3]}2{f}", "STACKS", s.X, s.Y, 1, 1, s.Id, quiet: true);
        }
        if (!s.LeaderAlive) Add(Unit(), "ICNDEADL", "STACKS", s.X, s.Y, 1, 1, s.Id);
        if (s.Invisible) Obj("INVISIBLE", "SYM_INVISIBLE", s.X, s.Y, 1, 1, s.Id);
        if (_looks.Banners.TryGetValue(s.Subrace, out int banner) && banner >= 0)
            Obj($"STACK_BANNER_{banner:00}00", "BANNERS", s.X, s.Y, 1, 1, s.Id);
    }

    private static string Unit() => "IsoUnit";

    private bool SwimsBonus(string unit) =>
        _art.MoveBonuses.FirstOrDefault(b => b.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase)) is { } b && b.Grounds.Contains(3);

    /// <summary>Объект из IsoCmon, затем IsoAnim или IsoStill (<c>0x5c723b</c>).</summary>
    private void Obj(string name, string layer, int x, int y, int sx, int sy, string source, bool quiet = false)
    {
        var art = Find("IsoCmon", name) ?? Find(_animated ? "IsoAnim" : "IsoStill", name);
        if (art is null)
        {
            if (!quiet) _missing.Add(name);
            return;
        }
        Put(art.Value, layer, x, y, sx, sy, source, false);
    }

    private void Add(string archive, string name, string layer, int x, int y, int sx, int sy, string source,
                     bool shadow = false, bool quiet = false, bool fog = false)
    {
        if (Find(archive, name) is { } art) Put(art, layer, x, y, sx, sy, source, shadow, fog);
        else if (!quiet) _missing.Add(name);
    }

    /// <param name="fog">Картинка самого тумана: туманом не прячется.</param>
    /// <summary>Метки поверх карты: эллипс выбранного отряда (<c>SELECTED</c>, слой <c>SYM_STACK_SEL</c>) и флажки
    /// пути (<c>MOVE*</c>, слой <c>SYM_MOVE_PATH</c>, кадр анимации — номер клетки в пути). Правила выбора флажка —
    /// <c>docs/map-path.md</c> §3: до последней достижимой клетки синий, дальше красный, если в конце ждёт действие,
    /// иначе белый; на самой последней достижимой при действии — «бой» или «переговоры».</summary>
    public IReadOnlyList<MapSprite> Marks((int X, int Y)? selected, IReadOnlyList<(int X, int Y)> path,
                                           int reachable, bool actionAtEnd, bool noble = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        var kept = _sprites.Count;
        if (selected is { } cell) Add("IsoCmon", "SELECTED", "SYM_STACK_SEL", cell.X, cell.Y, 1, 1, "выбор");
        bool today = reachable > 0;
        for (int i = 1; i < path.Count; i++)
        {
            string name = today ? "MOVENORMAL" : actionAtEnd ? "MOVEOUT" : "MOVEACTION";
            if (i == reachable)
            {
                today = false;
                if (actionAtEnd) name = noble ? "MOVENEGO" : "MOVEBATTLE";
            }
            Add("IsoCmon", name, "SYM_MOVE_PATH", path[i].X, path[i].Y, 1, 1, $"путь {i}");
        }
        var marks = _sprites.Skip(kept).OrderBy(m => m.Key).ThenBy(m => m.Y).ThenBy(m => m.X).ToList();
        _sprites.RemoveRange(kept, _sprites.Count - kept);
        return marks;
    }

    /// <summary>Подсветка клетки под курсором (<c>TILE_HIGHLIGHT</c>, IsoCmon 480×480, 31 кадр; слой <c>TILE_HIGHLIGHT</c> 320 — под отрядами;
    /// <c>0x4d4bc9</c>): над любой клеткой карты (решение пользователя 28.09; у движка — только над отрядом, мешком, жезлом, гробницей,
    /// локацией и крепостью). Вне карты или нет картинки — null.</summary>
    public MapSprite? Highlight(int x, int y)
    {
        if (!_looks.Map.Contains(x, y)) return null;
        var kept = _sprites.Count;
        Add("IsoCmon", "TILE_HIGHLIGHT", "TILE_HIGHLIGHT", x, y, 1, 1, "подсветка", fog: true);
        if (_sprites.Count == kept) return null;
        var sprite = _sprites[^1];
        _sprites.RemoveAt(_sprites.Count - 1);
        return sprite;
    }

    private void Put(ArtRef art, string layer, int x, int y, int sx, int sy, string source, bool shadow, bool fog = false)
    {
        var (ax, ay, dx) = Anchor(x, y, sx, sy);
        if (!fog && _fog is not null && MapFog.Hides(Hidden, ax, ay, sx, sy, art.Height, _looks.Map.Size)) return;
        var (px, py) = Place(ax, ay, art.Width, art.Height, dx);
        _sprites.Add(new MapSprite(art.Archive, art.Name, art.Anim, art.Frames, px, py, art.Width, art.Height,
                                   _art.Layer(layer), ax, ay, source, shadow));
    }

    private ArtRef? Find(string archive, string name)
    {
        if (_gfx.Image(archive, name) is { } i) return new ArtRef(archive, i.Name, false, 1, i.W, i.H);
        if (_gfx.Anim(archive, name) is { } a) return new ArtRef(archive, a.Name, true, a.Frames.Count, a.W, a.H);
        return null;
    }

    private bool IsWater(int x, int y) => _looks.Map.Contains(x, y) && _looks.Map.Tiles[x, y].Ground == Ground.Water;

    /// <summary><c>RACE_ID</c> игрока; нет игрока — нейтралы.</summary>
    private string PlayerRace(string player) =>
        _looks.Map.Players.TryGetValue(player, out var p) && p.RaceId.Length > 0 ? p.RaceId.ToUpperInvariant() : NeutralRace;

    private static int RaceIndex(string raceId) =>
        raceId.Length == 10 && int.TryParse(raceId[6..], out int n) ? n : 4;

    private static string Race(int index) => index >= 0 && index < RaceCodes.Length ? RaceCodes[index] : "NE";
}
