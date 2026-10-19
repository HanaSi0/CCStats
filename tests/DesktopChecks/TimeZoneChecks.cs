using System.Text.Json;
using CCStats.Core.Library;
using PvpStats.Analysis;
internal static class TimeZoneChecks {
    internal static async Task Run(Action<bool,string> check) {
        var directory=Path.Combine(Path.GetTempPath(),"ccstats-global-time-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            var start=new DateTimeOffset(2026,3,8,6,59,30,TimeSpan.Zero);var end=start.AddSeconds(60);
            foreach(var name in new[]{"UTC","Asia/Shanghai","Asia/Tokyo","Asia/Kathmandu","America/New_York","America/St_Johns","Pacific/Kiritimati","Pacific/Honolulu"}) {
                var zone=TimeZoneInfo.FindSystemTimeZoneById(name);var a=TimeZoneInfo.ConvertTime(start,zone);var b=TimeZoneInfo.ConvertTime(end,zone);
                await Verify(a.ToString("O"),b.ToString("O"),start.UtcDateTime,name);
            }
            await Verify("2026-11-01T01:59:30-04:00","2026-11-01T01:00:30-05:00",new DateTime(2026,11,1,5,59,30,DateTimeKind.Utc),"DST autumn repeated hour");
            await Verify("2026-03-08T06:59:30","2026-03-08T07:00:30",start.UtcDateTime,"historical offset-free UTC");
            var foldStart=new DateTime(2026,11,1,5,59,30,DateTimeKind.Utc);var foldEnd=foldStart.AddSeconds(60);
            var localFrames=new List<MovementFrame>{new(){Timestamp=foldStart.ToLocalTime(),Players=new(){new(){Player="Local Player",StatusTimestamp=foldStart.ToLocalTime()}}},new(){Timestamp=foldEnd.ToLocalTime(),Players=new()}};
            var normalized=MovementCodec.Decode(MovementCodec.Encode(localFrames));
            check(normalized[0].Timestamp==foldStart&&normalized[0].Timestamp.Kind==DateTimeKind.Utc&&normalized[0].Players[0].StatusTimestamp==foldStart&&normalized[1].Timestamp==foldEnd,"local-kind compression preserves real UTC elapsed time across DST fall-back");
            var encoded=MovementCodec.Encode(new List<MovementFrame>{new(){Timestamp=foldStart}});
            using var packedInput=new MemoryStream(encoded);using var gzip=new System.IO.Compression.GZipStream(packedInput,System.IO.Compression.CompressionMode.Decompress);using var raw=new MemoryStream();gzip.CopyTo(raw);var legacy=raw.ToArray();System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(legacy.AsSpan(10,8),foldStart.ToLocalTime().ToBinary());
            using var output=new MemoryStream();using(var zip=new System.IO.Compression.GZipStream(output,System.IO.Compression.CompressionLevel.Optimal,true))zip.Write(legacy);
            var recovered=MovementCodec.Decode(output.ToArray())[0].Timestamp;check(recovered==foldStart&&recovered.Kind==DateTimeKind.Utc,"historical binary local origins recover their absolute instant in every viewer timezone");
            async Task Verify(string a,string b,DateTime utc,string name) {
                var path=Path.Combine(directory,"input.json");
                var json=$$"""
                {"RecordKind":"IndependentObservation","MatchId":"507f1f77bcf86cd799439066","TerritoryId":1033,"StartUtc":"{{a}}","MatchEndTime":"{{b}}","Telemetry":{"Version":3,"Frames":[{"Timestamp":"{{a}}","Players":[{"Player":"Global Player Ixion","X":12,"Y":3,"Z":-5,"HP":100,"MaxHP":100,"ClassJobId":19,"StatusTimestamp":"{{a}}"}]},{"Timestamp":"{{b}}","Players":[]}],"Effects":[{"Timestamp":"{{a}}","Actor":"Global Player Ixion"}],"ReplaySkills":[{"Timestamp":"{{a}}","ObservedUntil":"{{b}}"}],"MapMechanics":[{"Timestamp":"{{a}}","ExpectedEndTime":"{{b}}"}]},"Timeline":[{"Timestamp":"{{a}}","Kind":"技能"}],"Knockouts":[{"Timestamp":"{{a}}","Victim":"Global Player Ixion"}]}
                """;
                await File.WriteAllTextAsync(path,json);var bundle=await ReplayImport.ReadAsync(path);
                check(bundle.Match.StartUtc==utc&&bundle.Match.StartUtc.Kind==DateTimeKind.Utc&&bundle.Match.DurationSeconds==60,"global import preserves instant and duration: "+name);
                check(bundle.Data.Effects[0].Timestamp==utc&&bundle.Data.MapMechanics[0].Timestamp==utc&&bundle.Data.ReplaySkills[0].Timestamp==utc&&bundle.Timeline.All(t=>t.Timestamp==utc),"every event channel shares the movement UTC timeline: "+name);
                using(var library=new RecordLibrary(Path.Combine(directory,"library"))){library.Save(bundle);var loaded=library.Load(bundle.Match.Id);check(loaded.Data.Frames[0].Timestamp==utc&&ReplaySceneBuilder.Build(loaded,null,loaded.Match.StartUtc.AddMilliseconds(50),false,false).Length>0,"database and packed replay preserve global positions: "+name);var export=Path.Combine(directory,"export.json");await ReplayImport.ExportAsync(loaded,export);using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(export));check(doc.RootElement.GetProperty("Telemetry").GetProperty("Frames")[0].GetProperty("Timestamp").GetString()!.EndsWith('Z'),"exports carry explicit UTC independent of the viewer: "+name);}
            }
        } finally {Directory.Delete(directory,true);}
    }
}
