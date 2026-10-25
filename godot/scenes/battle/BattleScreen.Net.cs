using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Бой двух людей по сети (Э7, решение пользователя 25.10 «каждый своих», <c>docs/network.md</c> §4): оба экрана ведут один и тот же бой
/// (одно состояние, копии одного генератора боя и CRT), каждый — свои юниты. Юниты другой стороны (<see cref="RemoteSide"/>) человек этого
/// экрана не водит: их решения приходят по сети (<see cref="PushRemote"/>) и исполняются тем же путём, что щелчок, — с показом; свои решения
/// уходят другой стороне (<see cref="LocalDecision"/>). «Быстрый бой» и автобой стороны в бою людей выключены (их миг у двух экранов разный);
/// другая сторона молчит дольше <see cref="RemoteWaitSeconds"/> — её юниты доигрывает ИИ (как при обрыве связи).
/// </summary>
public partial class BattleScreen
{
    /// <summary>Чья сторона — у другого человека: true — нападающие, false — защитники, null — боя людей нет.</summary>
    public bool? RemoteSide { get; init; }

    /// <summary>Своё решение в бою людей (атака, защита, ожидание, отступление, предмет) — другой стороне.</summary>
    public event Action<BattleInput>? LocalDecision;

    /// <summary>Сколько ждать решения другой стороны, секунд, прежде чем её юниты поведёт ИИ.</summary>
    public const double RemoteWaitSeconds = 90;

    private readonly Queue<BattleInput> _remote = new();
    private bool _applyingRemote;
    private double? _remoteSince;

    /// <summary>Сейчас ход юнита другого человека (и его автобой не включён по обрыву).</summary>
    public bool RemoteTurn => RemoteSide is { } side && _session is not null && _session.Current is { } unit && unit.Attacker == side
                              && !_session.AutoOf(side);

    /// <summary>Решение другой стороны пришло — исполнится, когда бой дойдёт до хода её юнита.</summary>
    public void PushRemote(BattleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (RemoteSide is null) return;
        _remote.Enqueue(input);
    }

    /// <summary>Действие записано сессией: своё решение — другой стороне (решения другой стороны, начало хода и ходы ИИ — нет).</summary>
    private void OnRecorded(BattleInput input)
    {
        if (RemoteSide is null || _applyingRemote) return;
        if (input is StartTurnAction or AiDecideAction or ResolveAction or PlayOutAction or SetAutoAction) return;
        LocalDecision?.Invoke(input);
    }

    /// <summary>Каждый кадр: ход юнита другой стороны и показ кончился — исполнить его решение, если пришло; долго нет — ИИ за неё.</summary>
    private void StepRemote()
    {
        if (RemoteSide is not { } side || _session is null) return;
        if (_playing || _message is not null || _session.Finished || !_session.WaitsPlayer || !RemoteTurn)
        {
            _remoteSince = null;
            return;
        }
        if (_remote.TryDequeue(out var input))
        {
            _remoteSince = null;
            ApplyRemote(input);
            return;
        }
        _remoteSince ??= Now;
        if (Now - _remoteSince.Value < RemoteWaitSeconds * 1000) return;
        GD.Print($"battle remote молчит {RemoteWaitSeconds} с — его юниты ведёт ИИ");
        _remoteSince = null;
        _session.SetAuto(side, true);
        Continue();
    }

    /// <summary>Решение другой стороны — тем же путём, что щелчок своего человека (показ удара, надписи, звук).</summary>
    private void ApplyRemote(BattleInput input)
    {
        _applyingRemote = true;
        try
        {
            GD.Print($"battle remote {input.GetType().Name.Replace("Action", "", StringComparison.Ordinal)} ходящий={_session.Current?.Id ?? "-"}");
            switch (input)
            {
                case AttackAction { Target: { } id } when _session.State.Unit(id) is { } target:
                    Attack(target);
                    break;
                case SummonAction summon when _session.Current is { } summoner:
                    Attack(summoner, summon.Slot);
                    break;
                case DefendAction:
                    if (_session.Defend()) BeforeAction();
                    ContinueAfterTurnShow();
                    break;
                case WaitAction:
                    if (_session.Wait()) BeforeAction();
                    ContinueAfterTurnShow();
                    break;
                case RetreatAction:
                    if (_session.Retreat()) BeforeAction();
                    Continue();
                    break;
                case UseItemAtAction or UseItemOnAction or UseItemAction:
                    int uses = _session.ItemUses.Count;
                    BeforeAction();
                    _session.Apply(input);
                    if (_session.ItemUses.Count > uses) ShowItem(_session.ItemUses[^1]);
                    else Continue();
                    break;
                default:
                    _session.Apply(input);
                    Continue();
                    break;
            }
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    /// <summary>Драйвер двух машин (<c>tools/net-duo.py</c>): свои решения экран принимает сам — удар по первой цели (призыв — на первую
    /// позицию), иначе защита; бой кончился — «Закрыть». Решения другой стороны — по сети, как у человека.</summary>
    public bool Scripted { get; init; }

    private bool _scriptClosing;

    private void StepScript()
    {
        if (!Scripted || _session is null || _scriptClosing) return;
        if (_session.Finished)
        {
            if (CanClose)
            {
                _scriptClosing = true;
                AutoClose();
            }
            else _closeWhenShown = true;
            return;
        }
        if (!AwaitsHuman) return;
        if (_session.SummonSlots() is { Count: > 0 } cells && _session.Current is { } summoner) Attack(summoner, cells.First());
        else if (_session.Targets() is { Count: > 0 } targets) Attack(targets[0]);
        else
        {
            if (_session.Defend()) BeforeAction();
            ContinueAfterTurnShow();
        }
    }

    /// <summary>Команды панели, разрешённые этому экрану: в бою людей — без «Быстрого боя» и автобоя.</summary>
    private IReadOnlySet<BattleCommand> LocalAllowed(IReadOnlySet<BattleCommand> allowed) =>
        RemoteSide is null ? allowed : allowed.Where(c => c is not (BattleCommand.Resolve or BattleCommand.Auto)).ToHashSet();
}
