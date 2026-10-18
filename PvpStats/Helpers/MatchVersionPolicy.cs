using System;

namespace PvpStats.Helpers;
public static class MatchVersionPolicy {
    public const int LegacyValidationRevision = 1;
    public static bool NeedsLegacyValidation(int savedRevision) => savedRevision < LegacyValidationRevision;
    public static bool NeedsLegacyDirectorWarning(string? recorder, string? pluginVersion, DateTime gameVersionDate) {
        // CCStats uses its own version sequence; it includes upstream 2.7.1's director fixes.
        if(string.Equals(recorder,"CCStats",StringComparison.Ordinal)) return false;
        return Version.TryParse(pluginVersion,out var version) && version < new Version(2,5,3,0)
            && gameVersionDate >= new DateTime(2025,5,17);
    }
}
