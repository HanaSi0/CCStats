using System;

namespace PvpStats.Types.Display;
public class CCScoreboardDouble : PvpScoreboardDouble, IEquatable<CCScoreboardDouble> {
    //public TimeSpan TimeOnCrystal { get; set; }
    public double TimeOnCrystal { get; set; }

    public CCScoreboardDouble() {

    }

    public CCScoreboardDouble(ScoreboardTally playerScoreboard, ScoreboardTally teamScoreboard) : base(playerScoreboard, teamScoreboard) {
        if(playerScoreboard is CCScoreboardTally && teamScoreboard is CCScoreboardTally) {
            var playerCCScoreboard = playerScoreboard as CCScoreboardTally;
            var teamCCScoreboard = teamScoreboard as CCScoreboardTally;

            TimeOnCrystal = teamCCScoreboard!.TimeOnCrystal > TimeSpan.Zero ? playerCCScoreboard!.TimeOnCrystal / teamCCScoreboard.TimeOnCrystal : double.NaN;
        }
    }

    public static CCScoreboardDouble operator /(CCScoreboardDouble a, double b) {
        if(!double.IsFinite(b) || b<=0) b=double.NaN;
        var c = (PvpScoreboardDouble)a / b;
        return new CCScoreboardDouble() {
            Kills = c.Kills,
            Deaths = c.Deaths,
            Assists = c.Assists,
            DamageDealt = c.DamageDealt,
            DamageTaken = c.DamageTaken,
            HPRestored = c.HPRestored,
            KillsAndAssists = c.KillsAndAssists,
            TimeOnCrystal = a.TimeOnCrystal / b,
        };
    }

    public static explicit operator CCScoreboardDouble(CCScoreboardTally a) {
        var c = (PvpScoreboardDouble)a;
        return new CCScoreboardDouble() {
            Kills = c.Kills,
            Deaths = c.Deaths,
            Assists = c.Assists,
            DamageDealt = c.DamageDealt,
            DamageTaken = c.DamageTaken,
            HPRestored = c.HPRestored,
            KillsAndAssists = c.KillsAndAssists,
            TimeOnCrystal = a.TimeOnCrystal.TotalSeconds
        };
    }

    public bool Equals(CCScoreboardDouble? other) {
        if(other is null) {
            return false;
        }
        var thisPvPScoreboard = (PvpScoreboardDouble)this;
        var otherPvPScoreboard = (PvpScoreboardDouble)other;
        return thisPvPScoreboard.Equals(otherPvPScoreboard)
            && TimeOnCrystal == other.TimeOnCrystal;
    }

    public override int GetHashCode() {
        return HashCode.Combine(base.GetHashCode(), TimeOnCrystal);
    }
}
