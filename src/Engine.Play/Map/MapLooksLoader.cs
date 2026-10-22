using D2.Data.Sg;
using D2.Rules.Map;
using Kaitai;
using KSg = Kaitai.Sg;

namespace D2.Data.Map;

/// <summary><c>.sg</c> → <see cref="MapLooks"/>: объекты карты для отрисовки. Правила карты — в
/// <see cref="MapLoader"/>, отсюда он и зовётся.</summary>
public static class MapLooksLoader
{
    private const int BlockWidth = 8;
    private const string EmptyId = "G000000000";

    /// <exception cref="InvalidDataException">Файл есть, но не читается как <c>.sg</c>.</exception>
    public static MapLooks Load(string sgPath) => Load(sgPath, SgFile.Open(sgPath));

    /// <summary>То же по уже разобранному файлу (<paramref name="sgPath"/> — только для сообщений).</summary>
    public static MapLooks Load(string sgPath, SgFile file)
    {
        // Файл разбирается один раз: и карта, и объекты для отрисовки берутся из одного разбора.
        ArgumentNullException.ThrowIfNull(file);
        var map = MapLoader.Load(sgPath, file);
        try
        {
            return Build(map, file.Raw.Objects.Select(o => o.Data).ToList());
        }
        catch (Exception e) when (e is not (InvalidDataException or FileNotFoundException or DirectoryNotFoundException))
        {
            throw new InvalidDataException($"{sgPath}: объекты карты не читаются", e);
        }
    }

    private static MapLooks Build(MapState map, IReadOnlyList<KaitaiStruct> data)
    {
        var (image, group) = TreeBits(data, map.Size);
        // Своё имя героя лежит в MidUnit.NAME_TXT; у рядовых оно пустое — имя берётся из таблицы по типу.
        var names = data.OfType<KSg.MidUnit>()
            .ToDictionary(u => u.UnitId.String, u => u.NameTxt.String, StringComparer.OrdinalIgnoreCase);
        return new MapLooks
        {
            Map = map,
            TreeImage = image,
            TreeGroup = group,
            Forts = [.. data.OfType<KSg.Capital>().Select(c => Fort(c.City, true, 0, false)),
                     .. data.OfType<KSg.MidVillage>().Select(v => Fort(v.City, false, (int)v.Size.Value, v.RiotT.Value != 0))],
            Ruins = [.. data.OfType<KSg.MidRuin>().Select(r =>
                new RuinLook(r.RuinId.String, X(r.Position), Y(r.Position), !IsEmpty(r.Looter.String), (int)r.Image.Value))],
            Sites = [.. Sites(data)],
            Crystals = [.. data.OfType<KSg.MidCrystal>().Select(c =>
                new CrystalLook(c.CrystalId.String, X(c.Position), Y(c.Position), (int)c.Reource.Value))],
            Bags = [.. data.OfType<KSg.MidBag>().Select(b => new BagLook(b.BagId.String, X(b.Position), Y(b.Position), (int)b.Image.Value))],
            Tombs = [.. data.OfType<KSg.MidTomb>().Select(t => new TombLook(t.TombId.String, X(t.Position), Y(t.Position)))],
            Rods = [.. data.OfType<KSg.MidRod>().Select(r => new RodLook(r.RodId.String, X(r.Position), Y(r.Position), r.Owner.String))],
            Landmarks = [.. data.OfType<KSg.MidLandmark>().Select(l =>
                new LandmarkLook(l.LmarkId.String, X(l.Position), Y(l.Position), l.Type.String.ToUpperInvariant()))],
            Mountains = [.. data.OfType<KSg.MidMountains>().SelectMany(m => m.Mountains).Select(m =>
                new MountainLook((int)m.IdMount.Value, X(m.Position), Y(m.Position), (int)m.Size.X.Value, (int)m.Size.Y.Value,
                                 (int)m.Image.Value, (int)m.Race.Value))],
            Roads = [.. data.OfType<KSg.MidRoad>().Select(r =>
                new RoadLook(r.RoadId.String, X(r.Position), Y(r.Position), (int)r.Index.Value, (int)r.Var.Value))],
            Stacks = [.. data.OfType<KSg.MidStack>().Select(s => Stack(s, map, names))],
            Banners = data.OfType<KSg.MidSubrace>()
                .ToDictionary(s => s.SubraceId.String, s => (int)s.Banner.Value, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Слово клетки — байт типа, два байта середины, байт картинки (<c>Sg.Tile</c>): биты 19–25 — в
    /// середине (старшие 5 бит) и картинке (младшие 2), биты 26–31 — старшие 6 бит байта картинки.</summary>
    private static (byte[,] Image, byte[,] Group) TreeBits(IReadOnlyList<KaitaiStruct> data, int size)
    {
        var image = new byte[size, size];
        var group = new byte[size, size];
        foreach (var block in data.OfType<KSg.MidgardMapBlock>())
        {
            string id = block.Blockid.String;
            int by = Convert.ToInt32(id.Substring(6, 2), 16), bx = Convert.ToInt32(id.Substring(8, 2), 16);
            for (int i = 0; i < block.Tiles.Count; i++)
            {
                int x = bx + i % BlockWidth, y = by + i / BlockWidth;
                if (x >= size || y >= size) continue;
                var t = block.Tiles[i];
                image[x, y] = (byte)(t.ImageId >> 2);
                group[x, y] = (byte)(((t.Unknown >> 11) & 0x1F) | ((t.ImageId & 3) << 5));
            }
        }
        return (image, group);
    }

    private static FortLook Fort(KSg.City c, bool capital, int level, bool riot) =>
        new(c.CityId.String, capital, X(c.Position), Y(c.Position), Ref(c.Owner.String), level, riot, Ref(c.Stack.String));

    private static IEnumerable<SiteLook> Sites(IReadOnlyList<KaitaiStruct> data)
    {
        foreach (var d in data)
        {
            var (site, type) = d switch
            {
                KSg.MidSiteMerchant m => (m.Site, "MERH"),
                KSg.MidSiteMage m => (m.Site, "MAGE"),
                KSg.MidSiteMercs m => (m.Site, "MERC"),
                KSg.MidSiteTrainer m => (m.Site, "TRAI"),
                _ => ((KSg.Site?)null, ""),
            };
            if (site is not null)
                yield return new SiteLook(site.SiteId.String, X(site.Position), Y(site.Position), type, (int)site.ImgIso.Value);
        }
    }

    private static StackLook Stack(KSg.MidStack s, MapState map, IReadOnlyDictionary<string, string> names)
    {
        string leader = Ref(s.LeaderId.String);
        string type = map.Units.TryGetValue(leader, out var u) ? u.TypeId.ToUpperInvariant() : "";
        bool anyAlive = map.Stacks.TryGetValue(s.StackId.String, out var st) &&
                        st.UnitIds.Any(id => map.Units.TryGetValue(id, out var m) && m.Hp > 0);
        return new StackLook(s.StackId.String, X(s.Position), Y(s.Position), Ref(s.Owner.String), Ref(s.Subrace.String),
                             type, s.LeaderAliv.Value != 0, anyAlive, (int)s.Facing.Value, Ref(s.Inside.String),
                             s.Invisible.Value != 0, names.GetValueOrDefault(leader, "").Trim());
    }

    private static int X(KSg.Position p) => (int)p.X.Value;

    private static int Y(KSg.Position p) => (int)p.Y.Value;

    private static bool IsEmpty(string id) => id.Length == 0 || id.Equals(EmptyId, StringComparison.OrdinalIgnoreCase);

    private static string Ref(string id) => IsEmpty(id) ? "" : id;
}
