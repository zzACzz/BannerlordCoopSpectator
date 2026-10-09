using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class EscapeInputTests
{
    internal static void Run()
    {
        var policy = new CoopCampaignMapEscapeInputPolicy();
        Check(!policy.Tick(true, false), "idle must not open menu");
        for (int i = 0; i < 100; i++)
        {
            Check(!policy.Tick(true, true), "release must defer dispatch");
            Check(policy.Tick(true, false), "next owned frame dispatches once");
            Check(!policy.Tick(true, false), "no repeated dispatch");
            Check(!policy.Tick(false, true), "foreign focus cannot enqueue");
            Check(!policy.Tick(true, false), "foreign release cannot replay");
            Check(!policy.Tick(true, true), "enqueue before focus loss");
            Check(!policy.Tick(false, false), "dialogue/menu/focus loss cancels pending");
            Check(!policy.Tick(true, false), "return cannot replay cancelled release");
            Check(!policy.Tick(true, true), "enqueue before teardown");
            policy.Reset();
            Check(!policy.Tick(true, false), "new mission cannot replay old input");
        }
        string view = File.ReadAllText(Path.Combine("UI", "CoopCampaignMapPrototypeMissionView.cs"));
        Check(view.Contains("HandleReplicaEscapeInput();") &&
              view.Contains("_escapeInput.Reset();") &&
              view.Contains("HotKeyManager.GetCategory(\"GenericPanelGameKeyCategory\")"),
              "map must wire tick, teardown and native hotkey category");
        int start = view.IndexOf("private void HandleReplicaEscapeInput()", StringComparison.Ordinal);
        int end = view.IndexOf("private void InitializeLocalFreeCamera()", start, StringComparison.Ordinal);
        string handler = view.Substring(start, end - start);
        Check(handler.Contains("CanUseReplicaMapInput()") &&
              handler.Contains("_mapSceneReadyToRender") &&
              handler.Contains("_replicaInfoLayer.Input.IsHotKeyReleased(\"ToggleEscapeMenu\")") &&
              handler.Contains("MissionScreen.OnEscape();") &&
              !handler.Contains("GlobalInput") && !handler.Contains("QuitFrom"),
              "focused owned layer forwards only to native dispatcher, no global input/disconnect bypass");
        string dialogue = File.ReadAllText(Path.Combine("UI", "CoopCampaignMapConversationView.cs"));
        int tickStart = dialogue.IndexOf("public override void OnMissionScreenTick", StringComparison.Ordinal);
        int tickEnd = dialogue.IndexOf("private void HandleConversationEscapeInput", tickStart, StringComparison.Ordinal);
        string tick = dialogue.Substring(tickStart, tickEnd - tickStart);
        int menuGuard = tick.IndexOf("GetMissionBehavior<MissionEscapeMenuView>()?.IsActive", StringComparison.Ordinal);
        int blockedReturn = tick.IndexOf("return;", menuGuard, StringComparison.Ordinal);
        int pendingRead = tick.IndexOf("CoopCampaignMapConversationState pending = _pendingState", StringComparison.Ordinal);
        Check(menuGuard >= 0 && blockedReturn > menuGuard && pendingRead > blockedReturn &&
              tick.Contains("ReferenceEquals(ScreenManager.TopScreen, MissionScreen)") &&
              tick.Contains("InformationManager.IsAnyInquiryActive()") &&
              tick.Substring(menuGuard, blockedReturn - menuGuard).Contains("_escapeInput.Reset();"),
              "dialogue retains queued host changes and cancels pending Escape while a local modal owns the screen");
        Check(tick.Contains("HasActiveOptionsLayer() ||") &&
              tick.Contains("foreach (ScreenLayer layer in MissionScreen.Layers)") &&
              tick.Contains("!layer.IsFinalized && layer.IsActive") &&
              tick.Contains("string.Equals(layer.Name, \"MissionOptions\", StringComparison.Ordinal)"),
              "same-screen native options must defer dialogue even when escape menu is closed");
        string mode = File.ReadAllText(Path.Combine("GameMode", "MissionMultiplayerCoopCampaignMapPrototypeMode.cs"));
        int clientViews = mode.IndexOf("#if !COOPSPECTATOR_DEDICATED", StringComparison.Ordinal);
        int optionsView = mode.IndexOf("ViewCreator.CreateOptionsUIHandler()", StringComparison.Ordinal);
        int viewEnd = mode.IndexOf("#endif", clientViews, StringComparison.Ordinal);
        Check(clientViews >= 0 && optionsView > clientViews && optionsView < viewEnd &&
              mode.Substring(clientViews, optionsView - clientViews).Contains("if (!GameNetwork.IsServer)") &&
              mode.Contains("MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission)") &&
              mode.IndexOf("ViewCreator.CreateOptionsUIHandler()", optionsView + 1, StringComparison.Ordinal) < 0,
              "isolated map adds exactly one native options subscriber, client-only, beside its component");
        int handlerStart = dialogue.IndexOf("private void HandleConversationEscapeInput", StringComparison.Ordinal);
        int handlerEnd = dialogue.IndexOf("public override void OnMissionScreenFinalize", handlerStart, StringComparison.Ordinal);
        string dialogueHandler = dialogue.Substring(handlerStart, handlerEnd - handlerStart);
        Check(tick.Contains("HandleConversationEscapeInput();") &&
              dialogueHandler.Contains("_conversationLayer.IsActive") &&
              dialogueHandler.Contains("ReferenceEquals(ScreenManager.FocusedLayer, _conversationLayer)") &&
              dialogueHandler.Contains("_conversationLayer.Input.IsHotKeyReleased(\"ToggleEscapeMenu\")") &&
              dialogueHandler.Contains("_escapeInput.Tick(ownsInput, released)") &&
              dialogueHandler.Contains("MissionScreen.OnEscape();") &&
              !dialogueHandler.Contains("CloseConversation(") && !dialogueHandler.Contains("GlobalInput"),
              "dialogue forwards owned Escape to stock menu without closing or advancing host dialogue");
        int closeStart = dialogue.IndexOf("private void CloseConversation", StringComparison.Ordinal);
        int closeEnd = dialogue.IndexOf("private void RegisterConversationInputCategories", closeStart, StringComparison.Ordinal);
        Check(dialogue.Substring(closeStart, closeEnd - closeStart).Contains("_escapeInput.Reset();"),
              "dialogue replacement/close/finalize cannot replay a stale Escape");
        // Map and dialogue are separate input owners, not one shared pending flag.
        var conversationPolicy = new CoopCampaignMapEscapeInputPolicy();
        Check(!policy.Tick(true, true), "map release queued before conversation begins");
        Check(!policy.Tick(false, false), "conversation ownership cancels map release");
        Check(!conversationPolicy.Tick(true, false), "conversation cannot inherit map release");
        Check(!conversationPolicy.Tick(true, true), "conversation release deferred");
        Check(conversationPolicy.Tick(true, false), "conversation opens stock menu on next frame");
        Check(!conversationPolicy.Tick(false, true), "menu close release not queued by dialogue");
        Check(!conversationPolicy.Tick(true, false), "dialogue return does not reopen menu");
        Console.WriteLine("Escape input assertions passed: 1015");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
