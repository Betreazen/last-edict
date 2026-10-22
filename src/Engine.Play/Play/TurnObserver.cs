using LastEdict.Rules.Battle;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>
/// Наблюдатель хода партии — точка расширения для показа хода ИИ (<c>docs/ai-screen.md</c>). Ядро зовёт его по ходу
/// <see cref="GameSession.EndTurn"/> и ничего не меняет в зависимости от него, кроме боя: <see cref="Battle"/> может
/// сыграть бой ИИ снаружи (экраном боя, где человек ведёт свою сторону). Без наблюдателя поведение прежнее.
/// </summary>
public interface ITurnObserver
{
    /// <summary>Начинается ход игрока ИИ (до его начала хода и стратегии).</summary>
    void PlayerTurn(string playerId);

    /// <summary>Отряд сейчас пойдёт (<see cref="GameSession.MoveStack"/>): состояние ещё прежнее.</summary>
    void StackMoving(StackMove move);

    /// <summary>Бой ИИ собран (<see cref="GameSession.StartBattle"/> уже вызван, <paramref name="state"/> — то состояние, которое
    /// примет <see cref="GameSession.FinishBattle"/>). Вернуть журнал боя, сыгранного на этом состоянии генератором партии
    /// (<see cref="GameSession.BattleRng"/>), — или null: тогда бой идёт автобоем, как без наблюдателя.</summary>
    IReadOnlyList<TurnRecord>? Battle(BattleState state);

    /// <summary>Запрос решения боя (<c>docs/game-commands.md</c> §2): ядро спрашивает так, ответ — данные. Журнал боя и, если бой вёл
    /// человек по шагам, его действия (<see cref="BattleDecision.Play"/>) — по ним журнал команд повторит бой. По умолчанию — журнал от
    /// <see cref="Battle"/>.</summary>
    BattleDecision? DecideBattle(BattleState state) => Battle(state) is { } log ? new BattleDecision(log) : null;

    /// <summary>На карте случилось что-то без хода по клеткам (<see cref="TurnSight"/>): захват укрепления, наём героя, литьё
    /// заклинания. По умолчанию — ничего.</summary>
    void Happened(TurnSight sight)
    {
    }

    /// <summary>ИИ предложил человеку союз, сделку или обмен картами (<see cref="GameState.Offers"/> уже с предложением). У движка
    /// оно приходит сразу, в ход ИИ (<c>CEffectProposeAlliance</c> <c>0x43b5c8</c> шлёт немедленно; журнал human: ход 43 —
    /// <c>alliance_propose</c> → окно → ответ → ИИ продолжает ход): наблюдатель может дать человеку ответить сейчас — тем же
    /// <c>Answer*Offer</c>, пока ход ИИ ждёт возврата. Не ответил — предложение ждёт начала хода человека, как прежде. По умолчанию —
    /// ничего (харнессы и проверки без сцены).</summary>
    void Offered(AllianceOffer offer)
    {
    }

    /// <summary>Начался ход человека (<c>BeginHumanTurn</c>: земля, доход, события начала хода уже прошли, ход — его); в «горячем стуле»
    /// — каждого. По умолчанию — ничего (зонд порта пишет снимок дня и сохранение).</summary>
    void HumanTurn(string playerId)
    {
    }

    /// <summary>Бой записан в карту (<see cref="GameSession.CommitBattle"/>: потери, опыт, гибель отрядов, вход в объект; события конца
    /// боя — ещё нет). Бои ИИ, человека и событий. По умолчанию — ничего.</summary>
    void BattleEnded(BattleOutcome outcome)
    {
    }

    /// <summary>Сработало событие сценария (<c>GameSession.Fire</c>): эффекты уже применены. По умолчанию — ничего.</summary>
    void EventFired(EventFiring firing)
    {
    }
}

/// <summary>Решение боя снаружи ядра (<see cref="ITurnObserver.DecideBattle"/>): журнал ходов, сыгранный на генераторе партии, и, если
/// бой сыгран по шагам (экран боя), — его действия (<see cref="LastEdict.Data.Battle.BattlePlay"/>).</summary>
public sealed record BattleDecision(IReadOnlyList<TurnRecord> Log, LastEdict.Data.Battle.BattlePlay? Play = null);

/// <summary>Итог боя для наблюдателей: группы сторон и их хозяева (укрепление и руина — хозяин укрепления, у руины пусто), победитель
/// (группа, что держит поле: отступивший нападающий проиграл), раундов, павших (не призванных) и полученного опыта по сторонам, день.</summary>
public sealed record BattleOutcome(string Attacker, string AttackerOwner, string Defender, string DefenderOwner, string Winner,
                                   int Rounds, int AttackersFallen, int DefendersFallen, int AttackersXp, int DefendersXp, int Day)
{
    /// <summary>Победил нападающий.</summary>
    public bool AttackerWon => Winner.Equals(Attacker, StringComparison.OrdinalIgnoreCase);

    /// <summary>Классы атак (<c>AttackClass</c>), которыми ходили юниты нападающих и защитников (по классу юнита на начало боя; вторая
    /// атака — своим классом), по алфавиту; для зонда порта (синтетические карты, <c>docs/synthetic-maps-port</c>).</summary>
    public IReadOnlyList<string> AttackerClasses { get; init; } = [];

    public IReadOnlyList<string> DefenderClasses { get; init; } = [];

    /// <summary>Типы предметов, применённых в бою (сфера, талисман, зелье), по порядку применения.</summary>
    public IReadOnlyList<string> ItemsUsed { get; init; } = [];

    /// <summary>Юнитов, бежавших с поля, по сторонам.</summary>
    public int AttackersRetreated { get; init; }

    public int DefendersRetreated { get; init; }

    /// <summary>Юниты, бежавшие с поля (обеих сторон, по id): по ним разбор отличает бегство самого лидера от бегства его бойцов (FR карты 1:
    /// лидер с «Ржавыми кандалами» пал, бежал сквайр — прежде вопрос закрывался по числу бежавших).</summary>
    public IReadOnlyList<string> RetreatedUnits { get; init; } = [];
}

/// <summary>Что случилось на карте без хода по клеткам.</summary>
public enum TurnSightKind
{
    /// <summary>Укрепление взято (<see cref="GameSession.Enter"/>): состояние уже новое.</summary>
    Capture,

    /// <summary>Нанят герой (<see cref="GameSession.HireLeader"/>): отряд уже в укреплении.</summary>
    Hire,

    /// <summary>Брошено заклинание (<see cref="GameSession.CastSpell"/>): оплачено, эффект ещё не применён.</summary>
    Spell,

    /// <summary>Отряд погиб в бою (<see cref="GameSession.FinishBattle"/>, <c>CEffectKillStack</c>): состояние ещё прежнее — отряд на
    /// карте; игрок — хозяин отряда, объект — отряд.</summary>
    Death,

    /// <summary>Отряд создан событием <c>L_CREATE_STACK</c> (<c>CEffectCreateStack</c>): состояние уже новое; объект — отряд.</summary>
    Created,

    /// <summary>Отряд сменил владельца по событию <c>L_CHANGE_OWNER</c> (<c>CEffectChangeOwner</c>): состояние уже новое; игрок — новый
    /// владелец, объект — отряд.</summary>
    OwnerChanged,

    /// <summary>Действия игрока в окнах — для звука интерфейса (<c>docs/ai-screen.md</c> §12): постройка заказана (объект — постройка),
    /// куплено (предмет у торговца, заклинание у мага, воин в укреплении или у наёмников; объект — купленное), вылечен и воскрешён
    /// в городе (объект — воин), заклинание исследовано, дань отправлена (объект — получатель). Модель не меняют — только сообщают.</summary>
    Built,
    Bought,
    Healed,
    Revived,
    Researched,
    GoldGiven,

    /// <summary>Уведомления окон человека без звука — звук ставит показ (сессия Г, <c>docs/ai-screen.md</c> §11); модель не меняют. Зелье
    /// выпито вне боя в окне отряда (объект — тип предмета: лечебное, воскрешение, усиление — по категории; <c>PHEAL</c>/<c>PREVIVE</c>/
    /// <c>PBOOST</c>/<c>USEITEM</c>); союз заключён, отвергнут, разорван (объект — другой игрок; <c>ALLIANCE</c>/<c>REFALLIA</c>/<c>BRKALLIA</c>);
    /// сделка прошла или отвергнута (объект — тип предмета или заклинание; <c>TRADITEM</c>/<c>TRADSPEL</c>/<c>REFTRITM</c>/<c>REFTRSPE</c>);
    /// итог вора открыт (<c>DLG_ACTION_RESULT</c>, объект — действие; <c>BKPOPUP</c>). Игрок — человек у окна.</summary>
    PotionDrunk,
    AllianceMade,
    AllianceRefused,
    AllianceBroken,
    Traded,
    TradeRefused,
    ThiefResult,

    /// <summary>Деревня выросла на уровень (<see cref="GameSession.GrowCity"/>, звук <c>CITYGROW</c>): состояние уже новое; объект —
    /// деревня.</summary>
    CityGrown,

    /// <summary>Своя земля растеклась в начале хода игрока (<c>CMidAnim2Terrain</c>, звук <c>OCCUPY</c>): модель уже посчитала новую землю,
    /// но состояние партии ещё прежнее (зовётся из начала хода до записи); клетки — <see cref="TurnSight.Cells"/>, по источникам в порядке
    /// обхода — <see cref="TurnSight.Groups"/>; клетка события — точка показа движка (один источник — его место, несколько — столица,
    /// <c>0x5e9450</c>).</summary>
    LandSpread,

    /// <summary>Руина разграблена (<see cref="GameSession.Enter"/>, анимация и звук <c>LOOTRUIN</c>): состояние уже новое; объект —
    /// руина, игрок — грабитель, <see cref="TurnSight.Bank"/> — золото руины (окно «Вы победили!» грабителю, <c>CCmdLootRuinMsg</c>).</summary>
    Looted,

    /// <summary>Бой начинается (<c>CCmdBattleStartMsg</c>, показ — <c>CMidAnim2Battle</c>): игрок — хозяин нападающего, объект —
    /// нападающий отряд, клетка — цель: отряд — его клетка, укрепление или руина — клетка входа (позиция + размер − 1). Бои ИИ, человека
    /// и событий; модель не меняет — только сообщает.</summary>
    BattleStarted,

    /// <summary>Вор ИИ подошёл к цели и выбирает действие (<c>CEffectNobleChoose</c>, сообщение 12 <c>CCmdNobleActionMsg</c>; показ —
    /// <c>ANOBLE</c>/<c>SNOBLE</c> между клеткой вора и цели, <c>0x4f1606</c>): список действий не пуст; игрок — хозяин вора, объект —
    /// отряд вора, клетка — цель (отряд — его клетка, крепость и площадка — клетка входа). Модель не меняет — только сообщает.</summary>
    NobleAction,

    /// <summary>Игроку пришли предметы (сообщение 44 <c>CCmdGiveItemMsg</c>, окно <c>DLG_ITEM</c> с <c>TAKEBAG</c>, <c>docs/ui-windows.md</c>
    /// §14): подбор мешка (<c>CEffectTakeBag</c> <c>0x43eb11</c> — и пустого: «Ничего не найдено»), трофей руины (<c>CEffectLootRuin</c>
    /// <c>0x439063</c>, только с трофеем), кража у торговца (<c>CEffectGiveItem</c> <c>0x438616</c> из <c>0x41df26</c>); события
    /// <c>L_GIVE_ITEM</c> — сессия В. Кража предмета из отряда идёт обменом (<c>CEffectExchange</c>) — сообщения нет. Состояние уже новое;
    /// игрок — получатель (не нейтрал), клетка — место получения, объект — откуда (мешок, руина, торговец), <see cref="TurnSight.Ids"/> —
    /// экземпляры предметов.</summary>
    ItemsGiven,

    /// <summary>Игроку пришли заклинания (сообщение 43 <c>CCmdGiveSpellMsg</c>, окно <c>DLG_ITEM</c>): кража заклинания (<c>0x41d7e0</c>),
    /// кража у мага (<c>0x41dfd6</c>); события <c>L_GIVE_SPELL</c> — сессия В. Состояние уже новое; игрок — получатель, объект — откуда,
    /// <see cref="TurnSight.Ids"/> — заклинания, <see cref="TurnSight.Library"/> — «из библиотеки столицы» (флаг сообщения <c>+0x2c</c>:
    /// заголовок <c>X005TA0774</c> вместо <c>X005TA0773</c>).</summary>
    SpellsGiven,

    /// <summary>Горы снесены событием <c>L_REMOVE_MOUNTAIN</c> (сообщение <c>0x478346</c> из <c>CEffectRemoveMountain</c> <c>0x439a3d</c>;
    /// показ — <c>RMVMOUNTAIN{3|5|7}</c>, слой <c>MOUNTAIN_REMOVE</c>): состояние уже новое (клетки — равнина, объекты гор сняты и
    /// переложены); игрок — кандидат события, клетка — центр локации, объект — локация (<c>GameState.Events.Zones</c>: радиус R, ширина
    /// зоны 2R + 1 — у движка размер картинки: ширина 1–3 → 3, 4–5 → 5, больше → 7, <c>0x5b82f5</c>), <see cref="TurnSight.Cells"/> —
    /// бывшие горные клетки зоны (x снаружи, y внутри, как <c>0x439a77</c>). Гор в зоне не было или не снят и не переложен ни один
    /// объект — события нет (движок сообщения не шлёт).</summary>
    MountainsRemoved,

    /// <summary>Попап события лёг человеку у ПК (<c>CEffectPopup</c> <c>0x43b290</c> → <c>CCmdPopupMsg</c>): сообщение уже в
    /// <see cref="GameState.Messages"/>; игрок — получатель, объект — событие, клетки нет (−1, −1). Клиент показывает попап сразу, и в
    /// ходе ИИ (<c>0x489e90</c>, проверки хода нет), очередь команд клиента стоит до «ОК» (<c>0x4bd98a</c>): ход ИИ на экране ждёт
    /// закрытия окна (сцена — <c>MapScreen.ShowAiPopups</c>). В действие человека попап показывает очередь окон после действия.</summary>
    EventPopup,

    /// <summary>Шпион раскрыт в начале хода его хозяина (<c>CEffectSpy</c> с флагом 0, <c>0x43e8c0</c> → <c>CCmdDiscoverSpyMsg</c> всем):
    /// сообщение тому, к кому он был заслан (клиент <c>0x489f16</c>: <c>X005TA0536</c> «Обнаружен шпион, которого к Вам заслал %PLAYER%…»).
    /// Игрок — разведанный, объект — хозяин шпиона, клетки нет. Модель не меняет — только сообщает; ход ИИ на экране ждёт окна, как у попапа.</summary>
    SpyCaught,

    /// <summary>То же раскрытие — хозяину шпиона (<c>0x489f16</c>: <c>X005TA0605</c> «%PLAYER% … шлет Вам голову Вашего шпиона…»). Игрок —
    /// хозяин шпиона, объект — разведанный.</summary>
    SpyLost,

    /// <summary>Убит отряд с лидером-иллюзией (<c>CEffectKillStack</c> <c>0x43895c</c>: лидер — <c>IUsIllusion</c> <c>0x438ff3</c>, убийца —
    /// отряд не нейтрала, — вместо <c>CCmdStackDestroyedMsg</c> всем <c>CCmdStackIllusionMsg</c>(убийца, иллюзия)); клиент хозяина иллюзии
    /// показывает <c>X005TA0408</c> «%KILLER% побеждает, %ILLUSION% гибнет.» (<c>CTaskIllusion</c> <c>0x4ce7bd</c>). Состояние ещё
    /// прежнее; игрок — хозяин иллюзии, объект — её отряд, <see cref="TurnSight.Ids"/> — собственное имя и тип лидера убийцы, затем
    /// иллюзии (имя пусто — подпись по типу, <c>0x5e97dc</c>).</summary>
    IllusionKilled,

    /// <summary>Взята столица (<c>CEffectCaptureCapital</c> <c>0x43496a</c> → <c>0x434d61</c>): казна погибшего — захватчику, захватчику
    /// <c>CCmdCaptureResourcesMsg</c> (окно <c>CCaptureResourcesInterf</c> на разметке <c>DLG_BEGIN_TURN</c>, <c>0x4929ae</c>). Состояние уже
    /// новое; игрок — захватчик, объект — погибший, <see cref="TurnSight.Bank"/> — его казна до захвата.</summary>
    ResourcesCaptured,

    /// <summary>Захват деревни не состоялся (<c>CEffectCapture</c> <c>0x433e2e</c>, флаг <c>+0x14</c> = 0, лидер — призванный
    /// <c>IUsSummon</c> или вор <c>IUsNoble</c>): <c>CCmdOccupyCityMsg</c> с флагом 0 (<c>0x433f2c</c>) — клиент хозяина отряда показывает
    /// <c>X005TA0458</c> «Призванные существа и воры не могут захватывать города.» (<c>0x48b415</c> → <c>0x4ced8e</c>). Состояние прежнее;
    /// игрок — хозяин отряда, клетка — деревня, объект — деревня.</summary>
    CaptureRefused,

    /// <summary>Сообщение ИИ человеку (<c>GameSession.Chat</c>, <c>0x4733a4</c> → <c>CCmdChatMsg</c>): клиент адресата открывает
    /// <c>DLG_CHAT_AI</c> (<c>CTaskOpenInterf&lt;CAIChatInterf&gt;</c> <c>0x48c3d1</c> → <c>0x4b9747</c>, звук <c>AICHAT</c>) — и в ходе ИИ,
    /// поверх панели ожидания; ход ИИ на экране ждёт «Закрыть», как у попапа. Корпус: 159 сообщений человеку — 159 окон (ненависть дня 1,
    /// предложение и ответы о союзе, сделки, дань, разрыв). Игрок — адресат, объект — отправитель, клетки нет (−1, −1),
    /// <see cref="TurnSight.Text"/> — строка <c>TAiMsg</c>.</summary>
    AiChat,

    /// <summary>Действия человека в окнах без звука и показа — для зонда порта (синтетические карты 6–9, <c>docs/synthetic-maps-port</c>):
    /// воин отпущен (<see cref="GameSession.DismissUnit"/>; объект — воин, отпуск лидера распускает отряд), мешок выброшен на пустую клетку
    /// или открыт броском на мешок (<see cref="GameSession.DropBag"/>; объект — мешок), предмет продан (<see cref="GameSession.SellItem"/>;
    /// объект — тип предмета), воин обучен (<see cref="GameSession.Train"/>; объект — воин). Игрок — хозяин отряда; модель уже новая.
    /// Клетка — у мешка и продажи (мешок, лавка); у отпуска и обучения клетки нет — 0, 0, как у прочих действий окон.</summary>
    Dismissed,
    BagDropped,
    Sold,
    Trained,
}

/// <summary>Событие для показа: вид, игрок, клетка (у заклинания — центр области), объект (укрепление, отряд, заклинание).
/// <see cref="Tomb"/> — у гибели: на клетке после боя стоит могила (<c>GameSession.Tombs.cs</c>), показ ставит её на середине
/// анимации гибели, как движок (<c>CMidAnim2Dying</c> <c>0x4f29c9</c>).</summary>
/// <para><see cref="Cells"/> — у растекания земли: клетки, сменившие землю; <see cref="Groups"/> — они же по источникам (крепость, жезл) в
/// порядке обхода (<c>LandModel.Walks</c>, без квадратов жил); у прочих — пусто.</para>
public sealed record TurnSight(TurnSightKind Kind, string PlayerId, int X, int Y, string Id, bool Tomb = false)
{
    public IReadOnlyList<(int X, int Y)> Cells { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<(int X, int Y)>> Groups { get; init; } = [];

    /// <summary>У полученных предметов и заклинаний (<see cref="TurnSightKind.ItemsGiven"/>, <see cref="TurnSightKind.SpellsGiven"/>) —
    /// что пришло: экземпляры предметов или заклинания; у прочих — пусто.</summary>
    public IReadOnlyList<string> Ids { get; init; } = [];

    /// <summary>У полученных заклинаний — «из библиотеки столицы» (взятие столицы); у прочих — ложь.</summary>
    public bool Library { get; init; }

    /// <summary>У взятых ресурсов (<see cref="TurnSightKind.ResourcesCaptured"/>) — что взято; у разграбленной руины
    /// (<see cref="TurnSightKind.Looted"/>) — что найдено; у прочих — ноль.</summary>
    public Bank Bank { get; init; } = Bank.Zero;

    /// <summary>У захвата (<see cref="TurnSightKind.Capture"/>) — сервер разослал <c>CCmdOccupyCityMsg</c> с флагом 1 (звук <c>OCCUPY</c>,
    /// клиент <c>0x48b415</c>): у деревни — только если расы нового хозяина нет среди прежних (<see cref="MapSite.PreviousOwners"/>,
    /// <c>0x434080</c>). Столица — как прежде, да.</summary>
    public bool Occupy { get; init; } = true;

    /// <summary>У сообщения ИИ (<see cref="TurnSightKind.AiChat"/>) — текст сообщения; у прочих — пусто.</summary>
    public string Text { get; init; } = "";
}

/// <summary>Шаг отряда по карте: клетки от старта до конца включительно (соседние по Чебышёву); у перестановки без пути —
/// старт и конец. <see cref="Attack"/> — ход кончается нападением (флаг команды хода <c>CCmdMoveStackMsg+0x2c</c>: показ даёт
/// <c>{тип}ATTK</c> на последнем шаге, прочие шаги молчат, <c>0x4f6dc3</c>).</summary>
public sealed record StackMove(string StackId, string Owner, IReadOnlyList<(int X, int Y)> Cells, bool Attack = false);

/// <summary>Основной наблюдатель и подписчики (<see cref="GameSession.Watch"/>) разом: ход, шаг и событие — всем, бой — только основному
/// (журнал боя один; подписчику <see cref="ITurnObserver.Battle"/> сообщается, что бой собран, его ответ не берётся).</summary>
public sealed class TurnWatchers(ITurnObserver? main, IReadOnlyList<ITurnObserver> watchers) : ITurnObserver
{
    public void PlayerTurn(string playerId)
    {
        main?.PlayerTurn(playerId);
        foreach (var w in watchers) w.PlayerTurn(playerId);
    }

    public void StackMoving(StackMove move)
    {
        main?.StackMoving(move);
        foreach (var w in watchers) w.StackMoving(move);
    }

    public IReadOnlyList<TurnRecord>? Battle(BattleState state) => DecideBattle(state)?.Log;

    public BattleDecision? DecideBattle(BattleState state)
    {
        foreach (var w in watchers) w.Battle(state);
        return main?.DecideBattle(state);
    }

    /// <summary>Подписчикам — раньше основного: основной в ходе ИИ ждёт показа события (окно сообщения и попап сцена берёт у сборщика
    /// окон, <c>NoticeWatch</c>), и сборщик должен получить событие до этого ожидания.</summary>
    public void Happened(TurnSight sight)
    {
        foreach (var w in watchers) w.Happened(sight);
        main?.Happened(sight);
    }

    /// <summary>Подписчикам — сведения (раньше ответа), ответ человека даёт окно основного наблюдателя (сцена).</summary>
    public void Offered(AllianceOffer offer)
    {
        foreach (var w in watchers) w.Offered(offer);
        main?.Offered(offer);
    }

    public void HumanTurn(string playerId)
    {
        main?.HumanTurn(playerId);
        foreach (var w in watchers) w.HumanTurn(playerId);
    }

    public void BattleEnded(BattleOutcome outcome)
    {
        main?.BattleEnded(outcome);
        foreach (var w in watchers) w.BattleEnded(outcome);
    }

    public void EventFired(EventFiring firing)
    {
        main?.EventFired(firing);
        foreach (var w in watchers) w.EventFired(firing);
    }
}
