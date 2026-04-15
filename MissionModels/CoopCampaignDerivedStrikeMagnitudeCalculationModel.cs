using System;
using System.Collections.Generic;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator.MissionModels
{
    /// <summary>
    /// Thin low-level wrapper over the active strike magnitude model.
    /// Phase 1 is diagnostics-only: it keeps vanilla magnitude behavior intact,
    /// but logs exact campaign hero missile magnitude inputs for CoopBattle.
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

        public override float ComputeRawDamage(DamageTypes damageType, float magnitude, float armorEffectiveness, float absorbedDamageRatio)
        {
            return _baseModel.ComputeRawDamage(damageType, magnitude, armorEffectiveness, absorbedDamageRatio);
        }

        public override float CalculateStrikeMagnitudeForUnarmedAttack(in AttackInformation attackInformation, in AttackCollisionData collisionData, float progressEffect, float momentumRemaining)
        {
            return _baseModel.CalculateStrikeMagnitudeForUnarmedAttack(attackInformation, collisionData, progressEffect, momentumRemaining);
        }

        public override float GetBluntDamageFactorByDamageType(DamageTypes damageType)
        {
            return _baseModel.GetBluntDamageFactorByDamageType(damageType);
        }

        public override float CalculateHorseArcheryFactor(BasicCharacterObject characterObject)
        {
            return _baseModel.CalculateHorseArcheryFactor(characterObject);
        }

        public override float CalculateAdjustedArmorForBlow(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseArmor, BasicCharacterObject attackerCharacter, BasicCharacterObject attackerCaptainCharacter, BasicCharacterObject victimCharacter, BasicCharacterObject victimCaptainCharacter, WeaponComponentData weaponComponent)
        {
            return _baseModel.CalculateAdjustedArmorForBlow(attackInformation, collisionData, baseArmor, attackerCharacter, attackerCaptainCharacter, victimCharacter, victimCaptainCharacter, weaponComponent);
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
                if (string.Equals(relevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase))
                {
                    return relevantSkill;
                }
            }

            switch (weaponComponent.WeaponClass)
            {
                case WeaponClass.Arrow:
                case WeaponClass.Bow:
                    return DefaultSkills.Bow;
                case WeaponClass.Bolt:
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
    }
}
