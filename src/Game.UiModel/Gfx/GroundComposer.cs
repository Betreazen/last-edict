namespace LastEdict.Data.Gfx;

/// <summary>Граница, которая ложится на клетку: узор соседа старшего типа через маску. <see cref="Main"/> — маска
/// рёбер (1..15), <see cref="Corners"/> — угловая (17..31), 0 — нет.</summary>
public readonly record struct GroundBorder(GroundType Type, int Main, int Corners);

/// <summary>Земля карты, собранная так, как это делает движок (<c>CGroundTexture::draw</c> <c>0x54361d</c>, разбор —
/// <c>docs/ground-render.md</c>): ромб клетки 64×32, узор 192×192 привязан к миру, вариант узора — по блоку 192×192 из
/// таблицы <see cref="GroundVariants"/>, на клетку накладываются узоры соседей старшего типа через маски, у воды —
/// спрайты берега поверх фона. Сетки поверхностей 128×128 здесь нет: это кэш вида движка, а содержимое каждой точки
/// зависит только от мира.
///
/// <para>Отступления от оригинала, осознанные: смешение по маске — в 8 битах на канал, а не в RGB565 (разница — на
/// младший разряд); вариант маски (<c>_vv</c>) — детерминированный от клетки и места, а не R250 при создании вида
/// (тот не восстановить: зависит от всего, что брало числа до него, и в <c>.sg</c> не пишется); фон воды — с
/// периодом 128 по миру (в движке он привязан к сетке вида).</para>
///
/// <para>Анимированная вода (<see cref="AnimatedWater"/>, <c>0x54361d</c> п. 1): точки воды остаются прозрачными (у движка —
/// заливка мадженты), под землю кладётся анимированный фон <c>WATERBKGND</c> (рисует вид); спрайт берега ложится как обычно, а
/// узор суши по маске на прозрачную точку не смешивается (<c>0x678ba7</c>: «dst — маджента → пропустить») — суша над водой
/// только там, где уже лёг берег.</para></summary>
public sealed class GroundComposer
{
    /// <summary>Соседи в порядке движка (<c>0x6e1f58</c>): 0 (−1,−1), 1 (0,−1), 2 (+1,−1), 3 (+1,0), 4 (+1,+1),
    /// 5 (0,+1), 6 (−1,+1), 7 (−1,0).</summary>
    private static readonly (int Dx, int Dy)[] Neighbours =
        [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)];

    /// <summary>Рёбра — биты маски рёбер: бит0 (−1,0), бит1 (0,−1), бит2 (+1,0), бит3 (0,+1).</summary>
    private static readonly int[] Edges = [7, 1, 3, 5];

    /// <summary>Углы — биты угловой маски, каждый со своими двумя рёбрами: бит0 (−1,−1), бит1 (+1,−1), бит2 (+1,+1),
    /// бит3 (−1,+1).</summary>
    private static readonly (int Corner, int EdgeA, int EdgeB)[] Corners = [(0, 7, 1), (2, 1, 3), (4, 3, 5), (6, 5, 7)];

    private const int WaterPeriod = 128;

    private readonly GroundType[,] _cells;
    private readonly GroundVariants _variants;
    private readonly IGroundArt _art;

    public GroundComposer(GroundType[,] cells, uint mapSeed, IGroundArt art)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(art);
        if (cells.GetLength(0) != cells.GetLength(1)) throw new ArgumentException("карта не квадратная", nameof(cells));
        _cells = cells;
        _variants = new GroundVariants(mapSeed);
        _art = art;
        _masks = new MaskArt(art.MaskVariants, art.Mask);
        _shores = new MaskArt(art.ShoreVariants, art.Shore);
        _water = new Lazy<RgbaImage?>(() => art.WaterBackground, LazyThreadSafetyMode.PublicationOnly);
    }

    public int Size => _cells.GetLength(0);

    /// <summary>Вода анимирована: её точки прозрачны, фон рисует вид (без анимации — неподвижный фон <c>WA_00</c>).</summary>
    public bool AnimatedWater { get; init; }

    public GroundType TypeAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Size && y < Size ? _cells[x, y] : GroundType.Black;

    /// <summary>Границы клетки (<c>0x542f39</c>): по каждому типу соседа старше своего — маска рёбер и угловая,
    /// по возрастанию типа. У края карты границ нет.</summary>
    public IReadOnlyList<GroundBorder> Borders(int x, int y)
    {
        Span<int> near = stackalloc int[Neighbours.Length];
        Near(x, y, near);
        Span<GroundBorder> borders = stackalloc GroundBorder[Neighbours.Length];
        return borders[..Borders(near, (int)TypeAt(x, y), borders)].ToArray();
    }

    /// <summary>Маски берега у клетки воды: флаги — любая суша вокруг (тип старше воды).</summary>
    public (int Main, int Corners) Shore(int x, int y)
    {
        Span<int> near = stackalloc int[Neighbours.Length];
        Near(x, y, near);
        return Shore(near, TypeAt(x, y));
    }

    /// <summary>Типы восьми соседей в порядке <see cref="Neighbours"/>, за картой — −1.</summary>
    private void Near(int x, int y, Span<int> near)
    {
        for (int i = 0; i < Neighbours.Length; i++)
        {
            int nx = x + Neighbours[i].Dx, ny = y + Neighbours[i].Dy;
            near[i] = Inside(nx, ny) ? (int)_cells[nx, ny] : -1;
        }
    }

    /// <summary>Границы клетки типа <paramref name="own"/> в <paramref name="into"/> (не больше восьми — по типу на соседа), по
    /// возрастанию типа; возвращает их число. Без LINQ и выделений: зовётся на каждую клетку (профиль 14.10: ~5 % сборки).</summary>
    private static int Borders(ReadOnlySpan<int> near, int own, Span<GroundBorder> into)
    {
        int count = 0;
        for (int type = Next(near, own); type != int.MaxValue; type = Next(near, type))
        {
            int raised = 0;
            for (int i = 0; i < near.Length; i++)
                if (near[i] == type) raised |= 1 << i;
            var (main, corners) = Masks(raised);
            if (main != 0 || corners != 0) into[count++] = new GroundBorder((GroundType)type, main, corners);
        }
        return count;

        // Наименьший тип соседа старше after; нет такого — int.MaxValue.
        static int Next(ReadOnlySpan<int> near, int after)
        {
            int next = int.MaxValue;
            foreach (int t in near)
                if (t > after && t < next) next = t;
            return next;
        }
    }

    /// <summary>Маски берега по соседям. У клетки воды граница есть ровно тогда, когда вокруг есть суша: сосед старшего типа даёт
    /// либо бит ребра, либо бит угла (если оба его ребра не того же типа), — так что отдельной проверки «есть ли границы» не нужно.</summary>
    private static (int Main, int Corners) Shore(ReadOnlySpan<int> near, GroundType own)
    {
        if (own != GroundType.Water) return (0, 0);
        int raised = 0;
        for (int i = 0; i < near.Length; i++)
            if (near[i] > (int)GroundType.Water) raised |= 1 << i;
        return Masks(raised);
    }

    /// <summary>Маски по флагам соседей: бит i <paramref name="raised"/> — сосед i (<see cref="Neighbours"/>).</summary>
    private static (int Main, int Corners) Masks(int raised)
    {
        int main = 0, corners = 0;
        for (int bit = 0; bit < Edges.Length; bit++)
            if (Raised(Edges[bit])) main |= 1 << bit;
        for (int bit = 0; bit < Corners.Length; bit++)
        {
            var (corner, a, b) = Corners[bit];
            if (Raised(corner) && !Raised(a) && !Raised(b)) corners |= 1 << bit;
        }
        return (main, corners == 0 ? 0 : 16 + corners);

        bool Raised(int neighbour) => ((raised >> neighbour) & 1) != 0;
    }

    /// <summary>Мировой прямоугольник <c>(left, top, width, height)</c> в картинку: все клетки, чьи ромбы его задевают
    /// (и клетки за картой — чёрным узором).</summary>
    public RgbaImage Render(int left, int top, int width, int height)
    {
        var image = new RgbaImage(width, height);
        // Клетки, задевающие прямоугольник: ромб (x,y) лежит в [(x−y)·32, +64) × [(x+y)·16, +32).
        int sMin = (int)Math.Floor(left / 32.0) - 2, sMax = (int)Math.Ceiling((left + width) / 32.0) + 1;
        int tMin = (int)Math.Floor(top / 16.0) - 2, tMax = (int)Math.Ceiling((top + height) / 16.0) + 1;
        for (int t = tMin; t <= tMax; t++)
            for (int s = sMin; s <= sMax; s++)
            {
                if (((s + t) & 1) != 0) continue; // x−y и x+y одной чётности
                int x = (s + t) / 2, y = (t - s) / 2;
                DrawCell(image, x, y, left, top);
            }
        return image;
    }

    /// <summary>Всё, от чего зависит картинка прямоугольника (<see cref="Render"/>): тип и чёрный ромб каждой клетки, задевающей его,
    /// с полем в клетку — границы и берег смотрят на соседей. Прочее (сид, графика, вода) у сборщика постоянно. Одинаковые входы —
    /// одинаковая картинка, поэтому кусок с теми же входами пересобирать не нужно (<see cref="GroundChunks.Refresh"/>).</summary>
    public byte[] Inputs(int left, int top, int width, int height)
    {
        const int margin = 2;   // сосед по x или y сдвигает s и t на 1, по диагонали — одну из них на 2
        int sMin = (int)Math.Floor(left / 32.0) - 2 - margin, sMax = (int)Math.Ceiling((left + width) / 32.0) + 1 + margin;
        int tMin = (int)Math.Floor(top / 16.0) - 2 - margin, tMax = (int)Math.Ceiling((top + height) / 16.0) + 1 + margin;
        var inputs = new List<byte>((sMax - sMin + 1) * (tMax - tMin + 1) / 2);
        for (int t = tMin; t <= tMax; t++)
            for (int s = sMin; s <= sMax; s++)
            {
                if (((s + t) & 1) != 0) continue;
                int x = (s + t) / 2, y = (t - s) / 2;
                inputs.Add((byte)(((int)TypeAt(x, y) << 1) | (Blackout(x, y) ? 1 : 0)));
            }
        return [.. inputs];
    }

    /// <summary>Туман игрока: клетка скрыта (за картой — скрыта). null — тумана нет. Клетка, скрытая вместе со всеми
    /// восемью соседями, заливается чёрным ромбом (<c>0x5ba1d0</c>, цвет 0); частично скрытая рисуется как обычно,
    /// её закрывает картинка тумана (<see cref="MapFog"/>).</summary>
    public Func<int, int, bool>? Hidden { get; init; }

    private bool Blackout(int x, int y)
    {
        if (Hidden is not { } hidden || !Inside(x, y)) return false;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (Inside(nx, ny) && !hidden(nx, ny)) return false;
            }
        return true;
    }

    private void DrawCell(RgbaImage image, int x, int y, int left, int top)
    {
        var (cx, cy) = GroundGeometry.CellOrigin(x, y);
        if (Blackout(x, y))
        {
            FillBlack(image, cx, cy, left, top);
            return;
        }
        var own = TypeAt(x, y);
        Span<int> near = stackalloc int[Neighbours.Length];
        Near(x, y, near);
        Span<GroundBorder> borders = stackalloc GroundBorder[Neighbours.Length];
        int count = Inside(x, y) ? Borders(near, (int)own, borders) : 0;
        // Точка узора: мир + (N·64, N·32) — так координаты неотрицательны (0x543bbf).
        int px = cx + (Size * 64), py = cy + (Size * 32);

        if (own == GroundType.Water)
        {
            if (!AnimatedWater) FillWater(image, cx, cy, left, top);
            var (main, corners) = Shore(near, own);
            if (main != 0 && Pick(_shores, main, x, y, 0, GroundVariantDraws.ShoreMain) is { } mainSprite)
                FillOpaque(image, cx, cy, left, top, mainSprite);
            if (corners != 0 && Pick(_shores, corners, x, y, 1, GroundVariantDraws.ShoreMain + 1) is { } cornerSprite)
                FillOpaque(image, cx, cy, left, top, cornerSprite);
        }
        else FillPattern(image, cx, cy, left, top, CellPattern(Patterns(own), px, py));

        for (int i = 0; i < count; i++)
        {
            var border = borders[i];
            // У движка на клетку 10 мест под границы (броски 0…19); дальше — без бросков (при 8 типах земли не бывает).
            int draw = i < GroundVariantDraws.MaxBorders ? 2 * i : -1;
            var main = border.Main == 0 ? null : Pick(_masks, border.Main, x, y, 2 * i, draw);
            var add = border.Corners == 0 ? null : Pick(_masks, border.Corners, x, y, (2 * i) + 1, draw < 0 ? -1 : draw + 1);
            if (main is null && add is null) continue;
            Blend(image, cx, cy, left, top, main, add, CellPattern(Patterns(border.Type), px, py));
        }
    }

    /// <summary>Узор типа на одну клетку: сдвиг в узоре <c>(X, Y)</c> — мир по модулю 192, вариант — по блоку. Ромб, переходящий
    /// край блока, берёт вариант соседнего блока; для перехода сразу по обеим осям движок берёт точку <c>(+32, +16)</c>, а не
    /// диагональный блок (проход 4, <c>0x543b1e</c>) — так же и здесь. Вариант зависит только от того, за каким краем точка, —
    /// четыре картинки считаются один раз на клетку, а не на точку.</summary>
    private readonly record struct Pattern(int X, int Y, RgbaImage Base, RgbaImage OverX, RgbaImage OverY, RgbaImage OverXY);

    private Pattern CellPattern(RgbaImage[] patterns, int px, int py)
    {
        const int n = GroundGeometry.PatternSize;
        // Клетки далеко за картой (чёрный край) дают отрицательную точку: остаток — всегда неотрицательный.
        return new Pattern(Mod(px, n), Mod(py, n), Variant(px, py), Variant(px + 64, py), Variant(px, py + 32), Variant(px + 32, py + 16));

        RgbaImage Variant(int vx, int vy) =>
            patterns[patterns.Length == 1 || vx < 0 || vy < 0 ? 0 : _variants.At(vx, vy) % patterns.Length];
    }

    /// <summary>Строка ромба в узоре: точки ромба до края блока по X берутся из <c>Left</c> (с <c>LeftO</c>), после — из
    /// <c>Right</c>; <c>Split</c> — первый столбец ромба за краем. Ромб уже́ блока (64 &lt; 192), так что за край по каждой оси
    /// он переходит не больше одного раза.</summary>
    private static (byte[] Left, int LeftO, byte[] Right, int RightO, int Split) PatternRow(in Pattern p, int r)
    {
        const int n = GroundGeometry.PatternSize;
        int sy = p.Y + r;
        bool overY = sy >= n;
        if (overY) sy -= n;
        var (left, right) = overY ? (p.OverY, p.OverXY) : (p.Base, p.OverX);
        // Точка c ромба: слева — (X + c, sy), справа — (X + c − 192, sy); смещения даны для c = 0.
        return (left.Pixels, left.Offset(p.X, sy), right.Pixels, right.Offset(p.X - n, sy), n - p.X);
    }

    /// <summary>Узоры по типу (индекс — значение <see cref="GroundType"/>, байт); null — ещё не спрошены.</summary>
    private readonly RgbaImage[]?[] _patterns = new RgbaImage[]?[byte.MaxValue + 1];

    /// <summary>Узоры типа по вариантам (<c>max(1, число)</c> — как было: у типа без вариантов берётся вариант 0). Один раз на тип:
    /// картинка по имени ищется в описи, и делать это на каждую точку — большая часть времени сборки (27.09: 12 с на карту 96).
    /// Массив по типу, а не словарь: спрашивается на каждую клетку. Сборка идёт в нескольких потоках — гонка лишь спросит графику
    /// дважды, а она отдаёт те же картинки (у реализаций — кэш).</summary>
    private RgbaImage[] Patterns(GroundType type)
    {
        ref var slot = ref _patterns[(byte)type];
        if (Volatile.Read(ref slot) is { } patterns) return patterns;
        patterns = new RgbaImage[Math.Max(1, _art.PatternVariants(type))];
        for (int v = 0; v < patterns.Length; v++) patterns[v] = _art.Pattern(type, v);
        Volatile.Write(ref slot, patterns);
        return patterns;
    }

    /// <summary>Маски границ и спрайты берега (<see cref="MaskArt"/>): спрашиваются на каждую клетку.</summary>
    private readonly MaskArt _masks, _shores;

    /// <summary>Фон воды — один раз на сборщик (свойство графики может искать картинку по имени при каждом обращении).</summary>
    private readonly Lazy<RgbaImage?> _water;

    /// <summary>Маски или спрайты берега одного рода по номеру маски (рёбра 1..15, углы 17..31): число вариантов и картинки
    /// спрашиваются у графики по первому обращению и дальше берутся из массива — раньше на каждую клетку строилось имя картинки и
    /// искалось в словаре описи (профиль 14.10: ~7 % сборки). Путь поиска картинки — тот же (графика и её слои модов), меняется
    /// только то, как часто её спрашивают. Сборка идёт в нескольких потоках: гонка лишь спросит графику дважды, а она отдаёт одно и
    /// то же.</summary>
    private sealed class MaskArt(Func<int, int> variants, Func<int, int, RgbaImage?> image)
    {
        private const int MaskCount = 32;

        /// <summary>Место ещё не спрошенной картинки (null — картинки нет).</summary>
        private static readonly RgbaImage NotLoaded = new(0, 0);

        private readonly int[] _variants = Enumerable.Repeat(-1, MaskCount).ToArray();
        private readonly RgbaImage?[]?[] _images = new RgbaImage?[]?[MaskCount];

        public int Variants(int mask)
        {
            if ((uint)mask >= MaskCount) return variants(mask);
            int n = Volatile.Read(ref _variants[mask]);
            if (n < 0) Volatile.Write(ref _variants[mask], n = variants(mask));
            return n;
        }

        public RgbaImage? Image(int mask, int variant)
        {
            if ((uint)mask >= MaskCount) return image(mask, variant);
            var slots = Volatile.Read(ref _images[mask]);
            if (slots is null)
            {
                var fresh = new RgbaImage?[Math.Max(0, Variants(mask))];
                Array.Fill(fresh, NotLoaded);
                slots = Interlocked.CompareExchange(ref _images[mask], fresh, null) ?? fresh;
            }
            if ((uint)variant >= (uint)slots.Length) return image(mask, variant);
            var result = Volatile.Read(ref slots[variant]);
            if (ReferenceEquals(result, NotLoaded)) Volatile.Write(ref slots[variant], result = image(mask, variant));
            return result;
        }
    }

    /// <summary>Земля на 16-битной поверхности, как у движка (DirectDraw 565): точки узора, воды и берега — в 565, смешение по маске —
    /// в единицах 565, результат расширен как снимок обёртки (<see cref="MapComposite.Expand565"/>). Для сверки со снимками; вид карты
    /// порта рисует в 888.</summary>
    public bool Rgb565 { get; init; }

    private static readonly int[] Shift565 = [3, 2, 3];

    /// <summary>Броски R250 движка на варианты масок и берега (<see cref="GroundVariantDraws"/>); null — выбор хэшем клетки.</summary>
    public GroundVariantDraws? Draws { get; init; }

    /// <summary>Вариант маски или спрайта берега: бросок движка клетки номер <paramref name="drawSlot"/> по модулю числа картинок
    /// (<see cref="Draws"/>, <c>0x54361d</c>); бросков нет (или клетка за картой) — детерминированно от клетки, места и маски.</summary>
    private RgbaImage? Pick(MaskArt art, int mask, int x, int y, int slot, int drawSlot)
    {
        int variants = art.Variants(mask);
        if (variants <= 0) return null;
        if (Draws?.At(x, y, drawSlot) is { } draw) return art.Image(mask, draw % variants);
        uint h = unchecked((uint)((x * 73856093) ^ (y * 19349663) ^ (slot * 83492791) ^ (mask * 2654435761)));
        return art.Image(mask, (int)(h % (uint)variants));
    }

    /// <summary>Точка чёрного ромба (<c>0x5ba1d0</c>, цвет 0): RGBA, как точка картинки, — пишется тем же <see cref="CopyPixel"/>.</summary>
    private static readonly byte[] BlackPixel = [0, 0, 0, 255];

    /// <summary>Чёрный ромб скрытой клетки.</summary>
    private void FillBlack(RgbaImage image, int cx, int cy, int left, int top)
    {
        var dst = image.Pixels;
        for (int r = 0; r < GroundGeometry.CellHeight; r++)
        {
            if (!RowSpan(image, cx, cy, left, top, r, out int c0, out int c1, out int o)) continue;
            for (int c = c0; c < c1; c++, o += 4) CopyPixel(dst, o, BlackPixel, 0);
        }
    }

    /// <summary>Ромб фоном воды с периодом <see cref="WaterPeriod"/> по миру — прямым циклом по строкам, как узор (профиль 14.10:
    /// вода и берег через делегат на точку — четверть времени сборки). Фона нет — чёрный.</summary>
    private void FillWater(RgbaImage image, int cx, int cy, int left, int top)
    {
        if (_water.Value is not { } water)
        {
            FillBlack(image, cx, cy, left, top);
            return;
        }
        var dst = image.Pixels;
        var src = water.Pixels;
        int width = water.Width;
        for (int r = 0; r < GroundGeometry.CellHeight; r++)
        {
            if (!RowSpan(image, cx, cy, left, top, r, out int c0, out int c1, out int o)) continue;
            int row = water.Offset(0, Mod(cy + r, WaterPeriod) % water.Height);
            int sx = Mod(cx + c0, WaterPeriod);   // столбец в периоде воды; в картинке — по модулю её ширины
            for (int c = c0; c < c1; c++, o += 4)
            {
                CopyPixel(dst, o, src, row + (4 * (sx < width ? sx : sx % width)));
                if (++sx == WaterPeriod) sx = 0;
            }
        }
    }

    /// <summary>Спрайт берега поверх ромба: прозрачные точки (альфа 0) не пишутся.</summary>
    private void FillOpaque(RgbaImage image, int cx, int cy, int left, int top, RgbaImage sprite)
    {
        var dst = image.Pixels;
        var src = sprite.Pixels;
        for (int r = 0; r < GroundGeometry.CellHeight; r++)
        {
            if (!RowSpan(image, cx, cy, left, top, r, out int c0, out int c1, out int o)) continue;
            for (int c = c0, so = sprite.Offset(c0, r); c < c1; c++, o += 4, so += 4)
                if (src[so + 3] != 0) CopyPixel(dst, o, src, so);
        }
    }

    /// <summary>Ромб узором клетки — прямым циклом по строкам, без делегата на точку (профиль 14.10: сборка клетки через делегаты —
    /// три четверти времени земли).</summary>
    private void FillPattern(RgbaImage image, int cx, int cy, int left, int top, in Pattern pattern)
    {
        var dst = image.Pixels;
        for (int r = 0; r < GroundGeometry.CellHeight; r++)
        {
            if (!RowSpan(image, cx, cy, left, top, r, out int c0, out int c1, out int o)) continue;
            var (srcL, oL, srcR, oR, split) = PatternRow(pattern, r);
            int mid = Math.Clamp(split, c0, c1);
            CopyRun(dst, o, srcL, oL + (4 * c0), mid - c0);
            CopyRun(dst, o + (4 * (mid - c0)), srcR, oR + (4 * mid), c1 - mid);
        }
    }

    private void CopyRun(byte[] dst, int o, byte[] src, int so, int count)
    {
        for (int i = 0; i < count; i++, o += 4, so += 4) CopyPixel(dst, o, src, so);
    }

    /// <summary>Точка источника в картинку: RGB (в 565 — через <see cref="Channel565"/>), альфа 255.</summary>
    private void CopyPixel(byte[] dst, int o, byte[] src, int so)
    {
        if (Rgb565)
        {
            dst[o] = Channel565(src[so], 0);
            dst[o + 1] = Channel565(src[so + 1], 1);
            dst[o + 2] = Channel565(src[so + 2], 2);
        }
        else (dst[o], dst[o + 1], dst[o + 2]) = (src[so], src[so + 1], src[so + 2]);
        dst[o + 3] = 255;
    }

    /// <summary>Смешение по маске (<c>0x678ba7</c>): <c>dst + ⌊(src − dst)·m / 256⌋</c> по каналу, при <c>m = 0</c>
    /// точка не трогается. Сдвиг вправо у отрицательной разности округляет вниз — как в оригинале. Вес — красный канал маски
    /// рёбер <paramref name="main"/>, где он 0 — угловой <paramref name="add"/>; источник — узор соседа.</summary>
    private void Blend(RgbaImage image, int cx, int cy, int left, int top, RgbaImage? main, RgbaImage? add, in Pattern pattern)
    {
        var dst = image.Pixels;
        for (int r = 0; r < GroundGeometry.CellHeight; r++)
        {
            if (!RowSpan(image, cx, cy, left, top, r, out int c0, out int c1, out int o)) continue;
            var (srcL, oL, srcR, oR, split) = PatternRow(pattern, r);
            var weights = new Weights(main?.Pixels, main?.Offset(0, r) ?? 0, add?.Pixels, add?.Offset(0, r) ?? 0);
            int mid = Math.Clamp(split, c0, c1);
            BlendRun(dst, o, srcL, oL, weights, c0, mid);
            BlendRun(dst, o + (4 * (mid - c0)), srcR, oR, weights, mid, c1);
        }
    }

    /// <summary>Строка масок клетки: смещения точки <c>c = 0</c> строки в маске рёбер и угловой (null — маски нет).</summary>
    private readonly record struct Weights(byte[]? Main, int MainO, byte[]? Add, int AddO);

    /// <summary>Столбцы ромба <c>[c0, c1)</c> строки: <paramref name="o"/> — точка <c>c0</c> в картинке, <paramref name="so"/> —
    /// точка <c>c = 0</c> в узоре.</summary>
    private void BlendRun(byte[] dst, int o, byte[] src, int so, in Weights w, int c0, int c1)
    {
        for (int c = c0; c < c1; c++, o += 4)
        {
            int m = w.Main is { } main ? main[w.MainO + (4 * c)] : 0;
            if (m == 0 && w.Add is { } add) m = add[w.AddO + (4 * c)];
            if (m == 0 || dst[o + 3] == 0) continue;   // прозрачная точка — анимированная вода: не смешивать
            int s = so + (4 * c);
            if (Rgb565)
                for (int ch = 0; ch < 3; ch++)
                    dst[o + ch] = MapComposite.Expand565(Mix(dst[o + ch] >> Shift565[ch], src[s + ch] >> Shift565[ch], m), ch);
            else
                for (int ch = 0; ch < 3; ch++) dst[o + ch] = (byte)Mix(dst[o + ch], src[s + ch], m);
            dst[o + 3] = 255;
        }
    }

    private static int Mix(int d, int s, int m) => d + (((s - d) * m) >> 8);

    private static byte Channel565(int v, int ch) => MapComposite.Expand565(v >> Shift565[ch], ch);

    /// <summary>Строка <paramref name="r"/> ромба клетки, попавшая в картинку: столбцы ромба <c>[c0, c1)</c> и смещение точки
    /// <c>c0</c> в картинке; false — строка вне картинки.</summary>
    private static bool RowSpan(RgbaImage image, int cx, int cy, int left, int top, int r, out int c0, out int c1, out int o)
    {
        int iy = cy + r - top;
        var (x0, w) = GroundGeometry.Row(r);
        c0 = Math.Max(x0, left - cx);
        c1 = Math.Min(x0 + w, left + image.Width - cx);
        o = 0;
        if (iy < 0 || iy >= image.Height || c0 >= c1) return false;
        o = image.Offset(cx + c0 - left, iy);
        return true;
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

    private static int Mod(int v, int n) => ((v % n) + n) % n;
}
