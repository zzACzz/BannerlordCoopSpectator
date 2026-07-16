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
    /// Low-level wrapper over the active MP AgentApplyDamageModel.
    /// Keeps stable MP runtime behavior intact, but injects a narrow
    /// campaign-derived personal damage subset for exact hero profiles
    /// in CoopBattle missions.
    /// </summary>
    public sealed class CoopCampaignDerivedAgentApplyDamageModel : AgentApplyDamageModel
    {
        private readonly AgentApplyDamageModel _baseModel;
        private readonly HashSet<string> _loggedDamageAmplificationKeys = new HashSet<string>(StringComparer.Ordinal);
        private bool _hasLoggedBattleActivation;
        private const int DamageAmplificationDiagnosticBudget = 96;

        public CoopCampaignDerivedAgentApplyDamageModel(AgentApplyDamageModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override bool IsDamageIgnored(in AttackInformation attackInformation, in AttackCollisionData collisionData)
        {
            if (_baseModel.IsDamageIgnored(attackInformation, collisionData))
                return true;

            Agent exactVictimAgent = ResolveExactVictimHumanAgent(attackInformation);
            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            if (exactVictimAgent == null ||
                weapon == null ||
                !weapon.IsConsumable ||
                !collisionData.CollidedWithShieldOnBack ||
                !CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(exactVictimAgent, "CrossbowPavise", out _))
            {
                return false;
            }

            return MBRandom.RandomFloat <= 0.75f;
        }

        public override float ApplyDamageAmplifications(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            float amplifiedDamage = _baseModel.ApplyDamageAmplifications(attackInformation, collisionData, baseDamage);
            bool personalApplied = TryApplyExactPersonalDamageAmplifications(
                    attackInformation,
                    collisionData,
                    amplifiedDamage,
                    out float updatedDamage,
                    out string entryId,
                    out string skillId,
                    out string factorSummary);
            if (!personalApplied)
                updatedDamage = amplifiedDamage;
            float personalDamage = updatedDamage;

            bool captainApplied = TryApplyGlobalCaptainDamageAmplifications(
                attackInformation,
                collisionData,
                updatedDamage,
                out float captainUpdatedDamage,
                out string captainEntryId,
                out string captainFactorSummary);
            if (captainApplied)
                updatedDamage = captainUpdatedDamage;

            if (!personalApplied && !captainApplied)
                return amplifiedDamage;

            TryLogBattleActivation(attackInformation.AttackerAgent);
            TryLogDamageAmplificationSample(
                attackInformation,
                collisionData,
                string.IsNullOrWhiteSpace(entryId) ? captainEntryId : entryId,
                skillId,
                amplifiedDamage,
                personalDamage,
                updatedDamage,
                personalApplied,
                captainApplied,
                factorSummary,
                captainFactorSummary);
            return updatedDamage;
        }

        public override float ApplyDamageScaling(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            return _baseModel.ApplyDamageScaling(attackInformation, collisionData, baseDamage);
        }

        public override float ApplyDamageReductions(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            float reducedDamage = _baseModel.ApplyDamageReductions(attackInformation, collisionData, baseDamage);
            Agent victimAgent = ResolveExactVictimHumanAgent(attackInformation);
            if (victimAgent == null)
                return reducedDamage;

            float totalFactor = 1f;
            WeaponComponentData attackerWeapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            WeaponComponentData victimWeapon = attackInformation.VictimMainHandWeapon.CurrentUsageItem;

            if (collisionData.IsMissile && attackerWeapon != null && attackerWeapon.IsConsumable)
            {
                if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "BowSkirmishPhaseMaster", out _))
                    totalFactor *= 0.9f;

                if (victimWeapon != null &&
                    IsCrossbowSkill(ResolveRelevantSkill(victimWeapon)?.StringId) &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "CrossbowCounterFire", out _))
                {
                    totalFactor *= 0.9f;
                }

                if (victimWeapon != null &&
                    string.Equals(ResolveRelevantSkill(victimWeapon)?.StringId, "Throwing", StringComparison.OrdinalIgnoreCase) &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "ThrowingSkirmisher", out _))
                {
                    totalFactor *= 0.9f;
                }
            }

            if (IsShieldHit(collisionData))
            {
                if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "OneHandedSteelCoreShields", out _))
                    totalFactor *= 0.9f;

                if (collisionData.AttackBlockedWithShield &&
                    !collisionData.CorrectSideShieldBlock &&
                    CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "OneHandedShieldWall", out _))
                {
                    totalFactor *= 0.8f;
                }
            }

            if (collisionData.IsHorseCharge)
            {
                if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "PolearmSureFooted", out _))
                    totalFactor *= 0.6f;

                if (CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "AthleticsBraced", out _))
                    totalFactor *= 0.6f;
            }

            if (collisionData.IsFallDamage &&
                CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(victimAgent, "AthleticsStrongLegs", out _))
            {
                totalFactor *= 0.5f;
            }

            float exactPersonalResult = MathF.Max(0f, reducedDamage * totalFactor);
            if (!TryResolveGlobalCaptainEntryId(victimAgent, out string victimEntryId))
                return exactPersonalResult;

            var captainAccumulator = new CaptainPerkBonusAccumulator(exactPersonalResult);
            if (collisionData.IsMissile && attackerWeapon != null && attackerWeapon.IsConsumable)
            {
                GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "ThrowingSkirmisher", captainAccumulator);
                if (victimAgent.Character?.IsRanged == true)
                    GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "BowSkirmishPhaseMaster", captainAccumulator);
                if (victimWeapon != null && IsCrossbowSkill(ResolveRelevantSkill(victimWeapon)?.StringId))
                    GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "CrossbowCounterFire", captainAccumulator);
            }

            if (IsShieldHit(collisionData))
                GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "OneHandedSteelCoreShields", captainAccumulator);
            if (collisionData.IsHorseCharge)
            {
                GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "PolearmSureFooted", captainAccumulator);
                GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "AthleticsBraced", captainAccumulator);
            }
            if (victimAgent.Formation != null &&
                (int)victimAgent.Formation.ArrangementOrder.OrderEnum == 5 &&
                attackerWeapon?.IsMeleeWeapon == true)
            {
                GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "OneHandedBasher", captainAccumulator);
            }
            GlobalCaptainPerkRuntimeState.AddEffect(victimEntryId, "TacticsEliteReserves", captainAccumulator);

            return captainAccumulator.HasEffects
                ? MathF.Max(0f, captainAccumulator.Result)
                : exactPersonalResult;
        }

        public override float ApplyGeneralDamageModifiers(in AttackInformation attackInformation, in AttackCollisionData collisionData, float baseDamage)
        {
            if (!ShouldUseCampaignDamageRules(attackInformation))
                return _baseModel.ApplyGeneralDamageModifiers(attackInformation, collisionData, baseDamage);

            Agent attackerAgent = attackInformation.AttackerAgent;
            if (attackerAgent == null)
                return baseDamage;

            float adjustedDamage = baseDamage;
            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            if (weapon != null)
            {
                if (weapon.RelevantSkill == DefaultSkills.Throwing)
                {
                    adjustedDamage *= 1f + attackerAgent.AgentDrivenProperties.ThrowingWeaponDamageMultiplierBonus;
                }
                else if (weapon.IsMeleeWeapon)
                {
                    adjustedDamage *= 1f + attackerAgent.AgentDrivenProperties.MeleeWeaponDamageMultiplierBonus;
                }
            }

            adjustedDamage *= 1f + attackerAgent.AgentDrivenProperties.DamageMultiplierBonus;
            return adjustedDamage;
        }

        public override void DecideMissileWeaponFlags(Agent attackerAgent, in MissionWeapon missileWeapon, ref WeaponFlags missileWeaponFlags)
        {
            _baseModel.DecideMissileWeaponFlags(attackerAgent, missileWeapon, ref missileWeaponFlags);

            WeaponComponentData weapon = missileWeapon.CurrentUsageItem;
            if (weapon != null &&
                weapon.WeaponClass == WeaponClass.Javelin &&
                HasExactPersonalPerk(attackerAgent, "ThrowingImpale"))
            {
                missileWeaponFlags |= WeaponFlags.CanPenetrateShield;
            }
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

            if (((int)collisionResult == 3 || (int)collisionResult == 4) &&
                HasExactPersonalPerk(attackerAgent, "AthleticsMightyBlow"))
            {
                attackerStunPeriod *= 1.05f;
            }
        }

        public override float CalculateStaggerThresholdDamage(Agent defenderAgent, in Blow blow)
        {
            float threshold = _baseModel.CalculateStaggerThresholdDamage(defenderAgent, blow);
            Agent humanDefender = ResolveHumanAgent(defenderAgent);
            if (humanDefender == null)
                return threshold;

            float factor = 0f;
            if (IsAgentMounted(humanDefender))
            {
                if (HasExactPersonalPerk(humanDefender, "RidingDauntlessSteed"))
                    factor += 0.5f;
            }
            else if (HasExactPersonalPerk(humanDefender, "AthleticsSpartan"))
            {
                factor += 0.5f;
            }

            MissionWeapon wieldedWeapon = humanDefender.WieldedWeapon;
            WeaponComponentData currentWeapon = wieldedWeapon.CurrentUsageItem;
            if (currentWeapon != null &&
                IsCrossbowSkill(ResolveRelevantSkill(currentWeapon)?.StringId) &&
                wieldedWeapon.IsReloading)
            {
                if (HasExactPersonalPerk(humanDefender, "CrossbowDeftHands"))
                    factor += 0.5f;

                if (TryResolveGlobalCaptainEntryId(humanDefender, out string entryId))
                {
                    var captainAccumulator = new CaptainPerkBonusAccumulator(1f);
                    GlobalCaptainPerkRuntimeState.AddEffect(entryId, "CrossbowDeftHands", captainAccumulator);
                    if (captainAccumulator.HasEffects)
                        factor += MathF.Max(0f, captainAccumulator.Result - 1f);
                }
            }

            return MathF.Max(0f, threshold * (1f + factor));
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
            MeleeCollisionReaction reaction = _baseModel.DecidePassiveAttackCollisionReaction(attacker, defender, isFatalHit);
            if (!isFatalHit || !IsAgentMounted(attacker))
                return reaction;

            float slicedThroughChance = 0.05f;
            if (HasExactPersonalPerk(attacker, "PolearmSkewer"))
                slicedThroughChance += 0.3f;

            return MBRandom.RandomFloat < slicedThroughChance
                ? MeleeCollisionReaction.SlicedThrough
                : reaction;
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
            if (ShouldUseCampaignDamageRules(attackInformation))
                return baseDamage;

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
            if (_baseModel.CanWeaponDismount(attackerAgent, attackerWeapon, blow, collisionData))
                return true;

            return IsDismountableBodyPart(blow.VictimBodyPart) &&
                   (HasExactPersonalCrossbowHammerBolts(attackerAgent, attackerWeapon) ||
                    HasExactPersonalThrowingKnockOff(attackerAgent, attackerWeapon));
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
            float penetration = _baseModel.GetDismountPenetration(attackerAgent, attackerWeapon, blow, collisionData);
            if (HasExactPersonalCrossbowHammerBolts(attackerAgent, attackerWeapon))
                penetration += 0.5f;

            if (HasExactPersonalThrowingKnockOff(attackerAgent, attackerWeapon))
                penetration += 0.25f;

            if (HasExactPersonalPolearmBraced(attackerAgent, attackerWeapon))
                penetration += 0.25f;

            return MathF.Max(0f, penetration);
        }

        public override float GetKnockBackPenetration(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            float penetration = _baseModel.GetKnockBackPenetration(attackerAgent, attackerWeapon, blow, collisionData);
            if (IsThrustCollision(collisionData) &&
                HasExactPersonalPolearmKeepAtBay(attackerAgent, attackerWeapon))
            {
                penetration += 0.3f;
            }

            return MathF.Max(0f, penetration);
        }

        public override float GetKnockDownPenetration(Agent attackerAgent, WeaponComponentData attackerWeapon, in Blow blow, in AttackCollisionData collisionData)
        {
            float penetration = _baseModel.GetKnockDownPenetration(attackerAgent, attackerWeapon, blow, collisionData);
            if (IsSwingCollision(collisionData) &&
                HasExactPersonalTwoHandedShowOfStrength(attackerAgent, attackerWeapon))
            {
                penetration += 0.3f;
            }

            if (HasExactPersonalPolearmHardKnock(attackerAgent, attackerWeapon))
                penetration += 0.25f;

            return MathF.Max(0f, penetration);
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

        private static bool ShouldUseCampaignDamageRules(in AttackInformation attackInformation)
        {
            Mission mission =
                attackInformation.AttackerAgent?.Mission ??
                attackInformation.VictimAgent?.Mission ??
                Mission.Current;
            return mission != null &&
                MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName);
        }

        private bool TryApplyExactPersonalDamageAmplifications(
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

            Agent attackerAgent = ResolveExactAttackerHumanAgent(attackInformation);
            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            if (attackerAgent == null || !attackerAgent.IsHuman)
                return false;

            if (weapon != null && IsBallistaProjectileWeapon(attackInformation.AttackerWeapon))
                return false;

            Mission mission = attackerAgent.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            SkillObject relevantSkill = ResolveRelevantSkill(weapon);
            int exactSkill = 0;
            if (relevantSkill != null)
            {
                skillId = relevantSkill.StringId ?? "null";
                if (!IsSupportedDamageSkill(skillId))
                    return false;

                TryResolveExactSkill(attackerAgent, relevantSkill, ref entryId, out exactSkill);
            }
            else if (!collisionData.IsAlternativeAttack && !collisionData.IsHorseCharge)
            {
                return false;
            }

            float totalFactor = 1f;
            float additiveDamage = 0f;

            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
            {
                if (TryHasExactPerk(attackerAgent, "OneHandedDeadlyPurpose", ref entryId))
                {
                    totalFactor *= 1.05f;
                    factorSummary = AppendFactorSummary(factorSummary, "DeadlyPurpose=1.05");
                }

                if (IsAgentMounted(attackerAgent) &&
                    TryHasExactPerk(attackerAgent, "OneHandedCavalry", ref entryId))
                {
                    totalFactor *= 1.05f;
                    factorSummary = AppendFactorSummary(factorSummary, "OneHandedCavalry=1.05");
                }

                if (IsOffHandEmpty(attackerAgent) &&
                    TryHasExactPerk(attackerAgent, "OneHandedDuelist", ref entryId))
                {
                    totalFactor *= 1.2f;
                    factorSummary = AppendFactorSummary(factorSummary, "Duelist=1.2");
                }

                if (IsOneHandedAxeOrMace(weapon) &&
                    TryHasExactPerk(attackerAgent, "OneHandedToBeBlunt", ref entryId))
                {
                    totalFactor *= 1.05f;
                    factorSummary = AppendFactorSummary(factorSummary, "ToBeBlunt=1.05");
                }

                if (IsShieldHit(collisionData) &&
                    TryHasExactPerk(attackerAgent, "OneHandedPrestige", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "Prestige=1.5");
                }

                if (weapon != null &&
                    weapon.IsShield &&
                    TryHasExactPerk(attackerAgent, "OneHandedBasher", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "Basher=1.5");
                }

                if (exactSkill > 250 &&
                    TryHasExactPerk(attackerAgent, "OneHandedWayOfTheSword", ref entryId))
                {
                    float wayOfTheSwordFactor = 1f + (exactSkill - 250) * 0.005f;
                    totalFactor *= wayOfTheSwordFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "WayOfTheSword=" + wayOfTheSwordFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
            {
                if (IsShieldHit(collisionData) &&
                    TryHasExactPerk(attackerAgent, "TwoHandedWoodChopper", ref entryId))
                {
                    totalFactor *= 1.3f;
                    factorSummary = AppendFactorSummary(factorSummary, "WoodChopper=1.3");
                }

                if (IsShieldHit(collisionData) &&
                    TryHasExactPerk(attackerAgent, "TwoHandedShieldBreaker", ref entryId))
                {
                    totalFactor *= 1.4f;
                    factorSummary = AppendFactorSummary(factorSummary, "ShieldBreaker=1.4");
                }

                if (IsTwoHandedAxeOrMace(weapon) &&
                    TryHasExactPerk(attackerAgent, "TwoHandedHeadBasher", ref entryId))
                {
                    totalFactor *= 1.1f;
                    factorSummary = AppendFactorSummary(factorSummary, "HeadBasher=1.1");
                }

                if (attackInformation.IsVictimAgentMount &&
                    TryHasExactPerk(attackerAgent, "TwoHandedBeastSlayer", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "BeastSlayer=1.5");
                }

                float attackerHpRate = ResolveHealthRate(attackerAgent);
                if (attackerHpRate > 0f &&
                    attackerHpRate < 0.5f &&
                    TryHasExactPerk(attackerAgent, "TwoHandedBerserker", ref entryId))
                {
                    totalFactor *= 1.2f;
                    factorSummary = AppendFactorSummary(factorSummary, "Berserker=1.2");
                }

                if (attackerHpRate > 0.9f &&
                    TryHasExactPerk(attackerAgent, "TwoHandedConfidence", ref entryId))
                {
                    totalFactor *= 1.15f;
                    factorSummary = AppendFactorSummary(factorSummary, "Confidence=1.15");
                }

                if (TryHasExactPerk(attackerAgent, "TwoHandedBladeMaster", ref entryId))
                {
                    totalFactor *= 1.1f;
                    factorSummary = AppendFactorSummary(factorSummary, "BladeMaster=1.1");
                }

                if (exactSkill > 250 &&
                    TryHasExactPerk(attackerAgent, "TwoHandedWayOfTheGreatAxe", ref entryId))
                {
                    float wayOfTheGreatAxeFactor = 1f + (exactSkill - 250) * 0.005f;
                    totalFactor *= wayOfTheGreatAxeFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "WayOfTheGreatAxe=" + wayOfTheGreatAxeFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAgentMounted(attackerAgent) &&
                    TryHasExactPerk(attackerAgent, "PolearmCavalry", ref entryId))
                {
                    totalFactor *= 1.02f;
                    factorSummary = AppendFactorSummary(factorSummary, "PolearmCavalry=1.02");
                }

                if (!IsAgentMounted(attackerAgent) &&
                    TryHasExactPerk(attackerAgent, "PolearmPikeman", ref entryId))
                {
                    totalFactor *= 1.02f;
                    factorSummary = AppendFactorSummary(factorSummary, "Pikeman=1.02");
                }

                if (IsThrustCollision(collisionData) &&
                    TryHasExactPerk(attackerAgent, "PolearmCleanThrust", ref entryId))
                {
                    totalFactor *= 1.1f;
                    factorSummary = AppendFactorSummary(factorSummary, "CleanThrust=1.1");
                }

                if (IsThrustCollision(collisionData) &&
                    TryHasExactPerk(attackerAgent, "PolearmSharpenTheTip", ref entryId))
                {
                    totalFactor *= 1.05f;
                    factorSummary = AppendFactorSummary(factorSummary, "SharpenTheTip=1.05");
                }

                if (attackInformation.IsVictimAgentMount &&
                    TryHasExactPerk(attackerAgent, "PolearmSteadKiller", ref entryId))
                {
                    totalFactor *= 1.7f;
                    factorSummary = AppendFactorSummary(factorSummary, "SteedKiller=1.7");
                }

                if (attackInformation.IsHeadShot &&
                    TryHasExactPerk(attackerAgent, "PolearmGuards", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "Guards=1.5");
                }

                if (exactSkill > 250 &&
                    TryHasExactPerk(attackerAgent, "PolearmWayOfTheSpear", ref entryId))
                {
                    float wayOfTheSpearFactor = 1f + (exactSkill - 250) * 0.005f;
                    totalFactor *= wayOfTheSpearFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "WayOfTheSpear=" + wayOfTheSpearFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase))
            {
                if (TryHasExactPerk(attackerAgent, "BowStrongBows", ref entryId))
                {
                    totalFactor *= 1.08f;
                    factorSummary = AppendFactorSummary(factorSummary, "StrongBows=1.08");
                }

                if (attackInformation.IsHeadShot &&
                    TryHasExactPerk(attackerAgent, "BowDeadAim", ref entryId))
                {
                    totalFactor *= 1.3f;
                    factorSummary = AppendFactorSummary(factorSummary, "DeadAim=1.3");
                }

                if (attackInformation.IsVictimAgentMount &&
                    TryHasExactPerk(attackerAgent, "BowHunterClan", ref entryId))
                {
                    totalFactor *= 1.3f;
                    factorSummary = AppendFactorSummary(factorSummary, "HunterClan=1.3");
                }

                if (exactSkill > 200 &&
                    TryHasExactPerk(attackerAgent, "BowDeadshot", ref entryId))
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
                if (TryHasExactPerk(attackerAgent, "AthleticsStrongArms", ref entryId))
                {
                    totalFactor *= 1.05f;
                    factorSummary = AppendFactorSummary(factorSummary, "StrongArms=1.05");
                }

                if (attackInformation.IsHeadShot &&
                    TryHasExactPerk(attackerAgent, "ThrowingHeadHunter", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "HeadHunter=1.5");
                }

                if (attackInformation.VictimHitPointRate > 0f &&
                    attackInformation.VictimHitPointRate < 0.5f &&
                    TryHasExactPerk(attackerAgent, "ThrowingLastHit", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "LastHit=1.5");
                }

                if (IsShieldHit(collisionData) &&
                    TryHasExactPerk(attackerAgent, "ThrowingShieldBreaker", ref entryId))
                {
                    totalFactor *= 1.4f;
                    factorSummary = AppendFactorSummary(factorSummary, "ThrowingShieldBreaker=1.4");
                }

                if (IsShieldHit(collisionData) &&
                    weapon != null &&
                    weapon.WeaponClass == WeaponClass.ThrowingAxe &&
                    TryHasExactPerk(attackerAgent, "ThrowingSplinters", ref entryId))
                {
                    totalFactor *= 3f;
                    factorSummary = AppendFactorSummary(factorSummary, "Splinters=3");
                }

                if (attackInformation.IsVictimAgentMount &&
                    TryHasExactPerk(attackerAgent, "ThrowingHunter", ref entryId))
                {
                    totalFactor *= 1.4f;
                    factorSummary = AppendFactorSummary(factorSummary, "ThrowingHunter=1.4");
                }

                if (weapon != null &&
                    !weapon.IsConsumable &&
                    TryHasExactPerk(attackerAgent, "ThrowingFlexibleFighter", ref entryId))
                {
                    totalFactor *= 1.1f;
                    factorSummary = AppendFactorSummary(factorSummary, "FlexibleFighter=1.1");
                }

                if (exactSkill > 200 &&
                    TryHasExactPerk(attackerAgent, "ThrowingUnstoppableForce", ref entryId))
                {
                    float unstoppableForceFactor = 1f + (exactSkill - 200) * 0.005f;
                    totalFactor *= unstoppableForceFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "UnstoppableForce=" + unstoppableForceFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            else if (IsCrossbowSkill(skillId))
            {
                if (attackInformation.IsVictimAgentMount &&
                    TryHasExactPerk(attackerAgent, "CrossbowUnhorser", ref entryId))
                {
                    totalFactor *= 1.4f;
                    factorSummary = AppendFactorSummary(factorSummary, "Unhorser=1.4");
                }

                if (attackInformation.IsHeadShot &&
                    TryHasExactPerk(attackerAgent, "CrossbowSheriff", ref entryId))
                {
                    totalFactor *= 1.5f;
                    factorSummary = AppendFactorSummary(factorSummary, "Sheriff=1.5");
                }

                if (exactSkill > 200 &&
                    TryHasExactPerk(attackerAgent, "CrossbowMightyPull", ref entryId))
                {
                    float mightyPullFactor = 1f + (exactSkill - 200) * 0.005f;
                    totalFactor *= mightyPullFactor;
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "MightyPull=" + mightyPullFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }

                if (TryHasExactPerk(attackerAgent, "EngineeringTorsionEngines", ref entryId))
                {
                    additiveDamage += 3f;
                    factorSummary = AppendFactorSummary(factorSummary, "TorsionEngines=+3");
                }
            }

            if (weapon != null &&
                IsAgentMounted(attackerAgent) &&
                weapon.IsConsumable &&
                TryHasExactPerk(attackerAgent, "RidingHorseArcher", ref entryId))
            {
                totalFactor *= 1.1f;
                factorSummary = AppendFactorSummary(factorSummary, "HorseArcher=1.1");
            }

            if (weapon != null &&
                IsAgentMounted(attackerAgent) &&
                weapon.IsMeleeWeapon &&
                TryHasExactPerk(attackerAgent, "RidingMountedWarrior", ref entryId))
            {
                totalFactor *= 1.05f;
                factorSummary = AppendFactorSummary(factorSummary, "MountedWarrior=1.05");
            }

            if (weapon != null &&
                weapon.IsMeleeWeapon &&
                TryHasExactPerk(attackerAgent, "AthleticsPowerful", ref entryId))
            {
                totalFactor *= 1.04f;
                factorSummary = AppendFactorSummary(factorSummary, "Powerful=1.04");
            }

            if (collisionData.IsAlternativeAttack &&
                TryHasExactPerk(attackerAgent, "AthleticsStrongLegs", ref entryId))
            {
                totalFactor *= 2f;
                skillId = "Athletics";
                factorSummary = AppendFactorSummary(factorSummary, "StrongLegs=2");
            }

            if (collisionData.IsHorseCharge)
            {
                if (TryHasExactPerk(attackerAgent, "RidingFullSpeed", ref entryId))
                {
                    totalFactor *= 1.2f;
                    skillId = "Riding";
                    factorSummary = AppendFactorSummary(factorSummary, "FullSpeed=1.2");
                }

                if (TryResolveExactSkill(attackerAgent, DefaultSkills.Riding, ref entryId, out int exactRidingSkill) &&
                    exactRidingSkill > 250 &&
                    TryHasExactPerk(attackerAgent, "RidingTheWayOfTheSaddle", ref entryId))
                {
                    float chargeDamageBonus = (exactRidingSkill - 250) * 0.3f;
                    additiveDamage += chargeDamageBonus;
                    skillId = "Riding";
                    factorSummary = AppendFactorSummary(
                        factorSummary,
                        "TheWayOfTheSaddle=+" + chargeDamageBonus.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            if (Math.Abs(totalFactor - 1f) < 0.0001f &&
                Math.Abs(additiveDamage) < 0.0001f)
            {
                return false;
            }

            updatedDamage = MathF.Max(0f, baseDamage * totalFactor + additiveDamage);
            return Math.Abs(updatedDamage - baseDamage) > 0.0001f;
        }

        private static bool TryApplyGlobalCaptainDamageAmplifications(
            in AttackInformation attackInformation,
            in AttackCollisionData collisionData,
            float baseDamage,
            out float updatedDamage,
            out string entryId,
            out string factorSummary)
        {
            updatedDamage = baseDamage;
            entryId = null;
            factorSummary = "none";
            Agent attackerAgent = ResolveExactAttackerHumanAgent(attackInformation);
            if (attackerAgent == null || !TryResolveGlobalCaptainEntryId(attackerAgent, out entryId))
                return false;

            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            if (weapon != null && IsBallistaProjectileWeapon(attackInformation.AttackerWeapon))
                return false;

            string skillId = ResolveRelevantSkill(weapon)?.StringId ?? string.Empty;
            bool isMounted = IsAgentMounted(attackerAgent);
            bool isShieldHit = IsShieldHit(collisionData);
            bool victimIsMount = attackInformation.IsVictimAgentMount;
            Agent victimAgent = ResolveExactVictimHumanAgent(attackInformation);
            var accumulator = new CaptainPerkBonusAccumulator(baseDamage);
            List<string> appliedEffects = CoopDebugConfig.CombatModelDiagnostics
                ? new List<string>()
                : null;

            if (string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase))
            {
                TryAddTrackedCaptainEffect(entryId, "RogueryCarver", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(
                    entryId,
                    isMounted ? "OneHandedCavalry" : "OneHandedDeadlyPurpose",
                    accumulator,
                    appliedEffects);
            }
            else if (string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase))
            {
                if (isShieldHit)
                {
                    TryAddTrackedCaptainEffect(entryId, "TwoHandedWoodChopper", accumulator, appliedEffects);
                    TryAddTrackedCaptainEffect(entryId, "TwoHandedShieldBreaker", accumulator, appliedEffects);
                }
                if (victimIsMount)
                    TryAddTrackedCaptainEffect(entryId, "TwoHandedBeastSlayer", accumulator, appliedEffects);
                if (!isMounted)
                    TryAddTrackedCaptainEffect(entryId, "TwoHandedRecklessCharge", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "TwoHandedHeadBasher", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "RogueryDashAndSlash", accumulator, appliedEffects);
            }
            else if (string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase))
            {
                if (victimIsMount)
                    TryAddTrackedCaptainEffect(entryId, "PolearmSteadKiller", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "PolearmPhalanx", accumulator, appliedEffects);
                if (isMounted)
                    TryAddTrackedCaptainEffect(entryId, "PolearmCavalry", accumulator, appliedEffects);
                else
                {
                    TryAddTrackedCaptainEffect(entryId, "PolearmPikeman", accumulator, appliedEffects);
                    if (IsThrustCollision(collisionData))
                    {
                        TryAddTrackedCaptainEffect(entryId, "PolearmBraced", accumulator, appliedEffects);
                        TryAddTrackedCaptainEffect(entryId, "PolearmSharpenTheTip", accumulator, appliedEffects);
                    }
                }
            }
            else if (string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) && weapon?.IsConsumable == true)
            {
                TryAddTrackedCaptainEffect(entryId, "BowBowControl", accumulator, appliedEffects);
                if ((BattleSnapshotRuntimeState.GetEntryState(entryId)?.Tier ?? 0) >= 3)
                    TryAddTrackedCaptainEffect(entryId, "BowStrongBows", accumulator, appliedEffects);
            }
            else if (IsCrossbowSkill(skillId) && weapon?.IsConsumable == true)
            {
                if (victimIsMount)
                    TryAddTrackedCaptainEffect(entryId, "CrossbowUnhorser", accumulator, appliedEffects);
                if (victimAgent?.Character?.IsInfantry == true)
                    TryAddTrackedCaptainEffect(entryId, "CrossbowSheriff", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "CrossbowHammerBolts", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "EngineeringDreadfulSieger", accumulator, appliedEffects);
            }
            else if (string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase))
            {
                if (isShieldHit)
                {
                    TryAddTrackedCaptainEffect(entryId, "ThrowingShieldBreaker", accumulator, appliedEffects);
                    TryAddTrackedCaptainEffect(entryId, "ThrowingSplinters", accumulator, appliedEffects);
                }
                if (victimIsMount)
                {
                    TryAddTrackedCaptainEffect(entryId, "ThrowingHunter", accumulator, appliedEffects);
                    TryAddTrackedCaptainEffect(entryId, "ThrowingKnockOff", accumulator, appliedEffects);
                }
                if (isMounted)
                    TryAddTrackedCaptainEffect(entryId, "ThrowingMountedSkirmisher", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "ThrowingImpale", accumulator, appliedEffects);
            }

            if (weapon?.IsMeleeWeapon == true)
            {
                TryAddTrackedCaptainEffect(entryId, "AthleticsPowerful", accumulator, appliedEffects);
                TryAddTrackedCaptainEffect(entryId, "EngineeringImprovedTools", accumulator, appliedEffects);
                if (isMounted)
                    TryAddTrackedCaptainEffect(entryId, "RidingMountedWarrior", accumulator, appliedEffects);
            }

            if (weapon?.IsConsumable == true && isMounted)
                TryAddTrackedCaptainEffect(entryId, "RidingHorseArcher", accumulator, appliedEffects);
            if (collisionData.IsHorseCharge)
                TryAddTrackedCaptainEffect(entryId, "RidingFullSpeed", accumulator, appliedEffects);
            if (isShieldHit)
                TryAddTrackedCaptainEffect(entryId, "EngineeringWallBreaker", accumulator, appliedEffects);
            if (collisionData.EntityExists)
                TryAddTrackedCaptainEffect(entryId, "TwoHandedVandal", accumulator, appliedEffects);

            if (victimAgent?.Character != null)
            {
                TryAddTrackedCaptainEffect(entryId, "TacticsCoaching", accumulator, appliedEffects);
                if (victimAgent.Character.Culture?.IsBandit == true)
                    TryAddTrackedCaptainEffect(entryId, "TacticsLawKeeper", accumulator, appliedEffects);
                if (isMounted && victimAgent.Character.IsInfantry)
                    TryAddTrackedCaptainEffect(entryId, "TacticsGensdarmes", accumulator, appliedEffects);
            }
            if (attackerAgent.Character?.Culture?.IsBandit == true)
                TryAddTrackedCaptainEffect(entryId, "RogueryPartnersInCrime", accumulator, appliedEffects);

            if (!accumulator.HasEffects)
                return false;

            factorSummary = appliedEffects != null && appliedEffects.Count > 0
                ? string.Join(",", appliedEffects)
                : "none";
            updatedDamage = MathF.Max(0f, accumulator.Result);
            return Math.Abs(updatedDamage - baseDamage) > 0.0001f;
        }

        private static bool TryAddTrackedCaptainEffect(
            string entryId,
            string perkId,
            CaptainPerkBonusAccumulator accumulator,
            ICollection<string> appliedEffects)
        {
            if (!GlobalCaptainPerkRuntimeState.AddEffect(entryId, perkId, accumulator))
                return false;

            if (appliedEffects != null)
            {
                string effectSummary = perkId;
                if (GlobalCaptainPerkRuntimeState.TryGetEffect(entryId, perkId, out var effect))
                {
                    effectSummary +=
                        "=" + effect.Bonus.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                        "(" + (string.IsNullOrWhiteSpace(effect.IncrementType) ? "additive" : effect.IncrementType) + ")";
                }
                appliedEffects.Add(effectSummary);
            }

            return true;
        }

        private static bool TryResolveGlobalCaptainEntryId(Agent agent, out string entryId)
        {
            entryId = null;
            return agent != null &&
                CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                !string.IsNullOrWhiteSpace(entryId);
        }

        private static WeaponComponentData ResolveCurrentWeapon(Agent agent)
        {
            if (agent?.Equipment == null)
                return null;

            EquipmentIndex wieldedIndex = agent.GetPrimaryWieldedItemIndex();
            return wieldedIndex == EquipmentIndex.None
                ? null
                : agent.Equipment[wieldedIndex].CurrentUsageItem;
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

        private void TryLogDamageAmplificationSample(
            in AttackInformation attackInformation,
            in AttackCollisionData collisionData,
            string entryId,
            string skillId,
            float baseDamage,
            float personalDamage,
            float finalDamage,
            bool personalApplied,
            bool captainApplied,
            string personalFactorSummary,
            string captainFactorSummary)
        {
            if (!CoopDebugConfig.CombatModelDiagnostics)
                return;

            Agent attackerAgent = attackInformation.AttackerAgent;
            Agent victimAgent = attackInformation.VictimAgent;
            WeaponComponentData weapon = attackInformation.AttackerWeapon.CurrentUsageItem;
            string logKey =
                (attackerAgent?.Index ?? -1) + "|" +
                (victimAgent?.Index ?? -1) + "|" +
                (entryId ?? string.Empty) + "|" +
                (skillId ?? string.Empty) + "|" +
                (weapon?.WeaponClass.ToString() ?? "None") + "|" +
                collisionData.VictimHitBodyPart + "|" +
                collisionData.IsHorseCharge + "|" +
                Math.Round(baseDamage, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                Math.Round(personalDamage, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                Math.Round(finalDamage, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                (personalFactorSummary ?? string.Empty) + "|" +
                (captainFactorSummary ?? string.Empty);
            if (_loggedDamageAmplificationKeys.Count >= DamageAmplificationDiagnosticBudget ||
                !_loggedDamageAmplificationKeys.Add(logKey))
            {
                return;
            }

            ModLogger.Info(
                "CoopCampaignDerivedAgentApplyDamageModel: exact damage amplification applied. " +
                "Attacker=" + (attackerAgent?.Index ?? -1) +
                " Victim=" + (victimAgent?.Index ?? -1) +
                " EntryId=" + (string.IsNullOrWhiteSpace(entryId) ? "unknown" : entryId) +
                " Skill=" + (string.IsNullOrWhiteSpace(skillId) ? "null" : skillId) +
                " WeaponClass=" + (weapon?.WeaponClass.ToString() ?? "None") +
                " BaseDamage=" + baseDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " PersonalDamage=" + personalDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " FinalDamage=" + finalDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " PersonalApplied=" + personalApplied +
                " CaptainApplied=" + captainApplied +
                " PersonalFactors=" + (string.IsNullOrWhiteSpace(personalFactorSummary) ? "none" : personalFactorSummary) +
                " CaptainFactors=" + (string.IsNullOrWhiteSpace(captainFactorSummary) ? "none" : captainFactorSummary) +
                " HeadShot=" + attackInformation.IsHeadShot +
                " VictimHpRate=" + attackInformation.VictimHitPointRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " AttackerMounted=" + (attackInformation.DoesAttackerHaveMountAgent || attackerAgent?.HasMount == true || attackerAgent?.MountAgent != null) +
                " AttackerIsMount=" + attackInformation.IsAttackerAgentMount +
                " VictimIsMount=" + attackInformation.IsVictimAgentMount +
                " HorseCharge=" + collisionData.IsHorseCharge +
                " BodyPart=" + collisionData.VictimHitBodyPart +
                " Mission=" + (attackerAgent?.Mission?.SceneName ?? "null") + ".");
        }

        private static bool IsSupportedDamageSkill(string skillId)
        {
            return string.Equals(skillId, "OneHanded", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "TwoHanded", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Polearm", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(skillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                   IsCrossbowSkill(skillId) ||
                   string.Equals(skillId, "Throwing", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCrossbowSkill(string skillId)
        {
            return string.Equals(skillId, "Crossbow", StringComparison.OrdinalIgnoreCase);
        }

        private static Agent ResolveExactAttackerHumanAgent(in AttackInformation attackInformation)
        {
            Agent attackerAgent = attackInformation.AttackerAgent;
            return ResolveHumanAgent(attackerAgent);
        }

        private static Agent ResolveExactVictimHumanAgent(in AttackInformation attackInformation)
        {
            Agent victimAgent = attackInformation.VictimAgent;
            if (attackInformation.IsVictimAgentMount)
                return victimAgent?.RiderAgent;

            return ResolveHumanAgent(victimAgent);
        }

        private static Agent ResolveHumanAgent(Agent agent)
        {
            if (agent == null)
                return null;

            if (agent.IsMount)
                return agent.RiderAgent;

            return agent.IsHuman ? agent : null;
        }

        private static bool TryResolveExactSkill(Agent agent, SkillObject skill, ref string entryId, out int exactSkill)
        {
            exactSkill = 0;
            if (agent == null || skill == null)
                return false;

            if (!CoopMissionSpawnLogic.TryGetExactHeroCombatProfileSkillValue(agent, skill, out exactSkill, out string skillEntryId))
                return false;

            if (string.IsNullOrWhiteSpace(entryId))
                entryId = skillEntryId ?? string.Empty;

            return true;
        }

        private static bool TryHasExactPerk(Agent agent, string perkId, ref string entryId)
        {
            if (!CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(agent, perkId, out string perkEntryId))
                return false;

            if (string.IsNullOrWhiteSpace(entryId))
                entryId = perkEntryId ?? string.Empty;

            return true;
        }

        private static bool HasExactPersonalPerk(Agent agent, string perkId)
        {
            Agent exactAgent = ResolveHumanAgent(agent);
            return exactAgent != null &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(exactAgent, perkId, out _);
        }

        private static bool HasExactPersonalCrossbowHammerBolts(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            attackerAgent = ResolveHumanAgent(attackerAgent);
            return attackerAgent != null &&
                   attackerWeapon != null &&
                   attackerWeapon.IsConsumable &&
                   IsCrossbowSkill(ResolveRelevantSkill(attackerWeapon)?.StringId) &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "CrossbowHammerBolts", out _);
        }

        private static bool HasExactPersonalThrowingKnockOff(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            attackerAgent = ResolveHumanAgent(attackerAgent);
            return attackerAgent != null &&
                   attackerWeapon != null &&
                   attackerWeapon.IsConsumable &&
                   string.Equals(ResolveRelevantSkill(attackerWeapon)?.StringId, "Throwing", StringComparison.OrdinalIgnoreCase) &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "ThrowingKnockOff", out _);
        }

        private static bool HasExactPersonalPolearmBraced(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            attackerAgent = ResolveHumanAgent(attackerAgent);
            return attackerAgent != null &&
                   attackerWeapon != null &&
                   string.Equals(ResolveRelevantSkill(attackerWeapon)?.StringId, "Polearm", StringComparison.OrdinalIgnoreCase) &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "PolearmBraced", out _);
        }

        private static bool HasExactPersonalPolearmKeepAtBay(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            attackerAgent = ResolveHumanAgent(attackerAgent);
            return attackerAgent != null &&
                   attackerWeapon != null &&
                   string.Equals(ResolveRelevantSkill(attackerWeapon)?.StringId, "Polearm", StringComparison.OrdinalIgnoreCase) &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "PolearmKeepAtBay", out _);
        }

        private static bool HasExactPersonalPolearmHardKnock(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            attackerAgent = ResolveHumanAgent(attackerAgent);
            return attackerAgent != null &&
                   attackerWeapon != null &&
                   string.Equals(ResolveRelevantSkill(attackerWeapon)?.StringId, "Polearm", StringComparison.OrdinalIgnoreCase) &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "PolearmHardKnock", out _);
        }

        private static bool HasExactPersonalTwoHandedShowOfStrength(Agent attackerAgent, WeaponComponentData attackerWeapon)
        {
            attackerAgent = ResolveHumanAgent(attackerAgent);
            return attackerAgent != null &&
                   attackerWeapon != null &&
                   string.Equals(ResolveRelevantSkill(attackerWeapon)?.StringId, "TwoHanded", StringComparison.OrdinalIgnoreCase) &&
                   CoopMissionSpawnLogic.HasExactHeroCombatProfilePerk(attackerAgent, "TwoHandedShowOfStrength", out _);
        }

        private static bool IsShieldHit(in AttackCollisionData collisionData)
        {
            return collisionData.AttackBlockedWithShield ||
                   collisionData.CollidedWithShieldOnBack;
        }

        private static bool IsThrustCollision(in AttackCollisionData collisionData)
        {
            return collisionData.StrikeType == (int)StrikeType.Thrust;
        }

        private static bool IsSwingCollision(in AttackCollisionData collisionData)
        {
            return collisionData.StrikeType == (int)StrikeType.Swing;
        }

        private static bool IsAgentMounted(Agent agent)
        {
            return agent != null &&
                   (agent.HasMount || agent.MountAgent != null);
        }

        private static bool IsOffHandEmpty(Agent agent)
        {
            return agent == null ||
                   agent.GetOffhandWieldedItemIndex() == EquipmentIndex.None;
        }

        private static bool IsOneHandedAxeOrMace(WeaponComponentData weapon)
        {
            return weapon != null &&
                   (weapon.WeaponClass == WeaponClass.OneHandedAxe ||
                    weapon.WeaponClass == WeaponClass.Mace);
        }

        private static bool IsTwoHandedAxeOrMace(WeaponComponentData weapon)
        {
            return weapon != null &&
                   (weapon.WeaponClass == WeaponClass.TwoHandedAxe ||
                    weapon.WeaponClass == WeaponClass.TwoHandedMace);
        }

        private static float ResolveHealthRate(Agent agent)
        {
            if (agent == null || agent.HealthLimit <= 0f)
                return 0f;

            return MBMath.ClampFloat(agent.Health / agent.HealthLimit, 0f, 1f);
        }

        private static bool IsDismountableBodyPart(BoneBodyPartType bodyPart)
        {
            int bodyPartValue = (int)bodyPart;
            return bodyPartValue >= 0 && bodyPartValue <= 6;
        }

        private static bool IsBallistaProjectileWeapon(MissionWeapon weapon)
        {
            return IsBallistaProjectileItemId(weapon.Item?.StringId);
        }

        private static bool IsBallistaProjectileItemId(string itemId)
        {
            return string.Equals(itemId, "ballista_projectile", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(itemId, "ballista_projectile_burning", StringComparison.OrdinalIgnoreCase);
        }

        private static SkillObject ResolveRelevantSkill(WeaponComponentData weapon)
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
