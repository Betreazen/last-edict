using System.Text.Json;
using D2.Data;
using D2.Data.Sg;
using Kaitai;

public class SgFileTests
{
    [SkippableFact] public void AllExports_Parse()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var files = Directory.GetFiles(GamePaths.Exports, "*.sg");
        Assert.Equal(33, files.Length);
        foreach (var f in files) { var s = SgFile.Open(f); Assert.InRange(s.MapSize, 24, 144); Assert.InRange(s.PlayerCount, 1, 5); Assert.True(s.StackCount > 0, f); }
    }

    [SkippableFact] public void StackPositions_MatchIndependentPythonInspector()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        using var doc = JsonDocument.Parse(File.ReadAllText("golden/sg_stacks.json"));
        foreach (var scenario in doc.RootElement.EnumerateObject())
        {
            var golden = scenario.Value.EnumerateArray()
                .Select(r => (r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32()))
                .ToList();

            // Exports/'Greenskins'.sg carries literal single quotes in the filename.
            var candidates = new[] { $"{scenario.Name}.sg", $"'{scenario.Name}'.sg" };
            var path = candidates
                .Select(name => Path.Combine(GamePaths.Exports, name))
                .First(File.Exists);

            var sg = SgFile.Open(path);
            var actual = sg.Raw.Objects
                .Select(o => o.Data)
                .OfType<Sg.MidStack>()
                .Select(s => ((int)s.Position.X.Value, (int)s.Position.Y.Value))
                .ToList();

            Assert.Equal(golden.Count, actual.Count);
            Assert.Equal(golden.ToHashSet(), actual.ToHashSet());
        }
    }

    /// <summary>Кеш <see cref="SgFile.Open"/> не держит файл открытым: игра или драйвер переписывают сохранение, пока процесс
    /// инструмента жив, и следующий Open видит новый файл.</summary>
    [SkippableFact] public void Open_DoesNotLockFile()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        string source = Directory.GetFiles(GamePaths.Exports, "*.sg").First();
        string copy = Path.Combine(Path.GetTempPath(), $"sgfile-lock-{Guid.NewGuid():N}.sg");
        File.Copy(source, copy);
        try
        {
            SgFile.Open(copy);
            File.WriteAllBytes(copy, File.ReadAllBytes(source));
        }
        finally { File.Delete(copy); }
    }
}
