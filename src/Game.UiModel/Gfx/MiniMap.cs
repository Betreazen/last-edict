using LastEdict.Data.Map;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Gfx;

/// <summary>Мини-карта (<c>docs/map-screen.md</c> §1, разбор <c>CPalMapInterf</c>): растр <c>4N × 2N</c>, клетка
/// <c>(x, y)</c> ложится в точку <c>(2(x − y) + W/2, x + y)</c> блоком 4×2 по маске движка, а цвет точки берётся из
/// картинки-узора <c>ISO*</c> по тому же адресу растра по модулю размера узора — то есть узор привязан к растру, а
/// не к клетке. Три прохода: земля и вода, значки, туман.
///
/// <para>Чего нет: продления тумана на 5 клеток за край карты и рамки текущего вида — их рисует сцена.</para></summary>
public sealed class MiniMap
{
    /// <summary>Маска блока 4×2 (<c>0x6f3fb0</c>, 1 — пропустить): рисуются <c>(1,0)</c>, <c>(0,1)</c>, <c>(1,1)</c>,
    /// <c>(2,1)</c>.</summary>
    private static readonly (int Dx, int Dy)[] Block = [(1, 0), (0, 1), (1, 1), (2, 1)];

    /// <summary>Узор по расе земли (<c>Lterrain</c> 1..6).</summary>
    private static readonly string[] TerrainPattern = ["ISONE", "ISOHU", "ISODW", "ISOHE", "ISOUN", "ISONE", "ISOEL"];

    /// <summary>Код расы в именах значков — по номеру расы в <c>Grace</c> (<c>G000RR000n</c>).</summary>
    private static readonly string[] RaceIcon = ["HU", "DW", "HE", "UN", "NE", "EL"];

    private static readonly string[] CrystalIcon =
        ["CRYSGOLD", "CRYSRED", "CRYSYELLOW", "CRYSORANGE", "CRYSWHITE", "CRYSBLUE"];

    private readonly MapLooks _looks;
    private readonly Func<string, RgbaImage?> _art;
    private readonly Func<int, int, bool>? _hidden;

    /// <summary>Код расы для значков (<c>CITY{код}</c>, <c>CAP…</c>, <c>STCK…</c>) по id расы — вид расы (<c>RaceLook.Code</c>, Э5: у расы мода
    /// свои значки); не задан — по номеру расы оригинала в id.</summary>
    public Func<string, string>? RaceCode { get; init; }

    public MiniMap(MapLooks looks, Func<string, RgbaImage?> art, Func<int, int, bool>? hidden = null)
    {
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(art);
        _looks = looks;
        _art = art;
        _hidden = hidden;
        Size = looks.Map.Size;
    }

    public int Size { get; }

    public int Width => 4 * Size;

    public int Height => 2 * Size;

    /// <summary>Точка растра для клетки (<c>0x67d66e</c> плюс центрирование <c>W/2</c>).</summary>
    public (int X, int Y) PointOf(int x, int y) => ((2 * (x - y)) + (Width / 2), x + y);

    /// <summary>Клетка под точкой растра — обратное преобразование <c>PointOf</c>.</summary>
    public (int X, int Y) CellAt(int px, int py)
    {
        int half = (px - (Width / 2)) / 2;   // px − W/2 = 2(x − y), py = x + y
        return ((py + half) / 2, (py - half) / 2);
    }

    /// <summary>Готовый растр мини-карты.</summary>
    public RgbaImage Render()
    {
        var image = new RgbaImage(Width, Height);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                Land(image, x, y);
        foreach (var (name, x, y) in Icons()) Icon(image, name, x, y);
        if (_hidden is not null)
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (_hidden(x, y)) Fill(image, x, y, "ISOFO");
        return image;
    }

    /// <summary>Значки по слоям (<c>0x6edc10</c>): горы 0, города и руины 1, столицы 2, кристаллы 3, отряды 4.</summary>
    private IEnumerable<(string Name, int X, int Y)> Icons()
    {
        foreach (var m in _looks.Mountains)
            yield return ($"MOUNTNE{Math.Clamp(m.Width, 1, 5)}", m.X, m.Y);
        foreach (var f in _looks.Forts.Where(f => !f.Capital))
            yield return ($"CITY{Race(f.Owner)}", f.X, f.Y);
        foreach (var r in _looks.Ruins)
            yield return ("RUIN", r.X, r.Y);
        foreach (var f in _looks.Forts.Where(f => f.Capital))
            yield return ($"CAP{Race(f.Owner)}", f.X, f.Y);
        foreach (var c in _looks.Crystals.Where(c => c.Resource >= 0 && c.Resource < CrystalIcon.Length))
            yield return (CrystalIcon[c.Resource], c.X, c.Y);
        foreach (var s in _looks.Stacks.Where(s => s.Inside.Length == 0))
            yield return ($"STCK{Race(s.Owner)}", s.X, s.Y);
    }

    /// <summary>Земля клетки: вода, кромка воды, горы или узор расы. Скрытая клетка земли не рисует, воду рисует
    /// (<c>0x67ef78</c>).</summary>
    private void Land(RgbaImage image, int x, int y)
    {
        var tile = _looks.Map.Tiles[x, y];
        bool hidden = _hidden?.Invoke(x, y) ?? false;
        if (tile.Ground == LastEdict.Rules.Map.Ground.Water)
        {
            Fill(image, x, y, "ISOWA");
            return;
        }
        if (hidden) return;
        Fill(image, x, y, tile.Ground == LastEdict.Rules.Map.Ground.Mountain ? "ISOMO" : Shore(x, y) ? "ISOWB" : Pattern(tile));
    }

    /// <summary>Кромка воды (<c>0x67e225</c>): под клеткой — вода по диагонали вниз.</summary>
    private bool Shore(int x, int y) =>
        Water(x + 1, y + 1) && Water(x + 2, y + 2);

    private bool Water(int x, int y) =>
        _looks.Map.Contains(x, y) && _looks.Map.Tiles[x, y].Ground == LastEdict.Rules.Map.Ground.Water;

    private static string Pattern(Tile tile) =>
        TerrainPattern[tile.Terrain >= 0 && tile.Terrain < TerrainPattern.Length ? tile.Terrain : 0];

    private void Fill(RgbaImage image, int x, int y, string pattern)
    {
        if (_art(pattern) is not { } art || art.Width == 0 || art.Height == 0) return;
        var (px, py) = PointOf(x, y);
        foreach (var (dx, dy) in Block)
        {
            int ix = px + dx, iy = py + dy;
            if (ix < 0 || iy < 0 || ix >= image.Width || iy >= image.Height) continue;
            int so = art.Offset(Mod(ix, art.Width), Mod(iy, art.Height));
            int o = image.Offset(ix, iy);
            image.Pixels[o] = art.Pixels[so];
            image.Pixels[o + 1] = art.Pixels[so + 1];
            image.Pixels[o + 2] = art.Pixels[so + 2];
            image.Pixels[o + 3] = 255;
        }
    }

    /// <summary>Значок кладётся центром в точку клетки (+1 по X, <c>0x67f2a9</c>).</summary>
    private void Icon(RgbaImage image, string name, int x, int y)
    {
        if (_art(name) is not { } icon) return;
        var (px, py) = PointOf(x, y);
        int left = px + 1 - (icon.Width / 2), top = py - (icon.Height / 2);
        for (int r = 0; r < icon.Height; r++)
            for (int c = 0; c < icon.Width; c++)
            {
                int ix = left + c, iy = top + r;
                if (ix < 0 || iy < 0 || ix >= image.Width || iy >= image.Height) continue;
                int so = icon.Offset(c, r);
                if (icon.Pixels[so + 3] == 0) continue;
                int o = image.Offset(ix, iy);
                Array.Copy(icon.Pixels, so, image.Pixels, o, 4);
            }
    }

    private string Race(string player)
    {
        if (!_looks.Map.Players.TryGetValue(player, out var p)) return "NE";
        if (RaceCode is { } code) return code(p.RaceId);
        string race = Ui.ArtNames.Of(p.RaceId);   // номер расы — по id оригинала
        if (race.Length != 10) return "NE";
        return int.TryParse(race[6..], out int n) && n >= 0 && n < RaceIcon.Length ? RaceIcon[n] : "NE";
    }

    private static int Mod(int value, int size) => ((value % size) + size) % size;
}
