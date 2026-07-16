using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;

namespace CoopSpectator.UI
{
    internal sealed class CoopSiegeOrderOfBattleVM : OrderOfBattleVM
    {
        private static CoopSiegeOrderOfBattleVM _activeReusableCaptainViewModel;
        private static Mission _autoDeployPreservedMission;
        private static BattleSideEnum _autoDeployPreservedSide = BattleSideEnum.None;
        private static DateTime _autoDeployPreservationLastMutationUtc = DateTime.MinValue;
        private static int _mountedFormationInitializationDepth;
        private static readonly Dictionary<int, string> AutoDeployPreservedCaptainEntryIdByFormationIndex =
            new Dictionary<int, string>();
        private static readonly TimeSpan AutoDeployCaptainAssignmentStabilityDelay = TimeSpan.FromMilliseconds(400);

        private readonly Dictionary<Agent, OrderOfBattleHeroItemVM> _reusableCaptainSources =
            new Dictionary<Agent, OrderOfBattleHeroItemVM>();
        private readonly Dictionary<int, OrderOfBattleHeroItemVM> _virtualCaptainByFormationIndex =
            new Dictionary<int, OrderOfBattleHeroItemVM>();
        private readonly Dictionary<int, string> _captainEntryIdByFormationIndex =
            new Dictionary<int, string>();
        private readonly HashSet<OrderOfBattleHeroItemVM> _autoDeployPreservedVirtualCaptains =
            new HashSet<OrderOfBattleHeroItemVM>();
        private readonly bool _projectMountedClassesToSiegeFootClasses;

        private Action<OrderOfBattleFormationItemVM> _nativeAcceptCaptain;
        private Action<OrderOfBattleHeroItemVM> _nativeHeroAssignedFormationChanged;
        private bool _isUpdatingReusableCaptains;
        private bool _isPreservingReusableCaptainsForAutoDeploy;

        internal static bool IsApplyingInitialProjectedConfiguration { get; private set; }
        internal static bool IsApplyingInitialMountedConfiguration =>
            _mountedFormationInitializationDepth > 0;

        internal static void BeginInitialMountedConfiguration()
        {
            _mountedFormationInitializationDepth++;
        }

        internal static void EndInitialMountedConfiguration()
        {
            if (_mountedFormationInitializationDepth > 0)
                _mountedFormationInitializationDepth--;
        }

        internal CoopSiegeOrderOfBattleVM(bool projectMountedClassesToSiegeFootClasses)
        {
            _projectMountedClassesToSiegeFootClasses = projectMountedClassesToSiegeFootClasses;
        }

        internal void EnableReusableCompanionCaptainAssignments()
        {
            _activeReusableCaptainViewModel = this;
            _nativeAcceptCaptain = OrderOfBattleFormationItemVM.OnAcceptCaptain;
            _nativeHeroAssignedFormationChanged = OrderOfBattleHeroItemVM.OnHeroAssignedFormationChanged;

            Team team = _allFormations
                .Select(item => item?.Formation?.Team)
                .FirstOrDefault(candidate => candidate != null);
            EnsureSemanticCampaignHeroItems(team);

            _reusableCaptainSources.Clear();
            foreach (OrderOfBattleHeroItemVM heroItem in _allHeroes.ToArray())
            {
                TryApplyExactCampaignHeroImage(heroItem);
                if (IsReusableCompanionSource(heroItem))
                    _reusableCaptainSources[heroItem.Agent] = heroItem;
            }

            OrderOfBattleFormationItemVM.OnAcceptCaptain = HandleFormationAcceptReusableCaptain;
            OrderOfBattleHeroItemVM.OnHeroAssignedFormationChanged = HandleReusableHeroAssignedFormationChanged;
            ConvertInitialCompanionCaptainAssignments();
            TryRestoreAutoDeployPreservedCaptainAssignments(team);

            OrderOfBattleSiegeProjectedCountsPatch.TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                team,
                "CoopSiegeOrderOfBattleVM reusable captain initialization");
        }

        internal void NormalizeMountedFormationComposition()
        {
            if (_projectMountedClassesToSiegeFootClasses || _allFormations == null)
                return;

            MethodInfo transferMethod = typeof(OrderOfBattleVM).GetMethod(
                "TransferAllAvailableTroopsToFormation",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(OrderOfBattleFormationItemVM), typeof(FormationClass) },
                null);
            if (transferMethod == null)
                return;

            for (FormationClass formationClass = FormationClass.Infantry;
                 formationClass < FormationClass.NumberOfDefaultFormations;
                 formationClass++)
            {
                List<OrderOfBattleFormationItemVM> targetItems = _allFormations
                    .Where(item =>
                        item?.Formation != null &&
                        item.Classes != null &&
                        item.Classes.Any(classVm => classVm?.Class == formationClass))
                    .ToList();
                if (targetItems.Count != 1)
                    continue;

                try
                {
                    transferMethod.Invoke(this, new object[] { targetItems[0], formationClass });
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopSiegeOrderOfBattleVM: mounted formation normalization failed open. " +
                        "Class=" + formationClass +
                        " Error=" + ex.GetType().Name + ":" + ex.Message);
                }
            }

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                formationItem?.OnSizeChanged();
                formationItem?.UpdateAdjustable();
            }
        }

        internal void RestoreMountedFormationPresentationAfterAutoDeploy()
        {
            if (_projectMountedClassesToSiegeFootClasses || _allFormations == null)
                return;

            BeginInitialMountedConfiguration();
            try
            {
                LoadMountedFormationConfiguration();
            }
            finally
            {
                EndInitialMountedConfiguration();
            }
        }

        internal void BeginAutoDeployCaptainAssignmentPreservation(Team team)
        {
            Mission mission = Mission.Current;
            if (mission == null || team == null || !IsForTeam(team))
                return;

            AutoDeployPreservedCaptainEntryIdByFormationIndex.Clear();
            foreach (KeyValuePair<int, string> assignment in _captainEntryIdByFormationIndex)
            {
                if (assignment.Key < 0 ||
                    assignment.Key >= (int)FormationClass.NumberOfRegularFormations ||
                    string.IsNullOrWhiteSpace(assignment.Value))
                {
                    continue;
                }

                AutoDeployPreservedCaptainEntryIdByFormationIndex[assignment.Key] = assignment.Value;
            }

            _autoDeployPreservedMission = mission;
            _autoDeployPreservedSide = team.Side;
            _autoDeployPreservationLastMutationUtc = DateTime.UtcNow;
            _autoDeployPreservedVirtualCaptains.Clear();
            foreach (OrderOfBattleHeroItemVM virtualCaptain in _virtualCaptainByFormationIndex.Values)
            {
                if (virtualCaptain != null)
                    _autoDeployPreservedVirtualCaptains.Add(virtualCaptain);
            }
            _isPreservingReusableCaptainsForAutoDeploy = true;
        }

        internal void CancelAutoDeployCaptainAssignmentPreservation()
        {
            _isPreservingReusableCaptainsForAutoDeploy = false;
            _autoDeployPreservedVirtualCaptains.Clear();
            ClearAutoDeployPreservedCaptainAssignments();
        }

        internal bool TryCompleteAutoDeployCaptainAssignmentRestorationIfStable(Team team)
        {
            if (!IsAutoDeployCaptainAssignmentPreservationActiveForTeam(team) ||
                DateTime.UtcNow - _autoDeployPreservationLastMutationUtc < AutoDeployCaptainAssignmentStabilityDelay)
            {
                return false;
            }

            TryRestoreAutoDeployPreservedCaptainAssignments(team);
            _isPreservingReusableCaptainsForAutoDeploy = false;
            _autoDeployPreservedVirtualCaptains.Clear();
            if (IsAutoDeployCaptainAssignmentPreservationActiveForTeam(team))
                ClearAutoDeployPreservedCaptainAssignments();
            return true;
        }

        private void EnsureSemanticCampaignHeroItems(Team team)
        {
            Mission mission = Mission.Current;
            if (team == null || mission?.AllAgents == null || UnassignedHeroes == null)
                return;

            int nativeHeroItemCount = _allHeroes.Count;
            int addedHeroItemCount = 0;
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(
                BattleSnapshotRuntimeState.GetState(),
                team.Side);

            foreach (Agent agent in mission.AllAgents)
            {
                if (agent == null ||
                    agent.IsMount ||
                    !agent.IsActive() ||
                    !ReferenceEquals(agent.Team, team) ||
                    _allHeroes.Any(item => ReferenceEquals(item?.Agent, agent)) ||
                    !CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out string entryId) ||
                    string.IsNullOrWhiteSpace(entryId))
                {
                    continue;
                }

                RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                if (entryState?.IsHero != true)
                    continue;

                var heroItem = new OrderOfBattleHeroItemVM(agent)
                {
                    IsMainHero = string.Equals(commanderEntry?.EntryId, entryId, StringComparison.Ordinal)
                };
                TryApplyExactCampaignHeroImage(heroItem, entryState);
                OrderOfBattleFormationItemVM initialFormationItem = _allFormations.FirstOrDefault(
                    item => ReferenceEquals(item?.Formation, agent.Formation));
                if (initialFormationItem != null)
                    heroItem.SetInitialFormation(initialFormationItem);

                _allHeroes.Add(heroItem);
                UnassignedHeroes.Add(heroItem);
                addedHeroItemCount++;
            }

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                ModLogger.Info(
                    "CoopSiegeOrderOfBattleVM: semantic campaign hero pool diagnostics. " +
                    "Side=" + team.Side +
                    " NativeHeroItems=" + nativeHeroItemCount +
                    " AddedSemanticHeroItems=" + addedHeroItemCount +
                    " TotalHeroItems=" + _allHeroes.Count +
                    " UnassignedHeroItems=" + UnassignedHeroes.Count);
            }
        }

        public override void OnFinalize()
        {
            if (ReferenceEquals(_activeReusableCaptainViewModel, this))
                _activeReusableCaptainViewModel = null;

            base.OnFinalize();
            _virtualCaptainByFormationIndex.Clear();
            _captainEntryIdByFormationIndex.Clear();
            _reusableCaptainSources.Clear();
            _autoDeployPreservedVirtualCaptains.Clear();
        }

        internal static bool TryBuildReusableCaptainAssignmentPayload(
            Team team,
            out byte[] payload,
            out string assignmentKey)
        {
            payload = Array.Empty<byte>();
            assignmentKey = string.Empty;

            CoopSiegeOrderOfBattleVM active = _activeReusableCaptainViewModel;
            bool useAutoDeployPreservedAssignments =
                IsAutoDeployCaptainAssignmentPreservationActiveForTeam(team);
            if (!useAutoDeployPreservedAssignments &&
                (active == null || team == null || !active.IsForTeam(team)))
            {
                return false;
            }

            IReadOnlyDictionary<int, string> assignmentSource = useAutoDeployPreservedAssignments
                ? AutoDeployPreservedCaptainEntryIdByFormationIndex
                : active._captainEntryIdByFormationIndex;

            var records = new List<KeyValuePair<int, string>>();
            foreach (KeyValuePair<int, string> assignment in assignmentSource)
            {
                if (assignment.Key < 0 ||
                    assignment.Key >= (int)FormationClass.NumberOfRegularFormations ||
                    string.IsNullOrWhiteSpace(assignment.Value))
                {
                    continue;
                }

                records.Add(new KeyValuePair<int, string>(assignment.Key, assignment.Value.Trim()));
            }

            records.Sort((left, right) => left.Key.CompareTo(right.Key));
            var bytes = new List<byte> { (byte)Math.Min(byte.MaxValue, records.Count) };
            foreach (KeyValuePair<int, string> record in records)
            {
                byte[] entryIdBytes = Encoding.UTF8.GetBytes(record.Value);
                if (entryIdBytes.Length <= 0 || entryIdBytes.Length > ushort.MaxValue)
                    continue;

                if (bytes.Count + 3 + entryIdBytes.Length >
                    CoopCommanderDeploymentFormationAssignmentsMessage.MaxCaptainAssignmentBytes)
                {
                    return false;
                }

                bytes.Add((byte)record.Key);
                bytes.Add((byte)(entryIdBytes.Length & 0xFF));
                bytes.Add((byte)((entryIdBytes.Length >> 8) & 0xFF));
                bytes.AddRange(entryIdBytes);
            }

            payload = bytes.ToArray();
            assignmentKey = Convert.ToBase64String(payload);
            return true;
        }

        private void ConvertInitialCompanionCaptainAssignments()
        {
            _isUpdatingReusableCaptains = true;
            try
            {
                foreach (OrderOfBattleFormationItemVM formationItem in _allFormations.ToArray())
                {
                    OrderOfBattleHeroItemVM captain = formationItem?.Captain;
                    if (!IsReusableCompanionSource(captain))
                        continue;

                    Agent physicalAgent = captain.Agent;
                    Formation physicalFormation = physicalAgent?.Formation;
                    formationItem.UnassignCaptain();
                    AssignReusableCaptain(formationItem, captain, physicalFormation);
                }

                foreach (OrderOfBattleHeroItemVM source in _reusableCaptainSources.Values)
                    EnsureReusableSourceInPool(source);
            }
            finally
            {
                _isUpdatingReusableCaptains = false;
            }
        }

        private void HandleFormationAcceptReusableCaptain(OrderOfBattleFormationItemVM formationItem)
        {
            OrderOfBattleHeroItemVM selectedHero = LastSelectedHeroItem;
            if (formationItem == null || SelectedHeroCount != 1 || selectedHero?.Agent == null)
            {
                _nativeAcceptCaptain?.Invoke(formationItem);
                return;
            }

            if (!_reusableCaptainSources.TryGetValue(selectedHero.Agent, out OrderOfBattleHeroItemVM source) ||
                !IsReusableCompanionSource(source))
            {
                _nativeAcceptCaptain?.Invoke(formationItem);
                return;
            }

            if (!TryResolveReusableCompanionEntryId(source.Agent, out string entryId))
            {
                _nativeAcceptCaptain?.Invoke(formationItem);
                return;
            }

            _isUpdatingReusableCaptains = true;
            try
            {
                Agent physicalAgent = source.Agent;
                Formation physicalFormation = physicalAgent.Formation;
                formationItem.UnassignCaptain();
                AssignReusableCaptain(formationItem, source, physicalFormation, entryId);
                EnsureReusableSourceInPool(source);
                ExecuteClearHeroSelection();
                source.IsShown = true;

                Game.Current?.EventManager.TriggerEvent(
                    new OrderOfBattleHeroAssignedToFormationEvent(source.Agent, formationItem.Formation));
            }
            finally
            {
                _isUpdatingReusableCaptains = false;
            }

            OnUnitDeployed();
            OrderOfBattleSiegeProjectedCountsPatch.TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                formationItem.Formation?.Team,
                "CoopSiegeOrderOfBattleVM reusable captain assigned");
        }

        private void AssignReusableCaptain(
            OrderOfBattleFormationItemVM formationItem,
            OrderOfBattleHeroItemVM source,
            Formation physicalFormation,
            string resolvedEntryId = null)
        {
            if (formationItem?.Formation == null || source?.Agent == null)
                return;

            if (string.IsNullOrWhiteSpace(resolvedEntryId) &&
                !TryResolveReusableCompanionEntryId(source.Agent, out resolvedEntryId))
            {
                return;
            }

            var virtualCaptain = new OrderOfBattleHeroItemVM(source.Agent);
            TryApplyExactCampaignHeroImage(virtualCaptain);
            _allHeroes.Add(virtualCaptain);
            formationItem.Captain = virtualCaptain;
            if (physicalFormation != null &&
                ReferenceEquals(physicalFormation.Team, source.Agent.Team) &&
                !ReferenceEquals(source.Agent.Formation, physicalFormation))
            {
                source.Agent.Formation = physicalFormation;
            }

            int formationIndex = formationItem.Formation.Index;
            _virtualCaptainByFormationIndex[formationIndex] = virtualCaptain;
            _captainEntryIdByFormationIndex[formationIndex] = resolvedEntryId;
        }

        private void HandleReusableHeroAssignedFormationChanged(OrderOfBattleHeroItemVM heroItem)
        {
            bool isPreservedAutoDeployVirtualCaptain =
                heroItem != null &&
                _isPreservingReusableCaptainsForAutoDeploy &&
                _autoDeployPreservedVirtualCaptains.Contains(heroItem);
            if (heroItem == null ||
                (!_virtualCaptainByFormationIndex.Values.Contains(heroItem) &&
                 !isPreservedAutoDeployVirtualCaptain))
            {
                _nativeHeroAssignedFormationChanged?.Invoke(heroItem);
                return;
            }

            if (heroItem.CurrentAssignedFormationItem != null)
                return;

            if (isPreservedAutoDeployVirtualCaptain)
            {
                _autoDeployPreservationLastMutationUtc = DateTime.UtcNow;
                UnassignedHeroes?.Remove(heroItem);
                _allHeroes.Remove(heroItem);
                if (_reusableCaptainSources.TryGetValue(heroItem.Agent, out OrderOfBattleHeroItemVM preservedSource))
                    EnsureReusableSourceInPool(preservedSource);
                return;
            }

            int removedFormationIndex = -1;
            foreach (KeyValuePair<int, OrderOfBattleHeroItemVM> assignment in _virtualCaptainByFormationIndex.ToArray())
            {
                if (!ReferenceEquals(assignment.Value, heroItem))
                    continue;

                removedFormationIndex = assignment.Key;
                _virtualCaptainByFormationIndex.Remove(assignment.Key);
                _captainEntryIdByFormationIndex.Remove(assignment.Key);
                break;
            }

            UnassignedHeroes?.Remove(heroItem);
            _allHeroes.Remove(heroItem);
            if (_reusableCaptainSources.TryGetValue(heroItem.Agent, out OrderOfBattleHeroItemVM source))
                EnsureReusableSourceInPool(source);

            if (_isUpdatingReusableCaptains || removedFormationIndex < 0)
                return;

            OnUnitDeployed();
            OrderOfBattleSiegeProjectedCountsPatch.TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                heroItem.Agent?.Team,
                "CoopSiegeOrderOfBattleVM reusable captain unassigned");
        }

        private void TryRestoreAutoDeployPreservedCaptainAssignments(Team team)
        {
            Mission mission = Mission.Current;
            if (mission == null ||
                team == null ||
                !ReferenceEquals(_autoDeployPreservedMission, mission) ||
                _autoDeployPreservedSide != team.Side ||
                AutoDeployPreservedCaptainEntryIdByFormationIndex.Count <= 0)
            {
                return;
            }

            var preservedAssignments = new Dictionary<int, string>(
                AutoDeployPreservedCaptainEntryIdByFormationIndex);
            _isUpdatingReusableCaptains = true;
            try
            {
                foreach (KeyValuePair<int, OrderOfBattleHeroItemVM> existing in
                         _virtualCaptainByFormationIndex.ToArray())
                {
                    OrderOfBattleFormationItemVM existingFormation = _allFormations.FirstOrDefault(
                        item => item?.Formation?.Index == existing.Key);
                    existingFormation?.UnassignCaptain();
                }

                _virtualCaptainByFormationIndex.Clear();
                _captainEntryIdByFormationIndex.Clear();

                foreach (KeyValuePair<int, string> preserved in preservedAssignments.OrderBy(item => item.Key))
                {
                    OrderOfBattleFormationItemVM formationItem = _allFormations.FirstOrDefault(
                        item => item?.Formation?.Index == preserved.Key);
                    OrderOfBattleHeroItemVM source = _reusableCaptainSources.Values.FirstOrDefault(
                        candidate =>
                            TryResolveReusableCompanionEntryId(candidate?.Agent, out string candidateEntryId) &&
                            string.Equals(candidateEntryId, preserved.Value, StringComparison.Ordinal));
                    if (formationItem == null || source == null)
                        continue;

                    formationItem.UnassignCaptain();
                    AssignReusableCaptain(
                        formationItem,
                        source,
                        source.Agent.Formation,
                        preserved.Value);
                    EnsureReusableSourceInPool(source);
                }
            }
            finally
            {
                _isUpdatingReusableCaptains = false;
                _isPreservingReusableCaptainsForAutoDeploy = false;
                ClearAutoDeployPreservedCaptainAssignments();
            }

            OnUnitDeployed();
        }

        private static void ClearAutoDeployPreservedCaptainAssignments()
        {
            AutoDeployPreservedCaptainEntryIdByFormationIndex.Clear();
            _autoDeployPreservedMission = null;
            _autoDeployPreservedSide = BattleSideEnum.None;
            _autoDeployPreservationLastMutationUtc = DateTime.MinValue;
        }

        private static bool IsAutoDeployCaptainAssignmentPreservationActiveForTeam(Team team)
        {
            return team != null &&
                   ReferenceEquals(_autoDeployPreservedMission, Mission.Current) &&
                   _autoDeployPreservedSide == team.Side;
        }

        private void EnsureReusableSourceInPool(OrderOfBattleHeroItemVM source)
        {
            if (source == null || UnassignedHeroes == null)
                return;

            source.IsShown = true;
            if (!UnassignedHeroes.Contains(source))
                UnassignedHeroes.Insert(0, source);
        }

        private bool IsForTeam(Team team)
        {
            return _allFormations.Any(item => ReferenceEquals(item?.Formation?.Team, team));
        }

        private static bool IsReusableCompanionSource(OrderOfBattleHeroItemVM heroItem)
        {
            return heroItem?.Agent != null &&
                   TryResolveReusableCompanionEntryId(heroItem.Agent, out _);
        }

        internal static void TryApplyExactCampaignHeroImage(
            OrderOfBattleHeroItemVM heroItem,
            RosterEntryState entryState = null)
        {
            Agent agent = heroItem?.Agent;
            if (agent == null || agent.Character == null)
                return;

            if (entryState == null &&
                CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out string entryId))
            {
                entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            }
            if (entryState?.IsHero != true)
                return;

            try
            {
                Equipment equipment = agent.SpawnEquipment ?? agent.Character.Equipment;
                string equipmentCode = equipment?.CalculateEquipmentCode() ?? string.Empty;
                CharacterCode characterCode = CharacterCode.CreateFrom(
                    equipmentCode,
                    agent.BodyPropertiesValue,
                    agent.IsFemale,
                    true,
                    agent.ClothingColor1,
                    agent.ClothingColor2,
                    agent.Character.DefaultFormationClass,
                    agent.Character.Race);
                Type imageIdentifierType = Type.GetType(
                    "TaleWorlds.Core.ViewModelCollection.ImageIdentifiers.CharacterImageIdentifierVM, TaleWorlds.Core.ViewModelCollection",
                    throwOnError: false);
                PropertyInfo imageIdentifierProperty = typeof(OrderOfBattleHeroItemVM).GetProperty(
                    "ImageIdentifier",
                    BindingFlags.Instance | BindingFlags.Public);
                if (imageIdentifierType == null || imageIdentifierProperty == null)
                    return;

                object imageIdentifier = Activator.CreateInstance(imageIdentifierType, characterCode);
                imageIdentifierProperty.SetValue(heroItem, imageIdentifier);
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "CoopSiegeOrderOfBattleVM: exact campaign hero portrait application failed. " +
                        "AgentIndex=" + agent.Index +
                        " Error=" + ex.GetType().Name + ":" + ex.Message);
                }
            }
        }

        private static bool TryResolveReusableCompanionEntryId(Agent agent, out string entryId)
        {
            entryId = null;
            if (agent == null ||
                !CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) ||
                string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState?.IsHero != true)
                return false;

            BattleSideEnum side = agent.Team?.Side ?? BattleSideEnum.None;
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(
                BattleSnapshotRuntimeState.GetState(),
                side);
            return commanderEntry == null ||
                   !string.Equals(commanderEntry.EntryId, entryId, StringComparison.Ordinal);
        }

        protected override void LoadConfiguration()
        {
            if (!_projectMountedClassesToSiegeFootClasses)
            {
                base.LoadConfiguration();
                LoadMountedFormationConfiguration();
                return;
            }

            IsApplyingInitialProjectedConfiguration = true;

            try
            {
                base.LoadConfiguration();

                if (_allFormations == null || _allFormations.Count <= 0)
                    return;

                RestrictFormationClassSelectorsToSiegeClasses();

                var usedFormationItems = new HashSet<OrderOfBattleFormationItemVM>();
                SeedProjectedClass(
                    DeploymentFormationClass.Infantry,
                    FormationClass.Infantry,
                    usedFormationItems);
                SeedProjectedClass(
                    DeploymentFormationClass.Ranged,
                    FormationClass.Ranged,
                    usedFormationItems);

                ClearUnusedFormationSlots(usedFormationItems);
                RestrictFormationClassSelectorsToSiegeClasses();
            }
            finally
            {
                IsApplyingInitialProjectedConfiguration = false;
            }
        }

        private void LoadMountedFormationConfiguration()
        {
            if (_allFormations == null || _allFormations.Count <= 0)
                return;

            var usedFormationItems = new HashSet<OrderOfBattleFormationItemVM>();
            SeedMountedClass(FormationClass.Infantry, usedFormationItems);
            SeedMountedClass(FormationClass.Ranged, usedFormationItems);
            SeedMountedClass(FormationClass.Cavalry, usedFormationItems);
            SeedMountedClass(FormationClass.HorseArcher, usedFormationItems);
            ClearUnusedMountedFormationSlots(usedFormationItems);
        }

        private void SeedMountedClass(
            FormationClass formationClass,
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            int totalCount = CountMountedUnitsInClass(formationClass);
            if (totalCount <= 0)
                return;

            List<OrderOfBattleFormationItemVM> formationItems = CollectDominantMountedFormationItems(
                formationClass,
                usedFormationItems);
            if (formationItems.Count <= 0)
            {
                OrderOfBattleFormationItemVM canonicalItem = _allFormations.FirstOrDefault(item =>
                    item?.Formation != null &&
                    item.Formation.Index == (int)formationClass &&
                    usedFormationItems?.Contains(item) != true);
                OrderOfBattleFormationItemVM fallbackItem =
                    canonicalItem ?? FindUnusedFormationItem(usedFormationItems, preferEmpty: true);
                if (fallbackItem != null)
                    formationItems.Add(fallbackItem);
            }

            if (formationItems.Count <= 0)
                return;

            int representedCount = formationItems.Sum(item =>
                CountMountedUnitsInClass(item?.Formation, formationClass));
            int remainingWeight = 100;
            int remainingCount = representedCount;
            for (int i = 0; i < formationItems.Count; i++)
            {
                OrderOfBattleFormationItemVM formationItem = formationItems[i];
                int weight;
                if (i == formationItems.Count - 1)
                {
                    weight = remainingWeight;
                }
                else if (remainingCount > 0)
                {
                    int itemCount = CountMountedUnitsInClass(formationItem?.Formation, formationClass);
                    weight = TaleWorlds.Library.MathF.Round(
                        (float)itemCount / remainingCount * remainingWeight);
                    weight = Math.Max(0, Math.Min(remainingWeight, weight));
                    remainingCount -= itemCount;
                    remainingWeight -= weight;
                }
                else
                {
                    int remainingItems = formationItems.Count - i;
                    weight = remainingWeight / remainingItems;
                    remainingWeight -= weight;
                }

                RefreshMountedFormationSlot(formationItem, formationClass, weight);
                usedFormationItems?.Add(formationItem);
            }
        }

        private List<OrderOfBattleFormationItemVM> CollectDominantMountedFormationItems(
            FormationClass formationClass,
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            var formationItems = new List<OrderOfBattleFormationItemVM>();
            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem?.Formation == null || usedFormationItems?.Contains(formationItem) == true)
                    continue;

                if (ResolveDominantMountedFormationClass(formationItem.Formation) == formationClass)
                    formationItems.Add(formationItem);
            }

            return formationItems;
        }

        private static FormationClass ResolveDominantMountedFormationClass(Formation formation)
        {
            FormationClass dominantClass = FormationClass.NumberOfAllFormations;
            int dominantCount = 0;
            for (FormationClass formationClass = FormationClass.Infantry;
                 formationClass < FormationClass.NumberOfDefaultFormations;
                 formationClass++)
            {
                int count = CountMountedUnitsInClass(formation, formationClass);
                bool isCanonicalTie =
                    count > 0 &&
                    count == dominantCount &&
                    formation?.Index == (int)formationClass;
                if (count > dominantCount || isCanonicalTie)
                {
                    dominantClass = formationClass;
                    dominantCount = count;
                }
            }

            return dominantClass;
        }

        private void RefreshMountedFormationSlot(
            OrderOfBattleFormationItemVM formationItem,
            FormationClass formationClass,
            int weight)
        {
            if (formationItem?.Formation == null)
                return;

            DeploymentFormationClass deploymentClass;
            switch (formationClass)
            {
                case FormationClass.Infantry:
                    deploymentClass = DeploymentFormationClass.Infantry;
                    break;
                case FormationClass.Ranged:
                    deploymentClass = DeploymentFormationClass.Ranged;
                    break;
                case FormationClass.Cavalry:
                    deploymentClass = DeploymentFormationClass.Cavalry;
                    break;
                case FormationClass.HorseArcher:
                    deploymentClass = DeploymentFormationClass.HorseArcher;
                    break;
                default:
                    return;
            }

            formationItem.RefreshFormation(formationItem.Formation, deploymentClass, true);
            SetPrimaryClassWeight(formationItem, weight);
            formationItem.OnSizeChanged();
            formationItem.UpdateAdjustable();
        }

        private int CountMountedUnitsInClass(FormationClass formationClass)
        {
            int count = 0;
            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
                count += CountMountedUnitsInClass(formationItem?.Formation, formationClass);
            return count;
        }

        private static int CountMountedUnitsInClass(
            Formation formation,
            FormationClass formationClass)
        {
            return OrderOfBattleSiegeProjectedCountsPatch.CountMountedCommanderDeploymentUnitsInClass(
                formation,
                formationClass);
        }

        private void ClearUnusedMountedFormationSlots(
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            if (usedFormationItems == null || _allFormations == null)
                return;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem?.Formation == null || usedFormationItems.Contains(formationItem))
                    continue;

                ClearFormationSlot(formationItem, restrictToSiegeClasses: false);
            }
        }

        private void SeedProjectedClass(
            DeploymentFormationClass deploymentClass,
            FormationClass projectedClass,
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            int totalCount = CountProjectedUnitsInClass(projectedClass);
            if (totalCount <= 0)
                return;

            List<OrderOfBattleFormationItemVM> formationItems = CollectFormationItemsForProjectedClass(
                projectedClass,
                usedFormationItems);
            if (formationItems.Count <= 0)
            {
                OrderOfBattleFormationItemVM fallbackItem = FindUnusedFormationItem(usedFormationItems, preferEmpty: false);
                if (fallbackItem != null)
                    formationItems.Add(fallbackItem);
            }

            if (totalCount > 1 && formationItems.Count == 1)
            {
                OrderOfBattleFormationItemVM secondaryItem = FindUnusedFormationItem(usedFormationItems, preferEmpty: true);
                if (secondaryItem != null && !formationItems.Contains(secondaryItem))
                    formationItems.Add(secondaryItem);
            }

            if (formationItems.Count <= 0)
                return;

            int slotCount = Math.Min(formationItems.Count, Math.Max(1, totalCount));
            int baseWeight = 100 / slotCount;
            int weightRemainder = 100 % slotCount;
            for (int i = 0; i < slotCount; i++)
            {
                OrderOfBattleFormationItemVM formationItem = formationItems[i];
                if (formationItem == null)
                    continue;

                int weight = baseWeight + (i < weightRemainder ? 1 : 0);
                RefreshFormationSlot(formationItem, deploymentClass, weight);
                usedFormationItems?.Add(formationItem);
            }
        }

        private List<OrderOfBattleFormationItemVM> CollectFormationItemsForProjectedClass(
            FormationClass projectedClass,
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            var formationItems = new List<OrderOfBattleFormationItemVM>();
            if (_allFormations == null)
                return formationItems;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem?.Formation == null || usedFormationItems?.Contains(formationItem) == true)
                    continue;

                if (CountProjectedUnitsInClass(formationItem.Formation, projectedClass) > 0)
                    formationItems.Add(formationItem);
            }

            return formationItems;
        }

        private OrderOfBattleFormationItemVM FindUnusedFormationItem(
            ISet<OrderOfBattleFormationItemVM> usedFormationItems,
            bool preferEmpty)
        {
            if (_allFormations == null)
                return null;

            OrderOfBattleFormationItemVM fallbackItem = null;
            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem == null || usedFormationItems?.Contains(formationItem) == true)
                    continue;

                bool isEmptyProjectedSlot =
                    formationItem.Formation == null ||
                    CountProjectedUnitsInClass(formationItem.Formation, FormationClass.Infantry) <= 0 &&
                    CountProjectedUnitsInClass(formationItem.Formation, FormationClass.Ranged) <= 0;

                if (!preferEmpty || isEmptyProjectedSlot)
                    return formationItem;

                if (fallbackItem == null)
                    fallbackItem = formationItem;
            }

            return fallbackItem;
        }

        private void RefreshFormationSlot(
            OrderOfBattleFormationItemVM formationItem,
            DeploymentFormationClass deploymentClass,
            int weight)
        {
            if (formationItem?.Formation == null)
                return;

            formationItem.RefreshFormation(formationItem.Formation, deploymentClass, true);
            RestrictFormationClassSelectorToSiegeClasses(formationItem);
            SetPrimaryClassWeight(formationItem, weight);
            formationItem.OnSizeChanged();
            formationItem.UpdateAdjustable();
        }

        private void ClearUnusedFormationSlots(ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            if (usedFormationItems == null || _allFormations == null)
                return;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem?.Formation == null || usedFormationItems.Contains(formationItem))
                    continue;

                ClearFormationSlot(formationItem, restrictToSiegeClasses: true);
            }
        }

        private static void ClearFormationSlot(
            OrderOfBattleFormationItemVM formationItem,
            bool restrictToSiegeClasses)
        {
            if (formationItem?.Formation == null)
                return;

            formationItem.RefreshFormation(formationItem.Formation, DeploymentFormationClass.Unset, false);
            if (restrictToSiegeClasses)
                RestrictFormationClassSelectorToSiegeClasses(formationItem);
            TrySetFormationClassSelectorIndex(formationItem, 0);

            if (formationItem.Classes != null)
            {
                for (int i = 0; i < formationItem.Classes.Count; i++)
                {
                    OrderOfBattleFormationClassVM classVm = formationItem.Classes[i];
                    if (classVm == null)
                        continue;

                    classVm.Class = FormationClass.NumberOfAllFormations;
                    classVm.Weight = 0;
                    classVm.IsLocked = false;
                }
            }

            formationItem.HasFormation = false;
            formationItem.IsSelectable = false;
            formationItem.OnSizeChanged();
            formationItem.UpdateAdjustable();
        }

        private void RestrictFormationClassSelectorsToSiegeClasses()
        {
            if (_allFormations == null)
                return;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
                RestrictFormationClassSelectorToSiegeClasses(formationItem);
        }

        private static void RestrictFormationClassSelectorToSiegeClasses(OrderOfBattleFormationItemVM formationItem)
        {
            if (formationItem == null)
                return;

            try
            {
                object selector = GetFormationClassSelector(formationItem);
                PropertyInfo itemListProperty = selector?.GetType().GetProperty(
                    "ItemList",
                    BindingFlags.Instance | BindingFlags.Public);
                if (!(itemListProperty?.GetValue(selector, null) is IEnumerable itemList))
                    return;

                var invalidItems = new List<object>();
                foreach (object selectorItem in itemList)
                {
                    if (!TryReadDeploymentFormationClass(selectorItem, out DeploymentFormationClass deploymentClass))
                        continue;

                    bool isAllowed = IsAllowedSiegeDeploymentFormationClass(deploymentClass);
                    SetSelectorItemCanBeSelected(selectorItem, isAllowed);
                    if (!isAllowed)
                        invalidItems.Add(selectorItem);
                }

                if (TryReadSelectedDeploymentFormationClass(selector, out DeploymentFormationClass selectedClass) &&
                    !IsAllowedSiegeDeploymentFormationClass(selectedClass))
                {
                    TrySetFormationClassSelectorIndex(formationItem, 0);
                }

                RemoveInvalidFormationClassSelectorItems(itemList, invalidItems);
            }
            catch
            {
            }
        }

        private static bool IsAllowedSiegeDeploymentFormationClass(DeploymentFormationClass deploymentClass)
        {
            return deploymentClass == DeploymentFormationClass.Unset ||
                   deploymentClass == DeploymentFormationClass.Infantry ||
                   deploymentClass == DeploymentFormationClass.Ranged ||
                   deploymentClass == DeploymentFormationClass.InfantryAndRanged;
        }

        private static bool TryReadSelectedDeploymentFormationClass(
            object selector,
            out DeploymentFormationClass deploymentClass)
        {
            deploymentClass = DeploymentFormationClass.Unset;
            try
            {
                PropertyInfo selectedItemProperty = selector?.GetType().GetProperty(
                    "SelectedItem",
                    BindingFlags.Instance | BindingFlags.Public);
                object selectedItem = selectedItemProperty?.GetValue(selector, null);
                return TryReadDeploymentFormationClass(selectedItem, out deploymentClass);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadDeploymentFormationClass(
            object selectorItem,
            out DeploymentFormationClass deploymentClass)
        {
            deploymentClass = DeploymentFormationClass.Unset;
            if (selectorItem == null)
                return false;

            try
            {
                FieldInfo formationClassField = selectorItem.GetType().GetField(
                    "FormationClass",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object formationClassValue = formationClassField?.GetValue(selectorItem);
                if (formationClassValue is DeploymentFormationClass fieldClass)
                {
                    deploymentClass = fieldClass;
                    return true;
                }

                PropertyInfo formationClassIntProperty = selectorItem.GetType().GetProperty(
                    "FormationClassInt",
                    BindingFlags.Instance | BindingFlags.Public);
                object formationClassIntValue = formationClassIntProperty?.GetValue(selectorItem, null);
                if (formationClassIntValue is int classInt &&
                    Enum.IsDefined(typeof(DeploymentFormationClass), classInt))
                {
                    deploymentClass = (DeploymentFormationClass)classInt;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void SetSelectorItemCanBeSelected(object selectorItem, bool canBeSelected)
        {
            if (selectorItem == null)
                return;

            try
            {
                PropertyInfo canBeSelectedProperty = selectorItem.GetType().GetProperty(
                    "CanBeSelected",
                    BindingFlags.Instance | BindingFlags.Public);
                if (canBeSelectedProperty == null || !canBeSelectedProperty.CanWrite)
                    return;

                object currentValue = canBeSelectedProperty.GetValue(selectorItem, null);
                if (currentValue is bool currentCanBeSelected && currentCanBeSelected == canBeSelected)
                    return;

                canBeSelectedProperty.SetValue(selectorItem, canBeSelected, null);
            }
            catch
            {
            }
        }

        private static void RemoveInvalidFormationClassSelectorItems(
            object itemList,
            List<object> invalidItems)
        {
            if (itemList == null || invalidItems == null || invalidItems.Count <= 0)
                return;

            try
            {
                Type itemListType = itemList.GetType();
                foreach (object invalidItem in invalidItems)
                {
                    if (invalidItem == null)
                        continue;

                    MethodInfo removeMethod = itemListType.GetMethod(
                        "Remove",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { invalidItem.GetType() },
                        null);
                    removeMethod?.Invoke(itemList, new[] { invalidItem });
                }
            }
            catch
            {
            }
        }

        private static void TrySetFormationClassSelectorIndex(
            OrderOfBattleFormationItemVM formationItem,
            int selectedIndex)
        {
            if (formationItem == null)
                return;

            try
            {
                object selector = GetFormationClassSelector(formationItem);
                PropertyInfo selectedIndexProperty = selector?.GetType().GetProperty(
                    "SelectedIndex",
                    BindingFlags.Instance | BindingFlags.Public);
                if (selectedIndexProperty == null || !selectedIndexProperty.CanWrite)
                    return;

                object currentValue = selectedIndexProperty.GetValue(selector, null);
                if (currentValue is int currentIndex && currentIndex == selectedIndex)
                    return;

                selectedIndexProperty.SetValue(selector, selectedIndex, null);
            }
            catch
            {
            }
        }

        private static object GetFormationClassSelector(OrderOfBattleFormationItemVM formationItem)
        {
            if (formationItem == null)
                return null;

            PropertyInfo selectorProperty = typeof(OrderOfBattleFormationItemVM).GetProperty(
                "FormationClassSelector",
                BindingFlags.Instance | BindingFlags.Public);
            return selectorProperty?.GetValue(formationItem, null);
        }

        private static void SetPrimaryClassWeight(OrderOfBattleFormationItemVM formationItem, int weight)
        {
            if (formationItem?.Classes == null)
                return;

            for (int i = 0; i < formationItem.Classes.Count; i++)
            {
                OrderOfBattleFormationClassVM classVm = formationItem.Classes[i];
                if (classVm == null)
                    continue;

                classVm.Weight = i == 0 ? weight : 0;
            }
        }

        private int CountProjectedUnitsInClass(FormationClass formationClass)
        {
            FormationClass projectedClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                return 0;

            int count = 0;
            foreach (OrderOfBattleFormationItemVM item in _allFormations.Where(item => item?.Formation != null))
            {
                count += CountProjectedUnitsInClass(item.Formation, projectedClass);
            }

            return count;
        }

        private static int CountProjectedUnitsInClass(Formation formation, FormationClass projectedClass)
        {
            if (formation == null ||
                projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
            {
                return 0;
            }

            return formation.GetCountOfUnitsWithCondition(agent =>
                ResolveProjectedSiegeAgentClass(agent) == projectedClass);
        }

        private static FormationClass ResolveProjectedSiegeAgentClass(Agent agent)
        {
            if (agent == null || agent.IsMount)
                return FormationClass.NumberOfAllFormations;

            if (!agent.HasMount && agent.IsRangedCached)
                return FormationClass.Ranged;

            FormationClass formationClass = FormationClass.NumberOfAllFormations;
            BasicCharacterObject character = agent.Character;
            if (character != null)
            {
                try
                {
                    BattleSideEnum side = agent.Team?.Side ?? BattleSideEnum.None;
                    if (Mission.Current != null && side != BattleSideEnum.None)
                        formationClass = Mission.Current.GetAgentTroopClass(side, character);
                    else
                        formationClass = character.DefaultFormationClass;
                }
                catch
                {
                    formationClass = character.DefaultFormationClass;
                }
            }

            if (formationClass == FormationClass.NumberOfAllFormations ||
                formationClass == FormationClass.NumberOfRegularFormations ||
                formationClass == FormationClass.Unset)
            {
                return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;
            }

            formationClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            if (formationClass == FormationClass.Ranged || formationClass == FormationClass.Infantry)
                return formationClass;

            return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;
        }

        private static FormationClass DismountSiegeFormationClass(FormationClass formationClass)
        {
            if (formationClass == FormationClass.Cavalry)
                return FormationClass.Infantry;

            if (formationClass == FormationClass.HorseArcher)
                return FormationClass.Ranged;

            return formationClass;
        }
    }
}
