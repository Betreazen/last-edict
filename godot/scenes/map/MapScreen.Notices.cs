using System;
using System.Linq;
using D2.Data.Play;
using D2.Data.Ui.Windows;
using Godot;

namespace D2.Godot;

/// <summary>Команды сервера клиенту без своего окна в ядре (<see cref="NoticeWatch"/>): выделение отряда после <c>L_CREATE_STACK</c> и
/// <c>L_CHANGE_OWNER</c> (<c>CCmdStackActionMsg</c> 1/2), раскрытый шпион (<c>CCmdDiscoverSpyMsg</c>), гибель иллюзии
/// (<c>CCmdStackIllusionMsg</c>), взятые ресурсы столицы (<c>CCmdCaptureResourcesMsg</c>). Окна — в общую очередь окон поверх карты; в ход
/// ИИ сообщения — сразу, ход ждёт их, как попапа события (<see cref="ShowAiPopups"/>).</summary>
public partial class MapScreen
{
    private NoticeWatch? _noticeWatch;
    private GameSession? _noticeSession;

    /// <summary>Сборщик подписан на нынешнюю партию (новая партия после загрузки — новый сборщик).</summary>
    private void WatchNotices()
    {
        var session = _map.Session;
        if (ReferenceEquals(_noticeSession, session)) return;
        if (_noticeSession is not null && _noticeWatch is not null) _noticeSession.Unwatch(_noticeWatch);
        _noticeSession = session;
        _noticeWatch = new NoticeWatch(id => session.State.Info.Humans.Contains(id, StringComparer.OrdinalIgnoreCase));
        session.Watch(_noticeWatch);
    }

    /// <summary>Разобрать накопленное для того, кто за ПК: в свой ход — выделения и окна, в ход ИИ — только окна.</summary>
    private void PumpNotices(bool messagesOnly)
    {
        if (_map.Viewer is not { } viewer || _noticeWatch is null) return;
        foreach (var sight in _noticeWatch.Take(viewer, messagesOnly)) Notice(sight, viewer);
    }

    private void Notice(TurnSight sight, string viewer)
    {
        var session = _map.Session;
        switch (sight.Kind)
        {
            // Действие 1 (0x491722 → 0x5cdca5 → 0x5cdd8c): отряд зрителя вне укрепления — выделить (CTaskSelectUnit).
            case TurnSightKind.Created or TurnSightKind.OwnerChanged
                when session.Map.Stacks.TryGetValue(sight.Id, out var stack) && stack.Owner.Equals(viewer, StringComparison.OrdinalIgnoreCase):
                if (stack.Inside.Length == 0) _map.Select(stack.Id);
                GD.Print($"notice select stack={stack.Id} {(stack.Inside.Length == 0 ? "выделен" : "в_укреплении")}");
                break;
            // Действие 2 (0x48d8f3): прежнему хозяину — снять выделение, если выделен этот отряд.
            case TurnSightKind.OwnerChanged:
                if (string.Equals(_map.Selected, sight.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _map.Deselect(sight.Id);
                    GD.Print($"notice deselect stack={sight.Id}");
                }
                break;
            case TurnSightKind.ResourcesCaptured:
                _overlayQueue.Enqueue((CaptureResourcesLogic.Screen, new CaptureResourcesLogic(sight.Bank, Text), "BTN_OK"));
                GD.Print($"notice resources loser={sight.Id} казна={sight.Bank.Gold}/{sight.Bank.Infernal}/{sight.Bank.Life}/" +
                         $"{sight.Bank.Death}/{sight.Bank.Runic}/{sight.Bank.Grove}");
                break;
            // CCmdLootRuinMsg грабителю (0x489787 → 0x4cea43): «Вы победили!» и найденное золото, звук ENTRRUIN — до окна предмета.
            case TurnSightKind.Looted:
            {
                string loot = NoticeWatch.RuinLoot(sight.Bank, Text);
                _overlayQueue.Enqueue((MessageBoxLogic.Screen, new NoticeLogic(loot, NoticeWatch.RuinSound), "BTN_OK,BTN_YES,BTN_NO"));
                GD.Print($"notice Looted for={sight.PlayerId} ruin={sight.Id} золото={sight.Bank.Gold} текст={loot.Replace(' ', '_').Replace('\n', '/')}");
                break;
            }
            // CCmdChatMsg адресату (0x48c3d1 → CAIChatInterf 0x4b9747): окно DLG_CHAT_AI поверх карты — в ходе ИИ поверх панели ожидания,
            // ход ИИ ждёт «Закрыть» (AiPopupShowing); после ответа на предложение — за его окном. Корпус: 159 сообщений человеку — 159 окон.
            case TurnSightKind.AiChat when session.Book is { } book:
            {
                var chat = ChatLogic.Of(sight, session.Map, book, _raceNames ??= RaceNames.Load(GameData.Rules), Text);
                OpenWindow(ChatLogic.Screen, chat);
                GD.Print($"notice AiChat for={sight.PlayerId} from={sight.Id} {chat.Summary}");
                break;
            }
            default:
                if (NoticeWatch.Text(sight, session.Map, GameData.Catalog, Text, Windows is { } w ? w.UnitName : id => id) is not { } text)
                    break;
                _overlayQueue.Enqueue((MessageBoxLogic.Screen, new NoticeLogic(text), "BTN_OK,BTN_YES,BTN_NO"));
                GD.Print($"notice {sight.Kind} for={sight.PlayerId} other={sight.Id} текст={text.Replace(' ', '_')}");
                break;
        }
    }
}
