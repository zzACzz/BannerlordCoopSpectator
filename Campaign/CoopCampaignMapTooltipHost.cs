using System;
using System.Diagnostics;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Campaign
{
    // Main-thread host adapter, attached only to the launcher-owned server.
    internal static class CoopCampaignMapTooltipHost
    {
        private static CoopTooltipRelayFrame _binding;
        private static TaleWorlds.CampaignSystem.Campaign _campaign;
        private static long _nextTick, _lastRound, _nextLivenessCheck;
        private static int _ownerThread;
        private static long _nextDiagnostic;

        internal static void Prepare(ProcessStartInfo start)
        {
            start.EnvironmentVariables.Remove(CoopCampaignMapTooltipRelay.SessionEnvironment);
            Reset();
            if (!ExperimentalFeatures.EnableCampaignMapPrototype || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            try
            {
                using (var self = Process.GetCurrentProcess()) _binding = new CoopTooltipRelayFrame {
                    Kind = CoopTooltipRelayKind.Binding, Run = Guid.NewGuid(), Host = CoopMissionProcessIdentity.Capture(self),
                    SentTimestamp = Stopwatch.GetTimestamp() };
                _campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                _ownerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                start.EnvironmentVariables[CoopCampaignMapTooltipRelay.SessionEnvironment] = _binding.Run.ToString("N");
            }
            catch (Exception) { Fail("prepare"); }
        }
        internal static void Attach(Process dedicated)
        {
            if (_binding == null) return;
            try
            {
                _binding.Dedicated = CoopMissionProcessIdentity.Capture(dedicated);
                CoopCampaignMapTooltipRelay.Write(_binding);
            }
            catch (Exception) { Fail("attach"); }
        }
        internal static void Reset()
        {
            _binding = null; _campaign = null; _nextTick = _lastRound = _nextLivenessCheck = _nextDiagnostic = 0; _ownerThread = 0;
            // Old run files expire/become unreachable; never enumerate/delete another run.
        }
        internal static void Tick()
        {
            if (_binding == null || _ownerThread != System.Threading.Thread.CurrentThread.ManagedThreadId) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextTick) return;
            _nextTick = now + Math.Max(1, Stopwatch.Frequency / 10);
            try
            {
                if (!ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current)) { Reset(); return; }
                if (_binding.Dedicated == null) return; // Failed/unattached launch retains no cross-campaign reference.
                if (now >= _nextLivenessCheck)
                {
                    _nextLivenessCheck = now + Stopwatch.Frequency;
                    if (!_binding.Dedicated.IsAlive()) { Reset(); return; }
                }
                var request = CoopCampaignMapTooltipRelay.Read(_binding.Run, CoopTooltipRelayKind.Requests);
                if (!CoopCampaignMapTooltipRelay.MatchesBinding(request, _binding) || request.Round <= _lastRound ||
                    !CoopCampaignMapTooltipRelay.IsFresh(request, now, Stopwatch.Frequency)) return;
                if (!_binding.Dedicated.IsAlive()) { Reset(); return; }
                _lastRound = request.Round; // At most one native execution per admitted round.
                var response = new CoopTooltipRelayFrame { Kind = CoopTooltipRelayKind.Responses, Run = _binding.Run,
                    Host = _binding.Host, Dedicated = _binding.Dedicated, Round = request.Round, SentTimestamp = request.SentTimestamp };
                foreach (var item in request.Items)
                {
                    bool captured = CoopCampaignMapPrototypePublisherBehavior.TryCapturePublishedTooltip(
                        item.Request, item.Sequence, out var payload, out var reason);
                    // Opt-in bounded metadata only; no extra native inspection.
                    if (CoopDebugConfig.VerboseDiagnostics && now >= _nextDiagnostic)
                    {
                        _nextDiagnostic = now + Stopwatch.Frequency * 5;
                        ModLogger.Info("CampaignMapTooltipHost: capture=" + captured + " reason=" + reason +
                            " rows=" + (payload?.Rows?.Count ?? 0) + " extended=" + item.Request.IsExtended);
                    }
                    var message = captured
                        ? new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Rows, Lease = item.Request.LeaseId, Payload = payload }
                        : new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Denied, Request = item.Request };
                    if (!CoopCampaignMapTooltipWireCodec.TryEncode(message, out var bytes)) throw new InvalidOperationException("Tooltip response encode.");
                    response.Items.Add(new CoopTooltipRelayItem { Request = item.Request, Sequence = item.Sequence, Response = bytes });
                }
                // Age remains anchored at request time; capture/transit cannot renew it.
                if (CoopCampaignMapTooltipRelay.IsFresh(response, Stopwatch.GetTimestamp(), Stopwatch.Frequency))
                    CoopCampaignMapTooltipRelay.Write(response);
            }
            catch (Exception) { Fail("capture-or-publication"); }
        }
        private static void Fail(string reason)
        {
            Reset();
            ModLogger.Info("CampaignMapTooltipHost: channel disabled after " + reason + ".");
        }
    }
}
