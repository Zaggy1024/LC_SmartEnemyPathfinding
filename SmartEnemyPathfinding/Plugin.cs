using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using PathfindingLib;

using SmartEnemyPathfinding.Patches;

namespace SmartEnemyPathfinding;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency(PathfindingLibPlugin.PluginGUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginName = "SmartEnemyPathfinding";
    public const string PluginGUID = "Zaggy1024." + PluginName;
    public const string PluginVersion = "0.0.1";

    private readonly Harmony harmony = new(PluginGUID);

    internal static Plugin Instance { get; private set; }
    internal new ManualLogSource Logger => base.Logger;

    internal static ConfigEntry<bool> GlobalRoaming;

    public void Awake()
    {
        Instance = this;

        GlobalRoaming = Config.Bind("Masked", "GlobalRoaming", false, "Whether the masked player search routine should take them through fire exits and the main entrance. When enabled, the masked will no longer run straight to the main entrance to leave the interior when no players are inside.");

        harmony.PatchAll(typeof(PatchMaskedPlayerEnemy));
    }
}
