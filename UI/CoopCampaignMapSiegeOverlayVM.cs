using CoopSpectator.Infrastructure;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    // Detached bindings for the stock MapSiegeOverlay movie. No campaign models.
    public sealed class CoopCampaignMapSiegeOverlayVM : ViewModel
    {
        private CoopCampaignMapSiegeOverlay _state;
        [DataSourceProperty] public string PreparationTitleText => _state?.PreparationTitleText ?? string.Empty;
        [DataSourceProperty] public float PreparationProgress => _state?.PreparationProgress ?? 0;
        [DataSourceProperty] public bool IsPreparationsCompleted => _state?.IsPreparationsCompleted ?? true;
        [DataSourceProperty] public MBBindingList<CoopCampaignMapSiegePointVM> PointsOfInterest { get; } =
            new MBBindingList<CoopCampaignMapSiegePointVM>();
        [DataSourceProperty] public CoopCampaignMapSiegeProductionVM ProductionController { get; } =
            new CoopCampaignMapSiegeProductionVM();

        public void Apply(CoopCampaignMapSiegeOverlay state)
        {
            if (state == null || !state.TrySnapshot(out var snapshot)) { Clear(); return; }
            _state = snapshot;
            OnPropertyChanged(nameof(PreparationTitleText));
            OnPropertyChanged(nameof(PreparationProgress));
            OnPropertyChanged(nameof(IsPreparationsCompleted));
            // Native lists are depth-sorted each frame. Identity is not list order.
            var retained = new Dictionary<long, CoopCampaignMapSiegePointVM>();
            foreach (var point in PointsOfInterest) retained.Add(point.Identity, point);
            var live = new HashSet<long>();
            foreach (var point in snapshot.Points)
            {
                long key = ((long)point.Type << 32) | (uint)point.MachineIndex;
                live.Add(key);
                if (retained.TryGetValue(key, out var item)) item.Apply(point);
                else PointsOfInterest.Add(new CoopCampaignMapSiegePointVM(point));
            }
            for (int i = PointsOfInterest.Count - 1; i >= 0; i--)
                if (!live.Contains(PointsOfInterest[i].Identity))
                { PointsOfInterest[i].OnFinalize(); PointsOfInterest.RemoveAt(i); }
        }

        public void Update(Camera camera, bool inVisibleRange)
        {
            foreach (var point in PointsOfInterest) point.Update(camera, inVisibleRange);
            PointsOfInterest.Sort(DepthComparer.Instance);
        }

        private sealed class DepthComparer : IComparer<CoopCampaignMapSiegePointVM>
        {
            internal static readonly DepthComparer Instance = new DepthComparer();
            public int Compare(CoopCampaignMapSiegePointVM x, CoopCampaignMapSiegePointVM y)
            {
                int depth = y.LatestW.CompareTo(x.LatestW);
                return depth != 0 ? depth : x.Identity.CompareTo(y.Identity);
            }
        }

        private void Clear()
        {
            _state = null;
            foreach (var point in PointsOfInterest) point.OnFinalize();
            PointsOfInterest.Clear();
            OnPropertyChanged(nameof(PreparationTitleText));
            OnPropertyChanged(nameof(PreparationProgress));
            OnPropertyChanged(nameof(IsPreparationsCompleted));
        }
        public override void OnFinalize() { Clear(); ProductionController.OnFinalize(); base.OnFinalize(); }
    }

    // The stock widget expects this object even when production is unavailable.
    public sealed class CoopCampaignMapSiegeProductionVM : ViewModel
    {
        [DataSourceProperty] public bool IsEnabled => false;
        [DataSourceProperty] public MBBindingList<ViewModel> PossibleProductionMachines { get; } = new MBBindingList<ViewModel>();
    }

    public sealed class CoopCampaignMapSiegePointVM : ViewModel
    {
        private CoopCampaignMapSiegePoint _state;
        private Vec2 _position = new Vec2(-10000, -10000);
        private bool _inRange;
        public CoopCampaignMapSiegePointVM(CoopCampaignMapSiegePoint state) { _state = state.Clone(); }
        internal long Identity => ((long)_state.Type << 32) | (uint)_state.MachineIndex;
        internal float LatestW { get; private set; } = -1;
        [DataSourceProperty] public Vec2 Position => _position;
        [DataSourceProperty] public bool IsInVisibleRange => _inRange;
        [DataSourceProperty] public bool HasItem => _state.HasItem;
        [DataSourceProperty] public bool IsConstructing => _state.IsConstructing;
        [DataSourceProperty] public bool IsPlayerSidePOI => _state.IsPlayerSidePOI;
        [DataSourceProperty] public bool IsSelected => false; // No local production selection.
        [DataSourceProperty] public int MachineType => _state.MachineType;
        [DataSourceProperty] public int QueueIndex => _state.QueueIndex;
        [DataSourceProperty] public float CurrentHitpoints => _state.CurrentHitpoints;
        [DataSourceProperty] public float MaxHitpoints => _state.MaxHitpoints;
        [DataSourceProperty] public Color SidePrimaryColor => Color.FromUint(_state.PrimaryColor);
        [DataSourceProperty] public Color SideSecondaryColor => Color.FromUint(_state.SecondaryColor);
        public void ExecuteSelection() { }
        public void ExecuteShowTooltip() { }
        public void ExecuteHideTooltip() { }

        internal void Apply(CoopCampaignMapSiegePoint state)
        {
            _state = state.Clone();
            foreach (string property in new[] { nameof(HasItem), nameof(IsConstructing), nameof(IsPlayerSidePOI),
                nameof(MachineType), nameof(QueueIndex), nameof(CurrentHitpoints), nameof(MaxHitpoints),
                nameof(SidePrimaryColor), nameof(SideSecondaryColor) }) OnPropertyChanged(property);
        }
        internal void Update(Camera camera, bool inRange)
        {
            float x = -10000, y = -10000, w = -1;
            if (camera != null) MBWindowManager.WorldToScreenInsideUsableArea(camera,
                new Vec3(_state.X, _state.Y, _state.Z), ref x, ref y, ref w);
            bool valid = CoopCampaignMapSiegePoint.Finite(x) && CoopCampaignMapSiegePoint.Finite(y) &&
                CoopCampaignMapSiegePoint.Finite(w) && w > 0;
            LatestW = valid ? w : -1;
            var position = valid ? new Vec2(x, y) : new Vec2(-10000, -10000);
            if (_position != position) { _position = position; OnPropertyChanged(nameof(Position)); }
            bool visible = valid && inRange;
            if (_inRange != visible) { _inRange = visible; OnPropertyChanged(nameof(IsInVisibleRange)); }
        }
    }
}
