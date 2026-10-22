using LastEdict.Data.Game;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Зачем окно <c>DLG_HOTSEAT_POPUP</c> зовёт человека к ПК (<c>CHotseatPopupInterf</c> <c>0x4bdd72</c>, режим — пятый аргумент;
/// <c>docs/day-cycle.md</c> §18).</summary>
public enum HotseatCall
{
    /// <summary>Режим 0 — начало его хода (<c>CTaskChangeHotseatTurn</c> <c>0x4d8fed</c>): <c>X005TA0718</c>.</summary>
    Turn = 0,

    /// <summary>Режим 1 — ему предложение другого человека (союз, обмен картами, сделка: <c>0x4d5eb3</c>, <c>0x4d633a</c>,
    /// <c>0x4d67cc</c>), когда он не за ПК: <c>X005TA0724</c>.</summary>
    Message = 1,

    /// <summary>Режим 2 — на него напали (<c>CTaskBattle</c> <c>0x4d7b02</c>): <c>X005TA0893</c>.</summary>
    Defend = 2,
}

/// <summary>Окно «горячего стула» (<c>DLG_HOTSEAT_POPUP</c>, <c>CHotseatPopupInterf</c> <c>0x4bdd72</c>): портрет <c>IMG_FACE</c> — всегда
/// игрок <paramref name="player"/>, кого зовут (<c>0x4be058</c> → <c>0x5b24ed</c>: кадр лобби <c>DLG_HOTSEAT_LOBBY_RACE_FACE{раса}{лицо+1}N</c>
/// — ранг 2); текст по режиму (ранг 1, <c>0x4bdef3</c>, <c>0x4bdfae</c>): ход — <c>%NAME%</c>/<c>%RACE%</c> его; предложение —
/// <c>%NAME%</c>/<c>%RACE%</c> предлагающего <paramref name="sender"/>, <c>%TARGETNAME%</c> — имя адресата; нападение —
/// <c>%NAME%</c>/<c>%RACE%</c> защитника, <c>%TARGETNAME%</c> — <paramref name="target"/> (имя отряда или крепости). «ОК» — <c>BTN_OK</c>;
/// само окно ничего не переключает (<c>0x4be139</c>).</summary>
public sealed class HotseatTurnLogic(MapPlayer player, GameCatalog catalog, Func<string, string> text,
                                     HotseatCall call = HotseatCall.Turn, MapPlayer? sender = null, string target = "") : IScreenLogic
{
    public const string TurnText = "X005TA0718";

    public const string MessageText = "X005TA0724";

    public const string DefendText = "X005TA0893";

    public HotseatCall Call => call;

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var who = call == HotseatCall.Message && sender is not null ? sender : player;
        string line = text(call switch { HotseatCall.Message => MessageText, HotseatCall.Defend => DefendText, _ => TurnText })
            .Replace("%NAME%", who.Name.Trim()).Replace("%RACE%", RaceOf(who) is { } r ? text(r.NameText) : "")
            .Replace("%TARGETNAME%", call == HotseatCall.Message ? player.Name.Trim() : target);
        ui.SetText("TXT_TEXT", line);
        if (RaceOf(player) is { } race) ui.SetImage("IMG_FACE", $"DLG_HOTSEAT_LOBBY_RACE_FACE{race.Code}{Math.Clamp(player.Face, 0, 3) + 1}N");
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    private LastEdict.Data.Game.RaceInfo? RaceOf(MapPlayer p) => catalog.Races.FirstOrDefault(r => r.Id.Equals(p.RaceId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Кого «горячий стул» зовёт к бою (<c>CTaskBattle</c> ctor <c>0x4d76c6</c>, ранг 1).</summary>
public static class HotseatCalls
{
    /// <summary>
    /// Защитник боя — человек этой партии «горячего стула»: окно режима 2 перед боем (<c>0x4d79c8…0x4d7b49</c>). Проверки «защитник уже за
    /// ПК» у движка нет — окно и тогда. Не хотсит, защитник — ИИ или нейтрал: null. Цель — имя группы защиты (<c>0x4d7be6</c>): отряд — имя
    /// его лидера (нет своего — имя типа), крепость — её имя. Засада (невидимый нападающий, <c>0x4d7790</c>) — другое окно, в порте его нет.
    /// </summary>
    /// <param name="defenderGroup">Группа защиты боя (<c>BattleState.DefenderGroup</c>): отряд или крепость.</param>
    public static (MapPlayer Player, string Target)? Defender(GameSession session, string defenderGroup, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(text);
        if (!session.Hotseat) return null;
        var map = session.Map;
        string owner, target;
        if (map.Stacks.TryGetValue(defenderGroup, out var stack))
        {
            owner = stack.Owner;
            string name = session.Looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "";
            target = name.Length > 0 ? name
                : map.Units.TryGetValue(stack.LeaderId, out var leader) ? text(session.Art.Unit(leader.TypeId)?.NameTxt ?? "") : "";
        }
        else if (map.Sites.FirstOrDefault(s => s.Id.Equals(defenderGroup, StringComparison.OrdinalIgnoreCase)) is { } fort)
            (owner, target) = (fort.Owner, fort.Name);
        else return null;
        return session.State.Info.Humans.Contains(owner, StringComparer.OrdinalIgnoreCase) && map.Players.TryGetValue(owner, out var player)
            ? (player, target) : null;
    }

    /// <summary>Обе стороны боя — люди (<c>CBattleViewerInterfData +0x14f8</c>, <c>0x62d2e6</c>: <c>isHuman</c> хозяев обеих групп): каждый ведёт
    /// свою сторону на общем экране — сервер шлёт выбор действия хозяину ходящего юнита (<c>0x42840e</c>), в хотсите оба — на этом ПК.</summary>
    public static bool BothHuman(GameSession session, string attackerGroup, string defenderGroup)
    {
        ArgumentNullException.ThrowIfNull(session);
        bool Human(string group) =>
            (session.Map.Stacks.TryGetValue(group, out var s) ? s.Owner
             : session.Map.Sites.FirstOrDefault(f => f.Id.Equals(group, StringComparison.OrdinalIgnoreCase))?.Owner ?? "") is { Length: > 0 } owner
            && session.State.Info.Humans.Contains(owner, StringComparer.OrdinalIgnoreCase);
        return Human(attackerGroup) && Human(defenderGroup);
    }
}
