using BepInEx;

namespace Reminiscence;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.reminiscence.mod";
    public const string PluginName = "Monster Train: Reminiscence";
    public const string PluginVersion = "0.1.0";

    private void Awake()
    {
        Logger.LogInfo($"Hello, world. {PluginName} {PluginVersion} loaded.");
    }
}
