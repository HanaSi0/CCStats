using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PvpStats.Leaderboards;

/// <summary>Each game start checks every region once; browsing and switching regions never poll.</summary>
public sealed class RegionalLeaderboardService : IDisposable {
    private readonly LeaderboardService[] _regions;
    private readonly Lazy<Task> _startup;
    private int _selected;
    public int SelectedRegion => Volatile.Read(ref _selected);
    public RankingRunState State => _regions[SelectedRegion].State;
    public bool CanCancel => _regions[SelectedRegion].CanCancel;
    public Task Ready => Task.WhenAll(_regions.Select(r => r.Ready));
    public RegionalLeaderboardService(string directory, int selected = 0, Func<RankingRegion, LeaderboardService>? factory = null) {
        _selected = Math.Clamp(selected, 0, RankingRegion.All.Length - 1);
        _regions = RankingRegion.All.Select(region => factory?.Invoke(region) ?? new LeaderboardService(Path.Combine(directory, region.Key))).ToArray();
        _startup = new Lazy<Task>(async () => {
            await Ready;
            // Prioritize the last viewed region, then check the remaining ones in sequence.
            var selectedRegion = SelectedRegion;
            foreach(var index in Enumerable.Range(0, _regions.Length).OrderBy(i => i == selectedRegion ? 0 : 1))
                await _regions[index].CheckDateAsync(RankingRegion.All[index]);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
    public void SelectRegion(int index) => Interlocked.Exchange(ref _selected, Math.Clamp(index, 0, _regions.Length - 1));
    public Task CheckAtStartupAsync() => _startup.Value;
    public Task CheckDateAsync(int index, bool force = false) {
        index = Math.Clamp(index, 0, _regions.Length - 1);
        return _regions[index].CheckDateAsync(RankingRegion.All[index], force);
    }
    public Task ExportAsync(int index) => _regions[Math.Clamp(index, 0, _regions.Length - 1)].ExportAsync();
    public void Cancel() => _regions[SelectedRegion].Cancel();
    public void Dispose() { foreach(var service in _regions) service.Dispose(); }
}
