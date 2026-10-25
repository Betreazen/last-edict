using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Бой двух людей по сети (Э7, решение пользователя 25.10 «каждый своих», <c>docs/network.md</c> §4): оба экрана ведут один и тот же бой
/// (одно состояние, копии одного генератора боя и CRT), каждый — свои юниты. Юниты другой стороны (<see cref="RemoteSide"/>) человек этого
/// экрана не водит: их решения приходят по сети (<see cref="PushRemote"/>) и исполняются тем же путём, что щелчок, — с показом; свои решения
/// уходят другой стороне (<see cref="LocalDecision"/>). «Быстрый бой» и автобой стороны в бою людей выключены (их миг у двух экранов разный).
/// Автобой решает только экран, который бой записывает (<see cref="RecordsBattle"/>): другая сторона молчит дольше
/// <see cref="RemoteWaitSeconds"/> (обрыв связи) — её юниты доигрывает ИИ, срок хода вышел — автобой всего боя; это тоже решения, они
/// уходят другому экрану, и бой у обоих тот же. Второй экран ждёт дольше (<see cref="SecondWaitSeconds"/>): его итог — только показ.
/// </summary>
public partial class BattleScreen
{
    /// <summary>Чья сторона — у другого человека: true — нападающие, false — защитники, null — боя людей нет.</summary>
    public bool? RemoteSide { get; init; }

    /// <summary>Своё решение в бою людей (атака, защита, ожидание, отступление, предмет) — другой стороне.</summary>
    public event Action<BattleInput>? LocalDecision;

    /// <summary>Экран подавшего <c>startBattle</c>: его действия — итог боя (хост повторит их), его автобой — решение для обоих экранов.</summary>
    public bool RecordsBattle { get; init; }

    /// <summary>Сколько ждать решения другой стороны, секунд, прежде чем её юниты поведёт ИИ (экран, который бой записывает).</summary>
    public const double RemoteWaitSeconds = 90;

    /// <summary>То же у второго экрана: записывающий молчит так долго — связи нет, бой этого экрана — только показ.</summary>
    public const double SecondWaitSeconds = 180;

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

    /// <summary>Действие записано сессией: своё решение — другой стороне (решения другой стороны, начало хода и ходы ИИ — нет; автобой —
    /// только экрана, который бой записывает).</summary>
    private void OnRecorded(BattleInput input)
    {
        if (RemoteSide is null || _applyingRemote) return;
        if (input is StartTurnAction or AiDecideAction or ResolveAction) return;
        if (input is PlayOutAction or SetAutoAction && !RecordsBattle) return;
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
        double wait = RecordsBattle ? RemoteWaitSeconds : SecondWaitSeconds;
        if (Now - _remoteSince.Value < wait * 1000) return;
        GD.Print($"battle remote молчит {wait} с — его юниты ведёт ИИ");
        _remoteSince = null;
        _session.SetAuto(side, true);
        Continue();
    }

    /// <summary>Решение другой стороны — тем же путём, что щелчок своего человека (показ удара, надписи, звук). Решение не из списка или
    /// не к этому бою (цели нет) — мимо, с записью в журнал: экран не падает от чужого кадра.</summary>
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
                case SetAutoAction or PlayOutAction:   // автобой записывающего экрана (тайм-аут, срок хода)
                    _session.Apply(input);
                    if (input is PlayOutAction) Refresh();
                    Continue();
                    break;
                default:
                    GD.PrintErr($"battle remote: решение {input.GetType().Name} не к этому бою — мимо");
                    break;
            }
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            GD.PrintErr($"battle remote: решение {input.GetType().Name} не исполнено: {e.Message}");
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
            // «Закрыть» — по показу конца: у боя по запросу хоста его покажет хозяин позже (EndAiBattle), а закрытие до показа
            // никто не услышит — ход встанет.
            _closeWhenShown = true;
            if (_endShown)
            {
                _scriptClosing = true;
                AutoClose();
            }
            return;
        }
        if (!AwaitsHuman || RemoteTurn) return;   // юниты другого человека водит он
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
