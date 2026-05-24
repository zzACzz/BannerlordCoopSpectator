using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Campaign
{
    internal static class CampaignFieldBattleExportBridge
    {
        public static CanonicalBattleContract Build(BattleStartMessage source)
        {
            if (!ExperimentalFeatures.EnableCanonicalFieldBattleContract || source?.Snapshot == null)
                return null;

            if (!IsEligibleFieldBattle(source))
                return null;

            BattleSnapshotMessage snapshot = source.Snapshot;
            var contract = new CanonicalBattleContract
            {
                Context = new CanonicalBattleContext
                {
                    BattleId = snapshot.BattleId,
                    BattleType = snapshot.BattleType,
                    CampaignScene = source.MapScene,
                    WorldMapScene = source.WorldMapScene,
                    MultiplayerScene = source.MultiplayerScene,
                    MultiplayerGameType = source.MultiplayerGameType,
                    PlayerSide = source.PlayerSide,
                    BattleSizeBudget = source.BattleSizeBudget,
                    ReinforcementWaveCount = source.ReinforcementWaveCount,
                    PlayerTroopsReceivedDamageMultiplier = snapshot.PlayerTroopsReceivedDamageMultiplier,
                    MapPatchSceneIndex = source.MapPatchSceneIndex,
                    MapPatchNormalizedX = source.MapPatchNormalizedX,
                    MapPatchNormalizedY = source.MapPatchNormalizedY,
                    HasPatchEncounterDirection = source.HasPatchEncounterDirection,
                    PatchEncounterDirX = source.PatchEncounterDirX,
                    PatchEncounterDirY = source.PatchEncounterDirY,
                    PatchEncounterDirectionSource = source.PatchEncounterDirectionSource
                }
            };

            foreach (BattleSideSnapshotMessage side in snapshot.Sides ?? Enumerable.Empty<BattleSideSnapshotMessage>())
            {
                if (side == null)
                    continue;

                contract.Sides.Add(BuildSide(side));
                contract.TroopInstances.AddRange(BuildTroopInstancesForSide(side, contract.Sides[contract.Sides.Count - 1]));
            }

            ModLogger.Info(
                "CampaignFieldBattleExportBridge: canonical contract built. " +
                "BattleId=" + (contract.Context?.BattleId ?? "unknown") +
                " Sides=" + contract.Sides.Count +
                " TroopInstances=" + contract.TroopInstances.Count +
                " MultiplayerScene=" + (contract.Context?.MultiplayerScene ?? "unknown") + ".");

            return contract;
        }

        private static bool IsEligibleFieldBattle(BattleStartMessage source)
        {
            return string.Equals(source?.MultiplayerGameType, "Battle", StringComparison.OrdinalIgnoreCase);
        }

        private static CanonicalBattleSide BuildSide(BattleSideSnapshotMessage side)
        {
            var result = new CanonicalBattleSide
            {
                SideId = side.SideId,
                SideText = side.SideText,
                LeaderPartyId = side.LeaderPartyId,
                SideMorale = side.SideMorale,
                IsPlayerSide = side.IsPlayerSide,
                TotalManCount = side.TotalManCount
            };

            foreach (BattlePartySnapshotMessage party in side.Parties ?? Enumerable.Empty<BattlePartySnapshotMessage>())
            {
                if (party == null)
                    continue;

                result.Parties.Add(new CanonicalBattleParty
                {
                    PartyId = party.PartyId,
                    PartyName = party.PartyName,
                    IsMainParty = party.IsMainParty,
                    TotalManCount = party.TotalManCount,
                    Modifiers = CloneModifiers(party.Modifiers)
                });
            }

            return result;
        }

        private static List<CanonicalTroopInstance> BuildTroopInstancesForSide(
            BattleSideSnapshotMessage side,
            CanonicalBattleSide canonicalSide)
        {
            var instances = new List<CanonicalTroopInstance>();
            var healthyByEntry = new Dictionary<string, Queue<CanonicalTroopInstance>>(StringComparer.OrdinalIgnoreCase);
            var allHealthy = new List<CanonicalTroopInstance>();
            int woundedOrdinalSeed = 0;

            foreach (TroopStackInfo troop in side?.Troops ?? Enumerable.Empty<TroopStackInfo>())
            {
                if (troop == null || troop.Count <= 0)
                    continue;

                int healthyCount = Math.Max(0, troop.Count - troop.WoundedCount);
                int woundedCount = Math.Max(0, Math.Min(troop.Count, troop.WoundedCount));

                if (healthyCount > 0)
                {
                    Queue<CanonicalTroopInstance> queue = GetOrCreateQueue(healthyByEntry, troop.EntryId);
                    for (int ordinal = 0; ordinal < healthyCount; ordinal++)
                    {
                        CanonicalTroopInstance instance = BuildTroopInstance(
                            troop,
                            isMissionParticipant: true,
                            isPreBattleWounded: false,
                        stableOrdinalWithinEntry: ordinal,
                        missionOrderIndex: -1,
                        instanceIdSource: "healthy_entry_ordinal");
                        queue.Enqueue(instance);
                        allHealthy.Add(instance);
                    }
                }

                for (int ordinal = 0; ordinal < woundedCount; ordinal++)
                {
                    instances.Add(BuildTroopInstance(
                        troop,
                        isMissionParticipant: false,
                        isPreBattleWounded: true,
                        stableOrdinalWithinEntry: woundedOrdinalSeed++,
                        missionOrderIndex: -1,
                        instanceIdSource: "pre_battle_wounded_ordinal"));
                }
            }

            int missionOrderIndex = 0;
            foreach (MissionReadyDescriptorMessage descriptor in side?.MissionReadyDescriptors ?? Enumerable.Empty<MissionReadyDescriptorMessage>())
            {
                string entryId = descriptor?.EntryId;
                if (!healthyByEntry.TryGetValue(entryId ?? string.Empty, out Queue<CanonicalTroopInstance> queue) || queue.Count == 0)
                    continue;

                CanonicalTroopInstance instance = queue.Dequeue();
                instance.MissionOrderIndex = missionOrderIndex++;
                instance.InstanceIdSource = descriptor != null && descriptor.DescriptorSeed > 0
                    ? "campaign_descriptor_seed"
                    : "mission_ready_entry_ordinal";
                if (descriptor != null && descriptor.DescriptorSeed > 0)
                {
                    instance.CampaignTroopDescriptorSeed = descriptor.DescriptorSeed;
                    instance.CampaignTroopDescriptorDebugText = descriptor.DescriptorDebugText;
                    instance.InstanceId = BuildDescriptorBackedInstanceId(instance, descriptor.DescriptorSeed);
                }
                canonicalSide.MissionReadyInstanceOrder.Add(instance.InstanceId);
                instances.Add(instance);
            }

            if ((side?.MissionReadyDescriptors?.Count ?? 0) == 0)
            {
                foreach (string entryId in side?.MissionReadyEntryOrder ?? Enumerable.Empty<string>())
                {
                    if (!healthyByEntry.TryGetValue(entryId ?? string.Empty, out Queue<CanonicalTroopInstance> queue) || queue.Count == 0)
                        continue;

                    CanonicalTroopInstance instance = queue.Dequeue();
                    instance.MissionOrderIndex = missionOrderIndex++;
                    instance.InstanceIdSource = "mission_ready_entry_ordinal";
                    canonicalSide.MissionReadyInstanceOrder.Add(instance.InstanceId);
                    instances.Add(instance);
                }
            }

            foreach (CanonicalTroopInstance unresolvedHealthy in allHealthy.Where(candidate => candidate.MissionOrderIndex < 0))
            {
                unresolvedHealthy.MissionOrderIndex = missionOrderIndex++;
                unresolvedHealthy.InstanceIdSource = "healthy_fallback_ordinal";
                instances.Add(unresolvedHealthy);
            }

            return instances
                .OrderBy(instance => instance.IsPreBattleWounded ? 1 : 0)
                .ThenBy(instance => instance.MissionOrderIndex < 0 ? int.MaxValue : instance.MissionOrderIndex)
                .ThenBy(instance => instance.EntryId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(instance => instance.StableOrdinalWithinEntry)
                .ToList();
        }

        private static Queue<CanonicalTroopInstance> GetOrCreateQueue(
            IDictionary<string, Queue<CanonicalTroopInstance>> index,
            string entryId)
        {
            string key = entryId ?? string.Empty;
            if (!index.TryGetValue(key, out Queue<CanonicalTroopInstance> queue))
            {
                queue = new Queue<CanonicalTroopInstance>();
                index[key] = queue;
            }

            return queue;
        }

        private static CanonicalTroopInstance BuildTroopInstance(
            TroopStackInfo troop,
            bool isMissionParticipant,
            bool isPreBattleWounded,
            int stableOrdinalWithinEntry,
            int missionOrderIndex,
            string instanceIdSource)
        {
            return new CanonicalTroopInstance
            {
                InstanceId = BuildTroopInstanceId(troop, isPreBattleWounded, stableOrdinalWithinEntry),
                InstanceIdSource = instanceIdSource,
                SideId = troop.SideId,
                PartyId = troop.PartyId,
                EntryId = troop.EntryId,
                CharacterId = troop.CharacterId,
                OriginalCharacterId = troop.OriginalCharacterId,
                SpawnTemplateId = troop.SpawnTemplateId,
                TroopName = troop.TroopName,
                CultureId = troop.CultureId,
                HeroId = troop.HeroId,
                HeroRole = troop.HeroRole,
                HeroOccupationId = troop.HeroOccupationId,
                HeroClanId = troop.HeroClanId,
                HeroTemplateId = troop.HeroTemplateId,
                HeroBodyProperties = troop.HeroBodyProperties,
                HeroLevel = troop.HeroLevel,
                HeroAge = troop.HeroAge,
                HeroIsFemale = troop.HeroIsFemale,
                IsHero = troop.IsHero,
                Tier = troop.Tier,
                IsMounted = troop.IsMounted,
                IsRanged = troop.IsRanged,
                HasShield = troop.HasShield,
                HasThrown = troop.HasThrown,
                AttributeVigor = troop.AttributeVigor,
                AttributeControl = troop.AttributeControl,
                AttributeEndurance = troop.AttributeEndurance,
                SkillOneHanded = troop.SkillOneHanded,
                SkillTwoHanded = troop.SkillTwoHanded,
                SkillPolearm = troop.SkillPolearm,
                SkillBow = troop.SkillBow,
                SkillCrossbow = troop.SkillCrossbow,
                SkillThrowing = troop.SkillThrowing,
                SkillRiding = troop.SkillRiding,
                SkillAthletics = troop.SkillAthletics,
                BaseHitPoints = troop.BaseHitPoints,
                IsMissionParticipant = isMissionParticipant,
                IsPreBattleWounded = isPreBattleWounded,
                MissionOrderIndex = missionOrderIndex,
                StableOrdinalWithinEntry = stableOrdinalWithinEntry,
                PerkIds = troop.PerkIds != null ? new List<string>(troop.PerkIds) : new List<string>(),
                Equipment = CloneEquipment(troop)
            };
        }

        private static string BuildTroopInstanceId(TroopStackInfo troop, bool isPreBattleWounded, int stableOrdinalWithinEntry)
        {
            string entryId = string.IsNullOrWhiteSpace(troop?.EntryId)
                ? ((troop?.SideId ?? "side") + "|" + (troop?.PartyId ?? "party") + "|" + (troop?.OriginalCharacterId ?? troop?.CharacterId ?? "troop"))
                : troop.EntryId;
            string state = isPreBattleWounded ? "wounded" : "ready";
            return entryId + "|" + state + "|" + stableOrdinalWithinEntry;
        }

        private static string BuildDescriptorBackedInstanceId(CanonicalTroopInstance instance, int descriptorSeed)
        {
            string entryId = string.IsNullOrWhiteSpace(instance?.EntryId)
                ? ((instance?.SideId ?? "side") + "|" + (instance?.PartyId ?? "party") + "|" + (instance?.OriginalCharacterId ?? instance?.CharacterId ?? "troop"))
                : instance.EntryId;
            return entryId + "|descriptor|" + descriptorSeed;
        }

        private static CanonicalEquipmentSpec CloneEquipment(TroopStackInfo troop)
        {
            if (troop == null)
                return new CanonicalEquipmentSpec();

            return new CanonicalEquipmentSpec
            {
                CombatItem0Id = troop.CombatItem0Id,
                CombatItem0Amount = troop.CombatItem0Amount,
                CombatItem1Id = troop.CombatItem1Id,
                CombatItem1Amount = troop.CombatItem1Amount,
                CombatItem2Id = troop.CombatItem2Id,
                CombatItem2Amount = troop.CombatItem2Amount,
                CombatItem3Id = troop.CombatItem3Id,
                CombatItem3Amount = troop.CombatItem3Amount,
                CombatHeadId = troop.CombatHeadId,
                CombatBodyId = troop.CombatBodyId,
                CombatLegId = troop.CombatLegId,
                CombatGlovesId = troop.CombatGlovesId,
                CombatCapeId = troop.CombatCapeId,
                CombatHorseId = troop.CombatHorseId,
                CombatHorseHarnessId = troop.CombatHorseHarnessId
            };
        }

        private static CanonicalBattlePartyModifiers CloneModifiers(BattlePartyModifierSnapshotMessage modifiers)
        {
            if (modifiers == null)
                return new CanonicalBattlePartyModifiers();

            return new CanonicalBattlePartyModifiers
            {
                LeaderHeroId = modifiers.LeaderHeroId,
                OwnerHeroId = modifiers.OwnerHeroId,
                ScoutHeroId = modifiers.ScoutHeroId,
                QuartermasterHeroId = modifiers.QuartermasterHeroId,
                EngineerHeroId = modifiers.EngineerHeroId,
                SurgeonHeroId = modifiers.SurgeonHeroId,
                Morale = modifiers.Morale,
                RecentEventsMorale = modifiers.RecentEventsMorale,
                MoraleChange = modifiers.MoraleChange,
                ContributionToBattle = modifiers.ContributionToBattle,
                LeaderLeadershipSkill = modifiers.LeaderLeadershipSkill,
                LeaderTacticsSkill = modifiers.LeaderTacticsSkill,
                ScoutScoutingSkill = modifiers.ScoutScoutingSkill,
                QuartermasterStewardSkill = modifiers.QuartermasterStewardSkill,
                EngineerEngineeringSkill = modifiers.EngineerEngineeringSkill,
                SurgeonMedicineSkill = modifiers.SurgeonMedicineSkill,
                PartyLeaderPerkIds = modifiers.PartyLeaderPerkIds != null ? new List<string>(modifiers.PartyLeaderPerkIds) : new List<string>(),
                ArmyCommanderPerkIds = modifiers.ArmyCommanderPerkIds != null ? new List<string>(modifiers.ArmyCommanderPerkIds) : new List<string>(),
                CaptainPerkIds = modifiers.CaptainPerkIds != null ? new List<string>(modifiers.CaptainPerkIds) : new List<string>(),
                ScoutPerkIds = modifiers.ScoutPerkIds != null ? new List<string>(modifiers.ScoutPerkIds) : new List<string>(),
                QuartermasterPerkIds = modifiers.QuartermasterPerkIds != null ? new List<string>(modifiers.QuartermasterPerkIds) : new List<string>(),
                EngineerPerkIds = modifiers.EngineerPerkIds != null ? new List<string>(modifiers.EngineerPerkIds) : new List<string>(),
                SurgeonPerkIds = modifiers.SurgeonPerkIds != null ? new List<string>(modifiers.SurgeonPerkIds) : new List<string>()
            };
        }
    }
}
