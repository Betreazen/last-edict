namespace LastEdict.Rules.Battle;

/// <summary>Четвёртый круг классов атак (задача 8 Ф6): <c>BestowWards</c> (24) и <c>Shatter</c> (25) —
/// последние два из двадцати трёх. Отдельным файлом по тому же правилу, что <see cref="LifeAttacks"/>
/// и <see cref="TransformAttacks"/>: <see cref="Attacks"/> остаётся диспетчером.
///
/// <para><b>Откуда числа.</b> Оба <c>onHit</c> прочитаны в дизассемблере GOG <c>Discipl2.exe</c>
/// (image base <c>0x400000</c>) до <c>ret</c> вместе со всем, что они зовут, — <b>ранг 1</b>. Точки
/// входа взяты из таблицы vftable задачи 6: <c>BestowWards</c> vft <c>0x6f2fac</c>, <c>onHit</c>
/// <c>0x65d528</c>; <c>Shatter</c> vft <c>0x6f2ffc</c>, <c>onHit</c> <c>0x65d8dc</c>.</para>
///
/// <para><b>Бросков — ноль у обоих.</b> Слот 6 vftable (<c>canMiss</c>) у обоих — общая заглушка
/// <c>0x6598f5</c> (<c>xor al,al</c>), то есть ни один не промахивается и точности не бросает; в
/// телах <c>onHit</c> генератор (<c>0x54851f</c>) не зовётся ни разу, случайной добавки у них нет
/// вовсе. Прежний список <c>attackHasPower</c> тулсета съедал у обоих по две кости — это уже
/// исправлено задачей 6 в <see cref="Formulas.ClassHasPower"/>, здесь то же закреплено тестом.</para>
///
/// <para><b>Иммунитет спрашивает только <c>Shatter</c>.</b> Слот 8: у <c>BestowWards</c> общая
/// заглушка <c>0x659913</c>, у <c>Shatter</c> своя <c>isImmune</c> <c>0x65d899</c> с прямым вызовом
/// <c>isUnitImmuneToAttack</c> (<c>0x65a528</c>). «Не промахивается» и «не проверяет иммунитет» —
/// разные свойства, и здесь они расходятся; список ведёт <see cref="Immunity.Checked"/>. В данных это
/// видно: в <c>GimmuC.dbf</c> пять юнитов неуязвимы именно к классу 25 (Myzrael, Vithar, Ashkael,
/// Ashgan, Illumielle).</para>
///
/// <para><b>Корпус к этому кругу слеп целиком.</b> Сплошной проход по всем 74 файлам
/// <c>corpus/**/*.jsonl</c>: типов юнитов в корпусе двадцать, ни один из семи вани́льных носителей
/// классов 24/25 (<c>g000uu8028</c>, <c>g000uu8033</c>, <c>g000uu8034</c>, <c>g000uu8035</c>,
/// <c>g000uu8050</c>, <c>g000uu8133</c>, <c>g000uu8150</c>) в него не попал; событий с id атак этих
/// классов ноль; все 3612 событий <c>armor</c> дают 0/40/50/60 — следа дробления брони нет. Ноль
/// здесь — законный результат, но он ничего не подтверждает: молчание журнала не отличает «формула
/// верна» от «случай не встретился». Проверку даст только бой задачи 13.</para></summary>
internal static class WardAttacks
{
    /// <summary>Потолок накопленной снятой брони — <b>100</b>, и это единственный потолок, который
    /// в движке есть. <c>BattleMsgData::setUnitShatteredArmor</c> (GOG <c>0x621e67</c>) целиком:
    /// найти запись юнита, <c>record-&gt;[0x9c] += value</c> (<c>0x621e8b</c>-<c>0x621e97</c>),
    /// и если стало больше <c>0x64</c> — записать <c>0x64</c> (<c>0x621ea0</c>-<c>0x621eac</c>).
    /// Поле <c>+0x9c</c> — то же, что читает <c>getUnitShatteredArmor</c> (<c>0x621ebc</c>), которую
    /// зовёт <c>computeArmor</c> (<c>0x5d2ce6</c>). Ранг 1.</summary>
    public const int ShatteredArmorMax = 100;

    /// <summary>Дробление брони (<c>onHit</c> <c>0x65d8dc</c>, тело — сорок инструкций).
    ///
    /// <para><b>Формула — ранг 1, и она проще обоих чужих реверсов:</b>
    /// <c>shattered = min(shattered + QTY_DAM, 100)</c>. Величина берётся одним вызовом
    /// <c>attack-&gt;getQtyDamage()</c> (слот 8 <c>IAttackVftable</c>, <c>call [vft+0x20]</c> на
    /// <c>0x65d930</c>) и уходит прямо в <c>setUnitShatteredArmor</c> (<c>0x65d93b</c>) — то есть
    /// <b>никакого <c>min</c> по броне цели, ни по остатку до потолка, ни по «максимуму за удар»
    /// в теле нет</b>. Урона по HP тоже нет: <c>changeUnitHp</c> в <c>onHit</c> не вызывается ни разу.</para>
    ///
    /// <para><b>Оба источника survey §4 здесь ошибаются, каждый по-своему</b> (это уже четырнадцатый
    /// случай за фазу). A (<c>hooks.cpp:1422-1521</c>, <c>unitutils.cpp:547-570</c>) считает
    /// <c>min(qtyDamage, armor, shatteredArmorMax − уже снятое, shatterDamageMax)</c> — три лишних
    /// потолка; два последних вообще не свойства игры, а настройки самого тулсета
    /// (<c>L/settings.lua:43-50</c>), то есть A снова описывает свой мод. B (<c>B:4381</c>) фиксирует
    /// <b>−15</b> — и это не формула, а вани́льное данное: у обеих атак класса 25 в игре
    /// (<c>g000aa8128</c> и <c>g000aa8150</c>) <c>QTY_DAM</c> равен 15, так что на ванили B угадывает
    /// каждый одиночный удар и промахивается на втором ударе по той же цели и на любом моде.</para>
    ///
    /// <para><b>Броня ниже фортификации не падает — и потому в ванили в минус не уходит.</b>
    /// <c>computeArmor</c> (<c>0x5d2c8f</c>, дизассемблирована до <c>ret</c>) считает
    /// <c>max(base − shattered, fortification)</c> (<c>0x5d2d02</c>–<c>0x5d2d18</c>), а
    /// <c>getUnitFortificationArmor</c> (<c>0x621f21</c>) вне города отдаёт 0 — значит нижняя граница
    /// ноль, а не минус (кламп 90 стоит только в ветке защиты и сюда не относится). Снятое сверх
    /// базовой брони просто пропадает: у юнита с бронёй 20 второй удар по 15 к результату уже ничего
    /// не добавляет, хотя счётчик снятого растёт. <c>Formulas.Armor</c> устроен так же и менять его не
    /// нужно. Ранг 1 — этого не сказано ни в A, ни в B, и первая редакция теста здесь ошиблась именно
    /// так (ждала −10), потому тест и остался в файле.</para>
    ///
    /// <para><b>Бросков 0</b>, иммунитет спрашивается (см. заголовок класса).</para></summary>
    public static AttackOutcome Shatter(AttackType attack, BattleUnit target)
    {
        target.ShatteredArmor = Math.Min(target.ShatteredArmor + attack.QtyDamage, ShatteredArmorMax);
        // Applied — «удар состоялся», а не «величина изменилась»: движок пишет BattleAttackInfo
        // (0x62c462) безусловно, в том числе когда накопленное уже упёрлось в потолок.
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Наложение вардов (<c>onHit</c> <c>0x65d528</c>). Тело — цикл по
    /// <c>attack-&gt;getWards()</c> (слот 14, <c>call [vft+0x38]</c> на <c>0x65d56c</c>: это
    /// <c>Gattacks.WARD1..4</c>), а всё содержательное — в <see cref="Immunity.Bestow"/>, где и
    /// разобрано по адресам: отсев повтора, потолок восемь, возврат заряда <c>Once</c>.
    ///
    /// <para><b>Бросков 0</b>, иммунитета не спрашивает вовсе — обе заглушки (см. заголовок класса).
    /// Целится в своих: слот 2 (<c>getTargetGroupId</c>) зовёт <c>getAllyOrEnemyGroupId</c>
    /// (<c>0x659a32</c>) с <c>push 1</c>, ранг 1 по замеру задачи 7 — потому класс и стоит в
    /// <see cref="Attacks.TargetsAllies"/>.</para>
    ///
    /// <para><b>Вани́льных атак три:</b> <c>g000aa8133</c> (все четыре варда, REACH 2),
    /// <c>g000aa8134</c> и <c>g000aa8135</c> (по одному, REACH 1 — то есть на весь свой отряд). У всех
    /// трёх SOURCE 2 (Life) и POWER 100. <c>QTY_WARDS</c> модель не читает: это просто длина списка
    /// <c>WARD1..4</c>, а движок берёт сам список.</para>
    ///
    /// <para><c>Applied</c> — лёг ли хоть один вард. Ноль лёгших бывает законно: у цели уже стоят все
    /// варды этой атаки (после отсева повторов это в ванили обычный случай при повторном ударе).</para></summary>
    public static AttackOutcome BestowWards(BattleUnit attacker, AttackType attack, BattleUnit target, BattleState? state)
    {
        var rules = state?.Rules ?? throw new InvalidOperationException(
            $"класс BestowWards кладёт на цель модификаторы Gmodif (атака {attack.Id}) и потому требует " +
            "BattleState.Rules; бой собран без справочников");
        return new AttackOutcome(false, 0, 0, Immunity.Bestow(target, rules.WardsOf(attack), attacker) > 0);
    }
}
