namespace D2.Rules.Units;

/// <summary>Модификатор — строка <c>Gmodif.dbf</c> со своими строками эффекта из <c>GmodifL.dbf</c>
/// в порядке файла. Через него в игру попадает всё, что Ф6 добавляет поверх базового юнита:
/// заклинание, предмет, вард, бафф, способность лидера, защита города — <c>Gspells.MODIF_ID</c>,
/// <c>GItem.MOD_EQUIP</c>/<c>MOD_POTION</c>, <c>Gattacks.WARD1..4</c>, <c>GleaUpg.MODIF_ID</c>,
/// <c>GcityInf.PROTECTION</c>, <c>Grace.PROTECTION</c> ведут сюда.
/// <para>Элементов у ванильного модификатора обычно один; двухэлементных всего два —
/// <c>g000um2306</c> и <c>g000um2307</c> (QtyDamage +40 % и Initiative +25/+50 %).</para>
/// <para><c>SOURCE</c> из <c>Gmodif</c> (0 Unit, 1 Stack, 3 Attack — <c>categoryids.h</c>,
/// <c>ModifierSourceCategory</c>) правилам не нужен: какой элемент к чему относится, задаёт сам
/// <see cref="ModifierElement.Type"/>. Не тащим то, чем не пользуемся.</para>
/// </summary>
public sealed record Modifier(string Id, IReadOnlyList<ModifierElement> Elements);
