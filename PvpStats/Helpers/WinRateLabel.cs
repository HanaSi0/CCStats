using System;

namespace PvpStats.Helpers;

public static class WinRateLabel {
    public static double Rate(int wins, int losses) => wins + (long)losses > 0 ? (double)wins / (wins + (long)losses) : 0;
    public static string Format(int wins, int losses) => wins + (long)losses > 0
        ? $"{Rate(wins, losses):P1} · {wins:N0}/{wins + (long)losses:N0}" : "— · 0/0";
}
