using System;
using System.Threading;
using LastEdict.Net;

namespace LastEdict.Godot;

/// <summary>
/// Сетевая партия на карте (Э7, <c>docs/network.md</c> §7). Чужой ход — тот же режим, что ход ИИ (<see cref="EndTurn"/>): ввод выключен,
/// вместо панели команд — окно ожидания, а рабочий поток ждёт своего хода. Пока ждёт, он у хоста исполняет конец хода клиента — его ходы ИИ
/// показываются, как свои (крючки <see cref="AiHooks"/> работают на этом потоке), — а у клиента показывает бои хода ИИ, о которых просит
/// хост (тот же экран боя, что у боя ИИ с человеком). Ход вернулся (или партия кончилась) — рабочий выходит, панель команд — назад.
/// </summary>
public partial class MapView
{
    /// <summary>Сетевая партия; null — обычная.</summary>
    public INetPlay? Net { get; set; }

    /// <summary>Как часто рабочий поток смотрит, не его ли ход.</summary>
    private const int NetPollMs = 25;

    /// <summary>Не свой ход в сети — ждать его в режиме хода ИИ. Уже ждём или идёт свой ход — ничего.</summary>
    public void WaitOthers()
    {
        if (Net is not { } net || Walking || AiRunning || net.MyTurn) return;
        BeginAiMode(NetWait);
    }

    /// <summary>Ждать своего хода (рабочий поток): работа хоста, бои клиента, иначе — опрос.</summary>
    private void NetWait()
    {
        var net = Net!;
        while (!_aiCancel)
        {
            if (net.TryTakeWork(out var work))
            {
                work();   // у хоста — конец хода клиента: ходы ИИ идут на этом потоке с показом
                continue;
            }
            if (net.TryTakeBattle(out var request))
            {
                ClientBattle(net, request);
                continue;
            }
            if (net.MyTurn || net.Over) return;
            Thread.Sleep(NetPollMs);
        }
    }

    /// <summary>Клиент: бой хода ИИ, о котором просит хост, — собрать по виду, сыграть экраном боя с человеком (как бой ИИ с человеком),
    /// ответить хосту действиями. Не собрался или экран занят — ответ «без боя»: хост играет автобой.</summary>
    private void ClientBattle(INetPlay net, BattleRequest request)
    {
        var kit = net.BuildBattle(request);
        if (kit is null || AiBattleRequested is null)
        {
            if (!request.Lockstep) net.ReplyBattle(request, null, []);   // бой хода ИИ без экрана — автобой у хоста
            return;
        }
        _aiShown = Session.State;   // вид после хода до боя — пришёл раньше запроса
        var battle = new AiBattle(kit.State, this) { Rng = kit.Rng, Crt = kit.Crt, RemoteSide = kit.RemoteSide };
        AskOnWorker(new AiBattleRequest(battle));
        if (request.Lockstep) return;   // бой людей записывает экран подавшего, этот экран вёл только свои юниты
        var log = battle.Log ?? (_aiCancel ? battle.ResolveOnce() : null);
        var play = log is null ? null : battle.Played?.Invoke();
        net.ReplyBattle(request, play, log ?? []);
    }

    /// <summary>Заявка показа с рабочего потока сети (как <see cref="AiHooks"/>): положить и ждать, пока главный поток её покажет.</summary>
    private void AskOnWorker(AiRequest request)
    {
        _aiRequest = request;
        while (!_aiResume.Wait(100))
            if (_aiCancel) break;
    }

    /// <summary>Партия сменилась без своей команды (ход другого человека, новый вид): пока ждём чужого хода и ничего не показываем —
    /// карта по новой партии. Главный поток.</summary>
    public void NetChanged()
    {
        if (!AiRunning)
        {
            RefreshGround();
            Reload();
            Marks();
            Changed?.Invoke();
            return;
        }
        if (_aiShowing is not null || _aiRequest is not null || _aiWalk is not null) return;
        _aiShown = Session.State;
        RefreshGround();
        Reload();
    }
}
