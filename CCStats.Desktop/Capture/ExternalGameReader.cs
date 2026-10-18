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
    private readonly long _objects,_gameMain;
    private readonly byte[] _table=new byte[ClientLayout.ObjectSlots*8],_header=new byte[ClientLayout.HeaderBytes],_statuses=new byte[ClientLayout.StatusBytes],_world=new byte[2],_territory=new byte[4];
    private readonly Dictionary<uint,(ushort World,DateTime At,List<ReplayBuff> Statuses)> _cache=new();
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
        _handle=OpenProcess(0x10|0x1000,false,_process.Id); // VM_READ | QUERY_LIMITED_INFORMATION only.
        try {
            if(_handle.IsInvalid)throw new InvalidOperationException("无法读取游戏进程，请确保软件与游戏使用相同用户权限。");
            var module=_process.MainModule??throw new InvalidOperationException("无法读取客户端模块信息。");
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
        } catch {_handle.Dispose();_process.Dispose();throw;}
    }
    private bool Read(long address,byte[] buffer) => address>0x10000&&ReadProcessMemory(_handle,(nint)address,buffer,buffer.Length,out var size)&&size==(nuint)buffer.Length;
    public (uint Territory,MovementFrame Frame) Sample() {
        if(_process.HasExited)throw new InvalidOperationException("游戏进程已退出。");
        if(!Read(_gameMain+ClientLayout.TerritoryOffset,_territory)||!Read(_objects+ClientLayout.ObjectArrayOffset,_table))throw new InvalidDataException("客户端地址不可读，请重新连接或等待版本适配。");
        uint territory=BinaryPrimitives.ReadUInt32LittleEndian(_territory);
        if(territory>100000)throw new InvalidDataException("区域编号异常，已停止采集。");
        var frame=new MovementFrame {Timestamp=DateTime.UtcNow};
        for(int slot=0;slot<200;slot+=2) {
            long address=BinaryPrimitives.ReadInt64LittleEndian(_table.AsSpan(slot*8,8));
            if(!Read(address,_header)||_header[0x90]!=1)continue;
            var id=BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(0x78,4));
            if(!_cache.TryGetValue(id,out var meta)||frame.Timestamp-meta.At>TimeSpan.FromMilliseconds(200)) {
                if(!Read(address+ClientLayout.WorldOffset,_world)||!Read(address+ClientLayout.StatusOffset,_statuses))continue;
                var parsed=ClientLayout.DecodePlayer(_header,BinaryPrimitives.ReadUInt16LittleEndian(_world),_statuses,frame.Timestamp);
                if(parsed==null)continue;
                meta=(BinaryPrimitives.ReadUInt16LittleEndian(_world),frame.Timestamp,parsed.Statuses);_cache[id]=meta;
            }
            var player=ClientLayout.DecodePlayer(_header,meta.World,ReadOnlySpan<byte>.Empty,meta.At);
            if(player!=null) {player.Statuses=meta.Statuses;frame.Players.Add(player);}
        }
        if(_cache.Count>1000)_cache.Clear();
        return (territory,frame);
    }
    public void Dispose() {_handle.Dispose();_process.Dispose();}
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint processId);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int processId);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool ReadProcessMemory(SafeProcessHandle process,nint address,[Out]byte[] buffer,int size,out nuint read);
}
