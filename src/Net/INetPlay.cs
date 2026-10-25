using LastEdict.Data.Battle;
using LastEdict.Data.Play;
using LastEdict.Rules.Battle;

namespace LastEdict.Net;

/// <summary>
/// Сетевая партия для показа (<c>docs/network.md</c> §7): чей ход, работа для рабочего потока карты, бои, о которых просит хост, почта и
/// события. Две реализации: обычная комната (<see cref="NetSession"/>: хост или клиент) и асинхронная (<see cref="AsyncPlay"/>: свой ход
/// у себя, партия у посредника).
/// </summary>
public interface INetPlay
{
    /// <summary>Игрок этой машины.</summary>
    string Me { get; }

    /// <summary>Партия этой машины (у клиента — вид).</summary>
    GameSession Session { get; }

    /// <summary>Ход этой машины: можно водить отряды и окна.</summary>
    bool MyTurn { get; }

    /// <summary>Чей ход.</summary>
    string Active { get; }

    bool Over { get; }

    /// <summary>Срок своего хода; null — без таймера.</summary>
    DateTimeOffset? Deadline { get; }

    /// <summary>Код комнаты.</summary>
    string Room { get; }

    /// <summary>Работа для рабочего потока карты, пока ход не свой (у хоста — конец хода клиента; в асинхронной — записать ход, взять
    /// партию).</summary>
    bool TryTakeWork(out Action work);

    /// <summary>Бой хода ИИ, который просит сыграть хост (клиент обычной комнаты).</summary>
    bool TryTakeBattle(out BattleRequest request);

    /// <summary>Собрать у себя бой по запросу хоста; не собрать — null.</summary>
    BattleState? BuildBattle(BattleRequest request);

    /// <summary>Ответить хосту боем (null — не сыграли: автобой у хоста).</summary>
    void ReplyBattle(BattleRequest request, BattlePlay? play, IReadOnlyList<TurnRecord> log);

    /// <summary>Разобрать почту (кадр игры).</summary>
    void Pump();

    /// <summary>Партия изменилась без своей команды — с любого потока.</summary>
    event Action? Changed;

    /// <summary>Сменился ход.</summary>
    event Action? TurnChanged;

    /// <summary>Строка игроку.</summary>
    event Action<string>? Notice;

    /// <summary>Строка журнала.</summary>
    event Action<string>? Log;
}
