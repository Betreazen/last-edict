using D2.Data.Map;

namespace D2.Data.Sound;

/// <summary>Источник фонового звука места: звук <see cref="Name"/> архива <c>AudioRgn</c> у объекта клетки (<see cref="X"/>, <see cref="Y"/>)
/// размером <see cref="W"/>×<see cref="H"/>.</summary>
public sealed record AudioSource(string Name, int X, int Y, int W, int H)
{
    /// <summary>Центр источника в точках движка: (x·64 + w·32, y·32 + h·16).</summary>
    public (int X, int Y) Centre => ((X * 64) + (W * 32), (Y * 32) + (H * 16));
}

/// <summary>Звучащий источник: громкость 0…100 и панорама 0…127 (64 — середина).</summary>
public sealed record AudioVoice(AudioSource Source, int Volume, int Pan);

/// <summary>
/// Фон мест (<c>CAudioRegionMgr</c>, <c>docs/ai-screen.md</c> §12, ранг 1 — листинг): источники собираются один раз при загрузке карты —
/// столицы по расе владельца (<c>CAPHU</c>, <c>CAPUN</c>, <c>CAPHE</c>, <c>CAPEL</c>, прочие — <c>CAPDW</c>), деревни <c>CITY</c>, золотые
/// жилы <c>GOLDMINE</c>, башня мага <c>MAGICTW</c>, наставник <c>TRAINCMP</c> (в архиве его нет — молчит), торговец <c>WINDMILL</c> —
/// только с картинкой 0, ориентиры по номеру <c>G000MG</c>, руины <c>RUIN</c> (<see cref="Ruin"/>). Играют <c>N</c> ближайших к центру вида
/// (настройка <c>AudioRegions</c>, поставляемый ini — 3) не дальше <see cref="Reach"/> точек; громкость <c>100 − d·100/769</c>, панорама — по
/// столбцу клетки <c>size + x − y − 1</c>: <c>Δ·63/12 + 64</c>. Источник, чьего звука нет в архиве, молчит и места не занимает: движок ищет
/// звук по имени точно (<c>0x5674ce</c> → <c>0x567189</c>, имя в верхнем регистре, поиск в оглавлении <c>0x568a99</c>), не нашёл — пропуск без
/// траты свободного канала (<c>0x509282</c>, счёт каналов <c>0x5208a8</c> уменьшается только у найденного).
/// </summary>
public static class AudioRegions
{
    public const string Archive = "AudioRgn";
    public const int Reach = 768, DefaultCount = 3;

    /// <summary>Звук руины (<c>CAudioRgnRuin</c>, конструктор <c>0x50851a</c>, имя — строка <c>0x6df704</c>): одно имя у всех руин, без номера и
    /// без условия разграбления (фабрика <c>0x507bb7</c>: объект типа <c>0x29</c> → <c>0x507dbe</c>). В <c>AudioRgn.wdb</c> такого звука нет —
    /// там <c>RUIN1</c>…<c>RUIN4</c>, их имён в <c>Discipl2.exe</c> нет вовсе, как и <c>AIRWARD</c> и <c>GOLDMINE2</c>: у движка руины молчат,
    /// а эти пять файлов не играет никто. Мод, положивший в архив звук <c>RUIN</c>, озвучит все руины.</summary>
    public const string Ruin = "RUIN";
    private const int CapitalSize = 5, VillageSize = 4, SiteSize = 3;

    /// <summary>Ориентиры: номер <c>G000MG????</c> → звук.</summary>
    private static readonly Dictionary<int, string> Landmarks = new()
    {
        [4] = "TORCH", [5] = "TORCH",
        [6] = "WATRFALL", [7] = "WATRFALL", [79] = "WATRFALL", [80] = "WATRFALL", [81] = "WATRFALL", [82] = "WATRFALL",
        [23] = "LAVA", [24] = "LAVA",
        [25] = "CEMETARY", [26] = "CEMETARY", [27] = "CEMETARY",
        [55] = "FLAMECTY", [73] = "WHIRL", [77] = "GATECLSD", [78] = "GATEOPEN",
    };

    /// <summary>Источники карты. <paramref name="raceTypeOf"/> — <c>RaceType</c> владельца по id игрока (нет — −1),
    /// <paramref name="landmarkSize"/> — след ориентира.</summary>
    public static IReadOnlyList<AudioSource> Sources(MapLooks looks, Func<string, int> raceTypeOf, Func<string, (int W, int H)> landmarkSize)
    {
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(raceTypeOf);
        ArgumentNullException.ThrowIfNull(landmarkSize);
        var sources = new List<AudioSource>();
        foreach (var fort in looks.Forts)
        {
            int size = fort.Capital ? CapitalSize : VillageSize;
            string name = !fort.Capital ? "CITY" : raceTypeOf(fort.Owner) switch
            {
                0 => "CAPHU", 1 => "CAPUN", 2 => "CAPHE", 5 => "CAPEL", _ => "CAPDW",
            };
            sources.Add(new AudioSource(name, fort.X, fort.Y, size, size));
        }
        foreach (var site in looks.Sites)
        {
            string? name = site.Type switch
            {
                "MAGE" => "MAGICTW",
                "TRAI" => "TRAINCMP",
                "MERH" when site.Image == 0 => "WINDMILL",
                _ => null,
            };
            if (name is not null) sources.Add(new AudioSource(name, site.X, site.Y, SiteSize, SiteSize));
        }
        foreach (var ruin in looks.Ruins) sources.Add(new AudioSource(Ruin, ruin.X, ruin.Y, SiteSize, SiteSize));
        foreach (var crystal in looks.Crystals.Where(c => c.Resource == 0))
            sources.Add(new AudioSource("GOLDMINE", crystal.X, crystal.Y, 1, 1));
        foreach (var mark in looks.Landmarks)
        {
            if (mark.Type.Length < 10 || !int.TryParse(mark.Type.AsSpan(6, 4), out int number)
                || !Landmarks.TryGetValue(number, out var name)) continue;
            var (w, h) = landmarkSize(mark.Type);
            sources.Add(new AudioSource(name, mark.X, mark.Y, w, h));
        }
        return sources;
    }

    /// <summary>Что звучит, когда в центре вида клетка (<paramref name="x"/>, <paramref name="y"/>): не больше <paramref name="count"/>
    /// ближайших в пределах <see cref="Reach"/> из тех, чей звук есть (<paramref name="audible"/>; не задан — все); <paramref name="mapSize"/> —
    /// сторона карты (для панорамы).</summary>
    public static IReadOnlyList<AudioVoice> Nearest(IReadOnlyList<AudioSource> sources, int x, int y, int count, int mapSize,
                                                    Func<string, bool>? audible = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var (vx, vy) = ((x * 64) + 32, (y * 32) + 16);
        int viewColumn = mapSize + x - y - 1;
        return sources
            .Select(s => (Source: s, Distance: Math.Sqrt(Math.Pow(s.Centre.X - vx, 2) + Math.Pow(s.Centre.Y - vy, 2))))
            .Where(p => p.Distance <= Reach && (audible is null || audible(p.Source.Name)))
            .OrderBy(p => p.Distance)
            .Take(Math.Max(0, count))
            .Select(p =>
            {
                int column = mapSize + p.Source.X + (p.Source.W / 2) - (p.Source.Y + (p.Source.H / 2)) - 1;
                int pan = Math.Clamp(((column - viewColumn) * 63 / 12) + 64, 0, 127);
                return new AudioVoice(p.Source, 100 - (int)(p.Distance * 100 / (Reach + 1)), pan);
            })
            .ToList();
    }
}
