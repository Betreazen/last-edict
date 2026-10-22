using LastEdict.Data.Dbf;
using Xunit;

namespace LastEdict.Data.Tests;

/// <summary>Кеш <see cref="GameTables.Read"/>: повторное чтение того же файла — тот же экземпляр, переписанный файл
/// (другой размер или время записи) — новая таблица.</summary>
public sealed class GameTablesCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("d2-tables-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Table(string id, int hp) =>
        $$"""{"table":"GTest","columns":[{"name":"ID","type":"C","length":10,"decimals":0},{"name":"HP","type":"N","length":5,"decimals":0}],"rows":[{"ID":"{{id}}","HP":{{hp}}}]}""";

    [Fact]
    public void Read_Twice_SameInstance()
    {
        File.WriteAllText(Path.Combine(_dir, "GTest.json"), Table("a", 1));
        var first = GameTables.Read(_dir, "GTest.dbf");
        Assert.Same(first, GameTables.Read(_dir, "GTest.dbf"));
        Assert.Same(first, GameTables.Read(_dir, "gtest.DBF"));
    }

    [Fact]
    public void Read_FileRewritten_NewTable()
    {
        string file = Path.Combine(_dir, "GTest.json");
        File.WriteAllText(file, Table("a", 1));
        var first = GameTables.Read(_dir, "GTest.dbf");

        File.WriteAllText(file, Table("a", 100));
        var longer = GameTables.Read(_dir, "GTest.dbf");
        Assert.NotSame(first, longer);
        Assert.Equal(100, longer.Rows[0].GetInt("HP"));

        var time = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, Table("a", 200));
        File.SetLastWriteTimeUtc(file, time.AddSeconds(5));
        var sameLength = GameTables.Read(_dir, "GTest.dbf");
        Assert.NotSame(longer, sameLength);
        Assert.Equal(200, sameLength.Rows[0].GetInt("HP"));
    }

    [Fact]
    public void Read_BrokenFile_NotCached()
    {
        string file = Path.Combine(_dir, "GTest.json");
        File.WriteAllText(file, "{\"table\":\"GTest\",\"columns\":[],\"rows\":[{\"A\":[1]}]}");
        Assert.Throws<FormatException>(() => GameTables.Read(_dir, "GTest.dbf"));
        var time = File.GetLastWriteTimeUtc(file);
        long length = new FileInfo(file).Length;
        // Тот же ключ (размер и время), но содержимое исправлено: ошибка не запомнилась, файл читается заново.
        string fixedJson = "{\"table\":\"GTest\",\"columns\":[],\"rows\":[{\"A\":\"1\"}]}".PadRight((int)length);
        File.WriteAllText(file, fixedJson);
        File.SetLastWriteTimeUtc(file, time);
        Assert.Equal("1", GameTables.Read(_dir, "GTest.dbf").Rows[0]["A"]);
    }

    [SkippableFact]
    public void Read_Dbf_CachedAndNamedAsOnDisk()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        File.Copy(Path.Combine(GamePaths.Globals, "GVars.dbf"), Path.Combine(_dir, "GVars.dbf"));
        var first = GameTables.Read(_dir, "gvars.dbf");
        Assert.Equal("GVars", first.Name);
        Assert.Same(first, GameTables.Read(_dir, "GVars.dbf"));
    }
}
