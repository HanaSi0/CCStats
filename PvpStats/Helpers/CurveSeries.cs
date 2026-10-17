namespace PvpStats.Helpers;
public static class CurveSeries {
    public static bool HasPoints(float[]? x,float[]? y) => x!=null&&y!=null&&x.Length>0&&x.Length==y.Length;
}
