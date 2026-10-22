using System.Collections.Generic;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>Окно дипломатии (<c>CDiplomacyInterf</c>, кнопка <c>BTN_DIPLOMACY</c> панели команд): на месте карты — поле
/// <c>DLG_ISO_DIPLO</c> (<see cref="DiplomacyFieldLogic"/>), на месте панели команд — <c>DLG_DIPLOMACY</c>
/// (<see cref="DiplomacyPanelLogic"/>); «Назад» возвращает карту. Вопросы и окна панели — обычным стеком окон.</summary>
public partial class MapScreen
{
    private DiplomacyModel? _diplo;
    private UiScreenView? _diploPanel, _diploField;
    private IReadOnlyDictionary<string, string>? _diploRaces;
    private UiScreenView? _goldView;

    /// <summary>Полоса ресурсов на время дипломатии — только золото (<see cref="GoldPanelLogic"/>); закрыта — прежняя полоса.</summary>
    private void GoldOnly(bool on)
    {
        if (on && _goldView is null)
        {
            // Полосу снимаем из дерева (ShowMap показывает все экраны списка), как панель команд при подмене.
            if (_resourceView is { } bar && _screens.Remove(bar)) RemoveChild(bar);
            _goldView = AddScreen(GoldPanelLogic.Screen, Vector2.Zero,
                                  new GoldPanelLogic(() => _map.Viewer is { } id && _map.Map.Players.TryGetValue(id, out var p) ? p.Bank.Gold : 0));
        }
        else if (!on && _goldView is { } view)
        {
            _goldView = null;
            RemoveChild(view);
            view.QueueFree();
            _screens.Remove(view);
            if (_resourceView is { } bar && !_screens.Contains(bar))
            {
                AddChild(bar);
                _screens.Add(bar);
            }
        }
    }

    private bool DiplomacyOpen => _diploPanel is not null;

    /// <summary>Карту закрывает картинка столицы или поле дипломатии.</summary>
    private bool CoversMap => CapitalOpen || DiplomacyOpen;

    private void OpenDiplomacy()
    {
        if (DiplomacyOpen || CapitalOpen || Aiming || _dropStack is not null || Windows is not { } context || _panelView is null) return;
        _diplo = new DiplomacyModel(context, _diploRaces ??= RaceNames.Load(GameData.Rules));
        _diplo.Changed += () => Callable.From(SyncDiplomacy).CallDeferred();
        var panel = new UiScreenView
        {
            Hosted = DiplomacyPanelLogic.Screen, Name = DiplomacyPanelLogic.Screen, SlotAt = _panelView.SlotAt, SlotHost = _host,
            Logic = new DiplomacyPanelLogic(_diplo, SpellTexts),
        };
        panel.Fired += events =>
        {
            foreach (var e in events)
                if (e is UiClicked { Element: "BTN_BACK" }) Callable.From(CloseDiplomacy).CallDeferred();
            Drain(panel);
        };
        panel.Pointed += () => Drain(panel);   // правая кнопка над лицом выбранного — карточка игрока (DLG_R_C_PLAYER)
        RemoveChild(_panelView);
        _screens.Remove(_panelView);
        AddChild(panel);
        _screens.Add(panel);
        _diploPanel = panel;
        BuildDiplomacyField();
        int covered = CoverPalette();
        GoldOnly(true);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
        GD.Print($"diplomacy open {DiplomacyLine()} пал_выкл={covered}");
    }

    /// <summary>Поле строится заново на каждое изменение: курсоры шкал стоят по отношениям (форма экрана).</summary>
    private void BuildDiplomacyField()
    {
        if (_diplo is null) return;
        if (_diploField is { } old)
        {
            RemoveChild(old);
            old.QueueFree();
            _screens.Remove(old);
        }
        _diploField = new UiScreenView
        {
            Hosted = DiplomacyFieldLogic.Screen, Name = DiplomacyFieldLogic.Screen, SlotAt = _mapRect.Position, SlotHost = _host,
            SlotSize = new Vector2I((int)_mapRect.Size.X, (int)_mapRect.Size.Y),   // поле — по середине места карты (UiScreenView.SlotInset)
            Logic = new DiplomacyFieldLogic(_diplo),
        };
        var field = _diploField;
        field.Pointed += () => Drain(field);   // правая кнопка над портретом слота — карточка игрока
        AddChild(_diploField);
        MoveChild(_diploField, _mapBox.GetIndex() + 1);
        _screens.Add(_diploField);
    }

    private void SyncDiplomacy()
    {
        if (_diploPanel is not { } panel || panel.Logic is not DiplomacyPanelLogic logic) return;
        BuildDiplomacyField();
        logic.Refresh(panel.State);
        panel.Sync();
        if (_goldView is { Logic: GoldPanelLogic gold })
        {
            gold.Refresh(_goldView.State);
            _goldView.Sync();
        }
        Drain(panel);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    private void CloseDiplomacy()
    {
        if (_diploPanel is not { } panel || _panelView is null) return;
        _diploPanel = null;
        if (_diploField is { } field)
        {
            RemoveChild(field);
            field.QueueFree();
            _screens.Remove(field);
        }
        _diploField = null;
        _diplo = null;
        RemoveChild(panel);
        panel.QueueFree();
        _screens.Remove(panel);
        AddChild(_panelView);
        _screens.Add(_panelView);
        CoverPalette();
        GoldOnly(false);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    /// <summary>Шаг проверки «diplo N»: выбрать игрока слота N (1 — сам).</summary>
    private void DiplomacySelect(int slot)
    {
        if (_diplo is null) return;
        _diplo.Selected = System.Math.Clamp(slot - 1, 0, _diplo.Players.Count - 1);
        _diplo.Notify();
    }

    private string DiplomacyLine() => _diploPanel?.Logic is DiplomacyPanelLogic panel && _diploField?.Logic is DiplomacyFieldLogic field
        ? $"{field.Summary} {panel.Summary} золото={_map.Map.Players[_map.Viewer!].Bank.Gold} дань={(_diploPanel.State.IsEnabled("BTN_GIVE_RESS") ? 1 : 0)} " +
          $"союз={(_diploPanel.State.IsEnabled("BTN_PROPOSE_ALLIANCE") ? 1 : 0)} полоса_золота={(_goldView is { Visible: true } ? 1 : 0)}"
        : "дипломатии нет";
}
