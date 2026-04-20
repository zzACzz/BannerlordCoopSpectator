using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.Campaign
{
    [OverrideView(typeof(CoopDedicatedServerSettingsMapView))]
    public sealed class GauntletCoopDedicatedServerSettingsView : CoopDedicatedServerSettingsMapView
    {
        private CoopDedicatedServerSettingsVM _dataSource;
        private GauntletLayer _layerAsGauntletLayer;

        protected override void CreateLayout()
        {
            base.CreateLayout();

            _dataSource = new CoopDedicatedServerSettingsVM(OnClose);
            base.Layer = new GauntletLayer("MapCoopDedicatedSettings", 4402, false)
            {
                IsFocusLayer = true
            };

            _layerAsGauntletLayer = base.Layer as GauntletLayer;
            _layerAsGauntletLayer.LoadMovie("CoopDedicatedSettings", _dataSource);
            base.Layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            base.Layer.InputRestrictions.SetInputRestrictions(true, (InputUsageMask)7);
            ((ScreenBase)base.MapScreen).AddLayer(base.Layer);
            base.MapScreen.PauseAmbientSounds();
            ScreenManager.TrySetFocus(base.Layer);
        }

        protected override void OnIdleTick(float dt)
        {
            base.OnFrameTick(dt);
            if (base.Layer != null && base.Layer.Input.IsHotKeyReleased("Exit"))
                _dataSource?.ExecuteDone();
        }

        protected override void OnFinalize()
        {
            base.OnFinalize();

            _dataSource?.PersistCurrentSettingsSnapshot("GauntletCoopDedicatedServerSettingsView.OnFinalize");

            if (base.Layer != null)
            {
                base.Layer.InputRestrictions.ResetInputRestrictions();
                ((ScreenBase)base.MapScreen).RemoveLayer(base.Layer);
                base.MapScreen.RestartAmbientSounds();
                ScreenManager.TryLoseFocus(base.Layer);
                base.Layer = null;
            }

            _dataSource = null;
            _layerAsGauntletLayer = null;
        }

        protected override void OnMapConversationStart()
        {
            base.OnMapConversationStart();
            if (_layerAsGauntletLayer != null)
                ScreenManager.SetSuspendLayer(_layerAsGauntletLayer, true);
        }

        protected override void OnMapConversationOver()
        {
            base.OnMapConversationOver();
            if (_layerAsGauntletLayer != null)
                ScreenManager.SetSuspendLayer(_layerAsGauntletLayer, false);
        }

        private void OnClose()
        {
            base.MapScreen.RemoveMapView(this);
        }
    }
}
