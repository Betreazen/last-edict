using LastEdict.Data.Game;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Menu;

/// <summary>
/// Новая своя кампания (<c>DLG_NEW_CUSTOM_TOURNEMENT</c>, <c>CMenuCustomCampaign</c> <c>0x4dba1e</c>; разбор — <c>docs/day-cycle.md</c> §15).
/// Список <c>TLBOX_GAME_SLOT</c> — свои кампании (<see cref="GameCatalog.Campaigns"/> с <see cref="CampaignInfo.Custom"/>): строка
/// <c>X005TA0739</c> (имя, раса), первая выбрана сама. У выбранной (<c>0x4dbd63</c>): число глав (<c>TXT_QTY</c>), уровень лидера
/// (<c>TXT_LEVEL</c>), автор (<c>TXT_AUTHOR</c>), значок расы (<c>IMG_RACE_1</c>); описание окно не показывает. «Загрузить»
/// (<c>0x4dbebd</c>) — первая глава, раса — только раса кампании (<c>0x4eae60</c>), дальше выбор расы и лорда как у квеста.
/// </summary>
internal sealed class CustomCampaignLogic(MenuContext context) : IScreenLogic
{
    private const string List = "TLBOX_GAME_SLOT";
    private IReadOnlyList<CampaignInfo> _campaigns = [];

    public void Open(UiScreenState ui)
    {
        _campaigns = [.. context.Catalog.Campaigns.Where(c => c.Custom && c.Chapters.Count > 0)];
        ui.SetItems(List, [.. _campaigns.Select(c => UiTemplate.Fill(context.Text("X005TA0739"), ("NAME", c.Name),
            ("RACE", context.Text(context.Catalog.Race(c.Race).NameText))))]);
        ui.SetEnabled("BTN_LOAD", _campaigns.Count > 0);
        if (_campaigns.Count == 0) return;
        ui.Select(List, 0);
        Show(ui, 0);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiListSelected { Element: List, Index: var i }:
                Show(ui, i);
                break;
            case UiClicked { Element: "BTN_LOAD" } when ui.ListSelected(List) is var i && i >= 0 && i < _campaigns.Count:
                Choose(_campaigns[i]);
                break;
        }
    }

    public bool Allows(string element, UiScreenState ui) => element != "BTN_LOAD" || _campaigns.Count > 0;

    private void Show(UiScreenState ui, int index)
    {
        if (index < 0 || index >= _campaigns.Count) return;
        var c = _campaigns[index];
        ui.SetText("TXT_QTY", c.Chapters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ui.SetText("TXT_LEVEL", c.LeaderLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        ui.SetText("TXT_AUTHOR", c.Author);
        ui.SetImage("IMG_RACE_1", $"DLG_HOTSEAT_LOBBY_RACE_R{context.Catalog.Race(c.Race).Art}");
    }

    private void Choose(CampaignInfo campaign)
    {
        context.Setup.Saga = false;
        context.Setup.Scenario = campaign.Chapters[0].Scenario;
        context.Setup.Campaign = new Play.CampaignRun(campaign.Id, 0, campaign.Chapters.Count, Custom: true);
        context.Setup.Carry = null;
        context.Setup.RaceLimit = [campaign.RaceId.Length > 0 ? campaign.RaceId : context.Catalog.Race(campaign.Race).Id];
    }
}
