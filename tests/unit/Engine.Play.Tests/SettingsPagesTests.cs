using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Map;
using LastEdict.Data.Settings;

namespace LastEdict.Data.Tests;

/// <summary>Окно настроек данными (<see cref="SettingsPages"/>, Э5): каждое поле файла настроек — ровно в одном пункте, черновик применяет и
/// откатывает, режим экрана ждёт подтверждения 15 с, свои строки окна есть на русском и английском.</summary>
public class SettingsPagesTests
{
    private static readonly string OwnStrings = Path.Combine(RepoPaths.Root, "data", "ui", "own");

    /// <summary>Поле <see cref="MapSettings"/>, которое меняет пункт: значение пункта ставится в другое допустимое, сравниваются поля.</summary>
    private static string FieldOf(SettingItem item)
    {
        var start = new MapSettings();
        int now = item.Get(start);
        int other = item.Kind switch
        {
            SettingKind.Toggle => 1 - now,
            SettingKind.Choice => item.Choices.First(c => c.Value != now).Value,
            _ => item.Clamp(now == item.Min ? item.Max : item.Min),
        };
        var before = JsonSerializer.SerializeToNode(start)!.AsObject();
        var after = JsonSerializer.SerializeToNode(item.Set(start, other).Clamped())!.AsObject();
        var changed = before.Where(p => !JsonNode.DeepEquals(p.Value, after[p.Key])).Select(p => p.Key).ToList();
        Assert.True(changed.Count == 1, $"{item.Id} меняет поля {string.Join(",", changed)}");
        return changed[0];
    }

    [Fact]
    public void EveryFileField_InExactlyOneItem()
    {
        var fields = SettingsPages.All.Select(FieldOf).ToList();
        Assert.Equal(fields.Count, fields.Distinct().Count());
        var layout = SettingsFile.Layout.Select(s => s.Field).Except(SettingsPages.Unlisted).Order();
        Assert.Equal(layout, fields.Order());
        Assert.Equal(SettingsPages.All.Count, SettingsPages.All.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Items_RoundTripTheirValues()
    {
        foreach (var item in SettingsPages.All)
        {
            var values = item.Kind switch
            {
                SettingKind.Toggle => [0, 1],
                SettingKind.Choice => item.Choices.Select(c => c.Value).ToList(),
                _ => Enumerable.Range(0, ((item.Max - item.Min) / item.Step) + 1).Select(i => item.Min + (i * item.Step)).ToList(),
            };
            foreach (int v in values)
                Assert.Equal(v, item.Get(item.Set(new MapSettings(), v).Clamped()));
        }
    }

    [Fact]
    public void Draft_DisplayWaitsConfirm_RevertDisplayKeepsTheRest()
    {
        var draft = new SettingsDraft(new MapSettings());
        Assert.False(draft.Changed);
        draft.Set(SettingsPages.Get("music"), 33);   // ползунок — к шагу и в границах
        Assert.Equal(33, draft.Current.MusicVolume);
        draft.Set(SettingsPages.Get("master"), 47);
        Assert.Equal(45, draft.Current.MasterVolume);
        Assert.False(draft.DisplayChanged);
        draft.Set(SettingsPages.Get("windowMode"), (int)Ui.DisplayMode.Fullscreen);
        draft.Set(SettingsPages.Get("maxFps"), 59);   // выбор — ближайший вариант
        Assert.Equal(60, draft.Current.MaxFps);
        Assert.True(draft.DisplayChanged);

        draft.RevertDisplay();
        Assert.False(draft.DisplayChanged);
        Assert.Equal("windowed", draft.Current.WindowMode);
        Assert.Equal(33, draft.Current.MusicVolume);
        Assert.True(draft.Changed);

        draft.Commit();
        Assert.False(draft.Changed);
        draft.Set(SettingsPages.Get("language"), 2);
        Assert.Equal("en", draft.Current.Language);
        Assert.Equal(new MapSettings().Clamped() with { MusicVolume = 33, MasterVolume = 45 }, draft.Revert());
    }

    [Fact]
    public void Confirm_ExpiresAfterFifteenSeconds()
    {
        var confirm = new DisplayConfirm();
        Assert.Equal(15, confirm.Left);
        Assert.False(confirm.Tick(14.5));
        Assert.Equal(1, confirm.Left);
        Assert.True(confirm.Tick(0.5));
        Assert.Equal(0, confirm.Left);
    }

    [Fact]
    public void Language_InSettingsFile_UnknownKeptLowercase()
    {
        var settings = new MapSettings(Language: " EN ").Clamped();
        Assert.Equal("en", settings.Language);
        Assert.Contains("\"interface\"", SettingsFile.Format(settings), StringComparison.Ordinal);
        Assert.Equal("en", SettingsFile.Parse(SettingsFile.Format(settings)).Language);
        Assert.Equal("", SettingsFile.Parse("""{ "version": 2 }""").Language);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void OwnStrings_CoverEveryOwnKey(string language)
    {
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(OwnStrings, $"strings.{language}.json")))!;
        var keys = SettingsPages.All.SelectMany(i => new[] { i.Label, i.Help }.Concat(i.Choices.Select(c => c.Label)))
            .Concat(Enum.GetValues<SettingsTab>().Select(SettingsPages.TabLabel))
            .OfType<string>().Where(k => k.StartsWith("LE_", StringComparison.Ordinal)).Distinct();
        Assert.All(keys, k => Assert.True(strings.ContainsKey(k), $"нет строки {k} ({language})"));
    }
}

/// <summary>Логика окна настроек (<see cref="SettingsLogic"/>): что уходит хозяину сразу, что — по «Применить», подтверждение экрана, язык.</summary>
public class SettingsLogicTests
{
    [Fact]
    public void Preview_Immediately_ApplyWritesOnce_DisplayWaitsAnswer()
    {
        var previews = new List<MapSettings>();
        var applied = new List<MapSettings>();
        var logic = new SettingsLogic(new MapSettings(), inGame: false, previews.Add, applied.Add);

        logic.Set(SettingsPages.Get("music"), 40);
        Assert.Equal(40, previews[^1].MusicVolume);
        Assert.Empty(applied);
        logic.Set(SettingsPages.Get("windowMode"), (int)Ui.DisplayMode.Fullscreen);
        Assert.Single(previews);   // экран — не на пробу сразу, а по «Применить»
        Assert.True(logic.Apply());
        Assert.NotNull(logic.Confirm);
        logic.Set(SettingsPages.Get("music"), 10);   // пока ждём ответа — выбор не меняется
        Assert.Equal(40, logic.Draft.Current.MusicVolume);
        Assert.False(logic.Tick(14));
        Assert.True(logic.Tick(1));
        var back = logic.Answer(keep: false);
        Assert.Equal("windowed", back.WindowMode);
        Assert.Equal(40, applied.Single().MusicVolume);
        Assert.False(logic.Draft.Changed);
    }

    [Fact]
    public void Cancel_RevertsPreview_PresetActsAsChoice()
    {
        var previews = new List<MapSettings>();
        var logic = new SettingsLogic(new MapSettings(), inGame: true, previews.Add, null);
        logic.Preset(lowEnd: true);
        Assert.True(previews[^1].LowEndVideoCard);
        Assert.Equal(0, previews[^1].AudioRegions);
        logic.Cancel();
        Assert.Equal(new MapSettings().Clamped(), previews[^1]);
        Assert.False(logic.Draft.Changed);
    }

    [Fact]
    public void InGame_LanguageLocked_GameButtonsAllowed()
    {
        var game = new SettingsLogic(new MapSettings(Language: "ru"), inGame: true, null, null);
        game.Set(SettingsPages.Get("language"), 2);
        Assert.Equal("ru", game.Draft.Current.Language);
        game.Defaults();
        Assert.Equal("ru", game.Draft.Current.Language);
        var state = new Ui.UiScreenState(new Ui.UiScreen { Id = SettingsLogic.Screen });
        Assert.True(game.Allows(SettingsLogic.Save, state));
        var menu = new SettingsLogic(new MapSettings(), inGame: false, null, null);
        Assert.False(menu.Allows(SettingsLogic.Save, state));
        Assert.True(menu.Allows(SettingsLogic.Close, state));
        menu.Set(SettingsPages.Get("language"), 2);
        Assert.Equal("en", menu.Draft.Current.Language);
    }
}
