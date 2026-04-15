using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    /// <summary>
    /// Diagnostics-only mission logic for campaign battles.
    /// Logs ranged hit and kill telemetry for tracked player-party heroes in a format
    /// comparable to CoopBattle logs.
    /// </summary>
    public sealed class CampaignBattleDamageDiagnosticsMissionLogic : MissionLogic
    {
        private readonly HashSet<string> _loggedActivationKeys = new HashSet<string>(StringComparer.Ordinal);

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();

            try
            {
                Mission mission = Mission;
                ModLogger.Info(
                    "CampaignBattleDamageDiagnosticsMissionLogic: attached. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " Mode=" + (mission?.Mode.ToString() ?? "null") +
                    " MainHero=" + (Hero.MainHero?.StringId ?? "null") +
                    " TrackedHeroes=" + BuildTrackedHeroSummary() + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignBattleDamageDiagnosticsMissionLogic: attach logging failed. " + ex.Message);
            }
        }

        public override void OnScoreHit(
            Agent affectedAgent,
            Agent affectorAgent,
            WeaponComponentData attackerWeapon,
            bool isBlocked,
            bool isSiegeEngineHit,
            in Blow blow,
            in AttackCollisionData collisionData,
            float damagedHp,
            float hitDistance,
            float shotDifficulty)
        {
            try
            {
                if (!TryResolveTrackedPlayerPartyHeroAttack(affectorAgent, attackerWeapon, out Hero trackedHero, out CharacterObject trackedCharacter, out string heroRole))
                    return;

                string skillId = ResolveRelevantSkillId(attackerWeapon);
                if (!IsTrackedRangedSkill(skillId))
                    return;

                LogActivationOnce(affectorAgent, trackedHero, trackedCharacter, heroRole);

                ModLogger.Info(
                    "CampaignBattleDamageDiagnosticsMissionLogic: tracked hero ranged hit sample. " +
                    "HeroId=" + (trackedHero?.StringId ?? trackedCharacter?.StringId ?? "null") +
                    " HeroName=" + (trackedHero?.Name?.ToString() ?? trackedCharacter?.Name?.ToString() ?? "null") +
                    " HeroRole=" + heroRole +
                    " PlayerControlled=" + (affectorAgent?.IsMainAgent ?? false) +
                    "Skill=" + skillId +
                    " WeaponClass=" + attackerWeapon.WeaponClass +
                    " Victim=" + (affectedAgent?.Character?.StringId ?? "null") +
                    " VictimHero=" + ((affectedAgent?.Character as CharacterObject)?.HeroObject != null) +
                    " VictimBodyPart=" + blow.VictimBodyPart +
                    " CollisionBodyPart=" + collisionData.VictimHitBodyPart +
                    " HeadShot=" + blow.IsHeadShot() +
                    " Blocked=" + isBlocked +
                    " DamageType=" + blow.DamageType +
                    " BlowDamage=" + blow.InflictedDamage +
                    " BlowBaseMagnitude=" + blow.BaseMagnitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " BlowAbsorbedByArmor=" + blow.AbsorbedByArmor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " BlowMovementSpeedModifier=" + blow.MovementSpeedDamageModifier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " DamagedHp=" + damagedHp.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " CollisionBaseMagnitude=" + collisionData.BaseMagnitude.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " MissileTotalDamage=" + collisionData.MissileTotalDamage.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " MissileStartSpeed=" + collisionData.MissileStartingBaseSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " MissileVelocity=" + collisionData.MissileVelocity.Length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " MovementSpeedDamageModifier=" + collisionData.MovementSpeedDamageModifier.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " HitDistance=" + hitDistance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " ShotDifficulty=" + shotDifficulty.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " Mission=" + (Mission?.SceneName ?? "null") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignBattleDamageDiagnosticsMissionLogic: OnScoreHit diagnostics failed. " + ex.Message);
            }
            finally
            {
                base.OnScoreHit(
                    affectedAgent,
                    affectorAgent,
                    attackerWeapon,
                    isBlocked,
                    isSiegeEngineHit,
                    blow,
                    collisionData,
                    damagedHp,
                    hitDistance,
                    shotDifficulty);
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            try
            {
                if (!killingBlow.IsValid || affectorAgent == null)
                    return;

                WeaponComponentData weapon = affectorAgent.WieldedWeapon.CurrentUsageItem;
                if (!TryResolveTrackedPlayerPartyHeroAttack(affectorAgent, weapon, out Hero trackedHero, out CharacterObject trackedCharacter, out string heroRole))
                    return;

                string skillId = ResolveRelevantSkillId(weapon);
                if (!IsTrackedRangedSkill(skillId))
                    return;

                LogActivationOnce(affectorAgent, trackedHero, trackedCharacter, heroRole);

                ModLogger.Info(
                    "CampaignBattleDamageDiagnosticsMissionLogic: tracked hero ranged removal sample. " +
                    "HeroId=" + (trackedHero?.StringId ?? trackedCharacter?.StringId ?? "null") +
                    " HeroName=" + (trackedHero?.Name?.ToString() ?? trackedCharacter?.Name?.ToString() ?? "null") +
                    " HeroRole=" + heroRole +
                    " PlayerControlled=" + (affectorAgent?.IsMainAgent ?? false) +
                    "Skill=" + skillId +
                    " WeaponClass=" + killingBlow.WeaponClass +
                    " Victim=" + (affectedAgent?.Character?.StringId ?? "null") +
                    " VictimHero=" + ((affectedAgent?.Character as CharacterObject)?.HeroObject != null) +
                    " RemovedState=" + agentState +
                    " DamageType=" + killingBlow.DamageType +
                    " InflictedDamage=" + killingBlow.InflictedDamage +
                    " VictimBodyPart=" + killingBlow.VictimBodyPart +
                    " HeadShot=" + killingBlow.IsHeadShot() +
                    " IsMissile=" + killingBlow.IsMissile +
                    " Mission=" + (Mission?.SceneName ?? "null") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignBattleDamageDiagnosticsMissionLogic: OnAgentRemoved diagnostics failed. " + ex.Message);
            }
            finally
            {
                base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, killingBlow);
            }
        }

        private void LogActivationOnce(Agent agent, Hero trackedHero, CharacterObject trackedCharacter, string heroRole)
        {
            Mission mission = agent?.Mission ?? Mission;
            string scene = mission?.SceneName ?? "null";
            string heroKey = trackedHero?.StringId ?? trackedCharacter?.StringId ?? "unknown";
            if (!_loggedActivationKeys.Add(scene + "|" + heroKey))
                return;

            ModLogger.Info(
                "CampaignBattleDamageDiagnosticsMissionLogic: activated for campaign battle. " +
                "Scene=" + scene +
                " HeroId=" + heroKey +
                " HeroName=" + (trackedHero?.Name?.ToString() ?? trackedCharacter?.Name?.ToString() ?? "null") +
                " HeroRole=" + heroRole +
                " PlayerControlled=" + (agent?.IsMainAgent ?? false) +
                " Mounted=" + (agent?.HasMount ?? false) +
                " Skills=" + BuildTrackedHeroSkillSummary(trackedCharacter) + ".");
        }

        private static bool TryResolveTrackedPlayerPartyHeroAttack(
            Agent affectorAgent,
            WeaponComponentData attackerWeapon,
            out Hero trackedHero,
            out CharacterObject trackedCharacter,
            out string heroRole)
        {
            trackedHero = null;
            trackedCharacter = null;
            heroRole = "none";

            if (affectorAgent == null || attackerWeapon == null)
                return false;

            trackedCharacter = affectorAgent.Character as CharacterObject;
            trackedHero = trackedCharacter?.HeroObject;
            if (trackedCharacter == null || trackedHero == null)
                return false;

            bool isMainHero = trackedHero == Hero.MainHero ||
                              string.Equals(trackedHero.StringId, Hero.MainHero?.StringId, StringComparison.OrdinalIgnoreCase);
            bool isMainPartyHero =
                trackedHero.PartyBelongedTo == MobileParty.MainParty ||
                trackedHero.PartyBelongedTo?.Party == PartyBase.MainParty;

            if (!isMainHero && !isMainPartyHero)
                return false;

            heroRole = isMainHero ? "player" : "companion";
            string skillId = ResolveRelevantSkillId(attackerWeapon);
            return IsTrackedRangedSkill(skillId);
        }

        private static bool IsTrackedRangedSkill(string skillId)
        {
            return string.Equals(skillId, DefaultSkills.Bow?.StringId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(skillId, DefaultSkills.Crossbow?.StringId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(skillId, DefaultSkills.Throwing?.StringId, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveRelevantSkillId(WeaponComponentData attackerWeapon)
        {
            if (attackerWeapon == null)
                return null;

            SkillObject relevantSkill = attackerWeapon.RelevantSkill;
            if (relevantSkill != null)
                return relevantSkill.StringId;

            switch (attackerWeapon.WeaponClass)
            {
                case WeaponClass.Arrow:
                case WeaponClass.Bow:
                    return DefaultSkills.Bow?.StringId;
                case WeaponClass.Bolt:
                case WeaponClass.Crossbow:
                    return DefaultSkills.Crossbow?.StringId;
                case WeaponClass.Javelin:
                case WeaponClass.ThrowingAxe:
                case WeaponClass.ThrowingKnife:
                case WeaponClass.Stone:
                    return DefaultSkills.Throwing?.StringId;
                default:
                    return null;
            }
        }

        private static string BuildTrackedHeroSummary()
        {
            if (MobileParty.MainParty?.MemberRoster == null)
                return "missing-main-party";

            List<string> trackedHeroes = new List<string>();
            foreach (TroopRosterElement rosterElement in MobileParty.MainParty.MemberRoster.GetTroopRoster())
            {
                CharacterObject character = rosterElement.Character;
                Hero hero = character?.HeroObject;
                if (hero == null)
                    continue;

                string role = hero == Hero.MainHero ? "player" : "companion";
                trackedHeroes.Add(
                    (hero.StringId ?? character.StringId ?? "null") +
                    "/" + role +
                    "/Bow=" + character.GetSkillValue(DefaultSkills.Bow) +
                    "/Crossbow=" + character.GetSkillValue(DefaultSkills.Crossbow) +
                    "/Throwing=" + character.GetSkillValue(DefaultSkills.Throwing) +
                    "/Riding=" + character.GetSkillValue(DefaultSkills.Riding));
            }

            if (trackedHeroes.Count == 0)
                return "no-tracked-heroes";

            return string.Join("; ", trackedHeroes);
        }

        private static string BuildTrackedHeroSkillSummary(CharacterObject trackedCharacter)
        {
            if (trackedCharacter == null)
                return "missing-tracked-hero";

            return
                "Bow=" + trackedCharacter.GetSkillValue(DefaultSkills.Bow) +
                "/Crossbow=" + trackedCharacter.GetSkillValue(DefaultSkills.Crossbow) +
                "/Throwing=" + trackedCharacter.GetSkillValue(DefaultSkills.Throwing) +
                "/Riding=" + trackedCharacter.GetSkillValue(DefaultSkills.Riding);
        }
    }
}
