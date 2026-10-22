using System.Text.Json.Serialization;

namespace LastEdict.Rules.Map;

/// <summary>
/// Условие события сценария. <see cref="Category"/> — номер из <c>Globals/LEvCond.DBF</c>
/// (он же <c>EventCondition.CATEGORY</c> в <c>.sg</c>), <see cref="Name"/> — имя оттуда же.
/// Ранг 1: таблица прочитана из <c>LEvCond.DBF</c> игры (17 строк с номером; номер 13 в ней
/// отсутствует, а строка <c>L_MOVE</c> идёт без номера вовсе). Поля каждого вида — прямая проекция записи <c>.sg</c>
/// (<c>EventCondition*</c> разбора Kaitai) с именами по смыслу; в данных порта вид пишется полем <c>Kind</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(FrequencyCondition), "Frequency")]
[JsonDerivedType(typeof(ZoneCondition), "Zone")]
[JsonDerivedType(typeof(EnterCityCondition), "EnterCity")]
[JsonDerivedType(typeof(OwnCityCondition), "OwnCity")]
[JsonDerivedType(typeof(KillStackCondition), "KillStack")]
[JsonDerivedType(typeof(OwnItemCondition), "OwnItem")]
[JsonDerivedType(typeof(LeaderOwnItemCondition), "LeaderOwnItem")]
[JsonDerivedType(typeof(DiplomacyCondition), "Diplomacy")]
[JsonDerivedType(typeof(AllianceCondition), "Alliance")]
[JsonDerivedType(typeof(LootRuinCondition), "LootRuin")]
[JsonDerivedType(typeof(TransformLandCondition), "TransformLand")]
[JsonDerivedType(typeof(VisitSiteCondition), "VisitSite")]
[JsonDerivedType(typeof(LeaderToZoneCondition), "LeaderToZone")]
[JsonDerivedType(typeof(LeaderToCityCondition), "LeaderToCity")]
[JsonDerivedType(typeof(ItemToZoneCondition), "ItemToZone")]
[JsonDerivedType(typeof(StackExistsCondition), "StackExists")]
[JsonDerivedType(typeof(VariableInRangeCondition), "VariableInRange")]
[JsonDerivedType(typeof(UnsupportedCondition), "Unknown")]
public abstract record EventCondition(int Category, string Name);

/// <summary>«Как часто» (<c>L_FREQUENCY</c>, 0).</summary>
public sealed record FrequencyCondition(int Frequency) : EventCondition(0, "L_FREQUENCY");

/// <summary>Отряд вошёл в область (<c>L_LOCATION</c>, 2) — <c>CMidLocation</c> с радиусом.</summary>
public sealed record ZoneCondition(string LocationId) : EventCondition(2, "L_LOCATION");

/// <summary>Отряд вошёл в город (<c>L_ENTER_CITY</c>, 3).</summary>
public sealed record EnterCityCondition(string CityId) : EventCondition(3, "L_ENTER_CITY");

/// <summary>Владеть городом (<c>L_OWN_CITY</c>, 4; <c>ID_CITY</c>).</summary>
public sealed record OwnCityCondition(string CityId) : EventCondition(4, "L_OWN_CITY");

/// <summary>Уничтожить отряд (<c>L_KILL_STACK</c>, 5; <c>ID_STACK</c>).</summary>
public sealed record KillStackCondition(string StackId) : EventCondition(5, "L_KILL_STACK");

/// <summary>Владеть предметом типа (<c>L_OWN_ITEM</c>, 6; <c>TYPE_ITEM</c>).</summary>
public sealed record OwnItemCondition(string ItemType) : EventCondition(6, "L_OWN_ITEM");

/// <summary>Предмет у определённого лидера (<c>L_LEADER_OWN_ITEM</c>, 7; <c>TYPE_ITEM</c>, <c>ID_STACK</c>).</summary>
public sealed record LeaderOwnItemCondition(string ItemType, string StackId) : EventCondition(7, "L_LEADER_OWN_ITEM");

/// <summary>Отношения двух игроков (<c>L_DIPLOMACY</c>, 8; <c>ID_PLAYER1</c>, <c>ID_PLAYER2</c>, <c>DIPLOMACY</c>).</summary>
public sealed record DiplomacyCondition(string Player1, string Player2, int Relation) : EventCondition(8, "L_DIPLOMACY");

/// <summary>Союз двух игроков (<c>L_ALLIANCE</c>, 9).</summary>
public sealed record AllianceCondition(string Player1, string Player2) : EventCondition(9, "L_ALLIANCE");

/// <summary>Разграбить руину (<c>L_LOOT_RUIN</c>, 10; <c>ID_RUIN</c>).</summary>
public sealed record LootRuinCondition(string RuinId) : EventCondition(10, "L_LOOT_RUIN");

/// <summary>Доля своей земли (<c>L_TRANSFORM_LAND</c>, 11; <c>PCT_LAND</c>).</summary>
public sealed record TransformLandCondition(int Percent) : EventCondition(11, "L_TRANSFORM_LAND");

/// <summary>Посетить объект (<c>L_VISIT_SITE</c>, 12; <c>ID_SITE</c>).</summary>
public sealed record VisitSiteCondition(string SiteId) : EventCondition(12, "L_VISIT_SITE");

/// <summary>Определённый лидер в области (<c>L_LEADER_TO_ZONE</c>, 14; <c>ID_STACK</c>, <c>ID_LOC</c>).</summary>
public sealed record LeaderToZoneCondition(string StackId, string LocationId) : EventCondition(14, "L_LEADER_TO_ZONE");

/// <summary>Определённый лидер в городе (<c>L_LEADER_TO_CITY</c>, 15; <c>ID_STACK</c>, <c>ID_CITY</c>).</summary>
public sealed record LeaderToCityCondition(string StackId, string CityId) : EventCondition(15, "L_LEADER_TO_CITY");

/// <summary>Предмет доставлен в область (<c>L_ITEM_TO_LOCATION</c>, 16; <c>TYPE_ITEM</c>, <c>ID_LOC</c>).</summary>
public sealed record ItemToZoneCondition(string ItemType, string LocationId) : EventCondition(16, "L_ITEM_TO_LOCATION");

/// <summary>Отряд существует (<c>L_STACK_EXISTS</c>, 17; <c>ID_STACK</c>, <c>MISC_INT</c>).</summary>
public sealed record StackExistsCondition(string StackId, int Mode) : EventCondition(17, "L_STACK_EXISTS");

/// <summary>Переменные в пределах (<c>L_VAR_IN_RANGE</c>, 18; <c>MISC_INT</c>…<c>MISC_INT7</c> — как в файле).</summary>
public sealed record VariableInRangeCondition(IReadOnlyList<int> Values) : EventCondition(18, "L_VAR_IN_RANGE");

/// <summary>Условие неизвестного вида (в ванили таких нет) — только номер.</summary>
public sealed record UnsupportedCondition(int Category, string Name) : EventCondition(Category, Name);

/// <summary>Эффект события. <see cref="Category"/> и <see cref="Name"/> — из <c>Globals/LEvEffct.DBF</c> (24 имени,
/// номера 0..23), ранг 1. Номер эффекта в событии (<c>NUM</c>) — его место в списке.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(WinEffect), "Win")]
[JsonDerivedType(typeof(CreateStackEffect), "CreateStack")]
[JsonDerivedType(typeof(CastSpellOnTriggererEffect), "CastSpellOnTriggerer")]
[JsonDerivedType(typeof(CastSpellAtEffect), "CastSpellAt")]
[JsonDerivedType(typeof(ChangeOwnerEffect), "ChangeOwner")]
[JsonDerivedType(typeof(MoveBesideEffect), "MoveBeside")]
[JsonDerivedType(typeof(BattleEffect), "Battle")]
[JsonDerivedType(typeof(EnableEventEffect), "EnableEvent")]
[JsonDerivedType(typeof(GiveSpellEffect), "GiveSpell")]
[JsonDerivedType(typeof(GiveItemEffect), "GiveItem")]
[JsonDerivedType(typeof(MoveStackEffect), "MoveStack")]
[JsonDerivedType(typeof(AllyEffect), "Ally")]
[JsonDerivedType(typeof(DiplomacyEffect), "Diplomacy")]
[JsonDerivedType(typeof(UnfogEffect), "Unfog")]
[JsonDerivedType(typeof(RemoveMountainsEffect), "RemoveMountains")]
[JsonDerivedType(typeof(RemoveLandmarkEffect), "RemoveLandmark")]
[JsonDerivedType(typeof(ChangeObjectiveEffect), "ChangeObjective")]
[JsonDerivedType(typeof(PopupEffect), "Popup")]
[JsonDerivedType(typeof(ChangeOrderEffect), "ChangeOrder")]
[JsonDerivedType(typeof(DestroyItemEffect), "DestroyItem")]
[JsonDerivedType(typeof(RemoveStackEffect), "RemoveStack")]
[JsonDerivedType(typeof(ChangeLandmarkEffect), "ChangeLandmark")]
[JsonDerivedType(typeof(ChangeTerrainEffect), "ChangeTerrain")]
[JsonDerivedType(typeof(ModifyVariableEffect), "ModifyVariable")]
[JsonDerivedType(typeof(UnsupportedEffect), "Unknown")]
public abstract record EventEffect(int Category, string Name);

/// <summary>Победа или поражение игрока (<c>L_WIN</c>, 0; <c>WIN_SCEN</c>, <c>ID_PLAYER1</c>).</summary>
public sealed record WinEffect(bool Win, string PlayerId) : EventEffect(0, "L_WIN");

/// <summary>Создать отряд по шаблону в области (<c>L_CREATE_STACK</c>, 1; <c>ID_STKTEMP</c>, <c>ID_LOC</c>).</summary>
public sealed record CreateStackEffect(string TemplateId, string LocationId) : EventEffect(1, "L_CREATE_STACK");

/// <summary>Заклинание на того, кто вызвал событие (<c>L_CAST_SPELL</c>, 2; <c>TYPE_SPELL</c>, <c>ID_PLAYER1</c>).</summary>
public sealed record CastSpellOnTriggererEffect(string SpellId, string PlayerId) : EventEffect(2, "L_CAST_SPELL");

/// <summary>Заклинание в области (<c>L_CAST_SPELL_MAP</c>, 3; <c>TYPE_SPELL</c>, <c>ID_LOC</c>, <c>ID_PLAYER1</c>).</summary>
public sealed record CastSpellAtEffect(string SpellId, string LocationId, string PlayerId) : EventEffect(3, "L_CAST_SPELL_MAP");

/// <summary>Сменить владельца отряда (<c>L_CHANGE_OWNER</c>, 4; <c>ID_STACK</c>, <c>ID_PLAYER1</c>, <c>FIRST_ONLY</c>,
/// <c>PLAY_ANIM</c>).</summary>
public sealed record ChangeOwnerEffect(string StackId, string PlayerId, bool FirstOnly, bool PlayAnimation)
    : EventEffect(4, "L_CHANGE_OWNER");

/// <summary>Поставить отряд рядом с вызвавшим (<c>L_MOVE_BESIDE</c>, 5; <c>ID_STACK</c>).</summary>
public sealed record MoveBesideEffect(string StackId) : EventEffect(5, "L_MOVE_BESIDE");

/// <summary>Бой с отрядом (<c>L_BATTLE</c>, 6; <c>ID_STACK</c>, <c>FIRST_ONLY</c>).</summary>
public sealed record BattleEffect(string StackId, bool FirstOnly) : EventEffect(6, "L_BATTLE");

/// <summary>Включить или выключить другое событие (<c>L_ENABLE_EVENT</c>, 7; <c>ID_EVENT</c>, <c>ENABLE</c>).</summary>
public sealed record EnableEventEffect(string EventId, bool Enable) : EventEffect(7, "L_ENABLE_EVENT");

/// <summary>Дать заклинание (<c>L_GIVE_SPELL</c>, 8; <c>TYPE_SPELL</c>).</summary>
public sealed record GiveSpellEffect(string SpellId) : EventEffect(8, "L_GIVE_SPELL");

/// <summary>Дать предмет (<c>L_GIVE_ITEM</c>, 9; <c>GIVETO</c>, <c>TYPE_ITEM</c>).</summary>
public sealed record GiveItemEffect(int GiveTo, string ItemType) : EventEffect(9, "L_GIVE_ITEM");

/// <summary>Переставить отряд (шаблон или отряд) в область (<c>L_MOVE_STACK</c>, 10; <c>ID_STKTEMP</c>, <c>ID_LOC</c>,
/// <c>BOOLVALUE</c>).</summary>
public sealed record MoveStackEffect(string StackId, string LocationId, bool Flag) : EventEffect(10, "L_MOVE_STACK");

/// <summary>Союз двух игроков ИИ (<c>L_ALLY</c>, 11; <c>ID_PLAYER1</c>, <c>ID_PLAYER2</c>, <c>PERMALLI</c>).</summary>
public sealed record AllyEffect(string Player1, string Player2, bool Permanent) : EventEffect(11, "L_ALLY");

/// <summary>Отношения игроков (<c>L_DIPLOMACY</c>, 12; <c>ID_PLAYER1</c>, <c>ID_PLAYER2</c>, <c>DIPLOMACY</c>, <c>ENABLE</c>).</summary>
public sealed record DiplomacyEffect(string Player1, string Player2, int Relation, bool Enable) : EventEffect(12, "L_DIPLOMACY");

/// <summary>Строка списка <see cref="UnfogEffect"/>: событие и игрок (<c>EVENT_ID</c>, <c>PLAYER</c> записи файла).</summary>
public sealed record UnfogEntry(string EventId, string PlayerId);

/// <summary>Открыть или закрыть туман в области (<c>L_UNFOG</c>, 13; <c>ID_LOC</c>, <c>ENABLE</c>, <c>NUMVALUE</c>, список).</summary>
public sealed record UnfogEffect(string LocationId, bool Enable, int Value, IReadOnlyList<UnfogEntry> Entries)
    : EventEffect(13, "L_UNFOG");

/// <summary>Убрать горы вокруг области (<c>L_REMOVE_MOUNTAIN</c>, 14; <c>ID_LOC</c>).</summary>
public sealed record RemoveMountainsEffect(string LocationId) : EventEffect(14, "L_REMOVE_MOUNTAIN");

/// <summary>Убрать ландмарк (<c>L_REMOVE_LMARK</c>, 15; <c>ID_LMARK</c>, <c>BOOLVALUE</c>).</summary>
public sealed record RemoveLandmarkEffect(string LandmarkId, bool Flag) : EventEffect(15, "L_REMOVE_LMARK");

/// <summary>Сменить текст цели сценария (<c>L_CHANGE_OBJECTIVE</c>, 16; <c>OBJECT_TXT</c>).</summary>
public sealed record ChangeObjectiveEffect(string Text) : EventEffect(16, "L_CHANGE_OBJECTIVE");

/// <summary>Окно с текстом (<c>L_POPUP</c>, 17): текст, музыка, звук, две картинки, сторона картинки, кому показывать
/// (<c>POPUP_SHOW</c>), флаг (<c>BOOLVALUE</c>).</summary>
public sealed record PopupEffect(string Text, string Music = "", string Sound = "", string Image = "", string Image2 = "",
                                 bool LeftSide = false, string ShowTo = "", bool Flag = false) : EventEffect(17, "L_POPUP");

/// <summary>Сменить приказ отряда (<c>L_CHANGE_ORDER</c>, 18; <c>ID_STACK</c>, <c>ORDER_TARG</c>, <c>FIRST_ONLY</c>,
/// <c>ORDER</c> — <c>LOrder.dbf</c>).</summary>
public sealed record ChangeOrderEffect(string StackId, string TargetId, bool FirstOnly, int Order) : EventEffect(18, "L_CHANGE_ORDER");

/// <summary>Уничтожить предмет (<c>L_DESTROY_ITEM</c>, 19; <c>TYPE_ITEM</c>, <c>TRIG_ONLY</c>).</summary>
public sealed record DestroyItemEffect(string ItemType, bool TriggererOnly) : EventEffect(19, "L_DESTROY_ITEM");

/// <summary>Убрать отряд (<c>L_REMOVE_STACK</c>, 20; <c>ID_STACK</c>, <c>FIRST_ONLY</c>).</summary>
public sealed record RemoveStackEffect(string StackId, bool FirstOnly) : EventEffect(20, "L_REMOVE_STACK");

/// <summary>Сменить ландмарк (<c>L_CHANGE_LANDMARK</c>, 21; <c>ID_LMARK</c>, <c>TYPE_LMARK</c>).</summary>
public sealed record ChangeLandmarkEffect(string LandmarkId, string LandmarkType) : EventEffect(21, "L_CHANGE_LANDMARK");

/// <summary>Сменить землю в области (<c>L_CHANGE_TERRAIN</c>, 22; <c>ID_LOC</c>, <c>LOOKUP</c>, <c>NUMVALUE</c>).</summary>
public sealed record ChangeTerrainEffect(string LocationId, int Lookup, int Value) : EventEffect(22, "L_CHANGE_TERRAIN");

/// <summary>Изменить переменную сценария (<c>L_MODIFY_VARIABLE</c>, 23): переменная <c>NUMVALUE2</c>, операция <c>LOOKUP</c>
/// (0 +, 1 −, 2 ×, 3 ÷, иначе присвоить), операнд <c>NUMVALUE</c> (<c>0x43995c</c>, поток <c>0x615e0e</c>, ранг 1,
/// <c>docs/scenario-events.md</c> §3).</summary>
public sealed record ModifyVariableEffect(int Variable, int Operation, int Operand) : EventEffect(23, "L_MODIFY_VARIABLE");

/// <summary>Эффект неизвестного вида (в ванили таких нет) — только номер.</summary>
public sealed record UnsupportedEffect(int Category, string Name) : EventEffect(Category, Name);

/// <summary>Область карты (<c>CMidLocation</c>): центр и радиус, как они лежат в <c>.sg</c>.</summary>
public sealed record EventZone(string Id, int X, int Y, int Radius);

/// <summary>Событие сценария (<c>CMidEvent</c>) — прямая проекция полей, ранг 1.</summary>
/// <param name="Races">Шесть флагов расы игрока, для которого событие работает, в порядке полей файла
/// <c>HUMAN, DWARF, UNDEAD, HERETIC, NEUTRAL, ELF</c>.</param>
/// <param name="TriggerRaces">Второй набор флагов (<c>VERHUMAN…VERELF</c> файла) в том же порядке; пусто — как
/// <paramref name="Races"/>.</param>
public sealed record ScenarioEvent(
    string Id,
    bool Enabled,
    bool OccurOnce,
    int Chance,
    int Order,
    IReadOnlyList<bool> Races,
    IReadOnlyList<EventCondition> Conditions,
    IReadOnlyList<EventEffect> Effects,
    string Name = "",
    IReadOnlyList<bool>? TriggerRaces = null);

/// <summary>События сценария и всё, без чего их не проверить: области (для <c>L_LOCATION</c>)
/// и переменные (для <c>L_MODIFY_VARIABLE</c>). Собирает <c>LastEdict.Data.Map.EventLoader</c>.</summary>
public sealed record ScenarioEventSet(
    IReadOnlyList<ScenarioEvent> Events,
    IReadOnlyDictionary<string, EventZone> Zones,
    IReadOnlyDictionary<int, int> Variables)
{
    public static readonly ScenarioEventSet Empty =
        new([], new Dictionary<string, EventZone>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<int, int>());

    /// <summary>Шаблоны отрядов (<c>MidStackTemplate</c>) — из них <c>L_CREATE_STACK</c> создаёт отряды; по id шаблона
    /// условия и эффекты находят его экземпляры (<c>SRCTMPL_ID</c>).</summary>
    public IReadOnlyDictionary<string, StackTemplate> Templates { get; init; } =
        new Dictionary<string, StackTemplate>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Игрок подрасы (<c>MidSubrace.PLAYER_ID</c>, <c>CMidSubRace +0x14</c>): <c>L_CHANGE_OWNER</c> задаёт нового владельца
    /// подрасой.</summary>
    public IReadOnlyDictionary<string, string> SubraceOwners { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Юнит шаблона отряда: тип и уровень (<c>UNIT_n</c>, <c>UNIT_nLVL</c>).</summary>
public sealed record TemplateUnit(string TypeId, int Level);

/// <summary>Модификатор юнита шаблона: клетка юнита и модификатор (<c>MODIF_n</c>).</summary>
public sealed record TemplateModifier(int Cell, string ModifierId);

/// <summary>Шаблон отряда (<c>MidStackTemplate</c>) — прямая проекция полей записи, ранг 1.</summary>
/// <param name="Units">Юниты шаблона в порядке записи (<c>UNIT_0…5</c>, пустые выброшены); <paramref name="Slots"/> — клетки
/// построения (<c>POS_0…5</c>: клетка → индекс в <paramref name="Units"/>, −1 — пусто), как у <see cref="MapStack.Slots"/>.</param>
public sealed record StackTemplate(
    string Id, string Owner, string Subrace, string LeaderType, int LeaderLevel, string Name, int Order, string OrderTarget,
    IReadOnlyList<TemplateUnit> Units, IReadOnlyList<int> Slots, bool UseFacing, int Facing,
    IReadOnlyList<TemplateModifier> Modifiers, int AiPriority);
