using LastEdict.Data.Menu;
using LastEdict.Data.Play;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Map;

/// <summary>Что показывает окно ожидания: крупный портрет лорда того, чей ход (null — пусто).</summary>
public sealed record AiWaitState(string? LordFace)
{
    public static readonly AiWaitState Empty = new((string?)null);
}

/// <summary>
/// Окно ожидания хода ИИ <c>DLG_STRAT_WAIT</c> (160×397, место панели команд <c>DLG_STRATEGIC</c>; граф:
/// <c>DLG_STRATEGIC.BTN_END_TURN</c> → панель <c>DLG_STRAT_WAIT</c>, 119 раз в журналах). Разбор 18.10 (Ин39):
/// <list type="bullet">
/// <item>панель ставит режим 3 смены панели <c>0x48d688</c> (<c>0x48d2c0</c>, игрок хода из <c>0x48d172</c>): <c>0x48d858</c> меняет
/// панель на <c>DLG_STRAT_WAIT</c> (<c>0x48854c</c>), затем <c>0x48d7bb…0x48d84c</c> — только <c>IMG_LORD_FACE</c>: лорд игрока
/// (<c>0x5b24ed</c> от <c>0x5e4727</c> игрока и его лица <c>+0x20</c>), нет игрока — по умолчанию <c>0x5e5254</c>;</item>
/// <item><c>IMG_LEADER</c> и <c>TXT_LEADER_NAME</c> движок в этом окне не заполняет: все ссылки на их имена (<c>[0x799f78]</c> —
/// <c>0x48d963</c>, <c>0x48dc10</c>, <c>0x48e19d</c>, <c>0x48e432</c>, <c>0x48e6bc</c>; <c>[0x799f54]</c> — <c>0x48d98b</c>, <c>0x48dc36</c>,
/// <c>0x48e1dd</c>) стоят за проверкой <c>_stricmp(имя текущей панели, "DLG_STRATEGIC")</c> (<c>0x48d934</c>, <c>0x48e128</c>,
/// <c>0x48e424</c>, <c>0x48e69b</c>), а другие таблицы строк с <c>IMG_LEADER</c> принадлежат иным окнам. В описании окна оба пусты
/// (<c>IMAGE IMG_LEADER,…,""</c>), поэтому на месте портрета виден знак фона <c>DLG_STRAT_WAIT_STRAVIDE</c>, полоса имени пустая.
/// Отражение, маска павшего, красная доля и значки (<see cref="PanelFace"/>) здесь не нужны.</item>
/// </list>
/// Прежде порт (со слов пользователя 25.09) ставил в <c>IMG_LEADER</c> портрет лидера ходящего отряда ИИ и его имя — у движка такого нет.
/// Внизу (<c>IMG_LORD_FACE</c> под рамкой <c>IMG_XCOVER</c>) — портрет лорда игрока, чей ход, у нейтралов — «разбойник»
/// <c>LORDNEL01</c>. Кнопок в окне нет.
/// </summary>
public sealed class AiWaitLogic(Func<AiWaitState> state) : IScreenLogic
{
    public const string Screen = "DLG_STRAT_WAIT";

    /// <summary>Портрет нейтрального «лорда» (<c>Lords.ff</c>, 160×220).</summary>
    public const string NeutralLord = "Lords:LORDNEL01";

    public void Open(UiScreenState ui) => Refresh(ui);

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public void Refresh(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetImage("IMG_LORD_FACE", state().LordFace);
        ui.SetImage("IMG_LEADER", null);       // как в описании окна: движок его не трогает
        ui.SetText("TXT_LEADER_NAME", "");
    }

    /// <summary>Крупный портрет лорда игрока: играбельная раса — <see cref="LordFaces.Large"/> по его лицу, иначе
    /// (нейтралы) — <see cref="NeutralLord"/>.</summary>
    public static string LordFace(MapPlayer player, GameBook book)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(book);
        return book.Economy.Races.TryGetValue(player.RaceId, out var race) && race.Playable
            ? LordFaces.Large(book.LookOf(player.RaceId), player.Face)
            : NeutralLord;
    }
}
