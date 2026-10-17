using PvpStats.Analysis;
using PvpStats.Helpers;
using LiteDB;
using System.Numerics;
internal static class MovementReplayChecks {
    internal static void Run(Action<bool,string> check) {
        check(!CurveSeries.HasPoints(null,null)&&!CurveSeries.HasPoints(Array.Empty<float>(),Array.Empty<float>()),"missing or empty curves never reach native index-zero plot access");
        check(!CurveSeries.HasPoints(new[]{1f},Array.Empty<float>())&&CurveSeries.HasPoints(new[]{1f},new[]{2f}),"curve rendering requires paired coordinate samples");
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var data=new DetailedTelemetry { Version=3,SampleIntervalSeconds=.05 };
        var names=Enumerable.Range(0,10).Select(i=>"Example Player "+i+"@ExampleWorld").ToArray();
        for(int i=0;i<24000;i++) {
            var f=new MovementFrame { Timestamp=start.AddMilliseconds(i*50),CrystalProgress=i%2000-1000,AstraProgress=i%1000,UmbraProgress=i%1000,Crystal=new() { X=i*.001f,Z=MathF.Sin(i*.002f)*10 } };
            for(int j=0;j<10;j++) f.Players.Add(new() { Player=names[j],Team=j<5?1:2,X=MathF.Sin(i*.01f+j)*70,Z=MathF.Cos(i*.01f+j)*70,Heading=j*.3f,HP=(uint)(60000-i%1000),MaxHP=60000,
                Controls=j==0?new() { new() { StatusId=1343,Remaining=2,Source=names[7] } }:new() });
            data.Frames.Add(f);
        }
        data.PrepareForStorage();
        check(data.Frames.Count==24000&&data.SampleIntervalSeconds==.05&&!data.MovementDownsampled,"20 minutes of ten-player 20 Hz movement fits the compressed storage budget without losing samples");
        check(data.PackedFrames.Length<4*1024*1024,"compressed high-frequency movement leaves room for combat analytics in the database document");
        var doc=BsonMapper.Global.ToDocument(data);
        check(doc["Frames"].IsNull&&doc["PackedFrames"].IsBinary,"new captures store one compact binary payload instead of duplicating full BSON position arrays");
        var loaded=BsonMapper.Global.ToObject<DetailedTelemetry>(doc);
        check(loaded.Frames.Count==24000&&loaded.Frames[37].Timestamp==start.AddMilliseconds(1850),"high-frequency movement survives persistent LiteDB roundtrip with exact timestamps");
        check(loaded.Frames[37].Players[0].Controls[0].Source==names[7]&&loaded.Frames[37].Players[0].X==data.Frames[37].Players[0].X,"movement compression preserves control sources and coordinates exactly");
        check(loaded.Frames[37].Crystal?.X==data.Frames[37].Crystal?.X,"the crystal's observed world coordinates persist beside player movement");
        var final=loaded.Frames[^1];
        check(DetailedAnalysis.FrameAt(loaded,final.Timestamp.AddMilliseconds(100))==final&&DetailedAnalysis.FrameAt(loaded,final.Timestamp.AddMilliseconds(200))==null,"high-frequency replay does not hold stale positions through missing observation gaps");
        var old=new DetailedTelemetry { Version=2,Frames=new() { new() { Timestamp=start,Players=new() { new() { Player="Legacy Player",X=12,Z=34 } } } } };
        var oldRoundtrip=BsonMapper.Global.ToObject<DetailedTelemetry>(BsonMapper.Global.ToDocument(old));
        check(oldRoundtrip.Frames.Count==1&&oldRoundtrip.Frames[0].Players[0].X==12&&oldRoundtrip.Frames[0].Crystal==null,"legacy uncompressed records remain readable and do not invent crystal coordinates");
        check(MovementCodec.Decode(MovementCodec.Encode(Array.Empty<MovementFrame>())).Count==0,"empty position recordings encode and load safely");
        bool Invalid(Action action) { try { action();return false; } catch(InvalidDataException) { return true; } }
        check(Invalid(()=>MovementCodec.Decode(new byte[]{1,2,3,4})),"damaged compressed movement is rejected instead of interpreted as valid positions");
        check(Invalid(()=>MovementCodec.Encode(Enumerable.Repeat(new MovementFrame(),MovementCodec.MaxFrames+1).ToList())),"oversized movement counts are rejected before allocation-heavy serialization");
        var camera=new MapCamera(); camera.Fit(new[]{new Vector2(.4f,.4f),new Vector2(.6f,.6f)});
        check(camera.Zoom>3&&Vector2.Distance(camera.Center,new(.5f))<.0001,"map opens enlarged around the actual observed arena area");
        var pixel=new Vector2(120,300); var world=camera.Unproject(pixel,600);camera.ZoomAt(1.5f,pixel,600);
        check(Vector2.Distance(camera.Unproject(pixel,600),world)<.00001,"wheel zoom keeps the map point under the cursor stationary");
        var before=camera.Project(world,600);camera.Pan(new Vector2(50,30),600);
        check(Vector2.Distance(camera.Project(world,600),before+new Vector2(50,30))<.001,"dragging map preserves the same coordinate transform for players and the crystal");
        camera.FullMap();check(camera.Zoom==1&&camera.Center==new Vector2(.5f),"full map remains one click away");camera.Reset();check(camera.Zoom>3,"reset restores the enlarged arena view");
        foreach(var screen in new[]{new Vector2(1920,1080),new Vector2(960,540),new Vector2(640,480)}) for(int p=0;p<3;p++) {
            var size=ViewLayout.WindowPreset(p,screen);
            check(size.X<=screen.X&&size.Y<=screen.Y&&size.X>0&&size.Y>0,"window preset fits available logical screen size "+screen+" preset "+p);
        }
        var sanitized=ViewLayout.ClampWindow(new(float.NaN,float.PositiveInfinity),new(1280,720));
        check(float.IsFinite(sanitized.X)&&sanitized.X<=1280&&sanitized.Y<=720,"corrupt saved window dimensions cannot break resizing");
        Console.WriteLine($"20 Hz movement fixture: {data.Frames.Count:N0} frames, {data.PackedFrames.Length:N0} compressed bytes.");
    }
}
