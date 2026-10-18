using System.Buffers.Binary;
using System.IO;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CCStats.Core.Capture;
using PvpStats.Analysis;

namespace CCStats.Desktop.Capture;

internal sealed class ExternalGameReader : IObservedGameSource {
    private readonly Process _process;
    private readonly SafeProcessHandle _handle;
    private readonly long _objects,_gameMain,_eventFramework,_profileAddress,_atkStage;
    private readonly string _version;
    private readonly byte[] _table=new byte[ClientLayout.ObjectSlots*8],_header=new byte[ClientLayout.HeaderBytes],_statuses=new byte[ClientLayout.StatusBytes],_world=new byte[2],_territory=new byte[4];
    private readonly byte[] _pointer=new byte[8],_nameId=new byte[4],_cast=new byte[ClientLayout.CastBytes],_effects=new byte[ClientLayout.EffectBytes],_roster=new byte[ClientLayout.DirectorPlayerBytes*10],_progress=new byte[ClientLayout.DirectorProgressBytes],_directorType=new byte[1],_mapStates=new byte[0x608];
    private readonly Dictionary<uint,(ushort World,DateTime At,List<ReplayBuff> Statuses)> _cache=new();
    private readonly ActionEffectJournal _journal=new();
    private readonly Dictionary<uint,ReplayMapEvent> _casts=new();
    private Dictionary<uint,DirectorPlayer> _teams=new();
    private readonly Dictionary<uint,(ushort State,byte Flags)> _mapEffects=new();
    private uint _lastTerritory;private long _lastDirector;
    private readonly byte[] _units=new byte[0x810],_unit=new byte[0x238];
    private DateTime _lastUiRead,_lastArenaSeen;
    private readonly Dictionary<string,string> _panelHashes=new();
    private readonly byte[] _profileBytes=new byte[0x8C];
    private DateTime _lastProfileRead;private PlayerPvpProfile? _profile;
    private DateTime _lastDetail;private DirectorSnapshot? _lastProgress;
    public ObservationBatch? Telemetry {get;private set;}
    public int ProcessId=>_process.Id;
    internal static int FindGameProcess() {
        var processes=Process.GetProcessesByName("ffxiv_dx11");
        try {
            if(processes.Length==0)return 0;
            GetWindowThreadProcessId(GetForegroundWindow(),out var active);
            return processes.FirstOrDefault(p=>p.Id==active)?.Id??processes.OrderBy(p=>p.Id).First().Id;
        } finally {foreach(var process in processes)process.Dispose();}
    }
    public ExternalGameReader(int processId) {
        _process=Process.GetProcessById(processId);
        if(_process.ProcessName!="ffxiv_dx11")throw new InvalidOperationException("所选进程不是游戏客户端。");
        _handle=OpenProcess(0x10|0x1000,false,_process.Id);
        try {
            if(_handle.IsInvalid)throw new InvalidOperationException("无法读取游戏进程，请确保软件与游戏使用相同用户权限。");
            var module=_process.MainModule??throw new InvalidOperationException("无法读取客户端模块信息。");
            var versionPath=Path.Combine(Path.GetDirectoryName(module.FileName)!,"ffxivgame.ver");
            _version=File.Exists(versionPath)&&new FileInfo(versionPath).Length<128?File.ReadAllText(versionPath).Trim():FileVersionInfo.GetVersionInfo(module.FileName).FileVersion??"";
            using var stream=File.OpenRead(module.FileName);using var pe=new PEReader(stream);
            var section=pe.PEHeaders.SectionHeaders.Single(s=>s.Name==".text");
            var bytes=pe.GetSectionData(section.VirtualAddress).GetContent().ToArray();
            long Resolve(string signature) {
                int offset=ClientLayout.FindUnique(bytes,signature);
                long rva=section.VirtualAddress+offset+7L+BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset+3,4));
                if(rva<=0||rva>=module.ModuleMemorySize)throw new InvalidDataException("客户端静态地址超出模块范围。");
                return module.BaseAddress.ToInt64()+rva;
            }
            _objects=Resolve(ClientLayout.ObjectsSignature);_gameMain=Resolve(ClientLayout.GameMainSignature);
            // Optional channel failure must not kill pose/HP recording.
            try {_eventFramework=Resolve(ClientLayout.EventFrameworkSignature);}catch(InvalidDataException) {_eventFramework=0;}
            try {_profileAddress=Resolve(ClientLayout.PvpProfileSignature);}catch(InvalidDataException) {_profileAddress=0;}
            try {_atkStage=Resolve(ClientLayout.AtkStageSignature);}catch(InvalidDataException) {_atkStage=0;}
        } catch {_handle.Dispose();_process.Dispose();throw;}
    }
    private bool Read(long address,byte[] buffer)=>address>0x10000&&address<0x0000800000000000-buffer.Length&&ReadProcessMemory(_handle,(nint)address,buffer,buffer.Length,out var size)&&size==(nuint)buffer.Length;
    private long Pointer(long address)=>Read(address,_pointer)?BinaryPrimitives.ReadInt64LittleEndian(_pointer):0;
    public (uint Territory,MovementFrame Frame) Sample() {
        if(_process.HasExited)throw new InvalidOperationException("游戏进程已退出。");
        if(!Read(_gameMain+ClientLayout.TerritoryOffset,_territory)||!Read(_objects+ClientLayout.ObjectArrayOffset,_table))throw new InvalidDataException("客户端地址不可读，请重新连接或等待版本适配。");
        uint territory=BinaryPrimitives.ReadUInt32LittleEndian(_territory);if(territory>100000)throw new InvalidDataException("区域编号异常，已停止采集。");
        if(territory!=_lastTerritory) {Reset();_lastTerritory=territory;}
        var now=DateTime.UtcNow;var frame=new MovementFrame {Timestamp=now,AstraProgress=-1,UmbraProgress=-1};var batch=new ObservationBatch {GameVersion=_version};Telemetry=batch;
        bool arena=ReplayMapMesh.MapKey(territory)!=null,detail=arena&&(now-_lastDetail).TotalSeconds>=.2;
        if(detail) {ReadDirector(batch);_lastDetail=now;}
        batch.DirectorAvailable=_lastProgress!=null;
        if(_lastProgress is {} progress) {frame.CrystalProgress=progress.CrystalProgress;frame.AstraProgress=progress.AstraProgress;frame.UmbraProgress=progress.UmbraProgress;}
        var actors=new Dictionary<uint,ObservedActor>();var addresses=new Dictionary<uint,long>();var crystals=new List<CrystalPosition>();
        // All loaded object slots, including NPCs and event objects outside the camera view.
        for(int slot=0;slot<ClientLayout.ObjectSlots;slot++) {
            long address=BinaryPrimitives.ReadInt64LittleEndian(_table.AsSpan(slot*8,8));if(!Read(address,_header))continue;
            byte kind=_header[0x90];if(kind is not (1 or 2 or 7))continue;
            uint id=U(_header,0x78);
            float x=F(_header,0xB0),y=F(_header,0xB4),z=F(_header,0xB8),heading=F(_header,0xC0);
            if(!Finite(x,y,z,heading))continue;
            uint nameId=kind==2&&Read(address+0x2338,_nameId)?U(_nameId,0):0;
            if(detail&&ArenaObjectDecoder.Decode(_header,nameId,territory,slot,now) is {} arenaObject)batch.ArenaObjects.Add(arenaObject);
            if(kind==7||id is 0 or 0xE0000000||actors.ContainsKey(id))continue;
            PlayerPosition? player=null;
            if(kind==1) {
                if(!_cache.TryGetValue(id,out var meta)||(now-meta.At).TotalSeconds>=.2) {
                    if(!Read(address+ClientLayout.WorldOffset,_world)||!Read(address+ClientLayout.StatusOffset,_statuses))continue;
                    ushort world=BinaryPrimitives.ReadUInt16LittleEndian(_world);
                    var parsed=ClientLayout.DecodePlayer(_header,world,_statuses,now);if(parsed==null)continue;
                    meta=(world,now,parsed.Statuses);_cache[id]=meta;
                }
                player=ClientLayout.DecodePlayer(_header,meta.World,ReadOnlySpan<byte>.Empty,meta.At);if(player==null)continue;
                player.Statuses=meta.Statuses;if(_teams.TryGetValue(id,out var team))player.Team=team.Team;
                foreach(var status in player.Statuses.Where(s=>DetailedAnalysis.Controls.ContainsKey(s.Id)))player.Controls.Add(new(){StatusId=status.Id,Remaining=Math.Max(0,status.Remaining-(float)(now-meta.At).TotalSeconds)});
                if(_teams.Count>0) {if(player.Team is 1 or 2)frame.Players.Add(player);}
                else if(frame.Players.Count<10)frame.Players.Add(player);else batch.UnconfirmedRosterLimited=true;
                if(slot==0)batch.LocalPlayer=player.Player;
            }
            actors[id]=new(id,U(_header,0x88),nameId,kind,x,y,z,heading,player);addresses[id]=address;
            if(kind==2&&nameId==11350)crystals.Add(new(){X=x,Y=y,Z=z});

        }
        if(crystals.Count==1)frame.Crystal=crystals[0];
        if(arena) {
            foreach(var pair in actors) {
                var actor=pair.Value;if(actor.Kind==7||actor.NameId==11350)continue;
                int before=batch.Skills.Count;
                if(Read(addresses[pair.Key]+ClientLayout.EffectOffset,_effects))_journal.Observe(pair.Key,_effects,actors,now,batch);
                if((detail||batch.Skills.Count>before)&&Read(addresses[pair.Key]+ClientLayout.CastOffset,_cast)) {
                    if(detail)ReadCast(actor,now,batch);
                    foreach(var skill in batch.Skills.Skip(before)) {
                        if(skill.SourceEntityId!=actor.EntityId||U(_cast,0x4C)!=skill.Sequence||U(_cast,0x48)!=skill.ActionId||U(_cast,4)!=skill.ActionId)continue;
                        if(skill.ActionId is 29266 or 29267 or 29673 or 29678&&Finite(F(_cast,0x20),F(_cast,0x24),F(_cast,0x28),F(_cast,0x30))) {
                            skill.GroundX=F(_cast,0x20);skill.GroundY=F(_cast,0x24);skill.GroundZ=F(_cast,0x28);skill.Heading=F(_cast,0x30);
                        }
                    }
                }
            }
            if(detail) {
                batch.Summons=new(){Timestamp=now};
                foreach(var actor in actors.Values.Where(a=>a.Kind==2&&ReplaySummons.IsPetName(a.NameId)).Take(10))
                    batch.Summons.Summons.Add(new(){EntityId=actor.EntityId,NameId=actor.NameId,Owner=actors.GetValueOrDefault(actor.OwnerId)?.Player?.Player??"",Phoenix=actor.NameId==8228,X=actor.X,Y=actor.Y,Z=actor.Z});
            }
        }
        if(batch.LocalPlayer.Length>0&&_profile!=null&&_profile.Player!=batch.LocalPlayer) {_profile=null;_lastProfileRead=DateTime.MinValue;}
        if(batch.LocalPlayer.Length>0&&now-_lastProfileRead>=TimeSpan.FromSeconds(2)) {
            _lastProfileRead=now;
            if(Read(_profileAddress,_profileBytes)&&PlayerPvpProfile.Decode(_profileBytes,batch.LocalPlayer,now) is {} profile) {
                if(_profile==null||profile with {ObservedAt=_profile.ObservedAt}!=_profile)_profile=profile;
            } else _profile=null;
        }
        batch.Profile=_profile;
        if(arena)_lastArenaSeen=now;
        if(now-_lastArenaSeen<TimeSpan.FromSeconds(60)&&now-_lastUiRead>=TimeSpan.FromSeconds(2)) {_lastUiRead=now;ReadResultPanels(now,batch);}
        if(_cache.Count>1000)_cache.Clear();return (territory,frame);
    }
    private void ReadDirector(ObservationBatch batch) {
        _lastProgress=null;_teams.Clear();
        long framework=Pointer(_eventFramework);long director=framework>0?Pointer(framework+ClientLayout.ActiveDirectorOffset):0;
        if(director!=_lastDirector) {_mapEffects.Clear();_lastDirector=director;}
        if(director==0||!Read(director+ClientLayout.DirectorTypeOffset,_directorType)||_directorType[0]!=5)return;
        long roster=Pointer(director+ClientLayout.DirectorPlayerPointerOffset);
        if(!Read(roster,_roster)||DirectorDecoder.Roster(_roster) is not {} players||!Read(director+ClientLayout.DirectorProgressOffset,_progress)||DirectorDecoder.Progress(_progress) is not {} progress)return;
        _teams=players.ToDictionary(p=>p.EntityId);_lastProgress=progress;batch.DirectorAvailable=true;
        // Native managed map-effect state persists after the activating event, including bridge timelines.
        if(Read(director+0xDE0,_mapStates)) {
            int count=BinaryPrimitives.ReadUInt16LittleEndian(_mapStates.AsSpan(0x602));if(count>128)return;
            for(int i=0;i<count;i++) {var row=_mapStates.AsSpan(i*12,12);uint id=U(row,0);if(id==0)continue;
                ushort state=BinaryPrimitives.ReadUInt16LittleEndian(row[8..]);byte flags=row[10];
                if(!_mapEffects.TryGetValue(id,out var old)||old!=(state,flags)) {batch.MapStates.Add(new(DateTime.UtcNow,id,state,flags));_mapEffects[id]=(state,flags);}
            }
        }
    }
    private void ReadCast(ObservedActor actor,DateTime now,ObservationBatch batch) {
        if((_cast[0]&1)==0||_cast[1]!=1)return;
        uint action=U(_cast,4);float duration=F(_cast,0x3C),elapsed=F(_cast,0x34);
        if(action==0||!float.IsFinite(duration)||!float.IsFinite(elapsed)||duration is <=0 or >600||elapsed<0||elapsed>duration+.5f)return;
        if(actor.Kind!=2||actor.OwnerId is not (0 or 0xE0000000)||MapMechanicVisuals.Find(action) is not {} rule)return;
        var end=now.AddSeconds(Math.Max(0,duration-elapsed));
        if(_casts.TryGetValue(actor.EntityId,out var previous)&&previous.ActionId==action&&now-(previous.ObservedUntil??previous.Timestamp)<TimeSpan.FromSeconds(.4)&&Math.Abs((end-(previous.ExpectedEndTime??end)).TotalSeconds)<.5) {previous.ObservedUntil=now;return;}
        var entry=new ReplayMapEvent {Timestamp=now,ObservedUntil=now,ExpectedEndTime=end,Kind=ReplayMapEventKind.CastObserved,ActionId=action,SourceEntityId=actor.EntityId,SourceNameId=actor.NameId,Label=rule.Name,X=actor.X,Y=actor.Y,Z=actor.Z,Heading=float.IsFinite(F(_cast,0x30))?F(_cast,0x30):actor.Heading};
        if(rule.Ground&&Finite(F(_cast,0x20),F(_cast,0x24),F(_cast,0x28),0)) {entry.TargetX=F(_cast,0x20);entry.TargetY=F(_cast,0x24);entry.TargetZ=F(_cast,0x28);}
        _casts[actor.EntityId]=entry;batch.MapEvents.Add(entry);
    }
    private void ReadResultPanels(DateTime now,ObservationBatch batch) {
        long stage=Pointer(_atkStage),manager=stage>0?Pointer(stage+0x20):0;
        if(manager==0||!Read(manager+0x6900,_units))return;
        int count=BinaryPrimitives.ReadUInt16LittleEndian(_units.AsSpan(0x808));if(count>256)return;
        for(int i=0;i<count;i++) {
            long unit=BinaryPrimitives.ReadInt64LittleEndian(_units.AsSpan(8+i*8));if(!Read(unit,_unit))continue;
            var nameBytes=_unit.AsSpan(8,32);int end=nameBytes.IndexOf((byte)0);if(end<1)continue;
            var name=System.Text.Encoding.ASCII.GetString(nameBytes[..end]);
            if(!name.StartsWith("PvPResult",StringComparison.Ordinal)&&!name.StartsWith("PvPColosseumResult",StringComparison.Ordinal))continue;
            if((U(_unit,0x198)&(2u<<20))==0)continue;
            int length=BinaryPrimitives.ReadUInt16LittleEndian(_unit.AsSpan(0x1E2));if(length is <1 or >2048)continue;
            long address=BinaryPrimitives.ReadInt64LittleEndian(_unit.AsSpan(0x178));var bytes=new byte[length*16];if(!Read(address,bytes))continue;
            var values=ClientUiDecoder.Decode(bytes,length,(pointer,wide)=> {
                var text=new byte[512];if(!Read(pointer,text))return "";
                if(wide) {int n=0;while(n+1<text.Length&&(text[n]!=0||text[n+1]!=0))n+=2;return System.Text.Encoding.Unicode.GetString(text,0,n);}
                int n8=Array.IndexOf(text,(byte)0);return System.Text.Encoding.UTF8.GetString(text,0,n8<0?text.Length:n8);
            });
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values)));
            if(_panelHashes.GetValueOrDefault(name)==hash)continue;_panelHashes[name]=hash;
            batch.SettlementPanels.Add(new(now,name,values));
        }
    }
    private void Reset() {_cache.Clear();_journal.Reset();_casts.Clear();_teams.Clear();_mapEffects.Clear();_lastDirector=0;_lastProgress=null;_lastDetail=DateTime.MinValue;}
    private static uint U(ReadOnlySpan<byte> data,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    private static float F(ReadOnlySpan<byte> data,int at)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[at..]));
    private static bool Finite(float x,float y,float z,float heading)=>float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z)&&float.IsFinite(heading)&&MathF.Abs(x)<100000&&MathF.Abs(y)<100000&&MathF.Abs(z)<100000;
    public void Dispose() {_handle.Dispose();_process.Dispose();}
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint processId);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int processId);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,[Out]byte[] buffer,int size,out nuint read);
}
