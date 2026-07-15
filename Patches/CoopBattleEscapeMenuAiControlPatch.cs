using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.EscapeMenu;

namespace CoopSpectator.Patches
{
    public static class CoopBattleEscapeMenuAiControlPatch
    {
        private const string TargetTypeName =
            "TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletMultiplayerEscapeMenu";

        private static MethodBase _patchedTarget;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            Type targetType = AccessTools.TypeByName(TargetTypeName);
            MethodInfo target = targetType?.GetMethod(
                "GetEscapeMenuItems",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null || ReferenceEquals(_patchedTarget, target))
                return;

            MethodInfo postfix = typeof(CoopBattleEscapeMenuAiControlPatch).GetMethod(
                nameof(GetEscapeMenuItemsPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            _patchedTarget = target;
            ModLogger.Info("CoopBattleEscapeMenuAiControlPatch: battle escape menu item patch applied.");
        }

        private static void GetEscapeMenuItemsPostfix(
            object __instance,
            ref List<EscapeMenuItemVM> __result)
        {
            if (__instance == null || __result == null || !ShouldAddDelegateControlItem())
                return;

            int insertIndex = Math.Min(1, __result.Count);
            __result.Insert(
                insertIndex,
                new EscapeMenuItemVM(
                    new TextObject("{=CoopDelegateControlToAi}Delegate control to AI"),
                    delegate(object _)
                    {
                        if (!CanDelegateLocalAgentToAi(out Agent controlledAgent, out TextObject unavailableReason))
                        {
                            InformationManager.DisplayMessage(
                                new InformationMessage(
                                    (unavailableReason ?? new TextObject(
                                        "{=CoopDelegateControlUnavailable}The current fighter cannot be delegated to AI."))
                                    .ToString()));
                            return;
                        }

                        int expectedAgentIndex = controlledAgent.Index;
                        TryCloseEscapeMenu(__instance);
                        if (CoopBattleNetworkRequestTransport.TryDelegateCurrentAgentToAi(
                                expectedAgentIndex,
                                "battle escape menu"))
                        {
                            InformationManager.DisplayMessage(
                                new InformationMessage("Coop Battle: transferring control to AI..."));
                        }
                    },
                    null,
                    () =>
                    {
                        bool isDisabled = !CanDelegateLocalAgentToAi(out _, out TextObject unavailableReason);
                        return new Tuple<bool, TextObject>(
                            isDisabled,
                            isDisabled ? unavailableReason : null);
                    },
                    false));
        }

        private static bool ShouldAddDelegateControlItem()
        {
            return TryResolveAgentControlMission(out _);
        }

        private static bool CanDelegateLocalAgentToAi(
            out Agent controlledAgent,
            out TextObject unavailableReason)
        {
            controlledAgent = null;
            unavailableReason = null;
            if (CoopBattleAgentControlRuntimeState.IsClientAiObservationOrTransitionActive())
            {
                unavailableReason = new TextObject(
                    "{=CoopDelegateControlTransitionActive}A control transfer is already in progress.");
                return false;
            }

            if (!TryResolveAgentControlMission(out Mission mission))
            {
                unavailableReason = new TextObject(
                    "{=CoopDelegateControlMissionUnavailable}AI control is unavailable in the current mission.");
                return false;
            }

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status =
                CoopBattleEntryStatusBridgeFile.ReadStatus();
            string statusMissionName = status?.MissionName ?? string.Empty;
            string currentMissionName = mission.SceneName ?? string.Empty;
            bool statusMatchesMission =
                string.IsNullOrWhiteSpace(statusMissionName) ||
                string.Equals(statusMissionName, currentMissionName, StringComparison.OrdinalIgnoreCase);
            if (status == null ||
                !statusMatchesMission ||
                !string.Equals(
                    status.BattlePhase,
                    nameof(CoopBattlePhase.BattleActive),
                    StringComparison.OrdinalIgnoreCase))
            {
                unavailableReason = new TextObject(
                    "{=CoopDelegateControlBattleNotActive}AI control can be delegated after the battle starts.");
                return false;
            }

            if (!TryResolveLocalControlledAgent(mission, out MissionPeer missionPeer, out Agent candidate))
            {
                unavailableReason = new TextObject(
                    "{=CoopDelegateControlAgentUnavailable}The current fighter is not available for AI control.");
                return false;
            }

            if (candidate.MissionPeer != null && !ReferenceEquals(candidate.MissionPeer, missionPeer))
            {
                unavailableReason = new TextObject(
                    "{=CoopDelegateControlOwnershipUnavailable}The current fighter is not owned by your player.");
                return false;
            }

            controlledAgent = candidate;
            return true;
        }

        private static bool TryResolveAgentControlMission(out Mission mission)
        {
            mission = null;
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            mission = Mission.Current;
            return mission != null &&
                   mission.GetMissionBehavior<CoopMissionNetworkBridge>() != null;
        }

        private static bool TryResolveLocalControlledAgent(
            Mission mission,
            out MissionPeer missionPeer,
            out Agent controlledAgent)
        {
            missionPeer = null;
            controlledAgent = null;
            if (mission == null)
                return false;

            missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            controlledAgent = missionPeer?.ControlledAgent ?? Agent.Main;
            return controlledAgent != null &&
                   controlledAgent.IsActive() &&
                   !controlledAgent.IsMount &&
                   ReferenceEquals(controlledAgent.Mission, mission);
        }

        private static void TryCloseEscapeMenu(object instance)
        {
            try
            {
                Type currentType = instance?.GetType();
                while (currentType != null)
                {
                    MethodInfo closeMethod = currentType.GetMethod(
                        "OnEscapeMenuToggled",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(bool) },
                        null);
                    if (closeMethod != null)
                    {
                        closeMethod.Invoke(instance, new object[] { false });
                        return;
                    }

                    currentType = currentType.BaseType;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopBattleEscapeMenuAiControlPatch: failed to close escape menu: " + ex.Message);
            }
        }
    }
}
