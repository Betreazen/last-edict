using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Game;

/// <summary>
/// Расы меню из содержимого (Э5): <see cref="RaceDef"/> с видом (<see cref="RaceLook"/>) и лорды (<see cref="LordDef"/>) — с модами поверх,
/// поэтому раса мода есть и в выборе расы. Прежде каталог рас меню был отдельными данными импорта (<c>data/game/races.json</c>) и знал
/// только расы оригинала. Выбрать в меню можно расу с лордом каждого типа (маг, воин, дипломат — окно выбора лорда): у расы мода без них
/// <see cref="RaceInfo.Playable"/> ложно, она не в круге выбора — а не ошибка каталога посреди меню.
/// </summary>
public static class MenuRaces
{
    public static IReadOnlyList<RaceInfo> From(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var lords = content.Lords.ToLookup(l => l.Race ?? "", StringComparer.OrdinalIgnoreCase);
        return [.. content.Races.Select(r =>
            {
                var look = RaceLooks.Of(r);
                var own = lords[r.Id].ToList();
                bool lordsComplete = Enum.GetValues<LastEdict.Rules.Map.LordCategory>().All(c => own.Count(l => l.Category == c) == 1);
                return new RaceInfo
                {
                    Type = (int)r.Kind, Id = r.Id, Code = look.Code, NameText = r.NameText ?? "", Playable = r.Playable && r.Neutral != true && lordsComplete,
                    Art = look.Art, BriefingVideo = look.BriefingVideo, TitleText = look.TitleText, ChoiceOrder = look.ChoiceOrder,
                    ChoiceWater = look.ChoiceWater, ChoiceFireflies = look.ChoiceFireflies, Saga = look.Saga,
                    Original = r.LegacyId is not null,
                    Lords = [.. own.Select(l => new LordInfo(l.Id, (int)l.Category, l.NameText ?? "", l.DescriptionText ?? ""))
                                           .OrderBy(l => l.Category)],
                };
            })
            .OrderBy(r => r.Type).ThenBy(r => r.Original ? 0 : 1).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }
}
