namespace CCStats.Core.Capture;
// Mirrors the existing MatchHelper.GetMatchType duty IDs. Unknown duties stay unknown.
public static class MatchModeRules {
    public static string Name(ushort duty)=>duty switch {
        835 or 836 or 837 or 912 or 967 or 1046 or 1102=>"练习赛",
        >=838 and <=843 or >=847 and <=861 or >=913 and <=922 or >=968 and <=977 or >=1047 and <=1056 or >=1103 and <=1112=>"段位赛",
        862 or 863 or 864 or 923 or 978 or 1057 or 1113=>"自定赛",
        _=>"未知"
    };
}
