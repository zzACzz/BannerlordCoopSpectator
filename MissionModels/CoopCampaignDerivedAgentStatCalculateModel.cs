using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.MissionModels
{
    /// <summary>
    /// Thin low-level wrapper over the active MP AgentStatCalculateModel.
    /// Phase 1 keeps the stable MP runtime behavior intact, but swaps in
    /// campaign-derived effective skills for entries that already have an
    /// exact combat profile or exact roster snapshot in CoopBattle.
    /// </summary>
    public sealed class CoopCampaignDerivedAgentStatCalculateModel : AgentStatCalculateModel
    {
        private readonly AgentStatCalculateModel _baseModel;
        private readonly ConcurrentDictionary<string, byte> _loggedExactSkillKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _loggedWeaponDamageKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _loggedRangedDrivenKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _loggedExactMaxHealthKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _loggedExactDefenseDrivenKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _loggedExactMeleeDrivenKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _loggedCampaignArmorKeys = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, float> _exactDefenseDrivenPropertyBaselines = new ConcurrentDictionary<string, float>(StringComparer.Ordinal);
        private int _hasLoggedBattleActivation;

        public CoopCampaignDerivedAgentStatCalculateModel(AgentStatCalculateModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override void InitializeAgentStats(Agent agent, Equipment spawnEquipment, AgentDrivenProperties agentDrivenProperties, AgentBuildData agentBuildData)
        {
            _baseModel.InitializeAgentStats(agent, spawnEquipment, agentDrivenProperties, agentBuildData);
            TryApplyCampaignEquipmentArmor(agent, spawnEquipment, agentDrivenProperties);
        }

        public override void InitializeMissionEquipment(Agent agent)
        {
            _baseModel.InitializeMissionEquipment(agent);
        }

        public override void InitializeAgentStatsAfterDeploymentFinished(Agent agent)
        {
            _baseModel.InitializeAgentStatsAfterDeploymentFinished(agent);
        }

        public override void InitializeMissionEquipmentAfterDeploymentFinished(Agent agent)
        {
            _baseModel.InitializeMissionEquipmentAfterDeploymentFinished(agent);
        }

        public override void UpdateAgentStats(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            _baseModel.UpdateAgentStats(agent, agentDrivenProperties);
            TryApplyCampaignEquipmentArmor(agent, agent?.SpawnEquipment, agentDrivenProperties);
            bool exactRangedDrivenPropertiesApplied = TryApplyExactRangedCampaignDrivenPropertyOverrides(agent, agentDrivenProperties);
            TryApplyExactDefenseDrivenPropertyOverrides(agent, agentDrivenProperties);
            TryApplyExactMeleeDrivenPropertyOverrides(agent, agentDrivenProperties);
            if (!exactRangedDrivenPropertiesApplied)
                TryApplyExactRangedDrivenPropertyOverrides(agent, agentDrivenProperties);
            TryApplyExactPersonalDrivenPropertyOverrides(agent, agentDrivenProperties);
            TryApplyGlobalCaptainDrivenProperties(agent, agentDrivenProperties);
        }

        public override float GetDifficultyModifier()
        {
            return _baseModel.GetDifficultyModifier();
        }

        public override bool CanAgentRideMount(Agent agent, Agent targetMount)
        {
            return _baseModel.CanAgentRideMount(agent, targetMount);
        }

        public override bool HasHeavyArmor(Agent agent)
        {
            return _baseModel.HasHeavyArmor(agent);
        }

        public override float GetEffectiveArmorEncumbrance(Agent agent, Equipment equipment)
        {
            return _baseModel.GetEffectiveArmorEncumbrance(agent, equipment);
        }

        public override float GetEffectiveMaxHealth(Agent agent)
        {
            float baseHealth = _baseModel.GetEffectiveMaxHealth(agent);
            if (!TryResolveExactMaxHealthOverride(agent, baseHealth, out float exactHealth, out string entryId))
                return baseHealth;

            TryLogBattleActivation(agent);
            TryLogExactMaxHealthOverride(agent, entryId, baseHealth, exactHealth);
            return exactHealth;
        }

        public override float GetEnvironmentSpeedFactor(Agent agent)
        {
            return _baseModel.GetEnvironmentSpeedFactor(agent);
        }

        public override float GetWeaponInaccuracy(Agent agent, WeaponComponentData weapon, int weaponSkill)
        {
            float inaccuracy;
            if (TryResolveExactRangedSkillForWeapon(agent, weapon, out int exactSkill, out _))
                inaccuracy = ComputeCampaignRangedWeaponInaccuracy(weapon, exactSkill);
            else
                inaccuracy = _baseModel.GetWeaponInaccuracy(agent, weapon, weaponSkill);

            if (weapon?.RelevantSkill == DefaultSkills.Bow &&
                TryResolveGlobalCaptainEntryId(agent, out string entryId))
            {
                var accumulator = new CaptainPerkBonusAccumulator(inaccuracy);
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "BowQuickAdjustments", accumulator);
                if (accumulator.HasEffects)
                    inaccuracy = accumulator.Result;
            }

            return Math.Max(0f, inaccuracy);
        }

        public override float GetDetachmentCostMultiplierOfAgent(Agent agent, IDetachment detachment)
        {
            return _baseModel.GetDetachmentCostMultiplierOfAgent(agent, detachment);
        }

        public override float GetInteractionDistance(Agent agent)
        {
            if (agent != null &&
                (agent.HasMount || agent.MountAgent != null) &&
                HasExactHeroPerk(agent, "ThrowingLongReach"))
            {
                return 3f;
            }

            return _baseModel.GetInteractionDistance(agent);
        }

        public override float GetMaxCameraZoom(Agent agent)
        {
            WeaponComponentData weapon = ResolveCurrentWeapon(agent);
            string skillId = ResolveWeaponDamageRelevantSkill(weapon)?.StringId ?? string.Empty;
            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) &&
                HasExactHeroPerk(agent, "BowEagleEye"))
            {
                return 1.5f;
            }

            if (string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase) &&
                HasExactHeroPerk(agent, "CrossbowLongShots"))
            {
                return 2f;
            }

            if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase) &&
                HasExactHeroPerk(agent, "ThrowingFocus"))
            {
                return 1.25f;
            }

            return _baseModel.GetMaxCameraZoom(agent);
        }

        public override int GetEffectiveSkill(Agent agent, SkillObject skill)
        {
            int fallbackSkill = _baseModel.GetEffectiveSkill(agent, skill);
            int resolvedSkill = fallbackSkill;
            if (TryResolveExactSkillOverride(agent, skill, fallbackSkill, out int exactSkill, out string entryId))
            {
                TryLogExactSkillOverride(agent, skill, fallbackSkill, exactSkill, entryId);
                resolvedSkill = exactSkill;
            }

            return ApplyGlobalCaptainSkillEffects(agent, skill, resolvedSkill);
        }

        public override int GetEffectiveSkillForWeapon(Agent agent, WeaponComponentData weapon)
        {
            SkillObject weaponRelevantSkill = ResolveWeaponDamageRelevantSkill(weapon);
            if (weapon == null || weaponRelevantSkill == null)
                return _baseModel.GetEffectiveSkillForWeapon(agent, weapon);

            int fallbackWeaponSkill = _baseModel.GetEffectiveSkillForWeapon(agent, weapon);
            if (TryResolveExactRangedSkillForWeapon(agent, weapon, out int exactRangedSkill, out string exactRangedEntryId))
            {
                TryLogExactSkillOverride(agent, weaponRelevantSkill, fallbackWeaponSkill, exactRangedSkill, exactRangedEntryId);
                return exactRangedSkill;
            }

            int desiredSkill = GetEffectiveSkill(agent, weaponRelevantSkill);
            if (desiredSkill <= 0)
                return fallbackWeaponSkill;

            if (weapon.IsRangedWeapon)
            {
                MPPerkObject.MPPerkHandler perkHandler = MPPerkObject.GetPerkHandler(agent);
                if (perkHandler != null)
                    desiredSkill = TaleWorlds.Library.MathF.Ceiling(desiredSkill * (perkHandler.GetRangedAccuracy() + 1f));
            }

            return desiredSkill;
        }

        public override float GetWeaponDamageMultiplier(Agent agent, WeaponComponentData weapon)
        {
            float damageMultiplier = CampaignCombatProfileAgentStatsPatch.InvokeWithoutWeaponDamagePostfix(
                () => _baseModel.GetWeaponDamageMultiplier(agent, weapon));

            if (!TryResolveExactWeaponDamageOverride(agent, weapon, damageMultiplier, out float updatedMultiplier, out string skillId, out string entryId))
                return damageMultiplier;

            TryLogWeaponDamageOverride(agent, weapon, skillId, entryId, damageMultiplier, updatedMultiplier);
            return updatedMultiplier;
        }

        public override float GetEquipmentStealthBonus(Agent agent)
        {
            return _baseModel.GetEquipmentStealthBonus(agent);
        }

        public override float GetSneakAttackMultiplier(Agent agent, WeaponComponentData weapon)
        {
            return _baseModel.GetSneakAttackMultiplier(agent, weapon);
        }

        public override float GetKnockBackResistance(Agent agent)
        {
            return _baseModel.GetKnockBackResistance(agent);
        }

        public override float GetKnockDownResistance(Agent agent, StrikeType strikeType = StrikeType.Invalid)
        {
            return _baseModel.GetKnockDownResistance(agent, strikeType);
        }

        public override float GetDismountResistance(Agent agent)
        {
            return _baseModel.GetDismountResistance(agent);
        }

        public override float GetBreatheHoldMaxDuration(Agent agent, float baseBreatheHoldMaxDuration)
        {
            return _baseModel.GetBreatheHoldMaxDuration(agent, baseBreatheHoldMaxDuration);
        }

        public override string GetMissionDebugInfoForAgent(Agent agent)
        {
            return _baseModel.GetMissionDebugInfoForAgent(agent);
        }

        private static int ApplyGlobalCaptainSkillEffects(Agent agent, SkillObject skill, int baseSkill)
        {
            if (agent == null || skill == null || !TryResolveGlobalCaptainEntryId(agent, out string entryId))
                return baseSkill;

            var accumulator = new CaptainPerkBonusAccumulator(baseSkill);
            string skillId = skill.StringId ?? string.Empty;
            bool isMounted = agent.HasMount || agent.MountAgent != null;
            bool isMeleeSkill =
                string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase);
            bool isRangedSkill =
                string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase);

            if ((agent.Character?.IsInfantry == true && isRangedSkill) ||
                (agent.Character?.IsRanged == true && isMeleeSkill))
            {
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "ThrowingFlexibleFighter", accumulator);
            }

            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "BowDeadAim", accumulator);
                if (isMounted)
                    GlobalCaptainPerkRuntimeState.AddEffect(entryId, "BowHorseMaster", accumulator);
            }
            else if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "AthleticsStrongArms", accumulator);
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "ThrowingRunningThrow", accumulator);
            }
            else if (string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase))
            {
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "CrossbowDonkeysSwiftness", accumulator);
            }
            else if (string.Equals(skillId, "Riding", StringComparison.OrdinalIgnoreCase) && isMounted)
            {
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "RidingNimbleStead", accumulator);
            }

            if (!isMounted)
            {
                if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
                    GlobalCaptainPerkRuntimeState.AddEffect(entryId, "OneHandedWrappedHandles", accumulator);
                else if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
                    GlobalCaptainPerkRuntimeState.AddEffect(entryId, "TwoHandedStrongGrip", accumulator);
                else if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
                {
                    GlobalCaptainPerkRuntimeState.AddEffect(entryId, "PolearmCleanThrust", accumulator);
                    GlobalCaptainPerkRuntimeState.AddEffect(entryId, "PolearmCounterWeight", accumulator);
                }
            }

            return accumulator.HasEffects ? Math.Max(0, (int)accumulator.Result) : baseSkill;
        }

        private static void TryApplyGlobalCaptainDrivenProperties(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null || !agent.IsHuman ||
                !TryResolveGlobalCaptainEntryId(agent, out string entryId))
            {
                return;
            }

            bool isMounted = agent.HasMount || agent.MountAgent != null;
            WeaponComponentData weapon = ResolveCurrentWeapon(agent);
            string skillId = weapon?.RelevantSkill?.StringId ?? string.Empty;
            int troopTier = Math.Max(0, BattleSnapshotRuntimeState.GetEntryState(entryId)?.Tier ?? 0);

            if (!isMounted)
            {
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.HandlingMultiplier,
                    "AthleticsFury");
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.SwingSpeedMultiplier,
                    "TwoHandedOnTheEdge",
                    "TwoHandedBladeMaster",
                    "PolearmSwiftSwing");
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.ThrustOrRangedReadySpeedMultiplier,
                    "TwoHandedBladeMaster");
            }

            if (isMounted)
            {
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.WeaponWorstMobileAccuracyPenalty,
                    "RidingSagittarius");
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.WeaponWorstUnsteadyAccuracyPenalty,
                    "RidingSagittarius");
            }

            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                ApplyGlobalCaptainEffects(entryId, agentDrivenProperties, DrivenProperty.ReloadSpeed, "BowRapidFire");
                if (!isMounted)
                {
                    ApplyGlobalCaptainEffects(
                        entryId,
                        agentDrivenProperties,
                        DrivenProperty.ReloadMovementPenaltyFactor,
                        "BowNockingPoint");
                }
            }
            else if (string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase))
            {
                ApplyGlobalCaptainEffects(entryId, agentDrivenProperties, DrivenProperty.ReloadSpeed, "CrossbowWindWinder");
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.WeaponWorstMobileAccuracyPenalty,
                    "CrossbowLooseAndMove");
            }
            else if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                ApplyGlobalCaptainEffects(entryId, agentDrivenProperties, DrivenProperty.ReloadSpeed, "ThrowingQuickDraw");
                ApplyGlobalCaptainEffects(entryId, agentDrivenProperties, DrivenProperty.MissileSpeedMultiplier, "ThrowingPerfectTechnique");
            }

            if (agent.Formation != null && (int)agent.Formation.ArrangementOrder.OrderEnum == 5)
            {
                ApplyGlobalCaptainEffects(
                    entryId,
                    agentDrivenProperties,
                    DrivenProperty.AttributeShieldMissileCollisionBodySizeAdder,
                    "OneHandedShieldWall");
            }
            ApplyGlobalCaptainEffects(
                entryId,
                agentDrivenProperties,
                DrivenProperty.AttributeShieldMissileCollisionBodySizeAdder,
                "OneHandedArrowCatcher");

            var movementPerks = new List<string>
            {
                "AthleticsMorningExercise",
                "OneHandedShieldBearer",
                "OneHandedFleetOfFoot",
                "TwoHandedRecklessCharge",
                "PolearmFootwork"
            };
            if (troopTier >= 3)
                movementPerks.Add("AthleticsFormFittingArmor");
            if (agent.Character?.IsInfantry == true)
                movementPerks.Add("AthleticsSprint");
            if (agent.Formation != null && agent.Formation.CountOfUnits <= 15)
                movementPerks.Add("TacticsSmallUnitTactics");
            if (agent.Mission?.HasValidTerrainType == true)
            {
                int terrainType = (int)agent.Mission.TerrainType;
                if (terrainType == 3 || terrainType == 4)
                    movementPerks.Add("TacticsExtendedSkirmish");
                else if (terrainType == 1 || terrainType == 2 || terrainType == 5)
                    movementPerks.Add("TacticsDecisiveBattle");
            }
            ApplyGlobalCaptainEffects(
                entryId,
                agentDrivenProperties,
                DrivenProperty.MaxSpeedMultiplier,
                movementPerks.ToArray());

        }

        private static void ApplyGlobalCaptainEffects(
            string entryId,
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty property,
            params string[] perkIds)
        {
            if (string.IsNullOrWhiteSpace(entryId) || agentDrivenProperties == null || perkIds == null)
                return;

            var accumulator = new CaptainPerkBonusAccumulator(agentDrivenProperties.GetStat(property));
            foreach (string perkId in perkIds)
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, perkId, accumulator);

            if (accumulator.HasEffects)
                agentDrivenProperties.SetStat(property, Math.Max(0f, accumulator.Result));
        }

        private static bool TryResolveGlobalCaptainEntryId(Agent agent, out string entryId)
        {
            entryId = null;
            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            return agent != null &&
                GlobalCaptainPerkRuntimeState.AreAgentStatEffectsReadyFor(state) &&
                CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId);
        }

        private void TryApplyCampaignEquipmentArmor(
            Agent agent,
            Equipment spawnEquipment,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || spawnEquipment == null || agentDrivenProperties == null ||
                agent.Mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(agent.Mission.SceneName))
            {
                return;
            }

            if (agent.IsHuman)
            {
                bool isMounted = agent.HasMount || agent.MountAgent != null;
                bool hasPersonalIgnorePain =
                    !isMounted &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, "AthleticsIgnorePain", out _);
                float personalArmorFactor = hasPersonalIgnorePain ? 1.1f : 1f;
                string entryId = null;
                TryResolveGlobalCaptainEntryId(agent, out entryId);

                float baseHeadArmor = spawnEquipment.GetHeadArmorSum();
                float baseTorsoArmor = spawnEquipment.GetHumanBodyArmorSum();
                float baseArmArmor = spawnEquipment.GetArmArmorSum();
                float baseLegArmor = spawnEquipment.GetLegArmorSum();
                float finalHeadArmor = ResolveCampaignHumanArmor(
                    baseHeadArmor,
                    entryId,
                    isMounted,
                    personalArmorFactor);
                float finalTorsoArmor = ResolveCampaignHumanArmor(
                    baseTorsoArmor,
                    entryId,
                    isMounted,
                    personalArmorFactor);
                float finalArmArmor = ResolveCampaignHumanArmor(
                    baseArmArmor,
                    entryId,
                    isMounted,
                    personalArmorFactor);
                float finalLegArmor = ResolveCampaignHumanArmor(
                    baseLegArmor,
                    entryId,
                    isMounted,
                    personalArmorFactor);

                agentDrivenProperties.ArmorHead = finalHeadArmor;
                agentDrivenProperties.ArmorTorso = finalTorsoArmor;
                agentDrivenProperties.ArmorArms = finalArmArmor;
                agentDrivenProperties.ArmorLegs = finalLegArmor;
                TryLogCampaignHumanArmor(
                    agent,
                    entryId,
                    isMounted,
                    hasPersonalIgnorePain,
                    baseHeadArmor,
                    baseTorsoArmor,
                    baseArmArmor,
                    baseLegArmor,
                    finalHeadArmor,
                    finalTorsoArmor,
                    finalArmArmor,
                    finalLegArmor);
                return;
            }

            if (!agent.IsMount)
                return;

            float mountArmor = 0f;
            for (int slotIndex = 1; slotIndex < (int)EquipmentIndex.NumEquipmentSetSlots; slotIndex++)
            {
                EquipmentElement equipmentElement = spawnEquipment[(EquipmentIndex)slotIndex];
                if (equipmentElement.Item != null)
                    mountArmor += equipmentElement.GetModifiedMountBodyArmor();
            }

            float baseMountArmor = mountArmor;
            Agent riderAgent = agent.RiderAgent;
            string riderEntryId = null;
            bool captainToughSteedApplied = false;
            bool personalToughSteedApplied = false;
            if (riderAgent != null)
            {
                if (TryResolveGlobalCaptainEntryId(riderAgent, out riderEntryId))
                {
                    var captainAccumulator = new CaptainPerkBonusAccumulator(mountArmor);
                    captainToughSteedApplied = GlobalCaptainPerkRuntimeState.AddEffect(
                        riderEntryId,
                        "RidingToughSteed",
                        captainAccumulator);
                    if (captainAccumulator.HasEffects)
                        mountArmor = captainAccumulator.Result;
                }

                personalToughSteedApplied =
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(riderAgent, "RidingToughSteed", out _);
                if (personalToughSteedApplied)
                    mountArmor *= 1.2f;
            }

            float finalMountArmor = Math.Max(0f, mountArmor);
            agentDrivenProperties.ArmorTorso = finalMountArmor;
            TryLogCampaignMountArmor(
                agent,
                riderAgent,
                riderEntryId,
                spawnEquipment,
                baseMountArmor,
                finalMountArmor,
                captainToughSteedApplied,
                personalToughSteedApplied);
        }

        private static float ResolveCampaignHumanArmor(
            float equipmentArmor,
            string entryId,
            bool isMounted,
            float personalArmorFactor)
        {
            float armor = Math.Max(0f, equipmentArmor);
            if (!string.IsNullOrWhiteSpace(entryId))
            {
                var accumulator = new CaptainPerkBonusAccumulator(armor);
                GlobalCaptainPerkRuntimeState.AddEffect(
                    entryId,
                    isMounted ? "RidingDauntlessSteed" : "AthleticsIgnorePain",
                    accumulator);
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "EngineeringMetallurgy", accumulator);
                if (accumulator.HasEffects)
                    armor = accumulator.Result;
            }

            return Math.Max(0f, armor * Math.Max(0f, personalArmorFactor));
        }

        private void TryLogCampaignHumanArmor(
            Agent agent,
            string entryId,
            bool isMounted,
            bool hasPersonalIgnorePain,
            float baseHeadArmor,
            float baseTorsoArmor,
            float baseArmArmor,
            float baseLegArmor,
            float finalHeadArmor,
            float finalTorsoArmor,
            float finalArmArmor,
            float finalLegArmor)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics ||
                !CoopDebugConfig.CombatModelDiagnostics ||
                agent == null)
                return;

            string captainPerks = BuildCaptainArmorEffectSummary(
                entryId,
                isMounted ? "RidingDauntlessSteed" : "AthleticsIgnorePain",
                "EngineeringMetallurgy");
            string logKey =
                "human|" + agent.Index + "|" + (entryId ?? string.Empty) + "|" + isMounted + "|" +
                FormatDiagnosticFloat(baseHeadArmor) + "|" + FormatDiagnosticFloat(baseTorsoArmor) + "|" +
                FormatDiagnosticFloat(baseArmArmor) + "|" + FormatDiagnosticFloat(baseLegArmor) + "|" +
                FormatDiagnosticFloat(finalHeadArmor) + "|" + FormatDiagnosticFloat(finalTorsoArmor) + "|" +
                FormatDiagnosticFloat(finalArmArmor) + "|" + FormatDiagnosticFloat(finalLegArmor) + "|" +
                captainPerks + "|" + hasPersonalIgnorePain;
            if (!_loggedCampaignArmorKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: campaign human armor sample. " +
                "Agent=" + agent.Index +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Mounted=" + isMounted +
                " BaseHead=" + FormatDiagnosticFloat(baseHeadArmor) +
                " BaseTorso=" + FormatDiagnosticFloat(baseTorsoArmor) +
                " BaseArms=" + FormatDiagnosticFloat(baseArmArmor) +
                " BaseLegs=" + FormatDiagnosticFloat(baseLegArmor) +
                " FinalHead=" + FormatDiagnosticFloat(finalHeadArmor) +
                " FinalTorso=" + FormatDiagnosticFloat(finalTorsoArmor) +
                " FinalArms=" + FormatDiagnosticFloat(finalArmArmor) +
                " FinalLegs=" + FormatDiagnosticFloat(finalLegArmor) +
                " PersonalPerks=" + (hasPersonalIgnorePain ? "AthleticsIgnorePain=1.1" : "none") +
                " CaptainPerks=" + captainPerks +
                " Mission=" + (agent.Mission?.SceneName ?? "null") + ".");
        }

        private void TryLogCampaignMountArmor(
            Agent mountAgent,
            Agent riderAgent,
            string riderEntryId,
            Equipment spawnEquipment,
            float baseMountArmor,
            float finalMountArmor,
            bool captainToughSteedApplied,
            bool personalToughSteedApplied)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics ||
                !CoopDebugConfig.CombatModelDiagnostics ||
                mountAgent == null)
                return;

            string horseId = spawnEquipment?[EquipmentIndex.Horse].Item?.StringId ?? "none";
            string harnessId = spawnEquipment?[EquipmentIndex.HorseHarness].Item?.StringId ?? "none";
            string captainPerk = captainToughSteedApplied
                ? BuildCaptainArmorEffectSummary(riderEntryId, "RidingToughSteed")
                : "none";
            string logKey =
                "mount|" + mountAgent.Index + "|" + (riderEntryId ?? string.Empty) + "|" + horseId + "|" + harnessId + "|" +
                FormatDiagnosticFloat(baseMountArmor) + "|" + FormatDiagnosticFloat(finalMountArmor) + "|" +
                captainPerk + "|" + personalToughSteedApplied;
            if (!_loggedCampaignArmorKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: campaign mount armor sample. " +
                "MountAgent=" + mountAgent.Index +
                " RiderAgent=" + (riderAgent?.Index ?? -1) +
                " RiderEntryId=" + (string.IsNullOrWhiteSpace(riderEntryId) ? "unknown" : riderEntryId) +
                " Horse=" + horseId +
                " Harness=" + harnessId +
                " BaseArmor=" + FormatDiagnosticFloat(baseMountArmor) +
                " FinalArmor=" + FormatDiagnosticFloat(finalMountArmor) +
                " PersonalPerks=" + (personalToughSteedApplied ? "RidingToughSteed=1.2" : "none") +
                " CaptainPerks=" + captainPerk +
                " Mission=" + (mountAgent.Mission?.SceneName ?? "null") + ".");
        }

        private static string BuildCaptainArmorEffectSummary(string entryId, params string[] perkIds)
        {
            if (string.IsNullOrWhiteSpace(entryId) || perkIds == null || perkIds.Length == 0)
                return "none";

            var summaries = new List<string>();
            foreach (string perkId in perkIds)
            {
                if (GlobalCaptainPerkRuntimeState.TryGetEffect(entryId, perkId, out var effect))
                {
                    summaries.Add(
                        perkId + "=" + FormatDiagnosticFloat(effect.Bonus) +
                        "(" + (string.IsNullOrWhiteSpace(effect.IncrementType) ? "additive" : effect.IncrementType) + ")");
                }
            }

            return summaries.Count > 0 ? string.Join(",", summaries) : "none";
        }

        private static string FormatDiagnosticFloat(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static WeaponComponentData ResolveCurrentWeapon(Agent agent)
        {
            if (agent?.Equipment == null)
                return null;

            EquipmentIndex wieldedIndex = agent.GetPrimaryWieldedItemIndex();
            if (wieldedIndex == EquipmentIndex.None)
                return null;

            return agent.Equipment[wieldedIndex].CurrentUsageItem;
        }

        private bool TryResolveExactSkillOverride(
            Agent agent,
            SkillObject skill,
            int fallbackSkill,
            out int exactSkill,
            out string entryId)
        {
            exactSkill = fallbackSkill;
            entryId = string.Empty;

            if (agent == null || skill == null)
                return false;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(
                    agent,
                    skill,
                    out int profileSkillValue,
                    out entryId))
            {
                TryLogBattleActivation(agent);
                exactSkill = profileSkillValue;
                return true;
            }

            if (!TryResolveExactRosterEntrySkillValue(agent, skill, out int rosterSkillValue, out entryId))
                return false;

            TryLogBattleActivation(agent);
            exactSkill = rosterSkillValue;
            return true;
        }

        private static bool TryResolveExactRosterEntrySkillValue(
            Agent agent,
            SkillObject skill,
            out int skillValue,
            out string entryId)
        {
            skillValue = 0;
            entryId = string.Empty;
            if (agent == null || skill == null)
                return false;

            if (!CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                !CoopMissionSpawnLogic.TryResolveSelectableEntryId(agent, out entryId) &&
                !ExactTransferContractRuntimeCache.TryGetEntryIdByRiderAgentIndex(agent.Index, out entryId))
            {
                return false;
            }

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null || entryState.IsHero)
                return false;

            string skillId = skill.StringId ?? string.Empty;
            if (string.Equals(skillId, DefaultSkills.Bow.StringId, StringComparison.OrdinalIgnoreCase))
                skillValue = entryState.SkillBow;
            else if (string.Equals(skillId, DefaultSkills.Crossbow.StringId, StringComparison.OrdinalIgnoreCase))
                skillValue = entryState.SkillCrossbow;
            else if (string.Equals(skillId, DefaultSkills.Throwing.StringId, StringComparison.OrdinalIgnoreCase))
                skillValue = entryState.SkillThrowing;
            else if (string.Equals(skillId, DefaultSkills.Riding.StringId, StringComparison.OrdinalIgnoreCase))
                skillValue = entryState.SkillRiding;
            else
                return false;

            skillValue = Math.Max(0, skillValue);
            return true;
        }

        private void TryLogBattleActivation(Agent agent)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics || agent?.Mission == null)
                return;

            if (Interlocked.CompareExchange(ref _hasLoggedBattleActivation, 1, 0) != 0)
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: activated for CoopBattle mission. " +
                "Scene=" + (agent.Mission.SceneName ?? "null") +
                " BaseModel=" + _baseModel.GetType().FullName + ".");
        }

        private void TryLogExactSkillOverride(
            Agent agent,
            SkillObject skill,
            int fallbackSkill,
            int exactSkill,
            string entryId)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                return;

            string skillId = skill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactSkill.ToString();

            if (!_loggedExactSkillKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact skill override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + skillId +
                " Base=" + fallbackSkill +
                " Exact=" + exactSkill +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        internal static bool IsActiveForMission(Mission mission)
        {
            return mission != null &&
                MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) &&
                MissionGameModels.Current?.AgentStatCalculateModel is CoopCampaignDerivedAgentStatCalculateModel;
        }

        private bool TryResolveExactWeaponDamageOverride(
            Agent agent,
            WeaponComponentData weapon,
            float baseMultiplier,
            out float updatedMultiplier,
            out string skillId,
            out string entryId)
        {
            updatedMultiplier = baseMultiplier;
            skillId = "null";
            entryId = string.Empty;

            if (agent == null || weapon == null)
                return false;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (IsExactSiegeBallistaProjectileWeapon(mission, weapon))
                return false;

            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(weapon);
            if (relevantSkill == null)
                return false;

            skillId = relevantSkill.StringId ?? "null";
            if (!CoopMissionSpawnLogic.TryGetMaterializedCombatProfileSkillValue(
                    agent,
                    relevantSkill,
                    out int materializedSkill,
                    out entryId))
            {
                return false;
            }

            int effectiveSkill = ApplyGlobalCaptainSkillEffects(agent, relevantSkill, materializedSkill);
            float candidateMultiplier = ComputeCampaignWeaponDamageMultiplier(relevantSkill, effectiveSkill);
            if (candidateMultiplier <= 0f)
                return false;

            if (Math.Abs(candidateMultiplier - baseMultiplier) < 0.0001f)
                return false;

            updatedMultiplier = candidateMultiplier;
            return true;
        }

        private static float ComputeCampaignWeaponDamageMultiplier(SkillObject relevantSkill, int effectiveSkill)
        {
            if (relevantSkill == null || effectiveSkill <= 0)
                return 1f;

            string skillId = relevantSkill.StringId ?? string.Empty;
            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
                return 1f + effectiveSkill * 0.0015f;
            if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
                return 1f + effectiveSkill * 0.0016f;
            if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
                return 1f + effectiveSkill * 0.0007f;
            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase))
                return 1f + effectiveSkill * 0.0011f;
            if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
                return 1f + effectiveSkill * 0.0006f;

            return 1f;
        }

        private static bool IsExactSiegeBallistaProjectileWeapon(Mission mission, WeaponComponentData weapon)
        {
            if (mission == null || weapon == null)
                return false;

            return SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(mission.SceneName ?? string.Empty) &&
                weapon.WeaponClass == WeaponClass.Arrow &&
                weapon.MissileDamage >= 1500 &&
                weapon.MissileSpeed >= 100;
        }

        private void TryLogWeaponDamageOverride(
            Agent agent,
            WeaponComponentData weapon,
            string skillId,
            string entryId,
            float baseMultiplier,
            float updatedMultiplier)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics ||
                !CoopDebugConfig.CombatModelDiagnostics)
                return;

            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                (skillId ?? "null") + "|" +
                (entryId ?? string.Empty) + "|" +
                updatedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedWeaponDamageKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact weapon damage override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + (string.IsNullOrWhiteSpace(skillId) ? "null" : skillId) +
                " WeaponClass=" + weapon.WeaponClass +
                " Base=" + baseMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Exact=" + updatedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        private static SkillObject ResolveWeaponDamageRelevantSkill(WeaponComponentData weapon)
        {
            SkillObject relevantSkill = weapon?.RelevantSkill;
            if (relevantSkill != null)
            {
                string relevantSkillId = relevantSkill.StringId;
                if (string.Equals(relevantSkillId, "OneHanded", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "TwoHanded", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "Polearm", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase))
                {
                    return relevantSkill;
                }
            }

            if (weapon == null)
                return null;

            switch (weapon.WeaponClass)
            {
                case WeaponClass.OneHandedSword:
                case WeaponClass.OneHandedAxe:
                case WeaponClass.Mace:
                case WeaponClass.Dagger:
                case WeaponClass.OneHandedPolearm:
                    return DefaultSkills.OneHanded;
                case WeaponClass.TwoHandedSword:
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.TwoHandedMace:
                    return DefaultSkills.TwoHanded;
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return DefaultSkills.Polearm;
                case WeaponClass.Bow:
                    return DefaultSkills.Bow;
                case WeaponClass.Crossbow:
                    return DefaultSkills.Crossbow;
                case WeaponClass.Javelin:
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Sling:
                case WeaponClass.Stone:
                case WeaponClass.SlingStone:
                    return DefaultSkills.Throwing;
                default:
                    return relevantSkill;
            }
        }

        private bool TryResolveExactMaxHealthOverride(
            Agent agent,
            float baseHealth,
            out float exactHealth,
            out string entryId)
        {
            exactHealth = baseHealth;
            entryId = string.Empty;

            if (agent == null)
                return false;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            Agent humanAgent = agent.IsMount ? agent.RiderAgent : agent;
            if (humanAgent == null ||
                !CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(humanAgent, out entryId))
            {
                return false;
            }

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null)
                return false;

            BattlePartyState partyState = null;
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.PartiesById != null && !string.IsNullOrWhiteSpace(entryState.PartyId))
                runtimeState.PartiesById.TryGetValue(entryState.PartyId, out partyState);

            float candidateHealth;
            if (agent.IsMount)
            {
                candidateHealth = ResolveExactCampaignMountHealth(baseHealth, humanAgent, partyState);
            }
            else if (CoopMissionSpawnLogic.TryGetExactHeroCombatProfileBaseHitPoints(
                         humanAgent,
                         out int exactBaseHitPoints,
                         out _))
            {
                candidateHealth = exactBaseHitPoints;
            }
            else
            {
                candidateHealth = ResolveExactCampaignRegularTroopHealth(
                    baseHealth,
                    agent,
                    entryState,
                    partyState);
            }

            candidateHealth = Math.Max(1f, candidateHealth);
            if (Math.Abs(candidateHealth - baseHealth) < 0.0001f)
                return false;

            exactHealth = candidateHealth;
            return true;
        }

        private void TryLogExactMaxHealthOverride(
            Agent agent,
            string entryId,
            float baseHealth,
            float exactHealth)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                return;

            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                (entryId ?? string.Empty) + "|" +
                exactHealth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedExactMaxHealthKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact max health override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Base=" + baseHealth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Exact=" + exactHealth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        private void TryApplyExactDefenseDrivenPropertyOverrides(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null || !agent.IsHuman)
                return;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            bool applied = false;
            string entryId = string.Empty;
            string summary = string.Empty;

            if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, "AthleticsFormFittingArmor", out string formFittingEntryId))
            {
                float baseArmorEncumbrance = GetExactDefenseDrivenPropertyBaseline(
                    agent,
                    agentDrivenProperties,
                    DrivenProperty.ArmorEncumbrance);
                float exactArmorEncumbrance = baseArmorEncumbrance * 0.85f;
                if (TrySetDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorEncumbrance, exactArmorEncumbrance))
                {
                    entryId = formFittingEntryId;
                    summary = AppendAppliedPerkSummary(
                        summary,
                        "FormFittingArmor ArmorEncumbrance=" +
                        baseArmorEncumbrance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                        "->" +
                        exactArmorEncumbrance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    applied = true;
                }
            }

            if (!applied)
                return;

            TryLogBattleActivation(agent);
            TryLogDefenseDrivenOverride(agent, entryId, summary);
        }

        private bool TryApplyExactDefenseDrivenPropertyScale(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty property,
            float scaleFactor)
        {
            if (agentDrivenProperties == null || scaleFactor <= 0f)
                return false;

            float baselineValue = GetExactDefenseDrivenPropertyBaseline(agent, agentDrivenProperties, property);
            if (baselineValue <= 0f)
                return false;

            float desiredValue = Math.Max(0f, baselineValue * scaleFactor);
            return TrySetDrivenProperty(agentDrivenProperties, property, desiredValue);
        }

        private float GetExactDefenseDrivenPropertyBaseline(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty property)
        {
            string key = BuildExactDefenseDrivenPropertyBaselineKey(agent, property);
            if (!string.IsNullOrWhiteSpace(key) &&
                _exactDefenseDrivenPropertyBaselines.TryGetValue(key, out float cachedBaseline) &&
                cachedBaseline > 0f)
            {
                return cachedBaseline;
            }

            float resolvedBaseline = ResolveExactDefenseDrivenPropertyBaseline(agent, agentDrivenProperties, property);
            if (!string.IsNullOrWhiteSpace(key) && resolvedBaseline > 0f)
                return _exactDefenseDrivenPropertyBaselines.GetOrAdd(key, resolvedBaseline);

            return resolvedBaseline;
        }

        private static float ResolveExactDefenseDrivenPropertyBaseline(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty property)
        {
            if (agentDrivenProperties == null)
                return 0f;

            float currentValue = agentDrivenProperties.GetStat(property);
            if (currentValue > 0.0001f)
                return currentValue;

            Equipment spawnEquipment = agent?.SpawnEquipment;
            if (spawnEquipment == null)
                return Math.Max(0f, currentValue);

            switch (property)
            {
                case DrivenProperty.ArmorEncumbrance:
                    return Math.Max(0f, spawnEquipment.GetTotalWeightOfArmor(agent.IsHuman));
                case DrivenProperty.ArmorHead:
                    return Math.Max(0f, spawnEquipment.GetHeadArmorSum());
                case DrivenProperty.ArmorTorso:
                    return Math.Max(0f, spawnEquipment.GetHumanBodyArmorSum());
                case DrivenProperty.ArmorArms:
                    return Math.Max(0f, spawnEquipment.GetArmArmorSum());
                case DrivenProperty.ArmorLegs:
                    return Math.Max(0f, spawnEquipment.GetLegArmorSum());
                default:
                    return Math.Max(0f, currentValue);
            }
        }

        private static string BuildExactDefenseDrivenPropertyBaselineKey(Agent agent, DrivenProperty property)
        {
            if (agent == null)
                return string.Empty;

            int missionHash = agent.Mission?.GetHashCode() ?? 0;
            return missionHash.ToString() + "|" +
                agent.Index.ToString() + "|" +
                (agent.Character?.StringId ?? "null") + "|" +
                ((int)property).ToString();
        }

        private void TryLogDefenseDrivenOverride(Agent agent, string entryId, string summary)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                return;

            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                (entryId ?? string.Empty) + "|" +
                (summary ?? string.Empty);

            if (!_loggedExactDefenseDrivenKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact defense driven-property override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Applied=" + (string.IsNullOrWhiteSpace(summary) ? "none" : summary) +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        private void TryApplyExactMeleeDrivenPropertyOverrides(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null || !agent.IsHuman)
                return;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            MissionEquipment equipment = agent.Equipment;
            if (equipment == null)
                return;

            EquipmentIndex primaryWieldedItemIndex = agent.GetPrimaryWieldedItemIndex();
            if (primaryWieldedItemIndex == EquipmentIndex.None)
                return;

            WeaponComponentData primaryWeapon = equipment[primaryWieldedItemIndex].CurrentUsageItem;
            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(primaryWeapon);
            if (relevantSkill == null)
                return;

            string skillId = relevantSkill.StringId ?? string.Empty;
            if (!string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!TryResolveExactSkillOverride(agent, relevantSkill, 0, out int exactSkill, out string entryId))
                return;

            int templateSkill = TryGetCharacterSkillValue(agent.Character, relevantSkill);
            float perSkillFactor = ResolvePersonalWeaponSpeedFactorPerSkill(skillId);
            if (perSkillFactor <= 0f)
                return;

            float baseSkillFactor = 1f + templateSkill * perSkillFactor;
            float exactSkillFactor = 1f + exactSkill * perSkillFactor;
            if (baseSkillFactor <= 0.0001f || Math.Abs(exactSkillFactor - baseSkillFactor) < 0.0001f)
                return;

            float baseSwingSpeed = agentDrivenProperties.SwingSpeedMultiplier;
            float baseReadySpeed = agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier;
            float updatedSwingSpeed = Math.Max(0f, baseSwingSpeed / baseSkillFactor * exactSkillFactor);
            float updatedReadySpeed = Math.Max(0f, baseReadySpeed / baseSkillFactor * exactSkillFactor);

            bool applied = false;
            if (Math.Abs(updatedSwingSpeed - baseSwingSpeed) >= 0.0001f)
            {
                agentDrivenProperties.SwingSpeedMultiplier = updatedSwingSpeed;
                applied = true;
            }

            if (Math.Abs(updatedReadySpeed - baseReadySpeed) >= 0.0001f)
            {
                agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier = updatedReadySpeed;
                applied = true;
            }

            if (!applied)
                return;

            TryLogBattleActivation(agent);
            TryLogExactMeleeDrivenOverride(
                agent,
                relevantSkill,
                entryId,
                baseSwingSpeed,
                updatedSwingSpeed,
                baseReadySpeed,
                updatedReadySpeed,
                templateSkill,
                exactSkill);
        }

        private void TryLogExactMeleeDrivenOverride(
            Agent agent,
            SkillObject relevantSkill,
            string entryId,
            float baseSwingSpeed,
            float exactSwingSpeed,
            float baseReadySpeed,
            float exactReadySpeed,
            int templateSkill,
            int exactSkill)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                return;

            string skillId = relevantSkill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactSwingSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                exactReadySpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedExactMeleeDrivenKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact melee driven-property override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + skillId +
                " TemplateSkill=" + templateSkill +
                " ExactSkill=" + exactSkill +
                " SwingSpeedMultiplier=" + baseSwingSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactSwingSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " ReadySpeedMultiplier=" + baseReadySpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactReadySpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        private static float ResolvePersonalWeaponSpeedFactorPerSkill(string skillId)
        {
            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
                return 0.0007f;
            if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
                return 0.0006f;
            if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
                return 0.0006f;

            return 0f;
        }

        private static void TryApplyExactPersonalDrivenPropertyOverrides(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null)
                return;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            if (agent.IsMount)
            {
                TryApplyExactPersonalMountDrivenPropertyOverrides(agent, agentDrivenProperties);
                return;
            }

            if (!agent.IsHuman)
                return;

            WeaponComponentData currentWeapon = ResolveCurrentWeapon(agent);
            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(currentWeapon);
            string skillId = relevantSkill?.StringId ?? string.Empty;
            bool isMounted = agent.HasMount || agent.MountAgent != null;

            if (!isMounted)
            {
                float combatSpeedFactor = 0f;
                if (HasExactHeroPerk(agent, "OneHandedFleetOfFoot"))
                    combatSpeedFactor += 0.04f;
                if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase) &&
                    HasExactHeroPerk(agent, "PolearmFootwork"))
                {
                    combatSpeedFactor += 0.02f;
                }
                if (combatSpeedFactor > 0f)
                    agentDrivenProperties.CombatMaxSpeedMultiplier *= 1f + combatSpeedFactor;

                float movementSpeedFactor = 0f;
                if (HasExactHeroPerk(agent, "AthleticsMorningExercise"))
                    movementSpeedFactor += 0.03f;
                if (HasExactHeroPerk(agent, "MedicineSelfMedication"))
                    movementSpeedFactor += 0.02f;

                WeaponComponentData offhandWeapon = ResolveOffhandWeapon(agent);
                bool hasWieldedShield = offhandWeapon?.IsShield == true;
                bool hasCurrentRangedWeapon = currentWeapon?.IsRangedWeapon == true;
                if (!hasWieldedShield && !hasCurrentRangedWeapon && HasExactHeroPerk(agent, "AthleticsSprint"))
                    movementSpeedFactor += 0.05f;
                if (currentWeapon == null && offhandWeapon == null && HasExactHeroPerk(agent, "RogueryFleetFooted"))
                    movementSpeedFactor += 0.1f;
                if (movementSpeedFactor > 0f)
                    agentDrivenProperties.MaxSpeedMultiplier *= 1f + movementSpeedFactor;

            }

            if (HasExactHeroPerk(agent, "RogueryDirtyFighting"))
                agentDrivenProperties.KickStunDurationMultiplier *= 1.5f;

            TryApplyExactPersonalMeleePerkDrivenProperties(
                agent,
                agentDrivenProperties,
                currentWeapon,
                relevantSkill,
                skillId);
            TryApplyExactPersonalShieldDrivenProperties(agent, agentDrivenProperties);
            TryRemoveExactPersonalRangedWeaponEncumbrance(agent, agentDrivenProperties);
        }

        private static void TryApplyExactPersonalMountDrivenPropertyOverrides(
            Agent mountAgent,
            AgentDrivenProperties agentDrivenProperties)
        {
            Agent riderAgent = mountAgent?.RiderAgent;
            if (riderAgent == null)
                return;

            float maneuverFactor = HasExactHeroPerk(riderAgent, "RidingNimbleStead") ? 0.1f : 0f;
            float maneuverAddition = 0f;
            if (HasExactHeroPerk(riderAgent, "RidingTheWayOfTheSaddle") &&
                CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(
                    riderAgent,
                    DefaultSkills.Riding,
                    out int ridingSkill,
                    out _))
            {
                maneuverAddition = Math.Max(0, ridingSkill - 250) * 0.3f;
            }

            if (maneuverFactor > 0f || maneuverAddition > 0f)
            {
                agentDrivenProperties.MountManeuver =
                    agentDrivenProperties.MountManeuver * (1f + maneuverFactor) + maneuverAddition;
            }

            if (HasExactHeroPerk(riderAgent, "RidingSweepingWind"))
                agentDrivenProperties.MountSpeed *= 1.05f;
        }

        private static void TryApplyExactPersonalMeleePerkDrivenProperties(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties,
            WeaponComponentData currentWeapon,
            SkillObject relevantSkill,
            string skillId)
        {
            if (currentWeapon?.IsMeleeWeapon != true || relevantSkill == null)
                return;

            int exactSkill = 0;
            CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(agent, relevantSkill, out exactSkill, out _);
            float swingFactor = 0f;
            float readyFactor = 0f;
            float handlingFactor = 0f;
            if (!(agent.HasMount || agent.MountAgent != null) && HasExactHeroPerk(agent, "AthleticsFury"))
                handlingFactor += 0.1f;

            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
            {
                if (HasExactHeroPerk(agent, "OneHandedSwiftStrike"))
                    swingFactor += 0.02f;
                if (HasExactHeroPerk(agent, "OneHandedWrappedHandles"))
                    handlingFactor += 0.2f;
                if (exactSkill > 250 && HasExactHeroPerk(agent, "OneHandedWayOfTheSword"))
                {
                    float epicFactor = (exactSkill - 250) * 0.002f;
                    swingFactor += epicFactor;
                    readyFactor += epicFactor;
                }
            }
            else if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
            {
                if (HasExactHeroPerk(agent, "TwoHandedOnTheEdge"))
                    swingFactor += 0.03f;
                if (HasExactHeroPerk(agent, "TwoHandedStrongGrip"))
                    handlingFactor += 0.1f;
                if (exactSkill > 250 && HasExactHeroPerk(agent, "TwoHandedWayOfTheGreatAxe"))
                {
                    float epicFactor = (exactSkill - 250) * 0.002f;
                    swingFactor += epicFactor;
                    readyFactor += epicFactor;
                }
            }
            else if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
            {
                if (HasExactHeroPerk(agent, "PolearmSwiftSwing"))
                    swingFactor += 0.05f;
                if ((int)currentWeapon.SwingDamageType != -1 && HasExactHeroPerk(agent, "PolearmCounterWeight"))
                    handlingFactor += 0.15f;
                if (exactSkill > 250 && HasExactHeroPerk(agent, "PolearmWayOfTheSpear"))
                {
                    float epicFactor = (exactSkill - 250) * 0.002f;
                    swingFactor += epicFactor;
                    readyFactor += epicFactor;
                }
            }

            if (swingFactor > 0f)
                agentDrivenProperties.SwingSpeedMultiplier *= 1f + swingFactor;
            if (readyFactor > 0f)
                agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= 1f + readyFactor;
            if (handlingFactor > 0f)
                agentDrivenProperties.HandlingMultiplier *= 1f + handlingFactor;
        }

        private static void TryApplyExactPersonalShieldDrivenProperties(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties)
        {
            WeaponComponentData offhandWeapon = ResolveOffhandWeapon(agent);
            if (offhandWeapon?.IsShield != true)
                return;

            if (HasExactHeroPerk(agent, "OneHandedArrowCatcher"))
                agentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder += 0.01f;
            if (HasExactHeroPerk(agent, "OneHandedBasher"))
                agentDrivenProperties.ShieldBashStunDurationMultiplier *= 1.5f;
        }

        private static void TryRemoveExactPersonalRangedWeaponEncumbrance(
            Agent agent,
            AgentDrivenProperties agentDrivenProperties)
        {
            bool ignoreBowEncumbrance = HasExactHeroPerk(agent, "BowRangersSwiftness");
            bool ignoreCrossbowEncumbrance = HasExactHeroPerk(agent, "CrossbowLooseAndMove");
            if (!ignoreBowEncumbrance && !ignoreCrossbowEncumbrance)
                return;

            MissionEquipment equipment = agent.Equipment;
            if (equipment == null)
                return;

            float ignoredEncumbrance = 0f;
            for (EquipmentIndex index = EquipmentIndex.Weapon0; index <= EquipmentIndex.Weapon3; index++)
            {
                MissionWeapon missionWeapon = equipment[index];
                ItemObject item = missionWeapon.Item;
                WeaponComponentData usage = missionWeapon.CurrentUsageItem ?? item?.PrimaryWeapon;
                SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(usage);
                string skillId = relevantSkill?.StringId ?? string.Empty;
                bool ignored =
                    (ignoreBowEncumbrance && string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase)) ||
                    (ignoreCrossbowEncumbrance && string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase));
                if (!ignored || item == null || usage == null)
                    continue;

                ignoredEncumbrance += 4f * MathF.Sqrt(Math.Max(0f, usage.GetRealWeaponLength())) * item.Weight;
            }

            if (ignoredEncumbrance > 0f)
            {
                agentDrivenProperties.WeaponsEncumbrance =
                    Math.Max(0f, agentDrivenProperties.WeaponsEncumbrance - ignoredEncumbrance);
            }
        }

        private static WeaponComponentData ResolveOffhandWeapon(Agent agent)
        {
            if (agent?.Equipment == null)
                return null;

            EquipmentIndex offhandIndex = agent.GetOffhandWieldedItemIndex();
            if (offhandIndex == EquipmentIndex.None)
                return null;

            MissionWeapon missionWeapon = agent.Equipment[offhandIndex];
            return missionWeapon.CurrentUsageItem ?? missionWeapon.Item?.PrimaryWeapon;
        }

        private static int TryGetCharacterSkillValue(BasicCharacterObject character, SkillObject skillObject)
        {
            if (character == null || skillObject == null)
                return 0;

            try
            {
                return character.GetSkillValue(skillObject);
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryScaleArmorDrivenProperty(
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty property,
            float scaleFactor)
        {
            float currentValue = agentDrivenProperties.GetStat(property);
            if (currentValue <= 0f)
                return false;

            float updatedValue = Math.Max(0f, currentValue * scaleFactor);
            if (Math.Abs(updatedValue - currentValue) < 0.0001f)
                return false;

            agentDrivenProperties.SetStat(property, updatedValue);
            return true;
        }

        private static float ResolveExactCampaignRegularTroopHealth(
            float fallbackHealth,
            Agent agent,
            RosterEntryState entryState,
            BattlePartyState partyState)
        {
            float health = entryState?.BaseHitPoints > 0
                ? entryState.BaseHitPoints
                : fallbackHealth;
            BattlePartyModifierState modifiers = partyState?.Modifiers;
            if (modifiers == null)
                return health;

            bool isMounted = agent?.HasMount == true ||
                agent?.MountAgent != null ||
                entryState?.IsMounted == true;
            bool isRanged = entryState?.IsRanged == true || agent?.Character?.IsRanged == true;
            bool isInfantry = agent?.Character?.IsInfantry == true;

            if (HasPerkId(modifiers.PartyLeaderPerkIds, "TwoHandedThickHides"))
                health += 5f;
            if (HasPerkId(modifiers.PartyLeaderPerkIds, "PolearmHardyFrontline"))
                health += 5f;
            if (isRanged && HasPerkId(modifiers.PartyLeaderPerkIds, "CrossbowBoltenGuard"))
                health += 5f;
            if (!isMounted && HasPerkId(modifiers.PartyLeaderPerkIds, "AthleticsWellBuilt"))
                health += 5f;
            if (!isMounted && HasPerkId(modifiers.PartyLeaderPerkIds, "PolearmHardKnock"))
                health += 3f;
            if (!isMounted && isInfantry && HasPerkId(modifiers.PartyLeaderPerkIds, "OneHandedUnwaveringDefense"))
                health += 10f;

            RosterEntryState leaderEntry = ResolvePartyLeaderEntry(partyState);
            if (leaderEntry != null && HasPerkId(leaderEntry.PerkIds, "MedicineMinisterOfHealth"))
                health += Math.Max(0, modifiers.LeaderMedicineSkill - 250);

            return health;
        }

        private static float ResolveExactCampaignMountHealth(
            float baseHealth,
            Agent riderAgent,
            BattlePartyState partyState)
        {
            float additiveHealth = HasPerkId(partyState?.Modifiers?.PartyLeaderPerkIds, "MedicineSledges")
                ? 15f
                : 0f;
            float healthFactor = 1f;
            if (HasExactHeroPerk(riderAgent, "RidingVeterinary"))
                healthFactor += 0.2f;
            if (HasPerkId(partyState?.Modifiers?.PartyLeaderPerkIds, "RidingVeterinary"))
                healthFactor += 0.1f;

            return Math.Max(1f, (baseHealth + additiveHealth) * healthFactor);
        }

        private static RosterEntryState ResolvePartyLeaderEntry(BattlePartyState partyState)
        {
            string leaderHeroId = partyState?.Modifiers?.LeaderHeroId;
            if (string.IsNullOrWhiteSpace(leaderHeroId) || partyState?.Entries == null)
                return null;

            foreach (RosterEntryState entry in partyState.Entries)
            {
                if (entry != null &&
                    string.Equals(entry.HeroId, leaderHeroId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool HasPerkId(IEnumerable<string> perkIds, string perkId)
        {
            if (perkIds == null || string.IsNullOrWhiteSpace(perkId))
                return false;

            foreach (string candidate in perkIds)
            {
                if (string.Equals(candidate, perkId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TrySetDrivenProperty(
            AgentDrivenProperties agentDrivenProperties,
            DrivenProperty property,
            float desiredValue,
            float epsilon = 0.001f)
        {
            if (agentDrivenProperties == null)
                return false;

            float currentValue = agentDrivenProperties.GetStat(property);
            if (Math.Abs(currentValue - desiredValue) <= epsilon)
                return false;

            agentDrivenProperties.SetStat(property, desiredValue);
            return true;
        }

        private void TryApplyExactRangedDrivenPropertyOverrides(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null || !agent.IsHuman)
                return;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            MissionEquipment equipment = agent.Equipment;
            if (equipment == null)
                return;

            EquipmentIndex primaryWieldedItemIndex = agent.GetPrimaryWieldedItemIndex();
            if (primaryWieldedItemIndex == EquipmentIndex.None)
                return;

            WeaponComponentData primaryWeapon = equipment[primaryWieldedItemIndex].CurrentUsageItem;
            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(primaryWeapon);
            if (relevantSkill == null)
                return;

            string skillId = relevantSkill.StringId ?? string.Empty;
            if (!string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
                return;

            if (!TryResolveExactSkillOverride(agent, relevantSkill, 0, out int exactSkill, out string entryId))
                return;

            float currentMissileSpeedMultiplier = agentDrivenProperties.MissileSpeedMultiplier;
            float desiredMissileSpeedMultiplier = currentMissileSpeedMultiplier;
            string appliedPerkSummary = string.Empty;

            if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, "ThrowingPerfectTechnique", out _))
            {
                desiredMissileSpeedMultiplier *= 1.25f;
                appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "PerfectTechnique=1.25");
            }

            if (exactSkill > 200 &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, "ThrowingUnstoppableForce", out _))
            {
                float epicFactor = 1f + (exactSkill - 200) * 0.002f;
                desiredMissileSpeedMultiplier *= epicFactor;
                appliedPerkSummary = AppendAppliedPerkSummary(
                    appliedPerkSummary,
                    "UnstoppableForce=" + epicFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (Math.Abs(desiredMissileSpeedMultiplier - currentMissileSpeedMultiplier) < 0.0001f)
                return;

            TryLogBattleActivation(agent);
            agentDrivenProperties.MissileSpeedMultiplier = desiredMissileSpeedMultiplier;
            TryLogRangedDrivenOverride(
                agent,
                relevantSkill,
                entryId,
                currentMissileSpeedMultiplier,
                desiredMissileSpeedMultiplier,
                appliedPerkSummary);
        }

        private bool TryApplyExactRangedCampaignDrivenPropertyOverrides(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            if (agent == null || agentDrivenProperties == null || !agent.IsHuman)
                return false;

            Mission mission = agent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            MissionEquipment equipment = agent.Equipment;
            if (equipment == null)
                return false;

            EquipmentIndex primaryWieldedItemIndex = agent.GetPrimaryWieldedItemIndex();
            if (primaryWieldedItemIndex == EquipmentIndex.None)
                return false;

            WeaponComponentData primaryWeapon = equipment[primaryWieldedItemIndex].CurrentUsageItem;
            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(primaryWeapon);
            if (!IsExactRangedRelevantSkill(relevantSkill))
                return false;

            if (!TryResolveExactSkillOverride(agent, relevantSkill, 0, out int exactSkill, out string entryId))
                return false;

            int fallbackRidingSkill = TryGetCharacterSkillValue(agent.Character, DefaultSkills.Riding);
            int exactRidingSkill = fallbackRidingSkill;
            TryResolveExactSkillOverride(agent, DefaultSkills.Riding, fallbackRidingSkill, out exactRidingSkill, out _);

            float baseWeaponInaccuracy = agentDrivenProperties.WeaponInaccuracy;
            float baseMovementPenalty = agentDrivenProperties.WeaponMaxMovementAccuracyPenalty;
            float baseUnsteadyPenalty = agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty;
            float baseBestAccuracyWaitTime = agentDrivenProperties.WeaponBestAccuracyWaitTime;
            float baseReloadMovementPenaltyFactor = agentDrivenProperties.ReloadMovementPenaltyFactor;
            float baseReloadSpeed = agentDrivenProperties.ReloadSpeed;
            float baseReadySpeed = agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier;
            float baseMissileSpeedMultiplier = agentDrivenProperties.MissileSpeedMultiplier;

            float weaponInaccuracy = ComputeCampaignRangedWeaponInaccuracy(primaryWeapon, exactSkill);
            ComputeCampaignRangedAccuracyPenalties(
                agent,
                primaryWeapon,
                relevantSkill,
                exactSkill,
                exactRidingSkill,
                out float movementPenalty,
                out float unsteadyPenalty,
                out float bestAccuracyWaitTime,
                out float unsteadyBeginTime,
                out float unsteadyEndTime,
                out float rotationalAccuracyPenalty);

            float reloadMovementPenaltyFactor = baseReloadMovementPenaltyFactor > 0f ? baseReloadMovementPenaltyFactor : 1f;
            float reloadSpeed = baseReloadSpeed > 0f ? baseReloadSpeed : 0.93f;
            float readySpeed = baseReadySpeed > 0f ? baseReadySpeed : 0.93f;
            float missileSpeedMultiplier = baseMissileSpeedMultiplier > 0f ? baseMissileSpeedMultiplier : 1f;
            string appliedPerkSummary = string.Empty;
            bool isMounted = agent.HasMount || agent.MountAgent != null;
            string relevantSkillId = relevantSkill.StringId ?? string.Empty;

            if (isMounted && HasExactHeroPerk(agent, "RidingSagittarius"))
            {
                movementPenalty *= 0.85f;
                unsteadyPenalty *= 0.85f;
                appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "RidingSagittarius=0.85");
            }

            if (string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                if (HasExactHeroPerk(agent, "BowNockingPoint"))
                {
                    reloadMovementPenaltyFactor *= 0.5f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "NockingPoint=0.5");
                }

                if (HasExactHeroPerk(agent, "BowBowControl"))
                {
                    movementPenalty *= 0.7f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "BowControl=0.7");
                }

                if (HasExactHeroPerk(agent, "BowRapidFire"))
                {
                    reloadSpeed *= 1.25f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "RapidFire=1.25");
                }

                if (HasExactHeroPerk(agent, "BowQuickAdjustments"))
                {
                    rotationalAccuracyPenalty *= 0.5f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "QuickAdjustments=0.5");
                }

                if (HasExactHeroPerk(agent, "BowDiscipline"))
                {
                    unsteadyBeginTime *= 1.5f;
                    unsteadyEndTime *= 1.5f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "Discipline=1.5");
                }

                if (HasExactHeroPerk(agent, "BowQuickDraw"))
                {
                    readySpeed *= 1.25f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "QuickDraw=1.25");
                }

                if (isMounted && HasExactHeroPerk(agent, "BowMountedArchery"))
                {
                    movementPenalty *= 0.7f;
                    unsteadyPenalty *= 0.7f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "MountedArchery=0.7");
                }

                if (exactSkill > 200 && HasExactHeroPerk(agent, "BowDeadshot"))
                {
                    float epicFactor = 1f + (exactSkill - 200) * 0.002f;
                    reloadSpeed *= epicFactor;
                    appliedPerkSummary = AppendAppliedPerkSummary(
                        appliedPerkSummary,
                        "DeadshotReload=" + epicFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase))
            {
                if (isMounted && HasExactHeroPerk(agent, "CrossbowSteady"))
                {
                    movementPenalty *= 0.5f;
                    rotationalAccuracyPenalty *= 0.5f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "Steady=0.5");
                }

                if (HasExactHeroPerk(agent, "CrossbowWindWinder"))
                {
                    reloadSpeed *= 1.25f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "WindWinder=1.25");
                }

                if (HasExactHeroPerk(agent, "CrossbowDonkeysSwiftness"))
                {
                    movementPenalty *= 0.7f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "DonkeysSwiftness=0.7");
                }

                if (HasExactHeroPerk(agent, "CrossbowMarksmen"))
                {
                    readySpeed *= 1.25f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "Marksmen=1.25");
                }

                if (exactSkill > 200 && HasExactHeroPerk(agent, "CrossbowMightyPull"))
                {
                    float epicFactor = 1f + (exactSkill - 200) * 0.002f;
                    reloadSpeed *= epicFactor;
                    appliedPerkSummary = AppendAppliedPerkSummary(
                        appliedPerkSummary,
                        "MightyPullReload=" + epicFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                if (HasExactHeroPerk(agent, "ThrowingQuickDraw"))
                {
                    reloadSpeed *= 1.2f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "QuickDraw=1.2");
                }

                if (HasExactHeroPerk(agent, "ThrowingPerfectTechnique"))
                {
                    missileSpeedMultiplier *= 1.25f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "PerfectTechnique=1.25");
                }

                if (isMounted && HasExactHeroPerk(agent, "ThrowingMountedSkirmisher"))
                {
                    movementPenalty *= 0.8f;
                    unsteadyPenalty *= 0.8f;
                    appliedPerkSummary = AppendAppliedPerkSummary(appliedPerkSummary, "MountedSkirmisher=0.8");
                }

                if (exactSkill > 200 && HasExactHeroPerk(agent, "ThrowingUnstoppableForce"))
                {
                    float epicFactor = 1f + (exactSkill - 200) * 0.002f;
                    missileSpeedMultiplier *= epicFactor;
                    appliedPerkSummary = AppendAppliedPerkSummary(
                        appliedPerkSummary,
                        "UnstoppableForceSpeed=" + epicFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            agentDrivenProperties.WeaponInaccuracy = weaponInaccuracy;
            agentDrivenProperties.WeaponMaxMovementAccuracyPenalty = Math.Max(0f, movementPenalty);
            agentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty = Math.Max(0f, unsteadyPenalty);
            agentDrivenProperties.WeaponBestAccuracyWaitTime = Math.Max(0f, bestAccuracyWaitTime);
            agentDrivenProperties.WeaponUnsteadyBeginTime = Math.Max(0f, unsteadyBeginTime);
            agentDrivenProperties.WeaponUnsteadyEndTime = Math.Max(0f, unsteadyEndTime);
            agentDrivenProperties.WeaponRotationalAccuracyPenaltyInRadians = Math.Max(0f, rotationalAccuracyPenalty);
            agentDrivenProperties.ReloadMovementPenaltyFactor = Math.Max(0f, reloadMovementPenaltyFactor);
            agentDrivenProperties.ReloadSpeed = Math.Max(0f, reloadSpeed);
            agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier = Math.Max(0f, readySpeed);
            agentDrivenProperties.MissileSpeedMultiplier = Math.Max(0f, missileSpeedMultiplier);

            TryLogExactRangedCampaignDrivenOverride(
                agent,
                relevantSkill,
                entryId,
                baseWeaponInaccuracy,
                weaponInaccuracy,
                baseMovementPenalty,
                movementPenalty,
                baseUnsteadyPenalty,
                unsteadyPenalty,
                baseBestAccuracyWaitTime,
                bestAccuracyWaitTime,
                baseReloadSpeed,
                reloadSpeed,
                baseReadySpeed,
                readySpeed,
                baseMissileSpeedMultiplier,
                missileSpeedMultiplier,
                appliedPerkSummary);

            return true;
        }

        private static bool IsExactRangedRelevantSkill(SkillObject relevantSkill)
        {
            string relevantSkillId = relevantSkill?.StringId ?? string.Empty;
            return string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryResolveExactRangedSkillForWeapon(
            Agent agent,
            WeaponComponentData weapon,
            out int exactSkill,
            out string entryId)
        {
            exactSkill = 0;
            entryId = string.Empty;

            if (weapon == null || !weapon.IsRangedWeapon)
                return false;

            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(weapon);
            if (!IsExactRangedRelevantSkill(relevantSkill))
                return false;

            return TryResolveExactSkillOverride(agent, relevantSkill, 0, out exactSkill, out entryId);
        }

        private static float ComputeCampaignRangedWeaponInaccuracy(WeaponComponentData weapon, int exactSkill)
        {
            if (weapon == null)
                return 0f;

            float skillFactor = weapon.WeaponClass == WeaponClass.Sling
                ? 1f - 0.003f * exactSkill
                : 1f - 0.002f * exactSkill;
            float inaccuracy = (100f - weapon.Accuracy) * skillFactor * 0.001f;

            return Math.Max(0.0001f, inaccuracy);
        }

        private static void ComputeCampaignRangedAccuracyPenalties(
            Agent agent,
            WeaponComponentData weapon,
            SkillObject relevantSkill,
            int exactSkill,
            int exactRidingSkill,
            out float movementPenalty,
            out float unsteadyPenalty,
            out float bestAccuracyWaitTime,
            out float unsteadyBeginTime,
            out float unsteadyEndTime,
            out float rotationalAccuracyPenalty)
        {
            bool isMounted = agent != null && agent.HasMount;
            int thrustSpeed = weapon?.ThrustSpeed ?? 0;

            if (!isMounted)
            {
                float skillFactor = Math.Max(0f, 1f - exactSkill / 500f);
                movementPenalty = Math.Max(0f, 0.125f * skillFactor);
                unsteadyPenalty = Math.Max(0f, 0.1f * skillFactor);
            }
            else
            {
                float skillFactor = Math.Max(0f, (1f - exactSkill / 500f) * (1f - exactRidingSkill / 1800f));
                movementPenalty = Math.Max(0f, 0.025f * skillFactor);
                unsteadyPenalty = Math.Max(0f, 0.12f * skillFactor);
            }

            string relevantSkillId = relevantSkill?.StringId ?? string.Empty;
            WeaponClass weaponClass = weapon?.WeaponClass ?? WeaponClass.Undefined;

            if (string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                float thrustFactor = MBMath.ClampFloat((thrustSpeed - 45f) / 90f, 0f, 1f);
                movementPenalty *= 6f;
                unsteadyPenalty *= 4.5f / MBMath.Lerp(0.75f, 2f, thrustFactor, 1E-05f);
            }
            else if (string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                if (weaponClass == WeaponClass.Sling)
                {
                    float thrustFactor = MBMath.ClampFloat((thrustSpeed - 30f) / 90f, 0f, 1f);
                    movementPenalty *= 5f;
                    unsteadyPenalty *= 2.4f * MBMath.Lerp(2.4f, 1.2f, thrustFactor, 1E-05f);
                }
                else
                {
                    float thrustFactor = MBMath.ClampFloat((thrustSpeed - 89f) / 13f, 0f, 1f);
                    movementPenalty *= 0.5f;
                    unsteadyPenalty *= 1.5f * MBMath.Lerp(1.5f, 0.8f, thrustFactor, 1E-05f);
                }
            }
            else if (string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase))
            {
                movementPenalty *= 2.5f;
                unsteadyPenalty *= 1.2f;
            }

            if (weaponClass == WeaponClass.Bow)
            {
                bestAccuracyWaitTime = 0.3f + (95.75f - thrustSpeed) * 0.005f;
                float thrustFactor = MBMath.ClampFloat((thrustSpeed - 45f) / 90f, 0f, 1f);
                unsteadyBeginTime = 0.6f + exactSkill * 0.01f * MBMath.Lerp(2f, 4f, thrustFactor, 1E-05f);
                if (agent != null && agent.IsAIControlled)
                    unsteadyBeginTime *= 4f;
                unsteadyEndTime = 2f + unsteadyBeginTime;
                rotationalAccuracyPenalty = 0.1f;
            }
            else if (weaponClass == WeaponClass.Javelin || weaponClass == WeaponClass.ThrowingAxe || weaponClass == WeaponClass.ThrowingKnife || weaponClass == WeaponClass.Stone)
            {
                bestAccuracyWaitTime = 0.2f + (89f - thrustSpeed) * 0.009f;
                unsteadyBeginTime = 2.5f + exactSkill * 0.01f;
                unsteadyEndTime = 10f + unsteadyBeginTime;
                rotationalAccuracyPenalty = 0.025f;
            }
            else if (weaponClass == WeaponClass.Sling)
            {
                bestAccuracyWaitTime = 2.6f + (89f - thrustSpeed) * 0.12f;
                unsteadyBeginTime = 3f + exactSkill * 0.064f;
                unsteadyEndTime = 22f + unsteadyBeginTime;
                rotationalAccuracyPenalty = 0.2f;
            }
            else
            {
                bestAccuracyWaitTime = 0.1f;
                unsteadyBeginTime = 0f;
                unsteadyEndTime = 0f;
                rotationalAccuracyPenalty = 0.1f;
            }
        }

        private static bool HasExactHeroPerk(Agent agent, string perkId)
        {
            return CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, perkId, out _);
        }

        private void TryLogExactRangedCampaignDrivenOverride(
            Agent agent,
            SkillObject relevantSkill,
            string entryId,
            float baseWeaponInaccuracy,
            float exactWeaponInaccuracy,
            float baseMovementPenalty,
            float exactMovementPenalty,
            float baseUnsteadyPenalty,
            float exactUnsteadyPenalty,
            float baseBestAccuracyWaitTime,
            float exactBestAccuracyWaitTime,
            float baseReloadSpeed,
            float exactReloadSpeed,
            float baseReadySpeed,
            float exactReadySpeed,
            float baseMissileSpeedMultiplier,
            float exactMissileSpeedMultiplier,
            string appliedPerkSummary)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                return;

            string skillId = relevantSkill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactWeaponInaccuracy.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                exactMovementPenalty.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                exactUnsteadyPenalty.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                (appliedPerkSummary ?? string.Empty);

            if (!_loggedRangedDrivenKeys.TryAdd(logKey, 0))
                return;

            TryLogBattleActivation(agent);
            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact campaign ranged driven-property override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + skillId +
                " WeaponInaccuracy=" + baseWeaponInaccuracy.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactWeaponInaccuracy.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                " MovementPenalty=" + baseMovementPenalty.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactMovementPenalty.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                " UnsteadyPenalty=" + baseUnsteadyPenalty.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactUnsteadyPenalty.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                " BestWait=" + baseBestAccuracyWaitTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactBestAccuracyWaitTime.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " ReloadSpeed=" + baseReloadSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactReloadSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " ReadySpeed=" + baseReadySpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactReadySpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " MissileSpeedMultiplier=" + baseMissileSpeedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactMissileSpeedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " AppliedPerks=" + (string.IsNullOrWhiteSpace(appliedPerkSummary) ? "none" : appliedPerkSummary) +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        private void TryLogRangedDrivenOverride(
            Agent agent,
            SkillObject relevantSkill,
            string entryId,
            float baseMissileSpeedMultiplier,
            float exactMissileSpeedMultiplier,
            string appliedPerkSummary)
        {
            if (!ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
                return;

            string skillId = relevantSkill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactMissileSpeedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedRangedDrivenKeys.TryAdd(logKey, 0))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedAgentStatCalculateModel: exact ranged driven-property override applied. " +
                "Agent=" + (agent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + skillId +
                " MissileSpeedMultiplier=" + baseMissileSpeedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "->" + exactMissileSpeedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " AppliedPerks=" + (string.IsNullOrWhiteSpace(appliedPerkSummary) ? "none" : appliedPerkSummary) +
                " Mission=" + (agent?.Mission?.SceneName ?? "null") + ".");
        }

        private static string AppendAppliedPerkSummary(string currentSummary, string addition)
        {
            if (string.IsNullOrWhiteSpace(addition))
                return currentSummary ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentSummary))
                return addition;

            return currentSummary + "/" + addition;
        }
    }
}
