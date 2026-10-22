using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Ui.Windows;
using Godot;

namespace D2.Godot;

/// <summary>Окна полученных предметов и заклинаний (<see cref="GiveItemLogic"/>, <c>DLG_ITEM</c>; сессия Б): сборщик
/// <see cref="GivenWatch"/> подписан на партию, окна открываются поверх карты по одному, когда других окон нет, — раньше попапов событий
/// того же действия (у движка сообщение 44 приходит сразу за подбором, <c>docs/ui-windows.md</c> §14), и не раньше конца анимации хода:
/// очередь команд клиента (<c>CMidCommandQueue2</c>, <c>0x410236</c>) ждёт анимации команды хода (<c>CMidAnim2System</c> <c>0x40b0ba</c>),
/// открытое окно держит её до закрытия (<c>0x4d97b6</c>). У порта ядро переставляет отряд в конце шага по карте — окно и так после.</summary>
public partial class MapScreen
{
    private GivenWatch? _givenWatch;
    private D2.Data.Play.GameSession? _givenSession;
    private readonly Queue<GiveItemLogic> _givenQueue = new();

    /// <summary>Сборщик подписан на нынешнюю партию (новая партия после загрузки — новый сборщик).</summary>
    private void WatchGiven()
    {
        var session = _map.Session;
        if (ReferenceEquals(_givenSession, session)) return;
        if (_givenSession is not null && _givenWatch is not null) _givenSession.Unwatch(_givenWatch);
        _givenQueue.Clear();
        _givenSession = session;
        _givenWatch = new GivenWatch(id => session.State.Info.Humans.Contains(id, StringComparer.OrdinalIgnoreCase), () => _map.Viewer);
        session.Watch(_givenWatch);
    }

    /// <summary>Окна полученного зрителя ещё впереди или одно открыто: попапы событий ждут.</summary>
    private bool GivenPending =>
        _givenQueue.Count > 0 || _logics.Any(l => l is GiveItemLogic)
        || (_map.Viewer is { } viewer && _givenWatch?.Has(viewer) == true);

    /// <summary>Следующее окно полученного, когда других окон нет: сообщения зрителя по порядку, в сообщении — окно на тип предмета
    /// или на заклинание (<see cref="GivenWatch.Entries"/>).</summary>
    private void AskGiven()
    {
        if (_leaving || _map.AiRunning || _map.Walking || _battle is not null || _overlay is not null || _logics.Count > 0 || _dialogsStale
            || Windows is not { } context || _map.Viewer is not { } viewer || _givenWatch is null) return;
        if (_givenQueue.Count == 0)
            foreach (var sight in _givenWatch.Take(viewer))
            {
                var entries = GivenWatch.Entries(sight, _map.Map);
                for (int i = 0; i < entries.Count; i++) _givenQueue.Enqueue(new GiveItemLogic(context, entries[i], i == 0, SpellTexts));
            }
        if (_givenQueue.Count == 0) return;
        var logic = _givenQueue.Dequeue();
        GD.Print($"giveitem window human={viewer} {logic.Summary}");
        OpenWindow(logic.Screen, logic);
        // TAKEBAG — за первым окном сообщения, и у пустого мешка (сессия Г, WindowSounds.OfGiven).
        if (D2.Data.Sound.WindowSounds.OfGiven(logic.First) is { } sound) GameAudio.Of(this).PlayFx(D2.Data.Sound.WindowSounds.Archive, sound);
    }

    /// <summary>Шаг проверки <c>given</c>: сборщику — сообщение зрителю, как от ядра, и показ.</summary>
    private int GivenForCheck(string what)
    {
        WatchGiven();
        if (_map.Viewer is not { } viewer || _givenWatch is null) return -1;
        var map = _map.Map;
        var kind = what == "spell" ? D2.Data.Play.TurnSightKind.SpellsGiven : D2.Data.Play.TurnSightKind.ItemsGiven;
        IReadOnlyList<string> ids = what switch
        {
            "empty" => [],
            "spell" => [.. SpellTexts.Keys.Order(StringComparer.Ordinal).Take(2)],
            _ => map.Stacks.Values.Where(s => s.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase) && s.Items.Count > 0)
                                  .Select(s => s.Items).FirstOrDefault() ?? [],
        };
        _givenWatch.Happened(new D2.Data.Play.TurnSight(kind, viewer, 0, 0, "CHECK") { Ids = ids, Library = what == "spell" });
        PumpOverlays();
        return ids.Count;
    }
}
