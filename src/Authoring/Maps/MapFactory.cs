using System.Globalization;
using System.Text.Json.Nodes;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;

namespace LastEdict.Authoring.Maps;

/// <summary>Место игрока новой карты: раса и (необязательно) лорд; лорда нет — первый лорд расы.</summary>
public sealed record MapSeat(string Race, string? Lord = null);

/// <summary>Что положить на новую карту: сторона (любая, квадрат — Р16), места, зерно, деревни и нейтральные отряды на 64² площади.</summary>
public sealed record NewMapOptions(string Id, int Size, IReadOnlyList<MapSeat> Seats, int Seed = 1, double VillagesPer64 = 2, double NeutralsPer64 = 8);

/// <summary>
/// Новая карта своей версии без потолка площади (Э6, Р6, Р16): квадрат любой стороны, все массивы — по размеру (клетки, туман на игрока,
/// деревья), столицы по кругу у краёв, деревни и нейтральные отряды по площади, лес, озёра и горы от зерна. Сценарий собирается в формате
/// порта (<c>d2-port-scenario</c>) и читается обычным <see cref="ScenarioFile.FromJson"/> — тем же путём, что сценарии импорта и test-pack
/// (<c>tools/test_pack.py</c> — этот же построитель на Python для своих карт CI). Юниты — из содержимого: страж и лидеры расы, нейтралы.
/// </summary>
public static class MapFactory
{
    /// <summary>Размер клетки столицы и деревни.</summary>
    private const int CapitalSide = 5, VillageSide = 4;

    public static ScenarioData Create(ContentSet content, NewMapOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Size < 24) throw new ArgumentException($"сторона карты {options.Size} — меньше 24 клеток столицы не помещаются", nameof(options));
        if (options.Seats.Count == 0) throw new ArgumentException("нет ни одного игрока", nameof(options));
        return ScenarioFile.FromJson(new Builder(content, options).Build().ToJsonString());
    }

    private sealed class Builder(ContentSet content, NewMapOptions o)
    {
        private readonly Random _rnd = new(o.Seed);
        private readonly string _p = $"S{900 + (Math.Abs(o.Id.GetHashCode(StringComparison.Ordinal)) % 99):D3}";
        private readonly JsonObject _units = [], _stacks = [], _objects = [];
        private readonly Dictionary<(int, int), List<string>> _plan = [];
        private readonly JsonArray _sites = [], _forts = [], _looksStacks = [];
        private readonly Dictionary<string, int> _next = new(StringComparer.Ordinal);
        private readonly byte[] _tiles = new byte[o.Size * o.Size];
        private readonly HashSet<(int, int)> _taken = [];
        private int N => o.Size;

        private string Id(string kind)
        {
            int n = _next.GetValueOrDefault(kind);
            _next[kind] = n + 1;
            return $"{_p}{kind}{n:x4}";
        }

        private void Put(int x, int y, int ground, int land)
        {
            if (x >= 0 && y >= 0 && x < N && y < N) _tiles[(y * N) + x] = (byte)(land | (ground << 3));
        }

        private RaceDef Race(string id) => content.Find(id) as RaceDef ?? throw new ArgumentException($"нет расы «{id}» в содержимом");

        private static int Land(RaceDef race) => (int)(race.Land ?? TerrainKind.Neutral);

        private static int Subrace(RaceDef race) => race.Kind switch
        {
            RaceKind.Human => 1, RaceKind.Undead => 2, RaceKind.Heretic => 3, RaceKind.Dwarf => 4, RaceKind.Elf => 14, _ => 5,
        };

        private string Unit(string type)
        {
            string uid = Id("UN");
            int hp = content.Find(type) is UnitDef u ? u.HitPoints : 1;
            _units[uid] = new JsonObject
            {
                ["Id"] = uid, ["TypeId"] = type, ["Level"] = 1, ["Hp"] = hp, ["Xp"] = 0, ["Modifiers"] = new JsonArray(), ["DynLevel"] = false,
            };
            return uid;
        }

        private void Cover(int x, int y, int w, string id)
        {
            for (int yy = y; yy < y + w; yy++)
                for (int xx = x; xx < x + w; xx++)
                {
                    (_plan.TryGetValue((xx, yy), out var list) ? list : _plan[(xx, yy)] = []).Add(id);
                    _taken.Add((xx, yy));
                }
        }

        private string Stack(string owner, string subrace, IReadOnlyList<string> types, int x, int y, string inside = "", int order = 2)
        {
            string sid = Id("KC");
            var ids = types.Select(Unit).ToList();
            int[] cells = [2, 0, 4, 3, 1, 5];
            var slots = new int[6];
            Array.Fill(slots, -1);
            for (int i = 0; i < ids.Count && i < 6; i++) slots[cells[i]] = i;
            _stacks[sid] = new JsonObject
            {
                ["Id"] = sid, ["Owner"] = owner, ["Subrace"] = subrace, ["LeaderId"] = ids[0], ["LeaderAlive"] = true, ["X"] = x, ["Y"] = y,
                ["Move"] = 20, ["Inside"] = inside, ["UnitIds"] = Arr(ids), ["Slots"] = Arr(slots), ["Items"] = new JsonArray(), ["TemplateId"] = "",
                ["Equipment"] = Arr(Enumerable.Repeat("", 7)), ["Order"] = order, ["OrderTarget"] = "", ["AiOrder"] = 1, ["AiOrderTarget"] = "",
                ["AiIgnore"] = false, ["UpgradeCount"] = 0, ["BattlesWon"] = 0, ["CreationLevel"] = 1, ["AiPriority"] = 3,
            };
            _objects[sid] = new JsonObject { ["Id"] = sid, ["Kind"] = "KC", ["X"] = x, ["Y"] = y, ["Resource"] = 0, ["TypeId"] = "" };
            (_plan.TryGetValue((x, y), out var list) ? list : _plan[(x, y)] = []).Add(sid);
            _taken.Add((x, y));
            _looksStacks.Add(new JsonObject
            {
                ["Id"] = sid, ["X"] = x, ["Y"] = y, ["Owner"] = owner, ["Subrace"] = subrace, ["LeaderType"] = types[0], ["LeaderAlive"] = true,
                ["AnyAlive"] = true, ["Facing"] = 0, ["Inside"] = inside, ["Invisible"] = false, ["LeaderName"] = "",
            });
            return sid;
        }

        private void Fort(string owner, string subrace, int x, int y, bool capital, string name, IReadOnlyList<string> guards, IReadOnlyList<string>? hero)
        {
            string fid = Id("FT");
            int w = capital ? CapitalSide : VillageSide;
            _objects[fid] = new JsonObject { ["Id"] = fid, ["Kind"] = "FT", ["X"] = x, ["Y"] = y, ["Resource"] = 0, ["TypeId"] = "" };
            Cover(x, y, w, fid);
            string visitor = hero is { Count: > 0 } ? Stack(owner, subrace, hero, x, y, inside: fid, order: 1) : "";
            var guardIds = guards.Select(Unit).ToList();
            var slots = new int[6];
            Array.Fill(slots, -1);
            int[] cells = [2, 0, 4];
            for (int i = 0; i < guardIds.Count && i < 3; i++) slots[cells[i]] = i;
            var zero = new JsonObject { ["Gold"] = 0, ["Infernal"] = 0, ["Life"] = 0, ["Death"] = 0, ["Runic"] = 0, ["Grove"] = 0 };
            var site = new JsonObject
            {
                ["Id"] = fid, ["Kind"] = capital ? "Capital" : "Village", ["RawKind"] = capital ? "Capital" : "MidVillage", ["X"] = x, ["Y"] = y,
                ["Name"] = name, ["Description"] = "", ["Owner"] = owner, ["Cash"] = zero, ["ItemId"] = "", ["LooterId"] = "", ["Items"] = new JsonArray(),
                ["Guards"] = Arr(guardIds), ["GuardSlots"] = Arr(slots), ["Resource"] = 0, ["VisitorId"] = "", ["Gone"] = false, ["StackId"] = visitor,
                ["Subrace"] = subrace, ["Size"] = null, ["GrowthTurn"] = null, ["RiotTurn"] = null, ["RegenBonus"] = null, ["ProtectBonus"] = "",
                ["Morale"] = null, ["Visitors"] = new JsonArray(), ["Stock"] = new JsonArray(), ["Buys"] = null, ["Mission"] = false,
                ["Roster"] = new JsonArray(), ["Spells"] = new JsonArray(), ["AiPriority"] = 3,
            };
            if (!capital)
            {
                site["Size"] = 1;
                site["GrowthTurn"] = 0;
                site["RiotTurn"] = 0;
                site["RegenBonus"] = 0;
                site["Morale"] = 0;
            }
            _sites.Add(site);
            _forts.Add(new JsonObject
            {
                ["Id"] = fid, ["Capital"] = capital, ["X"] = x, ["Y"] = y, ["Owner"] = owner, ["Level"] = capital ? 0 : 1, ["Riot"] = false,
                ["Garrison"] = visitor,
            });
        }

        private bool Free(int x, int y, int w, int margin) =>
            x - margin >= 0 && y - margin >= 0 && x + w + margin <= N && y + w + margin <= N
            && Enumerable.Range(x - margin, w + (2 * margin)).All(xx => Enumerable.Range(y - margin, w + (2 * margin)).All(yy => !_taken.Contains((xx, yy))));

        public JsonObject Build()
        {
            var neutral = content.Races.FirstOrDefault(r => r.Neutral == true) ?? throw new InvalidDataException("в содержимом нет расы нейтралов");
            var seats = o.Seats.Select(s => Race(s.Race)).ToList();
            var all = new List<RaceDef> { neutral };
            all.AddRange(seats);
            var players = all.Select((_, i) => Id("PL")).ToList();
            var subraces = all.Select((_, i) => $"{_p}SR{i:x4}").ToList();
            Array.Fill(_tiles, (byte)Land(neutral));
            double area = N * N / (64.0 * 64.0);
            // Лес, озёра и горы — от зерна, плотность — на площадь.
            for (int i = 0, n = Math.Max(1, (int)(N * N / 600.0)); i < n; i++)
            {
                int cx = _rnd.Next(N), cy = _rnd.Next(N), w = 2 + _rnd.Next(4), h = 1 + _rnd.Next(3);
                int ground = _rnd.Next(10) switch { < 6 => 1, < 8 => 3, _ => 4 };
                for (int dx = -w; dx <= w; dx++)
                    for (int dy = -h; dy <= h; dy++) Put(cx + dx, cy + dy, ground, Land(neutral));
            }
            // Столицы — по кругу у краёв, вокруг — своя земля.
            for (int s = 0; s < seats.Count; s++)
            {
                double angle = (2 * Math.PI * s / seats.Count) + (Math.PI / 4);
                int radius = (N / 2) - 10;
                int x = Math.Clamp((N / 2) + (int)Math.Round(radius * Math.Cos(angle)) - 2, 4, N - CapitalSide - 4);
                int y = Math.Clamp((N / 2) + (int)Math.Round(radius * Math.Sin(angle)) - 2, 4, N - CapitalSide - 4);
                for (int yy = y - 3; yy < y + CapitalSide + 3; yy++)
                    for (int xx = x - 3; xx < x + CapitalSide + 3; xx++) Put(xx, yy, 0, Land(seats[s]));
                var race = seats[s];
                var hero = race.Leaders.Take(1).Concat(race.Soldiers.Take(2)).ToList();
                Fort(players[s + 1], subraces[s + 1], x, y, true, $"{NameOf(race)} {s + 1}", race.Guardian is { } g ? [g] : [], hero.Count > 0 ? hero : null);
            }
            var wild = content.Units.Where(u => u.Race == neutral.Id && u.Level <= 2 && u.HitPoints > 0).Select(u => u.Id).Take(12).ToList();
            if (wild.Count == 0) wild = [.. content.Units.Where(u => u.HitPoints > 0).Select(u => u.Id).Take(4)];
            int villages = (int)Math.Round(o.VillagesPer64 * area), neutrals = (int)Math.Round(o.NeutralsPer64 * area);
            for (int i = 0, tries = 0; i < villages && tries < villages * 50; tries++)
            {
                int x = _rnd.Next(2, N - VillageSide - 2), y = _rnd.Next(2, N - VillageSide - 2);
                if (!Free(x, y, VillageSide, 1)) continue;
                for (int xx = x; xx < x + VillageSide; xx++)
                    for (int yy = y; yy < y + VillageSide; yy++) Put(xx, yy, 0, Land(neutral));
                Fort(players[0], subraces[0], x, y, false, $"Hamlet {i + 1}", [.. wild.Take(2)], null);
                i++;
            }
            for (int i = 0, tries = 0; i < neutrals && tries < neutrals * 50; tries++)
            {
                int x = _rnd.Next(1, N - 1), y = _rnd.Next(1, N - 1);
                int ground = _tiles[(y * N) + x] >> 3;
                if (_taken.Contains((x, y)) || ground is not (0 or 1)) continue;
                int count = 1 + _rnd.Next(Math.Min(4, wild.Count));
                Stack(players[0], subraces[0], [.. Enumerable.Range(0, count).Select(k => wild[(i + k) % wild.Count])], x, y);
                i++;
            }
            var playerNodes = new JsonObject();
            for (int i = 0; i < all.Count; i++)
            {
                var race = all[i];
                string lord = i > 0 && o.Seats[i - 1].Lord is { } chosen ? chosen
                    : content.Lords.FirstOrDefault(l => l.Race == race.Id)?.Id ?? content.Lords.FirstOrDefault()?.Id ?? "";
                var bank = new JsonObject { ["Gold"] = i == 0 ? 0 : 600, ["Infernal"] = 0, ["Life"] = 0, ["Death"] = 0, ["Runic"] = 0, ["Grove"] = 0 };
                playerNodes[players[i]] = new JsonObject
                {
                    ["Id"] = players[i], ["RaceId"] = race.Id, ["LordId"] = lord, ["Name"] = i == 0 ? "Neutrals" : $"{NameOf(race)} {i}", ["Face"] = 0,
                    ["IsHuman"] = i == 1, ["Bank"] = bank, ["FogId"] = $"{_p}FG{i:x4}", ["BuildingsId"] = $"{_p}PB{i:x4}", ["Buildings"] = new JsonArray(),
                    ["SpellBank"] = (JsonNode)bank.DeepClone(), ["QtyBreaks"] = 0, ["Attitude"] = 1, ["CapturedBy"] = "", ["ConstructionTurn"] = 0,
                    ["ResearchTurn"] = 0, ["KnownSpells"] = new JsonArray(), ["Spied"] = new JsonArray(), ["Party"] = 20 + i,
                };
            }
            var relations = new JsonArray();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    relations.Add(new JsonObject { ["Race1"] = 20 + i, ["Race2"] = 20 + j, ["Raw"] = 0 });
            var fog = new JsonObject();
            for (int i = 0; i < all.Count; i++) fog[$"{_p}FG{i:x4}"] = Grid(new byte[N * N]);
            var subraceNodes = new JsonObject();
            for (int i = 0; i < all.Count; i++) subraceNodes[subraces[i]] = Subrace(all[i]);
            var map = new JsonObject
            {
                ["Size"] = N, ["Tiles"] = Grid(_tiles), ["Stacks"] = _stacks, ["Units"] = _units, ["Players"] = playerNodes, ["Objects"] = _objects,
                ["Plan"] = new JsonArray([.. _plan.OrderBy(p => p.Key.Item2).ThenBy(p => p.Key.Item1)
                    .Select(p => (JsonNode)new JsonObject { ["X"] = p.Key.Item1, ["Y"] = p.Key.Item2, ["Ids"] = Arr(p.Value) })]),
                ["Fog"] = fog, ["Turn"] = 0, ["Rods"] = new JsonObject(), ["Sites"] = _sites, ["Relations"] = relations,
                ["TalismanCharges"] = new JsonObject(), ["SpellCasts"] = new JsonArray(), ["ItemCasts"] = new JsonArray(), ["SpellEffects"] = new JsonArray(),
                ["ItemTypes"] = new JsonObject(),
                ["Limits"] = new JsonObject
                {
                    ["MaxUnit"] = 5, ["MaxSpell"] = 5, ["MaxLeader"] = 20, ["MaxCity"] = 5, ["DifficultyScenario"] = 2, ["DifficultyGame"] = 1,
                    ["SuggestedLevel"] = 1,
                },
                ["Subraces"] = subraceNodes, ["Destroyed"] = new JsonArray(), ["Tombs"] = new JsonArray(), ["MapSeed"] = (uint)o.Seed * 7919u,
            };
            var banners = new JsonObject();
            for (int i = 0; i < subraces.Count; i++) banners[subraces[i]] = i;
            var looks = new JsonObject
            {
                ["Map"] = map, ["TreeImage"] = Grid(new byte[N * N]), ["TreeGroup"] = Grid(new byte[N * N]), ["Forts"] = _forts, ["Ruins"] = new JsonArray(),
                ["Sites"] = new JsonArray(), ["Crystals"] = new JsonArray(), ["Bags"] = new JsonArray(), ["Tombs"] = new JsonArray(), ["Rods"] = new JsonArray(),
                ["Landmarks"] = new JsonArray(), ["Mountains"] = new JsonArray(), ["Roads"] = new JsonArray(), ["Stacks"] = _looksStacks, ["Banners"] = banners,
            };
            var owners = new JsonObject();
            for (int i = 0; i < all.Count; i++) owners[subraces[i]] = players[i];
            var events = new JsonObject
            {
                ["Events"] = new JsonArray(), ["Zones"] = new JsonObject(), ["Variables"] = new JsonObject(), ["Templates"] = new JsonObject(),
                ["SubraceOwners"] = owners,
            };
            return new JsonObject
            {
                ["Format"] = ScenarioFile.Format, ["Version"] = ScenarioFile.Version, ["Id"] = o.Id, ["Looks"] = looks, ["Events"] = events,
            };
        }

        private static string NameOf(RaceDef race) => race.Id[(race.Id.LastIndexOf('/') + 1)..];

        private JsonObject Grid(byte[] data) => new() { ["w"] = N, ["h"] = N, ["data"] = Convert.ToBase64String(data) };

        private static JsonArray Arr<T>(IEnumerable<T> values) => new([.. values.Select(v => (JsonNode)JsonValue.Create(v)!)]);
    }
}
