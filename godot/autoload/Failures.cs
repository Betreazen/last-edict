using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace D2.Godot;

/// <summary>Пойманные исключения C# в главном потоке. Необработанное исключение Godot процесс не роняет — пишет ошибку и крутит цикл
/// дальше (<c>docs/godot-notes.md</c>), а цепочка, которая ждала продолжения (ход ИИ, показ боя, проверка), стоит навсегда. Поэтому
/// там, где ждут продолжения, исключение ловится: ошибка — в журнал движка и в журнал зонда, продолжение — зовёт сам ловящий,
/// под <c>--check</c> — выход с кодом 1.</summary>
public static class Failures
{
    /// <summary>Идёт проверка без окна (<c>--check</c>): ошибка — провал проверки.</summary>
    public static bool Checking => OS.GetCmdlineUserArgs().Contains("--check");

    /// <summary>Записать исключение (<see cref="GD.PushError(string)"/> и журнал зонда); в проверке — <c>Quit(1)</c>.</summary>
    public static void Report(Node? node, string where, Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        GD.PushError($"{where}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        PortProbe.Journal?.Error("caught", e.ToString(), where);
        if (Checking) Quit(node);
    }

    /// <summary>Задача проверки без ожидания (<c>_ = RunCheck(…)</c> теряет исключение после первого <c>await</c>): исключение —
    /// в журнал и выход с кодом 1.</summary>
    public static async void Run(Node node, Func<Task> body, string where)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            await body();
        }
        catch (Exception e)
        {
            Report(node, where, e);
            Quit(node);
        }
    }

    private static void Quit(Node? node)
    {
        if (node is not null && GodotObject.IsInstanceValid(node) && node.IsInsideTree()) node.GetTree().Quit(1);
        else if (Engine.GetMainLoop() is SceneTree tree) tree.Quit(1);
    }
}
