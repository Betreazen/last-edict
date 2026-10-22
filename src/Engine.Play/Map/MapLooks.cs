using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>Крепость: столица (<c>CCapital</c>, след 5×5) или город (<c>CMidVillage</c>, 4×4). <see cref="Level"/> —
/// уровень города (у столицы 0), <see cref="Riot"/> — идёт бунт, <see cref="Garrison"/> — id отряда в крепости (для
/// щита над воротами), пусто — нет. Имя и описание города — в модели (<c>MapSite.Name</c>/<c>Description</c>).</summary>
public sealed record FortLook(string Id, bool Capital, int X, int Y, string Owner, int Level, bool Riot, string Garrison);

public sealed record RuinLook(string Id, int X, int Y, bool Looted, int Image);

/// <summary>Лавка: <see cref="Type"/> — код в имени картинки (<c>MERH</c>, <c>MAGE</c>, <c>MERC</c>, <c>TRAI</c>). Имя и
/// описание — в модели (<c>MapSite.Name</c>/<c>Description</c>).</summary>
public sealed record SiteLook(string Id, int X, int Y, string Type, int Image);

public sealed record CrystalLook(string Id, int X, int Y, int Resource);

public sealed record BagLook(string Id, int X, int Y, int Image);

/// <summary>Могила на карте (<c>CMidTomb</c>, модель — <see cref="MapState.Tombs"/>): ставится гибелью целого отряда в бою и не
/// снимается (<c>docs/day-cycle.md</c> §13). Рисуется одним статичным кадром без анимации и звука (<see cref="Frame"/>).</summary>
public sealed record TombLook(string Id, int X, int Y)
{
    /// <summary>Кадр графики <c>IsoCmon</c> (<c>0x5c593a</c>): на воде — <c>G000TB0000W</c>, иначе <c>G000TB0000G</c>; раса, эпитафии и
    /// убитый отряд не читаются. Ранг 1.</summary>
    public string Frame(MapState map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Contains(X, Y) && map.At(X, Y).Ground == Ground.Water ? "G000TB0000W" : "G000TB0000G";
    }
}

public sealed record RodLook(string Id, int X, int Y, string Owner);

public sealed record LandmarkLook(string Id, int X, int Y, string Type);

/// <summary>Объект гор: угол, размер, номер картинки и раса — id <c>LRace</c> (0 HU, 1 UN, 2 HE, 3 DW, 4 NE, 5 EL).</summary>
public sealed record MountainLook(int Id, int X, int Y, int Width, int Height, int Image, int Race)
{
    /// <summary>Код местности расы гор — в имени картинки <c>MOM%s%02d%02d</c> (<c>0x5c70be</c>) и в ключе числа картинок
    /// (<c>0x5efd35</c>); прочие — нейтральная.</summary>
    public static string RaceCode(int race) => LegacyRaces.Code(race);
}

public sealed record RoadLook(string Id, int X, int Y, int Index, int Variant);

/// <summary>Отряд на карте: тип лидера (<c>Gunits.UNIT_ID</c>), жив ли он, есть ли в отряде живые, направление
/// <c>FACING</c> 0..7, подраса (знамя), <c>INSIDE</c> и <c>INVISIBLE</c>.</summary>
public sealed record StackLook(string Id, int X, int Y, string Owner, string Subrace, string LeaderType,
                               bool LeaderAlive, bool AnyAlive, int Facing, string Inside, bool Invisible,
                               string LeaderName = "");

/// <summary>Всё, что нужно нарисовать карту, в том виде, как оно лежит в <c>.sg</c> (разбор полей —
/// <c>docs/map-render.md</c> §2, <c>docs/map-render-stacks.md</c> §1–2). Правилам не нужно, поэтому отдельно от
/// <see cref="MapState"/>, но строится из того же файла.</summary>
public sealed record MapLooks
{
    public required MapState Map { get; init; }

    /// <summary>Номер картинки дерева клетки — биты 26–31 слова клетки (<c>MidgardMapBlock</c>), <c>[x, y]</c>.</summary>
    public required byte[,] TreeImage { get; init; }

    /// <summary>Биты 19–25 слова клетки — первая часть номера дерева (в ванили 0).</summary>
    public required byte[,] TreeGroup { get; init; }

    public IReadOnlyList<FortLook> Forts { get; init; } = [];
    public IReadOnlyList<RuinLook> Ruins { get; init; } = [];
    public IReadOnlyList<SiteLook> Sites { get; init; } = [];
    public IReadOnlyList<CrystalLook> Crystals { get; init; } = [];
    public IReadOnlyList<BagLook> Bags { get; init; } = [];
    public IReadOnlyList<TombLook> Tombs { get; init; } = [];
    public IReadOnlyList<RodLook> Rods { get; init; } = [];
    public IReadOnlyList<LandmarkLook> Landmarks { get; init; } = [];
    public IReadOnlyList<MountainLook> Mountains { get; init; } = [];
    public IReadOnlyList<RoadLook> Roads { get; init; } = [];
    public IReadOnlyList<StackLook> Stacks { get; init; } = [];

    /// <summary>Знамя подрасы (<c>MidSubrace.BANNER</c>) по id подрасы.</summary>
    public IReadOnlyDictionary<string, int> Banners { get; init; } = new Dictionary<string, int>();
}
