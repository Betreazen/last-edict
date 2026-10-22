namespace LastEdict.Rules.Map;

/// <summary>
/// Перенос юнита между группами (отряд героя, гарнизон, другой отряд) — ответ проверки <c>swap_check</c>
/// журнала зонда: 0 — можно, <c>0xB</c> — нельзя. Ранг 1 по прогону 20260917-synth (ai-q20b, M-run17):
/// <list type="bullet">
/// <item>в пустую клетку отряда героя — только при юнитах &lt; лидерства (seq 3817263 — 0xB у полного героя);</item>
/// <item>обмен с занятой клеткой — всегда 0;</item>
/// <item>лидер/страж на место чужой группы не встаёт (seq 3210163: Myzrael в клетку 2 крепости — 0xB), в том
/// числе обменом, уводящим лидера в гарнизон.</item>
/// </list>
/// ponytail: большие юниты (две клетки) и потолок 6 здесь не проверяются — в корпусе таких отказов нет; появятся —
/// считать клетки как <see cref="SiteTrade.FreeCells"/>, а не юниты.
/// </summary>
public static class GroupExchange
{
    public const int Allowed = 0;
    public const int Refused = 0xB;

    /// <param name="movedIsLeader">Переносимый — лидер или страж своей группы.</param>
    /// <param name="occupantIsLeader">Обитатель целевой клетки: null — клетка пуста (перенос), иначе обмен и
    /// признак «лидер/страж» у того, кто уйдёт в исходную группу.</param>
    /// <param name="leadership">Лидерство целевой группы (с навыками); null — гарнизон, лидерства нет.</param>
    /// <param name="units">Юниты целевой группы сейчас.</param>
    public static int Check(bool movedIsLeader, bool? occupantIsLeader, int? leadership, int units) =>
        movedIsLeader || occupantIsLeader == true ? Refused
        : occupantIsLeader is null && units >= leadership ? Refused
        : Allowed;
}
