using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using PvpStats.Managers;
using PvpStats.Leaderboards;
using System.IO;
using PvpStats.Managers.Game;
using PvpStats.Managers.Stats;
using PvpStats.Services;
using PvpStats.Services.DataCache;
using PvpStats.Settings;
using PvpStats.Types.Match;
using PvpStats.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PvpStats;

public sealed class Plugin : IDalamudPlugin {
    public string Name => "CCStats";

    internal bool LegacyImportPending { get; private set; }
    internal bool LegacyImportBusy { get; private set; }
    internal string LegacyImportStatus { get; private set; } = "正在检查 PvP Tracker 历史记录…";

    internal const string DatabaseName = "data.db";

    private const string CCStatsCommandName = "/ccstats";
    private const string DebugCommandName = "/pvpstatsdebug";
    private const string ConfigCommandName = "/pvpstatsconfig";
    private const string LastMatchCommandName = "/lastmatch";

    internal static IPluginLog Log2;

    //Dalamud services
    internal IDalamudPluginInterface PluginInterface { get; init; }
    private ICommandManager CommandManager { get; init; }
    internal IDataManager DataManager { get; init; }
    internal IClientState ClientState { get; init; }
    internal ICondition Condition { get; init; }
    internal IDutyState DutyState { get; init; }
    //internal IPartyList PartyList { get; init; }
    internal IChatGui ChatGui { get; init; }
    internal IGameGui GameGui { get; init; }
    internal IFramework Framework { get; init; }
    internal IPluginLog Log { get; init; }
    internal IAddonEventManager AddonEventManager { get; init; }
    internal IAddonLifecycle AddonLifecycle { get; init; }
    internal IObjectTable ObjectTable { get; init; }
    internal ITextureProvider TextureProvider { get; init; }
    internal IGameInteropProvider InteropProvider { get; init; }
    internal ISigScanner SigScanner { get; init; }

    internal CrystallineConflictMatchManager? CCMatchManager { get; init; }
    internal WindowManager WindowManager { get; init; }
    internal ValidationManager Validation { get; init; }
    internal CrystallineConflictStatsManager CCStatsEngine { get; init; }
    internal RegionalLeaderboardService Leaderboards { get; init; }

    internal DataQueue DataQueue { get; init; }
    internal LocalizationService Localization { get; init; }
    internal IpcService Ipc { get; init; }
    internal StorageService Storage { get; init; }
    internal CCMatchCacheService CCCache { get; init; }
    internal GameStateService GameState { get; init; }
    internal AtkNodeService AtkNodeService { get; init; }
    internal PlayerLinkService PlayerLinksService { get; init; }

    public Configuration Configuration { get; init; }
    internal MemoryService Functions { get; init; }

    internal bool DebugMode { get; set; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager dataManager,
        IClientState clientState,
        ICondition condition,
        IDutyState dutyState,
        IPartyList partyList,
        IChatGui chatGui,
        IGameGui gameGui,
        IFramework framework,
        IPluginLog log,
        IAddonEventManager addonEventManager,
        IAddonLifecycle addonLifecycle,
        IObjectTable objectTable,
        ITextureProvider textureProvider,
        IGameInteropProvider interopProvider,
        ISigScanner sigScanner) {
        try {
            PluginInterface = pluginInterface;
            CommandManager = commandManager;
            DataManager = dataManager;
            ClientState = clientState;
            Condition = condition;
            DutyState = dutyState;
            //PartyList = partyList;
            ChatGui = chatGui;
            GameGui = gameGui;
            Framework = framework;
            Log = log;
            Log2 = log;
            AddonEventManager = addonEventManager;
            AddonLifecycle = addonLifecycle;
            ObjectTable = objectTable;
            TextureProvider = textureProvider;
            InteropProvider = interopProvider;
            SigScanner = sigScanner;

            try {
                Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            } catch(Exception e) {
                Configuration = new();
                Log.Error(e, "Error in configuration setup.");
            }
            Configuration.Initialize(this);

            DataQueue = new();
            Ipc = new(this);
            Storage = new(this, $"{PluginInterface.GetPluginConfigDirectory()}\\{DatabaseName}");
            CCCache = new(this);
            Functions = new(this);
            GameState = new(this);
            AtkNodeService = new(this);
            PlayerLinksService = new(this);
            Localization = new(this);
            CCStatsEngine = new(this);
            Leaderboards = new(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "leaderboards"), Configuration.LeaderboardRegion);
            WindowManager = new(this);
            Validation = new(this);
            try {
                CCMatchManager = new(this);
            } catch(SignatureException e) {
                Log.Error(e, $"failed to initialize cc match manager");
            }

            CommandManager.AddHandler(CCStatsCommandName, new CommandInfo(OnCCCommand) {
                HelpMessage = "打开水晶冲突记录器；/ccstats probe 打开地图试验。"
            });
            CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand) {
                HelpMessage = "Opens config window."
            });
            CommandManager.AddHandler(LastMatchCommandName, new CommandInfo(OnLastMatchCommand) {
                HelpMessage = "Opens match details window of last played match."
            });

#if DEBUG
            CommandManager.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand) {
                HelpMessage = "Opens debug window."
            });
#endif
            DataQueue.QueueDataOperation(Initialize);
            _ = Task.Run(Leaderboards.CheckAtStartupAsync);
        } catch(Exception e) {
            //remove handlers and release database if we fail to start
            Log!.Error(e, "Failed to initialize plugin constructor");
            Dispose();
            //re-throw to prevent constructor from initializing
            throw;
        }

    }

    public void Dispose() {
#if DEBUG
        Log.Debug("disposing plugin");
#endif

        CommandManager.RemoveHandler(LastMatchCommandName);
#if DEBUG
        CommandManager.RemoveHandler(DebugCommandName);
#endif
        CommandManager.RemoveHandler(CCStatsCommandName);
        CommandManager.RemoveHandler(ConfigCommandName);

        Functions?.Dispose();
        CCMatchManager?.Dispose();
        WindowManager?.Dispose();
        Leaderboards?.Dispose();
        Ipc?.Dispose();
        Storage?.Dispose();
        DataQueue?.Dispose();
        GameState?.Dispose();

        Configuration?.Save();
    }

    private void OnLastMatchCommand(string command, string args) {
        Task.Run(() => {
            var lastMatch = CCCache.Matches.OrderByDescending(x => x.DutyStartTime)
                .FirstOrDefault(x => x.IsCompleted && !x.IsQuarantined && !x.IsDeleted);
            if(lastMatch != null) {
                WindowManager.OpenMatchDetailsWindow(lastMatch);
            }
        });
    }

    private void OnCCCommand(string command, string args) {
        if(string.Equals(args.Trim(),"probe",StringComparison.OrdinalIgnoreCase)) { WindowManager.OpenMapProbe();return; }
        WindowManager.OpenCCWindow();
    }

    private void OnConfigCommand(string command, string args) {
        WindowManager.OpenConfigWindow();
    }

#if DEBUG
    private void OnDebugCommand(string command, string args) {
        WindowManager.OpenDebugWindow();
    }
#endif

    internal void ChooseLegacyImport(bool import) {
        Configuration.LegacyImportChoice = import;
        Configuration.Save();
        LegacyImportPending = false;
        if(import) RequestLegacyImport();
        else LegacyImportStatus = "已选择暂不导入；以后可在此处手动导入旧战绩。";
    }

    internal void RequestLegacyImport() {
        if(LegacyImportBusy) return;
        Configuration.LegacyImportChoice = true;
        Configuration.Save();
        LegacyImportPending = false;
        LegacyImportBusy = true;
        LegacyImportStatus = "正在导入旧版战绩…";
        DataQueue.QueueDataOperation(() => ImportLegacyData(true));
    }

    private async Task ImportLegacyData(bool refreshViews) {
        LegacyImportBusy = true;
        try {
            var imported = await Storage.ImportLegacy(PluginInterface.GetPluginConfigDirectory());
            LegacyImportStatus = imported.State switch {
                "NotFound" => "未发现原版 PvP Tracker 数据库。",
                "AlreadyImported" => $"此前已导入：{imported.Matches} 场对局、{imported.Timelines} 份时间线、{imported.Links} 条玩家关联。",
                _ => $"已导入：{imported.Matches} 场对局、{imported.Timelines} 份时间线、{imported.Links} 条玩家关联；原版文件保留。",
            };
            PlayerLinksService.ReloadFromStorage();
            if(refreshViews) {
                if(Configuration.EnableDBCachingCC ?? true) CCCache.EnableCaching();
                await WindowManager.RefreshAll();
            }
            Log.Information(LegacyImportStatus);
        } catch(Exception e) {
            LegacyImportStatus = "旧数据导入未完成，未覆盖已有记录。请停用原版 PvP Tracker 后重新加载 CCStats 重试；详情见 Dalamud 日志。";
            Log.Error(e, "Failed to import PvP Tracker records; retry on next plugin load.");
        }
        LegacyImportBusy = false;
    }

    private async Task Initialize() {
        if(Configuration.LegacyImportChoice == true || Storage.LegacyAlreadyImported) {
            await ImportLegacyData(false);
        } else {
            var exists = File.Exists(LegacyDataImporter.SourcePath(PluginInterface.GetPluginConfigDirectory()));
            LegacyImportPending = exists && Configuration.LegacyImportChoice == null;
            LegacyImportStatus = !exists ? "未发现原版 PvP Tracker 数据库。" : LegacyImportPending
                ? "发现原版战绩，等待首次打开时选择是否导入。" : "已选择暂不导入，可随时点击下方按钮手动导入。";
        }

        if(Configuration.EnableDBCachingCC ?? true) {
            CCCache.EnableCaching();
        }

        var lastVersion = new Version(Configuration.LastPluginVersion);
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

        //version update validations
        var ccValidationTask = Task.Run(async () => {
            await Validation.BulkCCUpdateValidatePlayerCount();
            if(lastVersion < new Version(2, 4, 0, 0)) {
                await Validation.BulkUpdateCCMatchTypes();
            }
            if(lastVersion < new Version(2, 5, 3, 0)) {
                await Validation.SetCrystallineConflictMatchFlags();
            }
        });

        await ccValidationTask;

        await WindowManager.RefreshAll();
        Configuration.LastPluginVersion = currentVersion?.ToString() ?? "0.0.0.0";
        Configuration.Save();
        Log2.Information("PvP Tracker initialized.");
    }
}
