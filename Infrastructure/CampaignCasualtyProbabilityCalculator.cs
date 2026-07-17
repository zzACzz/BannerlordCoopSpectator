using System;
using System.Collections.Generic;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;

namespace CoopSpectator.Infrastructure
{
    internal static class CampaignCasualtyProbabilityCalculator
    {
        public const int CurrentRulesVersion = 1;

        private const string DoctorsOathPerkId = "MedicineDoctorsOath";
        private const string PhysicianOfPeoplePerkId = "MedicinePhysicianOfPeople";
        private const string CheatDeathPerkId = "MedicineCheatDeath";

        public static bool SupportsScenario(BattleSnapshotMessage snapshot)
        {
            return snapshot?.ScenarioContext != null &&
                   (snapshot.ScenarioContext.IsSiegeBattle ||
                    ExactLandBattleScenarioContract.IsLandBattleScenario(snapshot.ScenarioContext));
        }

        public static bool TryCalculateDeathProbability(
            BattleSnapshotMessage snapshot,
            BattleRuntimeState runtimeState,
            RosterEntryState victimEntry,
            RosterEntryState attackerEntry,
            DamageTypes damageType,
            WeaponFlags weaponFlags,
            out float deathProbability)
        {
            deathProbability = 1f;
            if (snapshot == null || runtimeState == null || victimEntry == null ||
                snapshot.CasualtyRulesVersion != CurrentRulesVersion ||
                !SupportsScenario(snapshot))
            {
                return false;
            }

            if ((damageType == DamageTypes.Blunt && (weaponFlags & WeaponFlags.CanKillEvenIfBlunt) == 0) ||
                victimEntry.ForceUnconscious ||
                (victimEntry.IsHero && !victimEntry.HeroCanDieInBattle) ||
                (victimEntry.IsHero && snapshot.BattleDeathDifficulty == 0) ||
                (victimEntry.IsPlayerCharacter && snapshot.BattleDeathDifficulty == 1))
            {
                deathProbability = 0f;
                return true;
            }

            runtimeState.PartiesById.TryGetValue(victimEntry.PartyId ?? string.Empty, out BattlePartyState victimParty);
            BattlePartyState attackerParty = null;
            if (attackerEntry != null)
                runtimeState.PartiesById.TryGetValue(attackerEntry.PartyId ?? string.Empty, out attackerParty);

            if (victimParty == null || !victimParty.HasMobileParty)
            {
                deathProbability = 1f;
                return true;
            }

            float denominator = 1f;
            if (victimParty?.Modifiers != null)
            {
                int medicineSkill = victimParty.Modifiers.SurvivalMedicineSkill > 0
                    ? victimParty.Modifiers.SurvivalMedicineSkill
                    : victimParty.Modifiers.SurgeonMedicineSkill;
                denominator += 0.01f * Math.Max(0, medicineSkill) * (snapshot.IsPlayerMapEvent ? 1f : 0.25f);
            }

            if (attackerParty?.Modifiers != null && HasPerk(attackerParty.Modifiers.SurgeonPerkIds, DoctorsOathPerkId))
            {
                int medicineSkill = attackerParty.Modifiers.SurvivalMedicineSkill > 0
                    ? attackerParty.Modifiers.SurvivalMedicineSkill
                    : attackerParty.Modifiers.SurgeonMedicineSkill;
                denominator += 0.01f * Math.Max(0, medicineSkill) * (snapshot.IsPlayerMapEvent ? 1f : 0.1f);
            }

            int characterLevel = victimEntry.CharacterLevel > 0
                ? victimEntry.CharacterLevel
                : victimEntry.HeroLevel > 0
                    ? victimEntry.HeroLevel
                    : Math.Max(0, victimEntry.Tier);
            denominator += characterLevel * 0.02f;

            float denominatorFactor = 1f;
            if (!victimEntry.IsHero &&
                victimEntry.Tier < 3 &&
                victimParty != null &&
                HasPerk(victimParty.Modifiers?.SurgeonPerkIds, PhysicianOfPeoplePerkId))
            {
                denominatorFactor += 0.3f;
            }

            if (victimEntry.IsHero)
            {
                denominator += Math.Max(0f, victimEntry.HeroTotalArmorSum) * 0.01f;
                denominator -= Math.Max(0f, victimEntry.HeroAge) * 0.01f;
                denominatorFactor += 0.5f;
            }

            denominator *= denominatorFactor;
            float probability = Math.Abs(denominator) < 0.000001f
                ? 1f
                : 1f / denominator;

            if (victimEntry.IsHero &&
                victimParty != null &&
                HasPerk(victimParty.Modifiers?.SurgeonPerkIds, CheatDeathPerkId))
            {
                probability *= 0.5f;
            }

            if (victimEntry.IsHero && victimEntry.IsPlayerClanHero)
                probability *= 1f + snapshot.ClanMemberDeathChanceMultiplier;

            deathProbability = Math.Max(0f, Math.Min(1f, probability));
            return true;
        }

        private static bool HasPerk(IEnumerable<string> perkIds, string expectedId)
        {
            if (perkIds == null || string.IsNullOrWhiteSpace(expectedId))
                return false;

            foreach (string perkId in perkIds)
            {
                if (string.Equals(perkId, expectedId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
