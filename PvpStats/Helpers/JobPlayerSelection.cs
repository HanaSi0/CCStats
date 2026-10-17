using System;
namespace PvpStats.Helpers;
public static class JobPlayerSelection {
    // Null follows the local participant saved in each match, never every player.
    public static bool Includes(string? selected, string candidate, bool isLocal) =>
        selected is null ? isLocal : string.Equals(selected,candidate,StringComparison.OrdinalIgnoreCase);
}
