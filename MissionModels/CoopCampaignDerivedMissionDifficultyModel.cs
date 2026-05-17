using System;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator.MissionModels
{
    /// <summary>
    /// Narrow campaign-derived difficulty bridge for CoopBattle missions.
    /// It applies the campaign "player troops received damage" multiplier
    /// only to agents that belong to the campaign main party in the
    /// authoritative battle snapshot/runtime state.
    /// </summary>
    public sealed class CoopCampaignDerivedMissionDifficultyModel : MissionDifficultyModel
    {
        private readonly MissionDifficultyModel _baseModel;
        private bool _hasLoggedBattleActivation;

        public CoopCampaignDerivedMissionDifficultyModel(MissionDifficultyModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override float GetDamageMultiplierOfCombatDifficulty(Agent victimAgent, Agent attackerAgent = null)
        {
            float baseResult = _baseModel.GetDamageMultiplierOfCombatDifficulty(victimAgent, attackerAgent);
            Agent normalizedVictimAgent = victimAgent != null && victimAgent.IsMount ? victimAgent.RiderAgent : victimAgent;
            Mission mission = normalizedVictimAgent?.Mission ?? attackerAgent?.Mission ?? Mission.Current;
            if (!IsActiveForMission(mission))
                return baseResult;

            if (!TryResolveMainPartyTroopDamageMultiplier(normalizedVictimAgent, out float multiplier))
                return baseResult;

            TryLogBattleActivation(mission, multiplier);
            return multiplier;
        }

        internal static bool IsActiveForMission(Mission mission)
        {
            return mission != null &&
                   MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) &&
                   MissionGameModels.Current?.MissionDifficultyModel is CoopCampaignDerivedMissionDifficultyModel;
        }

        private static bool TryResolveMainPartyTroopDamageMultiplier(Agent victimAgent, out float multiplier)
        {
            multiplier = 1f;
            if (victimAgent == null)
                return false;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState == null)
                return false;

            float configuredMultiplier = runtimeState.PlayerTroopsReceivedDamageMultiplier;
            if (configuredMultiplier <= 0f)
                configuredMultiplier = runtimeState.Snapshot?.PlayerTroopsReceivedDamageMultiplier ?? 1f;

            if (configuredMultiplier <= 0f || Math.Abs(configuredMultiplier - 1f) < 0.0001f)
                return false;

            if (!TryResolveAuthoritativeEntryId(victimAgent, out string entryId))
                return false;

            if (!TryIsMainPartyEntry(runtimeState, entryId))
                return false;

            multiplier = configuredMultiplier;
            return true;
        }

        private static bool TryResolveAuthoritativeEntryId(Agent agent, out string entryId)
        {
            entryId = null;
            if (agent == null)
                return false;

            if (CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                return true;
            }

            if (ExactCampaignArmyBootstrap.TryGetEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                return true;
            }

            entryId = null;
            return false;
        }

        private static bool TryIsMainPartyEntry(BattleRuntimeState runtimeState, string entryId)
        {
            if (runtimeState == null || string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null || string.IsNullOrWhiteSpace(entryState.PartyId))
                return false;

            return runtimeState.PartiesById.TryGetValue(entryState.PartyId, out BattlePartyState partyState) &&
                   partyState != null &&
                   partyState.IsMainParty;
        }

        private void TryLogBattleActivation(Mission mission, float multiplier)
        {
            if (_hasLoggedBattleActivation || mission == null)
                return;

            _hasLoggedBattleActivation = true;
            ModLogger.Info(
                "CoopCampaignDerivedMissionDifficultyModel: activated for CoopBattle mission. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " Multiplier=" + multiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " BaseModel=" + _baseModel.GetType().FullName + ".");
        }
    }
}
