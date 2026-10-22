namespace LastEdict.Rules.Units;

/// <summary>Одно действие, убившее юнитов одной стороны: сумма их <c>XP_KILLED</c> (с ростом), кто в этот момент делит
/// опыт и сколько из них призваны в этом бою (<see cref="Experience.SummonStatusBit"/>) — они берут долю, но в делитель
/// не входят.</summary>
public sealed record Kill(int XpKilled, IReadOnlyList<string> Receivers, int Summoned = 0);

/// <summary>Состояние лидера, от которого зависит переход уровня: уровень (<c>MidUnit.LEVEL</c>),
/// опыт (<c>XP</c>) и очередь отложенных выборов навыка (<c>MidStack.UPGCOUNT</c>, план Ф7 §1.5 —
/// «отложенный выбор копится»). У солдата аналог — <see cref="UnitProgress"/>: у него вместо
/// очереди смена типа, поэтому запись отдельная, а не общая с флагом.</summary>
public sealed record LeaderProgress(int Level, int Xp, int PendingSkills);

/// <summary>
/// Опыт за бой (план Ф7 §1.3, задача 4). Накопление — <c>BattleMsgData::UnitInfo::unitXp</c> по ходу боя, применение —
/// <c>CBatAttackGroupUpgrade::upgradeGroup</c> (Gog <c>0x665217</c>); функция дележа дизассемблером не найдена
/// (levels §2.4), правило взято из замеров.
/// </summary>
public static class Experience
{
    /// <summary><c>LleadA.L_WEAPON_MASTER</c> = 6 — «Инструктор». Ранг 1 (таблица). Во всех 26 строках
    /// <c>GLabi.dbf</c> ванили врождённым его нет — приходит только цепочкой модификаторов.</summary>
    public const int WeaponMasterAbility = 6;

    /// <summary><c>LunitC.L_LEADER</c> = 2 (<c>Gunits.UNIT_CAT</c>). Ранг 1 — таблица.</summary>
    public const int LeaderCategory = 2;

    /// <summary>Бит 30 <c>BattleStatus::Hidden</c> — юнит в бою не участвует (дуэль вора: «Non-leader units in both
    /// groups are marked with BattleStatus::Hidden», <c>reference/D2ModdingToolset/mss32/include/battlemsgdata.h:73,318</c>,
    /// ранг 2 на имя). Ранг 1 по данным: первая большая партия, бой seq 375338 — у трёх защитников бит взведён весь
    /// бой, строк <c>xp</c> у них нет, а <c>XP_KILLED 25</c> убитого целиком ушло лидеру, единственному без бита.</summary>
    public const int HiddenStatusBit = 30;

    /// <summary>Бит 27 поля <c>st</c> юнита в журнале — призван в этом бою. Ранг 1 по корпусу (16/0, разбор 17.09):
    /// призванный получает долю опыта, но делитель его не считает — ai-q37 seq 316125: 5 на двоих и призыв, всем по 2;
    /// <c>20260909-arena1</c> seq 124407: 110 на двоих при трёх призывах, всем по 55.</summary>
    public const int SummonStatusBit = 27;

    /// <summary>Бит 0 поля <c>st</c> — <c>BattleStatus::XpCounted</c>: опыт за смерть этого юнита уже роздан.</summary>
    public const int XpCountedStatusBit = 0;

    /// <summary>Делит ли юнит опыт на момент смерти врага (входит ли в делитель): жив, участвует в бою
    /// (<see cref="HiddenStatusBit"/>) и не из категорий <c>LunitC</c> 1 <c>L_NOBLE</c>, 4 <c>L_SUMMON</c>, 5
    /// <c>L_ILLUSION</c> (ранг 2: <c>reference/D2ModdingToolset/mss32/src/unitutils.cpp:661</c>, levels §2.4).
    /// <para><b>Страж столицы (8 <c>L_GUARDIAN</c>) делит</b>, хотя опыта не получает (<see cref="GainsXp"/>). Ранг 1 по
    /// корпусу <c>20260919-human</c>: бои seq 15504360 и 15520354 у столицы Нежити — <c>xp_group</c> даёт стражу
    /// <c>S073UN0006</c> (G000UU3004) acc 18 и 8 наравне с четырьмя юнитами (90 / 5 и 40 / 5, а не / 4), applied 0.</para>
    /// <para><b>«Жив» здесь — «опыт за его смерть ещё не сосчитан»</b> (бит <see cref="XpCountedStatusBit"/> снят):
    /// юнит, умерший от яда в начале своего хода, лежит со <c>st</c> = 2 (Dead без XpCounted) до следующей раздачи и в
    /// ней ещё делит. Ранг 1 по корпусу: <c>20260919-human</c> бой seq 2868558 — S073UN01a2 умер от яда (turn_after
    /// 2868863, st 2), следом убит G000UU5131 (XP_KILLED 30): делителей 4, по 7, итог 5 + 7 = 12 у троих выживших
    /// (<c>xp_group</c> acc 12), а не 5 + 10 = 15.</para></summary>
    /// <param name="alive">Жив или мёртв, но с неснятым XpCounted к моменту действия.</param>
    public static bool Receives(int category, bool alive, bool hidden = false) =>
        alive && !hidden && category is not (1 or 4 or 5);

    /// <summary>Получает ли юнит опыт вообще — <c>canUnitGainXp</c> (<c>0x5d8f84</c>: не <c>L_NOBLE</c>, <c>L_SUMMON</c>,
    /// <c>L_ILLUSION</c>, <c>L_GUARDIAN</c>). В <c>upgradeGroup</c> через <c>IsUnitLevelNotMax</c> (<c>0x5d8f13</c>) даёт
    /// прибавку 0 (<c>0x6656E6</c>): строки <c>xp</c> нет, <c>battle_end.xp</c> 0. Ранг 1 по листингу.</summary>
    public static bool GainsXp(int category) => category is not (1 or 4 or 5 or 8);

    /// <summary>
    /// Накопленный за бой опыт: на <b>каждое действие</b>, убившее врагов, <c>⌊Σ XP_KILLED убитых им / число делящих в этот
    /// момент⌋</c> каждому, остаток пропадает; копится отдельно у каждого юнита.
    /// <para>Ранг 1 по корпусу: счётчик <c>xp_per_kill</c> сверяет это с полем <c>xp</c> в <c>battle_end</c> (там движок
    /// пишет <c>unitXp</c>). Близко к правилу <c>reference/Disciples2-RL/Big_map/battle_env.py:2134-2160</c> (ранг 2), но
    /// единица дележа — действие, а не смерть: во второй большой партии бой seq 571740 — отдельная смерть, затем два
    /// Оруженосца (20 + 20) одним действием на троих дали 19, а поштучное округление даёт 18; бои seq 235948 и 1820205 —
    /// то же +1. <i>Было иначе:</i> <c>⌊Σ XP_KILLED / получателей⌋</c> одним делением за весь бой (план Ф7 §1.3 по
    /// <c>f7-m9</c>, где убит один волк и формулы неразличимы) — на корпусе давало 14 вместо 15, 32 вместо 33 и т. п.</para>
    /// <para>Делитель — делящие без призванных в бою (<see cref="Kill.Summoned"/>); нет таких — действие опыта не даёт
    /// никому. <c>ponytail:</c> случай «одни призванные» в корпусе не встречался — ноль взят по аналогии с пустым
    /// списком.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, int> Accumulate(IEnumerable<Kill> kills)
    {
        var sum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kill in kills.Where(k => k.Receivers.Count - k.Summoned > 0))
        {
            int share = kill.XpKilled / (kill.Receivers.Count - kill.Summoned);
            foreach (var r in kill.Receivers) sum[r] = sum.GetValueOrDefault(r) + share;
        }
        return sum;
    }

    /// <summary>
    /// Переход уровня лидера — <b>циклом</b>, пока опыта хватает на следующий порог и пока не
    /// достигнут потолок сценария (<c>CScenarioInfo.leaderMaxLevel</c>, поле <c>MAX_LEADER</c>
    /// сохранения). План Ф7 §1.5, задача 11.
    ///
    /// <para><b>Что делает повышение.</b> Уровень + 1, опыт в ноль и <b>один отложенный выбор
    /// навыка</b> в очередь (<c>MidStack.UPGCOUNT</c>): сам уровень ни одного модификатора не
    /// накладывает (<c>0x5d89c6</c> только перевыпускает реализацию юнита на уровень выше —
    /// см. <see cref="LeaderUpgrades"/>), а окно навыка открывается на следующем ходу
    /// (<see cref="SkillChoice"/>). Статы растут не здесь, а в <see cref="UnitGrowth"/> по
    /// <c>GDynUpgr</c>.</para>
    ///
    /// <para><b>Ранг 1</b> — «излишек не переносится, опыт после повышения ноль» (то же, что у
    /// солдата в <see cref="UnitUpgrade.Apply"/> — задача 4 по корпусу) и «порог берётся у нового
    /// уровня» (<c>getUpgradeUnitImplCheckXp</c> <c>0x5D8B17</c>: <c>currentXp &lt; xpNext</c>).
    /// <b>Ранг 1 (код)</b> — лидер НА потолке (<c>уровень ≥ MAX_LEADER</c>) не повышается ни в одном пути:
    /// <c>0x5d89c6</c> отдаёт NULL (<c>0x5D8A48</c> <c>cmp; jge</c>), дневной путь <c>0x4318ad</c> повышения не зовёт.
    /// Опыт при этом: в бою прибавка обнуляется (<c>0x6656E6</c>, см. <see cref="BattleXp"/>, <c>atMaxLevel</c>) —
    /// строки <c>xp</c> нет, опыт не меняется; вне боя (дневной чит ИИ) опыт растёт по
    /// <see cref="LeaderXpAfter"/> с «тип не создан» и садится на <c>XP_NEXT</c> (на <c>XP_NEXT − 1</c> при ровной
    /// прибавке). Выше порога опыт не бывает никогда (<c>addXp</c> зажимает). <b>Ранг 1 живьём</b>
    /// (<c>20260917-synth</c> q40, <c>MAX_LEADER</c> = 2): лидер на потолке CAP получил 0 дважды (накоплено 5,
    /// seq 611479, 613727), опыт 648 без изменений; LOW 148/150 с уже созданным типом следующего уровня получил +2
    /// (излишек урезан), уровень 2, опыт 0, <c>UPGCOUNT</c> + 1, а во втором бою — уже 0 (на потолке).</para>
    /// </summary>
    /// <param name="xpNextOf">Порог по уровню — <c>XP_NEXT</c> с ростом <c>GDynUpgr</c>.</param>
    /// <param name="maxLevel">Потолок уровня лидера; 0 и меньше — потолка нет.</param>
    public static LeaderProgress LevelUpLeader(LeaderProgress leader, Func<int, int> xpNextOf, int maxLevel)
    {
        for (int guard = 0; guard < 100; guard++)
        {
            if (maxLevel > 0 && leader.Level >= maxLevel) return leader;
            int next = xpNextOf(leader.Level);
            if (next <= 0 || leader.Xp < next) return leader;
            leader = new LeaderProgress(leader.Level + 1, 0, leader.PendingSkills + 1);
        }
        throw new InvalidOperationException(
            $"повышение лидера не сошлось за 100 шагов с уровня {leader.Level} — порог не растёт");
    }

    /// <summary>Путь повышения юнита — от него зависит урезание опыта.</summary>
    public enum UpgradePath
    {
        /// <summary><c>UNIT_CAT</c> 2.</summary>
        Leader,
        /// <summary>Наследников по <c>PREV_ID</c> нет — дальше только уровни на месте.</summary>
        LastTier,
        /// <summary>Наследник есть и открыт: его <c>UPGRADE_B</c> построен, либо юнит на <c>DYNLEVEL</c>.</summary>
        SuccessorOpen,
        /// <summary>Наследник есть, ни одной его постройки у игрока нет.</summary>
        SuccessorBlocked,
    }

    /// <summary>Путь по категории (<see cref="LeaderCategory"/>), <c>DYNLEVEL</c>, числу наследников по <c>PREV_ID</c> и
    /// тому, построен ли <c>UPGRADE_B</c> хоть одного из них. Ранг 1 — как <see cref="UnitUpgrade.Apply"/>: при
    /// <c>DYNLEVEL</c> наследники не собираются (<c>0x61E6D9</c>).
    /// <para><b>Нейтральный владелец — всегда «последний тир».</b> <c>upgradeGroup</c> спрашивает
    /// <c>IsUnitTierMax</c> (<c>0x5d8d58</c>, вызов <c>0x6656B8</c>), а тот первым делом отвечает «да» для нейтрального
    /// игрока (<c>0x5e5358</c> — раса игрока <c>L_NEUTRAL</c>, <c>0x5D8D6F</c>–<c>0x5D8D76</c>); тогда прибавка не
    /// урезается (<c>0x6656ED</c> → <c>0x665762</c>), и опыт зажимает только <c>addXp</c> — в <c>XP_NEXT</c>, без «− 1»
    /// закрытого наследника. Ранг 1 по листингу и корпусу <c>20260918-probe65</c>: бой seq 7433250 — нейтральные
    /// <c>S143UN021b</c>/<c>021a</c> (G000UU0006, опыт 27, порог 70): <c>xp_group</c> acc 96 → applied 43, строки
    /// <c>xp</c> amount 96, 27 → 70, <c>battle_end.xp</c> 43; бой seq 5520297 — <c>S143UN01a5</c> 207 + 385 при пороге
    /// 475 → 268.</para></summary>
    /// <param name="neutralOwner">Владелец юнита — нейтральный игрок (раса <c>RACE_TYPE</c> 4).</param>
    public static UpgradePath PathOf(int category, bool dynLevel, int successors, bool successorBuilt,
                                     bool neutralOwner = false) =>
        category == LeaderCategory ? UpgradePath.Leader
        : successors == 0 || neutralOwner ? UpgradePath.LastTier
        : dynLevel || successorBuilt ? UpgradePath.SuccessorOpen : UpgradePath.SuccessorBlocked;

    /// <summary>
    /// Сколько опыта пишет строка <c>xp</c> (применение к выжившему победителю): накопленное × (100 + <c>WEAPN_MSTR</c>)
    /// / 100 при Инструкторе; у юнита с наследником урезано до <c>XP_NEXT − опыт</c>, у лидера и последнего тира не
    /// урезается (они берут уровни циклом).
    /// <para>Ранг 1 по корпусу — матрица «путь × кандидат» на выживших победителях с известным опытом до боя (первый
    /// прогон харнесса задачи 4): лидер «= накоплено» 208/6 (с опытом лидера до боя из сохранения — 216/1 против
    /// «min(·, порог − опыт)» 184/33, например герой 144/145 получил 12; урезаны до <c>XP_NEXT − опыт</c> два лидера,
    /// повысившихся в том же бою: seq 4946529 первой большой партии 149/150 → 1 и seq 254442 <c>20260909-arena2</c>
    /// 94/100 → 6 — причина открыта, план Ф7 §3 п. 6, задача 11), последний тир «= накоплено» 372/5, наследник открыт
    /// «min(·, порог − опыт)» 75/0, закрыт — то же 196/5; run1 §5 — 14/4/14 при опыте 66 (Archer с <c>DYNLEVEL</c> —
    /// путь «открыт»). <i>Было иначе:</i> «каждому урезано до <c>XP_NEXT − опыт</c>» (план Ф7 §1.3). Инструктор: ранг 1 на
    /// число 25 живьём (<c>20260917-synth</c> q18: 19 → 23 дважды, с книгой и без, без Инструктора 19 → 19; прогноз 3/3),
    /// ранг 3 на место умножения (сумма или каждое слагаемое) — карта этого не различает.</para>
    /// </summary>
    /// <param name="boost">Надбавки группы по игроку и сложности (<see cref="GroupAmount"/>); по умолчанию нет.</param>
    /// <param name="nextTierCreated">Только для лидера: создан ли уже в этом запуске игры тип его следующего уровня
    /// (<see cref="LeaderAmount"/>). По умолчанию нет — «= накоплено», как в матрице.</param>
    /// <param name="atMaxLevel">Только для лидера: уровень уже на <c>MAX_LEADER</c> — строки нет, 0.</param>
    public static int Amount(int accumulated, int xp, int xpNext, UpgradePath path, int weaponMasterPercent = 0,
                             XpBoost? boost = null, bool nextTierCreated = false, bool atMaxLevel = false)
    {
        if (path == UpgradePath.Leader && atMaxLevel) return 0;
        int boosted = GroupAmount(accumulated, weaponMasterPercent, boost ?? XpBoost.None);
        return path switch
        {
            UpgradePath.Leader => LeaderAmount(boosted, xp, xpNext, nextTierCreated),
            UpgradePath.LastTier => boosted,
            _ => Math.Clamp(xpNext - xp, 0, Math.Max(boosted, 0)),
        };
    }

    /// <summary>Накопленный в бою опыт, как его пишет поле <c>xp</c> в <c>battle_end</c> (фактически применённый,
    /// <c>0x66576B</c>–<c>0x6657EB</c>): до <c>XP_NEXT − опыт</c> у всех, у закрытого наследника — до
    /// <c>XP_NEXT − 1 − опыт</c> (опыт уже упёрся в потолок без постройки); у лидера — по <see cref="LeaderXpAfter"/>.
    /// Ранг 1 по той же матрице: лидер 209/5, последний тир 372/5, открыт 75/0, закрыт «порог − 1» 189/12 (против
    /// «порог» 167/34). Надбавки группы (<paramref name="boost"/>) входят до урезания — как в <c>upgradeGroup</c>.</summary>
    /// <param name="atMaxLevel">Лидер на <c>MAX_LEADER</c>: <c>IsUnitTierMax</c> истина, <c>IsUnitLevelNotMax</c> ложь →
    /// <c>amount = 0</c> (<c>0x6656B8</c>–<c>0x6656E6</c>), <c>battle_end.xp</c> 0.</param>
    public static int BattleXp(int accumulated, int xp, int xpNext, UpgradePath path, XpBoost? boost = null,
                               bool nextTierCreated = false, bool atMaxLevel = false)
    {
        if (path == UpgradePath.Leader && atMaxLevel) return 0;
        int boosted = GroupAmount(accumulated, 0, boost ?? XpBoost.None);
        if (path == UpgradePath.Leader)
            return LeaderXpAfter(xp, LeaderAmount(boosted, xp, xpNext, nextTierCreated), xpNext, nextTierCreated) - xp;
        return Math.Clamp(xpNext - (path == UpgradePath.SuccessorBlocked ? 1 : 0) - xp, 0, Math.Max(boosted, 0));
    }

    /// <summary>+20 % опыта группе человека на лёгкой сложности: <c>push 0x14</c> <c>0x66543E</c> в <c>upgradeGroup</c>
    /// (<c>0x665217</c>) при владельце группы — человеке (<c>0x62A1A0</c>) и <c>gameDifficulty</c> = лёгкая
    /// (<c>0x837988</c>, <c>0x665428</c>). Ранг 1 по листингу; корпус <c>20260915-passive</c> — <c>⌊acc·1,2⌋</c> 129/0 (T08).</summary>
    public const int HumanEasyXpPercent = 20;

    /// <summary>Порог «высокоуровневого» сценария для бонуса опыта ИИ: <c>CScenarioInfo +0x70</c>
    /// (<c>suggestedLevel</c>, в <c>.sg</c> — <c>SUGG_LVL</c>) <c>≥ 10</c> (<c>0x61EB31</c>: <c>cmp eax, 0xa; setge</c>).
    /// Ранг 1 по форме, ранг 2 по имени поля (тулсет <c>scenarioinfo.h</c>).</summary>
    public const int AiXpBonusMinSuggestedLevel = 10;

    /// <summary>
    /// Опыт группы до урезания — <c>upgradeGroup</c> (<c>0x665217</c>), ранг 1 по листингу: <c>acc</c> →
    /// <c>+acc·WEAPN_MSTR/100</c> (<c>0x66565E</c>) → <c>+·20/100</c> человеку на лёгкой (<c>0x66566D</c>) →
    /// <b>не меньше 1</b> (<c>0x665675</c>–<c>0x66567B</c>) → у ИИ играбельной расы (5-й аргумент, <c>0x66509B</c>:
    /// не человек и раса играбельна, <c>0x5E53A6</c>) бонус сложности <c>0x61EF7E</c>. Каждый шаг усекается делением.
    /// Корпус (T08): пол 1 — 7 строк ИИ с <c>acc 0</c>.
    /// </summary>
    public static int GroupAmount(int accumulated, int weaponMasterPercent, XpBoost boost)
    {
        int amount = accumulated + accumulated * weaponMasterPercent / 100;
        if (boost.HumanOnEasy) amount += amount * HumanEasyXpPercent / 100;
        if (amount <= 0) amount = 1;
        return amount * boost.AiPercent / 100;
    }

    /// <summary>
    /// Множитель опыта ИИ за бой, <c>0x61EF7E</c> (ранг 1 по листингу): при ненулевой прибавке и
    /// <see cref="AiXpBonusMinSuggestedLevel"/> — лёгкая без бонуса, средняя <c>×125/100</c> (<c>imul 0x7d</c>), трудная
    /// <c>×150/100</c> (<c>0x96</c>), очень трудная <c>×200/100</c> (<c>0xc8</c>). Имена категорий сложности — ранг 2
    /// (тулсет, Gog: <c>0x837988</c>/<c>0x8379b8</c>/<c>0x8379a8</c>/<c>0x837998</c>, id по <c>+8</c>). 100 — бонуса нет.
    /// </summary>
    public static int AiXpPercent(Map.Difficulty difficulty, int suggestedLevel) =>
        suggestedLevel < AiXpBonusMinSuggestedLevel ? 100 : difficulty switch
        {
            Map.Difficulty.Average => 125,
            Map.Difficulty.Hard => 150,
            Map.Difficulty.VeryHard => 200,
            _ => 100,
        };

    /// <summary>
    /// Дневной опыт юнита ИИ — чит <c>AiCheatUnitXP</c> (<c>0x473439</c>, применение <c>0x431c77</c> →
    /// <c>0x5d7412</c> с возвратом <c>0x431d07</c>, затем повышение <c>0x4318ad</c>): <c>⌊XP_NEXT·p/100⌋</c>, где
    /// <c>p</c> — <c>GAI.D_*_XP</c> по сложности (<see cref="Map.AiCheatRules.UnitXp"/>: 0/2/4/6). Ранг 1 по листингу:
    /// сначала отряды игрока с <c>p</c> (<c>0x4736D4</c>–<c>0x473721</c>), затем <c>p += p</c> (<c>0x473733</c>–<c>0x473739</c>)
    /// и все его <b>укрепления — столицы и деревни</b> (обход id типа 0x1d, <c>0x419F3D</c>; <c>0x47378B</c>–<c>0x4737CF</c>).
    /// Удваивается процент: 9995 на трудной — ⌊9995·8/100⌋ = 799, не 2·399. Условия (там же): игрок не выбыл
    /// (<c>0x5e4e73</c>), нейтралы при <c>SUGG_LVL ≥ 10</c> пропускаются (<c>0x61eb31</c>, <c>0x5e53a6</c>), группа
    /// берётся, только если все клетки 3×3 вокруг неё в тумане у каждого человека (<c>0x47387a</c>). Корпус
    /// <c>20260916-synth</c>: отряды — p, столицы — 2p, <b>деревни — 2p (373 строки)</b>, противоречий 0; пропущенные
    /// юниты ИИ лежат у объектов человека (2117 из 3444 ближе 9 клеток против 421 из 24 363 получивших).
    /// <para>ponytail: в дневной тик не встроено — хода ИИ в порте нет (<see cref="Map.MapSim"/> ведёт только
    /// отряды игрока, <c>StartDay</c>). Встраивать туда же, где появится ход ИИ: на каждого юнита ИИ в его ход (порядок
    /// относительно дохода не измерен; <see cref="Map.Treasury.AiResourceCheat"/> там же не встроен).</para>
    /// </summary>
    /// <param name="fortGarrison">Юнит стоит в гарнизоне укрепления владельца — столицы или деревни.</param>
    public static int AiDailyXp(int xpNext, int percent, bool fortGarrison) =>
        xpNext * (fortGarrison ? 2 * percent : percent) / 100;

    /// <summary>Строка <c>xp</c> лидера: если тип его следующего уровня <b>уже создан</b> в этом запуске
    /// (<c>IsUnitTierMax 0x5d8d58</c> ложь), прибавка, дотягивающая до порога, урезается до <c>XP_NEXT − опыт</c>
    /// (<c>0x6656EF</c>–<c>0x665739</c>); иначе идёт целиком. Ранг 1 по листингу, 7/7 наблюдений (<c>spec-leaders.md</c> п. 6).</summary>
    public static int LeaderAmount(int amount, int xp, int xpNext, bool nextTierCreated) =>
        nextTierCreated && amount > 0 && xp + amount >= xpNext ? Math.Max(xpNext - xp, 0) : amount;

    /// <summary>
    /// Опыт лидера после строки <c>xp</c> — <c>changeUnitXpCheckUpgrade 0x5d7412</c> и <c>addXp 0x5e9a7b</c>, ранг 1:
    /// если <c>опыт + прибавка ≥ XP_NEXT</c>, а тип следующего уровня ещё не создан (<c>0x5d89c6(…, 0)</c> = 0,
    /// <c>0x5D7483</c>), прибавка уменьшается на 1 (<c>dec [ebp+0x14]</c> <c>0x5D749E</c>); затем опыт зажимается в
    /// <c>[0, XP_NEXT]</c> (<c>0x5E9A7F</c>–<c>0x5E9AB6</c>). <b>Ловушка:</b> прибавка ровно до порога даёт
    /// <c>XP_NEXT − 1</c>, и повышения в этом бою нет (<see cref="LevelUpLeader"/> его не находит). Корпус
    /// <c>20260915-passive</c>: <c>xp</c> seq 9491685 — 578 + 72 при пороге 650 → 649, повышение только в бою seq 10016809.
    /// </summary>
    public static int LeaderXpAfter(int xp, int amount, int xpNext, bool nextTierCreated)
    {
        if (xp + amount >= xpNext && !nextTierCreated) amount--;
        return Math.Clamp(xp + amount, 0, Math.Max(xpNext, 0));
    }
}

/// <summary>Надбавки опыта группы по игроку и сложности (<see cref="Experience.GroupAmount"/>).</summary>
/// <param name="HumanOnEasy">Владелец группы — человек, сложность лёгкая.</param>
/// <param name="AiPercent">Множитель ИИ (<see cref="Experience.AiXpPercent"/>), 100 — нет.</param>
public sealed record XpBoost(bool HumanOnEasy, int AiPercent)
{
    public static readonly XpBoost None = new(false, 100);
}
