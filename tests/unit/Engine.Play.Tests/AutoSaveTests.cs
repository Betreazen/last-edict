using D2.Data.Content;
using D2.Data.Map;
using D2.Data.Play;
using D2.Data.Ui;

namespace D2.Data.Tests;

/// <summary>Автосохранение по концу хода человека (<see cref="AutoSave"/>) — как у обёртки DisciplesGL-HD: архив «&lt;сценарий&gt; T####» в
/// каталоге сохранений, не больше 15 архивов сценария, чужие файлы не трогаются; быстрое сохранение (<see cref="QuickSave"/>) — один файл
/// «QuickSave»; строки о записи — в чат карты (<see cref="IsoChatLogic"/>).</summary>
public class AutoSaveTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "d2-autosave-" + Guid.NewGuid().ToString("N"));

    private static GameSession Tutorial()
    {
        string sg = Path.Combine(GamePaths.Exports, "Tutorial.sg");
        Skip.IfNot(File.Exists(sg), "нет копии игры");
        var looks = MapLooksLoader.Load(sg);
        return new GameSession(new GameState(GameInfo.FromMap("Tutorial", looks), looks), SharedContent.Load(Data).MapArt,
                               GameBook.Load(ImportedData.PathOf("rules")));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndOfTurn_SavesArchiveOfScenarioAndDay_OnlyWhenEnabled(bool enabled)
    {
        var session = Tutorial();
        string dir = TempDir();
        try
        {
            int day = session.Map.Turn;
            string? path = AutoSave.Write(dir, enabled, session.Snapshot());   // «Конец хода»: запись до хода остальных
            session.EndTurn();
            Assert.Equal(day + 1, session.Map.Turn);

            if (!enabled)
            {
                Assert.Null(path);
                Assert.Empty(SaveSlots.List(dir));
                return;
            }
            Assert.Equal(Path.Combine(dir, $"Tutorial T{day:D4}.d2save"), path);
            var slot = Assert.Single(SaveSlots.List(dir));
            Assert.Equal(($"Tutorial T{day:D4}", "Tutorial", day), (slot.Title, slot.Scenario, slot.Day));
            var loaded = new GameSession(GameSave.Read(slot.Path), MapArtData.Empty);
            Assert.Equal(day, loaded.Map.Turn);   // партия на миг конца хода: тот же день

            // Тот же день ещё раз (второй человек «горячего стула») — тот же файл, не второй.
            AutoSave.Write(dir, enabled, GameSave.Read(slot.Path));
            Assert.Single(SaveSlots.List(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Имя архива — <c>"%s T%04u"</c> обёртки: название сценария без знаков, запрещённых в именах файлов (пробел вместо них, пробелы
    /// подряд и в начале — один, в конце пробелы и точки сняты), без названия — «AutoSave».</summary>
    [Theory]
    [InlineData("Tutorial", "Tutorial")]
    [InlineData("Дыхание смерти", "Дыхание смерти")]
    [InlineData("'Vertigo'", "'Vertigo'")]
    [InlineData("  A: b?/c*  ", "A b c")]
    [InlineData("Конец...", "Конец")]
    [InlineData("", "AutoSave")]
    [InlineData(" ?? ", "AutoSave")]
    public void ArchiveBase_SanitizesLikeWrapper(string scenario, string expected) =>
        Assert.Equal(expected, AutoSave.ArchiveBase(scenario));

    /// <summary>Чистка (<c>TrimAutoSaveArchives</c>): из 19 архивов сценария остаются 15 — удалены четыре самых старых по времени записи, при
    /// равном времени — с меньшим ходом. Ручные сохранения с похожими именами, архивы других сценариев и «AutoSave», другие расширения —
    /// не тронуты.</summary>
    [Fact]
    public void Trim_KeepsFifteenNewestArchivesOfScenario_AndNeverTouchesOtherSaves()
    {
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var start = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            string Put(string name, DateTime written)
            {
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, written);
                return path;
            }
            // Ходы 1…18; ходы 2 и 3 записаны в одну секунду с ходом 1 — при равном времени первым уходит меньший ход.
            for (int turn = 1; turn <= 18; turn++)
                Put($"Tutorial T{turn:D4}.d2save", turn <= 3 ? start : start.AddMinutes(turn));
            Put("tutorial t0099.d2save", start.AddDays(-2));             // архив того же сценария в другом регистре — учитывается
            string[] untouched =
            [
                Put("Tutorial T0004 копия.d2save", start.AddDays(-3)),  // ручное: похоже, но не по шаблону
                Put("Tutorial — день 1.d2save", start.AddDays(-3)),     // автосохранения сборки 14.10
                Put("Tutorial T12.d2save", start.AddDays(-3)),          // не четыре цифры
                Put("Tutorial T00001.d2save", start.AddDays(-3)),       // пять цифр
                Put("My Tutorial T0001.d2save", start.AddDays(-3)),     // другое название
                Put("Tutorial2 T0001.d2save", start.AddDays(-3)),
                Put("AutoSave T0001.d2save", start.AddDays(-3)),        // архив без названия — другой сценарий
                Put("QuickSave.d2save", start.AddDays(-3)),
                Put("Tutorial T0001.sg", start.AddDays(-3)),            // не сохранение порта
                Put("Tutorial T0002.d2save.tmp", start.AddDays(-3)),
            ];

            var removed = AutoSave.Trim(dir, "Tutorial");

            Assert.Equal(["tutorial t0099.d2save", "Tutorial T0001.d2save", "Tutorial T0002.d2save", "Tutorial T0003.d2save"],
                         removed.Select(Path.GetFileName));
            var left = AutoSave.Archives(dir, "Tutorial");
            Assert.Equal(AutoSave.ArchiveLimit, left.Count);
            Assert.Equal(Enumerable.Range(4, 15), left.Select(a => a.Turn).Order());
            Assert.All(untouched, path => Assert.True(File.Exists(path), path));

            Assert.Empty(AutoSave.Trim(dir, "Tutorial"));   // 15 — ничего лишнего
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Конец хода 20 дней подряд: в каталоге — 15 архивов последних дней, ручное сохранение с похожим именем на месте.</summary>
    [SkippableFact]
    public void EndOfTurn_TwentyDays_LeavesFifteenArchives()
    {
        var session = Tutorial();
        string dir = TempDir();
        try
        {
            var state = session.Snapshot();
            var past = DateTime.UtcNow.AddDays(-1);
            string manual = SaveSlots.PathFor(dir, "Tutorial T0001 перед боем");
            GameSave.Write(manual, state, "Tutorial T0001 перед боем");
            for (int day = 1; day <= 20; day++)
            {
                var dayState = state with { Looks = state.Looks with { Map = GameSession.With(state.Looks.Map, turn: day) } };
                string path = AutoSave.Write(dir, enabled: true, dayState)!;
                File.SetLastWriteTimeUtc(path, past.AddMinutes(day));   // дни — по минуте, все раньше следующей записи
            }
            Assert.Equal(Enumerable.Range(6, 15), AutoSave.Archives(dir, "Tutorial").Select(a => a.Turn).Order());
            Assert.True(File.Exists(manual));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Быстрое сохранение: файл «QuickSave.d2save», второе — в тот же файл (день новый), в списке окна загрузки и грузится.</summary>
    [SkippableFact]
    public void QuickSave_OverwritesOneFile_AndLoads()
    {
        var session = Tutorial();
        string dir = TempDir();
        try
        {
            int day = session.Map.Turn;
            string first = QuickSave.Write(dir, session.Snapshot());
            session.EndTurn();
            string second = QuickSave.Write(dir, session.Snapshot());

            Assert.Equal(Path.Combine(dir, "QuickSave.d2save"), first);
            Assert.Equal(first, second);
            var slot = Assert.Single(SaveSlots.List(dir));
            Assert.Equal(("QuickSave", "Tutorial", day + 1), (slot.Title, slot.Scenario, slot.Day));
            Assert.Equal(day + 1, new GameSession(GameSave.Read(slot.Path), MapArtData.Empty).Map.Turn);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Строки о записи (<c>X005TA0581</c>, <c>X005TA0582</c>) — в чат карты оформлением <c>X005TA0703</c>; в <c>TXT_CHAT_AREA</c> —
    /// последняя; строк не больше пяти, каждая живёт 10 с.</summary>
    [SkippableFact]
    public void Chat_ShowsSaveMessages_FiveLinesTenSeconds()
    {
        var strings = UiStrings.Load(ImportedData.StringsDir(), "ru");
        string Text(string id) => strings.TryGetValue(id, out string? s) ? s : "";
        var (saving, saved) = QuickSave.Messages(Text, QuickSave.Name);
        Assert.Equal(("Сохраняется QuickSave.sg...", "Игра записана под именем QuickSave"), (saving, saved));

        var screen = UiScreens.Load(ImportedData.PathOf("ui/screens.json")).First(s => s.Id == IsoChatLogic.Screen);
        var ui = new UiScreenState(screen);
        var chat = new IsoChatLogic(Text);
        chat.Open(ui);
        Assert.Equal("", ui.TextOf(IsoChatLogic.Area) ?? "");

        chat.Say(saving, 1000);
        chat.Say(saved, 1000);
        chat.Show(ui);
        Assert.Equal(@"\o000;000;000;\c255;255;255;Игра записана под именем QuickSave", ui.TextOf(IsoChatLogic.Area));
        Assert.Equal(2, chat.Lines.Count);

        for (int i = 0; i < 4; i++) chat.Say($"строка {i}", 5000);
        Assert.Equal(IsoChatLogic.Capacity, chat.Lines.Count);   // шестая сняла старейшую
        Assert.DoesNotContain(chat.Lines, l => l.EndsWith(saving, StringComparison.Ordinal));

        Assert.False(chat.Expire(10_999));
        Assert.True(chat.Expire(11_000));   // «записана» — через 10 с
        Assert.Equal(4, chat.Lines.Count);
        Assert.True(chat.Expire(15_000));
        chat.Show(ui);
        Assert.Equal("", ui.TextOf(IsoChatLogic.Area) ?? "");
    }
}
