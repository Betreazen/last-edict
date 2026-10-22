using LastEdict.Rules.Map;

namespace LastEdict.Data.Gfx;

/// <summary>Тип земли клетки для отрисовки — порядок создания узоров в движке (<c>TileIndices</c> <c>0x5ab45c</c>),
/// он же старшинство границ: узор старшего типа наползает на младший (<c>docs/ground-render.md</c>).</summary>
public enum GroundType : byte { Black = 0, Water = 1, Neutral = 2, Heretic = 3, Undead = 4, Human = 5, Elf = 6, Dwarf = 7 }

/// <summary>Картинка в памяти: RGBA по 4 байта на точку, строками сверху вниз.</summary>
public sealed class RgbaImage(int width, int height, byte[]? pixels = null)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels ?? new byte[width * height * 4];

    public int Offset(int x, int y) => ((y * Width) + x) * 4;
}

/// <summary>Графика земли, которую сборщику даёт вызывающий (сцена грузит картинки из <c>assets/gfx</c>): узоры
/// <c>Ground XX_NN</c> (192×192), маски границ <c>GrBorder NE_mm_vv</c> (64×32, серые: вес — красный канал), спрайты
/// берега <c>GrBorder WA_mm_vv</c> (64×32, прозрачное — альфа 0) и фон воды <c>Ground WA_00</c>.</summary>
public interface IGroundArt
{
    int PatternVariants(GroundType type);

    RgbaImage Pattern(GroundType type, int variant);

    int MaskVariants(int mask);

    RgbaImage? Mask(int mask, int variant);

    int ShoreVariants(int mask);

    RgbaImage? Shore(int mask, int variant);

    RgbaImage? WaterBackground { get; }
}

/// <summary>Геометрия изометрии и выбор типа земли — всё из разбора <c>Discipl2.exe</c>
/// (<c>docs/ground-render.md</c>).</summary>
public static class GroundGeometry
{
    public const int CellWidth = 64, CellHeight = 32, PatternSize = 192;

    /// <summary>Левый верхний угол прямоугольника клетки в мировых точках (<c>0x545a32</c>).</summary>
    public static (int X, int Y) CellOrigin(int x, int y) => ((x - y) * 32, (x + y) * 16);

    /// <summary>Клетка под мировой точкой: чей ромб по таблице строк (<see cref="Row"/>) содержит точку. Кандидат —
    /// ближайший центр в метрике ромба, затем проверяются он и четыре соседа. Точку, не попавшую ни в один ромб
    /// (щель в две точки на концах строк 15–16), берёт ближайший центр. Как движок переводит курсор в клетку, не
    /// разобрано. Клетка может лежать вне карты — проверяет вызывающий.</summary>
    public static (int X, int Y) CellAt(double worldX, double worldY)
    {
        // Центр клетки (x, y) — (32·(x−y) + 32, 16·(x+y) + 16).
        double u = (worldX - 32) / 64.0, v = (worldY - 16) / 32.0;
        (int X, int Y) near = ((int)Math.Round(u + v), (int)Math.Round(v - u));
        int px = (int)Math.Floor(worldX), py = (int)Math.Floor(worldY);
        foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var (cx, cy) = CellOrigin(near.X + dx, near.Y + dy);
            int r = py - cy;
            if (r < 0 || r >= CellHeight) continue;
            var (x0, width) = Row(r);
            if (px - cx >= x0 && px - cx < x0 + width) return (near.X + dx, near.Y + dy);
        }
        return near;
    }

    /// <summary>Строка ромба (<c>0x6ecd08</c>): начало и ширина. До середины ромб расширяется на 4 точки за строку,
    /// строки 15 и 16 — полные (ширина 62), дальше симметрично сужается.</summary>
    public static (int X0, int Width) Row(int row)
    {
        int r = row < 16 ? row : 31 - row;
        return (30 - (2 * r), 2 + (4 * r));
    }

    /// <summary>Тип земли клетки (<c>getTileDataIndex</c> <c>0x5ac471</c>): вода → вода, горы → нейтральная,
    /// равнина и лес — по расе земли (<c>Lterrain</c> 1..6: HU, DW, HE, UN, NE, EL). Неизвестное — нейтральная.</summary>
    public static GroundType TypeOf(Tile tile) => tile.Ground switch
    {
        Ground.Water => GroundType.Water,
        Ground.Mountain => GroundType.Neutral,
        _ => tile.Terrain switch
        {
            1 => GroundType.Human, 2 => GroundType.Dwarf, 3 => GroundType.Heretic, 4 => GroundType.Undead,
            6 => GroundType.Elf, _ => GroundType.Neutral,
        },
    };

    /// <summary>Префикс имён узоров <c>Ground.ff</c> по типу.</summary>
    public static string Prefix(GroundType type) => type switch
    {
        GroundType.Black => "BL", GroundType.Water => "WA", GroundType.Neutral => "NE", GroundType.Heretic => "HE",
        GroundType.Undead => "UN", GroundType.Human => "HU", GroundType.Elf => "EL", _ => "DW",
    };
}

/// <summary>Таблица вариантов узора (<c>CMidIsoGroundIndexer</c>): 48×24 байт <c>rand() % 10</c> из MSVCRT после
/// <c>srand(MAP_SEED)</c>; вариант один на блок 192×192 мира.</summary>
public sealed class GroundVariants
{
    private const int Columns = 48, Rows = 24;
    private readonly byte[] _table = new byte[Columns * Rows];

    public GroundVariants(uint mapSeed)
    {
        uint state = mapSeed; // srand(1) и сразу srand(MAP_SEED): остаётся только второй
        for (int i = 0; i < _table.Length; i++) _table[i] = (byte)(Rand(ref state) % 10);
    }

    /// <summary><c>rand()</c> MSVCRT: <c>s = s·214013 + 2531011</c>, результат <c>(s &gt;&gt; 16) &amp; 0x7fff</c>.</summary>
    public static int Rand(ref uint state)
    {
        state = unchecked((state * 214013) + 2531011);
        return (int)((state >> 16) & 0x7fff);
    }

    /// <summary>Байт таблицы для точки (<c>getData</c> <c>0x5ae7b5</c>), координаты неотрицательны.</summary>
    public int At(int x, int y) =>
        _table[(Columns * ((y / GroundGeometry.PatternSize + Rows) % Rows)) + ((x / GroundGeometry.PatternSize + Columns) % Columns)];
}
