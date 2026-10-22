using System.Text.Json;
using LastEdict.Data.Diagnostics;

namespace LastEdict.Data.Tests;

/// <summary>Журнал зонда порта (<see cref="PortJournal"/>): строки JSON, контекст ошибки, повторы — счётом, итог при закрытии.</summary>
public class PortJournalTests
{
    private static List<JsonElement> Lines(string path) =>
        [.. File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    [Fact]
    public void Error_CarriesContext_RepeatsAreCounted_CloseWritesTotals()
    {
        string path = Path.Combine(Path.GetTempPath(), $"portjournal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var j = new PortJournal(path))
            {
                j.Write("start", new Dictionary<string, object?> { ["commit"] = "abc" });
                j.Message("walked S1 в=(3,4)\nbattle open ходов=0");
                for (int i = 0; i < 10; i++) j.Error("error", "ObjectDisposedException: Sprite2D", "BattleView.cs:469");
                j.Close("closed");
                j.Write("after", null);   // после закрытия — не пишется
            }

            var lines = Lines(path);
            Assert.Equal(["start", "print", "print", "error", "error", "error", "error_repeat", "error_repeat", "error_counts", "end"],
                         lines.Select(l => l.GetProperty("ev").GetString()));
            Assert.Equal(Enumerable.Range(1, lines.Count).Select(i => (long)i), lines.Select(l => l.GetProperty("seq").GetInt64()));
            Assert.Equal("walked", lines[1].GetProperty("tag").GetString());
            var first = lines[3];
            Assert.Equal("BattleView.cs:469", first.GetProperty("where").GetString());
            Assert.Equal(["walked S1 в=(3,4)", "battle open ходов=0"],
                         first.GetProperty("context").EnumerateArray().Select(c => c.GetString()));
            Assert.Equal([4, 8], lines.Where(l => l.GetProperty("ev").GetString() == "error_repeat").Select(l => l.GetProperty("n").GetInt32()));
            Assert.Equal(10, lines[^2].GetProperty("counts").EnumerateObject().Single().Value.GetInt32());
            Assert.Equal("closed", lines[^1].GetProperty("reason").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("walked S1 в=(3,4)", "walked")]
    [InlineData("mapbattle start attacker=S1", "mapbattle")]
    [InlineData("  отступ", "text")]
    [InlineData("ERROR: x", "ERROR")]
    public void Tag_IsTheFirstWord(string line, string tag) => Assert.Equal(tag, PortJournal.Tag(line));

    [Fact]
    public void ConcurrentWrites_KeepEveryLineWhole()
    {
        string path = Path.Combine(Path.GetTempPath(), $"portjournal-{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var j = new PortJournal(path))
                Parallel.For(0, 400, i => j.Message($"line {i}"));
            var lines = Lines(path);
            Assert.Equal(401, lines.Count);   // 400 сообщений и end
            Assert.Equal(400, lines.Count(l => l.GetProperty("ev").GetString() == "print"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Запись бросает (полный диск): журнал не бросает сам, после <see cref="PortJournal.MaxFailures"/> сбоев подряд
    /// выключается и больше не пытается писать.</summary>
    [Fact]
    public void FailingWrites_NeverThrow_AndDisableTheJournal()
    {
        var writer = new FailingWriter();
        var j = new PortJournal("port-x.jsonl", writer);
        for (int i = 0; i < 20; i++)
        {
            j.Write("turn", new Dictionary<string, object?> { ["day"] = i });
            j.Error("error", "x");
            j.Message("walked");
        }
        j.Close("closed");
        Assert.True(j.Disabled);
        Assert.Equal(PortJournal.MaxFailures, writer.Calls);
        Assert.Contains("IOException", j.LastFailure, StringComparison.Ordinal);
    }

    private sealed class FailingWriter : StringWriter
    {
        public int Calls;

        public override void WriteLine(string? value)
        {
            Calls++;
            throw new IOException("диск полон");
        }
    }
}
