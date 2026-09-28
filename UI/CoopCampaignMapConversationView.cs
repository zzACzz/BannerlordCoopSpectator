using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed class CoopCampaignMapConversationView : MissionView
    {
        private static readonly MethodInfo ScreenLayerHandleActivateMethod =
            typeof(ScreenLayer).GetMethod(
                "HandleActivate",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        private static readonly MethodInfo ScreenLayerHandleDeactivateMethod =
            typeof(ScreenLayer).GetMethod(
                "HandleDeactivate",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

        private CoopCampaignMapConversationState _pendingState;
        private CoopCampaignMapConversationPose _pendingPose;
        private readonly System.Diagnostics.Stopwatch _poseAge = new System.Diagnostics.Stopwatch();
        private CoopCampaignMapConversationTableauView _tableau;
        private int _appliedRevision = -1;
        private GauntletLayer _conversationLayer;
        private GauntletMovieIdentifier _conversationMovie;
        private CoopCampaignMapConversationVM _conversationViewModel;
        private SpriteCategory _conversationSpriteCategory;
        private readonly List<MissionLayerActivationSnapshot>
            _missionLayerActivationSnapshots =
                new List<MissionLayerActivationSnapshot>();
        private ScreenLayer _focusedLayerBeforeConversation;
        private bool _missionLayerStateCaptured;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopCampaignMapPrototypeNetworkController.ClientConversationChanged +=
                OnClientConversationChanged;
            CoopCampaignMapPrototypeNetworkController.ClientConversationPoseChanged += OnClientConversationPoseChanged;
            if (CoopCampaignMapPrototypeNetworkController
                    .CurrentClientConversation != null)
            {
                _pendingState = CoopCampaignMapPrototypeNetworkController
                    .CurrentClientConversation.Clone();
            }
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 1;
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || MissionScreen == null)
                return;

            _tableau?.Tick(dt);

            CoopCampaignMapConversationState pending = _pendingState;
            if (pending != null && pending.Revision > _appliedRevision)
            {
                _pendingState = null;
                _appliedRevision = pending.Revision;
                if (pending.IsActive) ShowConversation(pending);
                else CloseConversation();
            }
            if (_pendingPose != null)
            {
                if (_poseAge.Elapsed.TotalSeconds > 0.5 || _pendingPose.ConversationRevision < _appliedRevision)
                    _pendingPose = null;
                else if (_pendingPose.ConversationRevision == _appliedRevision && _tableau != null &&
                    _tableau.ApplyPose(_pendingPose))
                    _pendingPose = null;
            }
        }

        public override void OnMissionScreenFinalize()
        {
            CoopCampaignMapPrototypeNetworkController.ClientConversationChanged -=
                OnClientConversationChanged;
            CoopCampaignMapPrototypeNetworkController.ClientConversationPoseChanged -= OnClientConversationPoseChanged;
            CloseConversation();
            _pendingState = null;
            _appliedRevision = -1;
            base.OnMissionScreenFinalize();
        }

        private void OnClientConversationChanged(
            CoopCampaignMapConversationState state)
        {
            if (state != null &&
                (_pendingState == null ||
                 state.Revision > _pendingState.Revision) &&
                state.Revision > _appliedRevision)
            {
                _pendingState = state.Clone();
            }
        }

        private void OnClientConversationPoseChanged(CoopCampaignMapConversationPose pose)
        {
            if (pose == null || pose.ConversationRevision < _appliedRevision) return;
            _pendingPose = pose.Clone();
            _poseAge.Restart();
        }

        private void ShowConversation(CoopCampaignMapConversationState state)
        {
            if (state == null || !state.IsActive || MissionScreen == null)
                return;

            CloseConversation(releaseTableau: false);
            CaptureMissionLayerState();
            try
            {
                _conversationViewModel =
                    new CoopCampaignMapConversationVM(state);
                _conversationSpriteCategory =
                    UIResourceManager.LoadSpriteCategory("ui_conversation");
                _conversationLayer = new GauntletLayer(
                    "MissionConversation",
                    ViewOrderPriority + 4,
                    false)
                {
                    IsFocusLayer = true
                };
                MissionScreen.AddLayer(_conversationLayer);
                _conversationMovie = _conversationLayer.LoadMovie(
                    "SPConversation",
                    _conversationViewModel);
                RegisterConversationInputCategories();
                _conversationLayer.InputRestrictions.SetInputRestrictions(
                    true,
                    InputUsageMask.All);
                _conversationLayer.InputRestrictions.SetMouseVisibility(true);
                MissionScreen.SetConversationActive(true);
                ((ScreenBase)MissionScreen)
                    .SetLayerCategoriesStateAndDeactivateOthers(
                        new[] { "MissionConversation", "SceneLayer", CoopCampaignMapConversationTableauView.LayerName },
                        true);
                if (_tableau == null)
                    _tableau = new CoopCampaignMapConversationTableauView(MissionScreen);
                _tableau.Apply(state.Tableau);
                _tableau.BindPose(state);
                ScreenManager.TrySetFocus(_conversationLayer);
                InformationManager.HideAllMessages();
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapConversationView: synchronized host dialogue failed to open.",
                    ex);
                CloseConversation();
            }
        }

        private void CloseConversation(bool releaseTableau = true)
        {
            if (releaseTableau)
            {
                _pendingPose = null;
                _poseAge.Reset();
                _tableau?.Dispose();
                _tableau = null;
            }
            if (_conversationViewModel == null &&
                _conversationLayer == null &&
                _conversationSpriteCategory == null &&
                !_missionLayerStateCaptured)
            {
                return;
            }

            try
            {
                // Removing the hovered widget need not dispatch HoverEnd.
                // Clear dialogue-owned hints before restoring the map layers.
                MBInformationManager.HideInformations();
                _conversationViewModel?.OnFinalize();
            }
            catch
            {
            }
            try
            {
                if (_conversationLayer != null)
                {
                    _conversationLayer.InputRestrictions.ResetInputRestrictions();
                    _conversationLayer.InputRestrictions.SetMouseVisibility(false);
                    _conversationLayer.IsFocusLayer = false;
                    ScreenManager.TryLoseFocus(_conversationLayer);
                    if (_conversationMovie != null)
                        _conversationLayer.ReleaseMovie(_conversationMovie);
                    MissionScreen?.RemoveLayer(_conversationLayer);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapConversationView: dialogue layer release failed. Error=" +
                    ex.Message + ".");
            }
            try
            {
                MissionScreen?.SetConversationActive(false);
                RestoreMissionLayerState();
            }
            catch
            {
            }
            try
            {
                _conversationSpriteCategory?.Unload();
            }
            catch
            {
            }
            _conversationMovie = null;
            _conversationLayer = null;
            _conversationViewModel = null;
            _conversationSpriteCategory = null;
        }

        private void RegisterConversationInputCategories()
        {
            if (_conversationLayer == null)
                return;

            GameKeyContext conversationCategory =
                HotKeyManager.GetCategory("ConversationHotKeyCategory");
            GameKeyContext genericPanelCategory =
                HotKeyManager.GetCategory("GenericPanelGameKeyCategory");
            if (conversationCategory != null)
                _conversationLayer.Input.RegisterHotKeyCategory(conversationCategory);
            if (genericPanelCategory != null)
                _conversationLayer.Input.RegisterHotKeyCategory(genericPanelCategory);
        }

        private void CaptureMissionLayerState()
        {
            _missionLayerActivationSnapshots.Clear();
            _focusedLayerBeforeConversation = null;
            _missionLayerStateCaptured = false;
            if (MissionScreen == null)
                return;

            ScreenBase missionScreen = MissionScreen;
            foreach (ScreenLayer layer in missionScreen.Layers)
            {
                if (layer != null)
                {
                    _missionLayerActivationSnapshots.Add(
                        new MissionLayerActivationSnapshot(
                            layer,
                            layer.IsActive));
                }
            }
            _focusedLayerBeforeConversation = ScreenManager.FocusedLayer;
            _missionLayerStateCaptured = true;
        }

        private void RestoreMissionLayerState()
        {
            if (!_missionLayerStateCaptured)
                return;

            ScreenBase missionScreen = MissionScreen;
            ScreenLayer previousFocusedLayer = _focusedLayerBeforeConversation;
            try
            {
                if (missionScreen != null)
                {
                    foreach (MissionLayerActivationSnapshot snapshot in
                             _missionLayerActivationSnapshots)
                    {
                        ScreenLayer layer = snapshot.Layer;
                        if (layer == null ||
                            layer.IsFinalized ||
                            !missionScreen.HasLayer(layer) ||
                            layer.IsActive == snapshot.WasActive)
                        {
                            continue;
                        }

                        MethodInfo transitionMethod = snapshot.WasActive
                            ? ScreenLayerHandleActivateMethod
                            : ScreenLayerHandleDeactivateMethod;
                        transitionMethod?.Invoke(layer, Array.Empty<object>());
                    }

                    if (previousFocusedLayer != null &&
                        !previousFocusedLayer.IsFinalized &&
                        previousFocusedLayer.IsActive &&
                        missionScreen.HasLayer(previousFocusedLayer))
                    {
                        ScreenLayer currentFocusedLayer =
                            ScreenManager.FocusedLayer;
                        if (currentFocusedLayer != null &&
                            !ReferenceEquals(
                                currentFocusedLayer,
                                previousFocusedLayer))
                        {
                            ScreenManager.TryLoseFocus(currentFocusedLayer);
                        }
                        ScreenManager.TrySetFocus(previousFocusedLayer);
                    }
                }
            }
            finally
            {
                _missionLayerActivationSnapshots.Clear();
                _focusedLayerBeforeConversation = null;
                _missionLayerStateCaptured = false;
            }
        }

        private sealed class MissionLayerActivationSnapshot
        {
            public MissionLayerActivationSnapshot(
                ScreenLayer layer,
                bool wasActive)
            {
                Layer = layer;
                WasActive = wasActive;
            }

            public ScreenLayer Layer { get; }

            public bool WasActive { get; }
        }
    }
}
