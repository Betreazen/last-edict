using D2.Data.Sg;
using D2.Rules.Map;
using Kaitai;
using KSg = Kaitai.Sg;

namespace D2.Data.Map;

/// <summary>
/// Сценарий или сохранение (формат один и тот же) -> <see cref="MapState"/>. Читает только то, что
/// в файле лежит: террейн, отряды, юниты, игроков, план занятости, туман, номер дня. Ничего не
/// достраивает: чего в <c>.sg</c> нет, того нет и в <see cref="MapState"/>.
/// </summary>
public static class MapLoader
{
    private const int BlockWidth = 8;   // ранг 1, см. TilesOf

    /// <summary>
    /// Пустая ссылка в файле — не пустая строка, а десять знаков <c>G000000000</c>:
    /// строковый вид нулевого <c>CMidgardID</c> (ранг 1 — встречается в слотах отряда любого
    /// сценария; ранг 2 — <c>const CMidgardID emptyId{0u}</c>, D2RSG <c>rsgid.cpp:32</c>).
    /// Такие ссылки приводятся к пустой строке, чтобы "нет ссылки" проверялось одним способом.
    /// </summary>
    private const string EmptyId = "G000000000";

    private static bool IsEmptyId(string id) =>
        id.Length == 0 || id.Equals(EmptyId, StringComparison.OrdinalIgnoreCase);

    private static string Ref(KSg.StringRecord record) =>
        IsEmptyId(record.String) ? "" : record.String;

    /// <summary>
    /// Читает карту. Любой отказ разбора (обрезанный файл, чужой формат, отсутствующая запись)
    /// приводится к <see cref="InvalidDataException"/> с именем файла: без этого битый
    /// <c>.sg</c> падал как <c>Sequence contains no elements</c> из недр LINQ и не говорил, какой
    /// файл не прочитался. Исходное исключение остаётся в <c>InnerException</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">Файл есть, но не читается как <c>.sg</c>.</exception>
    public static MapState Load(string sgPath) => Load(sgPath, null);

    /// <summary>То же по уже разобранному файлу (<paramref name="file"/> не null) — чтобы не разбирать <c>.sg</c> дважды.</summary>
    internal static MapState Load(string sgPath, SgFile? file)
    {
        try
        {
            return LoadCore((file ?? SgFile.Open(sgPath)).Raw.Objects);
        }
        catch (Exception e) when (e is not (InvalidDataException or FileNotFoundException
                                            or DirectoryNotFoundException))
        {
            throw new InvalidDataException($"{sgPath}: не читается как .sg", e);
        }
    }

    private static MapState LoadCore(List<KSg.ScenarioObject> objects)
    {

        var info = objects.Select(o => o.Data).OfType<KSg.ScenarioInfo>().Single();
        // Сторона карты лежит в двух местах: CMidgardMap (в .ksy поле названо unknown) и
        // ScenarioInfo.MAP_SIZE. Берём первое — так же делает питоновский читатель
        // (tools/sgfile.py:326); совпадение с ScenarioInfo.MapSize проверяет тест.
        int size = (int)objects.Select(o => o.Data).OfType<KSg.MidgardMap>().Single().Unknown.Value;
        var data = objects.Select(o => o.Data).ToList();

        return new MapState
        {
            Size = size,
            Tiles = TilesOf(objects, size),
            Stacks = objects.Select(o => o.Data).OfType<KSg.MidStack>()
                .ToDictionary(s => s.StackId.String, StackOf, StringComparer.OrdinalIgnoreCase),
            Units = objects.Select(o => o.Data).OfType<KSg.MidUnit>()
                .ToDictionary(u => u.UnitId.String, UnitOf, StringComparer.OrdinalIgnoreCase),
            Players = PlayersOf(objects),
            Objects = ObjectsOf(objects),
            Plan = PlanOf(objects),
            Fog = FogOf(objects, size),
            Turn = (int)info.CurTurn.Value,
            Rods = data.OfType<KSg.MidRod>().ToDictionary(r => r.RodId.String, RodOf, StringComparer.OrdinalIgnoreCase),
            Sites = objects.Select(SiteLoader.SiteOf).OfType<MapSite>().ToList(),
            Relations = RelationsOf(data),
            TalismanCharges = ChargesOf(data),
            SpellCasts = CastsOf(data, c => c.Items),
            ItemCasts = CastsOf(data, c => c.ItemCasts),
            SpellEffects = [.. data.OfType<KSg.MidSpellEffects>().SelectMany(e => e.Items)
                .Select(e => new SpellEffectRecord(e.UnitId.String, e.OriginId.String, e.ModifId.String, Ref(e.CasterId),
                                                   (int)e.TurnStop.Value))],
            ItemTypes = data.OfType<KSg.MidItem>()
                .ToDictionary(i => i.ItemId.String, i => i.ItemType.String, StringComparer.OrdinalIgnoreCase),
            Limits = LimitsOf(info),
            MapSeed = info.MapSeed.Value,
            Destroyed = [.. data.OfType<KSg.MidStackDestroyed>().SelectMany(d => d.Stacks)
                .Select(e => new DestroyedStack(Ref(e.IdStack), Ref(e.IdKiller), Ref(e.SrctmplId)))],
            Tombs = [.. data.OfType<KSg.MidTomb>().Select(t => new MapTomb(t.TombId.String, (int)t.Position.X.Value, (int)t.Position.Y.Value,
                [.. t.Entries.Select(e => new TombEntry(Ref(e.StackOwner), Ref(e.Killer), (int)e.Turn.Value, e.StackName.String))]))],
            Subraces = data.OfType<KSg.MidSubrace>()
                .ToDictionary(s => s.SubraceId.String, s => (int)s.Subrace.Value, StringComparer.OrdinalIgnoreCase),
            IdNext = IdNextOf(objects.Select(o => o.ObjId.String)),
        };
    }

    /// <summary>Счётчики id после загрузки (<c>0x5ee996</c>): по каждому объекту карты — счётчик его класса не меньше номера + 1.</summary>
    internal static Dictionary<string, int> IdNextOf(IEnumerable<string> ids)
    {
        var next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in ids)
        {
            if (id.Length != 10 || !int.TryParse(id.AsSpan(6), System.Globalization.NumberStyles.HexNumber,
                                                 System.Globalization.CultureInfo.InvariantCulture, out int n)) continue;
            string kind = id.Substring(4, 2).ToUpperInvariant();
            if (next.GetValueOrDefault(kind) <= n) next[kind] = n + 1;
        }
        return next;
    }

    /// <summary>
    /// Террейн. Блок — 8 клеток в ширину и 4 в высоту; начало блока закодировано в его id
    /// <c>Sxxx<b>MB</b>&lt;yy&gt;&lt;xx&gt;</c> шестнадцатерично. Ранг 1, два независимых источника:
    /// <c>tools/sgfile.py:322-336</c> (проверено на 33 картах) и <c>CMidgardMapBlock</c> движка
    /// (<c>D2ModdingToolset include/midgardmapblock.h:37-50</c>, <c>uint32 tiles[32]</c> = 8x4).
    /// </summary>
    private static Tile[,] TilesOf(IReadOnlyList<KSg.ScenarioObject> objects, int size)
    {
        var tiles = new Tile[size, size];
        foreach (var block in objects.Select(o => o.Data).OfType<KSg.MidgardMapBlock>())
        {
            string id = block.Blockid.String;
            int by = Convert.ToInt32(id.Substring(6, 2), 16);
            int bx = Convert.ToInt32(id.Substring(8, 2), 16);
            for (int i = 0; i < block.Tiles.Count; i++)
            {
                int x = bx + i % BlockWidth, y = by + i / BlockWidth;
                if (x < size && y < size)
                    tiles[x, y] = new Tile(block.Tiles[i].Type);
            }
        }
        return tiles;
    }

    private static MapStack StackOf(KSg.MidStack s) => new()
    {
        Id = s.StackId.String,
        Owner = s.Owner.String,
        Subrace = s.Subrace.String,
        LeaderId = Ref(s.LeaderId),
        LeaderAlive = s.LeaderAliv.Value != 0,
        X = (int)s.Position.X.Value,
        Y = (int)s.Position.Y.Value,
        Move = (int)s.Move.Value,
        Inside = Ref(s.Inside),
        UnitIds = s.Group.Units.Select(u => u.String).Where(id => !IsEmptyId(id)).ToList(),
        // POS_k — КЛЕТКА k, в ней индекс юнита в UnitIds (-1 — клетка пуста), см. MapStack.Slots.
        // Пустая клетка в файле — 0xFFFFFFFF; IntRecord.Value имеет тип uint, поэтому приводим
        // к int до всякого сравнения, иначе -1 превращается в 4294967295.
        Slots = s.Group.Positions.Select(p => (int)p.Value).ToList(),
        Items = Ids(s.Items),
        Equipment = new[] { s.Banner, s.Tome, s.Battle1, s.Battle2, s.Artifact1, s.Artifact2, s.Boots }
            .Select(Ref).ToList(),
        Order = (int)s.Order.Value,
        OrderTarget = Ref(s.OrderTarg),
        AiOrder = (int)s.Aiorder.Value,
        AiOrderTarget = Ref(s.Aiordertar),
        AiIgnore = s.AiIgnore.Value != 0,
        UpgradeCount = (int)s.Upgcount.Value,
        BattlesWon = (int)s.Nbbattle.Value,
        CreationLevel = (int)s.CreatLvl.Value,
        TemplateId = Ref(s.SrctmplId),
        AiPriority = (int)s.Aipriority.Value,
    };

    private static MapUnit UnitOf(KSg.MidUnit u) => new()
    {
        Id = u.UnitId.String,
        TypeId = u.Type.String,
        Level = (int)u.Level.Value,
        Hp = (int)u.Hp.Value,
        Xp = (int)u.Xp.Value,
        Creation = (int)u.Creation.Value,
        // Цепочка без фильтра пустых ссылок: в том же виде её берёт SaveSnapshots (харнессы С1/С2).
        Modifiers = u.Modifiers.Select(m => m.String).ToList(),
        // DYNLEVEL есть только в файлах RotE и версии 35 (sg.ksy mid_unit, if:), иначе поля нет.
        DynLevel = u.Dynlevel is { Value: not 0 },
    };

    private static IReadOnlyDictionary<string, MapPlayer> PlayersOf(
        IReadOnlyList<KSg.ScenarioObject> objects)
    {
        var buildings = ListsById<KSg.PlayerBuildings>(objects, b => b.Buildings);
        var known = ListsById<KSg.PlayerKnownSpells>(objects, k => k.Spells);
        return objects.Select(o => o.Data).OfType<KSg.MidPlayer>().ToDictionary(
            p => p.PlayerId.String,
            p => new MapPlayer
            {
                Id = p.PlayerId.String,
                RaceId = p.RaceId.String,
                LordId = p.LordId.String,
                Name = p.NameTxt.String,
                Face = (int)p.Face.Value,
                IsHuman = p.IsHuman.Value != 0,
                Bank = BankOf(p.Bank),
                FogId = p.FogId.String,
                BuildingsId = p.BuildsId.String,
                Buildings = buildings.GetValueOrDefault(p.BuildsId.String, []),
                SpellBank = BankOf(p.SpellBank),
                QtyBreaks = (int)p.QtyBreaks.Value,
                Attitude = (int)p.Attitude.Value,
                CapturedBy = Ref(p.CaptBy),
                KnownSpells = KnownSpellOrder.Sort(known.GetValueOrDefault(p.KnownId.String, [])),
                Spied = [.. p.Spies.Select(Ref).Where(id => id.Length > 0)],
                MapExchanges = Exchanges(p),
                ConstructionTurn = (int)p.ConstrT.Value,
                ResearchTurn = (int)p.ResearT.Value,
            },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Память обмена картами (<c>EXMAPID1..3</c>/<c>EXMAPTURN1..3</c>, только в сохранениях — подпись <c>D2EESFISIG</c>):
    /// непустые записи цель → день; нет ни одной — null (<see cref="MapPlayer.MapExchanges"/>).</summary>
    private static Dictionary<string, int>? Exchanges(KSg.MidPlayer p)
    {
        var records = (p.ExmapRecords ?? []).Select(r => (Id: Ref(r.Id), Turn: (int)r.Turn.Value)).Where(r => r.Id.Length > 0).ToList();
        return records.Count == 0 ? null
            : records.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                     .ToDictionary(g => g.Key, g => g.Max(r => r.Turn), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Объекты-списки, на которые игрок ссылается по id (<c>BUILDS_ID</c>, <c>KNOWN_ID</c>):
    /// id объекта → непустые ссылки списка.</summary>
    private static Dictionary<string, IReadOnlyList<string>> ListsById<T>(
        IReadOnlyList<KSg.ScenarioObject> objects, Func<T, IEnumerable<KSg.StringRecord>> list) where T : KaitaiStruct =>
        objects.Where(o => o.Data is T).ToDictionary(o => o.ObjId.String, o => Ids(list((T)o.Data)),
                                                     StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Ids(IEnumerable<KSg.StringRecord> records) =>
        records.Select(r => r.String).Where(id => !IsEmptyId(id)).ToList();

    private static MapRod RodOf(KSg.MidRod r) =>
        new(r.RodId.String, Ref(r.Owner), (int)r.Position.X.Value, (int)r.Position.Y.Value);

    /// <summary>Записи <c>CMidDiplomacy</c> — по одной на пару рас (<see cref="Relation"/>).</summary>
    private static IReadOnlyList<Relation> RelationsOf(IEnumerable<KaitaiStruct> data) =>
        data.OfType<KSg.MidDiplomacy>().SelectMany(d => d.Entries)
            .Select(e => new Relation((int)e.Race1.Value, (int)e.Race2.Value, e.Relation.Value)).ToList();

    /// <summary><c>CMidTalismanCharges</c>: <c>ID_TALIS</c> → <c>CHARGES</c>. Повтор id — исключение
    /// из <c>ToDictionary</c>: два остатка у одного талисмана молча схлопывать нельзя.</summary>
    private static IReadOnlyDictionary<string, int> ChargesOf(IEnumerable<KaitaiStruct> data) =>
        data.OfType<KSg.MidTalismanCharges>().SelectMany(c => c.Charges)
            .ToDictionary(c => c.IdTalis.String, c => (int)c.Charges.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<CastRecord> CastsOf(IEnumerable<KaitaiStruct> data,
                                                      Func<KSg.MidSpellCast, IEnumerable<KSg.MidSpellCastItem>> list) =>
        data.OfType<KSg.MidSpellCast>().SelectMany(list)
            .Select(c => new CastRecord(c.PlayerId.String, c.Origin.String, (int)c.TurnStop.Value)).ToList();

    private static ScenarioLimits LimitsOf(KSg.ScenarioInfo i) => new(
        (int)i.MaxUnit.Value, (int)i.MaxSpell.Value, (int)i.MaxLeader.Value, (int)i.MaxCity.Value,
        (int)i.Diffscen.Value, (int)i.Diffgame.Value, (int)i.SuggLvl.Value);

    /// <summary>
    /// Казна. Декодер один на весь проект: запись собирается обратно в строковый вид
    /// <c>g0100:r0000:...</c> и разбирается <see cref="Bank.Parse"/> — там валюту определяет
    /// БУКВА, а не порядок полей. Раньше здесь был второй декодер, по позиции: два декодера
    /// одного формата расходятся молча.
    /// Шестая валюта (<c>:b</c>) есть только в файлах Rise of the Elves (сигнатура
    /// <c>D2EESFISIG</c>); в остальных Kaitai оставляет поле пустым, и части в строке нет.
    /// </summary>
    private static Bank BankOf(KSg.BankRecord b) => Bank.Parse(string.Join(':',
        new[] { b.Gold, b.InfernalMana, b.LifeMana, b.DeathMana, b.RunicMana, b.GroveMana }
            .Where(c => c is not null)
            .Select(c => (char)c.Type + c.AmountStr)));

    /// <summary>
    /// Объекты с координатой. Класс объекта — две буквы его id (<c>S001<b>KC</b>0007</c>);
    /// позиция у разных типов лежит по-разному: у города и столицы внутри <c>City</c>,
    /// у лавок, наставников и магов — внутри <c>Site</c>, у остальных прямо в объекте.
    /// </summary>
    private static IReadOnlyDictionary<string, MapObject> ObjectsOf(
        IReadOnlyList<KSg.ScenarioObject> objects)
    {
        var result = new Dictionary<string, MapObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects)
        {
            var position = PositionOf(o.Data);
            if (position is null)
                continue;
            string id = o.ObjId.String;
            result[id] = new MapObject
            {
                Id = id,
                Kind = id.Length >= 6 ? id.Substring(4, 2) : "",
                X = position.Value.X,
                Y = position.Value.Y,
                // Имя поля опечатано в самом формате (см. Global Constraints плана Ф5):
                // MidCrystal.Reource — это тип ресурса жилы, номер строки Lres.dbf.
                Resource = o.Data is KSg.MidCrystal crystal ? (int)crystal.Reource.Value : 0,
                TypeId = o.Data is KSg.MidLandmark landmark ? landmark.Type.String : "",
            };
        }
        return result;
    }

    private static (int X, int Y)? PositionOf(KaitaiStruct data) => data switch
    {
        KSg.Capital c => At(c.City.Position),
        KSg.MidVillage v => At(v.City.Position),
        KSg.MidSiteMage m => At(m.Site.Position),
        KSg.MidSiteMerchant m => At(m.Site.Position),
        KSg.MidSiteMercs m => At(m.Site.Position),
        KSg.MidSiteTrainer m => At(m.Site.Position),
        KSg.MidStack s => At(s.Position),
        KSg.MidRoad r => At(r.Position),
        KSg.MidRuin r => At(r.Position),
        KSg.MidBag b => At(b.Position),
        KSg.MidCrystal c => At(c.Position),
        KSg.MidLandmark l => At(l.Position),
        KSg.MidLocation l => At(l.Position),
        KSg.MidRod r => At(r.Position),
        KSg.MidTomb t => At(t.Position),
        _ => null,
    };

    private static (int X, int Y) At(KSg.Position p) => ((int)p.X.Value, (int)p.Y.Value);

    private static IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> PlanOf(
        IReadOnlyList<KSg.ScenarioObject> objects)
    {
        var plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>();
        foreach (var entry in objects.Select(o => o.Data).OfType<KSg.MidgardPlan>()
                     .SelectMany(p => p.Entries))
        {
            var key = At(entry.Position);
            if (!plan.TryGetValue(key, out var list))
                plan[key] = list = new List<string>();
            ((List<string>)list).Add(entry.Element.String);
        }
        return plan;
    }

    /// <summary>
    /// Туман по объектам <c>CMidgardMapFog</c>: запись на строку (<c>POS_Y</c>), в ней
    /// <c>FOG</c> байт маски, бит на клетку, МЛАДШИЙ бит группы из восьми — меньший X.
    /// <c>true</c> — клетка ещё скрыта (ранг 1: в сохранении бит столицы самого игрока равен 0,
    /// бит столицы противника — 1; про порядок бит внутри байта это измерение не говорит
    /// ничего, порядок проверяется связностью открытой области — см. тест
    /// <c>Load_SaveGameFogOpenAreaIsConnected</c>).
    /// </summary>
    private static IReadOnlyDictionary<string, bool[,]> FogOf(
        IReadOnlyList<KSg.ScenarioObject> objects, int size)
    {
        var fogs = new Dictionary<string, bool[,]>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects)
        {
            if (o.Data is not KSg.MidgardMapFog data)
                continue;
            var mask = new bool[size, size];
            foreach (var entry in data.Entries)
            {
                int y = (int)entry.PosY.Value;
                if (y >= size)
                    continue;
                // Kaitai читает маску по одному биту старшим вперёд, а в файле младший бит группы
                // из восьми — это МЕНЬШИЙ X: элемент i приходится на столбец (i & ~7) | (7 - i % 8).
                // Ранг 1: при прямом чтении открытая область каждого игрока рвётся ровно на границах
                // байта (у человека оторванный столбец x=39 и дыра x=23, у ИИ островок x∈[12,15]),
                // после инверсии обе маски — одно связное пятно вокруг своей столицы; переходов
                // открыто/скрыто 74 против 118 и 44 против 84. Счётчик «скрыто N из M» к этой
                // перестановке слеп, поэтому проверка в тестах — на связность, а не на число.
                for (int i = 0; i < entry.FogMask.Count; i++)
                {
                    int x = (i & ~7) | (7 - i % 8);
                    if (x < size) mask[x, y] = entry.FogMask[i];
                }
            }
            fogs[o.ObjId.String] = mask;
        }
        return fogs;
    }
}
