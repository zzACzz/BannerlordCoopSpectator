using System;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class TooltipRelayTests
{
    private static int _count;
    private static CoopMissionProcessIdentity Identity(int id) => new CoopMissionProcessIdentity {
        Id = id, StartUtcTicks = 639000000000000000, Path = Path.GetFullPath("synthetic-tooltip-role.exe") };
    private static CoopTooltipRelayFrame Binding() => new CoopTooltipRelayFrame {
        Kind = CoopTooltipRelayKind.Binding, Run = Guid.NewGuid(), Host = Identity(111), Dedicated = Identity(222), SentTimestamp = 1000 };
    internal static void Run()
    {
        var binding = Binding();
        var encoded = CoopCampaignMapTooltipRelay.Encode(binding);
        Check(CoopCampaignMapTooltipRelay.MatchesBinding(CoopCampaignMapTooltipRelay.Decode(encoded), binding), "binding roundtrip");
        var session = Guid.NewGuid().ToString("N");
        var item = new CoopTooltipRelayItem { Request = new CoopCampaignMapTooltipRequest(session, Guid.NewGuid(), "party:host", 1, false), Sequence = 1 };
        var request = CoopCampaignMapTooltipRelay.Decode(encoded);
        request.Kind = CoopTooltipRelayKind.Requests; request.Round = 1000; request.Items.Add(item);
        byte[] requestBytes = CoopCampaignMapTooltipRelay.Encode(request);
        Check(CoopCampaignMapTooltipRelay.Decode(requestBytes).Items[0].Request.EntityId == "party:host", "request roundtrip");
        CoopCampaignMapTooltipWireCodec.TryEncode(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Denied, Request = item.Request }, out var denial);
        var response = CoopCampaignMapTooltipRelay.Decode(requestBytes); response.Kind = CoopTooltipRelayKind.Responses; response.Items[0].Response = denial;
        Check(CoopCampaignMapTooltipRelay.Decode(CoopCampaignMapTooltipRelay.Encode(response)).Items[0].Response.SequenceEqual(denial), "denial roundtrip");
        foreach (var frame in new[] { binding, request, response })
        {
            var bytes = CoopCampaignMapTooltipRelay.Encode(frame);
            for (int length = 0; length < bytes.Length; length++)
                Reject(() => CoopCampaignMapTooltipRelay.Decode(bytes.Take(length).ToArray()), "every truncated prefix");
            Reject(() => CoopCampaignMapTooltipRelay.Decode(bytes.Concat(new byte[] { 0 }).ToArray()), "trailing byte");
        }
        Check(CoopCampaignMapTooltipRelay.IsFresh(request, 2999, 1000) && !CoopCampaignMapTooltipRelay.IsFresh(request, 3000, 1000), "strict age boundary");
        Check(!CoopCampaignMapTooltipRelay.IsFresh(request, 999, 1000) && !CoopCampaignMapTooltipRelay.IsFresh(request, 1000, 0), "future and invalid clock");
        var wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Host.StartUtcTicks++;
        Check(!CoopCampaignMapTooltipRelay.MatchesBinding(wrong, binding), "pid reuse");
        wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Dedicated.Path += ".other";
        Check(!CoopCampaignMapTooltipRelay.MatchesBinding(wrong, binding), "wrong executable");
        wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Run = Guid.NewGuid();
        Check(!CoopCampaignMapTooltipRelay.MatchesBinding(wrong, binding), "wrong run");
        wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Items.Add(wrong.Items[0]);
        Reject(() => CoopCampaignMapTooltipRelay.Encode(wrong), "duplicate peer lease");
        wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Kind = CoopTooltipRelayKind.Responses;
        Reject(() => CoopCampaignMapTooltipRelay.Encode(wrong), "missing response");
        wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Items[0].Response = denial;
        Reject(() => CoopCampaignMapTooltipRelay.Encode(wrong), "request injecting response");
        Reject(() => CoopCampaignMapTooltipRelay.DirectoryFor(Guid.Empty), "no empty-root fallback");
        Reject(() => CoopCampaignMapTooltipRelay.Decode(new byte[CoopCampaignMapTooltipRelay.MaxFrameBytes + 1]), "frame allocation bound");
        var rows = new CoopCampaignMapTooltipPayload { SessionId = session, EntityId = item.Request.EntityId,
            RequestGeneration = 1, Sequence = 2, Kind = CoopCampaignMapTooltipKind.MobileParty,
            Rows = new System.Collections.Generic.List<CoopCampaignMapTooltipRow> { new CoopCampaignMapTooltipRow { Value = "30" } } };
        CoopCampaignMapTooltipWireCodec.TryEncode(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Rows,
            Lease = item.Request.LeaseId, Payload = rows }, out var mismatchedRows);
        wrong = CoopCampaignMapTooltipRelay.Decode(requestBytes); wrong.Kind = CoopTooltipRelayKind.Responses; wrong.Items[0].Response = mismatchedRows;
        Reject(() => CoopCampaignMapTooltipRelay.Encode(wrong), "row sequence must match host ticket");

        // Isolated synthetic file round: no product process, native formatter or real endpoint.
        string directory = CoopCampaignMapTooltipRelay.DirectoryFor(binding.Run);
        Check(!Directory.Exists(directory), "fresh synthetic directory");
        try
        {
            CoopCampaignMapTooltipRelay.Write(binding);
            var client = new CoopCampaignMapTooltipRelayClient(binding, Identity(222));
            binding.Host.Id = 333; // Constructor must keep its own detached proof.
            var ticket = new CoopCampaignMapTooltipCaptureTicket(item.Request, 1);
            Check(client.TrySubmit(new[] { ticket }, 1000, 1000, _ => true), "submit");
            Check(!client.TrySubmit(new[] { ticket }, 1001, 1000, _ => true), "one outstanding batch");
            var fromDisk = CoopCampaignMapTooltipRelay.Read(binding.Run, CoopTooltipRelayKind.Requests);
            Check(fromDisk.Host.Id == 111 && fromDisk.Round == 1000, "immutable binding and round");
            var reply = CoopCampaignMapTooltipRelay.Decode(CoopCampaignMapTooltipRelay.Encode(fromDisk));
            reply.Kind = CoopTooltipRelayKind.Responses; reply.Items[0].Response = denial; reply.Round++;
            CoopCampaignMapTooltipRelay.Write(reply);
            Check(client.TryReceive(1100, 1000, _ => true) == null, "wrong round cannot complete");
            reply.Round = fromDisk.Round; reply.SentTimestamp++;
            CoopCampaignMapTooltipRelay.Write(reply);
            Check(client.TryReceive(1100, 1000, _ => true) == null, "response cannot refresh request timestamp");
            reply.SentTimestamp = fromDisk.SentTimestamp; CoopCampaignMapTooltipRelay.Write(reply);
            Check(client.TryReceive(1100, 1000, _ => true) != null, "exact response admitted");
            Check(client.TryReceive(1100, 1000, _ => true) == null, "no duplicate receive");
            Check(client.TrySubmit(new[] { ticket }, 1200, 1000, _ => true), "next batch");
            Check(client.TryReceive(3200, 1000, _ => true) == null, "expired pending batch");
            Check(client.TrySubmit(new[] { ticket }, 3200, 1000, _ => true), "new batch after timeout");
            Check(client.TryReceive(3200, 1000, _ => false) == null && !client.TrySubmit(new[] { ticket }, 4000, 1000, _ => true), "lost process permanently revokes");
        }
        finally
        {
            // Exact three files created above, then only the empty owned directory.
            foreach (string name in new[] { "binding.bin", "requests.bin", "responses.bin" })
            { string path = Path.Combine(directory, name); if (File.Exists(path)) File.Delete(path); }
            if (Directory.Exists(directory)) Directory.Delete(directory, false);
        }
        Check(!Directory.Exists(directory), "synthetic files cleaned");

        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string host = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapTooltipHost.cs"));
        string launcher = File.ReadAllText(Path.Combine(root, "DedicatedHelper", "DedicatedHelperLauncher.cs"));
        Check(!host.Contains("EnableAutomaticCampaignMissions") && host.Contains("EnableCampaignMapPrototype"), "manual map flow independent");
        Check(host.Contains("request.Round <= _lastRound") && host.Contains("_binding.Dedicated.IsAlive()") &&
            host.Contains("TryCapturePublishedTooltip") && host.Contains("SentTimestamp = request.SentTimestamp"), "host admission and unchanged age");
        Check(launcher.Contains("CoopCampaignMapTooltipHost.Prepare(moddedOfficialStartInfo)") &&
            launcher.Contains("CoopCampaignMapTooltipHost.Attach(current)"), "owned launcher hooks");
        Check(host.IndexOf("now >= _nextLivenessCheck", StringComparison.Ordinal) <
            host.IndexOf("var request = CoopCampaignMapTooltipRelay.Read", StringComparison.Ordinal), "idle dead server stops file polling");
        Check(host.IndexOf("!ReferenceEquals(_campaign", StringComparison.Ordinal) <
            host.IndexOf("if (_binding.Dedicated == null) return", StringComparison.Ordinal), "failed launch cannot retain previous campaign");
        Console.WriteLine("Tooltip relay assertions passed: " + _count);
    }
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip relay: " + name); }
    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; } catch (InvalidDataException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, name);
    }
}
