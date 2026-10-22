using LastEdict.Data.Map;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Перекладка гор после <c>L_REMOVE_MOUNTAIN</c> (<c>0x5f0206</c> → <c>0x5efb81</c>, ранг 1; <c>docs/scenario-events.md</c> §10).</summary>
public sealed partial class GameSession
{
    /// <summary>Размеры квадратов гор по проходам (таблица <c>0x838978</c>, заполняет <c>0x5ef57f</c>): крупные первыми.</summary>
    private static readonly int[] MountainSizes = [5, 3, 2, 1];

    /// <summary>Клетка сетки перекладки закрыта: не горы или накрыта оставшимся объектом гор (<c>0x5effa8</c>).</summary>
    private const int Closed = 0xFF;

    /// <summary>Буквы местности в сетке (<c>0x5f006d</c>): годные — <c>d…h</c> (<c>0x5efdb0</c>), эльфийская <c>i</c> — нет.</summary>
    private const byte LetterHuman = (byte)'d', LetterDwarf = (byte)'e', LetterHeretic = (byte)'f', LetterUndead = (byte)'g',
                       LetterNeutral = (byte)'h', LetterElf = (byte)'i';

    /// <summary>Следующий id объекта гор (<c>[mountains+0x24]</c>): при загрузке — наибольший id + 1 (<c>0x5ef77f</c>), дальше только
    /// растёт — удаление его не уменьшает. В сохранение не пишется: движок тоже заново считает его при загрузке.</summary>
    private int? _nextMountainId;

    /// <summary>
    /// После удаления объектов гор <paramref name="removed"/> (задели зону): горные клетки их общего прямоугольника (<c>0x5f0296</c>, правые
    /// границы исключены), не накрытые оставшимися объектами и не эльфийские, заново покрываются объектами — квадраты 5, 3, 2, 1 жадно
    /// (y снаружи, x внутри). Раса квадрата — самая частая буква местности (при равенстве — первая из <c>d, e, f, g, h</c>, <c>0x5efe12</c>),
    /// картинка — бросок R250 по числу картинок местности и размера (<see cref="MapArtData.MountainImageCount"/>, <c>0x54851f</c>), id —
    /// по порядку постановки. Земля клеток не меняется.
    /// </summary>
    private IReadOnlyList<MountainLook> RelayMountains(IReadOnlyList<MountainLook> removed, IReadOnlyList<MountainLook> kept, Tile[,] tiles)
    {
        if (removed.Count == 0) return [];
        int x0 = removed.Min(m => m.X), y0 = removed.Min(m => m.Y);
        int x1 = removed.Max(m => m.X + m.Width), y1 = removed.Max(m => m.Y + m.Height);
        int w = x1 - x0, h = y1 - y0;
        var grid = new int[w, h];
        for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
                grid[x - x0, y - y0] = !InMap(x, y) || tiles[x, y].Ground != Ground.Mountain || kept.Any(m => Covers(m, x, y))
                    ? Closed : Letter(tiles[x, y].Terrain);
        List<MountainLook> placed = [];
        foreach (int size in MountainSizes)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cell = grid[x, y];
                    if (!Fits(cell))
                    {
                        // 0x5efb81: негодная клетка с поставленным квадратом (в сетке — его размер) — прыжок через квадрат; у эльфийской
                        // буквы прыжок на её код (105) — остаток строки в этом проходе пропускается.
                        if (cell != Closed) x += cell - 1;
                        continue;
                    }
                    if (size > 1 && !Square(grid, x, y, size, w, h)) continue;
                    int race = SquareRace(grid, x, y, size);
                    int image = Rng.Next(Art.MountainImageCount(MountainLook.RaceCode(race), size));
                    int id = _nextMountainId ?? 0;
                    _nextMountainId = id + 1;
                    placed.Add(new MountainLook(id, x0 + x, y0 + y, size, size, image, race));
                    for (int dx = 0; dx < size; dx++)
                        for (int dy = 0; dy < size; dy++) grid[x + dx, y + dy] = size;
                    x += size - 1;
                }
        return placed;

        bool InMap(int x, int y) => x >= 0 && y >= 0 && x < tiles.GetLength(0) && y < tiles.GetLength(1);
    }

    private static bool Covers(MountainLook m, int x, int y) => x >= m.X && x < m.X + m.Width && y >= m.Y && y < m.Y + m.Height;

    private static bool Fits(int cell) => cell is >= LetterHuman and <= LetterNeutral;

    /// <summary>Квадрат <paramref name="size"/>×<paramref name="size"/> с угла (x, y) целиком в прямоугольнике и из годных клеток
    /// (<c>0x5efe96</c>).</summary>
    private static bool Square(int[,] grid, int x, int y, int size, int w, int h)
    {
        if (x + size > w || y + size > h) return false;
        for (int dx = 0; dx < size; dx++)
            for (int dy = 0; dy < size; dy++)
                if (!Fits(grid[x + dx, y + dy])) return false;
        return true;
    }

    /// <summary>Буква местности клетки (<c>0x5f006d</c>; <see cref="Tile.Terrain"/>: 1 HU, 2 DW, 3 HE, 4 UN, 5 NE, 6 EL).</summary>
    private static int Letter(int terrain) => terrain switch
    {
        1 => LetterHuman,
        2 => LetterDwarf,
        3 => LetterHeretic,
        4 => LetterUndead,
        6 => LetterElf,
        _ => LetterNeutral,
    };

    /// <summary>Раса квадрата (<c>0x5efe12</c> → <c>0x5efdd1</c>): самая частая буква, при равенстве — раньше по алфавиту; раса —
    /// id <c>LRace</c> (0 HU, 1 UN, 2 HE, 3 DW, 4 NE).</summary>
    private static int SquareRace(int[,] grid, int x, int y, int size)
    {
        var counts = new int[LetterNeutral - LetterHuman + 1];
        for (int dx = 0; dx < size; dx++)
            for (int dy = 0; dy < size; dy++) counts[grid[x + dx, y + dy] - LetterHuman]++;
        int best = 0;
        for (int i = 1; i < counts.Length; i++)
            if (counts[i] > counts[best]) best = i;
        return (best + LetterHuman) switch
        {
            LetterHuman => 0,
            LetterUndead => 1,
            LetterHeretic => 2,
            LetterDwarf => 3,
            _ => LegacyRaces.Neutral,
        };
    }
}
