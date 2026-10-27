using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using Reloaded.Universal.Redirector.Interfaces;
using gbfrelink.utility.manager.Interfaces;

namespace Spark.Gbfr.Mods;

public sealed class Startup : IMod
{
    private const string QuestPathList = "quest-baseinfo-paths.txt";
    private const string FsmPathList = "quest-fsm-paths.txt";
    private const string StoryFsmPathList = "story-fsm-paths.txt";

    private IModLoader _loader = null!;
    private IModConfig _config = null!;
    private ILogger _logger = null!;
    private ModSettings _settings = new();

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfig)
    {
        _loader = (IModLoader)loaderApi;
        _config = (IModConfig)modConfig;
        _logger = (ILogger)_loader.GetLogger();

        try
        {
            _settings = new ModConfigurator(_loader.GetModConfigDirectory(_config.ModId))
                .GetConfigurations().OfType<ModSettings>().First().Clamped();
        }
        catch (Exception ex)
        {
            Log($"WARNING: settings could not be read, using defaults: {ex.Message}");
        }

        try
        {
            ApplyPatches();
        }
        catch (Exception ex)
        {
            Log($"ERROR: patch could not run, game left vanilla: {ex.Message}");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void ApplyPatches()
    {
        var redirectorRef = _loader.GetController<IRedirectorController>();
        var dataManagerRef = _loader.GetController<IDataManager>();
        if (redirectorRef is null || !redirectorRef.TryGetTarget(out var redirector)
            || dataManagerRef is null || !dataManagerRef.TryGetTarget(out var dataManager) || !dataManager.Initialized)
        {
            Log("ERROR: Reloaded file redirector or GBFR Mod Manager is unavailable; game left vanilla.");
            return;
        }

        string gameDir = Path.GetDirectoryName(_loader.GetAppConfig().AppLocation)!;
        string modDir = _loader.GetDirectoryForModId(_config.ModId);
        string questListPath = Path.Combine(modDir, QuestPathList);
        if (!File.Exists(questListPath))
        {
            Log($"ERROR: missing {questListPath}; game left vanilla.");
            return;
        }

        byte[] index = File.ReadAllBytes(Path.Combine(gameDir, "data.i"));
        var questPaths = File.ReadAllLines(questListPath).Where(l => l.Length > 0).ToList();
        string fsmListPath = Path.Combine(modDir, FsmPathList);
        var fsmPaths = File.Exists(fsmListPath)
            ? File.ReadAllLines(fsmListPath).Where(l => l.Length > 0).ToList()
            : new List<string>();
        var timeUpVictoryQuests = _settings.QuestTimersEnabled && !_settings.ExtendSurviveDefendQuests
            ? FindTimeUpVictoryQuests(dataManager, fsmPaths)
            : new HashSet<string>();
        var summary = new List<string>();
        var extendedQuests = new Dictionary<string, (int Limit, IReadOnlyList<int> Objectives)>();
        var result = new ResidentChunkPatcher(Log).Apply(gameDir, index, Path.Combine(modDir, "cache"), read =>
        {
            var edits = new List<(string, byte[])>();
            edits.AddRange(BuildQuestEdits(read, questPaths, timeUpVictoryQuests, summary, extendedQuests));
            if (_settings.MspBonusPercent > 0 || _settings.ExpRupieBonusPercent > 0)
                edits.AddRange(BuildRewardEdits(read, summary));
            if (_settings.RaiseItemCap && _settings.ItemCap > ItemCapPatch.OriginalCap)
                edits.AddRange(BuildItemCapEdits(read, summary));
            if (_settings.UnlockFateEpisodes)
                edits.AddRange(BuildFateEpisodeEdits(read, summary));
            if (_settings.GradeEasierPercent > 0)
                edits.AddRange(BuildGradeTierEdits(read, summary));
            if (_settings.CaptainOptional)
                edits.AddRange(BuildFormationEdits(read, summary));
            if (_settings.CheapCrewmateCards)
                edits.AddRange(BuildCrewmateCostEdits(read, summary));
            return edits;
        });
        if (result is not null)
            RedirectData0(redirector, gameDir, result.ModdedData0Path);

        if (_settings.QuestTimersEnabled)
            ApplyFsmTimerEdits(dataManager, index, fsmListPath, extendedQuests, summary);
        if (_settings.CaptainOptional)
        {
            ApplyStoryPartyEdits(dataManager, index, Path.Combine(modDir, StoryFsmPathList), summary);
            ApplyCaptainCodePatch(summary);
        }

        foreach (var line in summary)
            Log(line);
    }

    private static void RedirectData0(IRedirectorController redirector, string gameDir, string moddedData0Path)
    {
        string moddedData0 = moddedData0Path.Replace('/', '\\');
        var gameDirs = new[] { gameDir, Path.GetDirectoryName(Environment.ProcessPath) }
            .Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in gameDirs)
            redirector.AddRedirect(Path.Combine(dir!, "data.0").Replace('/', '\\'), moddedData0);
    }

    private static void ApplyCaptainCodePatch(List<string> summary)
    {
        try
        {
            summary.Add(CaptainFormationCodePatch.Apply() is string error
                ? $"WARNING: captain party-check code patch skipped: {error}."
                : "Captain party check patched: story parties no longer need the captain.");
        }
        catch (Exception ex)
        {
            summary.Add($"WARNING: captain party-check code patch failed: {ex.Message}");
        }
    }

    private void ApplyFsmTimerEdits(IDataManager dataManager, byte[] index, string fsmListPath,
        Dictionary<string, (int Limit, IReadOnlyList<int> Objectives)> extendedQuests, List<string> summary)
    {
        if (!File.Exists(fsmListPath))
        {
            summary.Add($"WARNING: missing {fsmListPath}; scripted quest time limits left vanilla.");
            return;
        }

        int files = 0, checks = 0;
        foreach (var path in File.ReadAllLines(fsmListPath).Where(l => l.Length > 0))
        {
            if (FsmTimerPatch.QuestIdOf(path) is not string questId || !extendedQuests.TryGetValue(questId, out var original))
                continue;
            if (!ResidentChunkPatcher.IsStreamedArchiveFile(index, path))
            {
                Log($"WARNING: {path} is not a streamed archive file; left vanilla.");
                continue;
            }

            try
            {
                if (FsmTimerPatch.Patch(dataManager.GetArchiveFile(path), original.Limit, original.Objectives, _settings.QuestTimerExtraSeconds) is not var (data, count))
                    continue;
                dataManager.AddOrUpdateExternalFile(path, data);
                files++;
                checks += count;
            }
            catch (Exception ex)
            {
                Log($"WARNING: skipped {path}: {ex.Message}");
            }
        }

        summary.Add($"Scripted quest time limits and timed objectives +{_settings.QuestTimerExtraSeconds}s applied to {checks} timer checks in {files} quest FSM files.");
    }

    private void ApplyStoryPartyEdits(IDataManager dataManager, byte[] index, string listPath, List<string> summary)
    {
        if (!File.Exists(listPath))
        {
            summary.Add($"WARNING: missing {listPath}; story sections still force the captain.");
            return;
        }

        int files = 0, sections = 0;
        foreach (var path in File.ReadAllLines(listPath).Where(l => l.Length > 0))
        {
            try
            {
                if (!ResidentChunkPatcher.IsStreamedArchiveFile(index, path))
                    continue;
                if (CaptainUnlockPatch.ApplyStoryFsm(dataManager.GetArchiveFile(path)) is not var (data, count))
                    continue;
                dataManager.AddOrUpdateExternalFile(path, data);
                files++;
                sections += count;
            }
            catch (Exception ex)
            {
                Log($"WARNING: skipped {path}: {ex.Message}");
            }
        }

        summary.Add($"Captain-forced story sections freed: {sections} sections in {files} story quest FSM files.");
    }

    private HashSet<string> FindTimeUpVictoryQuests(IDataManager dataManager, List<string> fsmPaths)
    {
        var quests = new HashSet<string>();
        foreach (var path in fsmPaths)
        {
            if (FsmTimerPatch.QuestIdOf(path) is not string questId || quests.Contains(questId))
                continue;
            try
            {
                var root = MsgPack.Parse(dataManager.GetArchiveFile(path));
                if (root.Pairs?.Any(p => p.Key.Text == "PlayTimeupTelop") == true)
                    quests.Add(questId);
            }
            catch (Exception ex)
            {
                Log($"WARNING: could not inspect {path}: {ex.Message}");
            }
        }
        return quests;
    }

    private List<(string, byte[])> BuildQuestEdits(Func<string, byte[]?> read, List<string> questPaths,
        HashSet<string> timeUpVictoryQuests, List<string> summary, Dictionary<string, (int Limit, IReadOnlyList<int> Objectives)> extendedQuests)
    {
        var edits = new List<(string, byte[])>();
        int timedQuests = 0, timedObjectives = 0, swappedQuests = 0, swappedObjectives = 0;
        foreach (string path in questPaths)
        {
            if (read(path) is not byte[] data)
                continue;
            string questId = path.Split('/')[1].ToLowerInvariant();
            bool changed = false;
            try
            {
                if (_settings.ReplaceThrownObjectives
                    && QuestObjectivePatch.Patch(data, _settings.ReplacementDefeatCount) is var (swapped, count))
                {
                    data = swapped;
                    changed = true;
                    swappedQuests++;
                    swappedObjectives += count;
                }

                if (_settings.QuestTimersEnabled && !timeUpVictoryQuests.Contains(questId)
                    && QuestTimerPatch.Patch(data, _settings.QuestTimerExtraSeconds) is var (patched, originalSeconds, originalObjectives))
                {
                    data = patched;
                    changed = true;
                    timedQuests++;
                    timedObjectives += originalObjectives.Count;
                    extendedQuests[questId] = (originalSeconds, originalObjectives);
                }
            }
            catch (InvalidDataException ex)
            {
                Log($"WARNING: skipped {path}: {ex.Message}");
                continue;
            }

            if (changed)
                edits.Add((path, data));
        }

        if (_settings.QuestTimersEnabled)
            summary.Add(timedQuests > 0
                ? $"Quest timers +{_settings.QuestTimerExtraSeconds}s applied in-archive to {timedQuests} quests and {timedObjectives} completion-time objectives; " +
                  $"{timeUpVictoryQuests.Count} survive/defend quests (time-up = win) left vanilla."
                : "WARNING: no quest timers found; timers left vanilla.");
        if (_settings.ReplaceThrownObjectives)
            summary.Add($"Thrown-object kill objectives replaced with \"Defeat {_settings.ReplacementDefeatCount} foes\": {swappedObjectives} in {swappedQuests} quests.");
        return edits;
    }

    private List<(string, byte[])> BuildRewardEdits(Func<string, byte[]?> read, List<string> summary)
    {
        var rewards = read(RewardPointPatch.RewardTablePath);
        var points = read(RewardPointPatch.RewardPointTablePath);
        var counts = rewards is null || points is null ? null : RewardPointPatch.Apply(rewards, points, _settings.MspBonusPercent, _settings.ExpRupieBonusPercent);
        if (counts is null)
        {
            summary.Add("WARNING: reward tables missing or layout differs from Endless Ragnarok 2.0; MSP/EXP/rupies left vanilla.");
            return [];
        }

        if (_settings.MspBonusPercent > 0)
            summary.Add($"MSP +{_settings.MspBonusPercent}% (round up) applied to {counts.MspEntries} reward-point entries across {counts.MspKeys} MSP reward keys.");
        if (_settings.ExpRupieBonusPercent > 0)
            summary.Add($"EXP & rupies +{_settings.ExpRupieBonusPercent}% (round up) applied to {counts.ExpGoldEntries} reward-point entries across {counts.ExpGoldKeys} keys" +
                (counts.SharedKeys > 0 ? $" ({counts.SharedKeys} keys shared with MSP kept at the MSP bonus only)." : "."));
        return [(RewardPointPatch.RewardPointTablePath, points!)];
    }

    private List<(string, byte[])> BuildItemCapEdits(Func<string, byte[]?> read, List<string> summary)
    {
        var table = read(ItemCapPatch.ItemCategoryTablePath);
        var raised = table is null ? null : ItemCapPatch.Apply(table, (uint)_settings.ItemCap);
        if (raised is not > 0)
        {
            summary.Add("WARNING: item_category.tbl missing or layout differs from Endless Ragnarok 2.0; item cap left vanilla.");
            return [];
        }

        summary.Add($"Item hold cap raised {ItemCapPatch.OriginalCap} -> {_settings.ItemCap} on {raised} item categories.");
        return [(ItemCapPatch.ItemCategoryTablePath, table!)];
    }

    private static List<(string, byte[])> BuildFateEpisodeEdits(Func<string, byte[]?> read, List<string> summary)
    {
        var table = read(FateEpisodePatch.FateEpisodeTablePath);
        var counts = table is null ? null : FateEpisodePatch.Apply(table);
        if (counts is null)
        {
            summary.Add("WARNING: fate_episode.tbl missing or layout differs from Endless Ragnarok 2.0; fate episodes left vanilla.");
            return [];
        }

        summary.Add($"Fate episodes unlocked: {counts.Episodes} episodes; cleared {counts.QuestGates} story-quest prerequisites, " +
            $"{counts.MissionGates} character missions, {counts.LevelGates} level requirements.");
        return [(FateEpisodePatch.FateEpisodeTablePath, table!)];
    }

    private List<(string, byte[])> BuildGradeTierEdits(Func<string, byte[]?> read, List<string> summary)
    {
        var table = read(GradeTierPatch.QuestTablePath);
        var counts = table is null ? null : GradeTierPatch.Apply(table, _settings.GradeEasierPercent);
        if (counts is null)
        {
            summary.Add("WARNING: quest_baseinfo_ex_data.tbl missing or layout differs from Endless Ragnarok 2.0; grade thresholds left vanilla.");
            return [];
        }

        summary.Add($"Quest grade thresholds {_settings.GradeEasierPercent}% easier: {counts.TimeQuests} time-graded, " +
            $"{counts.KillQuests} kill-graded and {counts.HpQuests} HP-graded quests.");
        return [(GradeTierPatch.QuestTablePath, table!)];
    }

    private static List<(string, byte[])> BuildFormationEdits(Func<string, byte[]?> read, List<string> summary)
    {
        var table = read(CaptainUnlockPatch.FormationTablePath);
        var freed = table is null ? null : CaptainUnlockPatch.ApplyFormationTable(table);
        if (freed is null)
        {
            summary.Add("WARNING: formation_slot.tbl missing or layout differs from Endless Ragnarok 2.0; captain stays required.");
            return [];
        }

        summary.Add($"Captain no longer required in the story party: {freed} story formation presets freed.");
        return [(CaptainUnlockPatch.FormationTablePath, table!)];
    }

    private List<(string, byte[])> BuildCrewmateCostEdits(Func<string, byte[]?> read, List<string> summary)
    {
        var trade = read(CrewmateCostPatch.TradeTablePath);
        var tiers = read(CrewmateCostPatch.TierTablePath);
        var recipes = read(CrewmateCostPatch.MaterialTablePath);
        var counts = trade is null || tiers is null || recipes is null ? null : CrewmateCostPatch.Apply(trade, tiers, recipes, _settings.CrewmateCardCost);
        if (counts is null)
        {
            summary.Add("WARNING: shop tables missing or layout differs from Endless Ragnarok 2.0; crewmate card trades left vanilla.");
            return [];
        }

        summary.Add($"Crewmate cards in Treasure Trade: {counts.Trades} card trades now cost {_settings.CrewmateCardCost} Cobblestone(s), unlimited, listed from the first quest.");
        return [(CrewmateCostPatch.TradeTablePath, trade!), (CrewmateCostPatch.MaterialTablePath, recipes!)];
    }

    private void Log(string message) => _logger.WriteLine($"[{_config.ModId}] {message}");

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;
    public Action Disposing => () => { };
}
