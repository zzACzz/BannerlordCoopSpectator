using System;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class MapBarPresentationTests
{
    internal static void Run()
    {
        var bar = new CoopCampaignMapBarPresentation { Date = "Весна 1, 1084", Time = 23.99, TimeFlowState = 6, TutorialElementId = "PartySpeedLabel" };
        bar.PrimaryInfoItems.Add(new CoopCampaignMapBarInfo { ItemId = "gold", HintToken = "barhint:" + Guid.NewGuid().ToString("N"), VisualId = "gold", Value = "1.2M", IntValue = 1200000 });
        bar.SecondaryInfoItems.Add(new CoopCampaignMapBarInfo { ItemId = "speed", HintToken = "barhint:" + Guid.NewGuid().ToString("N"), VisualId = "speed_at_sea", Value = "3.1", HasWarning = true });
        bar.NavigationItems.Add(new CoopCampaignMapBarNavigation { ItemId = "Party", HasAlert = true, AlertText = "2" });
        Check(bar.IsValid(), "native-shaped data");
        string controlToken = "barhint:" + Guid.NewGuid().ToString("N");
        bar.ControlHints.Add(new CoopCampaignMapBarControlTarget
        { Key = "Pause", HintToken = controlToken, IsAvailable = true });
        var controls = bar.Clone();
        controls.IsEnabled = true;
        controls.IsInfoBarEnabled = false;
        Check(CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(controls, controlToken), "control independent of statistics visibility");
        controls.ControlHints[0].IsAvailable = false;
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(controls, controlToken), "unavailable control revoked");
        Check(bar.ControlHints[0].IsAvailable, "control deep clone");
        controls.ControlHints[0].IsAvailable = true;
        controls.IsEnabled = false;
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(controls, controlToken), "hidden bar revokes control");
        controls.IsEnabled = true;
        controls.PrimaryInfoItems[0].HintToken = controlToken;
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(controls, controlToken), "cross-list collision rejected even if statistics hidden");
        controls = bar.Clone(); controls.ControlHints.Add(controls.ControlHints[0].Clone());
        Check(!controls.IsValid(), "duplicate control key rejected");
        controls.ControlHints[1].Key = "Play"; controls.IsEnabled = true;
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(controls, controlToken), "control token collision rejected");
        controls = bar.Clone(); controls.ControlHints.Clear();
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(controls, controlToken), "removed control revoked");
        controls.ControlHints = null;
        Check(!controls.IsValid(), "missing controls rejected");
        controls = bar.Clone(); controls.ControlHints[0].Key = "../Pause";
        Check(!controls.IsValid(), "control path key rejected");
        controls = bar.Clone(); controls.ControlHints[0].HintToken = "Pause";
        Check(!controls.IsValid(), "control requires opaque identity");
        controls = bar.Clone(); controls.ControlHints.Clear();
        for (int i = 0; i <= CoopCampaignMapBarPresentation.MaxControlHints; i++)
            controls.ControlHints.Add(new CoopCampaignMapBarControlTarget
            { Key = "control_" + i, HintToken = "barhint:" + Guid.NewGuid().ToString("N"), IsAvailable = true });
        Check(!controls.IsValid(), "control count bounded");
        controls.ControlHints.RemoveAt(controls.ControlHints.Count - 1);
        Check(controls.IsValid() && CoopCampaignMapBarCodec.TryEncode(9, controls, out var maximumControls) &&
            CoopCampaignMapBarCodec.TryDecode(maximumControls, 9, out var decodedMaximum) &&
            decodedMaximum.ControlHints.Count == CoopCampaignMapBarPresentation.MaxControlHints,
            "maximum controls preserved");
        bar.IsEnabled = true; bar.IsInfoBarEnabled = true;
        string hint = bar.PrimaryInfoItems[0].HintToken;
        Check(CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(bar, hint), "current hint admitted");
        bar.IsInfoBarEnabled = false;
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(bar, hint), "disabled info rejected");
        bar.IsInfoBarEnabled = true;
        var ambiguous = bar.Clone(); ambiguous.SecondaryInfoItems.Add(bar.PrimaryInfoItems[0].Clone());
        Check(!CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(ambiguous, hint), "ambiguous token rejected");
        string hovered = null; int ended = 0;
        var controlVm = new CoopSpectator.UI.CoopCampaignMapBarVM(null,
            (token, begin) => { if (begin) hovered = token; else { hovered = null; ended++; } });
        var bound = bar.Clone(); bound.ControlHints[0].Key = "PauseHint";
        controlVm.Apply(bound); controlVm.PauseHint.ExecuteBeginHint();
        Check(controlVm.TutorialNotification.ElementID == "PartySpeedLabel", "host tutorial element bound");
        Check(hovered == controlToken, "control hover routed to published token");
        controlVm.Apply(bound);
        Check(hovered == controlToken && ended == 0, "unchanged control survives publication");
        bound.ControlHints[0].IsAvailable = false; controlVm.Apply(bound);
        Check(hovered == null && ended == 1, "unavailable control ends hover");
        controlVm.PauseHint.ExecuteBeginHint();
        Check(hovered == null, "unavailable control cannot begin");
        bound.ControlHints[0].IsAvailable = true; controlVm.Apply(bound);
        controlVm.PauseHint.ExecuteBeginHint(); controlVm.ClearHintHover();
        Check(hovered == null && ended == 2, "modal clear ends control hover");
        controlVm.PauseHint.ExecuteBeginHint(); controlVm.OnFinalize();
        Check(hovered == null && ended == 3, "finalization ends control hover");
        Check(controlVm.TutorialNotification.ElementID == string.Empty, "finalization clears tutorial");
        ended = 0;
        var alertBar = bar.Clone();
        alertBar.ControlHints.Clear();
        alertBar.ControlHints.Add(new CoopCampaignMapBarControlTarget
        { Key = "Alert_Party", HintToken = controlToken, IsAvailable = true });
        var alertVm = new CoopSpectator.UI.CoopCampaignMapBarVM(null,
            (token, begin) => { if (begin) hovered = token; else { hovered = null; ended++; } });
        alertVm.Apply(alertBar); alertVm.NavigationItems[0].AlertTooltip.ExecuteBeginHint();
        Check(hovered == controlToken, "visible alert routes hover");
        alertBar.NavigationItems[0].HasAlert = false; alertVm.Apply(alertBar);
        Check(hovered == null && ended == 1, "hidden alert ends hover despite stale available token");
        alertVm.NavigationItems[0].AlertTooltip.ExecuteBeginHint();
        Check(hovered == null, "hidden alert cannot request");
        alertVm.OnFinalize(); ended = 0;
        var infoVm = new CoopSpectator.UI.CoopCampaignMapBarInfoVM((token, begin) => { if (begin) hovered = token; else { hovered = null; ended++; } });
        infoVm.Apply(bar.PrimaryInfoItems[0]); infoVm.ExecuteBeginHint();
        Check(hovered == hint, "native info hover routed");
        var updated = bar.PrimaryInfoItems[0].Clone(); updated.Value = "updated"; infoVm.Apply(updated);
        Check(hovered == hint && ended == 0, "value update retains hover");
        updated.HintToken = "barhint:" + Guid.NewGuid().ToString("N"); infoVm.Apply(updated);
        Check(hovered == null && ended == 1, "replacement revokes hover");
        infoVm.ExecuteBeginHint(); infoVm.OnFinalize();
        Check(hovered == null && ended == 2, "finalization revokes hover");
        Check(CoopCampaignMapBarCodec.TryEncode(9, bar, out var wire), "encode");
        Check(CoopCampaignMapBarCodec.TryDecode(wire, 9, out var decoded) && decoded.Date == bar.Date &&
            decoded.PrimaryInfoItems[0].Value == "1.2M" && decoded.SecondaryInfoItems[0].HasWarning &&
            decoded.NavigationItems[0].ItemId == "Party" && decoded.Time == bar.Time &&
            decoded.TutorialElementId == "PartySpeedLabel", "roundtrip");
        Check(decoded.ControlHints.Count == 1 && decoded.ControlHints[0].Key == "Pause" &&
            decoded.ControlHints[0].HintToken == controlToken && decoded.ControlHints[0].IsAvailable,
            "control target roundtrip");
        var previousSchema = (byte[])wire.Clone(); previousSchema[0] = 0x32;
        Check(!CoopCampaignMapBarCodec.TryDecode(previousSchema, 9, out _), "old CMB2 rejected");
        previousSchema[0] = 0x33;
        Check(!CoopCampaignMapBarCodec.TryDecode(previousSchema, 9, out _), "old CMB3 rejected");
        var badControlBoolean = (byte[])wire.Clone(); badControlBoolean[wire.Length - 1] = 2;
        Check(!CoopCampaignMapBarCodec.TryDecode(badControlBoolean, 9, out _), "control boolean canonical");
        Check(!CoopCampaignMapBarCodec.TryDecode(wire, 10, out decoded) && decoded == null, "revision mismatch");
        Check(CoopCampaignMapBarCodec.TryDecode(wire, 9, out decoded) &&
            decoded.SecondaryInfoItems[0].ItemId == "speed" &&
            decoded.SecondaryInfoItems[0].HintToken == bar.SecondaryInfoItems[0].HintToken, "hint identity independent of icon");
        var oldSchema = (byte[])wire.Clone(); oldSchema[0] = 0x31;
        Check(!CoopCampaignMapBarCodec.TryDecode(oldSchema, 9, out _), "old CMB1 rejected");
        for (int length = 0; length < wire.Length; length++)
        {
            var truncated = new byte[length]; Array.Copy(wire, truncated, length);
            Check(!CoopCampaignMapBarCodec.TryDecode(truncated, 9, out decoded) && decoded == null, "truncated");
        }
        var trailing = new byte[wire.Length + 1]; Array.Copy(wire, trailing, wire.Length);
        Check(!CoopCampaignMapBarCodec.TryDecode(trailing, 9, out decoded), "trailing byte");
        var corrupt = (byte[])wire.Clone(); corrupt[12] = 2;
        Check(!CoopCampaignMapBarCodec.TryDecode(corrupt, 9, out decoded), "noncanonical boolean");
        corrupt = (byte[])wire.Clone(); corrupt[0] ^= 1;
        Check(!CoopCampaignMapBarCodec.TryDecode(corrupt, 9, out decoded), "unknown schema");
        Check(CoopCampaignMapBarCodec.TryEncode(10, null, out var closed) &&
            CoopCampaignMapBarCodec.TryDecode(closed, 10, out decoded) && decoded == null, "explicit close");
        Check(!CoopCampaignMapBarCodec.TryEncode(0, bar, out _), "invalid revision");
        Check(CoopCampaignMapInterfaceCodec.TryEncode(12, null, bar, out var combined), "interface encode");
        Check(CoopCampaignMapInterfaceCodec.TryDecode(combined, 12, out var menu, out decoded) &&
            menu == null && decoded.Date == bar.Date, "interface atomic decode");
        Check(!CoopCampaignMapInterfaceCodec.TryDecode(combined, 13, out menu, out decoded) &&
            menu == null && decoded == null, "interface revision");
        for (int size = 0; size < combined.Length; size++)
        {
            var truncated = combined.Take(size).ToArray();
            Check(!CoopCampaignMapInterfaceCodec.TryDecode(truncated, 12, out menu, out decoded) &&
                menu == null && decoded == null, "interface truncation");
        }
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(combined, out var chunks, out _), "interface chunks");
        var assembler = new CoopCampaignMapInterfaceAssembler();
        bool completed = false;
        for (int i = chunks.ChunkCount - 1; i >= 0; i--)
            Check(assembler.TryAccept(12, chunks.LogicalByteCount, chunks.WireByteCount, chunks.ChunkCount,
                chunks.CompressionKind, chunks.PayloadHash, i, chunks.Chunks[i], out completed, out menu, out decoded), "chunk accept");
        Check(completed && decoded != null && decoded.Date == bar.Date, "atomic chunk completion");
        Check(!assembler.TryAccept(12, chunks.LogicalByteCount, chunks.WireByteCount, chunks.ChunkCount,
            chunks.CompressionKind, chunks.PayloadHash, 0, chunks.Chunks[0], out _, out _, out _), "replay rejected");
        assembler.Reset();
        Check(CoopCampaignMapInterfaceCodec.TryEncode(1, null, null, out combined) &&
            CoopCampaignMapInterfaceCodec.TryDecode(combined, 1, out menu, out decoded) &&
            menu == null && decoded == null, "atomic close");
        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 9,
            IsActive = true, UpdatedUtc = DateTime.UtcNow, MapBar = bar
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var received, out _) &&
            received.MapBar.Date == bar.Date && received.MapBar.PrimaryInfoItems[0].Value == "1.2M", "host bridge");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(x => !x.StartsWith("MapBarPresentation=")).ToArray(),
            out _, out _), "missing bar rejected");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(x => x.StartsWith("MapBarPresentation=") ?
            "MapBarPresentation=" + Convert.ToBase64String(closed) : x).ToArray(), out _, out _), "bar revision binding");
        host.MapBar = null;
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
            out received, out _) && received.MapBar == null, "bridge close");
        var copy = bar.Clone();
        copy.PrimaryInfoItems[0].Value = "changed";
        copy.SecondaryInfoItems[0].HasWarning = false;
        copy.NavigationItems[0].AlertText = "changed";
        Check(bar.PrimaryInfoItems[0].Value == "1.2M" && bar.SecondaryInfoItems[0].HasWarning &&
            bar.NavigationItems[0].AlertText == "2", "deep copy");
        foreach (double time in new[] { double.NaN, double.PositiveInfinity, -1, 24 })
        { copy = bar.Clone(); copy.Time = time; Check(!copy.IsValid(), "invalid time"); }
        copy = bar.Clone(); copy.TimeFlowState = 7; Check(!copy.IsValid(), "unknown speed");
        copy = bar.Clone(); copy.PrimaryInfoItems = null; Check(!copy.IsValid(), "missing list");
        copy = bar.Clone(); copy.SecondaryInfoItems.Add(null); Check(!copy.IsValid(), "null item");
        copy = bar.Clone(); copy.NavigationItems.Add(copy.NavigationItems[0].Clone()); Check(!copy.IsValid(), "duplicate navigation");
        copy = bar.Clone(); copy.PrimaryInfoItems[0].VisualId = "../gold"; Check(!copy.IsValid(), "path rejected");
        copy = bar.Clone(); copy.Date = new string('x', CoopCampaignMapBarPresentation.MaxTextLength + 1); Check(!copy.IsValid(), "text bound");
        copy = bar.Clone(); copy.PausedText = "a\0b"; Check(!copy.IsValid(), "embedded nul");
        copy = bar.Clone();
        for (int i = 1; i <= CoopCampaignMapBarPresentation.MaxItems; i++) copy.PrimaryInfoItems.Add(bar.PrimaryInfoItems[0].Clone());
        Check(!copy.IsValid(), "list bound");
        copy = bar.Clone(); copy.PrimaryInfoItems[0].IntValue = -100; Check(copy.IsValid(), "signed native values");
        copy.PrimaryInfoItems.Clear(); Check(bar.PrimaryInfoItems.Count == 1, "list independence");
        copy = bar.Clone(); copy.HoursInDay = 30; copy.Time = 29; Check(copy.IsValid(), "host calendar");
        copy.HoursInDay = 0; Check(!copy.IsValid(), "invalid calendar");
        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string capture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapBarCapture.cs"));
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.MapBar.cs"));
        string vm = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapBarVM.cs"));
        Check(view.Contains("LoadMovie(\"MapBar\", _mapBarVm)") &&
            view.Contains("LoadSpriteCategory(\"ui_mapbar\")"), "stock presentation resources");
        Check(view.Contains("IsClientMenuHostFresh") && view.Contains("!modal") &&
            view.Contains("CurrentClientConversation?.IsActive != true"), "display gates");
        Check(view.Contains("_mapBarSprites?.Unload()") && view.Contains("ReleaseMovie(_mapBarMovie)"), "resource release");
        Check(vm.Contains("ExecuteTimeControlChange(int selectedTimeSpeed) { }") &&
            vm.Contains("ExecuteArmyManagement() { }") && vm.Contains("ExecuteOpen() { }") &&
            vm.Contains("ExecuteGoToLink() { }"), "campaign actions inert");
        Check(!vm.Contains("Campaign.Current") && !vm.Contains("new MapBarVM("), "detached VM");
        Check(capture.Contains("source.MapTimeControl") && capture.Contains("source.MapInfo") &&
            capture.Contains("source.MapNavigation"), "prepared native submodels");
        Check(capture.Contains("ReferenceEquals(source, _sourceField.GetValue(layer))") &&
            capture.Contains("if (!source.IsEnabled) return true;"), "source replacement and hidden gates");
        string controlsCapture = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapTooltipCapture.Controls.cs"));
        Check(controlsCapture.Contains("MapNavigationHelper.IsNavigationBarEnabled(handler)") &&
            controlsCapture.Contains("_mostRecentClanScreenPermission"), "clan prepared denial respects outer gates");
        Check(!controlsCapture.Contains("TriggerEvent") && !controlsCapture.Contains(".OpenView("),
            "control capture dispatches no campaign events or navigation commands");
        foreach (string forbidden in new[] { "new MapBarVM(", ".Refresh(", ".Tick(",
            ".ExecuteTimeControlChange(", ".ExecuteOpen(", ".ExecuteArmyManagement(" })
            Check(!capture.Contains(forbidden), "no native command: " + forbidden);
        Console.WriteLine("Map bar presentation checks passed.");
    }
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("Map bar presentation: " + name); }
}
