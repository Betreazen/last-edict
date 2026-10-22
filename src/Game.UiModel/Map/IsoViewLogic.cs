using D2.Data.Menu;
using D2.Data.Ui;

namespace D2.Data.Map;

/// <summary>Вид карты <c>DLG_ISO_VIEW</c> (<c>CIsoView</c>): поверх карты и под панелями — рамка края поля <c>ISO_OUTLINE</c>
/// (800×600, непрозрачна только полоса x 594…641: линия края и уголки-орнаменты вверху и внизу, внизу — x 595…640, y 505…600). Имя
/// стоит в таблице строк вида рядом с <c>IMG_MAIN</c>, <c>IMG_PALETTE</c>, <c>IMG_RESOURCE</c> (<c>0x799954</c>; ранг 2 — порядок
/// рисования по снимкам 28.09: уголок поверх земли, панели поверх уголка). В разметке <c>.dlg</c> её нет — рамку ставит логика вида.</summary>
public sealed class IsoViewLogic : IScreenLogic
{
    public const string Screen = "DLG_ISO_VIEW", Outline = "ISO_OUTLINE";

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetBackground(Outline);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
    }
}
