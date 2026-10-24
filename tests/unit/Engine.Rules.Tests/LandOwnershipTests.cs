using LastEdict.Rules.Map;

/// <summary>
/// Земля по владельцу клетки (<see cref="LandOwnership"/>) на синтетике: при общей расе своя земля — клетки рода расы с хозяином-игроком, и
/// рост одного берёт землю другого так же, как чужую; без слоя хозяев — правило оригинала (род земли).
/// </summary>
public class LandOwnershipTests
{
    private const int Terrain = 1;      // L_HU — раса обоих игроков
    private const int Size = 10;
    private const string A = "S001PL0000", B = "S001PL0001";

    /// <summary>Карта 10×10, вся равнина рода <see cref="Terrain"/>; хозяева — левая половина у <see cref="A"/>, правая у <see cref="B"/>.</summary>
    private static MapState SplitMap()
    {
        var tiles = new Tile[Size, Size];
        var owners = new byte[Size, Size];
        for (int x = 0; x < Size; x++)
            for (int y = 0; y < Size; y++)
            {
                tiles[x, y] = new Tile((byte)Terrain);
                owners[x, y] = x < Size / 2 ? (byte)1 : (byte)2;
            }
        return new MapState
        {
            Size = Size, Tiles = tiles, Turn = 1,
            Stacks = new Dictionary<string, MapStack>(), Units = new Dictionary<string, MapUnit>(),
            Players = new Dictionary<string, MapPlayer>(), Objects = new Dictionary<string, MapObject>(),
            Plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>(), Fog = new Dictionary<string, bool[,]>(),
            LandOwners = owners, LandOwnerIds = [A, B],
        };
    }

    [Fact]
    public void IsOwn_UsesTheOwner_WhenTracked_AndTheTerrainOtherwise()
    {
        var map = SplitMap();
        Assert.True(LandOwnership.IsOwn(map, 1, 1, A, Terrain));
        Assert.False(LandOwnership.IsOwn(map, 8, 1, A, Terrain));
        Assert.True(LandOwnership.IsOwn(map, 8, 1, B, Terrain));
        Assert.Equal(50, LandOwnership.OwnCells(map, A, Terrain).Count);

        var untracked = new MapState
        {
            Size = map.Size, Tiles = map.Tiles, Turn = 1, Stacks = map.Stacks, Units = map.Units, Players = map.Players,
            Objects = map.Objects, Plan = map.Plan, Fog = map.Fog,
        };
        Assert.False(LandOwnership.Tracked(untracked));
        Assert.True(LandOwnership.IsOwn(untracked, 8, 1, A, Terrain));   // оригинал: род земли — и всё
        Assert.Equal(Size * Size, LandOwnership.OwnCells(untracked, A, Terrain).Count);
    }

    /// <summary>Перемаркировка: клетка рода игроков общей земли — заявителю, клетка нейтрального рода — без хозяина; слой копируется.</summary>
    [Fact]
    public void Retag_GivesTheClaimant_AndClearsNeutralLand()
    {
        var map = SplitMap();
        var rules = new EconomyRules(new Dictionary<string, RaceInfo>
        {
            ["R0"] = new("R0", Bank.Zero, 0, 0, "", 0, true) { Terrain = Terrain },
        }, [], new Dictionary<string, BuildingType>(), new Dictionary<string, UnitHire>(), [], 0, 0, 0);
        var players = new Dictionary<string, MapPlayer>
        {
            [A] = new() { Id = A, RaceId = "R0", LordId = "", Bank = Bank.Zero, FogId = "", BuildingsId = "", Buildings = [] },
            [B] = new() { Id = B, RaceId = "R0", LordId = "", Bank = Bank.Zero, FogId = "", BuildingsId = "", Buildings = [] },
        };
        map = new MapState
        {
            Size = map.Size, Tiles = map.Tiles, Turn = 1, Stacks = map.Stacks, Units = map.Units, Players = players, Objects = map.Objects,
            Plan = map.Plan, Fog = map.Fog, LandOwners = map.LandOwners, LandOwnerIds = map.LandOwnerIds,
        };
        var tiles = (Tile[,])map.Tiles.Clone();
        tiles[3, 3] = new Tile(5);   // нейтральная
        var owners = LandOwnership.Retag(map, tiles, [(8, 8), (3, 3)], A, rules)!;
        Assert.Equal(1, owners[8, 8]);              // земля B — теперь A
        Assert.Equal(0, owners[3, 3]);              // нейтральная — без хозяина
        Assert.Equal(2, map.LandOwners![8, 8]);     // прежний слой не тронут

        Assert.True(LandOwnership.PartyBySlot(map, rules) is [null, 0, 0]);
    }

    /// <summary>Рост игрока A от его половины идёт в половину B (у оригинала её клетки были бы «своими», и расти было бы некуда).</summary>
    [Fact]
    public void Growth_TakesTheLandOfTheOtherPlayerOfTheSameRace()
    {
        var map = SplitMap();
        var capital = new LandSource("", (2, 5), LandQuotas.Vanilla.Capital, [(2, 5)]);
        var model = LandModel.BuildSources(map, Terrain, [capital], LandQuotas.Vanilla, A, owner: A);

        Assert.Equal(50, model.StartCells);
        var grown = model.Certain(2);
        Assert.Contains(grown, c => c.X >= Size / 2);
        Assert.Equal(50 + LandQuotas.Vanilla.Capital, grown.Count);

        var original = LandModel.BuildSources(map, Terrain, [capital], LandQuotas.Vanilla, A);   // без хозяина — как оригинал
        Assert.Equal(Size * Size, original.StartCells);
        Assert.Equal(Size * Size, original.Certain(2).Count);
    }
}
