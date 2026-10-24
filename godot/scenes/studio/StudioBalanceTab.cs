using System;
using System.Globalization;
using LastEdict.Authoring.Workspaces;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Пространство «Баланс / Арена» (<see cref="BalanceWorkspace"/>): сценарий, «Арена» — бои пар отрядов без мода и с модом на одних зёрнах,
/// «Стенд» — партии ИИ, доли побед рас; таблица «было / стало». Без запуска игры.
/// </summary>
public partial class StudioBalanceTab : VBoxContainer, IStudioTab
{
    private readonly BalanceWorkspace _ws;
    private readonly OptionButton _scenario = new() { CustomMinimumSize = new Vector2(260, 0) };
    private readonly SpinBox _pairs = new() { MinValue = 1, MaxValue = 32, Value = 8, Prefix = "пар" };
    private readonly SpinBox _seeds = new() { MinValue = 1, MaxValue = 200, Value = 20, Prefix = "зёрен" };
    private readonly SpinBox _games = new() { MinValue = 1, MaxValue = 200, Value = 24, Prefix = "партий" };
    private readonly SpinBox _days = new() { MinValue = 5, MaxValue = 120, Value = 30, Prefix = "дней" };
    private readonly ItemList _table = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _note = new() { ThemeTypeVariation = SettingsTheme.HelpLabel };

    public StudioBalanceTab(BalanceWorkspace ws)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        foreach (string s in ws.Scenarios) _scenario.AddItem(s);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var arena = new Button { Text = "Арена", ThemeTypeVariation = SettingsTheme.Primary };
        arena.Pressed += () => _ = ShowArena();
        var stand = new Button { Text = "Стенд" };
        stand.Pressed += () => _ = ShowStand();
        foreach (var c in new Control[] { new Label { Text = "Сценарий:" }, _scenario, _pairs, _seeds, arena, _games, _days, stand }) row.AddChild(c);
        AddChild(row);
        AddChild(_note);
        AddChild(_table);
    }

    public void Sync()
    {
    }

    private string? Scenario => _scenario.Selected >= 0 ? _scenario.GetItemText(_scenario.Selected) : null;

    /// <summary>Прогон без кнопки (проверка): сценарий по имени, арена или стенд.</summary>
    public System.Threading.Tasks.Task Run(string scenario, bool arena)
    {
        for (int i = 0; i < _scenario.ItemCount; i++)
            if (_scenario.GetItemText(i) == scenario) _scenario.Select(i);
        return arena ? ShowArena() : ShowStand();
    }

    /// <summary>Идёт прогон — кнопки ждут.</summary>
    private bool _running;

    /// <summary>Прогон в рабочем потоке (окно не замирает), итог и отказ — на главном.</summary>
    private async System.Threading.Tasks.Task<T?> Background<T>(Func<T> run, string what) where T : class
    {
        if (_running) return null;
        _running = true;
        _note.Text = $"{what}: идёт прогон…";
        try
        {
            var result = await System.Threading.Tasks.Task.Run(run);
            if (_ws.Error is { } error) _ws.Session.Refuse(error);
            return result;
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>Бои пар отрядов: «нападающий — защитник: побед было → стало, ходов было → стало».</summary>
    public async System.Threading.Tasks.Task ShowArena()
    {
        if (Scenario is not { } scenario) return;
        int pairs = (int)_pairs.Value, seeds = (int)_seeds.Value;
        if (await Background(() => _ws.RunArena(scenario, pairs, seeds), "арена") is not { } rows) return;
        _table.Clear();
        foreach (var r in rows)
        {
            int i = _table.AddItem(string.Create(CultureInfo.InvariantCulture,
                $"{r.AttackerName} ({r.Attacker}) против {r.DefenderName} ({r.Defender}):  побед {r.BaseShare:P0} → {r.ModShare:P0},  ходов {r.BaseTurns:0.0} → {r.ModTurns:0.0}"));
            if (r.BaseWins != r.ModWins) _table.SetItemCustomFgColor(i, r.ModWins > r.BaseWins ? new Color("#cfe8b0") : new Color("#e07a5f"));
        }
        _note.Text = $"арена: {rows.Count} пар × {(int)_seeds.Value} зёрен, без мода и с модом — {_ws.Millis} мс";
    }

    /// <summary>Партии ИИ: «раса: мест, побед было → стало».</summary>
    public async System.Threading.Tasks.Task ShowStand()
    {
        if (Scenario is not { } scenario) return;
        int games = (int)_games.Value, days = (int)_days.Value;
        if (await Background(() => _ws.RunStand(scenario, games, days), "стенд") is not { } rows) return;
        _table.Clear();
        foreach (var r in rows)
            _table.AddItem(string.Create(CultureInfo.InvariantCulture, $"{_ws.Session.NameOf(r.Race)}: мест {r.Seats}, побед {r.BaseShare:P0} → {r.ModShare:P0}"));
        _note.Text = $"стенд: {(int)_games.Value} партий до {(int)_days.Value} дней, без мода и с модом — {_ws.Millis} мс";
    }
}
