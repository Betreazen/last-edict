namespace LastEdict.Data.Battle;

/// <summary>Что видит панель боя в миг перерисовки (<see cref="BattlePanelButtons"/>).</summary>
/// <param name="AwaitsHuman">Ход юнита человека начался и ждёт команды: показ прошлого действия кончился, автобоя у стороны нет, бой не кончен.</param>
/// <param name="Allowed">Команды хода (<see cref="BattleSession.Allowed"/>).</param>
/// <param name="DraggingItem">Человек тащит предмет боя на цель.</param>
/// <param name="Closable">Бой кончен и показан — на панели «Закрыть» (<c>0x637581</c>).</param>
public readonly record struct BattlePanelMoment(bool AwaitsHuman, IReadOnlySet<BattleCommand> Allowed, bool DraggingItem, bool Closable);

/// <summary>
/// Доступность кнопок панели боя — как у <c>CBattleViewerInterf</c> (флаг <c>CButtonInterf::setEnabled</c> <c>0x531cca</c>, слот +0xa0;
/// у переключателя — +0x8c):
/// <list type="bullet">
/// <item>создание панели (<c>0x62ef83</c> → <c>0x62f27f</c>) и любое действие человека (<c>0x6372b1</c> → <c>0x6371b1</c> → <c>0x62f27f</c>:
/// удар, «Защищаться», «Ждать», «Отступить», предмет) гасят «Защищаться», «Отступить», «Ждать» и <c>TOG_RIGHTUNITS</c> — на показ действия
/// и на ходы ИИ;</item>
/// <item>начало хода юнита человека (<c>0x62f823</c> → <c>0x62fc08</c>) зажигает <c>TOG_RIGHTUNITS</c> и кнопки команд из списка хода
/// (переходы <c>0x63061f</c>: 2 → «Отступить», 3 → «Ждать», 4 → «Защищаться»);</item>
/// <item>тащение предмета гасит <c>TOG_RIGHTUNITS</c> (<c>0x63460d</c>) и по броску зажигает (<c>0x634a21</c>);</item>
/// <item>конец боя зажигает <c>TOG_RIGHTUNITS</c> (<c>0x637581</c>, там же «Закрыть»).</item>
/// </list>
/// Партия <c>corpus/20261020-faces</c>: 37 из 37 щелчков «Защищаться» гасят все четыре в ту же мс, зажигаются они после <c>bat_show_end</c>
/// последнего хода ИИ.
/// </summary>
public static class BattlePanelButtons
{
    public const string Defend = "BTN_DEFEND", Wait = "BTN_WAIT", Retreat = "BTN_RETREAT", Groups = "TOG_RIGHTUNITS";

    /// <summary>Элементы, чью доступность ведёт правило.</summary>
    public static readonly IReadOnlyList<string> Elements = [Defend, Wait, Retreat, Groups];

    /// <summary>Доступен ли <paramref name="element"/> (один из <see cref="Elements"/>) в миг <paramref name="moment"/>.</summary>
    public static bool Enabled(string element, BattlePanelMoment moment)
    {
        ArgumentNullException.ThrowIfNull(moment.Allowed);
        return element switch
        {
            Defend => moment.AwaitsHuman && moment.Allowed.Contains(BattleCommand.Defend),
            Wait => moment.AwaitsHuman && moment.Allowed.Contains(BattleCommand.Wait),
            Retreat => moment.AwaitsHuman && moment.Allowed.Contains(BattleCommand.Retreat),
            Groups => moment.AwaitsHuman && !moment.DraggingItem || moment.Closable,
            _ => throw new ArgumentOutOfRangeException(nameof(element), element, "не кнопка панели боя"),
        };
    }
}
