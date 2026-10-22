using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using D2.Data.Diagnostics;
using D2.Data.Map;
using D2.Data.Sound;
using Godot;

namespace D2.Godot;

/// <summary>
/// Звук и музыка игры (<c>docs/ai-screen.md</c> §12): один узел под корнем дерева на весь запуск (переживает смену сцен — меню,
/// карта, бой). Музыка — как у движка (<c>CMidMusic</c>): «основная» (меню или круг карты, <see cref="MusicRotation"/>) и
/// «временная» (бой, столица) — основная затухает, её место запоминается, по концу временной основная продолжается с того же места с
/// нарастанием (<c>0x5aa30e</c>); каждая дорожка играет раз, следующую выбирает конец дорожки. Звуки — пул проигрывателей
/// (<see cref="PlayFx"/>, голоса — <see cref="PlayVoice"/>). Громкости — доли 0…1 (у движка 0…127, <c>AIL_set_*_volume</c>) на шинах
/// <see cref="AudioBuses"/>; музыка при нуле не играет. Проверки без окна печатают строки <c>music</c> и <c>fx</c> (<see cref="GameLog"/>).
/// </summary>
public partial class GameAudio : Node
{
    private const string NodeName = "GameAudio";
    private const int FxPlayers = 8;
    private const float Silence = AudioBuses.Silence;

    private AudioStreamPlayer _music = null!;
    private readonly List<AudioStreamPlayer> _fx = new();
    private float _fxVolume = 1f, _musicVolume = 1f;

    /// <summary>Громкость голосов, доля: <c>Acknoledgements</c> / 100 (по умолчанию 66, <see cref="MapSettings"/>).</summary>
    private float _voiceVolume = new MapSettings().Acknowledgements / (float)MapSettings.MaxAcknowledgements;
    private MusicPlan _plan = new();
    private MusicRotation _rotation = null!;
    private Tween? _fade;

    /// <summary>Дорожка в проигрывателе музыки для журнала зонда (<see cref="MusicNow"/> очищается раньше конца затухания).</summary>
    private string _loaded = "";

    /// <summary>Основная музыка: следующая дорожка по концу текущей (меню — та же, карта — ротация).</summary>
    private Func<string?>? _main;

    /// <summary>Временная (бой, столица): следующая дорожка; null — временной нет.</summary>
    private Func<string?>? _temporary;

    /// <summary>Основная дорожка под временной и место в ней; новая временная во время возврата его не перезаписывает.</summary>
    private readonly MusicReturn _return = new();

    /// <summary>Единственный узел звука. Узел встаёт под корень отложенно (<c>CallDeferred</c>), и до конца кадра поиск по дереву его
    /// не видит — без этой ссылки два вызова в одном кадре создавали два узла (две музыки, два пула звуков).</summary>
    private static GameAudio? _instance;

    /// <summary>Узел звука на весь запуск; нет — создаётся под корнем дерева <paramref name="anyNode"/>.</summary>
    public static GameAudio Of(Node anyNode)
    {
        ArgumentNullException.ThrowIfNull(anyNode);
        if (_instance is { } live && IsInstanceValid(live) && !live.IsQueuedForDeletion()) return live;
        var root = anyNode.GetTree().Root;
        if (root.GetNodeOrNull<GameAudio>(NodeName) is { } found) return _instance = found;
        var audio = new GameAudio { Name = NodeName };
        _instance = audio;
        root.CallDeferred(Node.MethodName.AddChild, audio);
        audio.Build();
        return audio;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && ReferenceEquals(_instance, this)) _instance = null;
    }

    private void Build()
    {
        AudioBuses.Ensure();
        AudioBuses.Set(AudioBuses.Fx, _fxVolume);
        AudioBuses.Set(AudioBuses.Music, _musicVolume);
        AudioBuses.Set(AudioBuses.Voice, _voiceVolume);
        _music = new AudioStreamPlayer { Name = "Music", Bus = AudioBuses.Music };
        _music.Finished += TrackEnded;
        AddChild(_music);
        for (int i = 0; i < FxPlayers; i++)
        {
            var player = new AudioStreamPlayer { Name = $"Fx{i}", Bus = AudioBuses.Fx };
            player.Finished += () => SoundEnded(player, "end");
            AddChild(player);
            _fx.Add(player);
        }
        BuildRegions();
        _plan = MusicPlan.Load(GameData.FromRoot(Path.Combine("data", "music", "playlists.json")));
        _rotation = new MusicRotation(_plan, new Random());
        // Прогон по сценарию (--check, --input: ui-render-check) — затухания мгновенные и файл настроек пользователя не читается (как у
        // меню, UiFlowView.SettingsPath): шаги сценария идут по своим часам, а не по настоящим, и затухание 1500 мс перед роликом
        // «Вступления» не успевало кончиться до возврата в меню (а громкость музыки бралась из файла пользователя: при 0 музыки нет вовсе).
        var args = OS.GetCmdlineUserArgs();
        bool scripted = args.Contains("--check") || args.Contains("--input");
        _fadeScale = scripted ? 0 : 1;
        // Громкости до карты (меню) — из того же файла настроек, что у карты.
        if (!scripted) Apply(MapSettings.Load(ProjectSettings.GlobalizePath("user://settings.json")));
    }

    /// <summary>Последние применённые настройки; null — ещё ни одних (первое применение — без пробных звуков).</summary>
    private MapSettings? _applied;

    /// <summary>Голос пробы громкости голосов окна настроек (<c>0x4c1c19</c>: юнит <c>G000UU0021</c>, <c>0x6d95e8</c>).</summary>
    private const string PreviewVoiceUnit = "G000UU0021", PreviewVoiceTag = "options-ack", PreviewFxTag = "options-fx";

    private readonly Random _previewRandom = new();

    /// <summary>Громкости из настроек (<c>0…127</c>, как <c>FxVolume</c>/<c>MusicVolume</c> в <c>Disciple.ini</c>; голоса 0…100; общая и фоны
    /// мест 0…100 % — только шины <see cref="AudioBuses"/>, у движка их нет). Как у окна
    /// настроек движка, сменённая громкость звука сразу пробуется звуком <c>SOUNDFX</c> (<c>0x4c1b20</c> → <c>0x5b03ce(0x20, −1)</c>
    /// <c>0x4c1b60</c>), сменённая громкость голосов — голосом <c>G000UU0021ACK{1|2|3}</c> (<c>0x4c1bcb</c> → прежний голос пробы снят
    /// <c>0x4c1c06</c>, новый <c>0x4c1c41</c>); музыка пробы не даёт. Строка зонда <c>vol_set</c> — только у сменённой громкости (журнал
    /// <c>corpus/20261016e-av</c>: 18 строк — по одной на шаг ползунка).</summary>
    /// <param name="preview">Изменение из окна настроек (шаг ползунка): тогда сменённые громкости пробуются звуком; применение настроек при
    /// загрузке карты и при запуске — без проб.</param>
    public void Apply(MapSettings settings, bool preview = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var before = _applied;
        _applied = settings;
        bool fx = before is null || before.FxVolume != settings.FxVolume;
        bool voice = before is null || before.Acknowledgements != settings.Acknowledgements;
        if (fx) PortProbe.Av?.VolumeSet("fx", settings.FxVolume);
        if (before is null || before.MusicVolume != settings.MusicVolume) PortProbe.Av?.VolumeSet("music", settings.MusicVolume);
        if (voice) PortProbe.Av?.VolumeSet("voice", settings.Acknowledgements);
        FxVolume = settings.FxVolume / (float)MapSettings.MaxVolume;
        MusicVolume = settings.MusicVolume / (float)MapSettings.MaxVolume;
        _voiceVolume = settings.Acknowledgements / (float)MapSettings.MaxAcknowledgements;
        AudioBuses.Set(AudioBuses.Voice, _voiceVolume);
        AudioBuses.Set(AudioBuses.Master, settings.MasterVolume / (float)MapSettings.MaxBusVolume);
        AudioBuses.Set(AudioBuses.Ambient, settings.AmbientVolume / (float)MapSettings.MaxBusVolume);
        if (before is null || !preview) return;
        if (fx)
        {
            StopFx(PreviewFxTag);   // быстрые шаги ползунка не копят пробы
            PlayFx("Midgard", "SOUNDFX", tag: PreviewFxTag);
        }
        if (!voice) return;
        StopFx(PreviewVoiceTag);
        int roll = _previewRandom.Next(100);
        PlayVoice("Midgard", $"{PreviewVoiceUnit}ACK{(roll < 25 ? 1 : roll < 50 ? 2 : 3)}", PreviewVoiceTag);
    }

    /// <summary>Множитель длительности затуханий: проверки без окна проходят шаги за кадры — затухания мгновенные.</summary>
    private int _fadeScale = 1;

    public MusicPlan Plan => _plan;

    /// <summary>Громкость звуков, 0…1 (шина <see cref="AudioBuses.Fx"/>; голоса и фоны мест — под ней).</summary>
    public float FxVolume
    {
        get => _fxVolume;
        set
        {
            _fxVolume = Math.Clamp(value, 0f, 1f);
            AudioBuses.Set(AudioBuses.Fx, _fxVolume);
        }
    }

    /// <summary>Громкость музыки, 0…1. С нуля — музыка начинается заново (основная), до нуля — останавливается (<c>0x4c1c6d</c>).</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            float before = _musicVolume;
            _musicVolume = Math.Clamp(value, 0f, 1f);
            if (_loaded.Length > 0 && before != _musicVolume)
                PortProbe.Av?.MusicVolume(_loaded, Volume127(before), Volume127(_musicVolume), 0, 1);
            AudioBuses.Set(AudioBuses.Music, _musicVolume);
            if (_musicVolume <= 0) { StopTraced(); MusicNow = ""; return; }
            _music.VolumeDb = 0;   // громкость — на шине
            if (before <= 0 && _main is not null) Later(() => Start(_main(), fadeInMs: 0));
        }
    }

    /// <summary>Что играет сейчас (имя дорожки описи <c>Music</c>); ничего — пусто.</summary>
    public string MusicNow { get; private set; } = "";

    /// <summary>Музыка меню (<c>MENUTRCK</c> по кругу); уже играет — не прерывается. Со сменой — затухание прежней (<c>0x4e9dd8</c>).</summary>
    public void PlayMenu()
    {
        var menu = _plan.Menu;
        if (menu.Count > 0 && _temporary is null && menu.Contains(MusicNow, StringComparer.OrdinalIgnoreCase)) return;
        _temporary = null;
        _tuneEnded = null;
        _return.Reset();
        int i = 0;
        _main = () => menu.Count == 0 ? null : menu[i++ % menu.Count];
        FadeThen(_plan.Fades.MenuOut, () => Start(_main(), _plan.Fades.MenuIn));
    }

    /// <summary>Музыка брифа (<c>CBriefingInterf</c>, <c>0x48091e</c>): у главы своей музыкой (<paramref name="voice"/>,
    /// <see cref="D2.Data.Game.ChapterInfo.BriefingMusic"/>: сага эльфов — <c>briefel</c> + номер главы, <c>0x480a71</c>) — озвучка из
    /// <c>Briefing</c> один раз (<c>playMainFile</c> <c>0x5a9eeb</c> без повтора, папка <c>0x61a2cf</c>), по концу — тишина; иначе
    /// <c>GENBRIEF</c> основной по кругу (<c>0x480b59</c>, имя <c>[0x7ae460]</c>). Без нарастания; меню перед ней затухает 700 мс
    /// (<c>0x4e6f11</c>). Музыка уже играет — не трогается (<c>0x480940</c>: возврат на бриф с дочернего окна). Уход с брифа на карту —
    /// <see cref="PlayMap"/> (затухание 500 мс, <c>0x480c31</c>).</summary>
    public void PlayBriefing(string? voice = null)
    {
        if (voice is not null)
        {
            if (MusicNow.Equals(voice, StringComparison.OrdinalIgnoreCase)) return;
            _temporary = null;
            _tuneEnded = null;
            _return.Reset();
            _main = null;
            FadeThen(_plan.Fades.BriefingOut, () => Start(voice, _plan.Fades.TempIn, folder: BriefingFolder));
            return;
        }
        if (_main == BriefingNext && MusicNow.Length > 0) return;   // возврат на бриф с дочернего окна — музыка брифа продолжается
        _temporary = null;
        _tuneEnded = null;
        _return.Reset();
        _main = BriefingNext;
        FadeThen(_plan.Fades.BriefingOut, () => Start(_main(), _plan.Fades.TempIn));
    }

    /// <summary>Папка озвучки брифа (<c>Briefing</c>): у порта — звук без картинки в описи роликов (<see cref="D2.Data.Ui.UiVideos"/>).</summary>
    private const string BriefingFolder = "Briefing";

    /// <summary>Озвучка брифа по имени описи роликов (<c>.ogg</c>); нет в описи или на диске — null.</summary>
    private static AudioStream? BriefingVoice(string name)
    {
        if (!GameData.Content.VideosByName.TryGetValue(name, out var video) || video.HasVideo) return null;
        string path = D2.Data.Ui.UiVideos.PathOf(video, GameData.VideoRoot);
        return File.Exists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
    }

    /// <summary>Дорожка брифа (по кругу та же); списка нет — тишина.</summary>
    private string? BriefingNext() => _plan.Briefing.Count > 0 ? _plan.Briefing[0] : null;

    /// <summary>Музыка карты: списки ingame/amb чередуются (<c>0x5b92a8</c>); уже играет карта — не прерывается.</summary>
    public void PlayMap()
    {
        if (_main == MapNext && (_temporary is not null || MusicNow.Length > 0)) return;
        _temporary = null;
        _tuneEnded = null;
        _return.Reset();
        _main = MapNext;
        FadeThen(_plan.Fades.MenuOut, () => Start(_main(), _plan.Fades.TempIn));
    }

    private string? MapNext() => _rotation.NextMap();

    /// <summary>Конец мелодии <see cref="PlayTune"/>; нет мелодии — null.</summary>
    private Action? _tuneEnded;

    /// <summary>Мелодия поверх музыки (звук эпилога <c>WAV_WIN</c>/<c>GENBRIEF</c>, <c>0x4bca73</c>): музыка затухает
    /// <paramref name="fadeMs"/> и больше не продолжается, мелодия играет один раз громкостью музыки; по её концу — <paramref name="ended"/>.
    /// Нет дорожки или музыка выключена — <paramref name="ended"/> сразу. Затухание перед мелодией, прерванное другой музыкой (бой, меню,
    /// карта), мелодию отменяет, и <paramref name="ended"/> не зовётся (у эпилога — последнего окна партии — другой музыки нет).</summary>
    public void PlayTune(string name, int fadeMs, Action ended)
    {
        ArgumentNullException.ThrowIfNull(ended);
        _main = null;
        _temporary = null;
        _return.Reset();
        _tuneEnded = null;
        FadeThen(fadeMs, () =>
        {
            Start(name, fadeInMs: 0);
            if (MusicNow.Length == 0)
            {
                ended();
                return;
            }
            _tuneEnded = ended;
        });
    }

    /// <summary>Музыка затухает и молчит (ролик вступления со своим звуком, <c>0x4eb21a</c>: 1500 мс).</summary>
    public void StopMusic(int fadeMs)
    {
        _main = null;
        _temporary = null;
        _return.Reset();
        _tuneEnded = null;
        MusicNow = "";   // уже не «играет меню»: возврат в меню до конца затухания снова запустит его музыку (и снимет затухание)
        FadeThen(fadeMs, () =>
        {
            StopTraced();
            MusicNow = "";
            GameLog.Verbose("music stop");
        });
    }

    /// <summary>Музыка боя: основная затухает (1000 мс), временная — <c>BATTLE01–04</c> по кругу (<c>0x62cc90</c>).</summary>
    public void BeginBattle() => BeginTemporary(_rotation.NextBattle, _plan.Fades.BattleOut);

    /// <summary>Тема столицы расы (<c>RaceType</c>): основная затухает (1500 мс), временная — 1→2→3 по кругу (<c>0x4af097</c>).</summary>
    public void BeginCapital(int raceType) => BeginTemporary(() => _rotation.NextCapital(raceType), _plan.Fades.CapitalOut);

    /// <summary>Конец боя или столицы: временная затухает, основная продолжается с запомненного места с нарастанием. Пока идёт
    /// затухание, новая временная (бой, столица) место основной не трогает (<see cref="MusicReturn"/>).</summary>
    public void EndTemporary(bool battle)
    {
        if (_temporary is null) return;
        _temporary = null;
        _return.End();
        FadeThen(battle ? _plan.Fades.BattleEnd : _plan.Fades.CapitalEnd, () =>
        {
            _return.Resumed();
            Start(_return.Track, _plan.Fades.ResumeIn, _return.At);
        });
    }

    private void BeginTemporary(Func<string?> next, int fadeOutMs)
    {
        _return.Begin(MusicNow, _music.Playing ? _music.GetPlaybackPosition() : 0);
        _temporary = next;
        FadeThen(fadeOutMs, () => Start(next(), _plan.Fades.TempIn));
    }

    /// <summary>Дорожка кончилась: следующая временной или основной.</summary>
    private void TrackEnded()
    {
        if (_loaded.Length > 0) PortProbe.Av?.Music("end", _loaded);
        _loaded = "";
        if (_tuneEnded is { } tune)
        {
            _tuneEnded = null;
            MusicNow = "";
            GameLog.Verbose("music tune ended");
            tune();
            return;
        }
        var next = _temporary ?? _main;
        if (next is not null) Start(next(), fadeInMs: 0);
        else MusicNow = "";   // разовая дорожка (озвучка брифа) кончилась — тишина
    }

    /// <summary>Запустить дорожку с места <paramref name="from"/> и нарастанием; громкость 0 — не играть (<c>0x5a9b76</c>).</summary>
    private void Start(string? track, int fadeInMs, float from = 0, string folder = "Music")
    {
        if (!IsInsideTree()) { Later(() => Start(track, fadeInMs, from, folder)); return; }
        StopTraced();
        MusicNow = "";
        if (track is null || _musicVolume <= 0) return;
        var stream = folder == BriefingFolder ? BriefingVoice(track) : GameSound.Load("Music", track).Stream;
        if (stream is null) return;
        _music.Stream = stream;
        _music.VolumeDb = fadeInMs > 0 ? PlayerDb(Silence) : 0;
        _music.Play(from);
        MusicNow = track;
        _loaded = track;
        if (PortProbe.Av is { } av)
        {
            av.Music("open", track, $"{folder}/{track}");
            if (from > 0) av.Music("seek", track, pos: (long)Math.Round(from * 1000));
            av.Music("start", track);
            if (fadeInMs > 0)
            {
                av.MusicFade(track, "in", "none", fadeInMs, Volume127(_musicVolume));
                av.MusicVolume(track, 0, Volume127(_musicVolume), fadeInMs * _fadeScale, 1);
            }
        }
        if (fadeInMs > 0) Fade(Silence, Db(_musicVolume), fadeInMs, null);
        GameLog.Verbose(FormattableString.Invariant($"music {track} с={from:0.#} временная={(_temporary is null ? 0 : 1)}"));
    }

    /// <summary>Затухание текущей дорожки, затем действие (<c>CMusicFader</c> <c>0x5afbe4</c>); тишина — действие сразу.</summary>
    private void FadeThen(int ms, Action then)
    {
        ms *= _fadeScale;
        if (!IsInsideTree()) { Later(() => FadeThen(ms, then)); return; }
        if (!_music.Playing || ms <= 0) { then(); return; }
        if (_loaded.Length > 0 && PortProbe.Av is { } av)
        {
            av.MusicFade(_loaded, "out", "call", ms / Math.Max(1, _fadeScale));
            av.MusicVolume(_loaded, (int)Math.Round(Mathf.DbToLinear(HeardDb) * MapSettings.MaxVolume), 0, ms, 1);
        }
        Fade(HeardDb, Silence, ms, then);
    }

    /// <summary>Слышимая громкость музыки, дБ: проигрыватель плюс шина <see cref="AudioBuses.Music"/>.</summary>
    private float HeardDb => _music.VolumeDb + Db(_musicVolume);

    /// <summary>Громкость проигрывателя музыки, при которой слышно <paramref name="heardDb"/>: громкость настройки несёт шина, а затухания
    /// идут на проигрывателе по той же прямой в дБ, что прежде (от тишины до громкости настройки), только сдвинутой на громкость шины.</summary>
    private float PlayerDb(float heardDb) => heardDb - Db(_musicVolume);

    /// <summary>Затухание или нарастание музыки от <paramref name="fromDb"/> до <paramref name="toDb"/> (слышимые дБ).</summary>
    private void Fade(float fromDb, float toDb, int ms, Action? then)
    {
        if (ms * _fadeScale <= 0)
        {
            _fade?.Kill();
            if (toDb > Silence) _music.VolumeDb = PlayerDb(toDb);
            then?.Invoke();
            return;
        }
        _fade?.Kill();
        _music.VolumeDb = PlayerDb(fromDb);
        _fade = CreateTween();
        _fade.TweenProperty(_music, "volume_db", PlayerDb(toDb), ms / 1000.0);
        if (then is not null) _fade.Finished += then;
    }

    /// <summary>Отложить до входа в дерево. Узел могут освободить раньше (выход из игры с открытым боем: конец боя зовёт
    /// <see cref="EndTemporary"/>, а узел уже уходит) — тогда отложенное не выполняется, иначе <c>ObjectDisposedException</c>.</summary>
    private void Later(Action action) => Callable.From(() =>
    {
        if (IsInstanceValid(this)) action();
    }).CallDeferred();

    /// <summary>Звук из архива (<c>Midgard</c>, <c>Battle</c>, <c>AudioRgn</c>) на шине звуков; свободный проигрыватель пула, занятые — самый
    /// давний. Возвращает длительность звука (0 — нет в описи или на диске). <paramref name="volume"/> — доля громкости звука: у движка
    /// <c>громкость% × FxVolume / 100</c> (<c>0x51fff7</c>). <paramref name="tag"/> — метка группы звуков, которую снимает
    /// <see cref="StopFx(string)"/> (шаги отряда движок глушит в конце хода).</summary>
    public double PlayFx(string archive, string name, float volume = 1f, string tag = "") => Play(archive, name, volume, tag, voice: false);

    /// <summary>Звук громкостью голосов (<c>Acknoledgements</c>) на шине <see cref="AudioBuses.Voice"/>: голоса юнитов (<c>ACK</c>,
    /// <c>IDLE</c>, <c>ATTK</c>, <c>KILL</c>) и шаги отряда. В журнале зонда — доля голосов, как прежде.</summary>
    public double PlayVoice(string archive, string name, string tag = "") => Play(archive, name, _voiceVolume, tag, voice: true);

    private double Play(string archive, string name, float volume, string tag, bool voice)
    {
        var (stream, seconds) = GameSound.Load(archive, name);
        int percent = (int)Math.Round(Math.Clamp(volume, 0f, 1f) * 100);
        if (stream is null)
        {
            PortProbe.Av?.Sound(name, archive, percent, Center, false, null, 0, false, Via(archive), PortProbe.Top);
            return 0;
        }
        if (!IsInsideTree())
        {
            Later(() => Play(archive, name, volume, tag, voice));
            return seconds;
        }
        var player = _fx.FirstOrDefault(p => !p.Playing) ?? _fx[0];
        _fx.Remove(player);
        _fx.Add(player);   // в конец — самый недавний
        SoundEnded(player, "stop");   // занятый проигрыватель: прежний звук снят
        player.Stream = stream;
        player.SetMeta(TagMeta, tag);
        player.Bus = voice ? AudioBuses.Voice : AudioBuses.Fx;
        player.VolumeDb = voice ? 0 : Db(Math.Clamp(volume, 0f, 1f));   // громкость звуков и голосов — на шинах
        player.Play();
        Traced(player, name, archive, percent, Center, loop: false, Via(archive));
        GameLog.Verbose(FormattableString.Invariant($"fx {archive}/{name} с={seconds:0.###}"));
        return seconds;
    }

    /// <summary>Остановить звуки (смена сцены).</summary>
    public void StopFx()
    {
        foreach (var p in _fx)
        {
            SoundEnded(p, "stop");
            p.Stop();
        }
    }

    /// <summary>Остановить звуки группы <paramref name="tag"/>.</summary>
    public void StopFx(string tag)
    {
        foreach (var p in _fx.Where(p => p.Playing && (string)p.GetMeta(TagMeta, "") == tag))
        {
            SoundEnded(p, "stop");
            p.Stop();
        }
    }

    private const string TagMeta = "tag";

    // ------------------------------------------------------------------ журнал зонда (схема 7.3, AvTrace)

    /// <summary>Панорама «по центру» (у звуков без места; у движка — 64 из 0…127).</summary>
    private const int Center = 64;

    /// <summary>Звучащий звук для строки <c>snd_end</c>: номер, имя, начало, громкость Miles от и до, смены громкости и панорамы.</summary>
    private sealed class LiveSound(long id, string name, ulong startMs, int output)
    {
        public long Id { get; } = id;
        public string Name { get; } = name;
        public ulong StartMs { get; } = startMs;
        public int Min { get; set; } = output;
        public int Max { get; set; } = output;
        public int Changes { get; set; }
        private int _last = output;

        /// <summary>Новая громкость (та же — не смена).</summary>
        public void Volume(int output)
        {
            if (output == _last) return;
            _last = output;
            Min = Math.Min(Min, output);
            Max = Math.Max(Max, output);
            Changes++;
        }
    }

    private readonly Dictionary<Node, LiveSound> _live = new();
    private long _soundIds;

    /// <summary>Кто просил звук — как у зонда оригинала: проигрыватель боя, фон мест, общий проигрыватель карты и окон.</summary>
    private static string Via(string archive) => archive switch
    {
        "Battle" => "battle",
        "AudioRgn" => "region",
        _ => "game",
    };

    /// <summary>Громкость Miles 0…127, как у движка (<c>0x51fff7</c>): <c>vol% × FxVolume / 100</c>, целочисленно.</summary>
    private int Output(int percent) => percent * (int)Math.Round(_fxVolume * MapSettings.MaxVolume) / 100;

    private static int Volume127(float share) => (int)Math.Round(share * MapSettings.MaxVolume);

    private void Traced(Node player, string name, string archive, int percent, int pan, bool loop, string via)
    {
        if (PortProbe.Av is not { } av) return;
        long id = ++_soundIds;
        int output = Output(percent);
        _live[player] = new LiveSound(id, name, Time.GetTicksMsec(), output);
        av.Sound(name, archive, percent, pan, loop, output, id, true, via, PortProbe.Top);
    }

    /// <summary>Звук проигрывателя кончился (<c>end</c>) или снят (<c>stop</c>) — строка <c>snd_end</c>; не звучал — ничего.</summary>
    private void SoundEnded(Node player, string how)
    {
        if (!_live.Remove(player, out var live)) return;
        PortProbe.Av?.SoundEnd(live.Id, live.Name, how, (long)(Time.GetTicksMsec() - live.StartMs), live.Min, live.Max, live.Changes);
    }

    /// <summary>Музыка снята: строка <c>music stop</c> (была дорожка) и остановка проигрывателя.</summary>
    private void StopTraced()
    {
        if (_loaded.Length > 0 && _music.Playing) PortProbe.Av?.Music("stop", _loaded);
        _loaded = "";
        _music.Stop();
    }

    // ------------------------------------------------------------------ фон мест

    /// <summary>Слотов фона мест: настройка <c>AudioRegions</c> — до 4.</summary>
    private const int RegionSlots = 4;

    /// <summary>Смена громкости фона — плавно (<c>CAudioRegionMgr</c> ведёт её по шагам, ранг 2), здесь — за столько секунд.</summary>
    private const float RegionFadeSeconds = 0.25f;

    private sealed class RegionSlot
    {
        public required AudioStreamPlayer Player { get; init; }
        public required AudioEffectPanner Panner { get; init; }
        public string Key { get; set; } = "";
        public Tween? Fade { get; set; }
    }

    private readonly List<RegionSlot> _regions = new();

    /// <summary>Слот — своя шина с панорамой (у <see cref="AudioStreamPlayer"/> её нет), выход — в шину фонов мест
    /// <see cref="AudioBuses.Ambient"/>; звук идёт по кругу, пока слот занят.</summary>
    private void BuildRegions()
    {
        for (int i = 0; i < RegionSlots; i++)
        {
            string bus = $"Region{i}";
            bool fresh = AudioServer.GetBusIndex(bus) < 0;
            int index = AudioBuses.Add(bus, AudioBuses.Ambient);
            if (fresh) AudioServer.AddBusEffect(index, new AudioEffectPanner());
            var slot = new RegionSlot
            {
                Player = new AudioStreamPlayer { Name = bus, Bus = bus },
                Panner = (AudioEffectPanner)AudioServer.GetBusEffect(index, 0),
            };
            slot.Player.Finished += () => { if (slot.Key.Length > 0) slot.Player.Play(); };
            AddChild(slot.Player);
            _regions.Add(slot);
        }
    }

    /// <summary>Что звучит из фона мест (<see cref="AudioRegions.Nearest"/>); пусто — тишина (окна, бой, столица). Источник, который
    /// уже звучит, только меняет громкость и панораму.</summary>
    public void SetRegions(IReadOnlyList<AudioVoice> voices)
    {
        ArgumentNullException.ThrowIfNull(voices);
        if (!IsInsideTree()) return;
        var wanted = voices.ToDictionary(Key);
        foreach (var slot in _regions.Where(s => s.Key.Length > 0 && !wanted.ContainsKey(s.Key)))
        {
            slot.Key = "";
            SoundEnded(slot.Player, "stop");
            FadeRegion(slot, Silence, stop: true);
        }
        foreach (var (key, voice) in wanted)
        {
            var slot = _regions.FirstOrDefault(s => s.Key == key);
            if (slot is null)
            {
                var (stream, _) = GameSound.Load(AudioRegions.Archive, voice.Source.Name);
                slot = _regions.FirstOrDefault(s => s.Key.Length == 0);
                if (stream is null || slot is null) continue;
                slot.Fade?.Kill();
                slot.Key = key;
                slot.Player.Stream = stream;
                slot.Player.VolumeDb = Silence;
                slot.Player.Play();
                Traced(slot.Player, voice.Source.Name, AudioRegions.Archive, voice.Volume, voice.Pan, loop: true, "region");
                GameLog.Verbose($"region {voice.Source.Name} ({voice.Source.X},{voice.Source.Y}) громкость={voice.Volume} панорама={voice.Pan}");
            }
            else if (_live.TryGetValue(slot.Player, out var live))
            {
                live.Volume(Output(voice.Volume));
            }
            slot.Panner.Pan = (voice.Pan - 64) / 64f;
            FadeRegion(slot, Db(voice.Volume / 100f), stop: false);   // громкость звуков — на шине (фоны мест под ней)
        }
    }

    /// <summary>Звучащие сейчас источники фона (для проверок).</summary>
    public IReadOnlyList<string> RegionsNow => [.. _regions.Where(s => s.Key.Length > 0).Select(s => s.Key)];

    private static string Key(AudioVoice v) => $"{v.Source.Name}@{v.Source.X},{v.Source.Y}";

    private void FadeRegion(RegionSlot slot, float toDb, bool stop)
    {
        slot.Fade?.Kill();
        if (_fadeScale == 0)
        {
            slot.Player.VolumeDb = toDb;
            if (stop) slot.Player.Stop();
            return;
        }
        slot.Fade = CreateTween();
        slot.Fade.TweenProperty(slot.Player, "volume_db", toDb, RegionFadeSeconds);
        if (stop) slot.Fade.Finished += () => { if (slot.Key.Length == 0) slot.Player.Stop(); };
    }

    private static float Db(float linear) => AudioBuses.Db(linear);
}
