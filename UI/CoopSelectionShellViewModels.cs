using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    public sealed class CoopTeamSelectionVM : ViewModel, ICoopSelectionScreenViewModel
    {
        private readonly Action<BattleSideEnum> _onSelectSide;
        private readonly Action _onAutoAssign;
        private readonly Action _onSpectator;
        private CoopTeamSelectionSideVM _team1;
        private CoopTeamSelectionSideVM _team2;
        private string _titleText = "TEAM SELECTION";
        private string _subtitleText = "Coop Battle";
        private string _statusText = string.Empty;
        private string _hintText = string.Empty;
        private bool _canAutoAssign;
        private bool _canSpectate = true;

        public CoopTeamSelectionVM(
            CoopSelectionUiSnapshot snapshot,
            Action<BattleSideEnum> onSelectSide,
            Action onAutoAssign,
            Action onSpectator)
        {
            _onSelectSide = onSelectSide;
            _onAutoAssign = onAutoAssign;
            _onSpectator = onSpectator;
            Refresh(snapshot, force: true);
        }

        [DataSourceProperty] public string TitleText { get => _titleText; private set => SetField(ref _titleText, value, nameof(TitleText)); }
        [DataSourceProperty] public string SubtitleText { get => _subtitleText; private set => SetField(ref _subtitleText, value, nameof(SubtitleText)); }
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string HintText { get => _hintText; private set => SetField(ref _hintText, value, nameof(HintText)); }
        [DataSourceProperty] public bool CanAutoAssign { get => _canAutoAssign; private set => SetField(ref _canAutoAssign, value, nameof(CanAutoAssign)); }
        [DataSourceProperty] public bool CanSpectate { get => _canSpectate; private set => SetField(ref _canSpectate, value, nameof(CanSpectate)); }
        [DataSourceProperty] public CoopTeamSelectionSideVM Team1 { get => _team1; private set => SetField(ref _team1, value, nameof(Team1)); }
        [DataSourceProperty] public CoopTeamSelectionSideVM Team2 { get => _team2; private set => SetField(ref _team2, value, nameof(Team2)); }

        public void Refresh(CoopSelectionUiSnapshot snapshot, bool force)
        {
            StatusText = CoopSelectionUiHelpers.BuildTeamStatusText(snapshot);
            HintText = CoopSelectionUiHelpers.BuildTeamHintText(snapshot);
            Team1 = BuildSideVm(snapshot, BattleSideEnum.Attacker, snapshot?.AttackerSelectableEntryIds?.Length ?? 0);
            Team2 = BuildSideVm(snapshot, BattleSideEnum.Defender, snapshot?.DefenderSelectableEntryIds?.Length ?? 0);
            CanAutoAssign = (Team1?.IsEnabled == true) || (Team2?.IsEnabled == true);
            CanSpectate = true;

            if (force)
            {
                OnPropertyChanged(nameof(Team1));
                OnPropertyChanged(nameof(Team2));
            }
        }

        public void ExecuteAutoAssign()
        {
            if (CanAutoAssign)
                _onAutoAssign?.Invoke();
        }

        public void ExecuteSpectator()
        {
            if (CanSpectate)
                _onSpectator?.Invoke();
        }

        private CoopTeamSelectionSideVM BuildSideVm(CoopSelectionUiSnapshot snapshot, BattleSideEnum side, int selectableCount)
        {
            CoopSidePresentation presentation = CoopSelectionUiHelpers.ResolveSidePresentation(snapshot, side, selectableCount);
            return new CoopTeamSelectionSideVM(
                side,
                presentation.TitleText,
                presentation.CountText,
                presentation.DetailText,
                presentation.BannerCodeText,
                CoopSelectionUiHelpers.CanSelectSide(snapshot, side, selectableCount),
                snapshot?.EffectiveSide == side,
                _onSelectSide);
        }
    }

    public sealed class CoopTeamSelectionSideVM : ViewModel
    {
        private readonly BattleSideEnum _side;
        private readonly Action<BattleSideEnum> _onSelect;

        public CoopTeamSelectionSideVM(
            BattleSideEnum side,
            string titleText,
            string countText,
            string detailText,
            string bannerCodeText,
            bool isEnabled,
            bool isSelected,
            Action<BattleSideEnum> onSelect)
        {
            _side = side;
            _onSelect = onSelect;
            TitleText = titleText;
            CountText = countText;
            DetailText = detailText;
            BannerCodeText = bannerCodeText;
            IsEnabled = isEnabled;
            IsSelected = isSelected;
            IsAttacker = side == BattleSideEnum.Attacker;
        }

        [DataSourceProperty] public string TitleText { get; }
        [DataSourceProperty] public string CountText { get; }
        [DataSourceProperty] public string DetailText { get; }
        [DataSourceProperty] public string BannerCodeText { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
        [DataSourceProperty] public bool IsSelected { get; }
        [DataSourceProperty] public bool IsAttacker { get; }

        public void ExecuteSelect()
        {
            if (IsEnabled)
                _onSelect?.Invoke(_side);
        }
    }

    public sealed class CoopClassLoadoutVM : ViewModel, ICoopSelectionScreenViewModel
    {
        private readonly Action<BattleSideEnum, string> _onSelectUnit;
        private readonly Action _onSpawn;
        private readonly Action _onBack;
        private MBBindingList<CoopClassLoadoutUnitVM> _units = new MBBindingList<CoopClassLoadoutUnitVM>();
        private CoopCharacterVisualVM _selectedVisual = new CoopCharacterVisualVM(CoopCharacterVisualData.Empty);
        private string _sideTitleText = "Select Unit";
        private string _statusText = string.Empty;
        private string _hintText = string.Empty;
        private string _emptyText = string.Empty;
        private string _selectedNameText = string.Empty;
        private string _selectedDetailText = string.Empty;
        private string _selectedSummaryText = string.Empty;
        private string _selectedCommanderBadgeText = string.Empty;
        private bool _isAttacker;
        private bool _showEmptyText = true;
        private bool _canSpawn;
        private bool _showSelectedCommanderBadge;
        private string _lastUnitListSignature = string.Empty;
        private string _lastSelectedEntryId = string.Empty;
        private string _lastSelectedVisualSignature = string.Empty;

        public CoopClassLoadoutVM(
            CoopSelectionUiSnapshot snapshot,
            Action<BattleSideEnum, string> onSelectUnit,
            Action onSpawn,
            Action onBack)
        {
            _onSelectUnit = onSelectUnit;
            _onSpawn = onSpawn;
            _onBack = onBack;
            Refresh(snapshot, force: true);
        }

        [DataSourceProperty] public MBBindingList<CoopClassLoadoutUnitVM> Units { get => _units; private set => SetField(ref _units, value, nameof(Units)); }
        [DataSourceProperty] public CoopCharacterVisualVM SelectedVisual { get => _selectedVisual; private set => SetField(ref _selectedVisual, value, nameof(SelectedVisual)); }
        [DataSourceProperty] public string SideTitleText { get => _sideTitleText; private set => SetField(ref _sideTitleText, value, nameof(SideTitleText)); }
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string HintText { get => _hintText; private set => SetField(ref _hintText, value, nameof(HintText)); }
        [DataSourceProperty] public string EmptyText { get => _emptyText; private set => SetField(ref _emptyText, value, nameof(EmptyText)); }
        [DataSourceProperty] public string SelectedNameText { get => _selectedNameText; private set => SetField(ref _selectedNameText, value, nameof(SelectedNameText)); }
        [DataSourceProperty] public string SelectedDetailText { get => _selectedDetailText; private set => SetField(ref _selectedDetailText, value, nameof(SelectedDetailText)); }
        [DataSourceProperty] public string SelectedSummaryText { get => _selectedSummaryText; private set => SetField(ref _selectedSummaryText, value, nameof(SelectedSummaryText)); }
        [DataSourceProperty] public string SelectedCommanderBadgeText { get => _selectedCommanderBadgeText; private set => SetField(ref _selectedCommanderBadgeText, value, nameof(SelectedCommanderBadgeText)); }
        [DataSourceProperty] public bool IsAttacker { get => _isAttacker; private set => SetField(ref _isAttacker, value, nameof(IsAttacker)); }
        [DataSourceProperty] public bool ShowEmptyText { get => _showEmptyText; private set => SetField(ref _showEmptyText, value, nameof(ShowEmptyText)); }
        [DataSourceProperty] public bool CanSpawn { get => _canSpawn; private set => SetField(ref _canSpawn, value, nameof(CanSpawn)); }
        [DataSourceProperty] public bool ShowSelectedCommanderBadge { get => _showSelectedCommanderBadge; private set => SetField(ref _showSelectedCommanderBadge, value, nameof(ShowSelectedCommanderBadge)); }

        public void Refresh(CoopSelectionUiSnapshot snapshot, bool force)
        {
            string[] orderedEntryIds =
                snapshot == null || snapshot.EffectiveSide == BattleSideEnum.None
                    ? Array.Empty<string>()
                    : CoopSelectionUiHelpers.OrderSelectableEntryIdsForDisplay(snapshot);
            SideTitleText = CoopSelectionUiHelpers.ResolveSideDisplayName(snapshot?.BattleState, snapshot?.EffectiveSide ?? BattleSideEnum.None);
            StatusText = CoopSelectionUiHelpers.BuildStatusText(snapshot);
            HintText = CoopSelectionUiHelpers.BuildClassHintText(snapshot);
            EmptyText = CoopSelectionUiHelpers.BuildUnitEmptyText(snapshot);
            SelectedNameText = CoopSelectionUiHelpers.BuildSelectedNameText(snapshot);
            SelectedDetailText = CoopSelectionUiHelpers.BuildSelectedDetailText(snapshot);
            SelectedSummaryText = CoopSelectionUiHelpers.BuildSelectedSummaryText(snapshot);
            SelectedCommanderBadgeText = CoopSelectionUiHelpers.ResolveCommanderBadgeText(snapshot, snapshot?.SelectedEntryId);
            ShowSelectedCommanderBadge = !string.IsNullOrWhiteSpace(SelectedCommanderBadgeText);
            RefreshSelectedVisual(snapshot);
            IsAttacker = snapshot?.EffectiveSide == BattleSideEnum.Attacker;
            CanSpawn = snapshot?.CanSpawn == true;
            string selectedEntryId = snapshot?.SelectedEntryId ?? string.Empty;
            string unitListSignature = BuildUnitListSignature(snapshot, orderedEntryIds);
            bool unitListChanged = force || !string.Equals(_lastUnitListSignature, unitListSignature, StringComparison.Ordinal);
            bool selectionChanged = force || !string.Equals(_lastSelectedEntryId, selectedEntryId, StringComparison.Ordinal);
            if (unitListChanged)
            {
                RefreshUnitItems(snapshot, orderedEntryIds);
                _lastUnitListSignature = unitListSignature;
            }
            else if (selectionChanged)
            {
                RefreshUnitSelectionState(selectedEntryId);
            }

            _lastSelectedEntryId = selectedEntryId;
            ShowEmptyText = orderedEntryIds.Length <= 0;
        }

        public void ExecuteSpawn()
        {
            if (CanSpawn)
                _onSpawn?.Invoke();
        }

        public void ExecuteBack()
        {
            _onBack?.Invoke();
        }

        private void RefreshSelectedVisual(CoopSelectionUiSnapshot snapshot)
        {
            CoopCharacterVisualData visualData = CoopSelectionUiHelpers.BuildSelectedVisualData(snapshot);
            string visualSignature = BuildVisualSignature(visualData);
            if (SelectedVisual == null || !string.Equals(_lastSelectedVisualSignature, visualSignature, StringComparison.Ordinal))
            {
                SelectedVisual = new CoopCharacterVisualVM(visualData);
                _lastSelectedVisualSignature = visualSignature;
                return;
            }

            if (SelectedVisual != null)
                SelectedVisual.Refresh(visualData);
        }

        private void RefreshUnitItems(CoopSelectionUiSnapshot snapshot, IReadOnlyList<string> orderedEntryIds)
        {
            if (HasSameUnitOrder(orderedEntryIds))
            {
                for (int index = 0; index < orderedEntryIds.Count; index++)
                    RefreshUnitVm(Units[index], snapshot, orderedEntryIds[index]);
                return;
            }
            if (Units == null)
                Units = new MBBindingList<CoopClassLoadoutUnitVM>();

            var desiredEntryIds = new HashSet<string>(orderedEntryIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (int index = Units.Count - 1; index >= 0; index--)
            {
                CoopClassLoadoutUnitVM existingUnit = Units[index];
                if (existingUnit != null && desiredEntryIds.Contains(existingUnit.EntryId))
                    continue;

                Units.RemoveAt(index);
            }

            for (int desiredIndex = 0; desiredIndex < orderedEntryIds.Count; desiredIndex++)
            {
                string entryId = orderedEntryIds[desiredIndex];
                int currentIndex = FindUnitIndex(entryId);
                if (currentIndex < 0)
                {
                    Units.Insert(desiredIndex, CreateUnitVm(snapshot, entryId));
                    continue;
                }

                CoopClassLoadoutUnitVM unitVm = Units[currentIndex];
                if (currentIndex != desiredIndex)
                {
                    Units.RemoveAt(currentIndex);
                    Units.Insert(desiredIndex, unitVm);
                }

                RefreshUnitVm(unitVm, snapshot, entryId);
            }
        }

        private void RefreshUnitSelectionState(string selectedEntryId)
        {
            if (Units == null || Units.Count <= 0)
                return;

            for (int index = 0; index < Units.Count; index++)
            {
                CoopClassLoadoutUnitVM unitVm = Units[index];
                if (unitVm == null)
                    continue;

                unitVm.RefreshSelection(string.Equals(unitVm.EntryId, selectedEntryId, StringComparison.OrdinalIgnoreCase));
            }
        }

        private bool HasSameUnitOrder(IReadOnlyList<string> orderedEntryIds)
        {
            if (Units == null)
                return orderedEntryIds == null || orderedEntryIds.Count == 0;

            if ((orderedEntryIds?.Count ?? 0) != Units.Count)
                return false;

            for (int index = 0; index < Units.Count; index++)
            {
                if (!string.Equals(Units[index]?.EntryId, orderedEntryIds[index], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private int FindUnitIndex(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId) || Units == null)
                return -1;

            for (int index = 0; index < Units.Count; index++)
            {
                if (string.Equals(Units[index]?.EntryId, entryId, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return -1;
        }

        private CoopClassLoadoutUnitVM CreateUnitVm(CoopSelectionUiSnapshot snapshot, string entryId)
        {
            ResolveUnitPresentation(snapshot, entryId, out string nameText, out string iconType, out string commanderBadgeText, out bool isSelected);
            return new CoopClassLoadoutUnitVM(
                snapshot?.EffectiveSide ?? BattleSideEnum.None,
                entryId,
                nameText,
                iconType,
                commanderBadgeText,
                isSelected,
                _onSelectUnit);
        }

        private static void RefreshUnitVm(CoopClassLoadoutUnitVM unitVm, CoopSelectionUiSnapshot snapshot, string entryId)
        {
            if (unitVm == null)
                return;

            ResolveUnitPresentation(snapshot, entryId, out string nameText, out string iconType, out string commanderBadgeText, out bool isSelected);
            unitVm.Refresh(nameText, iconType, commanderBadgeText, isSelected);
        }

        private static void ResolveUnitPresentation(
            CoopSelectionUiSnapshot snapshot,
            string entryId,
            out string nameText,
            out string iconType,
            out string commanderBadgeText,
            out bool isSelected)
        {
            RosterEntryState entryState = CoopSelectionUiHelpers.ResolveEntryState(snapshot?.EffectiveSide ?? BattleSideEnum.None, entryId);
            nameText = CoopSelectionUiHelpers.ResolveEntryDisplayName(entryState, entryId);
            iconType = CoopSelectionUiHelpers.ResolveEntryIconType(entryState);
            commanderBadgeText = CoopSelectionUiHelpers.ResolveCommanderBadgeText(snapshot, entryId);
            isSelected = string.Equals(entryId, snapshot?.SelectedEntryId, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildUnitListSignature(CoopSelectionUiSnapshot snapshot, IReadOnlyList<string> orderedEntryIds)
        {
            if (snapshot == null || orderedEntryIds == null || orderedEntryIds.Count <= 0)
                return string.Empty;

            var parts = new List<string>(orderedEntryIds.Count);
            foreach (string entryId in orderedEntryIds)
            {
                RosterEntryState entryState = CoopSelectionUiHelpers.ResolveEntryState(snapshot.EffectiveSide, entryId);
                parts.Add(string.Join("|", new[]
                {
                    entryId ?? string.Empty,
                    CoopSelectionUiHelpers.ResolveEntryDisplayName(entryState, entryId),
                    CoopSelectionUiHelpers.ResolveEntryIconType(entryState),
                    CoopSelectionUiHelpers.ResolveCommanderBadgeText(snapshot, entryId)
                }));
            }

            return string.Join("\n", parts);
        }

        private static string BuildVisualSignature(CoopCharacterVisualData visualData)
        {
            CoopCharacterVisualData source = visualData ?? CoopCharacterVisualData.Empty;
            return string.Join("|", new[]
            {
                source.HasVisual.ToString(),
                source.BannerCodeText ?? string.Empty,
                source.BodyProperties ?? string.Empty,
                source.CharStringId ?? string.Empty,
                source.Race.ToString(),
                source.EquipmentCode ?? string.Empty,
                source.IsFemale.ToString(),
                source.MountCreationKey ?? string.Empty,
                source.StanceIndex.ToString(),
                source.ArmorColor1.ToString(),
                source.ArmorColor2.ToString()
            });
        }
    }

    public sealed class CoopCharacterVisualVM : ViewModel
    {
        private bool _hasVisual;
        private string _bannerCodeText = string.Empty;
        private string _bodyProperties = string.Empty;
        private string _charStringId = string.Empty;
        private int _race;
        private string _equipmentCode = string.Empty;
        private bool _isFemale;
        private string _mountCreationKey = string.Empty;
        private int _stanceIndex;
        private uint _armorColor1 = 0xFFFFFFFFu;
        private uint _armorColor2 = 0xFFFFFFFFu;

        internal CoopCharacterVisualVM(CoopCharacterVisualData data)
        {
            Refresh(data);
        }

        [DataSourceProperty] public bool HasVisual { get => _hasVisual; private set => SetField(ref _hasVisual, value, nameof(HasVisual)); }
        [DataSourceProperty] public string BannerCodeText { get => _bannerCodeText; private set => SetField(ref _bannerCodeText, value, nameof(BannerCodeText)); }
        [DataSourceProperty] public string BodyProperties { get => _bodyProperties; private set => SetField(ref _bodyProperties, value, nameof(BodyProperties)); }
        [DataSourceProperty] public string CharStringId { get => _charStringId; private set => SetField(ref _charStringId, value, nameof(CharStringId)); }
        [DataSourceProperty] public int Race { get => _race; private set => SetField(ref _race, value, nameof(Race)); }
        [DataSourceProperty] public string EquipmentCode { get => _equipmentCode; private set => SetField(ref _equipmentCode, value, nameof(EquipmentCode)); }
        [DataSourceProperty] public bool IsFemale { get => _isFemale; private set => SetField(ref _isFemale, value, nameof(IsFemale)); }
        [DataSourceProperty] public string MountCreationKey { get => _mountCreationKey; private set => SetField(ref _mountCreationKey, value, nameof(MountCreationKey)); }
        [DataSourceProperty] public int StanceIndex { get => _stanceIndex; private set => SetField(ref _stanceIndex, value, nameof(StanceIndex)); }
        [DataSourceProperty] public uint ArmorColor1 { get => _armorColor1; private set => SetField(ref _armorColor1, value, nameof(ArmorColor1)); }
        [DataSourceProperty] public uint ArmorColor2 { get => _armorColor2; private set => SetField(ref _armorColor2, value, nameof(ArmorColor2)); }

        internal void Refresh(CoopCharacterVisualData data)
        {
            CoopCharacterVisualData source = data ?? CoopCharacterVisualData.Empty;
            HasVisual = source.HasVisual;
            BannerCodeText = source.BannerCodeText;
            BodyProperties = source.BodyProperties;
            CharStringId = source.CharStringId;
            Race = source.Race;
            EquipmentCode = source.EquipmentCode;
            IsFemale = source.IsFemale;
            MountCreationKey = source.MountCreationKey;
            StanceIndex = source.StanceIndex;
            ArmorColor1 = source.ArmorColor1;
            ArmorColor2 = source.ArmorColor2;
        }
    }

    public sealed class CoopClassLoadoutUnitVM : ViewModel
    {
        private readonly BattleSideEnum _side;
        private readonly string _entryId;
        private readonly Action<BattleSideEnum, string> _onSelect;
        private string _nameText = string.Empty;
        private string _iconType = string.Empty;
        private string _commanderBadgeText = string.Empty;
        private bool _showCommanderBadge;
        private bool _isSelected;

        public CoopClassLoadoutUnitVM(
            BattleSideEnum side,
            string entryId,
            string nameText,
            string iconType,
            string commanderBadgeText,
            bool isSelected,
            Action<BattleSideEnum, string> onSelect)
        {
            _side = side;
            _entryId = entryId;
            _onSelect = onSelect;
            Refresh(nameText, iconType, commanderBadgeText, isSelected);
        }

        internal string EntryId => _entryId;

        [DataSourceProperty] public string NameText { get => _nameText; private set => SetField(ref _nameText, value, nameof(NameText)); }
        [DataSourceProperty] public string IconType { get => _iconType; private set => SetField(ref _iconType, value, nameof(IconType)); }
        [DataSourceProperty] public string CommanderBadgeText { get => _commanderBadgeText; private set => SetField(ref _commanderBadgeText, value, nameof(CommanderBadgeText)); }
        [DataSourceProperty] public bool ShowCommanderBadge { get => _showCommanderBadge; private set => SetField(ref _showCommanderBadge, value, nameof(ShowCommanderBadge)); }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; private set => SetField(ref _isSelected, value, nameof(IsSelected)); }

        public void ExecuteSelect()
        {
            _onSelect?.Invoke(_side, _entryId);
        }

        internal void Refresh(string nameText, string iconType, string commanderBadgeText, bool isSelected)
        {
            NameText = nameText ?? string.Empty;
            IconType = iconType ?? string.Empty;
            CommanderBadgeText = commanderBadgeText ?? string.Empty;
            ShowCommanderBadge = !string.IsNullOrWhiteSpace(CommanderBadgeText);
            IsSelected = isSelected;
        }

        internal void RefreshSelection(bool isSelected)
        {
            IsSelected = isSelected;
        }
    }
}
