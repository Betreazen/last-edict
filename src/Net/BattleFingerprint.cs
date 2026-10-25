using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LastEdict.Rules.Battle;

namespace LastEdict.Net;

/// <summary>Отпечаток собранного боя: группы, раунд, стороны и каждый юнит (тип, атаки, место, здоровье, опыт, броня укрепления). Клиент
/// собирает бой у себя по виду и сверяет отпечаток с хостом: не сошлось — вид неполон для этого боя, и клиент просит resync, а не играет
/// другой бой.</summary>
public static class BattleFingerprint
{
    public static string Of(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{state.AttackerGroup}|{state.DefenderGroup}|{state.Round}|{state.AttackersPowerShift}|")
            .Append(CultureInfo.InvariantCulture, $"{state.DefendersPowerShift}|{state.AttackersHumanOnEasy}|{state.DefendersHumanOnEasy}|")
            .Append(CultureInfo.InvariantCulture, $"{state.DefendersCanRetreat}|{state.LimitRounds}\n");
        foreach (var u in state.Units)
            text.Append(CultureInfo.InvariantCulture, $"{u.Id}|{u.Type.Id}|{u.Attack.Id}|{u.Attack2?.Id}|{u.Attacker}|{u.Slot}|{u.GroupIndex}|")
                .Append(CultureInfo.InvariantCulture, $"{u.HpMax}|{u.Hp}|{u.Xp}|{u.FortificationArmor}|{u.Hidden}\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32].ToLowerInvariant();
    }
}
