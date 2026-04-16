using System;
using System.Collections.Generic;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.MissionModels
{
    /// <summary>
    /// Thin low-level wrapper over the active MP AgentStatCalculateModel.
    /// Phase 1 keeps the stable MP runtime behavior intact, but swaps in
    /// campaign-derived effective skills for hero entries that already have
    /// an exact combat profile in CoopBattle.
    /// </summary>
    public sealed class CoopCampaignDerivedAgentStatCalculateModel : AgentStatCalculateModel
    {
        private readonly AgentStatCalculateModel _baseModel;
        private readonly HashSet<string> _loggedExactSkillKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedWeaponDamageKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedRangedDrivenKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedExactMaxHealthKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedExactDefenseDrivenKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedExactMeleeDrivenKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _hasLoggedBattleActivation;

        public CoopCampaignDerivedAgentStatCalculateModel(AgentStatCalculateModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override void InitializeAgentStats(Agent agent, Equipment spawnEquipment, AgentDrivenProperties agentDrivenProperties, AgentBuildData agentBuildData)
        {
            _baseModel.InitializeAgentStats(agent, spawnEquipment, agentDrivenProperties, agentBuildData);
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
            TryApplyExactDefenseDrivenPropertyOverrides(agent, agentDrivenProperties);
            TryApplyExactMeleeDrivenPropertyOverrides(agent, agentDrivenProperties);
            TryApplyExactRangedDrivenPropertyOverrides(agent, agentDrivenProperties);
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
            return _baseModel.GetWeaponInaccuracy(agent, weapon, weaponSkill);
        }

        public override float GetDetachmentCostMultiplierOfAgent(Agent agent, IDetachment detachment)
        {
            return _baseModel.GetDetachmentCostMultiplierOfAgent(agent, detachment);
        }

        public override float GetInteractionDistance(Agent agent)
        {
            return _baseModel.GetInteractionDistance(agent);
        }

        public override float GetMaxCameraZoom(Agent agent)
        {
            return _baseModel.GetMaxCameraZoom(agent);
        }

        public override int GetEffectiveSkill(Agent agent, SkillObject skill)
        {
            int fallbackSkill = _baseModel.GetEffectiveSkill(agent, skill);
            if (!TryResolveExactSkillOverride(agent, skill, fallbackSkill, out int exactSkill, out string entryId))
                return fallbackSkill;

            TryLogExactSkillOverride(agent, skill, fallbackSkill, exactSkill, entryId);
            return exactSkill;
        }

        public override int GetEffectiveSkillForWeapon(Agent agent, WeaponComponentData weapon)
        {
            if (weapon == null || weapon.RelevantSkill == null)
                return _baseModel.GetEffectiveSkillForWeapon(agent, weapon);

            int desiredSkill = GetEffectiveSkill(agent, weapon.RelevantSkill);
            if (desiredSkill <= 0)
                return _baseModel.GetEffectiveSkillForWeapon(agent, weapon);

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

            if (!CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(agent, skill, out int profileSkillValue, out entryId))
                return false;

            TryLogBattleActivation(agent);
            exactSkill = profileSkillValue;
            return true;
        }

        private void TryLogBattleActivation(Agent agent)
        {
            if (_hasLoggedBattleActivation || agent?.Mission == null)
                return;

            _hasLoggedBattleActivation = true;
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
            string skillId = skill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactSkill.ToString();

            if (!_loggedExactSkillKeys.Add(logKey))
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

            SkillObject relevantSkill = ResolveWeaponDamageRelevantSkill(weapon);
            if (relevantSkill == null)
                return false;

            skillId = relevantSkill.StringId ?? "null";
            if (!TryResolveExactSkillOverride(agent, relevantSkill, 0, out _, out entryId))
                return false;

            float candidateMultiplier = Math.Max(0f, baseMultiplier);
            if (!CoopMissionSpawnLogic.TryApplyWeaponDamageMultiplierCombatProfile(agent, weapon, ref candidateMultiplier))
                return false;

            if (Math.Abs(candidateMultiplier - baseMultiplier) < 0.0001f)
                return false;

            updatedMultiplier = candidateMultiplier;
            return true;
        }

        private void TryLogWeaponDamageOverride(
            Agent agent,
            WeaponComponentData weapon,
            string skillId,
            string entryId,
            float baseMultiplier,
            float updatedMultiplier)
        {
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                (skillId ?? "null") + "|" +
                (entryId ?? string.Empty) + "|" +
                updatedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedWeaponDamageKeys.Add(logKey))
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
                case WeaponClass.Stone:
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

            if (!CoopMissionSpawnLogic.TryGetExactHeroCombatProfileBaseHitPoints(agent, out int exactBaseHitPoints, out entryId))
                return false;

            float candidateHealth = Math.Max(1f, exactBaseHitPoints);
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
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                (entryId ?? string.Empty) + "|" +
                exactHealth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedExactMaxHealthKeys.Add(logKey))
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
                float baseArmorEncumbrance = agentDrivenProperties.ArmorEncumbrance;
                float exactArmorEncumbrance = baseArmorEncumbrance * 0.85f;
                if (Math.Abs(exactArmorEncumbrance - baseArmorEncumbrance) >= 0.0001f)
                {
                    agentDrivenProperties.ArmorEncumbrance = exactArmorEncumbrance;
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

            if (!(agent.HasMount || agent.MountAgent != null) &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, "AthleticsIgnorePain", out string ignorePainEntryId))
            {
                bool armorScaled = false;
                armorScaled |= TryScaleArmorDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorHead, 1.1f);
                armorScaled |= TryScaleArmorDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorTorso, 1.1f);
                armorScaled |= TryScaleArmorDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorArms, 1.1f);
                armorScaled |= TryScaleArmorDrivenProperty(agentDrivenProperties, DrivenProperty.ArmorLegs, 1.1f);
                if (armorScaled)
                {
                    if (string.IsNullOrWhiteSpace(entryId))
                        entryId = ignorePainEntryId;

                    summary = AppendAppliedPerkSummary(summary, "IgnorePain Armor=1.1");
                    applied = true;
                }
            }

            if (!applied)
                return;

            TryLogBattleActivation(agent);
            TryLogDefenseDrivenOverride(agent, entryId, summary);
        }

        private void TryLogDefenseDrivenOverride(Agent agent, string entryId, string summary)
        {
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                (entryId ?? string.Empty) + "|" +
                (summary ?? string.Empty);

            if (!_loggedExactDefenseDrivenKeys.Add(logKey))
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
            string skillId = relevantSkill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactSwingSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                exactReadySpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedExactMeleeDrivenKeys.Add(logKey))
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

        private void TryLogRangedDrivenOverride(
            Agent agent,
            SkillObject relevantSkill,
            string entryId,
            float baseMissileSpeedMultiplier,
            float exactMissileSpeedMultiplier,
            string appliedPerkSummary)
        {
            string skillId = relevantSkill?.StringId ?? "null";
            string logKey =
                (agent?.Index ?? -1).ToString() + "|" +
                skillId + "|" +
                (entryId ?? string.Empty) + "|" +
                exactMissileSpeedMultiplier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            if (!_loggedRangedDrivenKeys.Add(logKey))
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
