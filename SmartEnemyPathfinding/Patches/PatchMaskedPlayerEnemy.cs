using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using GameNetcodeStuff;
using HarmonyLib;
using PathfindingLib.API.SmartPathfinding;
using UnityEngine;
using UnityEngine.AI;

using SmartEnemyPathfinding.Utilities.IL;

namespace SmartEnemyPathfinding.Patches;

[HarmonyPatch(typeof(MaskedPlayerEnemy))]
internal static class PatchMaskedPlayerEnemy
{
    private static readonly Dictionary<MaskedPlayerEnemy, SmartPathTask> tasks = [];

    enum GoToDestinationResult
    {
        Success,
        InProgress,
        Failure,
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MaskedPlayerEnemy.Awake))]
    private static void AwakePrefix(MaskedPlayerEnemy __instance)
    {
        var agent = __instance.GetComponentInChildren<NavMeshAgent>();
        if (agent == null)
            return;
        SmartPathfinding.RegisterSmartAgent(agent);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MaskedPlayerEnemy.Start))]
    [HarmonyPatch(nameof(MaskedPlayerEnemy.SetEnemyOutside))]
    private static void ReplaceAINodesPostfix(MaskedPlayerEnemy __instance)
    {
        if (Plugin.GlobalRoaming.Value)
            __instance.allAINodes = [.. GameObject.FindGameObjectsWithTag("OutsideAINode"), .. GameObject.FindGameObjectsWithTag("AINode")];
    }

    private static SmartPathfindingLinkFlags GetAllowedPathLinks()
    {
        var allowedLinks = SmartPathfindingLinkFlags.InternalTeleports | SmartPathfindingLinkFlags.Elevators | SmartPathfindingLinkFlags.MainEntrance;
        if (Plugin.GlobalRoaming.Value)
            allowedLinks |= SmartPathfindingLinkFlags.FireExits;
        return allowedLinks;
    }

    private static void UseTeleport(MaskedPlayerEnemy masked, EntranceTeleport teleport)
    {
        if (teleport.exitPoint == null && !teleport.FindExitPoint())
            return;

        if (Plugin.GlobalRoaming.Value)
        {
            var searchWasInProgress = masked.searchForPlayers.inProgress;
            masked.searchForPlayers.inProgress = false;

            masked.TeleportMaskedEnemyAndSync(teleport.exitPoint.position, setOutside: !teleport.isEntranceToBuilding);

            masked.searchForPlayers.inProgress = searchWasInProgress;
        }
        else
        {
            masked.TeleportMaskedEnemyAndSync(teleport.exitPoint.position, setOutside: !teleport.isEntranceToBuilding);
        }
    }

    private static bool GoToSmartPathDestination(MaskedPlayerEnemy masked, in SmartPathDestination destination)
    {
        if (Vector3.Distance(masked.transform.position, destination.Position) <= 1f && destination.CanActivateDestination(masked.transform.position))
        {
            switch (destination.Type)
            {
                case SmartDestinationType.DirectToDestination:
                    break;
                case SmartDestinationType.InternalTeleport:
                    masked.agent.Warp(destination.InternalTeleport.Destination.position);
                    break;
                case SmartDestinationType.Elevator:
                    destination.ElevatorFloor.CallElevator();
                    break;
                case SmartDestinationType.EntranceTeleport:
                    UseTeleport(masked, destination.EntranceTeleport);
                    break;
                default:
                    return false;
            }
        }

        masked.SetDestinationToPosition(destination.Position);
        return true;
    }

    private static void RoamToSmartPathDestination(EnemyAI maskedAI, in SmartPathDestination destination)
    {
        GoToSmartPathDestination((MaskedPlayerEnemy)maskedAI, destination);
    }

    private static GoToDestinationResult GoToDestination(MaskedPlayerEnemy masked, Vector3 targetPosition)
    {
        var result = GoToDestinationResult.InProgress;

        if (tasks.TryGetValue(masked, out var task))
        {
            if (!task.IsResultReady(0))
                return result;

            if (task.GetResult(0) is SmartPathDestination destination)
                result = GoToSmartPathDestination(masked, in destination) ? GoToDestinationResult.Success : GoToDestinationResult.Failure;
            else
                result = GoToDestinationResult.Failure;
        }
        else
        {
            task = new SmartPathTask();
            tasks[masked] = task;
        }

        task.StartPathTask(masked.agent, masked.transform.position, targetPosition, GetAllowedPathLinks());
        return result;
    }

    private static bool CheckIfPlayersAreTargetable(MaskedPlayerEnemy masked)
    {
        if (Plugin.GlobalRoaming.Value)
            return true;

        if (masked.GetClosestPlayer() == null)
        {
            var result = GoToDestination(masked, RoundManager.FindMainEntrancePosition(getTeleportPosition: true, getOutsideEntrance: !masked.isOutside));

            if (result == GoToDestinationResult.InProgress)
                return false;
            if (result == GoToDestinationResult.Success)
            {
                masked.StopSearch(masked.searchForPlayers);
                return false;
            }
        }

        return true;
    }

    private static void StartSearch(MaskedPlayerEnemy masked, Vector3 searchStart, AISearchRoutine searchRoutine)
    {
        if (searchRoutine == masked.searchForPlayers)
        {
            if (Plugin.GlobalRoaming.Value)
            {
                if (!originalSearchWidths.ContainsKey(masked))
                    originalSearchWidths[masked] = searchRoutine.searchWidth;
                searchRoutine.searchWidth = float.PositiveInfinity;
            }
            else if (originalSearchWidths.TryGetValue(masked, out var originalSearchWidth))
            {
                searchRoutine.searchWidth = originalSearchWidth;
                originalSearchWidths.Remove(masked);
            }
        }

        masked.StartSmartSearch(searchStart, GetAllowedPathLinks(), RoamToSmartPathDestination, searchRoutine);
    }

    private static void PathToPlayer(MaskedPlayerEnemy masked, PlayerControllerB player)
    {
        if (tasks.TryGetValue(masked, out var task))
        {
            if (!task.IsResultReady(0))
                return;

            if (task.GetResult(0) is SmartPathDestination destination)
            {
                if (destination.Type == SmartDestinationType.DirectToDestination)
                    masked.SetMovingTowardsTargetPlayer(player);
                else
                    GoToSmartPathDestination(masked, in destination);
            }
        }
        else
        {
            task = new SmartPathTask();
            tasks[masked] = task;
        }

        task.StartPathTask(masked.agent, masked.transform.position, player.transform.position, GetAllowedPathLinks());
        return;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(nameof(MaskedPlayerEnemy.DoAIInterval))]
    private static IEnumerable<CodeInstruction> DoAIIntervalTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        //   if (Time.realtimeSinceStartup - timeAtLastUsingEntrance > 3) {
        // -   [...]
        // +   if (!CheckIfPlayersAreTargetable(this))
        // +     return;
        //   }
        var injector = new ILInjector(instructions, generator)
            .Find([
                ILMatcher.Call(typeof(Time).GetProperty(nameof(Time.realtimeSinceStartup)).GetMethod),
                ILMatcher.Ldarg(0),
                ILMatcher.Ldfld(typeof(MaskedPlayerEnemy).GetField(nameof(MaskedPlayerEnemy.timeAtLastUsingEntrance), BindingFlags.NonPublic | BindingFlags.Instance)),
                ILMatcher.Opcode(OpCodes.Sub),
                ILMatcher.LdcF32(3f),
                ILMatcher.Opcode(OpCodes.Ble_Un).CaptureOperandAs(out Label skipUsingEntranceLabel),
            ])
            .GoToMatchEnd();

        if (!injector.IsValid)
        {
            Plugin.Instance.Logger.LogError($"Failed to find the code block to make masked path through the entrance in {nameof(MaskedPlayerEnemy)}.{nameof(MaskedPlayerEnemy.DoAIInterval)}().");
            return instructions;
        }

        injector
            .FindLabel(skipUsingEntranceLabel)
            .DefineLabel(out var skipReturnLabel)
            .ReplaceLastMatch([
                new(OpCodes.Ldarg_0),
                new(OpCodes.Call, typeof(PatchMaskedPlayerEnemy).GetMethod(nameof(CheckIfPlayersAreTargetable), BindingFlags.NonPublic | BindingFlags.Static, [typeof(MaskedPlayerEnemy)])),
                new(OpCodes.Brtrue_S, skipReturnLabel),
                new(OpCodes.Ret),
            ])
            .GoToMatchEnd()
            .AddLabel(skipReturnLabel);

        // - this.StartSearch([...])
        // + PatchMaskedPlayerEnemy.StartSearch(this, [...])
        injector.GoToStart();
        while (true)
        {
            injector
                .Find([
                    ILMatcher.Call(typeof(EnemyAI).GetMethod(nameof(EnemyAI.StartSearch), [typeof(Vector3), typeof(AISearchRoutine)])),
                ]);

            if (!injector.IsValid)
                break;

            injector
                .ReplaceLastMatch([
                    new(OpCodes.Call, typeof(PatchMaskedPlayerEnemy).GetMethod(nameof(StartSearch), BindingFlags.NonPublic | BindingFlags.Static, [typeof(MaskedPlayerEnemy), typeof(Vector3), typeof(AISearchRoutine)])),
                ])
                .GoToMatchEnd();
        }

        // - this.SetMovingTowardsTargetPlayer(player)
        // + PatchMaskedPlayerEnemy.PathToPlayer(this, player)
        injector.GoToStart();
        while (true)
        {
            injector
                .Find([
                    ILMatcher.Call(typeof(EnemyAI).GetMethod(nameof(EnemyAI.SetMovingTowardsTargetPlayer), [typeof(PlayerControllerB)])),
                ]);

            if (!injector.IsValid)
                break;

            injector
                .ReplaceLastMatch([
                    new(OpCodes.Call, typeof(PatchMaskedPlayerEnemy).GetMethod(nameof(PathToPlayer), BindingFlags.NonPublic | BindingFlags.Static, [typeof(MaskedPlayerEnemy), typeof(PlayerControllerB)])),
                ])
                .GoToMatchEnd();
        }

        return injector
            .ReleaseInstructions();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MaskedPlayerEnemy.OnDestroy))]
    private static void OnDestroyPostfix(MaskedPlayerEnemy __instance)
    {
        tasks.Remove(__instance);
    }
}
