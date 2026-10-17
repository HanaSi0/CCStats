namespace PvpStats.Analysis;
public static class ReplayCrystalHud {
    public static float? Progress(int? raw)=>raw is >=0 and <=1000?raw.Value/1000f:null;
    public static float? Position(int? raw)=>raw is >=-1000 and <=1000?.5f-raw.Value/2000f:null;
    public static float? Checkpoint(int? raw)=>raw is >=0 and <=100?raw.Value/100f:null;
}
