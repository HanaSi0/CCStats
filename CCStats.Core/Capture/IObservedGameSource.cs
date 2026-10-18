using PvpStats.Analysis;
namespace CCStats.Core.Capture;
public interface IObservedGameSource : IDisposable {
    int ProcessId {get;}
    (uint Territory,MovementFrame Frame) Sample();
}
