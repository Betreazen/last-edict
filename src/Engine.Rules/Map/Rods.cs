namespace D2.Rules.Map;

/// <summary>
/// Итог команды жезла. <see cref="Refused"/> непусто — команды не было, и <see cref="Purse"/> с
/// <see cref="Stack"/> прежние (отказ происходит ДО списания: ранг 1 — мана не убыла ни разу за
/// прогон 2 <c>m6-rods</c>, где Sinkhole по своему жезлу отказан, run2 §2.4).
/// </summary>
/// <param name="MoveLeft">Остаток очков хода отряда после команды: у постановки и сноса это
/// <see cref="Movement.WholeTurnSpent"/>, у отказа — прежний остаток. Числом, а не новым
/// <see cref="MapStack"/>: отряды в порте — неизменяемый снимок <c>.sg</c>, а живой остаток хода
/// ведёт <see cref="MapSim"/> отдельным счётом.</param>
/// <param name="Placed">Поставленный жезл; <c>null</c> — команда его не ставила.</param>
/// <param name="Removed">Id снесённого жезла; пусто — команда ничего не сносила. Землю снос НЕ
/// возвращает (<see cref="Rods.Break"/>).</param>
/// <param name="Blamed">Есть виновник сноса чужого жезла — источник события <c>D_ROD</c> −15.
/// Само значение отношений здесь не считается: это задача 13 (план Ф7 §1.8).</param>
public sealed record RodCommand(int MoveLeft, Bank Purse, MapRod? Placed = null, string Removed = "",
                                bool Blamed = false, string? Refused = null)
{
    public bool Done => Refused is null;
}

/// <summary>
/// Жезл (<c>CMidRod</c>): постановка, снос и их цена. Функции чистые — казну меняет
/// <see cref="Treasury"/>, очки хода <see cref="Movement.WholeTurnSpent"/>, землю
/// <see cref="LandModel"/> (жезл там — обычный источник с квотой <c>ROD_RANGE</c>).
///
/// <para><b>Что жезл даёт.</b> Ставится — сразу 3 клетки (своя клетка плюс обход на
/// <c>ROD_RANGE</c> = 2), дальше +2 в сутки бессрочно; это правило и его числа перенесены задачей 7
/// (<see cref="LandModel.AcquisitionCells"/>, <see cref="LandSource"/>), здесь не дублируются.
/// Ранг 1: <c>CEffectDropRod::apply</c> <c>0x437606</c> красит клетку жезла (<c>0x4377B2</c> →
/// <c>0x5DA43F</c>) и сразу прогоняет обход (<c>0x4377D7</c> → <c>0x5DA38F</c>); в корпусе — семь
/// установок по 3 клетки (харнесс <c>rod_placement</c> задачи 7, 14/0).</para>
///
/// <para><b>Две калитки постановки.</b> Клиентская <c>CVisitorCreateRod::canApply</c>
/// <c>0x6137FE</c>: грунт клетки <b>равнина</b> (<c>0x61388B</c>), в клетке нет объектов кроме
/// <c>IdType::Location</c> = 47 (<c>0x6138A1…0x613928</c>), и в коробке <b>3×3</b> вокруг точки нет
/// объекта <c>IdType::Fortification</c> = 29 (<c>0x61392F…0x61397C</c>). Серверная
/// <c>tryPlaceRod</c> <c>0x5D9E59</c>: коробка <b>5×5</b> без чужого и своего жезла (код 9,
/// <c>0x5D9BDB</c>), не земля союзника (код 8, <c>0x5D9D1D</c> → <c>areAllies</c>
/// <c>0x5F78FC</c>, текст <c>X005TA0647</c>), хватает золота (код 5, <c>ROD_COST</c>
/// <c>0x587851</c>). Лимита на ЧИСЛО жезлов нет — проверены обе калитки, поля <c>CMidRod</c>,
/// <c>CMidPlayer +0x8C/+0x98</c> и <c>GVars</c> (heroes §3.3–3.4, ранг 1).</para>
///
/// <para><b>Порядок отказов — ранг 1</b> по синтетической карте ai-q27 (216 строк <c>rod_check</c>, модель
/// 216/0): соседний жезл (9) называется <b>первым</b> — раньше грунта (7) 4 строки, раньше объекта 3,
/// раньше земли союзника (8) 3; грунт и объект (7) — раньше союзника (8), 15 строк. Позиция казны (5) —
/// последней, ранг 3: кода 5 журнал не видел — при пустой казне отказывает интерфейс раньше проверки, а
/// ровно 150 проходит (2/2). Укрепление в 3×3 стоит вместе с грунтом и объектом (клиентская калитка).</para>
/// </summary>
public static class Rods
{
    /// <summary>Способность <c>L_ROD</c> = 13 (<c>LleadA.dbf</c>). Проверка идёт по СПОСОБНОСТИ, а
    /// не по категории лидера: <c>hasAbility(leader, abilities.rod)</c>
    /// (<c>tools/d2probe/mss32/src/unitutils.cpp:868</c>, ранг 2; глобаль
    /// <c>LeaderAbilityCategories::rod</c> = <c>0x837618</c>). Ранг 1 на само число — строка
    /// таблицы; в ванили её носят четыре расовых жезлоносца и <c>g000uu8012</c>
    /// (<c>GLabi.dbf</c>).</summary>
    public const int LeaderAbilityRod = 13;

    /// <summary>Полуширина коробки, в которой не должно быть укрепления: 3×3
    /// (<c>0x61392F</c>, <c>x−1..x+1</c> × <c>y−1..y+1</c>). Ранг 1.</summary>
    public const int FortKeepOut = 1;

    /// <summary>Полуширина коробки, в которой не должно быть другого жезла: 5×5
    /// (<c>0x5D9BDB</c>, <c>dx, dy ∈ [−2, +2]</c>) — минимальный отступ между жезлами 3 по
    /// Чебышёву. Ранг 1.</summary>
    public const int RodKeepOut = 2;

    private const string LocationKind = "LO";     // IdType::Location = 47 — единственный разрешённый
    private const string FortKind = "FT";         // IdType::Fortification = 29
    private const string RodKind = "RD";          // IdType::Rod = 43

    /// <summary>
    /// Почему жезл сюда поставить нельзя; <c>null</c> — можно. Ровно две калитки движка
    /// (описание класса) в одном месте: их нельзя разносить — отказ любой из них означает, что
    /// жезла в этой клетке в игре не появится.
    /// <para><b>Владельца мешающих объектов ни одна калитка не смотрит</b>, поэтому его здесь и нет
    /// среди параметров: своё укрепление в 3×3 запрещает постановку так же, как чужое, и свой жезл
    /// в 5×5 так же, как чужой (<c>0x61392F</c> и <c>0x5D9BDB</c> ищут объект по типу и на этом
    /// останавливаются). Единственная калитка, которой владение важно, — земля союзника, и она
    /// приходит предикатом <paramref name="allyTerrain"/>.</para>
    /// </summary>
    /// <param name="leaderAbilities">Способности лидера отряда — все три источника задачи 9:
    /// <c>GLabi</c> его типа, навыки уровня и <c>MOD_EQUIP</c> надетого
    /// (<see cref="Units.LeaderAbilities.Split"/>; <c>GLabi</c> читает
    /// <c>D2.Data.Magic.EquipLoader.LoadInnateAbilities</c>). Приходит списком, а не собирается
    /// здесь: правило жезла спрашивает «есть ли способность», а откуда она — дело
    /// <c>LeaderAbilities</c>.</param>
    /// <param name="allyTerrain">Террейн клетки (<c>Lterrain.ID</c>) принадлежит расе СОЮЗНИКА.
    /// Предикат строит <see cref="Diplomacy.AllyTerrain"/> (задача 13): союз — бит 0 пары рас,
    /// раса → террейн — <see cref="RaceInfo.Terrain"/>. <c>null</c> — союзников нет и калитка
    /// не спрашивается. Наблюдений кода 8 — три (ai-q27: (19,30), (35,35), (35,34), 3/0, план §3 п. 27).</param>
    public static string? PlaceRefusal(MapState map, int x, int y, Bank purse, TreasuryRules rules,
                                       IReadOnlyList<int> leaderAbilities,
                                       Func<int, bool>? allyTerrain = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(leaderAbilities);
        if (!map.Contains(x, y)) return $"клетка ({x},{y}) вне карты";
        if (!leaderAbilities.Contains(LeaderAbilityRod))
            return $"у лидера отряда нет способности L_ROD ({LeaderAbilityRod})";
        if (Near(map, x, y, RodKeepOut, RodKind) is { } rod)
            return $"жезл {rod} ближе чем через две клетки от ({x},{y})";
        if (map.At(x, y).Ground != Ground.Plain)
            return $"клетка ({x},{y}) — грунт {map.At(x, y).Ground}, а жезл ставится только на равнину";
        if (Foreign(map, x, y) is { } here) return $"в клетке ({x},{y}) стоит объект {here}";
        if (Near(map, x, y, FortKeepOut, FortKind) is { } fort)
            return $"укрепление {fort} ближе чем через клетку от ({x},{y})";
        if (allyTerrain is not null && allyTerrain(map.At(x, y).Terrain))
            return $"клетка ({x},{y}) — земля союзника";
        if (Treasury.Affordable(purse, Treasury.RodPrice(rules)) < 1)
            return $"казны не хватает на жезл ({Treasury.RodPrice(rules)}, есть {purse})";
        return null;
    }

    /// <summary>
    /// Постановка жезла: цена <c>ROD_COST</c> = 150 из казны и <b>весь остаток хода</b> отряда
    /// (ранг 1: три установки прогона 2 <c>m6-rods</c> — <c>MOVE</c> жезлоносца в сохранении 0,
    /// у не ставившего 20; run2 §2.2). Землю в этот же момент красит
    /// <see cref="LandModel.AcquisitionCells"/> — 1 + <c>ROD_RANGE</c> клетки.
    /// <para>Отношения постановка НЕ трогает: <c>CEffectDropRod::apply</c> не заходит в
    /// <c>0x5D93xx…0x5D9Axx</c> вовсе (ранг 1, run4 §4.6). <i>Было иначе:</i> «−15 за постановку».</para>
    /// </summary>
    /// <param name="rodId">Id нового объекта <c>RD</c> — его выдаёт сценарий, не правило.</param>
    public static RodCommand Place(MapState map, MapStack stack, string rodId, int x, int y, Bank purse,
                                   TreasuryRules rules, IReadOnlyList<int> leaderAbilities,
                                   Func<int, bool>? allyTerrain = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (PlaceRefusal(map, x, y, purse, rules, leaderAbilities, allyTerrain) is { } why)
            return new RodCommand(stack.Move, purse, Refused: why);
        return new RodCommand(Movement.WholeTurnSpent, Treasury.Pay(purse, Treasury.RodPrice(rules)),
                              Placed: new MapRod(rodId, stack.Owner, x, y));
    }

    /// <summary>
    /// Снос жезла отрядом: отряд идёт на клетку жезла и остаётся рядом — обработчик шага
    /// <c>0x4408cb</c> создаёт <c>CEffectBreakRod</c> (<c>0x440cd5</c>; других создателей, кроме заклинания
    /// <c>CSpellResultRemoveRod</c> <c>0x41cd7e</c>, в exe нет), визитёр сноса — <c>CVisitorDestroyRod</c>
    /// <c>0x613adb</c>. Ранг 1 по прогону 2 <c>m6-rods</c> (run2 §2.2, три сноса из трёх):
    /// <list type="bullet">
    /// <item>отряд НЕ двигается — позиции всех трёх жезлоносцев одинаковы во всех трёх сохранениях;</item>
    /// <item>денег не берут — между сносами ни одной строки <c>bank</c> с тратой;</item>
    /// <item>расход — весь остаток хода: <c>MOVE</c> = 0 ровно у сносившего, 20 у остальных.</item>
    /// </list>
    /// <para><b>Земля замерзает, а не возвращается</b> (ранг 1): <c>CVisitorDestroyRod::apply</c> —
    /// семнадцать инструкций, стирающих объект, и ни одного обращения к террейну
    /// (<c>changeMapTerrain</c> <c>0x5E6ED7</c> не вызывается ни им, ни <c>CEffectBreakRod</c>
    /// <c>0x433C65</c>, ни <c>CSpellResultRemoveRod</c>). Захваченные клетки остаются игроку,
    /// прирост прекращается — в модели это <see cref="LandSource.Until"/>, и поэтому здесь нет ни
    /// одной операции над землёй.</para>
    /// <para>Чужой жезл со <b>виновником</b> даёт <c>D_ROD</c> −15 (<see cref="RodCommand.Blamed"/>,
    /// величина и третья сторона — <see cref="Diplomacy.RodDestroyed"/>):
    /// <c>CEffectBreakRod::apply</c> <c>0x433C65</c> сравнивает <c>actorId</c> с пустым id и только
    /// при непустом идёт в <c>0x5D9FAB</c> (штраф + снос); путь заклинания строит эффект с пустым
    /// виновником (ранг 1, run4 §4.6).</para>
    /// <para><b>Контакт устанавливает вызывающий</b> — как у <see cref="Sites.Visit"/>: что отряд
    /// стоит рядом с жезлом, здесь не проверяется.</para>
    /// <para><b>Три калитки сноса — ранг 1</b> (§3 п. 28): <c>CEffectBreakRod::apply</c> <c>0x433c65</c> с
    /// виновником зовёт <c>0x5d9fab</c>, а та сносит, только если <c>0x5d9eea(map, stack, rod, 0)</c>
    /// вернула true; иначе она выходит ДО траты хода (<c>0x5d9ff6</c>), штрафа (<c>0x5da006</c>) и сноса
    /// (<c>0x5da011</c>). Порядок в <c>0x5d9eea</c>: (1) отряд внутри укрепления — отказ
    /// (<c>CMidStack+0x94 insideId</c> не пуст); (2) хозяин жезла — союзник хозяина отряда
    /// (<c>0x5f7ebd</c>: пустой или тот же игрок союзником не считается — свой жезл сносить можно) —
    /// отказ; (3) <c>hasAbility(лидер, L_ROD)</c> (<c>0x5d9f9b</c>, глобаль <c>0x837618</c>).
    /// <i>Было иначе:</i> «<c>L_ROD</c> не требуется» (ранг 3 по <c>0x440cd5</c>, где проверки нет —
    /// она ниже по цепочке). Корпус этого не различает: все шесть сносов сделали жезлоносцы
    /// (<c>G000UU8012</c>, <c>G000UU0022</c>, <c>G000UU0099</c>, у всех <c>GLabi</c> 13).</para>
    /// </summary>
    /// <param name="leaderAbilities">Способности лидера отряда (как у <see cref="PlaceRefusal"/>).</param>
    /// <param name="rodOwnerIsAlly">Хозяин жезла — союзник хозяина отряда (<see cref="Diplomacy"/>);
    /// свой жезл союзником не считается.</param>
    public static RodCommand Break(MapStack stack, MapRod rod, Bank purse, IReadOnlyList<int> leaderAbilities,
                                   bool rodOwnerIsAlly = false)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(rod);
        ArgumentNullException.ThrowIfNull(leaderAbilities);
        bool own = rod.Owner.Equals(stack.Owner, StringComparison.OrdinalIgnoreCase);
        string? why = stack.Inside.Length > 0 ? $"отряд {stack.Id} внутри укрепления {stack.Inside}"
            : rodOwnerIsAlly && !own ? $"жезл {rod.Id} принадлежит союзнику"
            : !leaderAbilities.Contains(LeaderAbilityRod) ? $"у лидера отряда нет способности L_ROD ({LeaderAbilityRod})"
            : null;
        return why is not null
            ? new RodCommand(stack.Move, purse, Refused: why)
            : new RodCommand(Movement.WholeTurnSpent, purse, Removed: rod.Id, Blamed: !own);
    }

    /// <summary>
    /// Почему заклинание снятия жезла («Sinkhole», <c>g000ss0119</c> — единственное в ванили) эту
    /// цель не берёт; <c>null</c> — берёт. Свой жезл своим заклинанием снести <b>нельзя</b>.
    /// <para><b>Ранг 1, отрицательный факт</b> (run2 §2.4): на <c>m6-rods</c> заклинание лежало в
    /// <c>CPlayerKnownSpells</c> всех трёх сохранений, маны было по 1000 каждого вида, а за весь
    /// прогон ни один из пяти видов маны не убыл ни разу (83 строки <c>bank</c>) — то есть каст не
    /// состоялся ни разу и движок отказал ДО списания. Запрет живёт в проверке цели заклинания, а
    /// не в визитёре сноса: <c>CVisitorDestroyRod::canApply</c> <c>0x613a8b</c> владельца не
    /// спрашивает вовсе — потому жезлоносцем можно, а Sinkhole нельзя.</para>
    /// <para><b>Чужой жезл — ранг 1</b> (ai-q29, 2/0): два каста <c>G000SS0119</c> по чужим жезлам в один
    /// день (seq 9879970, 9880537) — жезлы сняты, списано по y80/e60/w110/b250, отношения не менялись
    /// (<c>relation</c> 0 строк). <b>Свой — ранг 1</b> (<c>20260917-synth</c> q29b): в день 1, когда
    /// рядом был только свой жезл, каста не было и мана цела; в день 2 каст (seq 8676329) снял чужой жезл E2
    /// (<c>CMidRod</c> 3 → 2), свой RD0002 цел. Плюс отрицательный факт m6-rods выше. Адрес проверки цели не найден —
    /// здесь наблюдаемый запрет «свой — отказ», а не воспроизведённый движковый код.</para>
    /// </summary>
    public static string? SpellRefusal(MapRod rod, string casterPlayerId)
    {
        ArgumentNullException.ThrowIfNull(rod);
        return rod.Owner.Equals(casterPlayerId, StringComparison.OrdinalIgnoreCase)
            ? $"жезл {rod.Id} свой: своим заклинанием свой жезл не сносится"
            : null;
    }

    /// <summary>Объект в клетке, который мешает постановке: годится только
    /// <c>IdType::Location</c> (<c>LO</c>). Отряд в занятости карты лежит (класс <c>KC</c>,
    /// измерено на плане <c>m6-rods</c>), поэтому клетка под отрядом отказная — так и в движке
    /// (<c>IdType::Stack</c> = 35 ≠ 47).</summary>
    private static string? Foreign(MapState map, int x, int y) =>
        map.Plan.TryGetValue((x, y), out var ids)
            ? ids.FirstOrDefault(id => !Kind(id).Equals(LocationKind, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Объект класса <paramref name="kind"/> в коробке со полушириной
    /// <paramref name="half"/> вокруг клетки; <c>null</c> — нет такого.</summary>
    private static string? Near(MapState map, int x, int y, int half, string kind)
    {
        for (int dx = -half; dx <= half; dx++)
            for (int dy = -half; dy <= half; dy++)
                if (map.Plan.TryGetValue((x + dx, y + dy), out var ids) &&
                    ids.FirstOrDefault(id => Kind(id).Equals(kind, StringComparison.OrdinalIgnoreCase))
                        is { } found)
                    return found;
        return null;
    }

    /// <summary>Две буквы класса из id объекта (<c>S143<b>RD</b>0000</c>).</summary>
    private static string Kind(string id) => id.Length >= 6 ? id.Substring(4, 2) : "";
}
