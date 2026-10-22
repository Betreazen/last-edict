using System;
using System.Linq;
using LastEdict.Data.Map;
using LastEdict.Data.Play;
using Godot;

namespace LastEdict.Godot;

/// <summary>Быстрое сохранение (<see cref="QuickSave"/>): Q (и Ctrl+Q — у движка код клавиши без учёта Ctrl) на карте в свой ход человека —
/// партия в слот «QuickSave» каталога сохранений, один файл. Только когда карта принимает ввод: нет окон поверх, попапов, боя, столицы и
/// дипломатии, прицела, хода ИИ и идущего отряда (у движка клавишу берёт обработчик карты <c>CStratInterf</c>, <c>0x48e8e3</c>). Строки о
/// записи — в чат карты (<see cref="IsoChatLogic"/>), как у движка и у автосохранения.</summary>
public partial class MapScreen
{
    private IsoChatLogic? _chat;
    private UiScreenView? _chatView;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q } && QuickSaveAllowed) WriteQuickSave();
    }

    /// <summary>Карта принимает ввод человека в его ход.</summary>
    private bool QuickSaveAllowed =>
        _placed && !_leaving && _map.Viewer is not null && !_map.AiRunning && !_map.Walking && _dialogViews.Count == 0 && !_dialogsStale
        && _overlay is null && _battle is null && _card is null && !CoversMap && _exchangePanel is null && _dropPanel is null
        && !Aiming && _dropStack is null && _exchangeStack is null && _rodStack is null;

    private void WriteQuickSave()
    {
        if (SavesDirOrNull() is not { } dir) return;
        var state = _map.Session.Snapshot();
        if (SaveWithChat(QuickSave.Name, () => QuickSave.Write(dir, state)) is { } path)
            GD.Print($"quicksave {System.IO.Path.GetFileName(path)} день={_map.Map.Turn} чат={string.Join("|", _chat?.Lines ?? []).Replace(' ', '_')}");
    }

    /// <summary>Каталог сохранений; проверка (<c>--check</c>) без своего <c>--saves</c> не пишет никуда — иначе писала бы в сохранения
    /// пользователя. Без человека-зрителя — тоже null.</summary>
    private string? SavesDirOrNull()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (_map.Viewer is null || (args.Contains("--check") && MapSource.Arg(args, "--saves") is null)) return null;
        return UiFlowView.SavesDir(args);
    }

    /// <summary>Запись партии со строками чата движка (<c>0x48fa11</c>): «Сохраняется &lt;имя&gt;.sg...», запись, «Игра записана под именем
    /// &lt;имя&gt;». Ошибка записи — в лог, без второй строки.</summary>
    private string? SaveWithChat(string name, Func<string?> write)
    {
        var (saving, saved) = QuickSave.Messages(Text, name);
        Chat(saving);
        try
        {
            string? path = write();
            if (path is not null) Chat(saved);
            return path;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"MapScreen: «{name}» не записано: {e.Message}");
            return null;
        }
    }

    private void Chat(string message)
    {
        if (_chat is null || _chatView is null) return;
        _chat.Say(message, (long)Time.GetTicksMsec());
        _chat.Show(_chatView.State);
        _chatView.Sync();
    }

    private void ExpireChat()
    {
        if (_chat is null || _chatView is null || !_chat.Expire((long)Time.GetTicksMsec())) return;
        _chat.Show(_chatView.State);
        _chatView.Sync();
    }
}
