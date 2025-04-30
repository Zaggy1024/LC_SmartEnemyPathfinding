using BepInEx;
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

    public void Awake()
    {
        Instance = this;

        harmony.PatchAll(typeof(PatchMaskedPlayerEnemy));
    }
}
