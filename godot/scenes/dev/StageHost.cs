using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Стенд показа: отдельная сцена (экран карты, экран боя) — в буфере показа (<see cref="ScreenStage"/>), как экраны в игре
/// (<see cref="UiFlowView"/>). Для замеров кадра и снимков в каждом режиме вписывания без прохода по меню:
///
/// <c>res://scenes/dev/StageHost.tscn -- --scene res://scenes/map/MapScreen.tscn --scale sharp|integer|nearest</c> и ключи самой сцены
/// (<c>--sg</c>, <c>--check</c>, <c>--clicks</c>, <c>--hold-ms</c>…). Без <c>--scale</c> — чёткий.</summary>
public partial class StageHost : Node
{
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string? path = MapSource.Arg(args, "--scene");
        if (path is null || ResourceLoader.Load<PackedScene>(path) is not { } scene)
        {
            GD.PrintErr($"StageHost: нужна сцена --scene res://… (дано {path ?? "ничего"})");
            GetTree().Quit(1);
            return;
        }
        var stage = new ScreenStage { Name = "Stage", Mode = ScreenFit.Parse(MapSource.Arg(args, "--scale")) ?? ScreenScaling.Sharp };
        AddChild(stage);
        GD.Print($"stage {stage.Describe()}");
        stage.Stage.AddChild(scene.Instantiate());
    }
}
