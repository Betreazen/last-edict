using D2.Rules.Rng;
using Kaitai;

namespace D2.Data.Gfx;

/// <summary>
/// Варианты масок границ и берега, как их выбирает движок (<c>docs/ai-screen.md</c> §14; разбор 27.09, ранг 1 по листингу, ранг 2 —
/// 37 из 37 клеток на снимках): конструктор земли <c>CIsoEngineGround</c> (<c>0x54226a</c>) один раз на сеанс бросает R250
/// (<c>0x54851f(3)</c>) по 22 раза на клетку, клетки — y снаружи, x внутри. Броски 2i и 2i+1 — варианты основной и угловой маски
/// i-й границы (после сортировки по типу), 20 и 21 — основного и углового берега; вариант = бросок % число картинок маски.
/// Состояние R250 перед землёй: у новой игры — исходная таблица с индексом после reseed и одним броском сверху, у загрузки сразу
/// после запуска игры — исходная таблица с индексом 47. Индекс новой игры восстанавливается из заголовка <c>.sg</c> (там состояние
/// R250 на момент записи) перемоткой назад, если сохранение из первой партии запуска (<see cref="EngineRng.RewindToTable"/>).
/// </summary>
public sealed class GroundVariantDraws
{
    public const int PerCell = 22, ShoreMain = 20, ShoreCorner = 21, MaxBorders = 10;
    private const int Choices = 3;

    private readonly byte[] _draws;

    public int Size { get; }

    /// <summary>Откуда состояние: «новая игра, индекс N», «загрузка после запуска» и т. п. — для печати проверок.</summary>
    public string Origin { get; }

    private GroundVariantDraws(int size, byte[] draws, string origin) => (Size, _draws, Origin) = (size, draws, origin);

    /// <summary>Броски для карты стороной <paramref name="size"/> от генератора в состоянии перед землёй.</summary>
    public static GroundVariantDraws From(EngineRng rng, int size, string origin = "")
    {
        ArgumentNullException.ThrowIfNull(rng);
        var draws = new byte[size * size * PerCell];
        for (int k = 0; k < draws.Length; k++) draws[k] = (byte)rng.Next(Choices);
        return new GroundVariantDraws(size, draws, origin);
    }

    /// <summary>Броски по состоянию R250 из заголовка сохранения: перемотка к исходной таблице найдена — новая игра (индекс после
    /// reseed, один бросок сверху), не найдена — загрузка сразу после запуска игры (исходная таблица, индекс 47, ранг 3).</summary>
    public static GroundVariantDraws ForSave(IReadOnlyList<ushort>? table, int index, int size)
    {
        if (table is null) return From(new EngineRng(), size, "загрузка после запуска");
        if (EngineRng.RewindToTable(table, index) is { } start)
        {
            if (start.Index == EngineRng.DefaultIndex) return From(new EngineRng(), size, "загрузка после запуска");
            var rng = new EngineRng(start.Index);
            rng.Step();   // бросок между reseed новой игры и землёй (место не найдено, по снимкам — ровно один)
            return From(rng, size, $"новая игра, индекс {start.Index}");
        }
        // Не первая партия запуска: таблица продолжает прежние партии, reseed сменил только индекс — встречный поиск.
        if (EngineRng.RewindToReseed(table, index) is { } reseed)
        {
            var rng = EngineRng.FromSeed(reseed.Table, reseed.Index);
            rng.Step();
            return From(rng, size, $"новая игра не первой в запуске, индекс {reseed.Index}");
        }
        return From(new EngineRng(), size, "загрузка после запуска");
    }

    /// <summary>Состояние R250 из заголовка <c>.sg</c> (<c>0x42a017</c>: индекс и 250 слов); у файла без заголовка D2EE — null.</summary>
    public static (ushort[] Table, int Index)? ReadSaveState(string path)
    {
        // Заголовок читает строки в cp1251 (D2eeHeader): поставщик кодировок, как у прочих загрузчиков сохранений.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var stream = new KaitaiStream(path);
        var signature = System.Text.Encoding.ASCII.GetString(stream.ReadBytes(10));
        if (signature != "D2EESFISIG") return null;
        var header = new Kaitai.Sg.D2eeHeader(stream);
        return ([.. header.PrngValues.Select(v => (ushort)v)], (int)header.UnknownData);
    }

    /// <summary>Бросок клетки (<paramref name="x"/>, <paramref name="y"/>) номер <paramref name="slot"/> (0…21); клетка за картой — null.</summary>
    public int? At(int x, int y, int slot)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size || slot < 0 || slot >= PerCell) return null;
        return _draws[(((y * Size) + x) * PerCell) + slot];
    }
}
