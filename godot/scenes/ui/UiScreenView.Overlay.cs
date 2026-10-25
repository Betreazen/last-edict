using LastEdict.Data.Settings;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>Окно на <c>Control</c> поверх экрана своих данных без элементов (Э5): логика экрана — модель окна (<see cref="SettingsLogic"/>),
/// а рисует и принимает ввод окно Godot (<see cref="SettingsWindow"/>). Окно стоит в разметке экрана и вписывается вместе с ним, его
/// кнопки шлют хозяину события элементов (<see cref="Emit"/>) — граф экранов ведёт их, как у экранов из разметки; шаги сценария проверки
/// экран отдаёт окну.</summary>
public partial class UiScreenView
{
    private Control? _overlay;

    /// <summary>Окно поверх экрана, если логика — модель такого окна: настройки (Э5) или лобби сети (Э7).</summary>
    private void AttachOverlay()
    {
        if (_overlay is not null) return;
        _overlay = Logic switch
        {
            SettingsLogic settings => new SettingsWindow(settings, Lookup, Emit),
            LastEdict.Data.Menu.LobbyLogic lobby => new LobbyWindow(lobby, Lookup, Emit),
            _ => null,
        };
        if (_overlay is null) return;
        AddChild(_overlay);
        PlaceOverlay();
    }

    /// <summary>Строка по ключу на языке экрана; нет — сам ключ.</summary>
    private string Lookup(string key) => _strings.TryGetValue(key, out string? text) ? text : key;

    /// <summary>Окно — в разметке экрана: тот же угол и масштаб, что у узлов экрана.</summary>
    private void PlaceOverlay()
    {
        if (_overlay is null || _root is null) return;
        _overlay.Position = _root.Position;
        _overlay.Scale = _root.Scale;
    }

    /// <summary>Событие элемента от окна — тем же путём, что щелчок по элементу разметки (логика, хозяин, граф).</summary>
    public void Emit(string element) => Dispatch([new UiClicked(element)]);

    /// <summary>Шаг сценария проверки — окну поверх экрана, если оно его знает.</summary>
    private bool OverlayStep(string verb, string arg) => _overlay switch
    {
        SettingsWindow settings => settings.Step(verb, arg),
        LobbyWindow lobby => lobby.Step(verb, arg),
        _ => false,
    };

    /// <summary>Строка проверки окна поверх экрана; нет окна — null.</summary>
    public string? OverlayLine() => _overlay switch
    {
        SettingsWindow settings => settings.Line(),
        LobbyWindow lobby => lobby.Line(),
        _ => null,
    };

    private BuildTreeView? _tree;

    /// <summary>Разложенный экран, на котором логика окна построек последний раз раскладывала дерево.</summary>
    private LastEdict.Data.Ui.UiScreen? _treeScreen;

    /// <summary>Дерево окна построек (Э6) — узел в разметке экрана поверх фона дерева; рисует по состоянию логики.</summary>
    private void SyncTree()
    {
        if (Logic is not LastEdict.Data.Ui.Windows.BuildLogic build || _root is null) return;
        if (!ReferenceEquals(_treeScreen, _state.Screen))
        {
            _treeScreen = _state.Screen;
            Guarded(() => build.Relayout(_state), "дерево построек");
        }
        if (_tree is null || _tree.GetParent() != _root)
        {
            _tree?.QueueFree();
            _tree = new BuildTreeView(build, FrameTexture);
            _root.AddChild(_tree);
            // Вид дерева — сразу над фоном дерева: кнопки разметки («Закрыть», «Построить», золото) рисуются поверх него и его карточки.
            if (_sprites.TryGetValue(LastEdict.Data.Ui.Windows.BuildLogic.TreeBackground, out var background) && background.GetParent() == _root)
                _root.MoveChild(_tree, background.GetIndex() + 1);
        }
        _tree.Sync(Origin(_state.Screen));
    }

    /// <summary>Текстура кадра по имени (как у спрайтов экрана, HD — логического размера); нет — null.</summary>
    private static Texture2D? FrameTexture(string frame) => TextureOf(frame);

    /// <summary>Текстура кадра интерфейса или графики игры по имени — вне экрана тоже (Студия: значки построек, в том числе модов).</summary>
    public static Texture2D? TextureOf(string frame) => FrameImage(frame, out _) is { } image ? UiArt.Texture(image) : null;

    /// <summary>Значков дерева окна построек нарисовано (строка проверки); дерева нет — -1.</summary>
    public int TreeDrawn => _tree?.Drawn ?? -1;
}
