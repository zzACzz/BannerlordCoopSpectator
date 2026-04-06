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
        public static bool IsOperationalOnCurrentProcess { get; private set; }

        public static void Apply(Harmony harmony)
        {
            IsOperationalOnCurrentProcess = false;
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

            Equipment exactEquipment = CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(entryState);
            if (exactEquipment != null)
                agentBuildData.Equipment(exactEquipment);

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
                " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                " Equipment=" + SummarizeEquipment(exactEquipment) +
                " Body=" + (!bodyProperties.Equals(default(BodyProperties))));
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
