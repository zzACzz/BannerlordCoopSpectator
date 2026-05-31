using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly Dictionary<string, string> _lastLoggedRangedStateByTrackKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _sampledBattlefieldRangedAgentIndexBySkill =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private float _nextRangedStateProbeMissionTime;

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

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            try
            {
                ObserveTrackedRangedStateProofSamples();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignBattleDamageDiagnosticsMissionLogic: ranged state proof probe failed. " + ex.Message);
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

        private void ObserveTrackedRangedStateProofSamples()
        {
            Mission mission = Mission;
            if (mission == null)
                return;

            float currentMissionTime = mission.CurrentTime;
            if (currentMissionTime < _nextRangedStateProbeMissionTime)
                return;

            _nextRangedStateProbeMissionTime = currentMissionTime + 0.25f;

            foreach (Agent agent in mission.Agents)
            {
                if (!TryResolveTrackedPlayerPartyHeroAgent(agent, out Hero trackedHero, out CharacterObject trackedCharacter, out string heroRole))
                    continue;

                string heroId = trackedHero?.StringId ?? trackedCharacter?.StringId ?? ("agent-" + agent.Index);
                LogAgentRangedStateIfChanged(
                    agent,
                    "tracked-hero:" + heroId,
                    trackedCharacter,
                    heroRole,
                    currentMissionTime);
            }

            ObserveSampledBattlefieldRangedState(DefaultSkills.Bow?.StringId, currentMissionTime);
            ObserveSampledBattlefieldRangedState(DefaultSkills.Crossbow?.StringId, currentMissionTime);
            ObserveSampledBattlefieldRangedState(DefaultSkills.Throwing?.StringId, currentMissionTime);
        }

        private void ObserveSampledBattlefieldRangedState(string rangedSkillId, float currentMissionTime)
        {
            if (string.IsNullOrWhiteSpace(rangedSkillId))
                return;

            Mission mission = Mission;
            if (mission == null)
                return;

            Agent sampledAgent = null;
            if (_sampledBattlefieldRangedAgentIndexBySkill.TryGetValue(rangedSkillId, out int sampledAgentIndex))
                sampledAgent = Mission.MissionNetworkHelper.GetAgentFromIndex(sampledAgentIndex, canBeNull: true);

            if (sampledAgent == null ||
                !sampledAgent.IsActive() ||
                sampledAgent.IsMount ||
                !TryResolveTrackedRangedSkillFromAgentLoadout(sampledAgent, out string sampledSkillId) ||
                !string.Equals(sampledSkillId, rangedSkillId, StringComparison.OrdinalIgnoreCase))
            {
                sampledAgent = FindSampledBattlefieldRangedAgent(rangedSkillId);
                if (sampledAgent != null)
                    _sampledBattlefieldRangedAgentIndexBySkill[rangedSkillId] = sampledAgent.Index;
            }

            if (sampledAgent == null)
                return;

            string characterId = sampledAgent.Character?.StringId ?? "null";
            string side = sampledAgent.Team?.Side.ToString() ?? "None";
            LogAgentRangedStateIfChanged(
                sampledAgent,
                "sample-ranged:" + rangedSkillId + ":" + side + ":" + characterId,
                sampledAgent.Character as CharacterObject,
                "sample-" + rangedSkillId,
                currentMissionTime);
        }

        private Agent FindSampledBattlefieldRangedAgent(string rangedSkillId)
        {
            Mission mission = Mission;
            if (mission == null)
                return null;

            BattleSideEnum mainSide = Agent.Main?.Team?.Side ?? BattleSideEnum.None;
            Agent fallbackCandidate = null;

            foreach (Agent agent in mission.Agents)
            {
                if (agent == null || !agent.IsActive() || agent.IsMount)
                    continue;

                if (TryResolveTrackedPlayerPartyHeroAgent(agent, out _, out _, out _))
                    continue;

                if (!TryResolveTrackedRangedSkillFromAgentLoadout(agent, out string candidateSkillId) ||
                    !string.Equals(candidateSkillId, rangedSkillId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (agent.Team?.Side != mainSide)
                    return agent;

                if (fallbackCandidate == null)
                    fallbackCandidate = agent;
            }

            return fallbackCandidate;
        }

        private void LogAgentRangedStateIfChanged(
            Agent agent,
            string trackKey,
            CharacterObject trackedCharacter,
            string trackRole,
            float currentMissionTime)
        {
            if (agent == null || string.IsNullOrWhiteSpace(trackKey))
                return;

            string stateSnapshot = BuildAgentRangedStateSnapshot(
                agent,
                trackKey,
                trackedCharacter,
                trackRole,
                currentMissionTime);

            if (_lastLoggedRangedStateByTrackKey.TryGetValue(trackKey, out string previousSnapshot) &&
                string.Equals(previousSnapshot, stateSnapshot, StringComparison.Ordinal))
            {
                return;
            }

            _lastLoggedRangedStateByTrackKey[trackKey] = stateSnapshot;
            ModLogger.Info(
                "CampaignBattleDamageDiagnosticsMissionLogic: ranged state proof sample. " +
                stateSnapshot);
        }

        private static string BuildAgentRangedStateSnapshot(
            Agent agent,
            string trackKey,
            CharacterObject trackedCharacter,
            string trackRole,
            float currentMissionTime)
        {
            string trackedCharacterId = trackedCharacter?.StringId ?? agent.Character?.StringId ?? "null";
            string trackedCharacterName = trackedCharacter?.Name?.ToString() ?? agent.Character?.Name?.ToString() ?? "null";
            string trackedSkillId = TryResolveTrackedRangedSkillFromAgentLoadout(agent, out string rangedSkillId)
                ? rangedSkillId
                : "none";
            MissionWeapon wieldedWeapon = agent.WieldedWeapon;
            MissionWeapon offhandWeapon = agent.WieldedOffhandWeapon;
            MissionWeapon ammoWeapon = wieldedWeapon.AmmoWeapon;

            return
                "TrackKey=" + trackKey +
                " MissionTime=" + currentMissionTime.ToString("0.000", CultureInfo.InvariantCulture) +
                " Role=" + (trackRole ?? "unknown") +
                " CharacterId=" + trackedCharacterId +
                " CharacterName=" + trackedCharacterName +
                " AgentIndex=" + agent.Index +
                " TeamSide=" + (agent.Team?.Side.ToString() ?? "None") +
                " State=" + agent.State +
                " Controller=" + agent.Controller +
                " IsPlayerControlled=" + agent.IsPlayerControlled +
                " IsAIControlled=" + agent.IsAIControlled +
                " HasMount=" + agent.HasMount +
                " MovementFlags=" + agent.MovementFlags +
                " ActionStage=" + agent.GetCurrentActionStage(0) +
                " TrackedRangedSkill=" + trackedSkillId +
                " PrimaryWieldedIndex=" + agent.GetPrimaryWieldedItemIndex() +
                " WieldedItem=" + (wieldedWeapon.Item?.StringId ?? "none") +
                " WieldedUsage=" + (wieldedWeapon.CurrentUsageItem?.ItemUsage ?? "null") +
                " WieldedUsageClass=" + (wieldedWeapon.CurrentUsageItem?.WeaponClass.ToString() ?? "null") +
                " OffhandItem=" + (offhandWeapon.Item?.StringId ?? "none") +
                " AmmoItem=" + (ammoWeapon.Item?.StringId ?? "none") +
                " AmmoAmount=" + ammoWeapon.Amount +
                " WieldedAmount=" + wieldedWeapon.Amount +
                " WeaponSlots={" + BuildMissionWeaponSlotSummary(agent) + "}";
        }

        private static string BuildMissionWeaponSlotSummary(Agent agent)
        {
            if (agent == null)
                return "missing-agent";

            List<string> parts = new List<string>();
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                try
                {
                    MissionWeapon slotWeapon = agent.Equipment[slot];
                    if (slotWeapon.IsEmpty || slotWeapon.Item == null)
                        continue;

                    WeaponComponentData currentUsageItem = slotWeapon.CurrentUsageItem;
                    parts.Add(
                        slot +
                        "=" + slotWeapon.Item.StringId +
                        "|Amount=" + slotWeapon.Amount +
                        "|UsageClass=" + (currentUsageItem?.WeaponClass.ToString() ?? "null") +
                        "|RelevantSkill=" + (currentUsageItem?.RelevantSkill?.StringId ?? "null"));
                }
                catch (Exception ex)
                {
                    parts.Add(slot + "=summary-failed:" + ex.GetType().Name);
                }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "empty";
        }

        private static bool TryResolveTrackedPlayerPartyHeroAgent(
            Agent agent,
            out Hero trackedHero,
            out CharacterObject trackedCharacter,
            out string heroRole)
        {
            trackedHero = null;
            trackedCharacter = null;
            heroRole = "none";

            if (agent == null)
                return false;

            trackedCharacter = agent.Character as CharacterObject;
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
            return true;
        }

        private static bool TryResolveTrackedRangedSkillFromAgentLoadout(Agent agent, out string rangedSkillId)
        {
            rangedSkillId = null;
            if (agent == null)
                return false;

            try
            {
                WeaponComponentData wieldedUsageItem = agent.WieldedWeapon.CurrentUsageItem;
                rangedSkillId = ResolveRelevantSkillId(wieldedUsageItem);
                if (IsTrackedRangedSkill(rangedSkillId))
                    return true;

                for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
                {
                    MissionWeapon slotWeapon = agent.Equipment[slot];
                    rangedSkillId = ResolveRelevantSkillId(slotWeapon.CurrentUsageItem);
                    if (IsTrackedRangedSkill(rangedSkillId))
                        return true;
                }
            }
            catch
            {
            }

            rangedSkillId = null;
            return false;
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
