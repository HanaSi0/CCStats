using Dalamud.Configuration;
using Dalamud.Interface.Colors;
using PvpStats.Helpers;
using PvpStats.Types.Display;
using PvpStats.Types.Match;
using PvpStats.Types.Player;
using System;
using System.Numerics;
using System.Threading;

namespace PvpStats.Settings;

[Serializable]
public class Configuration : IPluginConfiguration {
    public static readonly int CurrentVersion = 0;
    public int Version { get; set; } = CurrentVersion;
    public string LastPluginVersion { get; set; } = "0.0.0.0";
    public bool? EnableDBCachingCC { get; set; }

    public bool EnableDetailedTelemetry { get; set; } = true;

    public bool? EnableTimelineCC { get; set; }
    public bool? EnableActionAnalyticsCC { get; set; }

    public bool? LegacyImportChoice { get; set; }

    public int WindowLayoutVersion { get; set; }

    public int LeaderboardRegion { get; set; } = 0;

    public bool ShowFloatingLauncher { get; set; } = true;
    public Vector2 FloatingLauncherPosition { get; set; } = new(40, 200);
    public float FloatingLauncherSize { get; set; } = 48;

    public bool EnablePlayerLinking { get; set; } = true;
    public bool EnableAutoPlayerLinking { get; set; } = true;
    public bool EnableManualPlayerLinking { get; set; } = true;
    public bool LeftPlayerTeam { get; set; } = false;
    public bool AnchorTeamNames { get; set; } = true;
    //public bool? JobIconCells { get; set; }
    public bool ResizeableMatchWindow { get; set; } = true;
    public bool ShowBackgroundImage { get; set; } = true;
    public bool? StretchScoreboardColumns { get; set; }
    public bool SizeFiltersToFit { get; set; } = false;
    public bool PersistWindowSizePerTab { get; set; } = false;
    public bool MinimizeWindow { get; set; } = false;
    public bool MinimizeDirectionLeft { get; set; } = false;
    public bool ResizeWindowLeft { get; set; } = false;
    public bool AdjustWindowHeightOnFilterCollapse { get; set; } = false;
    public bool ColorScaleStats { get; set; } = true;

    public float TeamRowAlpha { get; set; } = 0.6f;
    public float PlayerRowAlpha { get; set; } = 0.3f;
    //public float ScoreboardRowPaddingFactor { get; set; } = 1f;
    public WindowConfiguration CCWindowConfig { get; set; } = new();
    public ColorConfiguration Colors { get; set; } = new();
    public ThemeConfiguration Theme { get; set; } = new();

    [NonSerialized]
    private Plugin? _plugin;
    [NonSerialized]
    private SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

    public Configuration() {
    }

    public void Initialize(Plugin plugin) {
        _plugin = plugin;
        Theme ??= new();
        Theme.Normalize();
    }

    public void Save() {
        //try {
        //    await _fileLock.WaitAsync();
        //    _plugin!.PluginInterface.SavePluginConfig(this);
        //} finally {
        //    _fileLock.Release();
        //}
        _plugin!.PluginInterface.SavePluginConfig(this);
    }

    public Vector4 GetJobColor(Job? job) {
        return PlayerJobHelper.GetSubRoleFromJob(job) switch {
            JobSubRole.TANK => Colors.Tank,
            JobSubRole.HEALER => Colors.Healer,
            JobSubRole.MELEE => Colors.Melee,
            JobSubRole.RANGED => Colors.Ranged,
            JobSubRole.CASTER => Colors.Caster,
            _ => ImGuiColors.DalamudWhite,
        };
    }

}
