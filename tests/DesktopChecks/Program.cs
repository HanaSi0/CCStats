using CCStats.Core;
using CCStats.Core.Import;
using CCStats.Core.Capture;
using PvpStats.Analysis;
using System.Text.Json;
using System.Buffers.Binary;
using System.Text;
int passed=0;
void Check(bool ok,string message) {if(!ok)throw new Exception(message);passed++;}
async Task Reject(Func<Task> operation,string message) {
    bool rejected=false;try {await operation();}catch(Exception e) when(e is InvalidDataException or JsonException or OperationCanceledException) {rejected=true;}
    Check(rejected,message);
}
var directory=Path.Combine(Path.GetTempPath(),"ccstats-desktop-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
try {
    Check(!typeof(ReplayFileReader).Assembly.GetReferencedAssemblies().Any(a=>a.Name!.Contains("Dalamud")||a.Name!.Contains("FFXIVClientStructs")),"core must not load game host dependencies");
    var maps=typeof(ReplayMapMesh).Assembly.GetManifestResourceNames().Where(n=>n.EndsWith(".gz")).ToArray();
    Check(maps.Length==14,"all 14 map/ground resources are available to standalone core");
    var path=Path.Combine(directory,"replay.json");
    var original="""
    {"MatchId":"507f1f77bcf86cd799439011","TerritoryId":1060,"StartUtc":"2026-10-18T00:00:00Z","MatchEndTime":"2026-10-18T00:04:00Z","Telemetry":{"Frames":[{"Players":[{"Player":"Full Player Name Ixion"}]},{"Players":[{"Player":"Other Player Titan"},{"Player":"Full Player Name Ixion"}]}],"Effects":[{}],"ReplaySkills":[],"MapMechanics":[]}}
    """;
    await File.WriteAllTextAsync(path,original);
    var summary=await ReplayFileReader.ReadAsync(path);
    Check(summary.MapName=="九霄云上"&&summary.DurationLabel=="4:00"&&summary.Frames==2&&summary.Effects==1,"plugin replay export loads map, duration and actual data counts");
    Check(summary.Players.Count==2&&summary.Players.Contains("Full Player Name Ixion"),"complete names survive import and repeated appearances deduplicate");
    Check(await File.ReadAllTextAsync(path)==original&&!summary.IsObservation,"import never rewrites the original plugin export");
    foreach(var bad in new[]{"{}","[]",original.Replace("\"TerritoryId\":1060","\"TerritoryId\":\"bad\""),original.Replace("\"Frames\":[","\"Frames\":null,\"unused\":["),original.Replace("2026-10-18T00:04:00Z","2026-10-17T00:00:00Z"),original.Replace("507f1f77bcf86cd799439011","invalid")}) {
        var invalid=Path.Combine(directory,"invalid.json");await File.WriteAllTextAsync(invalid,bad);
        await Reject(async ()=>{await ReplayFileReader.ReadAsync(invalid);},"invalid replay format must be rejected");
    }
    var huge=Path.Combine(directory,"large.json");using(var stream=File.Create(huge))stream.SetLength(ReplayFileReader.MaximumBytes+1);
    await Reject(async ()=>{await ReplayFileReader.ReadAsync(huge);},"oversized input is rejected before JSON allocation");
    using(var cancel=new CancellationTokenSource()) {cancel.Cancel();await Reject(async ()=>{await ReplayFileReader.ReadAsync(path,cancel.Token);},"cancelled import stops parsing");}
    Check(ReplayFileReader.MapName(99999).Contains("未知"),"unknown map IDs remain explicit rather than selecting a guessed map");
    var preferences=new DesktopPreferences();for(int i=0;i<12;i++)preferences.Remember(Path.Combine(directory,i+".json"));preferences.Remember(path);
    Check(preferences.RecentFiles.Count==10&&preferences.RecentFiles[0]==path,"recent list is bounded and newest first");
    preferences.Remember(path);Check(preferences.RecentFiles.Count==10,"reopening a file cannot duplicate recent paths");
    var settings=Path.Combine(directory,"config","settings.json");preferences.Save(settings);
    Check(DesktopPreferences.Load(settings).RecentFiles.SequenceEqual(preferences.RecentFiles),"preferences survive atomic save/load");
    Check(!Directory.GetFiles(Path.GetDirectoryName(settings)!,"*.tmp").Any(),"settings save leaves no temporary files");
    File.WriteAllText(settings,"invalid JSON");Check(DesktopPreferences.Load(settings).RecentFiles.Count==0,"invalid preferences recover without touching replay files");
    Check(ClientLayout.FindUnique(new byte[]{0,0x48,0x8D,0x35,1,2,3,4,0x81,0xFA,0},ClientLayout.ObjectsSignature)==1,"signature matcher supports RIP-relative wildcards");
    await Reject(()=>Task.Run(()=>ClientLayout.FindUnique(new byte[]{0},ClientLayout.ObjectsSignature)),"missing signature cannot start capture");
    await Reject(()=>Task.Run(()=>ClientLayout.FindUnique(new byte[]{0x48,0x48},"48")),"ambiguous signatures cannot start capture");
    var header=new byte[ClientLayout.HeaderBytes];header[0x90]=1;Encoding.UTF8.GetBytes("Test Player").CopyTo(header,0x30);header[0x1CA]=24;
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x78),100);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x1AC),9000);BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x1B0),10000);
    BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(0xB0),12);BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(0xB4),3);BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(0xB8),-9);
    var statuses=new byte[ClientLayout.StatusBytes];BinaryPrimitives.WriteUInt16LittleEndian(statuses.AsSpan(8),3054);BinaryPrimitives.WriteSingleLittleEndian(statuses.AsSpan(12),4.5f);
    var player=ClientLayout.DecodePlayer(header,21,statuses,DateTime.UtcNow)!;
    Check(player.X==12&&player.Y==3&&player.Z==-9&&player.HP==9000&&player.ClassJobId==24,"external player layout decodes coordinates, HP and job from a byte fixture");
    Check(player.Team==0&&player.Player=="Test Player [世界 21]"&&player.Statuses.Single().Id==3054,"unknown teams/world names stay explicit; status IDs are captured");
    header[0x90]=2;Check(ClientLayout.DecodePlayer(header,21,statuses,DateTime.UtcNow)==null,"NPC memory cannot be interpreted as player layout");header[0x90]=1;
    BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(0xB0),float.NaN);Check(ClientLayout.DecodePlayer(header,21,statuses,DateTime.UtcNow)==null,"invalid positions reject an incompatible snapshot");
    string observation;
    using(var writer=new ObservationWriter(directory,1034)) {
        observation=writer.Destination;
        Check(File.Exists(observation+".partial")&&!File.Exists(observation),"an unfinished recording cannot appear as a complete JSON replay");
        for(int i=0;i<120;i++)writer.Append(1034,new MovementFrame {Timestamp=DateTime.UtcNow,Players=new(){player}});
        Check(writer.Frames==120,"streaming writer retains every appended observation");
        bool changed=false;try {writer.Append(1033,new MovementFrame());}catch(InvalidOperationException) {changed=true;}
        Check(changed,"map change terminates a segment rather than mixing territories");
    }
    var observed=await ReplayFileReader.ReadAsync(observation);
    Check(observed.IsObservation&&observed.Frames==120&&observed.Effects==0,"independent observation is importable and does not invent effect packets");
    Check(!File.Exists(observation+".partial"),"finished observation is atomically renamed to the usable export");
    await PortChecks.Run(Check);
    Console.WriteLine($"Passed {passed} standalone core/import/capture fixture checks.");
} finally {Directory.Delete(directory,true);}
