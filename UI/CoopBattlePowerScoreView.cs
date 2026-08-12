using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CoopSpectator.UI
{
    public sealed class CoopBattlePowerScoreView : MissionView
    {
        private const string MovieName = "CoopBattlePowerScore";

        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private CoopBattlePowerScoreVM _viewModel;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 15;
            TryCreateLayer();
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || MissionScreen == null)
                return;
            if (_viewModel == null)
                TryCreateLayer();
            _viewModel?.Update(CoopBattlePowerNetworkController.CurrentClientState);
        }

        public override void OnMissionScreenFinalize()
        {
            ReleaseLayer();
            base.OnMissionScreenFinalize();
        }

        public override void OnPhotoModeActivated()
        {
            base.OnPhotoModeActivated();
            if (_layer != null)
                _layer.UIContext.ContextAlpha = 0f;
        }

        public override void OnPhotoModeDeactivated()
        {
            base.OnPhotoModeDeactivated();
            if (_layer != null)
                _layer.UIContext.ContextAlpha = 1f;
        }

        private void TryCreateLayer()
        {
            if (_viewModel != null || MissionScreen == null)
                return;

            try
            {
                _viewModel = new CoopBattlePowerScoreVM();
                _layer = new GauntletLayer("CoopBattlePowerScore", ViewOrderPriority, false);
                MissionScreen.AddLayer(_layer);
                _movie = _layer.LoadMovie(MovieName, _viewModel);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattlePowerScoreView: native power comparer initialization failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
                ReleaseLayer();
            }
        }

        private void ReleaseLayer()
        {
            try
            {
                if (_layer != null && _movie != null)
                    _layer.ReleaseMovie(_movie);
                if (_layer != null)
                    MissionScreen?.RemoveLayer(_layer);
                _viewModel?.OnFinalize();
            }
            catch
            {
            }
            finally
            {
                _movie = null;
                _layer = null;
                _viewModel = null;
            }
        }
    }

    internal sealed class CoopBattlePowerScoreVM : ViewModel
    {
        private bool _isVisible;
        private string _lastBattleInstanceId = string.Empty;
        private int _lastRevision = -1;

        internal CoopBattlePowerScoreVM()
        {
            PowerComparer = new CoopBattlePowerComparerVM();
            Attackers = new CoopBattlePowerSideVM();
            Defenders = new CoopBattlePowerSideVM();
            RefreshSideAppearance();
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            private set
            {
                if (value == _isVisible)
                    return;
                _isVisible = value;
                OnPropertyChangedWithValue(value, nameof(IsVisible));
            }
        }

        [DataSourceProperty]
        public CoopBattlePowerComparerVM PowerComparer { get; }

        [DataSourceProperty]
        public CoopBattlePowerSideVM Attackers { get; }

        [DataSourceProperty]
        public CoopBattlePowerSideVM Defenders { get; }

        internal void Update(CoopBattlePowerState state)
        {
            bool stateChanged = state != null &&
                (!string.Equals(
                     _lastBattleInstanceId,
                     state.BattleInstanceId,
                     StringComparison.Ordinal) ||
                 _lastRevision != state.Revision);
            if (stateChanged)
            {
                _lastBattleInstanceId = state.BattleInstanceId ?? string.Empty;
                _lastRevision = state.Revision;
                PowerComparer.Update(state);
                RefreshSideAppearance();
            }

            IsVisible =
                CoopBattlePowerContract.CanRender(state) &&
                !BannerlordConfig.HideBattleUI &&
                !MBCommon.IsPaused;
        }

        public override void OnFinalize()
        {
            PowerComparer.OnFinalize();
            Attackers.OnFinalize();
            Defenders.OnFinalize();
            base.OnFinalize();
        }

        private void RefreshSideAppearance()
        {
            RefreshSideAppearance(
                BattleSideEnum.Attacker,
                Mission.Current?.AttackerTeam,
                Attackers,
                isAttacker: true);
            RefreshSideAppearance(
                BattleSideEnum.Defender,
                Mission.Current?.DefenderTeam,
                Defenders,
                isAttacker: false);
        }

        private void RefreshSideAppearance(
            BattleSideEnum side,
            Team team,
            CoopBattlePowerSideVM target,
            bool isAttacker)
        {
            string bannerCode = BattleSnapshotRuntimeState.ResolveSideBannerCode(
                side,
                team?.Banner?.BannerCode);
            uint color = BattleSnapshotRuntimeState.ResolveSideColor(
                side,
                team?.Color ?? 0u);
            target.Update(bannerCode);
            string colorText = color != 0u
                ? Color.FromUint(color).ToString()
                : isAttacker
                    ? "#A0341EFF"
                    : "#5E8C23FF";
            if (isAttacker)
                PowerComparer.AttackerColor = colorText;
            else
                PowerComparer.DefenderColor = colorText;
        }
    }

    internal sealed class CoopBattlePowerComparerVM : ViewModel
    {
        private double _initialAttackerBattlePowerValue;
        private double _attackerBattlePowerValue;
        private double _initialDefenderBattlePowerValue;
        private double _defenderBattlePowerValue;
        private string _attackerColor = "#A0341EFF";
        private string _defenderColor = "#5E8C23FF";

        [DataSourceProperty]
        public double InitialAttackerBattlePowerValue
        {
            get => _initialAttackerBattlePowerValue;
            private set => SetField(
                ref _initialAttackerBattlePowerValue,
                value,
                nameof(InitialAttackerBattlePowerValue));
        }

        [DataSourceProperty]
        public double AttackerBattlePowerValue
        {
            get => _attackerBattlePowerValue;
            private set => SetField(
                ref _attackerBattlePowerValue,
                value,
                nameof(AttackerBattlePowerValue));
        }

        [DataSourceProperty]
        public double InitialDefenderBattlePowerValue
        {
            get => _initialDefenderBattlePowerValue;
            private set => SetField(
                ref _initialDefenderBattlePowerValue,
                value,
                nameof(InitialDefenderBattlePowerValue));
        }

        [DataSourceProperty]
        public double DefenderBattlePowerValue
        {
            get => _defenderBattlePowerValue;
            private set => SetField(
                ref _defenderBattlePowerValue,
                value,
                nameof(DefenderBattlePowerValue));
        }

        [DataSourceProperty]
        public string AttackerColor
        {
            get => _attackerColor;
            set => SetField(ref _attackerColor, value ?? string.Empty, nameof(AttackerColor));
        }

        [DataSourceProperty]
        public string DefenderColor
        {
            get => _defenderColor;
            set => SetField(ref _defenderColor, value ?? string.Empty, nameof(DefenderColor));
        }

        internal void Update(CoopBattlePowerState state)
        {
            InitialAttackerBattlePowerValue = Math.Max(0, state?.InitialAttackerPower ?? 0);
            AttackerBattlePowerValue = Math.Max(0, state?.CurrentAttackerPower ?? 0);
            InitialDefenderBattlePowerValue = Math.Max(0, state?.InitialDefenderPower ?? 0);
            DefenderBattlePowerValue = Math.Max(0, state?.CurrentDefenderPower ?? 0);
        }

        private void SetField(ref double field, double value, string propertyName)
        {
            if (Math.Abs(field - value) < 0.001d)
                return;
            field = value;
            OnPropertyChangedWithValue(value, propertyName);
        }

        private void SetField(ref string field, string value, string propertyName)
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
                return;
            field = value;
            OnPropertyChangedWithValue(value, propertyName);
        }
    }

    internal sealed class CoopBattlePowerSideVM : ViewModel
    {
        internal CoopBattlePowerSideVM()
        {
            BannerVisualSmall = new CoopBattlePowerBannerIdentifierVM();
        }

        [DataSourceProperty]
        public CoopBattlePowerBannerIdentifierVM BannerVisualSmall { get; }

        internal void Update(string bannerCode)
        {
            BannerVisualSmall.Update(bannerCode);
        }

        public override void OnFinalize()
        {
            BannerVisualSmall.OnFinalize();
            base.OnFinalize();
        }
    }

    internal sealed class CoopBattlePowerBannerIdentifierVM : ViewModel
    {
        private string _id = string.Empty;

        [DataSourceProperty]
        public string Id
        {
            get => _id;
            private set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_id, normalized, StringComparison.Ordinal))
                    return;
                _id = normalized;
                OnPropertyChangedWithValue(normalized, nameof(Id));
            }
        }

        [DataSourceProperty]
        public string AdditionalArgs => string.Empty;

        [DataSourceProperty]
        public string TextureProviderName => "BannerImageTextureProvider";

        internal void Update(string bannerCode)
        {
            Id = bannerCode;
        }
    }
}
