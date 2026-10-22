using D2.Data.Sg;
using D2.Rules.Map;
using KSg = Kaitai.Sg;

namespace D2.Data.Map;

/// <summary>
/// Сценарий или сохранение -> события сценария (<see cref="ScenarioEventSet"/>): сами события,
/// области для условия <c>L_LOCATION</c> и переменные сценария. Прямая проекция полей, ранг 1.
///
/// <para>Условие или эффект, которых нет в подмножестве карты С4, разбирается в
/// <see cref="UnsupportedCondition"/>/<see cref="UnsupportedEffect"/> с именем из
/// <c>LEvCond.DBF</c>/<c>LEvEffct.DBF</c>: чтение полное, отказ — только при попытке применить
/// (<see cref="ScenarioEvents.Fire"/>). Так карту с чужими событиями можно прочитать и
/// пересчитать, не имея права её симулировать.</para>
/// </summary>
public static class EventLoader
{
    /// <inheritdoc cref="MapLoader.Load"/>
    /// <exception cref="InvalidDataException">Файл есть, но не читается как <c>.sg</c>.</exception>
    public static ScenarioEventSet Load(string sgPath) => Load(sgPath, null);

    /// <summary>То же по уже разобранному файлу (<paramref name="file"/> не null; <paramref name="sgPath"/> — только для сообщений).</summary>
    public static ScenarioEventSet Load(string sgPath, SgFile? file)
    {
        try
        {
            var objects = (file ?? SgFile.Open(sgPath)).Raw.Objects;
            return new ScenarioEventSet(
                objects.Select(o => (Object: o, Event: o.Data as KSg.MidEvent))
                    .Where(p => p.Event is not null)
                    .Select(p => EventOf(p.Object.ObjId.String, p.Event!))
                    .ToList(),
                objects.Select(o => o.Data).OfType<KSg.MidLocation>().ToDictionary(
                    l => Id(l.LocId),
                    l => new EventZone(Id(l.LocId), (int)l.Position.X.Value,
                                       (int)l.Position.Y.Value, (int)l.Radius.Value),
                    StringComparer.OrdinalIgnoreCase),
                objects.Select(o => o.Data).OfType<KSg.MidScenVariables>()
                    .SelectMany(v => v.Variables)
                    .ToDictionary(v => Int(v.Id), v => Int(v.Value)))
            {
                Templates = objects.Select(o => o.Data).OfType<KSg.MidStackTemplate>().Select(TemplateOf)
                                   .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase),
                SubraceOwners = objects.Select(o => o.Data).OfType<KSg.MidSubrace>()
                                       .ToDictionary(s => Id(s.SubraceId), s => Id(s.PlayerId), StringComparer.OrdinalIgnoreCase),
            };
        }
        catch (Exception e) when (e is not (InvalidDataException or FileNotFoundException
                                            or DirectoryNotFoundException))
        {
            throw new InvalidDataException($"{sgPath}: не читается как .sg", e);
        }
    }

    /// <summary>
    /// Состояние событий сохранения: выключенные и переменные — как у <see cref="EventProgress.From"/>, плюс журнал заданий
    /// (<c>MidQuestLog</c>: <c>ID_PLAYER</c>, <c>SEQ_NUM</c>, <c>CUR_TURN</c>, <c>ID_EVENT</c>, <c>EFF_NUM</c>) — окно «Задания» на
    /// сохранении движка иначе пусто (пометка сессии Б, 28.09). <c>TYPE</c> записи в модели нет.
    /// </summary>
    public static EventProgress LoadProgress(string sgPath, ScenarioEventSet events)
    {
        ArgumentNullException.ThrowIfNull(events);
        try
        {
            var log = SgFile.Open(sgPath).Raw.Objects.Select(o => o.Data).OfType<KSg.MidQuestLog>()
                .SelectMany(q => q.Sequences)
                .Select(e => new QuestLogEntry(Id(e.IdPlayer), Int(e.SeqNum), Int(e.CurTurn), Id(e.IdEvent), Int(e.EffNum)))
                .ToList();
            return EventProgress.From(events) with { Log = log };
        }
        catch (Exception e) when (e is not (InvalidDataException or FileNotFoundException or DirectoryNotFoundException))
        {
            throw new InvalidDataException($"{sgPath}: не читается как .sg", e);
        }
    }

    /// <summary>Флаг расы: <c>ELF</c> есть только в файлах Rise of the Elves — там, где записи
    /// нет, Kaitai оставляет поле пустым, и флаг считается снятым.</summary>
    private static bool Flag(KSg.BoolRecord? record) => record is not null && record.Value != 0;

    private static ScenarioEvent EventOf(string objectId, KSg.MidEvent e) => new(
        // Id самой записи (CMidEvent.ID) — то же, что id объекта; берём поле записи, а объект
        // сверяет тест: расхождение означало бы сбитый разбор.
        Id: e.Id.String.Length > 0 ? e.Id.String : objectId,
        Enabled: e.Enabled.Value != 0,
        OccurOnce: e.OccurOnce.Value != 0,
        Chance: (int)e.Chance.Value,
        Order: (int)e.Order.Value,
        Races: [Flag(e.Human), Flag(e.Dwarf), Flag(e.Undead), Flag(e.Heretic), Flag(e.Neutral),
                Flag(e.Elf)],
        Conditions: e.Conditions.Select(ConditionOf).ToList(),
        // NUM эффекта — его место в списке; файл пишет их по порядку, но опираться на это не будем.
        Effects: e.Effects.OrderBy(x => Num(x.Data)).Select(EffectOf).ToList(),
        Name: Text(e.NameTxt),
        TriggerRaces: [Flag(e.Verhuman), Flag(e.Verdwarf), Flag(e.Verundead), Flag(e.Verheretic),
                       Flag(e.Verneutral), Flag(e.Verelf)]);

    /// <summary><c>NUM</c> эффекта; у всех 24 видов записи поле есть — без него порядок эффектов не восстановить.</summary>
    private static long Num(Kaitai.KaitaiStruct? data) =>
        data is null ? 0
        : data.GetType().GetProperty("Num")?.GetValue(data) is KSg.IntRecord n ? n.Value
        : throw new InvalidDataException($"у эффекта {data.GetType().Name} нет поля NUM");

    /// <summary>Поля нет в старом файле — пусто (так же, как <see cref="Flag"/>).</summary>
    private static string Id(KSg.StringRecord? r) => r?.String?.Trim() ?? "";

    private static string Text(KSg.StringRecord? r) => r?.String ?? "";

    private static int Int(KSg.IntRecord? r) => r is null ? 0 : (int)r.Value;

    private static bool B(KSg.BoolRecord? r) => r is not null && r.Value != 0;

    /// <summary>Шаблон отряда (поток <c>0x5fee0b</c>): список юнитов обрывается на первом пустом <c>UNIT_n</c>; клетки
    /// <c>POS_n</c> — индекс юнита в этом списке (−1 — пусто; вне списка — тоже пусто: у движка это исключение загрузки).</summary>
    private static StackTemplate TemplateOf(KSg.MidStackTemplate t)
    {
        var kept = new List<int>();
        for (int i = 0; i < t.Units.Count && !IsEmpty(Id(t.Units[i].Unit)); i++) kept.Add(i);
        var slots = t.Positions.Select(p => (int)p.Value).Select(i => i >= 0 && i < kept.Count ? i : -1).ToList();
        return new StackTemplate(
            Id(t.Id), Id(t.Owner), Id(t.Subrace), Id(t.Leader), Int(t.LeaderLvl), Text(t.NameTxt), Int(t.Order), Id(t.OrderTarg),
            [.. kept.Select(i => new TemplateUnit(Id(t.Units[i].Unit), Int(t.Units[i].Level)))], slots,
            B(t.UseFacing), Int(t.Facing),
            [.. (t.Modifiers ?? []).Select(m => new TemplateModifier(Int(m.UnitPos), Id(m.ModifId)))], Int(t.Aipriority));
    }

    private static string EmptyToBlank(string id) => IsEmpty(id) ? "" : id;

    private static bool IsEmpty(string id) => id.Length == 0 || id.Equals("G000000000", StringComparison.OrdinalIgnoreCase);

    private static EventCondition ConditionOf(KSg.EventCondition c)
    {
        int category = (int)c.Category.Value;
        return c.Data switch
        {
            KSg.EventConditionFrequency f => new FrequencyCondition(Int(f.Frequency)),
            KSg.EventConditionEnteringAPredefinedZone z => new ZoneCondition(Id(z.IdLoc)),
            KSg.EventConditionEnteringACity x => new EnterCityCondition(Id(x.IdCity)),
            KSg.EventConditionOwningACity x => new OwnCityCondition(Id(x.IdCity)),
            KSg.EventConditionDestroyStack x => new KillStackCondition(Id(x.IdStack)),
            KSg.EventConditionOwningAnItem x => new OwnItemCondition(Id(x.TypeItem)),
            KSg.EventConditionSpecificLeaderOwningAnItem x => new LeaderOwnItemCondition(Id(x.TypeItem), Id(x.IdStack)),
            KSg.EventConditionDiplomacyRelations x => new DiplomacyCondition(Id(x.IdPlayer1), Id(x.IdPlayer2), Int(x.Diplomacy)),
            KSg.EventConditionAlliance x => new AllianceCondition(Id(x.IdPlayer1), Id(x.IdPlayer2)),
            KSg.EventConditionLootingARuin x => new LootRuinCondition(Id(x.IdRuin)),
            KSg.EventConditionTransformingLand x => new TransformLandCondition(Int(x.PctLand)),
            KSg.EventConditionVisitingASite x => new VisitSiteCondition(Id(x.IdSite)),
            KSg.EventConditionStackToLocation x => new LeaderToZoneCondition(Id(x.IdStack), Id(x.IdLoc)),
            KSg.EventConditionStackToCity x => new LeaderToCityCondition(Id(x.IdStack), Id(x.IdCity)),
            KSg.EventConditionItemToLocation x => new ItemToZoneCondition(Id(x.TypeItem), Id(x.IdLoc)),
            KSg.EventConditionStackExistance x => new StackExistsCondition(Id(x.IdStack), Int(x.MiscInt)),
            KSg.EventConditionVariableIsInRange x => new VariableInRangeCondition(
                [Int(x.MiscInt), Int(x.MiscInt2), Int(x.MiscInt3), Int(x.MiscInt4),
                 Int(x.MiscInt5), Int(x.MiscInt6), Int(x.MiscInt7)]),
            _ => new UnsupportedCondition(category, ScenarioEvents.ConditionName(category)),
        };
    }

    private static EventEffect EffectOf(KSg.EventEffect e)
    {
        int category = (int)e.Category.Value;
        return e.Data switch
        {
            KSg.EventEffectWinOrLoseScenario x => new WinEffect(B(x.WinScen), Id(x.IdPlayer1)),
            KSg.EventEffectCreateNewStack x => new CreateStackEffect(Id(x.IdStktemp), Id(x.IdLoc)),
            KSg.EventEffectCastSpellOnTriggerer x => new CastSpellOnTriggererEffect(Id(x.TypeSpell), Id(x.IdPlayer1)),
            KSg.EventEffectCastSpellAtSpecificLocation x => new CastSpellAtEffect(Id(x.TypeSpell), Id(x.IdLoc), Id(x.IdPlayer1)),
            KSg.EventEffectChangeStackOwner x => new ChangeOwnerEffect(Id(x.IdStack), Id(x.IdPlayer1), B(x.FirstOnly), B(x.PlayAnim)),
            KSg.EventEffectMoveStackNextToTriggerer x => new MoveBesideEffect(Id(x.IdStack)),
            KSg.EventEffectGoIntoBattle x => new BattleEffect(Id(x.IdStack), B(x.FirstOnly)),
            KSg.EventEffectEnableDisableAnotherEvent x => new EnableEventEffect(Id(x.IdEvent), B(x.Enable)),
            KSg.EventEffectGiveSpell x => new GiveSpellEffect(Id(x.TypeSpell)),
            KSg.EventEffectGiveItem x => new GiveItemEffect(Int(x.Giveto), Id(x.TypeItem)),
            KSg.EventEffectMoveStackToSpecificLocation x => new MoveStackEffect(Id(x.IdStktemp), Id(x.IdLoc), B(x.Boolvalue)),
            KSg.EventEffectAllyTwoAiPlayers x => new AllyEffect(Id(x.IdPlayer1), Id(x.IdPlayer2), B(x.Permally)),
            KSg.EventEffectChangePlayerDiplomacyMeter x => new DiplomacyEffect(Id(x.IdPlayer1), Id(x.IdPlayer2),
                                                                               Int(x.Diplomacy), B(x.Enable)),
            KSg.EventEffectUnfogOrFogAnAreaOnTheMap x => new UnfogEffect(Id(x.IdLoc), B(x.Enable), Int(x.Numvalue),
                [.. x.Events.Select(u => new UnfogEntry(Id(u.EventId), Id(u.Player)))]),
            KSg.EventEffectRemoveMountainsAroundALocation x => new RemoveMountainsEffect(Id(x.IdLoc)),
            KSg.EventEffectRemoveLandmark x => new RemoveLandmarkEffect(Id(x.IdLmark), B(x.Boolvalue)),
            KSg.EventEffectChangeScenarioObjectiveText x => new ChangeObjectiveEffect(Text(x.ObjectTxt)),
            KSg.EventEffectDisplayPopupMessage x => new PopupEffect(Text(x.PopupTxt), Id(x.Music), Id(x.Sound),
                                                                    Id(x.Image), Id(x.Image2), B(x.LeftSide),
                                                                    Id(x.PopupShow), B(x.Boolvalue)),
            KSg.EventEffectChangeStackOrder x => new ChangeOrderEffect(Id(x.IdStack), EmptyToBlank(Id(x.OrderTarg)), B(x.FirstOnly), Int(x.Order)),
            KSg.EventEffectDestroyItem x => new DestroyItemEffect(Id(x.TypeItem), B(x.TrigOnly)),
            KSg.EventEffectRemoveStack x => new RemoveStackEffect(Id(x.IdStack), B(x.FirstOnly)),
            KSg.EventEffectChangeLandmark x => new ChangeLandmarkEffect(Id(x.IdLmark), Id(x.TypeLmark)),
            KSg.EventEffectChangeTerrain x => new ChangeTerrainEffect(Id(x.IdLoc), Int(x.Lookup), Int(x.Numvalue)),
            KSg.EventEffectModifyVariable v => new ModifyVariableEffect(Int(v.Numvalue2), Int(v.Lookup), Int(v.Numvalue)),
            _ => new UnsupportedEffect(category, ScenarioEvents.EffectName(category)),
        };
    }
}
