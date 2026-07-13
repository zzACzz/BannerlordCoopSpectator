using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator.MissionModels
{
    /// <summary>
    /// Snapshot-backed campaign morale model for authoritative exact siege combat.
    /// It deliberately has no dependency on Campaign.Current so it is safe on the
    /// dedicated server, where TaleWorlds.CampaignSystem is not available.
    /// </summary>
    public sealed class CoopCampaignDerivedBattleMoraleModel : BattleMoraleModel
    {
        private const ulong AreaAffectingWeaponFlags = 1073766400UL;
        private const ulong BurningAreaWeaponFlags = 1073774592UL;
        private const int LoyaltyAndHonorMinimumTier = 3;
        private const int EpicPerkSkillThreshold = 250;
        private const int EpicPerkMaximumSkill = 330;

        private readonly BattleMoraleModel _baseModel;
        private bool _hasLoggedActivation;

        public CoopCampaignDerivedBattleMoraleModel(BattleMoraleModel baseModel)
        {
            _baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
        }

        public override (float affectedSideMaxMoraleLoss, float affectorSideMaxMoraleGain)
            CalculateMaxMoraleChangeDueToAgentIncapacitated(
                Agent affectedAgent,
                AgentState affectedAgentState,
                Agent affectorAgent,
                in KillingBlow killingBlow)
        {
            if (!IsActive(affectedAgent?.Mission ?? affectorAgent?.Mission))
            {
                return _baseModel.CalculateMaxMoraleChangeDueToAgentIncapacitated(
                    affectedAgent,
                    affectedAgentState,
                    affectorAgent,
                    in killingBlow);
            }

            TryLogActivation(affectedAgent?.Mission ?? affectorAgent?.Mission);
            Agent humanAffector = affectorAgent?.IsHuman == true
                ? affectorAgent
                : affectorAgent?.IsMount == true ? affectorAgent.RiderAgent : null;
            float battleImportance = ResolveBattleImportance(affectedAgent);
            float casualtiesFactor = CalculateCasualtiesFactor(
                affectedAgent?.Team?.Side ?? BattleSideEnum.None);
            SkillObject relevantSkill = WeaponComponentData.GetRelevantSkillFromWeaponClass(
                (WeaponClass)killingBlow.WeaponClass);
            bool isMelee = relevantSkill == DefaultSkills.OneHanded ||
                relevantSkill == DefaultSkills.TwoHanded ||
                relevantSkill == DefaultSkills.Polearm;
            bool isRanged = relevantSkill == DefaultSkills.Bow ||
                relevantSkill == DefaultSkills.Crossbow ||
                relevantSkill == DefaultSkills.Throwing;

            ulong weaponFlags = (ulong)killingBlow.WeaponRecordWeaponFlags;
            bool isAreaEffect = (weaponFlags & AreaAffectingWeaponFlags) != 0UL;
            float weaponFactor = isAreaEffect ? 0.25f : isRanged ? 0.5f : 0.75f;
            if (isAreaEffect && (weaponFlags & BurningAreaWeaponFlags) == BurningAreaWeaponFlags)
                weaponFactor *= 1.25f;

            float moraleGainBase = Math.Max(0f, battleImportance * 3f * weaponFactor);
            float moraleLossBase = Math.Max(0f, battleImportance * 4f * weaponFactor * casualtiesFactor);
            float moraleGain = ApplyAffectorMoraleGainPerks(humanAffector, relevantSkill, moraleGainBase);
            float moraleLoss = affectedAgentState == AgentState.Unconscious && HasPartyMedicineHealthAdvice(affectedAgent)
                ? 0f
                : ApplyMoraleLossPerks(affectedAgent, humanAffector, relevantSkill, isMelee, isRanged, moraleLossBase);

            ExactSiegeMoraleDiagnostics.RecordCasualtyShock(
                affectedAgent,
                affectedAgentState,
                moraleLoss,
                moraleGain);
            return (Math.Max(0f, moraleLoss), Math.Max(0f, moraleGain));
        }

        public override (float affectedSideMaxMoraleLoss, float affectorSideMaxMoraleGain)
            CalculateMaxMoraleChangeDueToAgentPanicked(Agent agent)
        {
            if (!IsActive(agent?.Mission))
                return _baseModel.CalculateMaxMoraleChangeDueToAgentPanicked(agent);

            float battleImportance = ResolveBattleImportance(agent);
            float moraleLossBase = battleImportance * CalculateCasualtiesFactor(
                agent?.Team?.Side ?? BattleSideEnum.None) * 1.1f;
            var lossAccumulator = new CaptainPerkBonusAccumulator(moraleLossBase);
            if (TryResolveEntry(agent, out string entryId, out RosterEntryState entry))
                GlobalCaptainPerkRuntimeState.AddEffect(entryId, "PolearmStandardBearer", lossAccumulator);
            AddQuartermasterPriceOfLoyalty(entry, lossAccumulator);

            float moraleLoss = Math.Max(0f, lossAccumulator.Result);
            float moraleGain = Math.Max(0f, battleImportance * 2f);
            ExactSiegeMoraleDiagnostics.RecordPanicShock(agent, moraleLoss, moraleGain);
            return (moraleLoss, moraleGain);
        }

        public override float CalculateMoraleChangeToCharacter(Agent agent, float maxMoraleChange)
        {
            if (!IsActive(agent?.Mission))
                return _baseModel.CalculateMoraleChangeToCharacter(agent, maxMoraleChange);

            float resistance = ResolveMoraleResistance(agent);
            float change = maxMoraleChange / Math.Max(1f, resistance);
            ExactSiegeMoraleDiagnostics.RecordRecipient(agent, maxMoraleChange, change);
            return change;
        }

        public override float GetEffectiveInitialMorale(Agent agent, float baseMorale)
        {
            if (!IsActive(agent?.Mission))
                return _baseModel.GetEffectiveInitialMorale(agent, baseMorale);

            // The native CommonAI base morale remains authoritative. Campaign-only
            // modifiers that need PartyBase are intentionally not queried here.
            return baseMorale;
        }

        public override bool CanPanicDueToMorale(Agent agent)
        {
            if (!IsActive(agent?.Mission))
                return _baseModel.CanPanicDueToMorale(agent);

            if (!TryResolveEntry(agent, out _, out RosterEntryState entry) ||
                entry == null ||
                entry.Tier < LoyaltyAndHonorMinimumTier ||
                string.IsNullOrWhiteSpace(entry.PartyId))
            {
                return true;
            }

            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            return !TryGetParty(state, entry.PartyId, out BattlePartyState party) ||
                !HasPerk(party?.Modifiers?.PartyLeaderPerkIds, "LeadershipLoyaltyAndHonor");
        }

        public override float CalculateCasualtiesFactor(BattleSideEnum battleSide)
        {
            Mission mission = Mission.Current;
            if (!IsActive(mission))
                return _baseModel.CalculateCasualtiesFactor(battleSide);

            if (battleSide == BattleSideEnum.None)
                return 1f;

            return Math.Max(0f, 1f + mission.GetRemovedAgentRatioForSide(battleSide) * 2f);
        }

        public override float GetAverageMorale(Formation formation)
        {
            if (!IsActive(Mission.Current))
                return _baseModel.GetAverageMorale(formation);

            float total = 0f;
            int count = 0;
            if (formation?.Arrangement != null)
            {
                foreach (IFormationUnit unit in formation.Arrangement.GetAllUnits())
                {
                    if (unit is Agent agent && agent.IsActive() && agent.IsHuman && agent.IsAIControlled)
                    {
                        total += agent.GetMorale();
                        count++;
                    }
                }
            }

            return count > 0 ? MBMath.ClampFloat(total / count, 0f, 100f) : 0f;
        }

        public override float CalculateMoraleChangeOnShipSunk(IShipOrigin shipOrigin)
        {
            return IsActive(Mission.Current)
                ? 0f
                : _baseModel.CalculateMoraleChangeOnShipSunk(shipOrigin);
        }

        public override float CalculateMoraleOnRamming(Agent agent, IShipOrigin rammingShip, IShipOrigin rammedShip)
        {
            return IsActive(agent?.Mission)
                ? agent?.GetMorale() ?? 0f
                : _baseModel.CalculateMoraleOnRamming(agent, rammingShip, rammedShip);
        }

        public override float CalculateMoraleOnShipsConnected(Agent agent, IShipOrigin ownerShip, IShipOrigin targetShip)
        {
            return IsActive(agent?.Mission)
                ? agent?.GetMorale() ?? 0f
                : _baseModel.CalculateMoraleOnShipsConnected(agent, ownerShip, targetShip);
        }

        private static float ApplyAffectorMoraleGainPerks(
            Agent affectorAgent,
            SkillObject relevantSkill,
            float baseValue)
        {
            var accumulator = new CaptainPerkBonusAccumulator(baseValue);
            if (!TryResolveEntry(affectorAgent, out _, out RosterEntryState entry))
                return accumulator.Result;

            AddPersonalFactor(entry, "LeadershipMakeADifference", 1f, accumulator);
            if (relevantSkill == DefaultSkills.TwoHanded)
                AddPersonalFactor(entry, "TwoHandedHope", 0.3f, accumulator);
            return accumulator.Result;
        }

        private static float ApplyMoraleLossPerks(
            Agent affectedAgent,
            Agent affectorAgent,
            SkillObject relevantSkill,
            bool isMelee,
            bool isRanged,
            float baseValue)
        {
            var accumulator = new CaptainPerkBonusAccumulator(baseValue);
            if (TryResolveEntry(affectorAgent, out string affectorEntryId, out RosterEntryState affectorEntry))
            {
                if (relevantSkill == DefaultSkills.TwoHanded)
                    AddPersonalFactor(affectorEntry, "TwoHandedTerror", 0.2f, accumulator);

                bool isMounted = affectorAgent?.HasMount == true || affectorAgent?.MountAgent != null;
                if (isMelee && isMounted)
                {
                    AddPersonalFactor(affectorEntry, "RidingThunderousCharge", 0.2f, accumulator);
                    GlobalCaptainPerkRuntimeState.AddEffect(affectorEntryId, "RidingThunderousCharge", accumulator);
                }

                if (relevantSkill == DefaultSkills.Crossbow)
                    GlobalCaptainPerkRuntimeState.AddEffect(affectorEntryId, "CrossbowTerror", accumulator);

                if (isRanged && isMounted)
                {
                    AddPersonalFactor(affectorEntry, "RidingAnnoyingBuzz", 0.2f, accumulator);
                    GlobalCaptainPerkRuntimeState.AddEffect(affectorEntryId, "RidingAnnoyingBuzz", accumulator);
                }

                GlobalCaptainPerkRuntimeState.AddEffect(affectorEntryId, "LeadershipHeroicLeader", accumulator);
            }

            if (TryResolveEntry(affectedAgent, out string affectedEntryId, out RosterEntryState affectedEntry))
            {
                ArrangementOrder.ArrangementOrderEnum arrangement =
                    affectedAgent.Formation?.ArrangementOrder.OrderEnum ?? ArrangementOrder.ArrangementOrderEnum.Line;
                if (arrangement == ArrangementOrder.ArrangementOrderEnum.ShieldWall ||
                    arrangement == ArrangementOrder.ArrangementOrderEnum.Square ||
                    arrangement == ArrangementOrder.ArrangementOrderEnum.Skein ||
                    arrangement == ArrangementOrder.ArrangementOrderEnum.Column)
                {
                    GlobalCaptainPerkRuntimeState.AddEffect(affectedEntryId, "TacticsTightFormations", accumulator);
                }

                if (arrangement == ArrangementOrder.ArrangementOrderEnum.Line ||
                    arrangement == ArrangementOrder.ArrangementOrderEnum.Loose ||
                    arrangement == ArrangementOrder.ArrangementOrderEnum.Circle ||
                    arrangement == ArrangementOrder.ArrangementOrderEnum.Scatter)
                {
                    GlobalCaptainPerkRuntimeState.AddEffect(affectedEntryId, "TacticsLooseFormations", accumulator);
                }

                GlobalCaptainPerkRuntimeState.AddEffect(affectedEntryId, "PolearmStandardBearer", accumulator);
                AddQuartermasterPriceOfLoyalty(affectedEntry, accumulator);
            }

            return accumulator.Result;
        }

        private static void AddQuartermasterPriceOfLoyalty(
            RosterEntryState beneficiaryEntry,
            CaptainPerkBonusAccumulator accumulator)
        {
            if (beneficiaryEntry == null ||
                accumulator == null ||
                string.IsNullOrWhiteSpace(beneficiaryEntry.PartyId))
            {
                return;
            }

            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            if (!TryGetParty(state, beneficiaryEntry.PartyId, out BattlePartyState party) ||
                !HasPerk(party?.Modifiers?.QuartermasterPerkIds, "StewardPriceOfLoyalty"))
            {
                return;
            }

            int skill = Math.Min(EpicPerkMaximumSkill, Math.Max(0, party.Modifiers.QuartermasterStewardSkill));
            int skillAboveThreshold = Math.Max(0, skill - EpicPerkSkillThreshold);
            if (skillAboveThreshold <= 0)
                return;

            accumulator.Add(new CaptainPerkEffectSnapshotMessage
            {
                PerkId = "StewardPriceOfLoyalty",
                Bonus = -0.005f * skillAboveThreshold,
                IncrementType = "AddFactor"
            });
        }

        private static void AddPersonalFactor(
            RosterEntryState entry,
            string perkId,
            float factor,
            CaptainPerkBonusAccumulator accumulator)
        {
            if (entry == null || accumulator == null || !HasPerk(entry.PerkIds, perkId))
                return;

            accumulator.Add(new CaptainPerkEffectSnapshotMessage
            {
                PerkId = perkId,
                Bonus = factor,
                IncrementType = "AddFactor"
            });
        }

        private static bool HasPartyMedicineHealthAdvice(Agent agent)
        {
            if (!TryResolveEntry(agent, out _, out RosterEntryState entry) ||
                entry == null ||
                string.IsNullOrWhiteSpace(entry.PartyId))
            {
                return false;
            }

            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            return TryGetParty(state, entry.PartyId, out BattlePartyState party) &&
                HasPerk(party?.Modifiers?.SurgeonPerkIds, "MedicineHealthAdvise");
        }

        private static bool TryResolveEntry(
            Agent agent,
            out string entryId,
            out RosterEntryState entry)
        {
            entry = null;
            if (agent == null ||
                !CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) ||
                string.IsNullOrWhiteSpace(entryId))
            {
                entryId = null;
                return false;
            }

            entry = BattleSnapshotRuntimeState.GetEntryState(entryId);
            return entry != null;
        }

        private static bool TryGetParty(
            BattleRuntimeState state,
            string partyId,
            out BattlePartyState party)
        {
            party = null;
            return state != null &&
                !string.IsNullOrWhiteSpace(partyId) &&
                state.PartiesById.TryGetValue(partyId, out party) &&
                party != null;
        }

        private static bool HasPerk(IEnumerable<string> perkIds, string expectedPerkId)
        {
            return perkIds?.Any(perkId =>
                !string.IsNullOrWhiteSpace(perkId) &&
                perkId.IndexOf(expectedPerkId, StringComparison.OrdinalIgnoreCase) >= 0) == true;
        }

        private static float ResolveMoraleResistance(Agent agent)
        {
            if (!TryResolveEntry(agent, out _, out RosterEntryState entry))
                return Math.Max(1f, agent?.Character?.GetMoraleResistance() ?? 1f);

            int powerTier = entry.IsHero
                ? Math.Max(1, entry.HeroLevel / 4 + 1)
                : Math.Max(0, entry.Tier);
            return (entry.IsHero ? 1.5f : 1f) * (0.5f * powerTier + 1f);
        }

        private static float ResolveBattleImportance(Agent agent)
        {
            if (!TryResolveEntry(agent, out _, out RosterEntryState entry))
                return Math.Max(0f, agent?.GetBattleImportance() ?? 1f);

            int powerTier = entry.IsHero
                ? Math.Max(1, entry.HeroLevel / 4 + 1)
                : Math.Max(0, entry.Tier);
            float roleMultiplier = entry.IsHero ? 1.5f : entry.IsMounted ? 1.2f : 1f;
            float power = (2f + powerTier) * (8f + powerTier) * 0.02f * roleMultiplier;
            float basePower = 2f * 8f * 0.02f;
            float importance = Math.Max(1f + 0.5f * (power - basePower), 1f);
            if (agent?.Team != null && ReferenceEquals(agent, agent.Team.GeneralAgent))
                importance *= 2f;
            else if (agent?.Formation != null && ReferenceEquals(agent, agent.Formation.Captain))
                importance *= 1.2f;
            return importance;
        }

        private static bool IsActive(Mission mission)
        {
            return GameNetwork.IsServer &&
                mission != null &&
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext?.IsSiegeBattle == true;
        }

        private void TryLogActivation(Mission mission)
        {
            if (_hasLoggedActivation)
                return;

            _hasLoggedActivation = true;
            ModLogger.Info(
                "CoopCampaignDerivedBattleMoraleModel: activated for authoritative exact siege. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " BaseModel=" + _baseModel.GetType().FullName + ".");
        }
    }
}
