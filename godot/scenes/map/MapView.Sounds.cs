using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Gfx;
using LastEdict.Data.Play;
using LastEdict.Data.Sound;
using LastEdict.Rules.Map;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Звуки отрядов на карте (<c>docs/ai-screen.md</c> §12, ранг 1 — <c>CMidAnim2Unit</c> <c>0x4f6e78</c>, <c>CTaskSelectUnit</c>
/// <c>0x4cf8fa</c>): в начале каждого шага — шаг лидера (<c>walk</c> из <c>SNDINFO</c>, вариант списка; прежний шаг глушится,
/// <c>0x4f6f75</c>), на воде у лидера без водного бонуса — <c>BOATSND</c>; первый шаг из укрепления — ещё <c>EXITCITY</c> (громкость
/// 100). Ход, кончающийся нападением, — <c>{тип}ATTK</c> на последнем шаге, прочие шаги такого хода молчат. В конце хода шаги
/// глушатся. Выбор своего отряда с живым лидером — <c>{тип}ACK{1|2|3}</c> (<c>rand(100)</c>: &lt; 25 — 1, &lt; 50 — 2, иначе 3); пока
/// отряд выбран — <c>{тип}IDLE1/2</c> раз в 20 + rand(20) с (<c>0x4d1d51</c>). Все звуки юнита (шаг, лодка, нападение, гибель, голоса)
/// — громкостью <c>Acknoledgements</c> (<c>0x5b0472</c>).
/// </summary>
public partial class MapView
{
    private const string Archive = "Midgard", StepTag = "шаг";

    private readonly Random _soundRandom = new();

    /// <summary>Звук шага отряда в клетку (<paramref name="toX"/>, <paramref name="toY"/>); <paramref name="first"/> — первый шаг хода,
    /// <paramref name="attackStep"/> — ход кончается нападением: null — нет, true — это его последний шаг, false — прочий шаг.</summary>
    private void StepSound(string stackId, int toX, int toY, bool first, bool? attackStep = null)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return;
        var audio = GameAudio.Of(this);
        audio.StopFx(StepTag);
        if (first && stack.Inside.Length >= 6 && stack.Inside.Substring(4, 2).Equals("FT", StringComparison.OrdinalIgnoreCase))
            audio.PlayFx(Archive, "EXITCITY", tag: StepTag);
        string type = LeaderType(stack);
        if (attackStep is { } last)
        {
            if (last) audio.PlayVoice(Archive, $"{type}ATTK", StepTag);
            return;
        }
        bool boat = Map.Contains(toX, toY) && Map.At(toX, toY).Ground == Ground.Water && !Options(stack).WaterBonus;
        string? name = boat ? "BOATSND"
            : GameSound.Units.Of(type) is { } info ? GameSound.Units.Pick(info.Walk, _soundRandom.Next) : null;
        if (name is not null) audio.PlayVoice(Archive, name, StepTag);   // шаги — громкостью голосов, как у движка
    }

    /// <summary>Ход кончился — шаги замолкают.</summary>
    private void StepsDone() => GameAudio.Of(this).StopFx(StepTag);

    /// <summary>Голос лидера выбранного отряда; лидер мёртв — движок просит <c>{id}ACKN</c>, которого нет в архиве: тишина.</summary>
    private void SelectVoice(MapStack stack)
    {
        _idleLeft = NextIdle();
        if (!stack.LeaderAlive) return;
        int roll = _soundRandom.Next(100);
        int n = roll < 25 ? 1 : roll < 50 ? 2 : 3;
        GameAudio.Of(this).PlayVoice(Archive, $"{LeaderType(stack)}ACK{n}");
    }

    /// <summary>До следующего «скучающего» голоса выбранного отряда, с.</summary>
    private double _idleLeft = double.PositiveInfinity;

    private const double IdleBase = 20, IdleSpread = 20;

    private double NextIdle() => IdleBase + (_soundRandom.NextDouble() * IdleSpread);

    /// <summary>Выбранный свой отряд с живым лидером раз в 20 + rand(20) с говорит <c>{тип}IDLE1</c> или <c>IDLE2</c> (поровну). Под окном
    /// (окно поверх карты, столица, бой — то же условие, что глушит фон мест, <see cref="RegionsPaused"/>) — молчит: журнал зонда 7.3
    /// (<c>corpus/20261016e-av</c>, <c>-g</c>) — 9 голосов скуки, все при верхнем <c>DLG_ISO</c>; у порта до 17.10 — 22 из 40 под окнами
    /// (лавки, книга, сообщения). Таймер движка (<c>0x4d1d51</c>) проверяет состояние приложения (<c>0x5613d6</c>) и флаг задачи выбора
    /// (<c>0x406020</c>) — что второй и есть «карта сверху», ранг 2 (по журналу).</summary>
    private void StepIdle(double delta)
    {
        if ((_idleLeft -= delta) > 0) return;
        _idleLeft = NextIdle();
        if (RegionsPaused || _selected is null || Walking || !Map.Stacks.TryGetValue(_selected, out var stack) || !stack.LeaderAlive) return;
        GameAudio.Of(this).PlayVoice(Archive, $"{LeaderType(stack)}IDLE{(_soundRandom.Next(100) < 50 ? 1 : 2)}");
    }

    /// <summary>Итог входа своего отряда (<see cref="GameSession.Enter"/>): в своё укрепление — <c>ENTRCITY</c>; разграбленная руина —
    /// <c>LOOTRUIN</c> (анимация карты); <c>ENTRRUIN</c> — у окна «Вы победили!» (<c>MapScreen.Notices.cs</c>). Захват — <c>OCCUPY</c> по
    /// уведомлению модели.</summary>
    private void VisitSound(bool entered, bool looted)
    {
        if (entered) GameAudio.Of(this).PlayFx(Archive, "ENTRCITY");
        if (looted) LootSound();
    }

    /// <summary>Руина разграблена своим отрядом: <c>LOOTRUIN</c> (анимация карты <c>CMidAnim2LootRuin</c>). <c>ENTRRUIN</c> звучит окном
    /// «Вы победили!» (<c>0x4cea43</c> → <c>0x5b03ce(3)</c>, журнал 20261016g seq 564439 → 564459), а не здесь.</summary>
    private void LootSound() => GameAudio.Of(this).PlayFx(Archive, "LOOTRUIN");

    /// <summary>Щелчок выбранным отрядом туда, куда пути нет: <c>BEEP</c> (пересчёт пути по щелчку <c>0x4ccaff</c>: построение <c>0x4cca01</c>
    /// не нашло пути — <c>0x5b03ce</c> с номером 11 таблицы служебных звуков <c>[0x7ae494]</c>, громкость 100). Журнал
    /// <c>20261017-port-synth</c>, карта 10: 14 щелчков с <c>map_click ret 0</c> — 14 <c>BEEP</c> (VA1/VA2: «не даёт встать в клетку»); у порта
    /// щелчок был молчаливым.</summary>
    private void NoPathSound() => GameAudio.Of(this).PlayFx(Archive, "BEEP");

    /// <summary>Начало боя на виду (<c>CMidAnim2Battle</c> <c>0x4f14ae</c>): <c>ABATTLE</c> (IsoCmon, 17 кадров, с кадра 0) по центру
    /// клетки цели поверх объектов и звук <c>SBATTLE</c>; ход ждёт конца. Лидер нападающего — вор (<c>0x4f178b</c> → <c>0x41b399</c>,
    /// дуэль) — <c>ANOBLE</c> и <c>SNOBLE</c> (ранг 3 — что дуэль идёт этой анимацией). ponytail: слой движка — <c>SYM_STACK_BATTLE</c>
    /// (под туманом), у порта — слой эффектов поверх всего.</summary>
    public bool PlayBattleStart(int x, int y, float speed = 1f, string? attacker = null)
    {
        bool noble = attacker is not null && IsNoble(attacker);
        if (GameData.Content.Gfx.Anim("IsoCmon", noble ? "ANOBLE" : "ABATTLE") is not { } anim) return false;
        if (!PlayEffect([(anim, true)], x, y, speed, [(Archive, noble ? "SNOBLE" : "SBATTLE", false)], null, cls: "CMidAnim2Battle")) return false;
        GameLog.Verbose($"battlestart в=({x},{y}) кадров={_spellLength}{(noble ? " знатный" : "")}");
        return true;
    }

    /// <summary>Вор подошёл к цели (<c>CEffectNobleChoose</c>, сообщение 12; ранг 1): <c>ANOBLE</c> и <c>SNOBLE</c> посередине между
    /// клеткой вора и клеткой цели (<c>0x4f1606</c>), до окна действий вора; ход ждёт конца.</summary>
    public bool PlayNoble(string thief, int x, int y, float speed = 1f)
    {
        if (!Map.Stacks.TryGetValue(thief, out var stack) || GameData.Content.Gfx.Anim("IsoCmon", "ANOBLE") is not { } anim) return false;
        var at = (CellCentre(stack.X, stack.Y) + CellCentre(x, y)) / 2f;
        if (!PlayEffect([(anim, true)], x, y, speed, [(Archive, "SNOBLE", false)], null, at, cls: "CMidAnim2Battle", obj: thief)) return false;
        GameLog.Verbose($"noble {thief} вор=({stack.X},{stack.Y}) цель=({x},{y}) кадров={_spellLength}");
        return true;
    }

    /// <summary>Лидер отряда — вор (категория юнита «знатный», <c>0x41b399</c>).</summary>
    private bool IsNoble(string stackId) =>
        Map.Stacks.TryGetValue(stackId, out var stack) && Map.Units.TryGetValue(stack.LeaderId, out var leader) && leader.Hp > 0
        && Session.Book?.CategoryOf(leader.TypeId) == LastEdict.Rules.Battle.UnitCategory.Noble;

    private static Vector2 CellCentre(int x, int y)
    {
        var (ox, oy) = GroundGeometry.CellOrigin(x, y);
        return new Vector2(ox + (GroundGeometry.CellWidth / 2f), oy + (GroundGeometry.CellHeight / 2f));
    }

    /// <summary>Сообщения боя, которые модель дописала концом боя (<see cref="GameSession.FinishBattle"/>: уход призванных, откат
    /// превращений, рост по опыту) начиная с <paramref name="heard"/>, — звуком по очереди (<see cref="BattleNoteSounds"/>; у движка —
    /// шаги 13–16 просмотра боя). Раса роста — владельца отряда юнита после боя.</summary>
    public void PlayBattleNotes(LastEdict.Rules.Battle.BattleState state, int heard)
    {
        ArgumentNullException.ThrowIfNull(state);
        var audio = GameAudio.Of(this);
        double at = 0;
        for (int i = heard; i < state.Notes.Count; i++)
        {
            var note = state.Notes[i];
            var owner = Map.Stacks.Values.FirstOrDefault(s => s.UnitIds.Contains(note.UnitId, StringComparer.OrdinalIgnoreCase))?.Owner;
            string name = BattleNoteSounds.Of(note.Kind, owner is null ? -1 : RaceTypeOf(owner));
            GameLog.Verbose($"battlenote {note.UnitId} {note.Kind} звук={name}");
            if (at <= 0) audio.PlayFx(BattleNoteSounds.Archive, name);
            else GetTree().CreateTimer(at).Timeout += () =>
            {
                if (IsInstanceValid(this) && IsInsideTree()) audio.PlayFx(BattleNoteSounds.Archive, name);
            };
            at += GameSound.Load(BattleNoteSounds.Archive, name).Seconds;   // следующий шаг — после звука (у движка шаги идут по очереди)
        }
    }

    /// <summary>Нападение человека ждёт конца анимации начала боя (<see cref="PlayBattleStart"/>) или вора у цели (<see cref="PlayNoble"/>):
    /// экран боя или окно вора — после неё; <c>Visit</c> — вор у площадки (окно площадки решает хозяин).</summary>
    private (string Stack, string Target, bool Visit)? _pendingAttack;

    private void StepPendingAttack()
    {
        if (_pendingAttack is not { } attack || SpellPlaying) return;
        _pendingAttack = null;
        if (attack.Visit) SiteVisited?.Invoke(attack.Stack, attack.Target);
        else AttackRequested?.Invoke(attack.Stack, attack.Target);
    }

    /// <summary>Скорость эффектов человека: в проверке без окна — как у хода ИИ в проверке.</summary>
    private static float EffectSpeed => System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--check") >= 0 ? CheckAiSpeed : 1f;

    // ------------------------------------------------------------------ фон мест

    /// <summary>Как часто пересчитывать звучащие источники фона, с.</summary>
    private const double RegionPeriod = 0.25;

    private IReadOnlyList<AudioSource>? _regionSources;
    private double _regionClock;

    /// <summary>Фон мест молчит: открыто окно, столица или бой (движок ставит его на паузу, <c>CAudioRegionMgr</c>).</summary>
    public bool RegionsPaused { get; set; }

    /// <summary>Сколько источников звучит сразу — настройка <c>AudioRegions</c> (0…4).</summary>
    public int RegionCount { get; set; } = AudioRegions.DefaultCount;

    /// <summary>Источники собираются один раз (при первом кадре карты), дальше — ближайшие к центру вида.</summary>
    private void StepRegions(double delta)
    {
        StepIdle(delta);
        if ((_regionClock -= delta) > 0) return;
        _regionClock = RegionPeriod;
        var audio = GameAudio.Of(this);
        if (RegionsPaused)
        {
            audio.SetRegions([]);
            return;
        }
        audio.SetRegions(RegionVoices());
    }

    /// <summary>Что звучит из фона мест при нынешнем центре вида.</summary>
    public IReadOnlyList<AudioVoice> RegionVoices()
    {
        _regionSources ??= AudioRegions.Sources(Looks, RaceTypeOf, GameData.Content.MapArt.LandmarkFootprint);
        var (x, y) = CentreCell;
        return AudioRegions.Nearest(_regionSources, x, y, RegionCount, Map.Size, Audible);
    }

    /// <summary>Источники в пределах слышимости, чьего звука в архиве нет (руины <c>RUIN</c>, наставник <c>TRAINCMP</c>): молчат и места не
    /// занимают (<see cref="AudioRegions"/>) — для проверок.</summary>
    public IReadOnlyList<AudioVoice> RegionMuted()
    {
        _regionSources ??= AudioRegions.Sources(Looks, RaceTypeOf, GameData.Content.MapArt.LandmarkFootprint);
        var (x, y) = CentreCell;
        return AudioRegions.Nearest(_regionSources, x, y, _regionSources.Count, Map.Size, name => !Audible(name));
    }

    /// <summary>Есть ли звук фона в описи архива <c>AudioRgn</c> (с модами).</summary>
    private static bool Audible(string name) => GameSound.Assets.Find(AudioRegions.Archive, name) is not null;

    /// <summary>Сколько на карте источников фона (собраны — после первого кадра).</summary>
    public int RegionSourceCount => _regionSources?.Count ?? 0;

    /// <summary>Карта уходит — фон замолкает.</summary>
    private void StopRegions()
    {
        if (IsInsideTree()) GameAudio.Of(this).SetRegions([]);
    }

    /// <summary>Тип расы владельца отряда юнита (рост по опыту <c>UPGRADE{раса}</c>: раса владельца группы, <c>0x652f1b</c>); нет — −1.</summary>
    public int UnitRaceType(string unitId) =>
        Map.Stacks.Values.FirstOrDefault(s => s.UnitIds.Contains(unitId, StringComparer.OrdinalIgnoreCase))?.Owner is { } owner ? RaceTypeOf(owner) : -1;

    private int RaceTypeOf(string playerId) =>
        Map.Players.TryGetValue(playerId, out var p) && Session.Book is { } book && book.Economy.Races.TryGetValue(p.RaceId, out var r)
            ? r.RaceType : -1;

    private string LeaderType(MapStack stack) =>
        Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId.ToUpperInvariant() : "";
}
