using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator.DedicatedServer.MissionOverrides
{
    internal static class DedicatedKnockoutOutcomeModelOverride
    {
        private static readonly FieldInfo AgentDecideField =
            typeof(MissionGameModels).GetField("<AgentDecideKilledOrUnconsciousModel>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static MissionGameModels _installedModels;
        private static AgentDecideKilledOrUnconsciousModel _originalModel;
        private static OverrideAgentDecideKilledOrUnconsciousModel _overrideModel;
        private static string _lastUpdateDiagnosticKey = string.Empty;

        public static void UpdateForMission(TaleWorlds.MountAndBlade.Mission mission)
        {
            try
            {
                if (!ShouldBeActive(mission))
                {
                    LogUpdateDiagnosticOnce(
                        mission,
                        "inactive",
                        "Reason=" + GetInactiveReason(mission));
                    RestoreIfNeeded();
                    return;
                }

                MissionGameModels missionGameModels = MissionGameModels.Current;
                if (missionGameModels == null)
                {
                    LogUpdateDiagnosticOnce(mission, "models-null", "MissionGameModels.Current=null");
                    return;
                }

                AgentDecideKilledOrUnconsciousModel currentModel = missionGameModels.AgentDecideKilledOrUnconsciousModel;
                if (currentModel == null)
                {
                    LogUpdateDiagnosticOnce(mission, "model-null", "CurrentModel=null");
                    return;
                }

                if (ReferenceEquals(_installedModels, missionGameModels) && ReferenceEquals(currentModel, _overrideModel))
                {
                    LogUpdateDiagnosticOnce(
                        mission,
                        "already-installed",
                        "Model=" + currentModel.GetType().FullName);
                    return;
                }

                LogUpdateDiagnosticOnce(
                    mission,
                    "install-attempt",
                    "MissionGameModels=" + missionGameModels.GetType().FullName +
                    " CurrentModel=" + currentModel.GetType().FullName +
                    " BackingFieldFound=" + (AgentDecideField != null));

                RestoreIfNeeded();

                _installedModels = missionGameModels;
                _originalModel = currentModel;
                _overrideModel = new OverrideAgentDecideKilledOrUnconsciousModel(currentModel);
                SetCurrentModel(missionGameModels, _overrideModel);

                ModLogger.Info(
                    "DedicatedKnockoutOutcomeModelOverride: installed mission knockout model override. " +
                    "Original=" + currentModel.GetType().FullName +
                    " Override=" + _overrideModel.GetType().FullName + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedKnockoutOutcomeModelOverride: update failed: " + ex.Message);
            }
        }

        public static void RestoreIfNeeded()
        {
            try
            {
                if (_installedModels != null && _originalModel != null && ReferenceEquals(GetCurrentModel(_installedModels), _overrideModel))
                {
                    SetCurrentModel(_installedModels, _originalModel);
                    ModLogger.Info("DedicatedKnockoutOutcomeModelOverride: restored original mission knockout model.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedKnockoutOutcomeModelOverride: restore failed: " + ex.Message);
            }
            finally
            {
                _installedModels = null;
                _originalModel = null;
                _overrideModel = null;
            }
        }

        private static bool ShouldBeActive(TaleWorlds.MountAndBlade.Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return false;

            string missionMode = mission.Mode.ToString();
            if (string.Equals(missionMode, "StartUp", StringComparison.OrdinalIgnoreCase) &&
                !IsExactSiegeStartupCasualtyContext())
            {
                return false;
            }

            return true;
        }

        private static bool IsExactSiegeStartupCasualtyContext()
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            return snapshot?.CasualtyRulesVersion == CampaignCasualtyProbabilityCalculator.CurrentRulesVersion &&
                   snapshot.ScenarioContext?.IsSiegeBattle == true &&
                   BattleSnapshotRuntimeState.GetState() != null;
        }

        private static string GetInactiveReason(TaleWorlds.MountAndBlade.Mission mission)
        {
            if (mission == null)
                return "Mission=null";

            if (!GameNetwork.IsServer)
                return "GameNetwork.IsServer=false";

            string missionMode = mission.Mode.ToString();
            if (string.Equals(missionMode, "StartUp", StringComparison.OrdinalIgnoreCase))
            {
                return "MissionMode=StartUp ExactSiegeCasualtyContext=" +
                       IsExactSiegeStartupCasualtyContext();
            }

            return "unknown";
        }

        private static void LogUpdateDiagnosticOnce(TaleWorlds.MountAndBlade.Mission mission, string stage, string details)
        {
            string key =
                (mission?.GetHashCode().ToString() ?? "null") + "|" +
                (mission?.SceneName ?? "null") + "|" +
                (stage ?? "none") + "|" +
                (details ?? string.Empty);
            if (string.Equals(_lastUpdateDiagnosticKey, key, StringComparison.Ordinal))
                return;

            _lastUpdateDiagnosticKey = key;
            ModLogger.Info(
                "DedicatedKnockoutOutcomeModelOverride: update diagnostics. " +
                "Mission=" + (mission?.SceneName ?? "null") +
                " Mode=" + (mission?.Mode.ToString() ?? "null") +
                " Stage=" + (stage ?? "none") +
                " Details=" + (details ?? string.Empty) + ".");
        }

        private static AgentDecideKilledOrUnconsciousModel GetCurrentModel(MissionGameModels missionGameModels)
        {
            return missionGameModels?.AgentDecideKilledOrUnconsciousModel;
        }

        private static void SetCurrentModel(MissionGameModels missionGameModels, AgentDecideKilledOrUnconsciousModel model)
        {
            if (missionGameModels == null)
                throw new ArgumentNullException(nameof(missionGameModels));

            if (AgentDecideField == null)
                throw new InvalidOperationException("MissionGameModels knockout backing field not found.");

            AgentDecideField.SetValue(missionGameModels, model);
        }

        private sealed class OverrideAgentDecideKilledOrUnconsciousModel : AgentDecideKilledOrUnconsciousModel
        {
            private readonly AgentDecideKilledOrUnconsciousModel _inner;

            public OverrideAgentDecideKilledOrUnconsciousModel(AgentDecideKilledOrUnconsciousModel inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public override float GetAgentStateProbability(
                Agent affectorAgent,
                Agent affectedAgent,
                DamageTypes damageType,
                WeaponFlags weaponFlags,
                out float useSurgeryProbability)
            {
                if (TryGetCampaignCasualtyProbability(
                        affectorAgent,
                        affectedAgent,
                        damageType,
                        weaponFlags,
                        out float campaignDeathProbability))
                {
                    useSurgeryProbability = 1f;
                    return campaignDeathProbability;
                }

                float innerUseSurgeryProbability;
                float result = _inner.GetAgentStateProbability(
                    affectorAgent,
                    affectedAgent,
                    damageType,
                    weaponFlags,
                    out innerUseSurgeryProbability);

                if (!ShouldForceBluntKnockout(affectedAgent, damageType, weaponFlags))
                {
                    useSurgeryProbability = innerUseSurgeryProbability;
                    return result;
                }

                useSurgeryProbability = 0f;
                return 0f;
            }

            private static bool TryGetCampaignCasualtyProbability(
                Agent affectorAgent,
                Agent affectedAgent,
                DamageTypes damageType,
                WeaponFlags weaponFlags,
                out float deathProbability)
            {
                deathProbability = 1f;
                if (affectedAgent == null || !affectedAgent.IsHuman || affectedAgent.IsMount)
                    return false;

                BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
                BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
                if (snapshot?.StoryModeTutorialProtectionEnabled == true &&
                    affectedAgent.Team != null &&
                    Mission.Current != null &&
                    Mission.Current.GetMemberCountOfSide(affectedAgent.Team.Side) > 4)
                {
                    deathProbability = 0f;
                    return true;
                }

                if (snapshot?.CasualtyRulesVersion != CampaignCasualtyProbabilityCalculator.CurrentRulesVersion ||
                    snapshot.ScenarioContext?.IsSiegeBattle != true ||
                    runtimeState == null ||
                    !CoopMissionSpawnLogic.TryGetMaterializedBattleResultEntryId(affectedAgent, out string victimEntryId))
                {
                    return false;
                }

                RosterEntryState victimEntry = BattleSnapshotRuntimeState.GetEntryState(victimEntryId);
                if (victimEntry == null)
                    return false;

                RosterEntryState attackerEntry = null;
                if (affectorAgent != null &&
                    affectorAgent.IsHuman &&
                    CoopMissionSpawnLogic.TryGetMaterializedBattleResultEntryId(affectorAgent, out string attackerEntryId))
                {
                    attackerEntry = BattleSnapshotRuntimeState.GetEntryState(attackerEntryId);
                }

                return CampaignCasualtyProbabilityCalculator.TryCalculateDeathProbability(
                    snapshot,
                    runtimeState,
                    victimEntry,
                    attackerEntry,
                    damageType,
                    weaponFlags,
                    out deathProbability);
            }

            private static bool ShouldForceBluntKnockout(Agent affectedAgent, DamageTypes damageType, WeaponFlags weaponFlags)
            {
                if (affectedAgent == null || !affectedAgent.IsHuman || affectedAgent.IsMount)
                    return false;

                if (damageType != DamageTypes.Blunt)
                    return false;

                if ((weaponFlags & WeaponFlags.CanKillEvenIfBlunt) != 0)
                    return false;

                return true;
            }

        }
    }
}
