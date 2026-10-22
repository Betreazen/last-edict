using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>Столица поверх карты (ответ пользователя 25.09): на месте карты — картинка столицы с постройками
/// (<see cref="CapitalView"/>), на месте панели команд <c>DLG_STRATEGIC</c> — панель <c>DLG_CAPITAL</c>. Переходы панели —
/// по графу экранов (<c>DLG_STRATEGIC.BTN_CAPITAL</c> → <c>Goto DLG_CAPITAL</c>, <c>BTN_BACK</c> → <c>Goto DLG_STRATEGIC</c>,
/// <c>BTN_UNITS</c>/<c>BTN_BUILD_STRUCTURE</c> → <c>Open</c> окна поверх).</summary>
public partial class MapScreen
{
    private UiNavigator? _panelNav;
    private UiScreenView? _capitalPanel;
    private CapitalPanelLogic? _capitalLogic;
    private CapitalView? _capitalView;
    private CapitalArt? _capitalArt;
    private IReadOnlyDictionary<string, BuildingText>? _buildingTexts;
    private UiScreenView? _buildingPanel, _palHidden;

    private UiNavigator PanelNav => _panelNav ??= new UiNavigator(GameData.Content.Flow, "DLG_STRATEGIC", UiPhase.Game);

    private bool CapitalOpen => _capitalPanel is not null;

    /// <summary>Открыть столицу игрока (<paramref name="capitalId"/> или его собственную).</summary>
    private void OpenCapital(string? capitalId = null)
    {
        if (CapitalOpen || Aiming || _dropStack is not null || Windows is not { } context || _panelView is null) return;
        capitalId ??= _map.Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital
                                                         && s.Owner.Equals(_map.Viewer, StringComparison.OrdinalIgnoreCase))?.Id;
        if (capitalId is null) return;
        if (PanelNav.Top == "DLG_STRATEGIC") PanelNav.Activate("BTN_CAPITAL");

        _capitalLogic = new CapitalPanelLogic(context, capitalId);
        var slot = _panelView.SlotAt ?? Vector2.Zero;
        _capitalPanel = new UiScreenView
        {
            Hosted = CapitalPanelLogic.Screen, Name = CapitalPanelLogic.Screen, SlotAt = slot, SlotHost = _host, Logic = _capitalLogic,
        };
        _capitalPanel.Fired += CapitalCommands;
        _capitalPanel.Released += CapitalPanelReleased;
        RemoveChild(_panelView);
        _screens.Remove(_panelView);
        AddChild(_capitalPanel);
        _screens.Add(_capitalPanel);

        _capitalArt ??= CapitalArt.Load(GameData.FromGame("data/ui/capital.json"));
        _capitalView = new CapitalView { Name = "Capital", Active = () => _dialogViews.Count == 0 };
        _capitalView.Hovered += ShowBuildingPanel;
        string capital = capitalId;
        _capitalView.Clicked += () =>
        {
            ShowBuildingPanel(null);
            OpenWindow(BuildLogic.Screen, CapitalWindow(BuildLogic.Screen, capital));
        };
        _capitalView.RightClicked += building =>
        {
            if (Windows is not { } ctx) return;
            ShowBuildingPanel(null);
            OpenWindow(BuildingCardLogic.Screen, new BuildingCardLogic(ctx, building, _buildingTexts ??= BuildLogic.LoadTexts(GameData.Rules)));
        };
        AddChild(_capitalView);
        MoveChild(_capitalView, _mapBox.GetIndex() + 1);
        ShowCapitalPicture();
        int covered = CoverPalette();
        var owner = context.Map.Players[context.Map.Sites.First(s => s.Id == capitalId).Owner];
        GD.Print($"capital open {capitalId} слоёв={_capitalView.Counts.Drawn} нет={_capitalView.Counts.Missing} пал_выкл={covered} " +
                 $"постройки={string.Join(",", owner.Buildings.DefaultIfEmpty("-"))}");
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    /// <summary>Кнопки палитры, которые движок выключает, пока карту закрывает столица или дипломатия (снимки 28.09: на карте включены,
    /// в <c>CCapitalInterf</c> и <c>CDiplomacyInterf</c> — тёмные), и пока на месте панели команд другая (<see cref="CommandPanelShown"/>).</summary>
    private static readonly string[] CoveredPalette = ["BTN_REPORT", "BTN_QUESTLOG", "BTN_OPTIONS"];

    /// <summary>На месте панели команд — она сама (<c>DLG_STRATEGIC</c>), не окно ожидания хода ИИ и не панель режима. Палитра у движка
    /// живёт по ней (<c>0x48d880</c>, Зс57): смена на <c>DLG_STRATEGIC</c> (<c>0x48fc5e</c>) зажигает «Задание», «Журнал» и «Настройки»,
    /// смена на <c>DLG_STRAT_WAIT</c> (ход ИИ, <c>0x48d858</c>), <c>DLG_STRAT_SPELL</c> (прицел, <c>0x490061</c>) и <c>DLG_DROP_BAG</c>
    /// (жезл, мешок, обмен, <c>0x490553</c>) — гасит (партия <c>20261020-faces</c>: строк −11/+3 у каждой).</summary>
    private bool CommandPanelShown => _panelView is { Visible: true } panel && _screens.Contains(panel) && !_map.AiRunning;

    /// <summary>Палитра по тому, закрыта ли карта (<see cref="CoversMap"/>) и стоит ли панель команд (<see cref="CommandPanelShown"/>);
    /// вернёт, сколько кнопок выключено.</summary>
    private int CoverPalette()
    {
        if (PaletteView is not { } pal) return 0;
        bool on = !CoversMap && CommandPanelShown;
        foreach (var id in CoveredPalette) pal.State.SetEnabled(id, on);
        pal.Sync();
        return on ? 0 : CoveredPalette.Length;
    }

    private void ShowCapitalPicture()
    {
        if (_capitalView is null || _capitalLogic is null || _capitalArt is null || Windows is not { } context) return;
        var owner = context.Map.Players[context.Map.Sites.First(s => s.Id == _capitalLogic.CapitalId).Owner];
        int raceType = _map.Session.Book!.Economy.Races.TryGetValue(owner.RaceId, out var race) ? race.RaceType : -1;
        _capitalView.Show(_capitalArt.Visible(raceType, owner.Buildings), GameData.Content.Gfx, GameData.GfxRoot);
    }

    private void CloseCapital()
    {
        if (_capitalPanel is not { } panel || _panelView is null) return;
        _capitalPanel = null;
        _capitalLogic = null;
        Callable.From(() =>
        {
            RemoveChild(panel);
            panel.QueueFree();
        }).CallDeferred();
        _screens.Remove(panel);
        AddChild(_panelView);
        _screens.Add(_panelView);
        ShowBuildingPanel(null);
        _capitalView?.QueueFree();
        _capitalView = null;
        CoverPalette();
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    /// <summary>Кнопки панели столицы — по графу: <c>Goto DLG_STRATEGIC</c> закрывает столицу, <c>Open</c> — окно поверх.</summary>
    private void CapitalCommands(IReadOnlyList<UiEvent> events)
    {
        foreach (var e in events)
        {
            if (e is not UiClicked { Element: var element } || _capitalLogic is not { } logic) continue;
            if (!logic.Allows(element, _capitalPanel!.State)) continue;
            if (element == "BTN_RESEARCH_SPELL")
            {
                OpenResearch();
                return;
            }
            if (element == "BTN_EXIT_CAPITAL")
            {
                string stack = logic.VisitorId;
                string? refused = stack.Length == 0 ? "нет гостя" : _map.Session.Run<string?>(new LastEdict.Data.Play.Commands.LeaveFortCommand(stack));
                if (refused is not null)
                {
                    GD.Print($"panel exit refused {stack} {refused.Replace(' ', '_')}");
                    continue;
                }
                CloseCapital();
                FocusStack(stack);
                return;
            }
            if (PanelNav.Activate(element) is not { } t) continue;
            GD.Print($"panel {t.Action} {t.From}.{t.Element} -> {t.Target ?? "-"}");
            if (t.Action == UiFlowAction.Goto && t.Target == "DLG_STRATEGIC")
            {
                CloseCapital();
                return;
            }
            if (t.Action == UiFlowAction.Open && t.Target is { } screen)
            {
                PanelNav.Pop();   // окно встаёт в стек окон карты, панель остаётся столицей
                ShowBuildingPanel(null);
                OpenWindow(screen, CapitalWindow(screen, logic.CapitalId));
            }
        }
    }

    /// <summary>Отпускание левой на портрете гостя панели столицы — окно армии (<c>CFortInterf</c> <c>0x4867ca</c>: точка в <c>IMG_LEADER</c>,
    /// у укрепления есть гость, <c>WM_LBUTTONUP</c> → <c>0x486c8e</c>). Двойной щелчок — тоже: в нём есть отпускание.</summary>
    private void CapitalPanelReleased(int x, int y)
    {
        if (_capitalLogic is not { } logic || logic.VisitorId.Length == 0 || _dialogViews.Count > 0) return;
        var leader = GameData.Content.Screens.First(s => s.Id == CapitalPanelLogic.Screen).Elements.First(e => e.Id == "IMG_LEADER").Rect;
        if (x < leader.X || x >= leader.Right || y < leader.Y || y >= leader.Bottom) return;
        ShowBuildingPanel(null);
        OpenCityArmy(logic.CapitalId);
    }

    private IScreenLogic? CapitalWindow(string screen, string capitalId) => Windows is not { } context ? null : screen switch
    {
        CityStackLogic.Screen => new CityStackLogic(context, capitalId, GameData.Content.Screens),
        BuildLogic.Screen => new BuildLogic(context, GameData.Content.Flow, GameData.Content.Screens,
                                            _buildingTexts ??= BuildLogic.LoadTexts(GameData.Rules)),
        _ => null,
    };

    /// <summary>Панель столицы и картинка — заново (после окон: наняли героя, построили здание).</summary>
    private void SyncCapital()
    {
        if (_capitalPanel is null || _capitalLogic is null) return;
        _capitalLogic.Refresh(_capitalPanel.State);
        _capitalPanel.Sync();
        ShowCapitalPicture();
    }

    /// <summary>Постройка под мышью — панель <c>DLG_BUILDING</c> на месте <c>DLG_ISO_PAL</c>; ушла — прежняя панель.</summary>
    private void ShowBuildingPanel(string? building)
    {
        if (_buildingPanel is { } shown)
        {
            RemoveChild(shown);
            shown.QueueFree();
            _screens.Remove(shown);
            _buildingPanel = null;
            if (_palHidden is { } pal)
            {
                AddChild(pal);
                _screens.Add(pal);
            }
            _palHidden = null;
        }
        if (building is null || Windows is not { } context || PaletteView is not { } palView) return;
        _buildingPanel = new UiScreenView
        {
            Hosted = BuildingPanelLogic.Screen, Name = BuildingPanelLogic.Screen, SlotAt = palView.SlotAt, SlotHost = _host,
            Logic = new BuildingPanelLogic(context, building),
        };
        RemoveChild(palView);
        _screens.Remove(palView);
        _palHidden = palView;
        AddChild(_buildingPanel);
        _screens.Add(_buildingPanel);
    }

    /// <summary>Строка проверки картинки столицы.</summary>
    private string CapitalLine() => _capitalView is { } v
        ? $"постройка={v.Hover ?? "-"} пал={(_buildingPanel is not null ? BuildingPanelLogic.Screen : "DLG_ISO_PAL")} анимаций={v.Animated}"
        : "столицы нет";

    private void PlaceCapital(Vector2 size, float k)
    {
        if (_capitalView is null) return;
        // Картинка 640×600 — по середине места карты (на холсте шире 800 оно шире картинки), поля — растяжкой картинки (CapitalView.Area).
        var inset = ((_mapRect.Size - CapitalView.PictureSize) / 2f).Floor();
        _capitalView.Position = ((size - (_host * k)) / 2f) + ((_mapRect.Position + inset) * k);
        _capitalView.Scale = new Vector2(k, k);
        _capitalView.Area = _mapRect.Size;
    }
}
