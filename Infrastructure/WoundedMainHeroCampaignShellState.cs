using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class WoundedMainHeroCampaignShellState
    {
        private const string HarmonyOwner = "com.coopspectator.mod";
        private const string PatchTypePrefix =
            "CoopSpectator.Patches.WoundedMainHeroCampaignShellPatches+";

        private static readonly object Sync = new object();

        private static bool _openingMission;
        private static Mission _boundMission;
        private static Hero _mainHero;
        private static int _originalHitPoints;
        private static string _missionName;
        private static bool _patchCoverageConfirmed;
        private static bool _patchCoverageFailureLogged;

        internal static bool ShouldTreatMainHeroAsWoundedForEncounterAttack(Hero hero)
        {
            if (hero == null)
                return true;

            if (!hero.IsWounded)
                return false;

            if (!ReferenceEquals(hero, Hero.MainHero))
                return true;

            return !CanVirtualizeCurrentEncounter(out _);
        }

        internal static void BeginMissionOpen(string missionName)
        {
            if (!IsSupportedNativeBattleMissionName(missionName) ||
                !CanVirtualizeCurrentEncounter(out string eligibilitySummary))
            {
                return;
            }

            lock (Sync)
            {
                ClearLocked(restoreOriginalHitPoints: false, "new eligible mission open");

                _openingMission = true;
                _boundMission = null;
                _mainHero = Hero.MainHero;
                _originalHitPoints = Math.Max(1, _mainHero?.HitPoints ?? 1);
                _missionName = missionName ?? string.Empty;
            }

            ModLogger.Info(
                "WoundedMainHeroCampaignShellState: armed virtual healthy main-hero shell. " +
                "MissionName=" + (missionName ?? "null") +
                " OriginalHitPoints=" + _originalHitPoints +
                " " + eligibilitySummary + ".");
        }

        internal static void BindOpenedMission(Mission mission)
        {
            bool bound = false;
            lock (Sync)
            {
                if (!_openingMission)
                    return;

                if (mission == null)
                {
                    ClearLocked(restoreOriginalHitPoints: true, "MissionState.OpenNew returned null");
                    return;
                }

                _openingMission = false;
                _boundMission = mission;
                bound = true;
            }

            if (bound)
            {
                ModLogger.Info(
                    "WoundedMainHeroCampaignShellState: bound virtual healthy main-hero shell. " +
                    "MissionName=" + (_missionName ?? "null") +
                    " Scene=" + (mission.SceneName ?? "null") + ".");
            }
        }

        internal static void AbortMissionOpen(string source)
        {
            lock (Sync)
            {
                if (!_openingMission || _boundMission != null)
                    return;

                ClearLocked(restoreOriginalHitPoints: true, source ?? "mission open aborted");
            }
        }

        internal static void CompleteMission(Mission mission, string source)
        {
            lock (Sync)
            {
                if (_boundMission == null || !ReferenceEquals(_boundMission, mission))
                    return;

                ClearLocked(restoreOriginalHitPoints: true, source ?? "mission completed");
            }
        }

        internal static bool ShouldForceMainHeroTroopEligible(
            FlattenedTroopRosterElement troopRoster,
            bool includePlayer)
        {
            if (!includePlayer || troopRoster.Troop == null || !troopRoster.Troop.IsPlayerCharacter)
                return false;

            lock (Sync)
            {
                return (_openingMission || _boundMission != null) &&
                       _mainHero != null &&
                       ReferenceEquals(troopRoster.Troop, _mainHero.CharacterObject);
            }
        }

        internal static bool ShouldSuppressVirtualMainHeroWriteback(PartyGroupAgentOrigin origin)
        {
            if (origin == null)
                return false;

            Hero mainHero;
            lock (Sync)
            {
                if (_boundMission == null || _mainHero == null)
                    return false;

                mainHero = _mainHero;
            }

            try
            {
                return ReferenceEquals(origin.Troop, mainHero.CharacterObject);
            }
            catch
            {
                return false;
            }
        }

        private static bool CanVirtualizeCurrentEncounter(out string summary)
        {
            summary = "Eligibility=unknown";

            if (!AreRequiredPatchesInstalled())
            {
                summary = "Eligibility=required-patch-coverage-missing";
                return false;
            }

            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            Hero mainHero = Hero.MainHero;
            MobileParty mainParty = MobileParty.MainParty;
            if (campaign == null || mainHero == null || mainParty?.MemberRoster == null)
            {
                summary = "Eligibility=campaign-state-missing";
                return false;
            }

            if (!mainHero.IsWounded)
            {
                summary = "Eligibility=main-hero-healthy";
                return false;
            }

            int healthyMainPartyCount = Math.Max(0, mainParty.MemberRoster.TotalHealthyCount);
            if (healthyMainPartyCount <= 0)
            {
                summary = "Eligibility=no-healthy-main-party-troops";
                return false;
            }

            MapEvent battle =
                PlayerEncounter.Battle ??
                PlayerEncounter.EncounteredBattle ??
                mainParty.MapEvent;
            if (battle == null || PlayerEncounter.Current == null)
            {
                summary = "Eligibility=player-encounter-missing";
                return false;
            }

            Settlement settlement =
                PlayerEncounter.EncounterSettlement ??
                battle.MapEventSettlement ??
                mainParty.CurrentSettlement;
            if (battle.IsHideoutBattle || settlement?.IsHideout == true)
            {
                summary = "Eligibility=hideout-unsupported";
                return false;
            }

            if (battle.IsBlockade || battle.IsBlockadeSallyOut)
            {
                summary = "Eligibility=blockade-unsupported";
                return false;
            }

            if (battle.IsSiegeAmbush)
            {
                summary = "Eligibility=siege-ambush-priority-roster-unsupported";
                return false;
            }

            if (battle.IsSiegeAssault &&
                settlement?.CurrentSiegeState == Settlement.SiegeState.InTheLordsHall)
            {
                summary = "Eligibility=lords-hall-priority-roster-unsupported";
                return false;
            }

            bool isNavalEncounter;
            try
            {
                isNavalEncounter = battle.IsNavalMapEvent || PlayerEncounter.IsNavalEncounter();
            }
            catch
            {
                isNavalEncounter = true;
            }

            if (isNavalEncounter)
            {
                summary = "Eligibility=naval-unsupported";
                return false;
            }

            summary =
                "Eligibility=eligible" +
                " HealthyMainPartyCount=" + healthyMainPartyCount +
                " BattleType=" + battle.EventType;
            return true;
        }

        private static bool IsSupportedNativeBattleMissionName(string missionName)
        {
            return string.Equals(missionName, "Battle", StringComparison.Ordinal) ||
                   string.Equals(missionName, "SiegeMissionWithDeployment", StringComparison.Ordinal) ||
                   string.Equals(missionName, "SiegeMissionNoDeployment", StringComparison.Ordinal);
        }

        private static bool AreRequiredPatchesInstalled()
        {
            if (_patchCoverageConfirmed)
                return true;

            try
            {
                MethodInfo encounterAttackCondition = AccessTools.Method(
                    typeof(MenuHelper),
                    nameof(MenuHelper.EncounterAttackCondition),
                    new[] { typeof(MenuCallbackArgs) });
                MethodInfo missionOpen = AccessTools.Method(
                    typeof(MissionState),
                    nameof(MissionState.OpenNew),
                    new[]
                    {
                        typeof(string),
                        typeof(MissionInitializerRecord),
                        typeof(InitializeMissionBehaviorsDelegate),
                        typeof(bool),
                        typeof(bool)
                    });
                MethodInfo troopEligibility = AccessTools.Method(
                    typeof(DefaultTroopSupplierProbabilityModel),
                    "CanTroopJoinBattle",
                    new[] { typeof(FlattenedTroopRosterElement), typeof(bool) });
                MethodInfo setWounded = AccessTools.Method(typeof(PartyGroupAgentOrigin), nameof(PartyGroupAgentOrigin.SetWounded));
                MethodInfo setKilled = AccessTools.Method(typeof(PartyGroupAgentOrigin), nameof(PartyGroupAgentOrigin.SetKilled));
                MethodInfo onAgentRemoved = AccessTools.Method(
                    typeof(PartyGroupAgentOrigin),
                    nameof(PartyGroupAgentOrigin.OnAgentRemoved),
                    new[] { typeof(float) });
                MethodInfo missionEndInternal = AccessTools.Method(typeof(Mission), "EndMissionInternal");

                bool complete =
                    HasPatch(encounterAttackCondition, PatchCollectionKind.Transpiler, "EncounterAttackConditionPatch", "Transpiler") &&
                    HasPatch(missionOpen, PatchCollectionKind.Prefix, "MissionOpenPatch", "Prefix") &&
                    HasPatch(missionOpen, PatchCollectionKind.Postfix, "MissionOpenPatch", "Postfix") &&
                    HasPatch(missionOpen, PatchCollectionKind.Finalizer, "MissionOpenPatch", "Finalizer") &&
                    HasPatch(troopEligibility, PatchCollectionKind.Prefix, "TroopEligibilityPatch", "Prefix") &&
                    HasPatch(setWounded, PatchCollectionKind.Prefix, "OriginWritebackPatch", "Prefix") &&
                    HasPatch(setKilled, PatchCollectionKind.Prefix, "OriginWritebackPatch", "Prefix") &&
                    HasPatch(onAgentRemoved, PatchCollectionKind.Prefix, "OriginWritebackPatch", "Prefix") &&
                    HasPatch(missionEndInternal, PatchCollectionKind.Postfix, "MissionEndPatch", "Postfix");

                if (complete)
                {
                    _patchCoverageConfirmed = true;
                    ModLogger.Info(
                        "WoundedMainHeroCampaignShellState: required Harmony patch coverage confirmed.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (!_patchCoverageFailureLogged)
                {
                    _patchCoverageFailureLogged = true;
                    ModLogger.Info(
                        "WoundedMainHeroCampaignShellState: patch coverage verification failed. " +
                        ex.Message);
                }

                return false;
            }

            if (!_patchCoverageFailureLogged)
            {
                _patchCoverageFailureLogged = true;
                ModLogger.Info(
                    "WoundedMainHeroCampaignShellState: required patch coverage is incomplete; " +
                    "the vanilla wounded gate remains active.");
            }

            return false;
        }

        private static bool HasPatch(
            MethodBase target,
            PatchCollectionKind collectionKind,
            string nestedPatchTypeName,
            string patchMethodName)
        {
            if (target == null)
                return false;

            HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(target);
            if (patchInfo == null)
                return false;

            IEnumerable<Patch> patches;
            switch (collectionKind)
            {
                case PatchCollectionKind.Prefix:
                    patches = patchInfo.Prefixes;
                    break;
                case PatchCollectionKind.Postfix:
                    patches = patchInfo.Postfixes;
                    break;
                case PatchCollectionKind.Transpiler:
                    patches = patchInfo.Transpilers;
                    break;
                case PatchCollectionKind.Finalizer:
                    patches = patchInfo.Finalizers;
                    break;
                default:
                    return false;
            }

            string expectedTypeName = PatchTypePrefix + nestedPatchTypeName;
            foreach (Patch patch in patches)
            {
                MethodInfo patchMethod = patch?.PatchMethod;
                if (patchMethod != null &&
                    string.Equals(patch.owner, HarmonyOwner, StringComparison.Ordinal) &&
                    string.Equals(patchMethod.DeclaringType?.FullName, expectedTypeName, StringComparison.Ordinal) &&
                    string.Equals(patchMethod.Name, patchMethodName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearLocked(bool restoreOriginalHitPoints, string source)
        {
            Hero mainHero = _mainHero;
            int originalHitPoints = _originalHitPoints;
            string missionName = _missionName;
            int observedHitPoints = mainHero?.HitPoints ?? 0;
            bool restored = false;

            if (restoreOriginalHitPoints &&
                mainHero != null &&
                mainHero.IsAlive &&
                originalHitPoints > 0 &&
                mainHero.HitPoints != originalHitPoints)
            {
                mainHero.HitPoints = Math.Min(mainHero.MaxHitPoints, originalHitPoints);
                restored = true;
            }

            _openingMission = false;
            _boundMission = null;
            _mainHero = null;
            _originalHitPoints = 0;
            _missionName = null;

            if (mainHero != null)
            {
                ModLogger.Info(
                    "WoundedMainHeroCampaignShellState: cleared virtual healthy main-hero shell. " +
                    "MissionName=" + (missionName ?? "null") +
                    " Source=" + (source ?? "unknown") +
                    " ObservedHitPoints=" + observedHitPoints +
                    " OriginalHitPoints=" + originalHitPoints +
                    " Restored=" + restored + ".");
            }
        }

        private enum PatchCollectionKind
        {
            Prefix,
            Postfix,
            Transpiler,
            Finalizer
        }
    }
}
