using System.Collections.Generic;
using D2.Data.Battle;
using D2.Rules.Battle;

namespace D2.Godot;

/// <summary>Доступность «Защищаться», «Ждать», «Отступить» и <c>TOG_RIGHTUNITS</c> (<see cref="BattlePanelButtons"/>, Зс55): у движка они гаснут
/// на показ действия и на ходы ИИ и зажигаются к ходу человека; погашенные — кадр «погашена» разметки, щелчок и клавиша не проходят
/// (<see cref="D2.Data.Ui.UiScreenState.SetEnabled"/>).</summary>
public partial class BattleScreen
{
    /// <summary>От чего зависела доступность при прошлой сверке: пока ключ тот же, набор команд хода (<see cref="BattleSession.Allowed"/> —
    /// поиск целей) заново не считается.</summary>
    private (BattleSession Session, BattleUnit? Current, bool Awaits, bool Dragging, bool Closable, int Round, int Actions, int Log)? _buttonsKey;

    /// <summary>Ход юнита человека ждёт команды: показ прошлого действия кончился (<see cref="_playing"/>), автобоя нет, бой не кончен.</summary>
    private bool AwaitsHuman => !_playing && !_session.Finished && _session.WaitsPlayer;

    /// <summary>Поставить доступность кнопок панели по правилу движка; true — что-то сменилось (панель надо перерисовать). Зовётся каждым кадром
    /// (<see cref="_Process"/>: конец показа — отложенный шаг) и сборкой панелей (<see cref="PanelBackgrounds"/>).</summary>
    private bool SyncButtons()
    {
        if (_panel is null || _session is null) return false;
        (BattleSession Session, BattleUnit? Current, bool Awaits, bool Dragging, bool Closable, int Round, int Actions, int Log) key =
            (_session, _session.Current, AwaitsHuman, _itemDrag is not null, CanClose, _session.State.Round, _session.State.Actions.Count,
             _session.Log.Count);
        if (_buttonsKey == key) return false;
        _buttonsKey = key;
        var moment = new BattlePanelMoment(key.Awaits, key.Awaits ? _session.Allowed : EmptyCommands, key.Dragging, key.Closable);
        bool changed = false;
        foreach (string id in BattlePanelButtons.Elements)
        {
            bool on = BattlePanelButtons.Enabled(id, moment);
            if (_panel.State.IsEnabled(id) == on) continue;
            _panel.State.SetEnabled(id, on);
            changed = true;
        }
        return changed;
    }

    private static readonly IReadOnlySet<BattleCommand> EmptyCommands = new HashSet<BattleCommand>();
}
