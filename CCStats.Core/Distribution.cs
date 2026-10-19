namespace CCStats.Core;
public static class Distribution {
#if CCSTATS_CHINA
    public static bool IsChina {get;}=true;
#else
    public static bool IsChina {get;}=false;
#endif
    public static string Edition=>IsChina?"China":"Global";
    public static string DataDirectoryName=>IsChina?"CCStatsDesktop-CN":"CCStatsDesktop";
}
