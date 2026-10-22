namespace D2.Rules.Units;

/// <summary>Типы элементов модификатора — строка <c>GmodifL.TYPE</c>.
/// <para>Ранг 1: перечисление есть в самой игре — <c>Globals/LmodifE.dbf</c> отдаёт ровно эти
/// семнадцать пар (<c>0 L_QTY_TARGET … 17 L_LOWER_COST</c>, значения 8 нет), и они совпадают
/// строка в строку с <c>reference/D2ModdingToolset/mss32/include/categoryids.h:251-270</c>
/// (<c>enum class ModifierElementType</c>). Чужой реверс здесь не источник, а подтверждение.</para>
/// <para>Какую колонку <c>GmodifL</c> несёт каждый тип — измерено по всем 187 живым строкам
/// ванили (задача 2, кросс-таблица «тип × заполненные колонки»): у каждого типа заполнена ровно
/// одна значащая колонка, кроме <see cref="Hp"/>. <c>IMMUNECAT = 2</c> стоит фоном почти везде и
/// значит что-то только при <see cref="Immunity"/> — см. отчёт задачи 1.</para>
/// <list type="table">
/// <item><term>1 ScoutingRange</term><description>NUMBER, слагаемое («Keen-Sight» +3)</description></item>
/// <item><term>2 Leadership</term><description>NUMBER, слагаемое («+1 Leadership»)</description></item>
/// <item><term>3 Power</term><description>PERCENT («+ 33% Power», x000tgc036)</description></item>
/// <item><term>4 QtyDamage</term><description>PERCENT</description></item>
/// <item><term>5 Armor</term><description>NUMBER («+ 20 Armor», x000tgc012)</description></item>
/// <item><term>6 Hp</term><description>PERCENT — величина, NUMBER — вид: 1 процент, 2 число</description></item>
/// <item><term>7 MoveAllowance</term><description>PERCENT («Boots 3 (+25% Move)»)</description></item>
/// <item><term>9 Initiative</term><description>PERCENT («+ 10% Initiative», x000tgc002)</description></item>
/// <item><term>10 MoveAbility</term><description>MOVE — id из <c>Lground.dbf</c> (3 = вода, «Enhances Sailing ability»)</description></item>
/// <item><term>11 LeaderAbility</term><description>ABILITY — id из <c>LleadA.dbf</c></description></item>
/// <item><term>12 Immunity</term><description>IMMUNITY — источник атаки (<c>LattS.dbf</c>), IMMUNECAT — уровень (<c>LImmune.dbf</c>)</description></item>
/// <item><term>13 Regeneration</term><description>PERCENT («Add 10% regeneration», x000tg6035)</description></item>
/// <item><term>15 AttackDrain</term><description>PERCENT («Drain Life», x000tg8817)</description></item>
/// <item><term>16 FastRetreat</term><description>флаг, числовых полей нет («Escape»)</description></item>
/// <item><term>17 LowerCost</term><description>PERCENT («Lute of Charming»)</description></item>
/// </list>
/// <para><see cref="QtyTarget"/> и <see cref="Immunityclass"/> в ванили не встречаются ни разу
/// (0 строк из 187) — <see cref="EffectiveStats"/> на них бросает, а не молчит.</para>
/// </summary>
public enum ModifierElementType
{
    QtyTarget = 0,
    ScoutingRange = 1,
    Leadership = 2,
    Power = 3,
    QtyDamage = 4,
    Armor = 5,
    Hp = 6,
    MoveAllowance = 7,
    Initiative = 9,
    MoveAbility = 10,
    LeaderAbility = 11,
    Immunity = 12,
    Regeneration = 13,
    Immunityclass = 14,
    AttackDrain = 15,
    FastRetreat = 16,
    LowerCost = 17,
}

/// <summary>Один элемент эффекта — строка <c>GmodifL.dbf</c>.
/// <para>Все числовые поля — <c>int?</c>: в движке это <c>ModifierValue { bool initialized; int value; }</c>
/// (<c>ummodifier.h</c>), и «не задано» отличается от «задано нулём». Различие настоящее и в
/// данных: по всей таблице 771 поле «не задано» против двух «задано нулём» (<c>g000um0079</c>
/// хранит <c>IMMUNITY = 0</c>, <c>g000um2109</c> — <c>MOVE = 0</c>). Поэтому нужное полю значение
/// берётся через проверку, а не через <c>?? 0</c>.</para>
/// <para>Определение одно на решение: <c>D2.Data.Battle.ModifierLoader</c> строит сразу эту
/// запись, разбирая <c>TYPE</c> на месте. Ссылка идёт только <c>D2.Data → D2.Rules</c>, поэтому
/// дом у записи здесь (задача 3 свела сюда копию из <c>D2.Data.Battle</c>).</para>
/// </summary>
public sealed record ModifierElement(ModifierElementType Type, int? Percent = null, int? Number = null,
                                     int? Ability = null, int? Immunity = null, int? ImmuneCat = null,
                                     int? Move = null);

/// <summary>Уровень иммунитета — <c>Globals/LImmune.dbf</c> (три строки: 1 <c>L_NOTIMMUNE</c>,
/// 2 <c>L_ONCE</c>, 3 <c>L_ALWAYS</c>). <b>Ранг 1</b>: таблица самой игры; <c>categoryids.h:172-178</c>
/// тулсета совпадает и служит подтверждением.</summary>
public enum ImmunityLevel
{
    NotImmune = 1,
    Once = 2,
    Always = 3,
}

/// <summary>Иммунитет: к чему и насколько.
/// <para><paramref name="Category"/> — либо <b>источник</b> атаки (<c>LattS.dbf</c>: 0 Weapon,
/// 1 Mind, 2 Life, 3 Death, 4 Fire, 5 Water, 6 Earth, 7 Air), либо <b>класс</b> атаки
/// (<c>LattC.dbf</c>: 1 Damage … 25 Shatter) — смотря из какой таблицы взята запись. Одна запись
/// на оба случая потому, что в данных это буквально одна и та же пара колонок: у <c>Gimmu.dbf</c>
/// и <c>GimmuC.dbf</c> колонки совпадают (<c>UNIT_ID, IMMUNITY, IMMUNECAT</c>), различается только
/// смысл <c>IMMUNITY</c>.</para>
/// <para><b>Ранг 1 — и он исправляет подпись задачи 1.</b> Комментарий
/// <c>RulesLoader.ImmunityRow</c> называл <c>IMMUNITY</c> уровнем, а <c>IMMUNECAT</c> категорией;
/// данные говорят обратное. В <c>Gimmu</c> <c>IMMUNITY</c> принимает значения 0..7 (ровно
/// <c>LattS</c>), а <c>IMMUNECAT</c> — только 2 и 3 (124 и 103 строки, ровно <c>LImmune</c>
/// Once/Always). В <c>GimmuC</c> <c>IMMUNITY</c> принимает только 12 и 25 — это <c>L_POISON</c> и
/// <c>L_SHATTER</c> из <c>LattC</c> (8 «каменных» юнитов не травятся, 5 драконоподобных не
/// крошатся), а <c>IMMUNECAT</c> всюду 3. Уровнем 12 и 25 быть не могут: в <c>LImmune</c> всего
/// три строки.</para>
/// <para>То же у <c>GmodifL</c> <c>TYPE 12</c>: <c>IMMUNITY</c> — источник, <c>IMMUNECAT</c> —
/// уровень; все шесть вани́льных вардов дают <c>Once</c> к своему источнику.</para>
/// <para>Проверка и расход <c>Once</c> — <c>D2.Rules.Battle.Immunity</c>.</para></summary>
public readonly record struct ImmunityGrant(int Category, ImmunityLevel Level);
