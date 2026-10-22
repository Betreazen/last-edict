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
    private SettingsWindow? _overlay;

    /// <summary>Окно поверх экрана, если логика — модель такого окна.</summary>
    private void AttachOverlay()
    {
        if (Logic is not SettingsLogic settings || _overlay is not null) return;
        _overlay = new SettingsWindow(settings, Lookup, Emit);
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

    /// <summary>Строка проверки окна поверх экрана; нет окна — null.</summary>
    public string? OverlayLine() => _overlay?.Line();
}
