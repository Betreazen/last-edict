using System.Text.Json;
using LastEdict.Data.Game;
using LastEdict.Data.Sg;

namespace LastEdict.Data.Play;

/// <summary>
/// Свои кампании игрока (<c>user://campaigns</c>) в формате порта: каталог кампании — <c>campaign.json</c> (<c>d2-port-campaign</c>:
/// <see cref="CampaignInfo"/> с главами) и сценарии глав (<see cref="ScenarioFile"/>). Кампании игры (<c>Campaign\*.csg</c>) попадают
/// сюда импортом (<see cref="Import"/>) один раз, как герои <c>.ldr</c>; в рантайме порт от <c>.csg</c> не зависит.
/// </summary>
public static class CampaignStore
{
    public const string Format = "d2-port-campaign";
    public const int Version = 1;
    private const string File = "campaign.json";

    private sealed record Envelope(string Format, int Version, CampaignInfo Campaign);

    /// <summary>Кампании <paramref name="csgDir"/> (<c>*.csg</c>, ещё не начатые — флаг заголовка 0, <c>0x66b49c</c>) — в каталог порта
    /// под именем файла; уже переведённые пропускаются, нечитаемые — тоже, с причиной в <paramref name="failed"/>. Возвращает число
    /// переведённых.</summary>
    public static int Import(string csgDir, string dir, IReadOnlyList<RaceInfo> races, ICollection<string>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(races);
        if (!Directory.Exists(csgDir)) return 0;
        int count = 0;
        foreach (string path in Directory.GetFiles(csgDir, "*" + CsgFile.Extension))
        {
            string stem = Path.GetFileNameWithoutExtension(path), target = Path.Combine(dir, stem);
            if (Directory.Exists(target)) continue;
            try
            {
                var csg = CsgFile.Read(System.IO.File.ReadAllBytes(path));
                if (csg.Started) continue;
                Write(target, stem, csg, races, failed);
                count++;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or Kaitai.ValidationFailedError or EndOfStreamException
                                          or ArgumentOutOfRangeException or KeyNotFoundException or InvalidOperationException)
            {
                // Главы чужого формата (правленые редакторами .sg) разбор не берёт — кампания не переводится и не остаётся наполовину.
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                failed?.Add($"{stem}: {e.GetType().Name}: {e.Message}");
            }
        }
        return count;
    }

    /// <summary>Кампания — в каталог <paramref name="target"/>. Глава, которая не читается (файл главы в <c>.csg</c> оборван — так у
    /// «Without Alternate» кампании AVALANCHE: 3045 объектов из 3781), остаётся в списке без файла сценария: предыдущие главы играются,
    /// до неё дойдёт — «Ошибка при загрузке саги» (<c>X005TA0740</c>), как у движка при неудачной распаковке главы (<c>0x4dbf00</c>).
    /// Не читается первая глава — кампания не переводится.</summary>
    private static void Write(string target, string stem, CsgFile csg, IReadOnlyList<RaceInfo> races, ICollection<string>? failed)
    {
        Directory.CreateDirectory(target);
        var chapters = new List<ChapterInfo>();
        for (int i = 0; i < csg.Chapters.Count; i++)
        {
            var (name, data) = csg.Chapters[i];
            string id = $"{stem}:{Path.GetFileNameWithoutExtension(name)}";
            try
            {
                var sg = SgFile.Load(data);
                ScenarioFile.Write(Path.Combine(target, ChapterFile(i)), ScenarioFile.FromSg(id, sg));
                chapters.Add(new ChapterInfo(i, GameImport.ScenarioOf(sg, id, ChapterFile(i), races)));
            }
            catch (Exception e) when (i > 0 && e is InvalidDataException or IOException or Kaitai.ValidationFailedError
                                                      or EndOfStreamException or ArgumentOutOfRangeException)
            {
                failed?.Add($"{stem}: глава {name} не читается ({e.GetType().Name}: {e.Message})");
                chapters.Add(new ChapterInfo(i, new ScenarioInfo { Id = id, File = "", Name = Path.GetFileNameWithoutExtension(name) }));
            }
        }
        string raceId = $"G000RR{csg.RaceIndex:X4}";
        var campaign = new CampaignInfo
        {
            Id = stem, Custom = true, Name = csg.Name, Description = csg.Description, Author = csg.Author, LeaderLevel = csg.LeaderLevel,
            Race = races.FirstOrDefault(r => r.Id.Equals(raceId, StringComparison.OrdinalIgnoreCase))?.Type ?? 0,
            CanImport = true, Chapters = chapters,
        };
        System.IO.File.WriteAllText(Path.Combine(target, File), JsonSerializer.Serialize(new Envelope(Format, Version, campaign), SaveJson.Options));
    }

    private static string ChapterFile(int index) => $"chapter{index}{ScenarioFile.Extension}";

    /// <summary>Свои кампании каталога по имени; у глав — полный путь файла сценария (<see cref="ScenarioInfo.File"/>). Нечитаемая
    /// кампания пропускается.</summary>
    public static IReadOnlyList<CampaignInfo> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var list = new List<CampaignInfo>();
        foreach (string sub in Directory.GetDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(sub, File);
            if (!System.IO.File.Exists(path)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<Envelope>(System.IO.File.ReadAllText(path), SaveJson.Options);
                if (e is null || e.Format != Format || e.Version > Version || e.Campaign is null) continue;
                list.Add(e.Campaign with
                {
                    Chapters = [.. e.Campaign.Chapters.Select(c => c.Scenario.File.Length == 0 ? c
                        : c with { Scenario = c.Scenario with { File = Path.Combine(sub, c.Scenario.File) } })],
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Чужой или битый каталог — не кампания порта.
            }
        }
        return list;
    }
}
