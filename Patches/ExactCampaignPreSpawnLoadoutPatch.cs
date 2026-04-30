using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ExactCampaignPreSpawnLoadoutPatch
    {
        private static readonly HashSet<string> LoggedEntryIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> EquipmentInjectedByEntryId = new Dictionary<string, bool>(StringComparer.Ordinal);
        public static bool IsOperationalOnCurrentProcess { get; private set; }

        public static void Apply(Harmony harmony)
        {
            IsOperationalOnCurrentProcess = false;
            EquipmentInjectedByEntryId.Clear();
            try
            {
                var target = AccessTools.Method(
                    typeof(Mission),
                    nameof(Mission.SpawnAgent),
                    new[] { typeof(AgentBuildData), typeof(bool) });
                var prefix = AccessTools.Method(
                    typeof(ExactCampaignPreSpawnLoadoutPatch),
                    nameof(Mission_SpawnAgent_Prefix));
                if (target == null || prefix == null)
                {
                    ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: Mission.SpawnAgent target not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                IsOperationalOnCurrentProcess = true;
                ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: prefix applied to Mission.SpawnAgent.");
            }
            catch (Exception ex)
            {
                IsOperationalOnCurrentProcess = false;
                ModLogger.Error("ExactCampaignPreSpawnLoadoutPatch.Apply failed.", ex);
            }
        }

        private static void Mission_SpawnAgent_Prefix(AgentBuildData agentBuildData, bool spawnFromAgentVisuals)
        {
            if (!ExperimentalFeatures.EnableExactCampaignPreSpawnLoadoutInjection || !GameNetwork.IsServer)
                return;

            if (!(agentBuildData?.AgentOrigin is ExactCampaignSnapshotAgentOrigin exactOrigin))
                return;

            if (string.IsNullOrWhiteSpace(exactOrigin.EntryId))
                return;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(exactOrigin.EntryId);
            if (entryState == null)
                return;

            bool isPlayerControlledOrigin = ((IAgentOriginBase)exactOrigin).IsUnderPlayersCommand;

            string exactEntryCompatibilitySummary;
            string weaponDecisionReason;
            bool includeWeapons = ShouldIncludeWeaponsForPreSpawnInjection(
                exactOrigin,
                isPlayerControlledOrigin,
                entryState,
                out exactEntryCompatibilitySummary,
                out weaponDecisionReason);
            string capeDecisionReason;
            bool includeCape;
            if (isPlayerControlledOrigin)
            {
                includeCape = false;
                capeDecisionReason =
                    "player-controlled origin keeps native template visual slots until local client exact overlay applies";
            }
            else
            {
                includeCape = CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(
                    entryState,
                    out _,
                    out capeDecisionReason);
            }
            bool injectEquipment = includeWeapons || includeCape;
            Equipment exactEquipment = injectEquipment
                ? CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                    entryState,
                    includeWeapons: includeWeapons)
                : null;
            EquipmentInjectedByEntryId[exactOrigin.EntryId] = injectEquipment && exactEquipment != null;
            if (exactEquipment != null)
                agentBuildData.Equipment(exactEquipment);

            CoopMissionSpawnLogic.TraceServerPreSpawnExactHeroContract(
                exactOrigin,
                entryState,
                exactEquipment,
                injectEquipment,
                includeWeapons,
                includeCape,
                weaponDecisionReason,
                capeDecisionReason,
                spawnFromAgentVisuals);

            if (TryResolveEntryBodyProperties(entryState, out BodyProperties bodyProperties))
                agentBuildData.BodyProperties(bodyProperties);

            if (HasHeroIdentity(entryState))
            {
                agentBuildData.IsFemale(entryState.HeroIsFemale);
                if (entryState.HeroAge > 0.01f)
                {
                    int roundedAge = Math.Max(1, Math.Min(120, (int)Math.Round(entryState.HeroAge)));
                    agentBuildData.Age(roundedAge);
                }
            }

            if (!LoggedEntryIds.Add(exactOrigin.EntryId))
                return;

            ModLogger.Info(
                "ExactCampaignPreSpawnLoadoutPatch: injected snapshot loadout into Mission.SpawnAgent. " +
                "EntryId=" + exactOrigin.EntryId +
                " TroopId=" + exactOrigin.TroopId +
                " Hero=" + entryState.IsHero +
                " PlayerControlledOrigin=" + isPlayerControlledOrigin +
                " InjectEquipment=" + (exactEquipment != null) +
                " IncludeWeapons=" + includeWeapons +
                " WeaponDecision=" + weaponDecisionReason +
                " IncludeCape=" + includeCape +
                " CapeDecision=" + capeDecisionReason +
                " " + exactEntryCompatibilitySummary +
                " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                " Equipment=" + (exactEquipment != null ? SummarizeEquipment(exactEquipment) : "(native-template)") +
                " Body=" + (!bodyProperties.Equals(default(BodyProperties))));
        }

        public static bool WasEquipmentInjectedForEntry(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            return EquipmentInjectedByEntryId.TryGetValue(entryId, out bool injected) && injected;
        }

        private static bool ShouldIncludeWeaponsForPreSpawnInjection(
            ExactCampaignSnapshotAgentOrigin exactOrigin,
            bool isPlayerControlledOrigin,
            RosterEntryState entryState,
            out string exactEntryCompatibilitySummary,
            out string decisionReason)
        {
            exactEntryCompatibilitySummary = "ExactEntryContract=(none)";
            if (exactOrigin == null || entryState == null)
            {
                decisionReason = "entry state unavailable";
                return false;
            }

            if (isPlayerControlledOrigin)
            {
                decisionReason =
                    "player-controlled origin keeps native template spawn equipment until remote CreateAgent contract is proven safe";
                return false;
            }

            // Only strict exact hero entries may carry pre-spawn weapons through Mission.SpawnAgent.
            // Bulk native MP agents must keep weapon injection disabled until their runtime path is
            // proven safe, otherwise CreateAgent / SetWieldedItemIndex can desync the client.
            return CoopMissionSpawnLogic.EvaluateExactRuntimePreSpawnWeaponInjectionContract(
                entryState,
                out exactEntryCompatibilitySummary,
                out decisionReason);
        }

        private static bool HasHeroIdentity(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroRole) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroTemplateId) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroBodyProperties));
        }

        private static bool TryResolveEntryBodyProperties(
            RosterEntryState entryState,
            out BodyProperties bodyProperties)
        {
            bodyProperties = default;
            if (string.IsNullOrWhiteSpace(entryState?.HeroBodyProperties))
                return false;

            try
            {
                return BodyProperties.FromString(entryState.HeroBodyProperties, out bodyProperties);
            }
            catch
            {
                return false;
            }
        }

        private static string SummarizeEquipment(Equipment equipment)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon0, "Item0");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon1, "Item1");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon2, "Item2");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon3, "Item3");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Head, "Head");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Body, "Body");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Leg, "Leg");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Gloves, "Gloves");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Cape, "Cape");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Horse, "Horse");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.HorseHarness, "HorseHarness");
            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        private static void AppendEquipmentSummary(
            List<string> parts,
            Equipment equipment,
            EquipmentIndex slot,
            string label)
        {
            EquipmentElement element = equipment[slot];
            if (element.IsEmpty || element.Item == null)
                return;

            parts.Add(label + "=" + element.Item.StringId);
        }
    }
}
