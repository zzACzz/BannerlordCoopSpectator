using CoopSpectator.Infrastructure;
using SandBox.ViewModelCollection;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.UI
{
    // Stock movie bindings without native campaign models or event subscriptions.
    public sealed class CoopCampaignMapBattleMarkersVM : ViewModel
    {
        [DataSourceProperty]
        public MBBindingList<CoopCampaignMapBattleMarkerVM> MapEvents { get; } =
            new MBBindingList<CoopCampaignMapBattleMarkerVM>();

        public void Apply(CoopCampaignMapBattleMarker[] markers)
        {
            foreach (var item in MapEvents) item.OnFinalize();
            MapEvents.Clear();
            if (!CoopCampaignMapBattleMarkerPolicy.TrySnapshot(markers, out var snapshot)) return;
            foreach (var marker in snapshot) MapEvents.Add(new CoopCampaignMapBattleMarkerVM(marker));
        }

        public void Update(Camera camera)
        {
            foreach (var item in MapEvents) item.Update(camera);
        }

        public override void OnFinalize()
        {
            foreach (var item in MapEvents) item.OnFinalize();
            MapEvents.Clear();
            base.OnFinalize();
        }
    }

    public sealed class CoopCampaignMapBattleMarkerVM : ViewModel
    {
        private readonly CoopCampaignMapBattleMarker _marker;
        private Vec2 _position;
        private bool _visible;
        public CoopCampaignMapBattleMarkerVM(CoopCampaignMapBattleMarker marker) { _marker = marker; }
        [DataSourceProperty]
        public int EventType => (int)SandBoxUIHelper.MapEventVisualTypes.Battle;
        [DataSourceProperty]
        public Vec2 Position
        {
            get => _position;
            private set { if (_position != value) { _position = value; OnPropertyChangedWithValue(value, nameof(Position)); } }
        }
        [DataSourceProperty]
        public bool IsVisibleOnMap
        {
            get => _visible;
            private set { if (_visible != value) { _visible = value; OnPropertyChangedWithValue(value, nameof(IsVisibleOnMap)); } }
        }
        public void Update(Camera camera)
        {
            if (camera == null) { IsVisibleOnMap = false; return; }
            float x = 0, y = 0, w = 0;
            MBWindowManager.WorldToScreenInsideUsableArea(camera,
                new Vec3(_marker.X, _marker.Y, _marker.Z + CoopCampaignMapBattleMarkerPolicy.VerticalOffset),
                ref x, ref y, ref w);
            Position = new Vec2(x, y);
            IsVisibleOnMap = CoopCampaignMapBattleMarkerPolicy.Show(true, camera.Position.z, w);
        }
    }
}
