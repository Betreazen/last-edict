using LastEdict.Data.Input;
using LastEdict.Data.Map;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Settings;

/// <summary>
/// Логика окна настроек Last Edict (Э5, roadmap §3.3: «сцена на Control, вкладки…»): экран <see cref="Screen"/> своих данных без элементов
/// (<c>data/ui/own/screens.json</c>) стоит в графе экранов на месте окна оригинала <c>DLG_OPTIONS</c> (<c>data/ui/flow.manual.json</c>) —
/// в главном меню и в партии; рисует и принимает ввод окно на <c>Control</c> (<c>godot/scenes/settings/SettingsWindow</c>), а кнопки партии
/// («Сохранить», «Загрузить», «Выйти») и «Закрыть» уходят хозяину теми же событиями элементов, что у оригинала, — граф ведёт их как прежде.
///
/// <para>Пункты — <see cref="SettingsPages"/>, выбор — черновик <see cref="SettingsDraft"/>: всё, кроме экрана, действует сразу
/// (<paramref name="preview"/>: громкость слышно, пока двигаешь), в файл — по «Применить» (<paramref name="apply"/>: хозяин применяет и
/// пишет файл); пункты экрана — по «Применить» с подтверждением (<see cref="DisplayConfirm"/>), не подтвердили — откат. Язык меняется из
/// главного меню (<see cref="LanguageLocked"/> в партии): строки экранов перечитывает хозяин.</para>
/// </summary>
public sealed class SettingsLogic(MapSettings settings, bool inGame, Action<MapSettings>? preview, Action<MapSettings>? apply) : IScreenLogic
{
    public const string Screen = "LE_SETTINGS";

    /// <summary>События элементов окна для хозяина и графа (как у <c>DLG_OPTIONS</c>).</summary>
    public const string Close = "BTN_CLOSE", Save = "BTN_SAVE", Load = "BTN_LOAD", Quit = "BTN_QUIT";

    public SettingsDraft Draft { get; } = new(settings);

    public bool InGame => inGame;

    /// <summary>Открытая вкладка.</summary>
    public SettingsTab Tab { get; set; } = SettingsTab.Display;

    /// <summary>Идёт подтверждение нового режима экрана; null — нет.</summary>
    public DisplayConfirm? Confirm { get; private set; }

    /// <summary>Язык меняется из главного меню: в партии строки уже разобраны картой и окнами.</summary>
    public bool LanguageLocked => inGame;

    /// <summary>Кнопки партии — только в партии (в меню их нет, как у окна оригинала вне партии).</summary>
    public static readonly string[] GameButtons = [Save, Load, Quit];

    public void Open(UiScreenState ui)
    {
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }

    public bool Allows(string element, UiScreenState ui) => inGame || !GameButtons.Contains(element);

    /// <summary>Пункт доступен: язык в партии — нет.</summary>
    public bool Enabled(SettingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !(LanguageLocked && item.Tab == SettingsTab.Language);
    }

    /// <summary>Выбор в пункте. Не экран — сразу в игру (без записи в файл).</summary>
    public void Set(SettingItem item, int value)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Enabled(item) || Confirm is not null) return;
        Draft.Set(item, value);
        if (!item.Display) preview?.Invoke(Draft.Current);
    }

    /// <summary>«Применить». Пункты экрана изменены — начато подтверждение (окно применяет режим экрана само и ждёт ответа), иначе выбранное
    /// сохранено. true — нужно подтверждение.</summary>
    public bool Apply()
    {
        if (Confirm is not null) return true;
        if (Draft.DisplayChanged)
        {
            Confirm = new DisplayConfirm();
            return true;
        }
        apply?.Invoke(Draft.Commit());
        return false;
    }

    /// <summary>Ответ на подтверждение: <paramref name="keep"/> — новый режим остаётся и всё сохранено; нет (или время вышло) — пункты экрана
    /// назад, прочее выбранное сохранено. Возвращает настройки, которые окно применяет к экрану.</summary>
    public MapSettings Answer(bool keep)
    {
        Confirm = null;
        if (!keep) Draft.RevertDisplay();
        apply?.Invoke(Draft.Commit());
        return Draft.Current;
    }

    /// <summary>Прошло <paramref name="seconds"/> подтверждения; true — время вышло (окно откатывает через <see cref="Answer"/>).</summary>
    public bool Tick(double seconds) => Confirm?.Tick(seconds) == true;

    /// <summary>«По умолчанию»: все пункты — к умолчаниям, кроме языка в партии; действуют, как выбор (экран — по «Применить»).</summary>
    public void Defaults()
    {
        if (Confirm is not null) return;
        string language = Draft.Current.Language;
        Draft.Defaults();
        if (LanguageLocked) Draft.Set(SettingsPages.Get("language"), SettingsPages.Get("language").Get(new MapSettings(Language: language)));
        preview?.Invoke(Draft.Current);
    }

    /// <summary>Быстрые настройки «Низкое/Высокое качество» (строки оригинала <see cref="LowEndLabel"/>, <see cref="HighEndLabel"/>): действуют, как выбор.</summary>
    public void Preset(bool lowEnd)
    {
        if (Confirm is not null) return;
        preview?.Invoke(Draft.Preset(lowEnd));
    }

    public const string LowEndLabel = "X150TA0031", HighEndLabel = "X150TA0030";

    /// <summary>Вкладка «Управление»: у действия привязка устройства (<c>key</c>/<c>pad</c>) — <paramref name="binding"/> (null — снять), у
    /// действий тех же экранов она снимается (<see cref="InputActions.Bind"/>); действует сразу, как выбор, в файл — по «Применить».</summary>
    public void Bind(IReadOnlyList<InputAction> actions, InputAction action, string device, string? binding)
    {
        if (Confirm is not null) return;
        var map = InputActions.Bind(actions, Draft.Current.Bindings?.Map, action, device, binding);
        preview?.Invoke(Draft.Bind(new KeyBindings(map)));
    }

    /// <summary>Все привязки — по умолчанию (кнопка вкладки «Управление»).</summary>
    public void ResetBindings()
    {
        if (Confirm is not null) return;
        preview?.Invoke(Draft.Bind(null));
    }

    /// <summary>«Отмена» или закрытие: невыбранное — назад к сохранённому, в игру.</summary>
    public void Cancel()
    {
        if (!Draft.Changed) return;
        preview?.Invoke(Draft.Revert());
    }

    /// <summary>Строка проверки: вкладка, черновик и подтверждение.</summary>
    public string Line() =>
        $"settings вкладка={Tab} изменено={(Draft.Changed ? 1 : 0)} экран={(Draft.DisplayChanged ? 1 : 0)} " +
        $"подтверждение={(Confirm is { } c ? c.Left.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")} " +
        $"окно={Draft.Current.WindowMode} музыка={Draft.Current.MusicVolume} противник={Draft.Current.OpponentSpeed} " +
        $"привязок={Draft.Current.Bindings?.Count ?? 0} " +
        $"язык={(Draft.Current.Language.Length == 0 ? "-" : Draft.Current.Language)} сохранено_окно={Draft.Saved.WindowMode}";
}
