using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;
using LastEdict.Rules.Rng;

namespace LastEdict.Tests;

/// <summary>
/// Снимки «порт против порта» (<c>docs/own-version-roadmap.md</c> §3.7, Э3): арбитр правил после отделения от оригинала. Партия без людей
/// с фиксированным зерном играет дни командами <see cref="EndTurnCommand"/>; после каждой — хеш состояния (<see cref="StateHash"/>) и
/// сводка по игрокам; бои — автобой пар отрядов карты на своём генераторе. Строки сверяются с файлом <c>tests/snapshots/&lt;имя&gt;.jsonl</c>:
/// изменение правил даёт расхождение, его принимают осознанно — <c>D2_SNAPSHOTS_ACCEPT=1</c> переписывает файл (diff виден в git).
/// </summary>
internal static class PortSnapshot
{
    /// <summary>Каталог снимков в исходниках (<c>tests/snapshots</c>): сборка тестов знает его из метаданных (<c>SnapshotsDir</c>).</summary>
    public static string Dir { get; } = typeof(PortSnapshot).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "SnapshotsDir").Value!;

    /// <summary>Переписать снимки вместо сверки — после осознанной правки правил.</summary>
    public static bool Accept => Environment.GetEnvironmentVariable("D2_SNAPSHOTS_ACCEPT") == "1";

    /// <summary>Каталог трассы (<c>D2_SNAPSHOTS_TRACE</c>): рядом со строками снимка — сохранение после каждого дня и журнал каждого боя
    /// целиком, чтобы разобрать расхождение хеша по полям (или доказать, что правка поменяла только запись, а не правила).</summary>
    private static string? TraceDir => Environment.GetEnvironmentVariable("D2_SNAPSHOTS_TRACE") is { Length: > 0 } dir ? dir : null;

    private static readonly JsonSerializerOptions Line = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><paramref name="days"/> концов хода партии без людей: строка на команду — день, хеш после неё и, кроме
    /// <paramref name="hashesOnly"/>, сводка игроков. Снимки сценариев оригинала — только хеши: в публичном репозитории Last Edict ни id
    /// объектов, ни чисел его карт (Р3).</summary>
    public static IReadOnlyList<string> AiDays(GameSession session, int days, bool hashesOnly = false, string trace = "")
    {
        ArgumentNullException.ThrowIfNull(session);
        Trace(trace, 0, session);
        var start = new JsonObject { ["start"] = StateHash.Of(session) };
        if (!hashesOnly) start["players"] = Players(session.Map);
        var lines = new List<string> { Json(start) };
        for (int day = 1; day <= days; day++)
        {
            session.Execute(new EndTurnCommand());
            Trace(trace, day, session);
            var line = new JsonObject { ["day"] = day, ["turn"] = session.Map.Turn, ["hash"] = StateHash.Of(session) };
            if (!hashesOnly) line["players"] = Players(session.Map);
            lines.Add(Json(line));
        }
        return lines;
    }

    /// <summary>Сводка игрока для чтения расхождения: золото, отряды, объекты (города, столица) — хеш решает, сводка объясняет.</summary>
    private static JsonObject Players(MapState map)
    {
        var players = new JsonObject();
        foreach (var p in map.Players.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
            players[p.Id] = new JsonObject
            {
                ["gold"] = p.Bank.Gold,
                ["stacks"] = map.Stacks.Values.Count(s => s.Owner.Equals(p.Id, StringComparison.OrdinalIgnoreCase)),
                ["sites"] = map.Sites.Count(s => s.Owner.Equals(p.Id, StringComparison.OrdinalIgnoreCase)),
            };
        return players;
    }

    /// <summary>
    /// Бои автобоем: непустые отряды карты по id парами (первый против второго, третий против четвёртого…), не больше <paramref name="count"/>
    /// боёв — так в бой идут разные юниты, а не все против одного соседа; генератор боя — <see cref="EngineRng"/> с индексом
    /// <paramref name="seed"/> + номер боя. Строка — номер, сторона-победитель, число ходов, хеш журнала ходов и итоговых юнитов и, кроме
    /// <paramref name="hashesOnly"/>, id отрядов пары.
    /// </summary>
    public static IReadOnlyList<string> Battles(MapState map, GameBook book, int count, int seed, bool hashesOnly = false, string trace = "")
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(book);
        var stacks = map.Stacks.Values.Where(s => s.Inside.Length == 0 && s.UnitIds.Count > 0 && s.LeaderAlive)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        var lines = new List<string>();
        for (int i = 0; i + 1 < stacks.Count && lines.Count < count; i += 2)
        {
            var (attacker, defender) = (stacks[i], stacks[i + 1]);
            var state = MapBattle.Build(map, attacker.Id, defender.Id, book, difficulty: 1, attackerMove: 0);
            var rules = book.Rules;
            var battle = new BattleSession(state, new BattleSim(state, new EngineRng(seed + lines.Count), rules.Constants), rules.Constants)
            {
                Auto = true,
            };
            battle.PlayOut();
            var units = state.Units.OrderBy(u => u.Id, StringComparer.Ordinal)
                .Select(u => $"{u.Id}:{u.Type.Id}:{u.Hp}:{u.Xp}:{u.Retreated}:{u.Summoned}:{u.Transformed}");
            string record = JsonSerializer.Serialize(battle.Log) + "\n" + string.Join("\n", units);
            if (TraceDir is { } dir && trace.Length > 0)
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(dir, trace)).FullName, $"battle-{lines.Count + 1:D2}.txt"), record);
            string digest = Hash(record);
            string winner = battle.Winner is not { } w ? "" : w.Equals(attacker.Id, StringComparison.OrdinalIgnoreCase) ? "attacker" : "defender";
            var line = new JsonObject { ["battle"] = lines.Count + 1 };
            if (!hashesOnly)
            {
                line["attacker"] = attacker.Id;
                line["defender"] = defender.Id;
            }
            line["winner"] = winner;
            line["turns"] = battle.Log.Count;
            line["hash"] = digest;
            lines.Add(Json(line));
        }
        return lines;
    }

    /// <summary>Сверить строки со снимком <paramref name="name"/> (или переписать его при <see cref="Accept"/>).</summary>
    public static void Verify(string name, IReadOnlyList<string> lines)
    {
        string path = Path.Combine(Dir, name + ".jsonl");
        string text = string.Join("\n", lines) + "\n";
        if (Accept)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, text);
            return;
        }
        Assert.True(File.Exists(path), $"нет снимка {path}: запишите его прогоном с D2_SNAPSHOTS_ACCEPT=1");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        for (int i = 0; i < Math.Max(expected.Length, lines.Count); i++)
        {
            string want = i < expected.Length ? expected[i] : "(нет строки)";
            string got = i < lines.Count ? lines[i] : "(нет строки)";
            Assert.True(want == got, $"снимок {name}, строка {i + 1} разошлась — правила дали другой результат. Если правка правил " +
                                     $"осознанная, перепишите снимки (D2_SNAPSHOTS_ACCEPT=1) и посмотрите diff.\nбыло:  {want}\nстало: {got}");
        }
    }

    /// <summary>Сохранение партии после дня <paramref name="day"/> в трассу <paramref name="name"/> (если она включена).</summary>
    private static void Trace(string name, int day, GameSession session)
    {
        if (TraceDir is not { } dir || name.Length == 0) return;
        var save = GameSave.ToJson(session.SnapshotWithoutSummaries() with { Summaries = new Dictionary<string, PendingSummary>() }, "", DateTime.UnixEpoch);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(dir, name)).FullName, $"day-{day:D2}.json"), save);
    }

    private static string Json(JsonNode node) => node.ToJsonString(Line);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32].ToLowerInvariant();
}
