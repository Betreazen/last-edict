using D2.Data.Content;
using D2.Data.Map;
using D2.Data.Play;
using D2.Data.Ui;

namespace D2.Data.Tests;

/// <summary>Окно ожидания хода ИИ <c>DLG_STRAT_WAIT</c> (<see cref="AiWaitLogic"/>): внизу — лорд того, чей ход (у нейтралов —
/// «разбойник» <c>LORDNEL01</c>); <c>IMG_LEADER</c> и <c>TXT_LEADER_NAME</c> пусты, как у движка (разбор 18.10, Ин39: смена панели
/// <c>0x48d858</c> ставит только <c>IMG_LORD_FACE</c>, портрет и имя лидера — только в <c>DLG_STRATEGIC</c>, проверка <c>0x48d934</c>).</summary>
public class AiWaitPanelTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [SkippableFact]
    public void Panel_ShowsLordOfTurn_LeaderSlotEmpty()
    {
        const string scenario = "Power to Corrupt";
        Skip.IfNot(File.Exists(Path.Combine(GamePaths.Exports, scenario + ".sg")), "нет копии игры");
        var looks = MapLooksLoader.Load(Path.Combine(GamePaths.Exports, scenario + ".sg"));
        var book = GameBook.Load(Path.Combine(Data, "rules"));
        var map = looks.Map;
        var playable = map.Players.Values.First(p => book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Playable);
        var neutral = map.Players.Values.First(p => !(book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Playable));

        string lord = AiWaitLogic.LordFace(playable, book);
        Assert.Matches("^Lords:LORD(HU|UN|HE|DW|EL)L0[1-4]$", lord);
        Assert.Equal(AiWaitLogic.NeutralLord, AiWaitLogic.LordFace(neutral, book));

        var screen = UiScreens.Load(Path.Combine(Data, "ui", "screens.json")).First(s => s.Id == AiWaitLogic.Screen);
        var ui = new UiScreenState(screen);
        var state = new AiWaitState(lord);
        var logic = new AiWaitLogic(() => state);
        logic.Open(ui);
        Assert.Equal(lord, ui.Frame("IMG_LORD_FACE"));
        Assert.Null(ui.Frame("IMG_LEADER"));                     // знак фона DLG_STRAT_WAIT_STRAVIDE, портрета нет
        Assert.Equal("", ui.TextOf("TXT_LEADER_NAME") ?? "");
        Assert.Equal("DLG_STRAT_WAIT_COVER", ui.Frame("IMG_XCOVER"));   // рамка поверх портрета лорда

        state = AiWaitState.Empty with { LordFace = AiWaitLogic.NeutralLord };
        logic.Refresh(ui);
        Assert.Equal(AiWaitLogic.NeutralLord, ui.Frame("IMG_LORD_FACE"));
        Assert.Null(ui.Frame("IMG_LEADER"));
        Assert.Equal("", ui.TextOf("TXT_LEADER_NAME") ?? "");
    }
}
