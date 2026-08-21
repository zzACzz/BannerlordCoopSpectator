using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class CoopSiegeFormationMembershipSafetyPatch
    {
        private static readonly object ApplySync = new object();
        private static MethodInfo _lineFormationRemoveUnitWithoutGapMethod;
        private static bool _isApplied;

        public static bool Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            lock (ApplySync)
            {
                if (_isApplied)
                    return true;

                try
                {
                    MethodInfo formationSetter =
                        AccessTools.PropertySetter(typeof(Agent), nameof(Agent.Formation));
                    MethodInfo prefix = AccessTools.Method(
                        typeof(CoopSiegeFormationMembershipSafetyPatch),
                        nameof(Agent_Formation_Setter_Prefix));
                    MethodInfo removeWithoutGapMethod = typeof(LineFormation).GetMethod(
                        "RemoveUnit",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        binder: null,
                        types: new[]
                        {
                            typeof(IFormationUnit),
                            typeof(bool),
                            typeof(bool)
                        },
                        modifiers: null);

                    if (formationSetter == null ||
                        prefix == null ||
                        removeWithoutGapMethod == null ||
                        removeWithoutGapMethod.ReturnType != typeof(void))
                    {
                        ModLogger.Info(
                            "CoopSiegeFormationMembershipSafetyPatch: required Bannerlord 1.4.8 method signature not found. Skip.");
                        return false;
                    }

                    _lineFormationRemoveUnitWithoutGapMethod = removeWithoutGapMethod;
                    harmony.Patch(
                        formationSetter,
                        prefix: new HarmonyMethod(prefix));
                    _isApplied = true;
                    ModLogger.Info(
                        "CoopSiegeFormationMembershipSafetyPatch: Agent.Formation prefix applied.");
                    return true;
                }
                catch (Exception ex)
                {
                    _lineFormationRemoveUnitWithoutGapMethod = null;
                    ModLogger.Error(
                        "CoopSiegeFormationMembershipSafetyPatch.Apply failed.",
                        ex);
                    return false;
                }
            }
        }

        private static bool Agent_Formation_Setter_Prefix(
            Agent __instance,
            Formation __0)
        {
            if (__instance == null ||
                ReferenceEquals(__instance.Formation, __0) ||
                !GameNetwork.IsServer ||
                __instance.IsDetachedFromFormation)
            {
                return true;
            }

            bool hasBoundMissionPeer;
            try
            {
                hasBoundMissionPeer =
                    __instance.MissionPeer != null &&
                    ReferenceEquals(__instance.MissionPeer.ControlledAgent, __instance);
            }
            catch
            {
                hasBoundMissionPeer = false;
            }

            bool isExactCampaignSiege =
                IsExactCampaignSiegeAssault(__instance);
            if (!CoopSiegeFormationMembershipSafetyContract.ShouldInspect(
                    isServer: true,
                    isExactCampaignSiege: isExactCampaignSiege,
                    hasBoundMissionPeer: hasBoundMissionPeer))
            {
                return true;
            }

            Formation currentFormation = __instance.Formation;
            LineFormation arrangement =
                currentFormation?.Arrangement as LineFormation;
            if (arrangement == null)
                return true;

            return TryNormalizeCurrentFormationMembership(
                __instance,
                arrangement);
        }

        private static bool IsExactCampaignSiegeAssault(Agent agent)
        {
            try
            {
                Mission mission = agent?.Mission ?? Mission.Current;
                if (mission == null ||
                    !SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(
                        mission.SceneName ?? string.Empty))
                {
                    return false;
                }

                var scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext ??
                    CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
                return ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .IsSiegeAssaultScenario(scenarioContext);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryNormalizeCurrentFormationMembership(
            Agent agent,
            LineFormation arrangement)
        {
            IFormationUnit formationUnit = agent;
            int storedFileIndex = formationUnit.FormationFileIndex;
            int storedRankIndex = formationUnit.FormationRankIndex;
            var matches =
                new List<CoopSiegeFormationPositionedMatch>();

            int canonicalMatchIndex = -1;
            try
            {
                arrangement.GetFormationInfo(
                    out int fileCount,
                    out int rankCount);
                for (int fileIndex = 0; fileIndex < fileCount; fileIndex++)
                {
                    for (int rankIndex = 0; rankIndex < rankCount; rankIndex++)
                    {
                        if (ReferenceEquals(
                                arrangement.GetUnit(fileIndex, rankIndex),
                                formationUnit))
                        {
                            matches.Add(
                                new CoopSiegeFormationPositionedMatch(
                                    fileIndex,
                                    rankIndex));
                        }
                    }
                }

                if (matches.Count <= 1)
                    return true;

                canonicalMatchIndex =
                    CoopSiegeFormationMembershipSafetyContract
                        .ResolveCanonicalMatchIndex(
                            storedFileIndex,
                            storedRankIndex,
                            matches);
                int[] redundantMatchIndices =
                    CoopSiegeFormationMembershipSafetyContract
                        .ResolveRedundantMatchIndices(
                            canonicalMatchIndex,
                            matches.Count);
                if (canonicalMatchIndex < 0 ||
                    redundantMatchIndices.Length == 0 ||
                    _lineFormationRemoveUnitWithoutGapMethod == null)
                {
                    return false;
                }

                foreach (int redundantMatchIndex in redundantMatchIndices)
                {
                    CoopSiegeFormationPositionedMatch redundantMatch =
                        matches[redundantMatchIndex];
                    formationUnit.FormationFileIndex =
                        redundantMatch.FileIndex;
                    formationUnit.FormationRankIndex =
                        redundantMatch.RankIndex;
                    _lineFormationRemoveUnitWithoutGapMethod.Invoke(
                        arrangement,
                        new object[]
                        {
                            formationUnit,
                            false,
                            false
                        });
                }

                if (!TryRestoreCanonicalCoordinates(
                        arrangement,
                        formationUnit,
                        matches[canonicalMatchIndex]))
                {
                    return false;
                }

                foreach (int redundantMatchIndex in redundantMatchIndices)
                {
                    CoopSiegeFormationPositionedMatch redundantMatch =
                        matches[redundantMatchIndex];
                    if (ReferenceEquals(
                            arrangement.GetUnit(
                                redundantMatch.FileIndex,
                                redundantMatch.RankIndex),
                            formationUnit))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (canonicalMatchIndex >= 0 &&
                    canonicalMatchIndex < matches.Count)
                {
                    TryRestoreCanonicalCoordinates(
                        arrangement,
                        formationUnit,
                        matches[canonicalMatchIndex]);
                }
            }
        }

        private static bool TryRestoreCanonicalCoordinates(
            LineFormation arrangement,
            IFormationUnit formationUnit,
            CoopSiegeFormationPositionedMatch canonicalMatch)
        {
            try
            {
                if (!ReferenceEquals(
                        arrangement.GetUnit(
                            canonicalMatch.FileIndex,
                            canonicalMatch.RankIndex),
                        formationUnit))
                {
                    return false;
                }

                formationUnit.FormationFileIndex =
                    canonicalMatch.FileIndex;
                formationUnit.FormationRankIndex =
                    canonicalMatch.RankIndex;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
