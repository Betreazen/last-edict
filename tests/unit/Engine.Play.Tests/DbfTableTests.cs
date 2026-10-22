using D2.Data; using D2.Data.Dbf;
public class DbfTableTests
{
    static string T(string name) => Path.Combine(GamePaths.Globals, name);

    [SkippableFact] public void Gunits_HasExpectedShape()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var t = DbfTable.Read(T("Gunits.dbf"));
        Assert.Equal(356, t.Rows.Count);
        Assert.Equal(38, t.Columns.Count);
        Assert.Equal("UNIT_ID", t.Columns[0].Name);
        Assert.All(t.Rows, r => Assert.Matches("(?i)^g000uu", r.GetString("UNIT_ID")));
    }

    [SkippableFact] public void AllGlobalsAndScens_Open()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var files = Directory.GetFiles(GamePaths.Globals, "*.dbf").Concat(Directory.GetFiles(GamePaths.Scens, "*.dbf")).ToList();
        Assert.True(files.Count >= 112, files.Count.ToString());
        foreach (var f in files) { var t = DbfTable.Read(f); Assert.NotEmpty(t.Columns); }
    }

    [SkippableFact] public void Gattacks_NumericColumn_ParsesAsInt()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var t = DbfTable.Read(T("Gattacks.dbf"));
        Assert.Contains(t.Rows, r => r.GetInt("INITIATIVE") > 0);
        Assert.Contains(t.Rows, r => r.GetBool("CRIT_HIT"));
    }

    [SkippableFact] public void Gunits_MatchesIndependentPythonReader()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        // Эталон — выписка таблицы игры: в публичном дереве её нет (docs/publishing.md).
        Skip.IfNot(File.Exists("golden/Gunits.json"), "нет эталона golden/Gunits.json (данные игры)");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText("golden/Gunits.json"));
        var golden = doc.RootElement.GetProperty("rows").EnumerateArray().ToList();
        var t = DbfTable.Read(Path.Combine(GamePaths.Globals, "Gunits.dbf"));
        Assert.Equal(golden.Count, t.Rows.Count);
        for (int i = 0; i < golden.Count; i++)
        {
            Assert.Equal(golden[i].GetProperty("UNIT_ID").GetString(), t.Rows[i].GetString("UNIT_ID"));
            Assert.Equal(golden[i].GetProperty("HIT_POINT").GetInt32(), t.Rows[i].GetInt("HIT_POINT"));
            Assert.Equal(golden[i].GetProperty("ATCK_TWICE").GetBoolean(), t.Rows[i].GetBool("ATCK_TWICE"));
        }
    }

    [SkippableFact] public void Tglobal_TextDecodesAs866()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        // Эталон — выписка таблицы игры: в публичном дереве её нет (docs/publishing.md).
        Skip.IfNot(File.Exists("golden/Tglobal.ru.head.json"), "нет эталона golden/Tglobal.ru.head.json (данные игры)");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText("golden/Tglobal.ru.head.json"));
        var golden = doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("TEXT").GetString()).ToList();
        var ru = Path.GetFullPath(Path.Combine(GamePaths.GameDir, "..", "l10n", "russobit", "Globals", "Tglobal.dbf"));
        Skip.IfNot(File.Exists(ru), ru);
        var t = DbfTable.Read(ru);
        for (int i = 0; i < golden.Count; i++) Assert.Equal(golden[i], t.Rows[i].GetString("TEXT"));
    }
}
