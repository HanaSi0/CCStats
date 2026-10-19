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
    private readonly Process? _process;
    private readonly SafeProcessHandle? _handle;
    private readonly Func<long,byte[],bool>? _fixtureRead;
    private readonly long _objects,_gameMain,_eventFramework,_profileAddress,_atkStage,_localPlayerAddress;
    private readonly string _version="";
    private string _directorStatus="尚未读取",_profileStatus="尚未读取",_uiStatus="尚未读取";
    private readonly byte[] _table=new byte[ClientLayout.ObjectSlots*8],_header=new byte[ClientLayout.HeaderBytes],_statuses=new byte[ClientLayout.StatusBytes],_world=new byte[2],_territory=new byte[4];
    private readonly byte[] _pointer=new byte[8],_nameId=new byte[4],_cast=new byte[ClientLayout.CastBytes],_effects=new byte[ClientLayout.EffectBytes],_roster=new byte[ClientLayout.DirectorPlayerBytes*10],_progress=new byte[ClientLayout.DirectorProgressBytes],_directorType=new byte[1],_mapStates=new byte[0x608];
    private readonly Dictionary<uint,(ushort World,DateTime At,List<ReplayBuff> Statuses,List<ControlSource> Controls)> _cache=new();
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
    public int ProcessId=>_process?.Id??0;
    internal ExternalGameReader(Func<long,byte[],bool> read,long objects,long gameMain,long eventFramework=0,long localPlayerAddress=0) {_fixtureRead=read;_objects=objects;_gameMain=gameMain;_eventFramework=eventFramework;_localPlayerAddress=localPlayerAddress;_version="fixture";}
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
            try {_localPlayerAddress=Resolve(ClientLayout.LocalPlayerSignature);}catch(InvalidDataException) {_localPlayerAddress=0;}
            try {_atkStage=Resolve(ClientLayout.AtkStageSignature);}catch(InvalidDataException) {_atkStage=0;}
        } catch(Exception error) {_handle.Dispose();_process.Dispose();if(_version.Length>0)throw new InvalidDataException($"客户端 {_version}：{error.Message}",error);throw;}
    }
    private bool Read(long address,byte[] buffer)=>_fixtureRead!=null?_fixtureRead(address,buffer):address>0x10000&&address<0x0000800000000000-buffer.Length&&ReadProcessMemory(_handle!,(nint)address,buffer,buffer.Length,out var size)&&size==(nuint)buffer.Length;
    private long Pointer(long address)=>Read(address,_pointer)?BinaryPrimitives.ReadInt64LittleEndian(_pointer):0;
    public (uint Territory,MovementFrame Frame) Sample() {
        if(_process?.HasExited==true)throw new InvalidOperationException("游戏进程已退出。");
        if(!Read(_gameMain+ClientLayout.TerritoryOffset,_territory)||!Read(_objects+ClientLayout.ObjectArrayOffset,_table))throw new InvalidDataException("客户端地址不可读，请重新连接或等待版本适配。");
        uint territory=BinaryPrimitives.ReadUInt32LittleEndian(_territory);if(territory>100000)throw new InvalidDataException("区域编号异常，已停止采集。");
        if(territory!=0&&territory!=_lastTerritory) {Reset();_lastTerritory=territory;}
        var now=DateTime.UtcNow;var frame=new MovementFrame {Timestamp=now,AstraProgress=-1,UmbraProgress=-1};var batch=new ObservationBatch {GameVersion=_version};Telemetry=batch;
        bool arena=ReplayMapMesh.MapKey(territory)!=null,detail=arena&&(now-_lastDetail).TotalSeconds>=.2;
        if(detail) {ReadDirector(batch);_lastDetail=now;}
        batch.DirectorAvailable=_lastProgress!=null;
        if(_lastProgress is {} progress) {frame.CrystalProgress=progress.CrystalProgress;frame.AstraProgress=progress.AstraProgress;frame.UmbraProgress=progress.UmbraProgress;}
        long localAddress=Pointer(_localPlayerAddress);
        int headerReads=0,pcSeen=0,worldFailures=0,statusFailures=0,rejected=0,rosterMatches=0;
        var rejections=new Dictionary<string,int>();
        var sceneIds=new Dictionary<string,int>();
        var actors=new Dictionary<uint,ObservedActor>();var addresses=new Dictionary<uint,long>();var crystals=new List<CrystalPosition>();
        // All loaded object slots, including NPCs and event objects outside the camera view.
        for(int slot=0;slot<ClientLayout.ObjectSlots;slot++) {
            long address=BinaryPrimitives.ReadInt64LittleEndian(_table.AsSpan(slot*8,8));if(!Read(address,_header))continue;headerReads++;
            byte kind=_header[0x90];if(kind is not (1 or 2 or 7))continue;
            uint id=U(_header,0x78);
            float x=F(_header,0xB0),y=F(_header,0xB4),z=F(_header,0xB8),heading=F(_header,0xC0);
            if(!Finite(x,y,z,heading))continue;
            uint nameId=kind==2&&Read(address+0x2338,_nameId)?U(_nameId,0):0;
            if(detail&&kind is 2 or 7) {var key=$"{kind}:{nameId}:{U(_header,0x84)}";sceneIds[key]=sceneIds.GetValueOrDefault(key)+1;}
            if(detail&&ArenaObjectDecoder.Decode(_header,nameId,territory,slot,now) is {} arenaObject)batch.ArenaObjects.Add(arenaObject);
            if(kind==7||id is 0 or 0xE0000000||actors.ContainsKey(id))continue;
            PlayerPosition? player=null;
            if(kind==1) {
                pcSeen++;
                if(!_cache.TryGetValue(id,out var meta)||(now-meta.At).TotalSeconds>=.2) {
                    // Optional world/status pages must never suppress valid pose/HP data.
                    ushort world=meta.World;
                    if(Read(address+ClientLayout.WorldOffset,_world))world=BinaryPrimitives.ReadUInt16LittleEndian(_world);else worldFailures++;
                    var buffs=new List<ReplayBuff>();var controls=new List<ControlSource>();
                    if(Read(address+ClientLayout.StatusOffset,_statuses)) {
                        buffs=ClientLayout.DecodePlayer(_header,world,_statuses,now)?.Statuses??new();controls=ControlSourceDecoder.Decode(_statuses);
                    } else statusFailures++;
                    meta=(world,now,buffs,controls);_cache[id]=meta;
                }
                player=ClientLayout.DecodePlayer(_header,meta.World,ReadOnlySpan<byte>.Empty,meta.At,out var reason);
                if(player==null) {rejected++;rejections[reason]=rejections.GetValueOrDefault(reason)+1;continue;}
                player.Statuses=meta.Statuses;if(_teams.TryGetValue(id,out var team)&&team.Job==player.ClassJobId&&ClientCatalog.PlayerName(team.Name,team.World)==player.Player) {player.Team=team.Team;rosterMatches++;}
                // A valid-looking but stale/mismatched roster must not erase every loaded PC.
                if(frame.Players.Count<10)frame.Players.Add(player);
                else if(player.Team is 1 or 2&&frame.Players.FindIndex(p=>p.Team==0) is var replace&&replace>=0)frame.Players[replace]=player;
                else batch.UnconfirmedRosterLimited=true;
                if(address==localAddress&&meta.World!=0)batch.LocalPlayer=player.Player;
            }
            actors[id]=new(id,U(_header,0x88),nameId,kind,x,y,z,heading,player);addresses[id]=address;
            if(kind==2&&nameId==11350)crystals.Add(new(){X=x,Y=y,Z=z});

        }
        // Only apply the roster exclusion when all observed roster members correlate.
        if(_teams.Count==10&&rosterMatches==10)frame.Players.RemoveAll(p=>p.Team==0);
        foreach(var actor in actors.Values)if(actor.Player is {} player&&_cache.TryGetValue(actor.EntityId,out var status))player.Controls=ControlSourceDecoder.Resolve(status.Controls,status.At,now,actors);
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
        if(batch.LocalPlayer.Length==0) {_profile=null;_lastProfileRead=DateTime.MinValue;}
        batch.ChannelStatus["login"]=_localPlayerAddress==0?"本地角色签名未匹配":localAddress==0?"未登录角色":batch.LocalPlayer.Length==0?"角色加载中或身份字段未确认":"已确认登录角色";
        if(batch.LocalPlayer.Length>0&&_profile!=null&&_profile.Player!=batch.LocalPlayer) {_profile=null;_lastProfileRead=DateTime.MinValue;}
        if(batch.LocalPlayer.Length>0&&now-_lastProfileRead>=TimeSpan.FromSeconds(2)) {
            _lastProfileRead=now;
            if(Read(_profileAddress,_profileBytes)&&PlayerPvpProfile.Decode(_profileBytes,batch.LocalPlayer,now) is {} profile) {
                if(_profile==null||profile with {ObservedAt=_profile.ObservedAt}!=_profile)_profile=profile;
                _profileStatus="已校验个人资料";
            } else {_profile=null;_profileStatus=_profileAddress==0?"资料签名未匹配":_profileBytes[0]==0?"资料尚未加载":"资料不可读或校验失败";}
        }
        batch.Profile=_profile;
        if(arena)_lastArenaSeen=now;
        if(now-_lastArenaSeen<TimeSpan.FromSeconds(60)&&now-_lastUiRead>=TimeSpan.FromSeconds(2)) {_lastUiRead=now;ReadResultPanels(now,batch);}
        batch.ChannelStatus["director"]=_directorStatus;batch.ChannelStatus["profile"]=_profileStatus;batch.ChannelStatus["resultPanel"]=_uiStatus;
        batch.ChannelStatus["objectHeaders"]=headerReads.ToString();batch.ChannelStatus["playerObjects"]=pcSeen.ToString();batch.ChannelStatus["worldReadFailures"]=worldFailures.ToString();batch.ChannelStatus["statusReadFailures"]=statusFailures.ToString();batch.ChannelStatus["rejectedPlayers"]=rejected.ToString();batch.ChannelStatus["playerRejections"]=string.Join(",",rejections.Select(p=>$"{p.Key}:{p.Value}"));batch.ChannelStatus["rosterMatches"]=rosterMatches.ToString();
        if(detail)batch.ChannelStatus["sceneIdentities"]=string.Join(";",sceneIds.OrderBy(p=>p.Key).Take(96).Select(p=>$"{p.Key}={p.Value}"));
        batch.ChannelStatus["loadedActors"]=actors.Count.ToString();batch.ChannelStatus["capturedPlayers"]=frame.Players.Count.ToString();batch.ChannelStatus["newEffects"]=batch.Effects.Count.ToString();
        if(_cache.Count>1000)_cache.Clear();return (territory,frame);
    }
    private void ReadDirector(ObservationBatch batch) {
        _lastProgress=null;_teams.Clear();
        if(_eventFramework==0) {_directorStatus="事件框架签名未匹配";return;}
        long framework=Pointer(_eventFramework);long director=framework>0?Pointer(framework+ClientLayout.ActiveDirectorOffset):0;
        if(director!=_lastDirector) {_mapEffects.Clear();_lastDirector=director;}
        if(director==0) {_directorStatus="未加载副本控制器";return;}
        if(!Read(director+ClientLayout.DirectorTypeOffset,_directorType)) {_directorStatus="副本类型不可读";return;}
        if(_directorType[0]!=5) {_directorStatus=$"当前不是水晶冲突控制器（类型 {_directorType[0]}）";return;}
        // Native managed map-effect state persists after the activating event, including bridge timelines.
        if(Read(director+0xDE0,_mapStates)) {
            int count=BinaryPrimitives.ReadUInt16LittleEndian(_mapStates.AsSpan(0x602));batch.ChannelStatus["sceneStates"]=count<=128?$"已读取 {count} 项":"数量校验失败";
            for(int i=0;i<count&&count<=128;i++)
            {var row=_mapStates.AsSpan(i*12,12);uint id=U(row,0);if(id==0)continue;
                ushort state=BinaryPrimitives.ReadUInt16LittleEndian(row[8..]);byte flags=row[10];
                if(!_mapEffects.TryGetValue(id,out var old)||old!=(state,flags)) {batch.MapStates.Add(new(DateTime.UtcNow,id,state,flags));_mapEffects[id]=(state,flags);}
            }
        }
        long roster=Pointer(director+ClientLayout.DirectorPlayerPointerOffset);
        if(!Read(roster,_roster)) {_directorStatus="参赛名单不可读";return;}
        if(DirectorDecoder.Roster(_roster) is not {} players) {_directorStatus="参赛名单校验失败";return;}
        _teams=players.ToDictionary(p=>p.EntityId);
        if(!Read(director+ClientLayout.DirectorProgressOffset,_progress)) {_directorStatus="推进状态不可读";return;}
        if(DirectorDecoder.Progress(_progress) is not {} progress) {_directorStatus="名单已校验，推进状态范围校验失败："+string.Join(",",Enumerable.Range(0,7).Select(i=>BinaryPrimitives.ReadInt32LittleEndian(_progress.AsSpan(i*4))));return;}
        _directorStatus=$"已校验 {players.Count} 名参赛者和推进状态";
        _lastProgress=progress;batch.DirectorAvailable=true;
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
        if(manager==0||!Read(manager+0x6900,_units)) {_uiStatus=_atkStage==0?"界面签名未匹配":"界面列表尚不可读";return;}
        int count=BinaryPrimitives.ReadUInt16LittleEndian(_units.AsSpan(0x808));if(count>256) {_uiStatus="界面列表数量校验失败";return;}
        _uiStatus="尚未打开结果窗口";
        for(int i=0;i<count;i++) {
            long unit=BinaryPrimitives.ReadInt64LittleEndian(_units.AsSpan(8+i*8));if(!Read(unit,_unit))continue;
            var nameBytes=_unit.AsSpan(8,32);int end=nameBytes.IndexOf((byte)0);if(end<1)continue;
            var name=System.Text.Encoding.ASCII.GetString(nameBytes[..end]);
            if(!SettlementTextProbe.IsResultPanel(name))continue;
            if((U(_unit,0x198)&(2u<<20))==0)continue;
            _uiStatus="已检测到结果窗口 "+name;
            int length=BinaryPrimitives.ReadUInt16LittleEndian(_unit.AsSpan(0x1E2));if(length>2048)continue;
            long address=BinaryPrimitives.ReadInt64LittleEndian(_unit.AsSpan(0x178));var bytes=new byte[length*16];if(length>0&&!Read(address,bytes))continue;
            var values=ClientUiDecoder.Decode(bytes,length,(pointer,wide)=> {
                var text=new byte[512];if(!Read(pointer,text))return "";
                if(wide) {int n=0;while(n+1<text.Length&&(text[n]!=0||text[n+1]!=0))n+=2;return System.Text.Encoding.Unicode.GetString(text,0,n);}
                int n8=Array.IndexOf(text,(byte)0);return System.Text.Encoding.UTF8.GetString(text,0,n8<0?text.Length:n8);
            });
            var texts=SettlementTextProbe.Read(unit,(at,size)=>{var data=new byte[size];return Read(at,data)?data:null;});
            _uiStatus=$"{name}：{values.Count} 个基本字段 / {texts.Count} 条文本";
            if(values.Count==0&&texts.Count==0)continue;
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new{Values=values,TextNodes=texts})));
            if(_panelHashes.GetValueOrDefault(name)==hash)continue;_panelHashes[name]=hash;
            batch.SettlementPanels.Add(new(now,name,values){TextNodes=texts});
        }
    }
    private void Reset() {_cache.Clear();_journal.Reset();_casts.Clear();_teams.Clear();_mapEffects.Clear();_lastDirector=0;_lastProgress=null;_lastDetail=DateTime.MinValue;}
    private static uint U(ReadOnlySpan<byte> data,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    private static float F(ReadOnlySpan<byte> data,int at)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[at..]));
    private static bool Finite(float x,float y,float z,float heading)=>float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z)&&float.IsFinite(heading)&&MathF.Abs(x)<100000&&MathF.Abs(y)<100000&&MathF.Abs(z)<100000;
    public void Dispose() {_handle?.Dispose();_process?.Dispose();}
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint processId);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int processId);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,[Out]byte[] buffer,int size,out nuint read);
}
