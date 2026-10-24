using System.Linq;
using LastEdict.Data.Content;
using LastEdict.Data.Ui.Windows;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Горячая перезагрузка модов в идущей партии (Э6, <c>--watch</c>, roadmap §3.4): Студия записала мод — <see cref="GameData"/> перечитал
/// реестр, карта перезапускается <b>со снимка партии</b> (то же состояние, новые правила, экраны, строки и графика) и снова открывает окно,
/// которое было открыто (столица и окно построек). Партию хозяина (игра по графу экранов) — через сохранение-снимок и загрузку; карту,
/// запущенную сама (<c>d2mod run</c>), — перезапуском сцены.
/// </summary>
public partial class MapScreen
{
    private void HookReload() => GameData.Reloaded += OnModsReloaded;

    private void UnhookReload() => GameData.Reloaded -= OnModsReloaded;

    private void OnModsReloaded(ContentReload reload)
    {
        if (_leaving || _map?.Session is not { } session) return;
        _leaving = true;
        string? reopen = _capitalLogic is { } capital
            ? _logics.OfType<BuildLogic>().LastOrDefault() is { } build ? $"build:{capital.CapitalId}|{build.Branch}" : $"capital:{capital.CapitalId}"
            : null;
        GD.Print($"reload map revision={reload.Revision} reopen={reopen ?? "-"} day={session.Map.Turn}");
        GameData.HoldForReload(session.State, _map.Viewer, reopen);
        if (Session is null)
        {
            GetTree().ReloadCurrentScene();
            return;
        }
        string snapshot = ProjectSettings.GlobalizePath("user://studio-reload.d2save");
        LastEdict.Data.Play.GameSave.Write(snapshot, session.State, "studio reload");
        LoadRequested?.Invoke(snapshot);
    }

    /// <summary>После перезапуска со снимка — окно, которое было открыто.</summary>
    private void Reopen(string? reopen)
    {
        if (reopen is null) return;
        string[] parts = reopen.Split(':', 2);
        if (parts.Length != 2) return;
        string[] place = parts[1].Split('|', 2);   // столица и открытая ветка окна построек
        OpenCapital(place[0]);
        if (parts[0] != "build") return;
        if (place.Length == 2 && Windows is { } context) context.Memory.BuildBranch = place[1];
        if (CapitalWindow(BuildLogic.Screen, place[0]) is { } logic) OpenWindow(BuildLogic.Screen, logic);
    }
}
