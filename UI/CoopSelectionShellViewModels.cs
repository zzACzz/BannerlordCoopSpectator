using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    public sealed class CoopTeamSelectionVM : ViewModel, ICoopSelectionScreenViewModel
    {
        private readonly Action<BattleSideEnum> _onSelectSide;
        private CoopTeamSelectionSideVM _team1;
        private CoopTeamSelectionSideVM _team2;
        private string _titleText = "TEAM SELECTION";
        private string _subtitleText = "Coop Battle";
        private string _statusText = string.Empty;
        private string _hintText = string.Empty;

        public CoopTeamSelectionVM(CoopSelectionUiSnapshot snapshot, Action<BattleSideEnum> onSelectSide)
        {
            _onSelectSide = onSelectSide;
            Refresh(snapshot, force: true);
        }

        [DataSourceProperty] public string TitleText { get => _titleText; private set => SetField(ref _titleText, value, nameof(TitleText)); }
        [DataSourceProperty] public string SubtitleText { get => _subtitleText; private set => SetField(ref _subtitleText, value, nameof(SubtitleText)); }
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string HintText { get => _hintText; private set => SetField(ref _hintText, value, nameof(HintText)); }
        [DataSourceProperty] public CoopTeamSelectionSideVM Team1 { get => _team1; private set => SetField(ref _team1, value, nameof(Team1)); }
        [DataSourceProperty] public CoopTeamSelectionSideVM Team2 { get => _team2; private set => SetField(ref _team2, value, nameof(Team2)); }

        public void Refresh(CoopSelectionUiSnapshot snapshot, bool force)
        {
            StatusText = CoopSelectionUiHelpers.BuildMissionSummaryText(snapshot);
            HintText = CoopSelectionUiHelpers.BuildTeamHintText(snapshot);
            Team1 = BuildSideVm(snapshot, BattleSideEnum.Attacker, snapshot?.AttackerSelectableEntryIds?.Length ?? 0);
            Team2 = BuildSideVm(snapshot, BattleSideEnum.Defender, snapshot?.DefenderSelectableEntryIds?.Length ?? 0);

            if (force)
            {
                OnPropertyChanged(nameof(Team1));
                OnPropertyChanged(nameof(Team2));
            }
        }

        private CoopTeamSelectionSideVM BuildSideVm(CoopSelectionUiSnapshot snapshot, BattleSideEnum side, int selectableCount)
        {
            return new CoopTeamSelectionSideVM(
                side,
                CoopSelectionUiHelpers.FormatSideLabel(side),
                CoopSelectionUiHelpers.BuildSideCountText(selectableCount),
                CoopSelectionUiHelpers.BuildSideDetailText(snapshot?.BattleState, side),
                selectableCount > 0,
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
            bool isEnabled,
            bool isSelected,
            Action<BattleSideEnum> onSelect)
        {
            _side = side;
            _onSelect = onSelect;
            TitleText = titleText;
            CountText = countText;
            DetailText = detailText;
            IsEnabled = isEnabled;
            IsSelected = isSelected;
            IsAttacker = side == BattleSideEnum.Attacker;
        }

        [DataSourceProperty] public string TitleText { get; }
        [DataSourceProperty] public string CountText { get; }
        [DataSourceProperty] public string DetailText { get; }
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
        private string _sideTitleText = "Select Unit";
        private string _statusText = string.Empty;
        private string _hintText = string.Empty;
        private string _emptyText = string.Empty;
        private string _selectedNameText = string.Empty;
        private string _selectedDetailText = string.Empty;
        private string _selectedSummaryText = string.Empty;
        private bool _isAttacker;
        private bool _showEmptyText = true;
        private bool _canSpawn;

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
        [DataSourceProperty] public string SideTitleText { get => _sideTitleText; private set => SetField(ref _sideTitleText, value, nameof(SideTitleText)); }
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string HintText { get => _hintText; private set => SetField(ref _hintText, value, nameof(HintText)); }
        [DataSourceProperty] public string EmptyText { get => _emptyText; private set => SetField(ref _emptyText, value, nameof(EmptyText)); }
        [DataSourceProperty] public string SelectedNameText { get => _selectedNameText; private set => SetField(ref _selectedNameText, value, nameof(SelectedNameText)); }
        [DataSourceProperty] public string SelectedDetailText { get => _selectedDetailText; private set => SetField(ref _selectedDetailText, value, nameof(SelectedDetailText)); }
        [DataSourceProperty] public string SelectedSummaryText { get => _selectedSummaryText; private set => SetField(ref _selectedSummaryText, value, nameof(SelectedSummaryText)); }
        [DataSourceProperty] public bool IsAttacker { get => _isAttacker; private set => SetField(ref _isAttacker, value, nameof(IsAttacker)); }
        [DataSourceProperty] public bool ShowEmptyText { get => _showEmptyText; private set => SetField(ref _showEmptyText, value, nameof(ShowEmptyText)); }
        [DataSourceProperty] public bool CanSpawn { get => _canSpawn; private set => SetField(ref _canSpawn, value, nameof(CanSpawn)); }

        public void Refresh(CoopSelectionUiSnapshot snapshot, bool force)
        {
            SideTitleText = CoopSelectionUiHelpers.FormatSideLabel(snapshot?.EffectiveSide ?? BattleSideEnum.None);
            StatusText = CoopSelectionUiHelpers.BuildStatusText(snapshot);
            HintText = CoopSelectionUiHelpers.BuildClassHintText(snapshot);
            EmptyText = CoopSelectionUiHelpers.BuildUnitEmptyText(snapshot);
            SelectedNameText = CoopSelectionUiHelpers.BuildSelectedNameText(snapshot);
            SelectedDetailText = CoopSelectionUiHelpers.BuildSelectedDetailText(snapshot);
            SelectedSummaryText = CoopSelectionUiHelpers.BuildSelectedSummaryText(snapshot);
            IsAttacker = snapshot?.EffectiveSide == BattleSideEnum.Attacker;
            CanSpawn = snapshot?.CanSpawn == true;

            Units = BuildUnitItems(snapshot);
            ShowEmptyText = Units.Count <= 0;

            if (force)
                OnPropertyChanged(nameof(Units));
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

        private MBBindingList<CoopClassLoadoutUnitVM> BuildUnitItems(CoopSelectionUiSnapshot snapshot)
        {
            var result = new MBBindingList<CoopClassLoadoutUnitVM>();
            if (snapshot == null || snapshot.EffectiveSide == BattleSideEnum.None)
                return result;

            foreach (string entryId in snapshot.EffectiveSelectableEntryIds ?? Array.Empty<string>())
            {
                RosterEntryState entryState = CoopSelectionUiHelpers.ResolveEntryState(snapshot.EffectiveSide, entryId);
                result.Add(new CoopClassLoadoutUnitVM(
                    snapshot.EffectiveSide,
                    entryId,
                    CoopSelectionUiHelpers.ResolveEntryDisplayName(entryState, entryId),
                    CoopSelectionUiHelpers.ResolveEntryDetailText(entryState),
                    CoopSelectionUiHelpers.ResolveEntrySummaryText(entryState),
                    string.Equals(entryId, snapshot.SelectedEntryId, StringComparison.OrdinalIgnoreCase),
                    _onSelectUnit));
            }

            return result;
        }
    }

    public sealed class CoopClassLoadoutUnitVM : ViewModel
    {
        private readonly BattleSideEnum _side;
        private readonly string _entryId;
        private readonly Action<BattleSideEnum, string> _onSelect;

        public CoopClassLoadoutUnitVM(
            BattleSideEnum side,
            string entryId,
            string nameText,
            string detailText,
            string summaryText,
            bool isSelected,
            Action<BattleSideEnum, string> onSelect)
        {
            _side = side;
            _entryId = entryId;
            _onSelect = onSelect;
            NameText = nameText;
            DetailText = detailText;
            SummaryText = summaryText;
            IsSelected = isSelected;
        }

        [DataSourceProperty] public string NameText { get; }
        [DataSourceProperty] public string DetailText { get; }
        [DataSourceProperty] public string SummaryText { get; }
        [DataSourceProperty] public bool IsSelected { get; }

        public void ExecuteSelect()
        {
            _onSelect?.Invoke(_side, _entryId);
        }
    }
}
