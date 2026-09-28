using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class ConversationVisualLifetimeTests
{
    private static int _count;
    internal static void Run()
    {
        var state = ConversationTableauTests.Fixture();
        for (int count = 1; count <= CoopCampaignMapConversationTableauCodec.MaxActors; count++)
        {
            int all = (1 << count) - 1;
            Check(CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, true, count, all), "target and all actor resources ready");
            Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(false, true, count, all), "drawing cannot replace target configuration");
            Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, false, count, all), "configuration cannot replace loaded target submission");
            for (int mask = 0; mask < all; mask++)
                Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, true, count, mask), "missing actor resources remain pending");
            Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, true, count, all | (1 << count)), "out-of-range resource mask rejected");
        }
        Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, true, 0, 0), "empty actor set rejected");
        Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, true, -1, -1), "negative actor count rejected");
        Check(!CoopCampaignMapConversationVisualLifetime.IsTargetOperational(true, true, 32, -1), "oversize actor count rejected before shift");
        var lifetime = new CoopCampaignMapConversationVisualLifetime();
        Check(lifetime.Apply(null) == CoopConversationVisualChange.Keep, "initial inactive");
        Check(lifetime.Apply(state) == CoopConversationVisualChange.Replace, "first scene");
        Check(!lifetime.Advance(0.5, false) && !lifetime.IsFailed, "asynchronous readiness");
        Check(lifetime.Apply(state.Clone()) == CoopConversationVisualChange.Keep, "text update preserves scene");
        Check(lifetime.Advance(0.5, true) && lifetime.IsReady, "ready");
        Check(lifetime.Apply(state.Clone()) == CoopConversationVisualChange.Keep && lifetime.IsReady, "ready preserved");
        lifetime.InvalidateRenderTarget();
        Check(!lifetime.IsReady && !lifetime.IsFailed && lifetime.Apply(state.Clone()) == CoopConversationVisualChange.Keep,
            "resize preserves scene but invalidates readiness");
        Check(!lifetime.Advance(0.5, false) && lifetime.Advance(0.5, true), "resized target becomes ready");
        state.Actors[0].ClothColor1++;
        Check(lifetime.Apply(state) == CoopConversationVisualChange.Replace && !lifetime.IsReady, "visual update replaces");
        Check(!lifetime.Advance(15, false) && lifetime.IsFailed, "bounded timeout");
        lifetime.InvalidateRenderTarget();
        Check(lifetime.IsFailed && !lifetime.IsReady, "resize cannot retry failed scene");
        Check(lifetime.Apply(state.Clone()) == CoopConversationVisualChange.Keep && lifetime.IsFailed, "no same-state native retry");
        Check(!lifetime.Advance(1, true), "failure is terminal for same scene");
        Check(lifetime.Apply(null) == CoopConversationVisualChange.Clear && !lifetime.IsFailed, "close clears failure");
        Check(lifetime.Apply(state) == CoopConversationVisualChange.Replace, "reopen allowed");
        Check(!lifetime.Advance(double.NaN, false) && lifetime.IsFailed, "invalid time");
        lifetime.Reset();
        Check(lifetime.Apply(state) == CoopConversationVisualChange.Replace, "mission reset");
        var invalid = state.Clone(); invalid.CameraFrame[0] = 0;
        Check(lifetime.Apply(invalid) == CoopConversationVisualChange.Clear, "invalid descriptor clears old scene");
        Check(lifetime.Apply(state) == CoopConversationVisualChange.Replace, "valid descriptor after invalid");
        lifetime.Fail();
        Check(lifetime.IsFailed && !lifetime.IsReady, "native exception failure");
        state.CameraFrame[9]++;
        Check(lifetime.Apply(state) == CoopConversationVisualChange.Replace && !lifetime.IsFailed, "new visual identity retries");

        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapConversationView.cs"));
        string tableau = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapConversationTableauView.cs"));
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapConversationTableauCapture.cs"));
        Check(capture.Contains("Scale = actor.GetScale()") && !capture.Contains("Scale = data.ScaleData") &&
            tableau.Contains(".Scale(descriptor.Scale)"), "resolved native scale preserved from host to client");
        Check(view.Contains("CloseConversation(releaseTableau: false)") && view.Contains("_tableau.Apply(state.Tableau)") &&
            view.Contains("_tableau?.Tick(dt)") && view.Contains("_tableau?.Dispose()"), "view lifetime wiring");
        Check(tableau.Contains("AgentVisuals.Create(") && !tableau.Contains("SpawnAgent(") &&
            !tableau.Contains("new MapConversationTableau(") && !tableau.Contains("ThumbnailCacheManager."), "visual-only isolated ownership");
        Check(tableau.Contains(".PrepareImmediately(true)") && !tableau.Contains(".PrepareImmediately(false)"),
            "bounded dialogue actors match stock face preparation without changing global loading");
        Check(!tableau.Contains("ForceLoadResources(") && !tableau.Contains("while (") &&
            tableau.Contains("_lifetime.Advance(") && tableau.Contains("_lifetime.IsFailed"), "no unbounded readiness loop");
        Check(tableau.Contains("CoopCampaignMapConversationTableauCodec.SceneName") &&
            tableau.Contains("GameNetwork.IsClient") && tableau.Contains("ResolveSceneOwner()"), "local known-scene admission");
        Check(!tableau.Contains("atmosphereFound") && tableau.Contains("GetCameraParamsFromCameraScript") &&
            tableau.Contains("Stopwatch") && tableau.Contains("Elapsed.TotalSeconds"), "native outdoor/camera and wall-clock readiness");
        Check(tableau.Contains("oldTexture?.Release()") && tableau.Contains("_scene?.ClearAll()") &&
            tableau.Contains("DestructAgentRendererSceneController") && tableau.Contains("ReleaseCameraEntity()"), "explicit cleanup owner");
        Check(!tableau.Contains(".ReadyToRender()") && tableau.Contains(".IsTargetOperational(") &&
            !tableau.Contains(".CheckSceneReadyToRender(") && !tableau.Contains(".PreloadForRendering("),
            "tableau avoids world-view readiness flag and terrain preparation");
        Check(!tableau.Contains("_scene.CheckResources(") &&
            tableau.Contains("visuals.CheckResources(true)") &&
            tableau.Contains("_targetConfigured, _widget.HasSubmittedLoadedTarget, _actors.Count, _lastActorReadyMask"),
            "terrain-disabled tableau avoids scene-wide request; target and every actor remain resource-gated");
        Check(tableau.Contains("HasSubmittedLoadedTarget |= Image.IsLoaded()") &&
            tableau.Contains("_widget.HasSubmittedLoadedTarget = false;") &&
            tableau.Contains("_targetConfigured = true;"), "loaded draw and configuration required anew on target resize");
        string creation = tableau.Substring(tableau.IndexOf("object epoch = _targetEpoch = new object();", StringComparison.Ordinal));
        creation = creation.Substring(0, creation.IndexOf("private void OnTextureUpdate", StringComparison.Ordinal));
        Check(creation.IndexOf("ConfigureTableau();", StringComparison.Ordinal) < creation.IndexOf("_targetConfigured = true;", StringComparison.Ordinal) &&
            creation.Contains("(sender, args) => OnTextureUpdate(epoch)"), "owned target configuration admits readiness without a second native callback");
        Check(tableau.Contains("TableauView.AddTableau(") && !tableau.Contains(": SceneLayer") &&
            tableau.Contains("new EngineTexture(_texture)"), "native off-screen target replaces world scene layer");
        Check(tableau.Contains("base(LayerName, 4, false)") && tableau.Contains("DoNotAcceptEvents = true") &&
            view.Contains("CoopCampaignMapConversationTableauView.LayerName"), "passive visual layer survives text updates below conversation");
        string widget = tableau.Substring(tableau.IndexOf("private sealed class TableauTextureWidget", StringComparison.Ordinal));
        widget = widget.Substring(0, widget.IndexOf("private sealed class ResolvedActor", StringComparison.Ordinal));
        Check(widget.Contains("WidthSizePolicy = SizePolicy.Fixed;") && widget.Contains("HeightSizePolicy = SizePolicy.Fixed;") &&
            !widget.Contains("SizePolicy.StretchToParent"), "direct context-root child must not inherit its scaled full-screen size");
        Check(widget.Contains("ScaledSuggestedWidth = Context.TwoDimensionContext.Width;") &&
            widget.Contains("ScaledSuggestedHeight = Context.TwoDimensionContext.Height;"), "viewport dimensions use stock movie physical-pixel sizing");
        Check(widget.Contains("protected override void OnUpdate(float dt)") &&
            widget.Replace("\r\n", "\n").Contains("base.OnUpdate(dt);\n                UpdateViewportSize();"),
            "viewport sizing follows resolution and UI-scale updates");
        Check(tableau.Contains("!ReferenceEquals(epoch, _targetEpoch)") &&
            tableau.Contains("_callbackFailure = ex.GetType().Name") &&
            tableau.IndexOf("_targetEpoch = null;", StringComparison.Ordinal) < tableau.IndexOf("oldTexture?.Release()", StringComparison.Ordinal) &&
            tableau.IndexOf("_texture = null;", StringComparison.Ordinal) < tableau.IndexOf("oldTexture?.Release()", StringComparison.Ordinal),
            "stale render callbacks rejected and errors deferred before release");
        string map = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        Check(map.Contains(".CheckSceneReadyToRender()"), "world-map terrain readiness remains intact");
        Check(tableau.Contains("_verboseAttempt = CoopDebugConfig.VerboseDiagnostics;") &&
            tableau.Contains("if (!_verboseAttempt) return;") &&
            tableau.Contains("if (_verboseAttempt)"), "load and failure observations require explicit verbose");
        string failure = tableau.Substring(tableau.IndexOf("private void Fail(string reason)", StringComparison.Ordinal));
        failure = failure.Substring(0, failure.IndexOf("private void ReleaseResources()", StringComparison.Ordinal));
        Check(!failure.Contains(".GetVisuals()") && !failure.Contains(".CheckResources(") &&
            !failure.Contains(".CheckSceneReadyToRender()"), "diagnostic failure path does not probe native state");
        Console.WriteLine("Conversation visual lifetime assertions passed: " + _count);
    }

    private static void Check(bool condition, string message)
    {
        _count++;
        if (!condition) throw new InvalidOperationException("Conversation visual lifetime: " + message);
    }
}
