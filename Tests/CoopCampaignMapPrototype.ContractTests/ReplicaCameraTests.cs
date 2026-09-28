using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CoopSpectator.Infrastructure;

internal static class ReplicaCameraTests
{
    private static int _assertions;

    internal static void Run()
    {
        var first = new CoopCampaignMapReplicaCameraPolicy();
        var second = new CoopCampaignMapReplicaCameraPolicy();
        Check(first.IsFollowingHost && second.IsFollowingHost, "Every new client starts following.");
        Check(first.AdmitHostFrame(false) == CoopCampaignMapCameraApplication.None,
            "Missing host frames must not apply a fabricated camera.");
        Check(first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.Snap,
            "First valid host frame snaps, including after an initial gap.");
        Check(first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.Interpolate,
            "Subsequent host frames must keep updating the camera.");
        first.ObserveManualMovement(false);
        Check(first.IsFollowingHost, "Idle/click-only input must not detach.");
        first.ObserveManualMovement(true);
        Check(!first.IsFollowingHost && second.IsFollowingHost,
            "Manual movement detaches only that client's camera.");
        for (int i = 0; i < 1000; i++)
        {
            first.ObserveManualMovement(false);
            Check(first.AdmitHostFrame(i % 2 == 0) == CoopCampaignMapCameraApplication.None,
                "Neither elapsed idle ticks nor arriving host frames may reattach a detached camera.");
        }
        first.FollowHost();
        Check(first.IsFollowingHost, "Explicit return resumes following.");
        Check(first.AdmitHostFrame(false) == CoopCampaignMapCameraApplication.None,
            "Return during a host gap must wait for a valid frame.");
        Check(first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.Snap,
            "Return uses the next valid current host frame, not a saved detached origin.");
        Check(first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.Interpolate,
            "Return continues following subsequent host movement.");
        first.ObserveManualMovement(true);
        first.Reset();
        Check(first.IsFollowingHost && first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.Snap,
            "New map mission/scene lifetime resets follow ownership and initial snap.");
        first.Reset();
        first.ObserveManualMovement(true);
        Check(first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.None,
            "Manual fallback navigation before first host frame must not be overwritten later.");
        for (int i = 0; i < 100; i++)
        {
            first.FollowHost();
            Check(first.AdmitHostFrame(true) == CoopCampaignMapCameraApplication.Snap,
                "Repeated explicit returns are deterministic.");
            first.ObserveManualMovement(true);
        }

        string root = FindRoot();
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        string vm = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapReplicaInfoVM.cs"));
        XDocument prefab = XDocument.Load(Path.Combine(root, "Module", "CoopSpectator", "GUI", "Prefabs", "CoopCampaignMapReplicaInfo.xml"));
        var buttons = prefab.Descendants("ButtonWidget").Where(e =>
            (string)e.Attribute("Command.Click") == "ExecuteFollowHost").ToList();
        Check(buttons.Count == 1 && buttons[0].Descendants("TextWidget").Any(e =>
                (string)e.Attribute("Text") == "@FollowHostText"),
            "Exactly one visible native button must bind the explicit return action and label.");
        Check(vm.Contains("ExecuteFollowHost() => _followHost?.Invoke()") &&
              vm.Contains("До камери хоста") && vm.Contains("Самостійний огляд") &&
              view.Contains("new CoopCampaignMapReplicaInfoVM(FollowHostCamera)"),
            "Button and camera ownership status must be connected to the view, not decorative.");
        Check(view.Contains("_cameraPolicy.AdmitHostFrame(true)") &&
              view.Contains("_cameraPolicy.ObserveManualMovement(moved)") &&
              view.Contains("_cameraPolicy.Reset()") && !view.Contains("_hostCameraInitialized"),
            "Native view must consume policy and must not retain one-shot host initialization.");
        Check(view.Contains("!CanUseReplicaMapInput() ||") &&
              view.Contains("_replicaInfoLayer.HitTest()") &&
              view.Contains("CurrentClientConversation?.IsActive != true") &&
              view.Contains("ReferenceEquals(ScreenManager.FocusedLayer, _replicaInfoLayer)"),
            "Map input must yield to dialogue, foreign focus and interactive UI hit testing.");
        int callbackStart = view.IndexOf("private void FollowHostCamera()", StringComparison.Ordinal);
        int callbackEnd = view.IndexOf("private bool CanUseReplicaMapInput()", callbackStart, StringComparison.Ordinal);
        string callback = view.Substring(callbackStart, callbackEnd - callbackStart);
        Check(callback.Contains("_cameraPolicy.FollowHost()") &&
              callback.Contains("UpdateDisplayedCamera(current?.Camera, 1d)") &&
              !callback.Contains("Write") && !callback.Contains("Send"),
            "Explicit return must use current host state locally with no campaign/network write.");
        Console.WriteLine("Replica camera assertions passed: " + _assertions);
    }

    private static string FindRoot()
    {
        for (var path = new DirectoryInfo(Directory.GetCurrentDirectory()); path != null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "CoopSpectator.csproj"))) return path.FullName;
        throw new InvalidOperationException("Repository root unavailable.");
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
