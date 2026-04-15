using System;
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
    /// Low-level wrapper over the active MP AgentApplyDamageModel.
    /// Keeps stable MP runtime behavior intact, but injects a narrow
    /// campaign-derived personal ranged damage subset for exact hero profiles
    /// in CoopBattle missions.
    /// </summary>
    public sealed class CoopCampaignDerivedAgentApplyDamageModel : AgentApplyDamageModel
    {
        private readonly AgentApplyDamageModel _baseModel;
        private bool _hasLoggedBattleActivation;

        public CoopCampaignDerivedAgentApplyDamageModel(AgentApplyDamageModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override bool IsDamageIgnored(in AttackInformation attackInformation, in AttackCollisionData collisionData)
        {
            return _baseModel.IsDamageIgnored(attackInformation, collisionData);
        }

        public override float ApplyDamageAmplifications(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            float amplifiedDamage = _baseModel.ApplyDamageAmplifications(attackInformation, collisionData, baseDamage);
            if (!TryApplyExactPersonalRangedDamageAmplifications(
                    attackInformation,
                    collisionData,
                    amplifiedDamage,
                    out float updatedDamage,
                    out string entryId,
                    out string skillId,
                    out string factorSummary))
            {
                return amplifiedDamage;
            }

            TryLogBattleActivation(attackInformation.AttackerAgent);
            TryLogDamageAmplificationSample(
                attackInformation,
                collisionData,
                entryId,
                skillId,
                amplifiedDamage,
                updatedDamage,
                factorSummary);
            return updatedDamage;
        }

        public override float ApplyDamageScaling(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            return _baseModel.ApplyDamageScaling(attackInformation, collisionData, baseDamage);
        }

        public override float ApplyDamageReductions(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            return _baseModel.ApplyDamageReductions(attackInformation, collisionData, baseDamage);
        }

        public override float ApplyGeneralDamageModifiers(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            return _baseModel.ApplyGeneralDamageModifiers(attackInformation, collisionData, baseDamage);
        }

        public override void DecideMissileWeaponFlags(Agent attackerAgent, in MissionWeapon missileWeapon, ref WeaponFlags missileWeaponFlags)
        {
            _baseModel.DecideMissileWeaponFlags(attackerAgent, missileWeapon, ref missileWeaponFlags);
        }

        public override void CalculateDefendedBlowStunMultipliers(
            Agent attackerAgent,
            Agent defenderAgent,
            CombatCollisionResult collisionResult,
            WeaponComponentData attackerWeapon,
            WeaponComponentData defenderWeapon,
            ref float attackerStunPeriod,
            ref float defenderStunPeriod)
        {
            _baseModel.CalculateDefendedBlowStunMultipliers(
                attackerAgent,
                defenderAgent,
                collisionResult,
                attackerWeapon,
                defenderWeapon,
                ref attackerStunPeriod,
                ref defenderStunPeriod);
        }

        public override float CalculateStaggerThresholdDamage(Agent defenderAgent, in Blow blow)
        {
            return _baseModel.CalculateStaggerThresholdDamage(defenderAgent, blow);
        }

        public override float CalculateAlternativeAttackDamage(in AttackInformation attackInformation, in AttackCollisionData collisionData, WeaponComponentData weapon)
        {
            return _baseModel.CalculateAlternativeAttackDamage(attackInformation, collisionData, weapon);
        }

        public override float CalculatePassiveAttackDamage(BasicCharacterObject attackerCharacter, in AttackCollisionData collisionData, float baseDamage)
        {
            return _baseModel.CalculatePassiveAttackDamage(attackerCharacter, collisionData, baseDamage);
        }

        public override MeleeCollisionReaction DecidePassiveAttackCollisionReaction(Agent attacker, Agent defender, bool isFatalHit)
        {
            return _baseModel.DecidePassiveAttackCollisionReaction(attacker, defender, isFatalHit);
        }

        public override void DecideWeaponCollisionReaction(
            in Blow registeredBlow,
            in AttackCollisionData collisionData,
            Agent attacker,
            Agent defender,
            in MissionWeapon attackerWeapon,
            bool isFatalHit,
            bool isShruggedOff,
            float momentumRemaining,
            out MeleeCollisionReaction colReaction)
        {
            _baseModel.DecideWeaponCollisionReaction(
                registeredBlow,
                collisionData,
                attacker,
                defender,
                attackerWeapon,
                isFatalHit,
                isShruggedOff,
                momentumRemaining,
                out colReaction);
        }

        public override float CalculateShieldDamage(in AttackInformation attackInformation, float baseDamage)
        {
            return _baseModel.CalculateShieldDamage(attackInformation, baseDamage);
        }

        public override float CalculateSailFireDamage(Agent attackerAgent, IShipOrigin shipOrigin, float baseDamage, bool damageFromShipMachine)
        {
            return _baseModel.CalculateSailFireDamage(attackerAgent, shipOrigin, baseDamage, damageFromShipMachine);
        }

        public override float CalculateHullFireDamage(float baseFireDamage, IShipOrigin shipOrigin)
        {
            return _baseModel.CalculateHullFireDamage(baseFireDamage, shipOrigin);
        }

        public override float GetDamageMultiplierForBodyPart(BoneBodyPartType bodyPart, DamageTypes type, bool isHuman, bool isMissile)
        {
            return _baseModel.GetDamageMultiplierForBodyPart(bodyPart, type, isHuman, isMissile);
        }

        public override bool CanWeaponIgnoreFriendlyFireChecks(WeaponComponentData weapon)
        {
            return _baseModel.CanWeaponIgnoreFriendlyFireChecks(weapon);
        }

        public override bool CanWeaponDealSneakAttack(in AttackInformation attackInformation, WeaponComponentData weapon)
        {
            return _baseModel.CanWeaponDealSneakAttack(attackInformation, weapon);
        }

        public override bool CanWeaponDismount(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            return _baseModel.CanWeaponDismount(attackerAgent, attackerWeapon, blow, collisionData);
        }

        public override bool CanWeaponKnockback(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            return _baseModel.CanWeaponKnockback(attackerAgent, attackerWeapon, blow, collisionData);
        }

        public override bool CanWeaponKnockDown(Agent attackerAgent, Agent victimAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            return _baseModel.CanWeaponKnockDown(attackerAgent, victimAgent, attackerWeapon, blow, collisionData);
        }

        public override bool DecideCrushedThrough(Agent attackerAgent, Agent defenderAgent, float totalAttackEnergy, Agent.UsageDirection attackDirection, StrikeType strikeType, WeaponComponentData defendItem, bool isPassiveUsageHit)
        {
            return _baseModel.DecideCrushedThrough(attackerAgent, defenderAgent, totalAttackEnergy, attackDirection, strikeType, defendItem, isPassiveUsageHit);
        }

        public override float CalculateRemainingMomentum(float originalMomentum, in Blow b, in AttackCollisionData collisionData, Agent attacker, Agent victim, in MissionWeapon attackerWeapon, bool isCrushThrough)
        {
            return _baseModel.CalculateRemainingMomentum(originalMomentum, b, collisionData, attacker, victim, attackerWeapon, isCrushThrough);
        }

        public override bool DecideAgentShrugOffBlow(Agent victimAgent, in AttackCollisionData collisionData, in Blow blow)
        {
            return _baseModel.DecideAgentShrugOffBlow(victimAgent, collisionData, blow);
        }

        public override bool DecideAgentDismountedByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, in Blow blow)
        {
            return _baseModel.DecideAgentDismountedByBlow(attackerAgent, victimAgent, collisionData, attackerWeapon, blow);
        }

        public override bool DecideAgentKnockedBackByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, in Blow blow)
        {
            return _baseModel.DecideAgentKnockedBackByBlow(attackerAgent, victimAgent, collisionData, attackerWeapon, blow);
        }

        public override bool DecideAgentKnockedDownByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, in Blow blow)
        {
            return _baseModel.DecideAgentKnockedDownByBlow(attackerAgent, victimAgent, collisionData, attackerWeapon, blow);
        }

        public override bool DecideMountRearedByBlow(Agent attackerAgent, Agent victimAgent, in AttackCollisionData collisionData, WeaponComponentData attackerWeapon, in Blow blow)
        {
            return _baseModel.DecideMountRearedByBlow(attackerAgent, victimAgent, collisionData, attackerWeapon, blow);
        }

        public override bool ShouldMissilePassThroughAfterShieldBreak(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            return _baseModel.ShouldMissilePassThroughAfterShieldBreak(attackerAgent, attackerWeapon);
        }

        public override float GetDismountPenetration(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            return _baseModel.GetDismountPenetration(attackerAgent, attackerWeapon, blow, collisionData);
        }

        public override float GetKnockBackPenetration(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            return _baseModel.GetKnockBackPenetration(attackerAgent, attackerWeapon, blow, collisionData);
        }

        public override float GetKnockDownPenetration(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            return _baseModel.GetKnockDownPenetration(attackerAgent, attackerWeapon, blow, collisionData);
        }

        public override float GetHorseChargePenetration()
        {
            return _baseModel.GetHorseChargePenetration();
        }

        internal static bool IsActiveForMission(Mission mission)
        {
            return mission != null &&
                   MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) &&
                   MissionGameModels.Current?.AgentApplyDamageModel is CoopCampaignDerivedAgentApplyDamageModel;
        }

        private bool TryApplyExactPersonalRangedDamageAmplifications(
            in AttackInformation attackInformation,
            in AttackCollisionData collisionData,
            float baseDamage,
            out float updatedDamage,
            out string entryId,
            out string skillId,
            out string factorSummary)
        {
            updatedDamage = baseDamage;
            entryId = string.Empty;
            skillId = "null";
            factorSummary = string.Empty;

            Agent attackerAgent = attackInformation.AttackerAgent;
            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            if (attackerAgent == null || weapon == null || !attackerAgent.IsHuman)
                return false;

            Mission mission = attackerAgent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            SkillObject relevantSkill = ResolveRelevantSkill(weapon);
            if (relevantSkill == null)
                return false;

            skillId = relevantSkill.StringId ?? "null";
            if (!IsSupportedRangedDamageSkill(skillId))
                return false;

            if (!CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(attackerAgent, relevantSkill, out int exactSkill, out entryId))
                return false;

            float totalFactor = 1f;

            if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "BowStrongBows", out _))
                {
                    totalFactor *= 1.08f;
                    factorSummary = AppendFactorSummary(factorSummary, "StrongBows=1.08");
                }

                if (attackInformation.IsHeadShot &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "BowDeadAim", out _))
                {
                    totalFactor *= 1.3f;
                    factorSummary = AppendFactorSummary(factorSummary, "DeadAim=1.3");
                }

                if (exactSkill > 200 &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "BowDeadshot", out _))
                {
                    float deadshotFactor = 1f + (exactSkill - 200) * 0.005f;
                    totalFactor *= deadshotFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "Deadshot=" + deadshotFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "AthleticsStrongArms", out _))
                {
                    totalFactor *= 1.05f;
                    factorSummary = AppendFactorSummary(factorSummary, "StrongArms=1.05");
                }

                if (attackInformation.IsHeadShot &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "ThrowingHeadHunter", out _))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "HeadHunter=1.5");
                }

                if (attackInformation.VictimHitPointRate > 0f &&
                    attackInformation.VictimHitPointRate < 0.5f &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "ThrowingLastHit", out _))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "LastHit=1.5");
                }

                if (exactSkill > 200 &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "ThrowingUnstoppableForce", out _))
                {
                    float unstoppableForceFactor = 1f + (exactSkill - 200) * 0.005f;
                    totalFactor *= unstoppableForceFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "UnstoppableForce=" + unstoppableForceFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            if ((attackInformation.DoesAttackerHaveMountAgent || attackInformation.IsAttackerAgentMount) &&
                weapon.IsConsumable &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "RidingHorseArcher", out _))
            {
                totalFactor *= 1.1f;
                factorSummary = AppendFactorSummary(factorSummary, "HorseArcher=1.1");
            }

            if (Math.Abs(totalFactor - 1f) < 0.0001f)
                return false;

            updatedDamage = MathF.Max(0f, baseDamage * totalFactor);
            return Math.Abs(updatedDamage - baseDamage) > 0.0001f;
        }

        private void TryLogBattleActivation(Agent agent)
        {
            if (_hasLoggedBattleActivation || agent?.Mission == null)
                return;

            _hasLoggedBattleActivation = true;
            ModLogger.Info(
                "CoopCampaignDerivedAgentApplyDamageModel: activated for CoopBattle mission. " +
                "Scene=" + (agent.Mission.SceneName ?? "null") +
                " BaseModel=" + _baseModel.GetType().FullName + ".");
        }

        private static void TryLogDamageAmplificationSample(
            in AttackInformation attackInformation,
            in AttackCollisionData collisionData,
            string entryId,
            string skillId,
            float baseDamage,
            float updatedDamage,
            string factorSummary)
        {
            Agent attackerAgent = attackInformation.AttackerAgent;
            Agent victimAgent = attackInformation.VictimAgent;
            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;

            ModLogger.Info(
                "CoopCampaignDerivedAgentApplyDamageModel: exact ranged damage amplification applied. " +
                "Attacker=" + (attackerAgent?.Index ?? -1) +
                " Victim=" + (victimAgent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + (string.IsNullOrWhiteSpace(skillId) ? "null" : skillId) +
                " WeaponClass=" + (weapon?.WeaponClass.ToString() ?? "None") +
                " BaseDamage=" + baseDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " ExactDamage=" + updatedDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Factors=" + (string.IsNullOrWhiteSpace(factorSummary) ? "none" : factorSummary) +
                " HeadShot=" + attackInformation.IsHeadShot +
                " VictimHpRate=" + attackInformation.VictimHitPointRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Mounted=" + (attackInformation.DoesAttackerHaveMountAgent || attackInformation.IsAttackerAgentMount) +
                " BodyPart=" + collisionData.VictimHitBodyPart +
                " Mission=" + (attackerAgent?.Mission?.SceneName ?? "null") + ".");
        }

        private static bool IsSupportedRangedDamageSkill(string skillId)
        {
            return string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase);
        }

        private static SkillObject ResolveRelevantSkill(WeaponComponentData weapon)
        {
            SkillObject relevantSkill = weapon?.RelevantSkill;
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

            if (weapon == null)
                return null;

            switch (weapon.WeaponClass)
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

        private static string AppendFactorSummary(string currentSummary, string addition)
        {
            if (string.IsNullOrWhiteSpace(addition))
                return currentSummary ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentSummary))
                return addition;

            return currentSummary + "/" + addition;
        }
    }
}
