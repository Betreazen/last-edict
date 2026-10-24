using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Литьё заклинания на карте (<see cref="SpellMapAnim"/>, <c>docs/ai-screen.md</c> §6): картинки <c>IsoSpell</c> в центре клетки
/// литья, под объектами (<c>GROUND</c>, <c>UNDER</c>) или над ними (<c>FRONT</c>, <c>OVER</c>, <c>TOP</c>), один проход по 66 мс на кадр;
/// каждая снимается на своём последнем кадре. Звук — запись <c>Midgard</c> с именем id заклинания (<c>0x5b0986</c>); литьё
/// кончается, когда доиграли и картинки, и звук (<c>0x4f12d0</c>). ponytail: громкость — полная (у движка 100, настройка
/// <c>FxVolume</c> не перенесена).</summary>
public partial class MapView
{
    private const double SpellFrameSeconds = LastEdict.Data.Map.AnimClock.SlowMs / 1000.0;   // CMidAnim2Animation — медленная группа

    private Node2D? _spellUnder, _spellOver;
    private readonly List<(Sprite2D Sprite, Texture2D[] Frames)> _spellParts = new();
    private int _spellFrame = -1, _spellLength;
    private double _spellClock;
    private float _spellSpeed = 1f;

    private double _spellSoundLeft;

    /// <summary>Идёт литьё: картинки, звук или тряска экрана ещё не доиграли, вид ещё едет к месту показа (<see cref="Scrolling"/>) или туман меняется кольцами (<see cref="FogChanging"/>).</summary>
    public bool SpellPlaying => _spellFrame >= 0 || _spellSoundLeft > 0 || _shakeLeft > 0 || Scrolling || FogChanging;

    private const int ShakeSteps = 9;
    private int _shakeLeft;
    private ulong _shakeStart;
    private double _shakeClock;
    private readonly System.Random _shakeRandom = new();

    /// <summary>Литьё в точке: анимация со звуком, а при <paramref name="shake"/> — тряска экрана (у невидимой чужой столицы).</summary>
    public bool ShowSpell(string spellId, int x, int y, bool shake, float speed)
    {
        if (!shake) return PlaySpell(spellId, x, y, speed);
        Shake(speed);
        return true;
    }

    /// <summary>Тряска экрана (<c>CMidAnim2Shake</c>): девять случайных сдвигов вида ±32/±16 точек по кадру медленного аниматора, затем
    /// вид на место. Случайность — своя (у движка <c>rand()</c>, на правила не влияет).</summary>
    public void Shake(float speed = 1f)
    {
        (_shakeLeft, _shakeClock, _spellSpeed) = (ShakeSteps, 0, speed);
        _shakeStart = Time.GetTicksMsec();
        GameLog.Verbose($"shake шагов={ShakeSteps}");
    }

    private void StepShake(double delta)
    {
        if (_shakeLeft <= 0) return;
        _shakeClock += delta * _spellSpeed;
        if (_shakeClock < SpellFrameSeconds) return;
        _shakeClock -= SpellFrameSeconds;
        _shakeLeft--;
        if (_shakeLeft == 0)
            PortProbe.Av?.MapAnim("end", "CMidAnim2Shake", null, null, null, (long)(Time.GetTicksMsec() - _shakeStart), ShakeSteps);
        _camera.Offset = _shakeLeft == 0 ? Vector2.Zero
            : new Vector2(_shakeRandom.Next(-1, 2) * 32, _shakeRandom.Next(-1, 2) * 16);
    }

    /// <summary>Показать литьё <paramref name="spellId"/> в клетке; картинок нет — false. <paramref name="speed"/> — множитель
    /// скорости (проверки без окна).</summary>
    public bool PlaySpell(string spellId, int x, int y, float speed = 1f)
    {
        var parts = SpellMapAnim.Parts(GameData.Content.Gfx, spellId);
        if (!PlayEffect([.. parts.Select(p => (p.Anim, p.Front))], x, y, speed, [("Midgard", LastEdict.Data.Ui.ArtNames.Of(spellId), false)], null, cls: "CMidAnim2Spell")) return false;
        GameLog.Verbose($"spell {spellId} в=({x},{y}) картинок={_spellParts.Count} кадров={_spellLength} звук={_spellSoundLeft:0.###}");
        return true;
    }

    /// <summary>
    /// Гибель отряда на карте (<c>CMidAnim2Dying</c>, <c>docs/ai-screen.md</c> §8): <c>G000UU9999DYIN8</c> (у лидера-призыва —
    /// <c>G000UU9998DYIN8</c>; IsoCmon, 800×600, 16 кадров) в клетке отряда поверх объектов (слой <c>STACK_DEATH</c>); на середине
    /// картинки отряда снимаются и, если модель поставила там могилу (<paramref name="tomb"/>), встаёт картинка могилы (<c>0x4f29c9</c>:
    /// слой <c>TOMBS</c>, <c>G000TB0000W</c> на воде, иначе <c>G</c>); звуки — общий <c>AUNN7778</c> (у призыва <c>AUNN7788</c>) и голос
    /// лидера <c>{тип}KILL</c>, гибель кончается, когда доиграли и кадры, и голос. Своей анимации и звука у могилы нет.
    /// </summary>
    public bool PlayDeath(string stackId, int x, int y, float speed = 1f, bool tomb = false)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return false;
        string type = Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        bool summon = Session.Book?.CategoryOf(type) == LastEdict.Rules.Battle.UnitCategory.Summon;
        if (GameData.Content.Gfx.Anim("IsoCmon", summon ? "G000UU9998DYIN8" : "G000UU9999DYIN8") is not { } anim) return false;
        var sounds = new List<(string, string, bool)> { ("Midgard", summon ? "AUNN7788" : "AUNN7778", false) };
        if (type.Length > 0) sounds.Add(("Midgard", $"{LastEdict.Data.Ui.ArtNames.Of(type)}KILL", true));   // голос юнита — громкостью голосов (0x5b08f4)
        if (!PlayEffect([(anim, true)], x, y, speed, sounds, () =>
            {
                Reload(hiddenStack: stackId);
                // Модель уже рисует могилу (бой человека: состояние обновлено раньше середины) — своя не нужна.
                if (tomb && !Looks.Tombs.Any(t => t.X == x && t.Y == y)) ShowTomb(x, y);
            }, cls: "CMidAnim2Dying", obj: stackId)) return false;
        GameLog.Verbose($"death {stackId} в=({x},{y}) кадров={_spellLength} призыв={(summon ? 1 : 0)} могила={(tomb ? 1 : 0)} " +
                 $"звук={_spellSoundLeft:0.###}");
        return true;
    }

    private const string TombChannel = "tomb";

    /// <summary>Могила на середине гибели, пока модель её ещё не показывает (ход ИИ ждёт показа до снятия отряда).</summary>
    private readonly List<(int X, int Y)> _tombsShown = new();

    private void ShowTomb(int x, int y)
    {
        _tombsShown.Add((x, y));
        ShowLayered(TombChannel, [.. _tombsShown.Select(t =>
            CellSprite("IsoCmon", Map.Contains(t.X, t.Y) && Map.At(t.X, t.Y).Ground == LastEdict.Rules.Map.Ground.Water ? "G000TB0000W" : "G000TB0000G",
                       t.X, t.Y, TombCanvas, TombCanvas, GameData.Content.MapArt.Layer("TOMBS")))]);
    }

    /// <summary>Холст картинки могилы (IsoCmon, 320×320; центр — центр клетки).</summary>
    private const int TombCanvas = 320;

    /// <summary>Модель уже рисует свои могилы — показанные на середине гибели снимаются (зовётся из <see cref="Reload"/>).</summary>
    private void DropShownTombs()
    {
        if (_tombsShown.Count == 0) return;
        int before = _tombsShown.Count;
        _tombsShown.RemoveAll(t => Looks.Tombs.Any(m => m.X == t.X && m.Y == t.Y));
        if (_tombsShown.Count == before) return;
        var left = _tombsShown.ToList();
        _tombsShown.Clear();
        ShowLayered(TombChannel, []);
        foreach (var (x, y) in left) ShowTomb(x, y);
    }

    /// <summary>
    /// Отряд создан событием (<c>CMidAnim2CreateStack</c> <c>0x4f21f2</c>, <c>docs/ai-screen.md</c> §8): <c>G000UU9999CREA8</c> (IsoCmon,
    /// 800×600, 16 кадров) в клетке отряда, звук <c>CREATSTK</c>; до середины отряда не видно. У эльфов движок берёт
    /// <c>G000UU9997CREA8</c>, которого в архивах нет — анимации нет (ранг 2).
    /// </summary>
    public bool PlayCreate(string stackId, int x, int y, float speed = 1f)
    {
        if (GameData.Content.Gfx.Anim("IsoCmon", "G000UU9999CREA8") is not { } anim) return false;
        Reload(hiddenStack: stackId);
        if (!PlayEffect([(anim, true)], x, y, speed, [("Midgard", "CREATSTK", false)], () => Reload(), cls: "CMidAnim2CreateStack", obj: stackId))
        {
            Reload();   // кадров нет на диске — отряд не остаётся скрытым
            return false;
        }
        GameLog.Verbose($"create {stackId} в=({x},{y}) кадров={_spellLength} звук={_spellSoundLeft:0.###}");
        return true;
    }

    /// <summary>Отряд сменил владельца по событию (<c>CMidAnim2ChangeOwner</c> <c>0x4f1eab</c>): <c>G000UU9999COWNF</c>, если новый
    /// владелец — зритель, иначе <c>COWNE</c> (IsoCmon, 26 кадров), звука нет. ponytail: знамя уже нового владельца с начала анимации
    /// (у движка — с середины).</summary>
    public bool PlayOwnerChange(string stackId, string owner, int x, int y, float speed = 1f)
    {
        string name = string.Equals(owner, Viewer, StringComparison.OrdinalIgnoreCase) ? "G000UU9999COWNF" : "G000UU9999COWNE";
        if (GameData.Content.Gfx.Anim("IsoCmon", name) is not { } anim) return false;
        if (!PlayEffect([(anim, true)], x, y, speed, [], () => Reload(), cls: "CMidAnim2ChangeOwner", obj: stackId)) return false;
        GameLog.Verbose($"owner {stackId} в=({x},{y}) картинка={name} кадров={_spellLength}");
        return true;
    }

    /// <summary>
    /// Снос гор событием <c>L_REMOVE_MOUNTAIN</c> (<see cref="LastEdict.Data.Play.TurnSightKind.MountainsRemoved"/>; <c>CMidAnim2Mountain</c>, создание
    /// <c>0x4f44a3</c>, ранг 1): <c>RMVMOUNTAIN7</c> (IsoCmon 800×600; у сообщения сноса размер всегда 7, <c>0x5b82f5</c> → 7) по центру клетки
    /// центра области, слой <c>MOUNTAIN_REMOVE</c> 594; на кадре всего/2 — звук <c>OCCUPY</c> (<c>0x7ae468[10]</c>), горы сносятся и карта
    /// перерисовывается (<c>0x40b2e1</c>). Камеру к центру (если он дальше 2 клеток) ведёт вызывающий — <see cref="ScrollThen"/>. Упрощение: слой — эффекты поверх
    /// объектов, не общий порядок; в ход человека модель уже перерисовала карту до анимации.
    /// </summary>
    public bool PlayMountains(string zoneId, int x, int y, float speed = 1f)
    {
        void Apply()
        {
            GameAudio.Of(this).PlayFx(LastEdict.Data.Sound.WindowSounds.Archive, "OCCUPY");
            RefreshGround();
            Reload();
        }
        if (GameData.Content.Gfx.Anim("IsoCmon", "RMVMOUNTAIN7") is not { } anim || !PlayEffect([(anim, true)], x, y, speed, [], Apply, cls: "CMidAnim2Mountain", obj: zoneId))
        {
            Apply();
            return false;
        }
        GameLog.Verbose($"mountains {zoneId} в=({x},{y}) картинка=RMVMOUNTAIN7 кадров={_spellLength}");
        return true;
    }

    private readonly Queue<Func<bool>> _effectQueue = new();

    /// <summary>Эффект действия человека (главный поток, без ожидания): сразу, если ничего не играет, иначе — после текущего.</summary>
    public void QueueEffect(Func<bool> play)
    {
        _effectQueue.Enqueue(play);
        if (!SpellPlaying) NextEffect();
    }

    /// <summary>Запустить следующий эффект из очереди (не запустившийся — пропустить).</summary>
    private void NextEffect()
    {
        while (_effectQueue.Count > 0 && !_effectQueue.Dequeue()()) { }
    }
    private Action? _spellMiddle;

    /// <summary>Эффект на карте: картинки в центре клетки (под или над объектами), один проход по 66 мс на кадр, звуки, действие на
    /// середине (кадр = всего/2); идёт, пока не доиграли кадры и самый длинный звук.</summary>
    private bool PlayEffect(IReadOnlyList<(GfxAnim Anim, bool Front)> parts, int x, int y, float speed,
                            IReadOnlyList<(string Archive, string Name, bool Voice)> sounds, Action? middle,
                            Vector2? at = null, string cls = "CMidAnim2Animation", string? obj = null)
    {
        StopSpell();
        if (parts.Count == 0) return false;
        _spellUnder ??= SpellLayer("SpellsUnder", under: true);
        _spellOver ??= SpellLayer("SpellsOver", under: false);
        var centre = at ?? CellCentre(x, y);
        foreach (var part in parts)
        {
            // Кадры — общим кэшем графики мира (обрезка, DDS, HD логического размера); свой словарь пережил бы GfxTextures.Release.
            var frames = GfxAssets.FramePaths(part.Anim, GameData.GfxRoot).Select(p => GfxTextures.Of(p)).OfType<Texture2D>().ToArray();
            if (frames.Length == 0) continue;
            var sprite = new Sprite2D
            {
                Texture = frames[0], Centered = false, TextureFilter = ImageScales.Filter(frames[0], TextureFilterEnum.Nearest),
                Position = centre - (frames[0].GetSize() / 2f),
            };
            GfxBlending.Apply(sprite, GfxAssets.BlendOf(part.Anim.Opacity));   // заклинания — 300 (сложение) и 400 (вычитание)
            (part.Front ? _spellOver : _spellUnder).AddChild(sprite);
            _spellParts.Add((sprite, frames));
        }
        if (_spellParts.Count == 0) return false;
        _spellLength = Math.Max(1, parts.Max(p => p.Anim.Frames.Count));
        (_spellFrame, _spellClock, _spellSpeed, _spellMiddle) = (0, 0, speed, middle);
        _anim = (cls, x, y, obj, Time.GetTicksMsec());
        _spellSoundLeft = 0;
        var audio = GameAudio.Of(this);   // общий звук: громкость звуков из настроек
        foreach (var (archive, name, voice) in sounds)
            _spellSoundLeft = Math.Max(_spellSoundLeft, voice ? audio.PlayVoice(archive, name) : audio.PlayFx(archive, name));
        return true;
    }

    private void StepSpell(double delta)
    {
        StepPendingAttack();   // нападение человека после анимации начала боя (MapView.Sounds.cs)
        if (!SpellPlaying && _effectQueue.Count > 0) NextEffect();
        StepShake(delta);
        if (_spellSoundLeft > 0) _spellSoundLeft -= delta * _spellSpeed;
        if (_spellFrame < 0) return;
        _spellClock += delta * _spellSpeed;
        while (_spellClock >= SpellFrameSeconds && _spellFrame >= 0)
        {
            _spellClock -= SpellFrameSeconds;
            _spellFrame++;
            if (_spellFrame == _spellLength / 2 && _spellMiddle is { } middle)
            {
                _spellMiddle = null;
                middle();
            }
            if (_spellFrame >= _spellLength - 1) { StopSpell(); return; }
            foreach (var (sprite, frames) in _spellParts)
            {
                if (_spellFrame >= frames.Length - 1) sprite.Visible = false;
                else sprite.Texture = frames[_spellFrame];
            }
        }
    }

    /// <summary>Идущая анимация для строки <c>map_anim</c> зонда (класс движка, клетка, объект, начало).</summary>
    private (string Class, int X, int Y, string? Obj, ulong StartMs)? _anim;

    private void StopSpell()
    {
        if (_anim is { } a)
        {
            _anim = null;
            // Оборванная новой анимацией — «cut»: сверка длительности берёт только доигравшие (у движка задачи не обрываются).
            PortProbe.Av?.MapAnim(_spellFrame >= _spellLength - 1 ? "end" : "cut", a.Class, a.X, a.Y, a.Obj, (long)(Time.GetTicksMsec() - a.StartMs), Math.Max(0, _spellFrame),
                                  _spellLength);
        }
        foreach (var (sprite, _) in _spellParts) sprite.QueueFree();
        _spellParts.Clear();
        _spellFrame = -1;
        if (_spellMiddle is { } middle)
        {
            _spellMiddle = null;
            middle();
        }
    }

    /// <summary>Слой литья: под объектами — сразу над землёй, над объектами — поверх них.</summary>
    private Node2D SpellLayer(string name, bool under)
    {
        var node = new Node2D { Name = name };
        AddChild(node);
        if (under) MoveChild(node, _objects.GetIndex());
        return node;
    }
}
