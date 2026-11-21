using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Reminiscence;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.reminiscence.mod";
    public const string PluginName = "Monster Train: Reminiscence";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource LogSource { get; private set; } = null!;
    internal static ConfigEntry<string> Mt1Path { get; private set; } = null!;
    internal static Plugin Instance { get; private set; } = null!;

    private void Awake()
    {
        Instance = this;
        LogSource = Logger;
        Mt1Path = Config.Bind(
            "MT1",
            "InstallPath",
            "",
            "Monster Train 1 install directory. Empty uses MT1_ROOT, then a Monster Train folder beside this game.");

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        if (AccessTools.TypeByName("SoundManager") == null)
            Logger.LogError("SoundManager was not found. Battle music will stay vanilla.");
        new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
        MusicInstaller.OnPluginAwake();
    }

    private void Update()
    {
        StatusNotices.Tick();
    }
}
