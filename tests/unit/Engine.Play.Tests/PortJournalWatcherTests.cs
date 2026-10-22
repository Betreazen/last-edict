using System.Text.Json;
using LastEdict.Data.Content;
using LastEdict.Data.Diagnostics;
using LastEdict.Data.Map;
using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Rules.Map;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Подписчик зонда порта (<see cref="PortJournalWatcher"/>) на долгой партии: снимок дня и сохранение в начале хода человека,
/// итоги боёв, события, трасса ИИ — пишутся, а партия идёт так же, как без него (трасса и подписчик ничего не решают).</summary>
public class PortJournalWatcherTests(ITestOutputHelper output)
{
    private const string Scenario = "Power to Corrupt";
    private const int Days = 10;

    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static string ScenarioPath => ScenarioFile.PathOf(Path.Combine(Data, "scenarios"), Scenario);

    private static readonly Lazy<(GameContent Content, GameBook Book)> Shared =
        new(() => (SharedContent.Load(Data), GameBook.Load(Path.Combine(Data, "rules"))));

    /// <summary>Новая партия на сценарии порта: человек — Кланы, остальные — ИИ (одинаково при каждом вызове: <c>Random(1)</c>).</summary>
    private static GameSession Session()
    {
        var (content, book) = Shared.Value;
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == Scenario), Race = 3, LordCategory = 0, Difficulty = 1,
        };
        var state = NewGame.Start(ScenarioFile.Read(ScenarioPath), setup, content.Catalog, book, new Random(1));
        return new GameSession(state, content.MapArt, book);
    }

    [SkippableFact]
    public void LongGame_WritesDaysSavesBattlesAndAiTrace_GameUnchanged()
    {
        Skip.IfNot(File.Exists(ScenarioPath), "нет сценария порта");
        string dir = Path.Combine(Path.GetTempPath(), $"portwatch-{Guid.NewGuid():N}");
        string path = Path.Combine(dir, "port-test.jsonl");
        try
        {
            var plain = Session();
            var watched = Session();
            using (var journal = new PortJournal(path))
            {
                var watcher = new PortJournalWatcher(journal, watched);
                watched.Watch(watcher);
                watched.AiTrace = watcher;
                watcher.Attached(watched.ActiveHuman);
                for (int day = 0; day < Days; day++)
                {
                    var a = plain.EndTurn();
                    var b = watched.EndTurn();
                    Assert.Equal(a.Events, b.Events);
                }
            }
            Assert.Equal(plain.Map.Players.Values.Select(p => p.Bank), watched.Map.Players.Values.Select(p => p.Bank));
            Assert.Equal(plain.Rng.Index, watched.Rng.Index);
            Assert.Equal(plain.Crt.State, watched.Crt.State);

            var lines = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
            var kinds = lines.GroupBy(l => l.GetProperty("ev").GetString()!).ToDictionary(g => g.Key, g => g.ToList());
            output.WriteLine(string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value.Count}")));
            foreach (var l in kinds.GetValueOrDefault("invariant", [])) output.WriteLine(l.GetRawText());

            // Снимок дня: на подписке (помечен attach) и в начале каждого хода человека — день, казна, генераторы, сохранение рядом с
            // журналом; сохранения старше недели прорежены (остаются кратные 5).
            var days = kinds["day"];
            Assert.Equal(Days + 1, days.Count);
            Assert.Equal(Enumerable.Range(1, Days + 1), days.Select(d => d.GetProperty("day").GetInt32()));
            Assert.True(days[0].GetProperty("attach").GetBoolean());
            Assert.EndsWith("-attach.d2save", days[0].GetProperty("save").GetString(), StringComparison.Ordinal);
            Assert.False(days[1].TryGetProperty("attach", out _));
            var kept = Directory.GetFiles(dir, "*.d2save").Select(f => int.Parse(Path.GetFileName(f)["port-test-day".Length..][..3],
                                                                                    System.Globalization.CultureInfo.InvariantCulture))
                                .Order().ToList();
            Assert.Equal(Enumerable.Range(Days + 1 - PortJournalWatcher.KeepDays, PortJournalWatcher.KeepDays + 1)
                                   .Where(d => d > Days + 1 - PortJournalWatcher.KeepDays || d % PortJournalWatcher.KeepEvery == 0), kept);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            var last = days[^1];
            string human = watched.ActiveHuman;
            var me = last.GetProperty("players").GetProperty(human);
            Assert.Equal(watched.Map.Players[human].Bank.Gold, me.GetProperty("bank")[0].GetInt64());
            Assert.True(me.GetProperty("capital").GetString() is { Length: > 0 }, "столица человека в снимке");
            Assert.True(me.GetProperty("land").GetInt32() > 0, "своя земля в снимке");
            Assert.Equal(watched.Rng.Index, last.GetProperty("rng").GetProperty("r250").GetInt32());
            string save = last.GetProperty("save").GetString()!;
            Assert.EndsWith($"-day{Days + 1:000}-{human}.d2save", save, StringComparison.Ordinal);
            var restored = GameSave.Read(save);
            Assert.Equal(Days + 1, restored.Looks.Map.Turn);
            Assert.Equal(watched.Rng.Index, restored.Random!.Index);

            // Итоги боёв — на каждый бой; трасса ИИ: выборы тактики и шаги стратегии, ход ИИ с длительностью.
            Assert.Equal(kinds["battle"].Count, kinds["battle_end"].Count);
            var end = kinds["battle_end"][0];
            Assert.True(end.GetProperty("winner").GetString() is { Length: > 0 });
            Assert.True(end.GetProperty("rounds").GetInt32() > 0);
            Assert.True(kinds["ai_pick"].Count > 0 && kinds["ai_step"].Count > 0);
            var aiTurns = kinds["ai_turn"];
            Assert.Equal(kinds["ai_pick"].Count, aiTurns.Sum(t => t.GetProperty("picks").GetInt32()));
            Assert.Equal(kinds["ai_step"].Count, aiTurns.Sum(t => t.GetProperty("steps").GetInt32()));
            // На оригинальном сценарии нарушений инвариантов нет.
            Assert.False(kinds.ContainsKey("invariant"),
                         string.Join("\n", kinds.GetValueOrDefault("invariant", []).Select(l => l.GetRawText())));
            Assert.False(kinds.ContainsKey("error"), "сбоев подписчика нет");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Журнал, запись в который бросает (полный диск, файл заблокирован): партия с подписчиком и трассой ИИ идёт так же, как без
    /// них, — ход ИИ, бои, события; журнал после <see cref="PortJournal.MaxFailures"/> сбоев выключается.</summary>
    [SkippableFact]
    public void BrokenJournal_NeverBreaksTheGame()
    {
        Skip.IfNot(File.Exists(ScenarioPath), "нет сценария порта");
        string dir = Path.Combine(Path.GetTempPath(), $"portwatch-{Guid.NewGuid():N}");
        try
        {
            var plain = Session();
            var watched = Session();
            var journal = new PortJournal(Path.Combine(dir, "port-broken.jsonl"), new ThrowingWriter());
            var watcher = new PortJournalWatcher(journal, watched);
            var counter = new Counter();
            watched.Watch(watcher);
            watched.Watch(counter);
            watched.AiTrace = watcher;
            watcher.Attached(watched.ActiveHuman);
            for (int day = 0; day < 8; day++) Assert.Equal(plain.EndTurn().Events, watched.EndTurn().Events);
            journal.Close("test");
            Assert.True(counter.Battles > 0 && counter.Events > 0, $"боёв {counter.Battles}, событий {counter.Events}");
            Assert.True(journal.Disabled);
            Assert.Contains("IOException", journal.LastFailure, StringComparison.Ordinal);
            Assert.Equal(plain.Rng.Index, watched.Rng.Index);
            Assert.Equal(plain.Map.Players.Values.Select(p => p.Bank), watched.Map.Players.Values.Select(p => p.Bank));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class ThrowingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("диск полон");
    }

    private sealed class Counter : ITurnObserver
    {
        public int Battles, Events;

        public void PlayerTurn(string playerId)
        {
        }

        public void StackMoving(StackMove move)
        {
        }

        public IReadOnlyList<LastEdict.Rules.Battle.TurnRecord>? Battle(LastEdict.Rules.Battle.BattleState state) => null;

        public void BattleEnded(BattleOutcome outcome) => Battles++;

        public void EventFired(LastEdict.Rules.Map.EventFiring firing) => Events++;
    }

    /// <summary>Одно и то же нарушение одного объекта — не чаще раза в день; новый день — снова.</summary>
    [SkippableFact]
    public void Invariant_RepeatsOncePerDay()
    {
        Skip.IfNot(File.Exists(ScenarioPath), "нет сценария порта");
        string path = Path.Combine(Path.GetTempPath(), $"portwatch-{Guid.NewGuid():N}.jsonl");
        try
        {
            var session = Session();
            string human = session.ActiveHuman;
            using (var journal = new PortJournal(path))
            {
                // Порча на всю партию: юнит ни в одной группе (его никто не трогает — нарушение живёт и завтра).
                var corrupt = new GameSession(session.State with
                {
                    Looks = session.Looks with
                    {
                        Map = GameSession.With(session.Map, units: new Dictionary<string, MapUnit>(session.Map.Units, StringComparer.OrdinalIgnoreCase)
                        {
                            ["S073UN0fff"] = new() { Id = "S073UN0fff", TypeId = "G000UU0001", Level = 1, Hp = 100 },
                        }),
                    },
                }, session.Art, session.Book);
                var watcher = new PortJournalWatcher(journal, corrupt, autosave: false);
                watcher.PlayerTurn(human);
                watcher.PlayerTurn(human);   // тот же день — не повторяется
                corrupt.EndTurn();
                watcher.PlayerTurn(human);   // новый день — снова
            }
            var bad = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement.Clone())
                          .Where(l => l.GetProperty("ev").GetString() == "invariant").ToList();
            Assert.Equal(2, bad.Count);
            Assert.All(bad, l => Assert.Equal(("unit_orphan", "S073UN0fff"), (l.GetProperty("code").GetString(), l.GetProperty("object").GetString())));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Журнал партии 17.10 (<c>port_log_report --synth</c>): события старта партии (<see cref="NewGame.Begin"/> — до подписки зонда;
    /// карта 9, C8w/C8p) пишутся строками <c>event</c> с <c>start</c> при подписке, один раз; действие с соседней клетки
    /// (<see cref="GameSession.ActInPlace"/>: нападение, вход, жезл, мешок — карта 8, R1–R7) — строкой <c>move</c> из одной клетки.</summary>
    [SkippableFact]
    public void StartEvents_AndActionInPlace_AreWritten()
    {
        Skip.IfNot(File.Exists(ScenarioPath), "нет сценария порта");
        var (content, book) = Shared.Value;
        var setup = new NewGameSetup
        {
            Scenario = content.Catalog.Scenarios.Single(s => s.Id == Scenario), Race = 3, LordCategory = 0, Difficulty = 1,
        };
        var session = NewGame.Begin(ScenarioFile.Read(ScenarioPath), setup, content.Catalog, book, content.MapArt, new Random(1));
        string path = Path.Combine(Path.GetTempPath(), $"portwatch-{Guid.NewGuid():N}.jsonl");
        try
        {
            string human = session.ActiveHuman;
            var stack = session.Map.Stacks.Values.First(s => s.Owner.Equals(human, StringComparison.OrdinalIgnoreCase));
            using (var journal = new PortJournal(path))
            {
                var watcher = new PortJournalWatcher(journal, session, autosave: false);
                session.Watch(watcher);
                watcher.Attached(human);
                watcher.Attached(human);   // вторая подписка — событий старта уже нет
                session.ActInPlace(stack.Id, attack: true);
            }
            var lines = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
            var start = lines.Where(l => l.GetProperty("ev").GetString() == "event").ToList();
            output.WriteLine(string.Join(", ", start.Select(l => l.GetProperty("id").GetString())));
            Assert.NotEmpty(start);
            Assert.All(start, l => Assert.True(l.GetProperty("start").GetBoolean()));
            Assert.Empty(session.TakeUnwatchedFirings());
            var move = Assert.Single(lines, l => l.GetProperty("ev").GetString() == "move");
            Assert.Equal((stack.Id, 1, true), (move.GetProperty("stack").GetString(), move.GetProperty("cells").GetInt32(),
                                               move.GetProperty("attack").GetBoolean()));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
