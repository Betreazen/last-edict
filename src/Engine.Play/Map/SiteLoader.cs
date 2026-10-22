using LastEdict.Data.Sg;
using LastEdict.Rules.Map;
using KSg = Kaitai.Sg;

namespace LastEdict.Data.Map;

/// <summary>
/// Сценарий или сохранение -> объекты карты, которые можно посетить (<see cref="MapSite"/>).
/// Прямая проекция полей <c>.sg</c>, ранг 1; ничего не достраивается.
///
/// <para><see cref="MapLoader"/> кладёт те же объекты в <c>MapState.Sites</c> за один проход
/// (<see cref="SiteOf"/>); <see cref="Load"/> остаётся для вызывающих, которым карта целиком не нужна.</para>
/// </summary>
public static class SiteLoader
{
    /// <summary>Пустая ссылка в <c>.sg</c> — десять знаков нулевого <c>CMidgardID</c>
    /// (то же соглашение и та же причина, что в <see cref="MapLoader"/>).</summary>
    private const string EmptyId = "G000000000";

    private static bool IsEmptyId(string id) =>
        id.Length == 0 || id.Equals(EmptyId, StringComparison.OrdinalIgnoreCase);

    private static string Ref(KSg.StringRecord record) =>
        IsEmptyId(record.String) ? "" : record.String;

    /// <summary>Расы прежних хозяев деревни (<see cref="MapSite.PreviousOwners"/>): <c>P_O_HU</c> — 0 Империя, <c>P_O_UN</c> — 1 Орды,
    /// <c>P_O_HE</c> — 2 Легионы, <c>P_O_DW</c> — 3 Кланы, <c>P_O_EL</c> — 5 Эльфы (только в файлах <c>D2EESFISIG</c>).</summary>
    private static IReadOnlyList<int>? PreviousOwners(KSg.MidVillage v)
    {
        var races = new List<int>();
        if (v.POHu.Value != 0) races.Add(0);
        if (v.POUn.Value != 0) races.Add(1);
        if (v.POHe.Value != 0) races.Add(2);
        if (v.PODw.Value != 0) races.Add(3);
        if (v.POEl is { Value: not 0 }) races.Add(5);
        return races.Count > 0 ? races : null;
    }

    /// <inheritdoc cref="MapLoader.Load"/>
    /// <exception cref="InvalidDataException">Файл есть, но не читается как <c>.sg</c>.</exception>
    public static IReadOnlyList<MapSite> Load(string sgPath)
    {
        try
        {
            return SgFile.Open(sgPath).Raw.Objects.Select(SiteOf).OfType<MapSite>().ToList();
        }
        catch (Exception e) when (e is not (InvalidDataException or FileNotFoundException
                                            or DirectoryNotFoundException))
        {
            throw new InvalidDataException($"{sgPath}: не читается как .sg", e);
        }
    }

    /// <summary>
    /// Объект карты, если его вид посещается. Класс записи (а не две буквы id) решает вид:
    /// у лавки, наёмников, наставника и башни мага id одного класса <c>SI</c>.
    /// </summary>
    internal static MapSite? SiteOf(KSg.ScenarioObject o)
    {
        string id = o.ObjId.String;
        string raw = o.Data.GetType().Name;
        return o.Data switch
        {
            KSg.Capital c => CityOf(id, SiteKind.Capital, raw, c.City) with { Items = Ids(c.Items), AiPriority = (int)c.Aipriority.Value },
            KSg.MidVillage v => CityOf(id, SiteKind.Village, raw, v.City) with
            {
                Items = Ids(v.Items), Size = (int)v.Size.Value, GrowthTurn = (int)v.GrowthT.Value,
                RiotTurn = (int)v.RiotT.Value, RegenBonus = (int)v.RegenB.Value, ProtectBonus = Ref(v.ProtectB),
                Morale = (int)v.Morale.Value, AiPriority = (int)v.Aipriority.Value, PreviousOwners = PreviousOwners(v),
            },
            KSg.MidRuin r => new MapSite
            {
                Id = id, Kind = SiteKind.Ruin, RawKind = raw,
                X = (int)r.Position.X.Value, Y = (int)r.Position.Y.Value, Name = r.Title.String.Trim(), Description = r.Desc.String.Trim(),
                Cash = BankOf(r.Cash), ItemId = Ref(r.Item), LooterId = Ref(r.Looter),
                Guards = UnitsOf(r.Group), GuardSlots = SlotsOf(r.Group), Visitors = Ids(r.Visitors.Select(x => x.Visiter)),
                AiPriority = (int)r.Aipriority.Value,
            },
            KSg.MidBag b => new MapSite
            {
                Id = id, Kind = SiteKind.Bag, RawKind = raw,
                X = (int)b.Position.X.Value, Y = (int)b.Position.Y.Value, Items = Ids(b.Items), AiPriority = (int)b.Aipriority.Value,
            },
            KSg.MidCrystal c => new MapSite
            {
                Id = id, Kind = SiteKind.Crystal, RawKind = raw,
                X = (int)c.Position.X.Value, Y = (int)c.Position.Y.Value,
                // Имя поля опечатано в самом формате: Reource — тип ресурса жилы (Lres.dbf).
                Resource = (int)c.Reource.Value, AiPriority = (int)c.Aipriority.Value,
            },
            KSg.MidSiteMerchant m => Of(id, SiteKind.Merchant, raw, m.Site, m.Visitors) with
            {
                Stock = m.Items.Select(i => new MerchantStock(i.ItemId.String, (int)i.ItemCount.Value)).ToList(),
                Buys = BuysOf(m),
                Mission = m.Mission.Value != 0,
            },
            KSg.MidSiteMercs m => Of(id, SiteKind.Mercenaries, raw, m.Site, m.Visitors) with
            {
                Roster = m.Units.Select(u => new MercenaryOffer(u.UnitId.String, (int)u.UnitLevel.Value,
                                                                u.UnitUniq.Value != 0)).ToList(),
            },
            // Жезл — не посещаемый объект (Sites.Visit его отклоняет), но вид ему нужен: по клетке
            // объекта решается, можно ли поставить рядом свой (Rods.PlaceRefusal), и он источник
            // роста земли. Владелец — игрок, как у города.
            KSg.MidRod r => new MapSite
            {
                Id = id, Kind = SiteKind.Rod, RawKind = raw, Owner = Ref(r.Owner),
                X = (int)r.Position.X.Value, Y = (int)r.Position.Y.Value,
            },
            KSg.MidSiteTrainer t => Of(id, SiteKind.Trainer, raw, t.Site, t.Visitors),
            KSg.MidSiteMage m => Of(id, SiteKind.Mage, raw, m.Site, m.Visitors) with { Spells = Ids(m.Spells) },
            _ => null,
        };
    }

    /// <summary>Столица или деревня: общая часть <c>City</c>.</summary>
    private static MapSite CityOf(string id, SiteKind kind, string raw, KSg.City city) => new()
    {
        Id = id, Kind = kind, RawKind = raw,
        X = (int)city.Position.X.Value, Y = (int)city.Position.Y.Value, Name = city.NameTxt.String.Trim(), Description = city.DescTxt.String.Trim(),
        Owner = Ref(city.Owner), Subrace = Ref(city.Subrace), StackId = Ref(city.Stack), Guards = UnitsOf(city.Group),
        GuardSlots = SlotsOf(city.Group),
    };

    private static MapSite Of(string id, SiteKind kind, string raw, KSg.Site site, IEnumerable<KSg.SiteVisitor> visitors) =>
        new()
        {
            Id = id, Kind = kind, RawKind = raw,
            X = (int)site.Position.X.Value, Y = (int)site.Position.Y.Value,
            Name = site.TxtTitle.String.Trim(), Description = site.TxtDesc.String.Trim(),
            VisitorId = Ref(site.Visiter), Visitors = Ids(visitors.Select(v => v.Visitor)), AiPriority = (int)site.Aipriority.Value,
        };

    private static MerchantBuys BuysOf(KSg.MidSiteMerchant m) => new(
        m.BuyArmor.Value != 0, m.BuyJewel.Value != 0, m.BuyWeapon.Value != 0, m.BuyBanner.Value != 0,
        m.BuyPotion.Value != 0, m.BuyScroll.Value != 0, m.BuyWand.Value != 0, m.BuyValue.Value != 0);

    private static IReadOnlyList<string> UnitsOf(KSg.Group group) =>
        group.Units.Select(u => u.String).Where(u => !IsEmptyId(u)).ToList();

    /// <summary><c>POS_0..5</c> группы; пустая клетка — <c>0xFFFFFFFF</c>, приводится к -1 (как у отряда).</summary>
    private static IReadOnlyList<int> SlotsOf(KSg.Group group) => group.Positions.Select(p => (int)p.Value).ToList();

    private static IReadOnlyList<string> Ids(IEnumerable<KSg.StringRecord> records) =>
        records.Select(r => r.String).Where(r => !IsEmptyId(r)).ToList();

    /// <summary>Казна записи — тем же единственным декодером, что и в <see cref="MapLoader"/>:
    /// запись собирается в строковый вид <c>g0100:r0000:…</c>, валюту определяет БУКВА.
    /// (Декодер там приватный, а <c>MapLoader.cs</c> — чужой файл задачи 4; при их слиянии
    /// эта копия уходит.)</summary>
    private static Bank BankOf(KSg.BankRecord b) => Bank.Parse(string.Join(':',
        new[] { b.Gold, b.InfernalMana, b.LifeMana, b.DeathMana, b.RunicMana, b.GroveMana }
            .Where(c => c is not null)
            .Select(c => (char)c.Type + c.AmountStr)));
}
