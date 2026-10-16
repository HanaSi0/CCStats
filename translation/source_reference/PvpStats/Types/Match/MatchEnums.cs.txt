namespace PvpStats.Types.Match;

public enum DutyType {
    Unknown,
    CrystallineConflict,
}

public enum CrystallineConflictTeamName {
    Unknown,
    Astra,
    Umbra
}

public enum CrystallineConflictMatchType {
    Unknown,
    Casual,
    Ranked,
    Custom
}

public enum CrystallineConflictMap {
    Palaistra,
    VolcanicHeart,
    CloudNine,
    ClockworkCastleTown,
    RedSands,
    BaysideBattleground,
    ArcheiaHarmonias,
}

//this matches the data sheet row IDs for ColosseumMatchRank
public enum ArenaTier {
    None = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4,
    Diamond = 5,
    Crystal = 6,
    Omega = 7,
    Ultima = 8
}

public enum CrystallineConflictMatchEvent {
    CrystalUnchained,
    OvertimeCommenced,
    MatchEnded,
    SpecialEvent
}
