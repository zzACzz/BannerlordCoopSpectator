using System;
using System.Collections.Generic;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator.MissionModels
{
    /// <summary>
    /// Thin low-level wrapper over the active strike magnitude model.
    /// Keeps the stable multiplayer runtime shell, but applies the sandbox
    /// raw armor-damage formula for CoopBattle missions.
    /// </summary>
    public sealed class CoopCampaignDerivedStrikeMagnitudeCalculationModel : StrikeMagnitudeCalculationModel
    {
        private readonly StrikeMagnitudeCalculationModel _baseModel;
        private readonly HashSet<string> _loggedMissileMagnitudeKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _hasLoggedBattleActivation;

        public CoopCampaignDerivedStrikeMagnitudeCalculationModel(StrikeMagnitudeCalculationModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override float CalculateStrikeMagnitudeForMissile(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float missileSpeed)
        {
            float magnitude = _baseModel.CalculateStrikeMagnitudeForMissile(attackInformation, collisionData, weapon, missileSpeed);
            TryLogMissileMagnitude(attackInformation.AttackerAgent, weapon.CurrentUsageItem, collisionData, missileSpeed, magnitude);
            return magnitude;
        }

        public override float CalculateStrikeMagnitudeForSwing(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float swingSpeed, float impactPointAsPercent, float extraLinearSpeed)
        {
            return _baseModel.CalculateStrikeMagnitudeForSwing(attackInformation, collisionData, weapon, swingSpeed, impactPointAsPercent, extraLinearSpeed);
        }

        public override float CalculateStrikeMagnitudeForThrust(in AttackInformation attackInformation, in AttackCollisionData collisionData, in MissionWeapon weapon, float thrustSpeed, float extraLinearSpeed, bool isThrown = false)
        {
            return _baseModel.CalculateStrikeMagnitudeForThrust(attackInformation, collisionData, weapon, thrustSpeed, extraLinearSpeed, isThrown);
        }

        public override float CalculateBaseBlowMagnitudeForPassiveUsage(in AttackInformation attackInformation, in AttackCollisionData collisionData, float extraLinearSpeed)
        {
            return _baseModel.CalculateBaseBlowMagnitudeForPassiveUsage(attackInformation, collisionData, extraLinearSpeed);
        }

        public override float ComputeRawDamage(DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio)
        {
            if (ShouldUseSandboxArmorFormula())
                return ComputeSandboxRawDamage(damageType, magnitude, armorEffectiveness, absorbedDamageRatio);

            return _baseModel.ComputeRawDamage(damageType, magnitude, armorEffectiveness, absorbedDamageRatio);
        }

        public override float CalculateStrikeMagnitudeForUnarmedAttack(in AttackInformation attackInformation, in AttackCollisionData collisionData, float progressEffect, float momentumRemaining)
        {
            return _baseModel.CalculateStrikeMagnitudeForUnarmedAttack(attackInformation, collisionData, progressEffect, momentumRemaining);
        }

        public override float GetBluntDamageFactorByDamageType(DamageTypes damageType)
        {
            if (ShouldUseSandboxArmorFormula())
            {
                switch (damageType)
                {
                    case DamageTypes.Blunt:
                        return 0.6f;
                    case DamageTypes.Cut:
                        return 0.1f;
                    case DamageTypes.Pierce:
                        return 0.25f;
                }
            }

            return _baseModel.GetBluntDamageFactorByDamageType(damageType);
        }

        public override float CalculateHorseArcheryFactor(BasicCharacterObject characterObject)
        {
            return _baseModel.CalculateHorseArcheryFactor(characterObject);
        }

        public override float CalculateAdjustedArmorForBlow(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseArmor, BasicCharacterObject attackerCharacter, BasicCharacterObject attackerCaptainCharacter, BasicCharacterObject victimCharacter, BasicCharacterObject victimCaptainCharacter, WeaponComponentData weaponComponent)
        {
            float adjustedArmor = _baseModel.CalculateAdjustedArmorForBlow(attackInformation, collisionData, baseArmor, attackerCharacter, attackerCaptainCharacter, victimCharacter, victimCaptainCharacter, weaponComponent);
            if (!TryApplyExactPersonalArmorPenetration(attackInformation.AttackerAgent, collisionData, baseArmor, adjustedArmor, weaponComponent, out float exactAdjustedArmor))
                return adjustedArmor;

            return exactAdjustedArmor;
        }

        private void TryLogMissileMagnitude(
            Agent attackerAgent,
            WeaponComponentData weaponComponent,
            in AttackCollisionData collisionData,
            float missileSpeed,
            float magnitude)
        {
            if (attackerAgent == null || weaponComponent == null)
                return;

            Mission mission = attackerAgent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return;

            SkillObject relevantSkill = ResolveRelevantSkill(weaponComponent);
            if (relevantSkill == null)
                return;

            if (!CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(attackerAgent, relevantSkill, out int exactSkill, out string entryId))
                return;

            TryLogBattleActivation(attackerAgent);

            string skillId = relevantSkill.StringId ?? "null";
            string magnitudeBucket = Math.Round(magnitude, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string logKey =
                (attackerAgent.Index).ToString() + "|" +
                (entryId ?? string.Empty) + "|" +
                skillId + "|" +
                weaponComponent.WeaponClass + "|" +
                magnitudeBucket;

            if (!_loggedMissileMagnitudeKeys.Add(logKey))
                return;

            ModLogger.Info(
                "CoopCampaignDerivedStrikeMagnitudeCalculationModel: missile magnitude sample. " +
                "Agent=" + attackerAgent.Index +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + skillId +
                " ExactSkill=" + exactSkill +
                " WeaponClass=" + weaponComponent.WeaponClass +
                " MissileTotalDamage=" + collisionData.MissileTotalDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " MissileStartSpeed=" + collisionData.MissileStartingBaseSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " MissileSpeed=" + missileSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Magnitude=" + magnitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Mission=" + mission.SceneName + ".");
        }

        private void TryLogBattleActivation(Agent agent)
        {
            if (_hasLoggedBattleActivation || agent?.Mission == null)
                return;

            _hasLoggedBattleActivation = true;
            ModLogger.Info(
                "CoopCampaignDerivedStrikeMagnitudeCalculationModel: activated for CoopBattle mission. " +
                "Scene=" + (agent.Mission.SceneName ?? "null") +
                " BaseModel=" + _baseModel.GetType().FullName + ".");
        }

        private static SkillObject ResolveRelevantSkill(WeaponComponentData weaponComponent)
        {
            if (weaponComponent == null)
                return null;

            SkillObject relevantSkill = weaponComponent.RelevantSkill;
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

            switch (weaponComponent.WeaponClass)
            {
                case WeaponClass.OneHandedSword:
                case WeaponClass.OneHandedAxe:
                case WeaponClass.Mace:
                case WeaponClass.Pick:
                case WeaponClass.Dagger:
                case WeaponClass.OneHandedPolearm:
                case WeaponClass.SmallShield:
                case WeaponClass.LargeShield:
                    return DefaultSkills.OneHanded;
                case WeaponClass.TwoHandedSword:
                case WeaponClass.TwoHandedAxe:
                case WeaponClass.TwoHandedMace:
                    return DefaultSkills.TwoHanded;
                case WeaponClass.TwoHandedPolearm:
                case WeaponClass.LowGripPolearm:
                    return DefaultSkills.Polearm;
                case WeaponClass.Arrow:
                case WeaponClass.Bow:
                    return DefaultSkills.Bow;
                case WeaponClass.Bolt:
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

        private static bool TryApplyExactPersonalArmorPenetration(
            Agent attackerAgent,
            in AttackCollisionData collisionData,
            float baseArmor,
            float adjustedArmor,
            WeaponComponentData weaponComponent,
            out float exactAdjustedArmor)
        {
            exactAdjustedArmor = adjustedArmor;
            attackerAgent = ResolveHumanAgent(attackerAgent);
            if (attackerAgent == null || weaponComponent == null || adjustedArmor <= 0f)
                return false;

            SkillObject relevantSkill = ResolveRelevantSkill(weaponComponent);
            string skillId = relevantSkill?.StringId ?? string.Empty;
            if (!IsSupportedArmorPenetrationSkill(skillId))
                return false;

            if (string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase) &&
                baseArmor < 20f &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "CrossbowPiercer", out _))
            {
                exactAdjustedArmor = 0f;
                return true;
            }

            if (weaponComponent.WeaponClass == WeaponClass.Sling &&
                IsHeadHit(collisionData) &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "ThrowingSlingingCompetitions", out _))
            {
                exactAdjustedArmor = 0f;
                return true;
            }

            float penetrationFactor = 0f;
            if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "TwoHandedVandal", out _))
                penetrationFactor += 0.25f;

            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase) &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "OneHandedChinkInTheArmor", out _))
            {
                penetrationFactor += 0.1f;
            }

            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "BowBodkin", out _))
            {
                penetrationFactor += 0.1f;
            }

            if (string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase) &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "CrossbowPuncture", out _))
            {
                penetrationFactor += 0.1f;
            }

            if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase) &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "ThrowingWeakSpot", out _))
            {
                penetrationFactor += 0.3f;
            }

            if (penetrationFactor <= 0f)
                return false;

            penetrationFactor = MBMath.ClampFloat(penetrationFactor, 0f, 0.95f);
            exactAdjustedArmor = TaleWorlds.Library.MathF.Max(0f, adjustedArmor * (1f - penetrationFactor));
            return Math.Abs(exactAdjustedArmor - adjustedArmor) > 0.0001f;
        }

        private static bool IsSupportedArmorPenetrationSkill(string skillId)
        {
            return string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase);
        }

        private static Agent ResolveHumanAgent(Agent agent)
        {
            if (agent == null)
                return null;

            if (agent.IsMount)
                return agent.RiderAgent;

            return agent.IsHuman ? agent : null;
        }

        private static bool IsHeadHit(in AttackCollisionData collisionData)
        {
            return (int)collisionData.VictimHitBodyPart == 0;
        }

        private static bool ShouldUseSandboxArmorFormula()
        {
            Mission mission = Mission.Current;
            return mission != null &&
                MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName);
        }

        private float ComputeSandboxRawDamage(
            DamageTypes damageType,
            float magnitude,
            float armorEffectiveness,
            float absorbedDamageRatio)
        {
            float bluntDamageFactor = GetBluntDamageFactorByDamageType(damageType);
            float armorScale = 50f / (50f + armorEffectiveness);
            float scaledMagnitude = magnitude * armorScale;
            float bluntPortion = bluntDamageFactor * scaledMagnitude;
            float reducedDamage;

            switch (damageType)
            {
                case DamageTypes.Cut:
                    reducedDamage = TaleWorlds.Library.MathF.Max(0f, scaledMagnitude - armorEffectiveness * 0.5f);
                    break;
                case DamageTypes.Pierce:
                    reducedDamage = TaleWorlds.Library.MathF.Max(0f, scaledMagnitude - armorEffectiveness * 0.33f);
                    break;
                case DamageTypes.Blunt:
                    reducedDamage = TaleWorlds.Library.MathF.Max(0f, scaledMagnitude - armorEffectiveness * 0.2f);
                    break;
                default:
                    return _baseModel.ComputeRawDamage(damageType, magnitude, armorEffectiveness, absorbedDamageRatio);
            }

            return (bluntPortion + (1f - bluntDamageFactor) * reducedDamage) * absorbedDamageRatio;
        }
    }
}
