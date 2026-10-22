using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Play;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Эллипс выделения (<c>SELECTED</c>, слой <c>SYM_STACK_SEL</c>) на время хода своего отряда (реестр Ин49). Движок снимает его, когда
/// отдаёт команду хода (<c>CTaskSelectUnit</c> <c>0x4d2260</c>: команда <c>0x40619b</c>, затем <c>0x4d0252</c> — снять эллипс в клетке
/// отряда), а ставит снова только по извещению о дошедшем отряде (слот 8 <c>0x4d24b0</c>: снять в старой клетке, поставить в новой —
/// <c>0x4d2619</c> → <c>0x5ad1ee</c>). Попап события, сработавшего на пути (в конце или с обрывом), открывается раньше этого извещения:
/// снимки партии 19.10 (карта 22, EV1 и EV2) — с открытым окном эллипса под ходоком нет, после «ОК» есть. Порт держит эллипс снятым от
/// начала хода до прихода; пришёл без попапов — сразу назад, с попапами — когда закрыт последний (<see cref="ReleaseSelection"/>, зовёт
/// хозяин окон).
/// </summary>
public partial class MapView
{
    private bool _selectionHeld;

    /// <summary>Попапы событий, пришедшие с этим ходом: эллипс ждёт только их (не прочих сообщений зрителя).</summary>
    private IReadOnlyList<EventMessage> _heldBy = [];

    /// <summary>Эллипс выделения снят ходом своего отряда и ещё не возвращён.</summary>
    public bool SelectionHeld => _selectionHeld;

    /// <summary>Попапы этого хода ещё не показаны партией (<see cref="GameSession.TakeMessages"/> их не забрала).</summary>
    public bool SelectionPopupsPending => _heldBy.Any(m => Session.State.Messages.Contains(m));

    /// <summary>Эллипс выделения сейчас на карте (последний показ меток).</summary>
    public bool SelectionShown { get; private set; }

    /// <summary>Вернуть эллипс после хода: попапов события этого хода больше нет.</summary>
    public void ReleaseSelection()
    {
        if (!_selectionHeld) return;
        _selectionHeld = false;
        _heldBy = [];
        Marks();
        GameLog.Verbose($"selection back {_selected ?? "-"}");
    }

    /// <summary>Ход дошёл (после <see cref="GameSession.MoveStack"/>): попапов событий зрителю этот ход не принёс — эллипс сразу назад;
    /// принёс — ждёт их закрытия.</summary>
    private void SelectionAfterWalk(IReadOnlyList<EventMessage> before)
    {
        _heldBy = [.. Session.State.Messages.Where(m => !before.Contains(m)
                                                        && string.Equals(m.PlayerId, Viewer, System.StringComparison.OrdinalIgnoreCase))];
        if (_heldBy.Count == 0) _selectionHeld = false;
        else GameLog.Verbose($"selection held {_selected ?? "-"} попапов={_heldBy.Count}");
    }
}
