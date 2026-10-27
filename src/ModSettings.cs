using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reloaded.Mod.Interfaces;

namespace Spark.Gbfr.Mods;

public sealed class ModSettings : IUpdatableConfigurable
{
    private const string Restart = "\n\nRestart the game after changing this.";

    [Category("1. Quest timers")]
    [DisplayName("Extend quest timers")]
    [Description("Adds extra time to every timed quest, including its scripted time-up checks and \"clear within X:XX\" side goals." + Restart)]
    [DefaultValue(true)]
    public bool QuestTimersEnabled { get; set; } = true;

    [Category("1. Quest timers")]
    [DisplayName("Extra seconds")]
    [Description("Seconds added to each quest timer (1-600)." + Restart)]
    [DefaultValue(60)]
    public int QuestTimerExtraSeconds { get; set; } = 60;

    [Category("1. Quest timers")]
    [DisplayName("Also extend survive/defend quests")]
    [Description("Survive/defend quests are won when the clock runs out, and their enemy waves follow the original length, so extending them only adds waiting time. Off by default." + Restart)]
    [DefaultValue(false)]
    public bool ExtendSurviveDefendQuests { get; set; }

    [Category("2. Side goals")]
    [DisplayName("Replace thrown-object objectives")]
    [Description("Replaces every \"Defeat N foe(s) with thrown objects\" side goal with \"Defeat N foes\". The side goal's reward is unchanged." + Restart)]
    [DefaultValue(true)]
    public bool ReplaceThrownObjectives { get; set; } = true;

    [Category("2. Side goals")]
    [DisplayName("Foes to defeat")]
    [Description("Kill count for the replacement side goal (1-100). Some quests spawn only about 12 foes, so keep it low." + Restart)]
    [DefaultValue(10)]
    public int ReplacementDefeatCount { get; set; } = 10;

    [Category("3. Rewards")]
    [DisplayName("MSP bonus (%)")]
    [Description("Extra mastery points from quest and treasure rewards, rounded up (0 = vanilla, 50 = x1.5, max 900)." + Restart)]
    [DefaultValue(50)]
    public int MspBonusPercent { get; set; } = 50;

    [Category("3. Rewards")]
    [DisplayName("EXP and rupies bonus (%)")]
    [Description("Extra EXP and rupies from quest and treasure rewards, rounded up (0 = vanilla, 20 = x1.2, max 900)." + Restart)]
    [DefaultValue(20)]
    public int ExpRupieBonusPercent { get; set; } = 20;

    [Category("4. Items")]
    [DisplayName("Raise item cap")]
    [Description("Raises the held-item cap for every item category normally capped at 999. If you hold more than 999 of an item and later turn this off, the excess is lost." + Restart)]
    [DefaultValue(true)]
    public bool RaiseItemCap { get; set; } = true;

    [Category("4. Items")]
    [DisplayName("Item cap")]
    [Description("New held-item cap (999-9999)." + Restart)]
    [DefaultValue(9999)]
    public int ItemCap { get; set; } = 9999;

    [Category("5. Fate episodes")]
    [DisplayName("Unlock all fate episodes")]
    [Description("Removes story, level and character-mission requirements from every fate episode. You still need to own the character." + Restart)]
    [DefaultValue(true)]
    public bool UnlockFateEpisodes { get; set; } = true;

    [Category("6. Quest grades")]
    [DisplayName("Easier grades (%)")]
    [Description("Makes every quest grade threshold (S++ and below) easier: more time allowed, fewer kills and less objective HP needed (0 = vanilla, max 90)." + Restart)]
    [DefaultValue(20)]
    public int GradeEasierPercent { get; set; } = 20;

    [Category("7. Story party")]
    [DisplayName("Captain optional in the main story")]
    [Description("The captain no longer has to be in your party during the main story. Cutscenes still show the captain." + Restart)]
    [DefaultValue(true)]
    public bool CaptainOptional { get; set; } = true;

    [Category("8. Crewmate cards")]
    [DisplayName("Cheap crewmate cards")]
    [Description("Treasure Trade at Siero's Knickknack Shack sells the Crewmate Card and the Seofon, Tweyen and Sandalphon cards for Cobblestones, unlimited." + Restart)]
    [DefaultValue(true)]
    public bool CheapCrewmateCards { get; set; } = true;

    [Category("8. Crewmate cards")]
    [DisplayName("Cobblestones per card")]
    [Description("Cost of each card in Cobblestones (1-99)." + Restart)]
    [DefaultValue(1)]
    public int CrewmateCardCost { get; set; } = 1;

    [JsonIgnore, Browsable(false)]
    public string ConfigName { get; private set; } = "Settings";

    [JsonIgnore, Browsable(false)]
    public Action Save { get; private set; } = () => { };

    [Browsable(false)]
    public event Action<IUpdatableConfigurable>? ConfigurationUpdated;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ModSettings Load(string configDirectory)
    {
        string path = Path.Combine(configDirectory, "Config.json");
        ModSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<ModSettings>(File.ReadAllText(path), JsonOptions) ?? new ModSettings()
                : new ModSettings();
        }
        catch (JsonException)
        {
            settings = new ModSettings();
        }

        settings.Save = () =>
        {
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
            settings.ConfigurationUpdated?.Invoke(settings);
        };
        return settings;
    }

    public ModSettings Clamped() => new()
    {
        QuestTimersEnabled = QuestTimersEnabled,
        QuestTimerExtraSeconds = Math.Clamp(QuestTimerExtraSeconds, 1, 600),
        ExtendSurviveDefendQuests = ExtendSurviveDefendQuests,
        ReplaceThrownObjectives = ReplaceThrownObjectives,
        ReplacementDefeatCount = Math.Clamp(ReplacementDefeatCount, 1, 100),
        MspBonusPercent = Math.Clamp(MspBonusPercent, 0, 900),
        ExpRupieBonusPercent = Math.Clamp(ExpRupieBonusPercent, 0, 900),
        RaiseItemCap = RaiseItemCap,
        ItemCap = Math.Clamp(ItemCap, 999, 9999),
        UnlockFateEpisodes = UnlockFateEpisodes,
        GradeEasierPercent = Math.Clamp(GradeEasierPercent, 0, 90),
        CaptainOptional = CaptainOptional,
        CheapCrewmateCards = CheapCrewmateCards,
        CrewmateCardCost = Math.Clamp(CrewmateCardCost, 1, 99),
    };
}

public sealed class ModConfigurator : IConfiguratorV3
{
    private ModSettings? _settings;

    public string? ConfigFolder { get; private set; }
    public string? ModFolder { get; private set; }
    public ConfiguratorContext Context { get; private set; }

    public ModConfigurator() { }

    public ModConfigurator(string configFolder) => ConfigFolder = configFolder;

    public IConfigurable[] GetConfigurations() => [_settings ??= ModSettings.Load(ConfigFolder!)];

    public bool TryRunCustomConfiguration() => false;

    public void Migrate(string oldDirectory, string newDirectory) { }

    public void SetConfigDirectory(string configDirectory) => ConfigFolder = configDirectory;

    public void SetModDirectory(string modDirectory) => ModFolder = modDirectory;

    public void SetContext(in ConfiguratorContext context) => Context = context;
}
