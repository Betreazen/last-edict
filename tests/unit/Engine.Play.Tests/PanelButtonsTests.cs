using D2.Data.Battle;
using D2.Data.Content;
using D2.Data.Map;
using D2.Data.Ui;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Погашенные кнопки панели боя и панели команд карты — как у движка (<c>CButtonInterf::setEnabled</c> <c>0x531cca</c>; Зс55, Зс56):
/// правило по моменту боя (<see cref="BattlePanelButtons"/>), панель команд по выбранному отряду (<see cref="StrategicPanelLogic"/>);
/// погашенная кнопка настоящей разметки — кадр «погашена» из <c>.dlg</c>, щелчок и клавиша не проходят.</summary>
public class PanelButtonsTests
{
    private static GameContent Content => _content ??= SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static GameContent? _content;

    private static UiScreen Screen(string id) => Content.Screens.First(s => s.Id == id);

    private static readonly IReadOnlySet<BattleCommand> All =
        new HashSet<BattleCommand> { BattleCommand.Attack, BattleCommand.Defend, BattleCommand.Wait, BattleCommand.Retreat, BattleCommand.Skip };

    private static string Lit(BattlePanelMoment moment) =>
        string.Join(" ", BattlePanelButtons.Elements.Select(id => $"{id}{(BattlePanelButtons.Enabled(id, moment) ? "+" : "−")}"));

    [Fact]
    public void Battle_HumanTurn_LightsAllowedCommandsAndGroups()
    {
        Assert.Equal("BTN_DEFEND+ BTN_WAIT+ BTN_RETREAT+ TOG_RIGHTUNITS+", Lit(new(true, All, false, false)));
        // Ждал в этом раунде, отступать нельзя (0x63061f: кнопки — только из списка хода).
        var noWait = new HashSet<BattleCommand> { BattleCommand.Attack, BattleCommand.Defend };
        Assert.Equal("BTN_DEFEND+ BTN_WAIT− BTN_RETREAT− TOG_RIGHTUNITS+", Lit(new(true, noWait, false, false)));
    }

    [Fact]
    public void Battle_ShowAndAiTurn_DimAllFour()
    {
        // Показ действия человека и ходы ИИ (0x6371b1 → 0x62f27f): набор команд не смотрится.
        Assert.Equal("BTN_DEFEND− BTN_WAIT− BTN_RETREAT− TOG_RIGHTUNITS−", Lit(new(false, All, false, false)));
    }

    [Fact]
    public void Battle_ItemDragAndEnd_OnlyGroupsToggleChanges()
    {
        Assert.Equal("BTN_DEFEND+ BTN_WAIT+ BTN_RETREAT+ TOG_RIGHTUNITS−", Lit(new(true, All, true, false)));   // 0x63460d
        Assert.Equal("BTN_DEFEND− BTN_WAIT− BTN_RETREAT− TOG_RIGHTUNITS+", Lit(new(false, All, false, true)));  // 0x637581
        Assert.Throws<ArgumentOutOfRangeException>(() => BattlePanelButtons.Enabled("BTN_RESOLVE", new(true, All, false, false)));
    }

    [SkippableFact]
    public void Battle_DimmedButtons_HaveDisabledFrameAndIgnoreClicks()
    {
        var ui = new UiScreenState(Screen("DLG_BATTLE_A"));
        foreach (string id in (string[])["BTN_CLOSE", "BTN_PAPERDOLL"]) ui.SetHidden(id, true);   // до конца боя их нет (их места — «Ждать», «Отступить»)
        foreach (string id in BattlePanelButtons.Elements)
        {
            var e = ui.Screen.Elements.Single(x => x.Id == id);
            ui.SetEnabled(id, false);
            Assert.Equal(UiLook.Disabled, ui.Look(id));
            Assert.False(string.IsNullOrEmpty(e.Sprites?.Disabled), $"{id}: нет кадра «погашена» в разметке");
            Assert.Equal(e.Sprites!.Disabled, ui.Frame(id));
            int x = e.Rect.X + e.Rect.Width / 2, y = e.Rect.Y + e.Rect.Height / 2;
            ui.Move(x, y);
            ui.Press(x, y);
            Assert.Empty(ui.Release(x, y));
            foreach (int key in e.Hotkeys) Assert.Empty(ui.Key(key));
        }
    }

    private static UiScreenState Strategic(MapPanelState state)
    {
        var ui = new UiScreenState(Screen("DLG_STRATEGIC"));
        new StrategicPanelLogic(() => state, id => id).Refresh(ui);
        return ui;
    }

    [SkippableFact]
    public void Strategic_RodAndWand_FollowSelectedStack()
    {
        // Отряд выбран, лидер умеет посохи (0x48df25, 0x48de33).
        var both = Strategic(MapPanelState.Empty with { LeaderName = "Лидер", CanDropRod = true, CanWandScroll = true, CanGuard = true });
        Assert.True(both.IsEnabled("BTN_DROP_ROD"));
        Assert.True(both.IsEnabled("BTN_GUARD"));
        Assert.True(both.IsEnabled("BTN_WAND_SCROLL"));
        // Лидер не умеет посохи — гаснет только посох; отряда нет — оба (как у созданной панели, 0x48fd0f/0x48fe95).
        var rodOnly = Strategic(MapPanelState.Empty with { LeaderName = "Лидер", CanDropRod = true });
        Assert.True(rodOnly.IsEnabled("BTN_DROP_ROD"));
        Assert.False(rodOnly.IsEnabled("BTN_WAND_SCROLL"));
        var none = Strategic(MapPanelState.Empty);
        Assert.Equal((false, false, false), (none.IsEnabled("BTN_DROP_ROD"), none.IsEnabled("BTN_WAND_SCROLL"), none.IsEnabled("BTN_GUARD")));
        Assert.Equal(UiLook.Disabled, none.Look("BTN_DROP_ROD"));
        Assert.Equal(UiLook.Disabled, none.Look("BTN_WAND_SCROLL"));
    }
}
