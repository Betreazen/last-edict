using System.Text.Json;
using System.Text.Json.Nodes;

namespace LastEdict.Relay;

/// <summary>Асинхронные комнаты на диске: <c>&lt;код&gt;.room.json</c> (участники с хешами ключей, ревизия, чей ход, день, хеш), партия
/// <c>&lt;код&gt;.state</c> (последнее сохранение целиком) и журнал ходов <c>&lt;код&gt;.journal.jsonl</c> (дописывается). Запись — через
/// временный файл: оборванная запись не портит прежнюю партию.</summary>
internal sealed class RoomStore(string dir)
{
    public IEnumerable<Room> LoadAll()
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (string path in Directory.EnumerateFiles(dir, "*.room.json"))
        {
            Room? room = null;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                room = new Room(node["code"]!.GetValue<string>(), node["name"]?.GetValue<string>() ?? "", async: true)
                {
                    Host = node["host"]?.GetValue<string>() ?? "h",
                    Rev = node["rev"]?.GetValue<long>() ?? 0,
                    Next = node["next"]?.GetValue<string>() ?? "",
                    Day = node["day"]?.GetValue<int>() ?? 0,
                    Hash = node["hash"]?.GetValue<string>() ?? "",
                    Touched = node["touched"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UtcNow,
                };
                foreach (var m in node["members"]?.AsArray().OfType<JsonObject>() ?? [])
                    room.Add(m["peer"]!.GetValue<string>(), m["nick"]?.GetValue<string>() ?? "", m["seat"]?.GetValue<string>() ?? "",
                             m["keyHash"]!.GetValue<string>());
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or NullReferenceException or IOException or FormatException)
            {
                room = null;   // битый файл комнаты — комнаты нет
            }
            if (room is not null) yield return room;
        }
    }

    public void Save(Room room, string? state = null, string? journal = null)
    {
        Directory.CreateDirectory(dir);
        if (state is not null) Write(StatePath(room), state);
        if (!string.IsNullOrEmpty(journal)) File.AppendAllText(Path.Combine(dir, room.Code + ".journal.jsonl"), journal.EndsWith('\n') ? journal : journal + "\n");
        var node = new JsonObject
        {
            ["code"] = room.Code, ["name"] = room.Name, ["host"] = room.Host, ["rev"] = room.Rev, ["next"] = room.Next, ["day"] = room.Day,
            ["hash"] = room.Hash, ["touched"] = room.Touched,
            ["members"] = new JsonArray([.. room.Members.Select(m => (JsonNode)new JsonObject
            {
                ["peer"] = m.Peer, ["nick"] = m.Nick, ["seat"] = m.Seat, ["keyHash"] = m.KeyHash,
            })]),
        };
        Write(Path.Combine(dir, room.Code + ".room.json"), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public long JournalBytes(Room room)
    {
        var file = new FileInfo(Path.Combine(dir, room.Code + ".journal.jsonl"));
        return file.Exists ? file.Length : 0;
    }

    public string? ReadState(Room room) => File.Exists(StatePath(room)) ? File.ReadAllText(StatePath(room)) : null;

    public void Delete(Room room)
    {
        foreach (string suffix in new[] { ".room.json", ".state", ".journal.jsonl" })
        {
            string path = Path.Combine(dir, room.Code + suffix);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private string StatePath(Room room) => Path.Combine(dir, room.Code + ".state");

    private static void Write(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
