using LastEdict.Rules.Battle;
using LastEdict.Rules.Battle.Effects;

namespace LastEdict.Data.Sound;

/// <summary>Звук в плане: через <see cref="At"/> секунд от начала действия — звук <see cref="Name"/> архива <see cref="Archive"/> с долей
/// громкости <see cref="Volume"/>.</summary>
public sealed record SoundCue(double At, string Archive, string Name, float Volume = 1f);

/// <summary>
/// Звуки одного действия боя по записям хода (<c>docs/ai-screen.md</c> §12). Движок (<c>0x5c0be8</c>, ранг 1): замах A — на кадре
/// <c>attackFrame</c> анимации удара, удар по цели C — на кадре <c>hitFrame</c>, по каждой задетой цели; «больно» D — у цели с шансом 30 %
/// (<c>rand()%100 &gt;= 70</c>) и громкостью 70 %; гибель — <c>DEATH</c>. Промах звука не даёт (в <c>Battle.wdb</c> его нет). Вторая
/// атака звучит звуком своего класса, где он есть (<c>HEAL</c>, <c>POISON</c>, <c>FRSTBITE</c>, <c>BLISTER</c>, <c>SHATTER</c>). Кадр — 66 мс,
/// как у анимаций поля (<c>BattleView</c>); вторая атака — через <see cref="SecondDelay"/> после удара (ранг 3).
/// </summary>
public static class BattleSoundPlan
{
    public const string Archive = "Battle";
    public const double FrameSeconds = 0.066, SecondDelay = 0.3;
    public const int HurtChance = 30;
    public const float HurtVolume = 0.7f;

    /// <summary>План звуков действия: записи одного хода одного юнита (<paramref name="records"/>), юниты боя — по id
    /// (<paramref name="unitOf"/>), <paramref name="random"/> — <c>rand() % n</c>. <paramref name="actorType"/> — чьи звуки замаха и
    /// удара: у боевого предмета — юнит предмета (<c>GItem.UNIT_ID</c>, <c>0x647bac</c>), иначе тип ходящего. Пусто — ни записей, ни
    /// звуков у юнита. <paramref name="casterEffect"/> — у ходящего есть картинка эффекта <c>HEFF</c>: звук эффекта движок играет в её стадии
    /// (<c>0x63a30f</c> в <c>0x639f2b</c>), без картинки звука эффекта нет (призыв Повелителя стихий: один <c>SUMMON</c> — удар, журнал
    /// <c>20261016g-port-synth</c>).
    /// <para><paramref name="itemStep"/> — шаг боевого предмета или зелья (шаг 12, <c>0x6365f2</c>): замаха носителя нет, анимация предмета и
    /// <c>TUCH</c> его юнита — с начала шага, поэтому удар (<c>SNDINFO+0x5c</c>, стадия <c>TUCH</c>) звучит сразу вместе с замахом, а звука
    /// эффекта <c>HEFF</c> нет вовсе (журнал <c>20261017-port-synth</c>, BI2: <c>mage1</c> и <c>unit08a</c> в одну миллисекунду с <c>ITEM</c>;
    /// <c>20261016e-av</c>, зелья: <c>heal</c> + <c>unit161c</c>).</para>
    /// <para><paramref name="deathAfter"/> — когда звучит <c>DEATH</c>: движок играет его в конце удара цели <c>HHIT</c> (<c>0x6533fd</c> →
    /// <c>0x65363e</c>), а удар цели есть только при «дополнительной анимации боя» (<c>0x6517e1</c>: картинки <c>HHIT</c> грузятся при
    /// <c>battleAnimations</c> и не мгновенном бое). Функция даёт по id цели длину её удара, с; null у функции — <c>DEATH</c> нет (журналы с
    /// <c>BattleAnim</c> 0 — 0 на 51 смерть, с 1 — 11 на 11, в конце <c>HHIT</c>). Без функции — в миг удара.</para></summary>
    public static IReadOnlyList<SoundCue> Of(IReadOnlyList<TurnRecord> records, Func<string, BattleUnit?> unitOf,
                                             UnitSounds sounds, Func<int, int> random, string? actorType = null, bool casterEffect = true,
                                             bool itemStep = false, Func<string, double?>? deathAfter = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(unitOf);
        ArgumentNullException.ThrowIfNull(sounds);
        var cues = new List<SoundCue>();
        if (records.Count == 0 || unitOf(records[0].Unit) is not { } actor) return cues;
        var info = sounds.Of(actorType ?? actor.Type.Id);
        double hitAt = info is null || itemStep ? 0 : Math.Max(0, info.HitFrame - info.AttackFrame) * FrameSeconds;
        if (info is not null)
        {
            Add(0, sounds.Pick(info.Attack, random));
            if (casterEffect && !itemStep) Add(Math.Max(0, info.EffectFrame - info.AttackFrame) * FrameSeconds, sounds.Pick(info.Effect, random));
        }
        foreach (var record in records)
        {
            var target = unitOf(record.Target);
            if (record.Attack == 2)
            {
                // Звук второй атаки — облик её эффекта (лечение HEAL, яды, дробление SHATTER; у прочих нет).
                if (!record.Miss && actor.Attack2 is { Effect.Looks.Sound: { } classSound })
                    Add(hitAt + SecondDelay, classSound);
            }
            else if (!record.Miss)
            {
                if (info is not null) Add(hitAt, sounds.Pick(info.Hit, random));
                if (record.Damage > 0 && target is not null && sounds.Of(target.Type.Id) is { } hurt && random(100) >= 100 - HurtChance)
                    Add(hitAt, sounds.Pick(hurt.Hurt, random), HurtVolume);
            }
            if (record.Damage > 0 && record.TargetHpAfter <= 0)
            {
                double struck = record.Attack == 2 ? hitAt + SecondDelay : hitAt;
                if (deathAfter is null) Add(struck, "DEATH");
                else if (deathAfter(record.Target) is { } after) Add(struck + after, "DEATH");
            }
        }
        return cues;

        void Add(double at, string? name, float volume = 1f)
        {
            if (name is not null) cues.Add(new SoundCue(at, Archive, name, volume));
        }
    }

    /// <summary>Звук тика — облик эффекта (<see cref="EffectLooks.TickSound"/>): яд — <c>POISON</c>, мороз — <c>FRSTBITE</c>, ожог —
    /// <c>BLISTER</c> (сообщения боя 6/7/8, <c>0x635cb9</c>…, громкость 100); прочее — null.</summary>
    public static string? DotSound(AttackClass kind) => AttackEffects.Of(kind).Looks.TickSound;

    /// <summary>Тики подряд — одно действие: по звуку через <see cref="SecondDelay"/>.</summary>
    public static IReadOnlyList<SoundCue> OfTicks(IEnumerable<DotTick> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        return [.. ticks.Select(t => DotSound(t.Class)).OfType<string>().Select((name, i) => new SoundCue(i * SecondDelay, Archive, name))];
    }
}
