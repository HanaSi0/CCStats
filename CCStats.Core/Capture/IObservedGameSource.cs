using PvpStats.Analysis;
namespace CCStats.Core.Capture;
public interface IObservedGameSource : IDisposable {
    int ProcessId {get;}
    ObservationBatch? Telemetry => null;
    (uint Territory,MovementFrame Frame) Sample();
}
