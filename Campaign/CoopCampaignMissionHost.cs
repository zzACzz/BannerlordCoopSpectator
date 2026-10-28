using System;
using System.Diagnostics;
using System.IO;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // All entry points run on the campaign main thread. The launcher attaches the
    // exact process it owns; no discovery or adoption of an existing server.
    internal static class CoopCampaignMissionHost
    {
        private static CoopCampaignMissionEnvelope _state;
        private static TaleWorlds.CampaignSystem.Campaign _campaign;
        private static CoopCampaignMissionIntent _lastIntent;
        private static long _revision, _pulse, _nextPulse;
        private static bool _faulted, _finalApplied = true;
        private static string _appliedBattleKey = "";
        private static CoopCampaignReloadFrame _reload;
        private static long _reloadOrdinal;
        public static bool OwnsCommands => _state != null;
        // A faulted owner must keep command ownership (no legacy fallback), but
        // cannot justify rebuilding a large roster every frame after save reload.
        public static bool CanAttemptBattleStart => _state == null ||
            (!_faulted && _reload == null && _campaign != null && ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current));

        internal static void ObserveCampaignLoaded()
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (_state == null || campaign == null || ReferenceEquals(_campaign, campaign)) return;
            if (_state.Dedicated == null || !_state.Dedicated.IsAlive() ||
                !BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId))
            { Fail("reload-owned-binding-unavailable"); return; }
            _reload = new CoopCampaignReloadFrame { Binding = new CoopCampaignMissionEnvelope {
                Host = _state.Host, Dedicated = _state.Dedicated, CampaignId = campaignId,
                Epoch = Guid.NewGuid().ToString("N"), Pulse = 1,
                Intent = new CoopCampaignMissionIntent { Session = _state.Intent.Session,
                    Revision = checked(++_reloadOrdinal), Kind = CoopCampaignMissionIntentKind.Hold } } };
            _campaign = campaign; _faulted = false; _nextPulse = 0;
            ModLogger.Info("CampaignMissionHost: loaded world queued for owned-session reset.");
        }

        public static void Prepare(ProcessStartInfo start)
        {
            start.EnvironmentVariables.Remove(CoopCampaignMissionChannel.SessionEnvironment);
            if (!ExperimentalFeatures.EnableAutomaticCampaignMissions) return;
            Reset();
            if (TaleWorlds.CampaignSystem.Campaign.Current == null ||
                !BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId))
                throw new InvalidOperationException("Automatic missions require an active campaign binding.");
            _campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            string session = Guid.NewGuid().ToString("N");
            using (var process = Process.GetCurrentProcess())
                _state = new CoopCampaignMissionEnvelope { CampaignId = campaignId, Epoch = Guid.NewGuid().ToString("N"), Host = CoopMissionProcessIdentity.Capture(process),
                    Intent = new CoopCampaignMissionIntent { Session = session } };
            CoopCampaignMissionChannel.HostSession = session;
            start.EnvironmentVariables[CoopCampaignMissionChannel.SessionEnvironment] = session;
        }

        public static void Attach(Process dedicated)
        {
            if (_state == null) return;
            try { _state.Dedicated = CoopMissionProcessIdentity.Capture(dedicated); }
            catch { Fail("dedicated-identity-unavailable"); }
        }

        public static void Reset()
        {
            // A former session's pulse expires; never delete a path owned by another process.
            _state = null; _campaign = null; _lastIntent = null;
            _revision = _pulse = _nextPulse = 0; _faulted = false;
            _appliedBattleKey = ""; _finalApplied = true;
            _reload = null; _reloadOrdinal = 0;
            CoopCampaignMissionChannel.HostSession = null;
        }

        public static bool QueueBattle()
        {
            if (_state == null || !CanAttemptBattleStart) return false;
            try
            {
                byte[] bytes = CoopCampaignMissionChannel.ReadBounded(BattleRosterFileHelper.GetRosterFilePath(), CoopCampaignMissionChannel.MaxRosterBytes);
                var dto = JsonConvert.DeserializeObject<BattleRosterFileDto>(System.Text.Encoding.UTF8.GetString(bytes));
                BattleSnapshotMessage battle = dto?.Snapshot;
                if (battle == null || battle.CampaignId != _state.CampaignId || string.IsNullOrEmpty(battle.BattleId) ||
                    string.IsNullOrEmpty(battle.BattleInstanceId) || battle.Sides == null || battle.Sides.Count == 0 ||
                    !CoopCampaignMissionChannel.IsToken(battle.MultiplayerScene) || !CoopCampaignMissionChannel.IsToken(battle.MultiplayerGameType)) return false;
                string key = CoopCampaignMissionChannel.Digest(bytes);
                if (_state.Intent.BattleKey == key) return true;
                if (!string.IsNullOrEmpty(_state.Intent.BattleKey) && _appliedBattleKey != _state.Intent.BattleKey)
                    return false;
                CoopCampaignMissionChannel.WriteAtomic(Path.Combine(
                    CoopCampaignMissionChannel.DirectoryFor(_state.Intent.Session), "host_roster.json"), bytes);
                _state.BattleId = battle.BattleId; _state.BattleInstanceId = battle.BattleInstanceId;
                _state.Scene = battle.MultiplayerScene; _state.GameType = battle.MultiplayerGameType;
                _state.RosterSha256 = key; _state.Intent.BattleKey = key;
                _finalApplied = false;
                _nextPulse = 0;
                Tick();
                return !_faulted;
            }
            catch (Exception) { Fail("battle-intent-publication-failed"); return false; }
        }

        // Called only at the validated applied-result journal boundary, not on file read.
        public static void ObserveApplied(CoopBattleResultBridgeFile.BattleResultSnapshot result, bool followupPending)
        {
            if (_state == null || _reload != null || !CanAttemptBattleStart || result == null || result.CampaignId != _state.CampaignId ||
                result.BattleId != _state.BattleId || result.BattleInstanceId != _state.BattleInstanceId) return;
            _appliedBattleKey = _state.Intent.BattleKey;
            _finalApplied = result.IsFinalStage && !followupPending;
            _nextPulse = 0;
        }

        public static void Tick()
        {
            if (_state == null || _faulted || _state.Dedicated == null) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextPulse) return;
            _nextPulse = now + Stopwatch.Frequency;
            try
            {
                // Loading temporarily removes/replaces Campaign.Current. Wait for
                // its session-launched callback instead of faulting the process pair.
                if (!ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current)) return;
                if (!_state.Dedicated.IsAlive()) { Fail("dedicated-identity-lost"); return; }
                if (_reload != null)
                {
                    _reload.Binding.SentTimestamp = now;
                    _reload.Binding.Pulse = checked(_reload.Binding.Pulse + 1);
                    CoopCampaignMissionReloadChannel.Write(_reload);
                    var acknowledgement = CoopCampaignMissionReloadChannel.Read(_state.Intent.Session, true);
                    if (!_reload.Matches(acknowledgement) ||
                        !CoopCampaignMissionChannel.IsFresh(acknowledgement.Binding, now, Stopwatch.Frequency)) return;
                    _state.CampaignId = _reload.Binding.CampaignId; _state.Epoch = _reload.Binding.Epoch;
                    _state.BattleId = _state.BattleInstanceId = _state.Scene = _state.GameType = _state.RosterSha256 = "";
                    _state.Intent = new CoopCampaignMissionIntent { Session = _state.Intent.Session };
                    _appliedBattleKey = ""; _finalApplied = true; _lastIntent = null; _reload = null;
                    ModLogger.Info("CampaignMissionHost: owned-session reset acknowledged; loaded world active.");
                }
                if (!BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId) || campaignId != _state.CampaignId)
                { Fail("loaded-campaign-binding-unavailable"); return; }
                // Once final results are applied, Loot-mode screens are map-backed
                // read-only presentation too. The existing final/applied-key gates
                // still forbid returning during an intermediate siege/hideout stage.
                bool onMap = Mission.Current == null &&
                    (GameStateManager.Current?.ActiveState is TaleWorlds.CampaignSystem.GameState.MapState ||
                     CoopCampaignAftermathCapture.IsAftermathState(GameStateManager.Current?.ActiveState));
                bool awaitingBattle = !string.IsNullOrEmpty(_state.Intent.BattleKey) && _appliedBattleKey != _state.Intent.BattleKey;
                var intent = new CoopCampaignMissionIntent {
                    Session = _state.Intent.Session, BattleKey = _state.Intent.BattleKey ?? "",
                    Kind = awaitingBattle ? CoopCampaignMissionIntentKind.Battle : onMap && _finalApplied ? CoopCampaignMissionIntentKind.Map : CoopCampaignMissionIntentKind.Hold,
                    HostOnGlobalMap = onMap, AftermathApplied = !string.IsNullOrEmpty(_appliedBattleKey),
                    AppliedBattleKey = _appliedBattleKey, FollowupStagePending = !_finalApplied };
                if (_lastIntent == null || !_lastIntent.SameContent(intent)) _revision = checked(_revision + 1);
                intent.Revision = _revision;
                _state.Intent = intent;
                _state.Pulse = checked(++_pulse); _state.SentTimestamp = now;
                CoopCampaignMissionChannel.Write(_state);
                _lastIntent = intent.Copy();
            }
            catch (Exception) { Fail("intent-publication-failed"); }
        }

        private static void Fail(string reason)
        {
            if (_faulted) return;
            _faulted = true;
            ModLogger.Info("CampaignMissionHost: stopped automatic transitions: " + reason);
            UiFeedback.ShowMessageDeferred("Coop: automatic mission transitions stopped (" + reason + "). Restart the owned server after inspection.");
        }
    }
}
