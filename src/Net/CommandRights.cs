using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;
using LastEdict.Rules.Map;

namespace LastEdict.Net;

/// <summary>Права клиента на объекты команды: свои отряды, крепости, группы и сам игрок. Ход проверяет хост до правил (контракт §6:
/// «владение юнитом, ход, фаза»); текст отказа не называет чужого. Читы и победа клиенту не даются.</summary>
public static class CommandRights
{
    /// <summary>null — можно; иначе — текст отказа.</summary>
    public static string? Check(GameState state, string player, GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        var map = state.Looks.Map;
        bool Me(string id) => id.Equals(player, StringComparison.OrdinalIgnoreCase);
        bool Stack(string id) => map.Stacks.TryGetValue(id, out var s) && Me(s.Owner);
        bool Site(string id) => map.Sites.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && Me(s.Owner));
        bool Group(string id) => Stack(id) || Site(id);
        bool Unit(string id) => map.Stacks.Values.Any(s => Me(s.Owner) && (s.LeaderId == id || s.UnitIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
                                || map.Sites.Any(s => Me(s.Owner) && s.Guards.Contains(id, StringComparer.OrdinalIgnoreCase));
        bool ok = command switch
        {
            PlanMoveCommand c => Stack(c.Stack),
            RevealFromCommand c => Stack(c.Stack),
            MoveStackCommand c => Stack(c.Stack),
            ActInPlaceCommand c => Stack(c.Stack),
            LeftFortCommand c => Stack(c.Stack),
            LeaveFortCommand c => Stack(c.Stack),
            EndTurnCommand => true,
            BuildCommand c => Me(c.Player),
            HireUnitCommand c => Site(c.Fort),
            HireLeaderCommand c => Site(c.Fort),
            CastSpellCommand c => Me(c.Player),
            CastItemCommand c => Stack(c.Stack),
            ResearchSpellCommand c => Me(c.Player),
            GrowCityCommand c => Site(c.Village),
            DismissUnitCommand c => Group(c.Group),
            HealUnitCommand c => Site(c.Fort),
            ReviveUnitCommand c => Site(c.Fort),
            SwapUnitsCommand c => Group(c.FromGroup) && Group(c.ToGroup),
            MoveInGroupCommand c => Group(c.Group),
            EquipItemCommand c => Stack(c.Stack),
            UnequipItemCommand c => Stack(c.Stack),
            DrinkPotionCommand c => Group(c.Holder),
            ExchangeItemCommand c => Group(c.From) && Group(c.To),
            DropBagCommand c => Stack(c.Stack),
            CloseBagCommand => true,
            TakeFortItemsCommand c => Stack(c.Stack),
            GiveFortItemsCommand c => Stack(c.Stack),
            GiveItemsCommand c => Group(c.From),
            ChooseSkillCommand c => Stack(c.Stack),
            RenameLeaderCommand c => Stack(c.Stack),
            ForceDynLevelCommand c => Unit(c.Unit),
            RequestPaperdollCommand c => Stack(c.Stack),
            PlaceRodCommand c => Stack(c.Stack),
            BreakRodCommand c => Stack(c.Stack),
            BuyItemCommand c => Stack(c.Stack),
            BuySpellCommand c => Stack(c.Stack),
            SellItemCommand c => Stack(c.Stack),
            HireMercenaryCommand c => Stack(c.Stack),
            TrainCommand c => Stack(c.Stack),
            ThiefCommand c => Stack(c.Stack),
            ThiefCancelCommand c => Stack(c.Stack),
            GiveGoldCommand c => Me(c.From),
            BreakAllianceCommand c => Me(c.Player),
            ProposeAllianceCommand c => Me(c.Player),
            AnswerAllianceCommand c => Me(c.Human),
            AnswerTradeCommand c => Me(c.Human),
            AnswerExchangeCommand c => Me(c.Human),
            ProposeTradeCommand c => Me(c.Player),
            ProposeExchangeMapCommand c => Me(c.Player),
            EnterCommand c => Stack(c.Stack),
            StartBattleCommand c => Stack(c.Attacker),
            EndBattleCommand or CommitBattleCommand or NextEventBattleCommand => true,
            TakeMessagesCommand c => Me(c.Player),
            _ => false,   // победа, читы и новые команды без правила — не клиенту
        };
        return ok ? null : $"команда «{command.GetType().Name}» — не над вашими объектами";
    }
}
