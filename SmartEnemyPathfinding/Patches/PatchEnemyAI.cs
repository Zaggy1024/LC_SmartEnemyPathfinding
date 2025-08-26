using HarmonyLib;

namespace SmartEnemyPathfinding.Patches;

[HarmonyPatch(typeof(EnemyAI))]
internal static class PatchEnemyAI
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(EnemyAI.NavigateTowardsTargetPlayer))]
    private static bool NavigateTowardsTargetPlayerPrefix(EnemyAI __instance)
    {
        if (__instance is MaskedPlayerEnemy masked)
            return PatchMaskedPlayerEnemy.NavigateTowardsTargetPlayerPrefix(masked);
        return true;
    }
}
