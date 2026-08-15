using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Infrastructure;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using CampaignSystemRuntime = TaleWorlds.CampaignSystem.Campaign;

namespace CoopSpectator.Campaign
{
    public sealed class PlayerHeroCreationCampaignBehavior : CampaignBehaviorBase
    {
        private const int MaxConsumedResults = 64;
        private static PlayerHeroCreationCampaignBehavior _instance;
        private string _campaignScopeId;
        private string _activeRequestJson;
        private List<string> _heroRecordJson = new List<string>();
        private List<string> _consumedResultIds = new List<string>();
        private int _previousTimeMode;
        private bool _timePausedForCreator;
        private readonly CoopHeroCreationWallClockPollGate _pollGate =
            new CoopHeroCreationWallClockPollGate(TimeSpan.FromMilliseconds(500));
        private string _lastObservedProgressSignature;
        private string _currentStatusText;
        private int _statusRevision;

        public PlayerHeroCreationCampaignBehavior() { _instance = this; }

        public override void RegisterEvents()
        {
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("CoopHeroCampaignScopeId", ref _campaignScopeId);
            dataStore.SyncData("CoopHeroActiveRequest", ref _activeRequestJson);
            dataStore.SyncData("CoopHeroRecords", ref _heroRecordJson);
            dataStore.SyncData("CoopHeroConsumedResults", ref _consumedResultIds);
            dataStore.SyncData("CoopHeroPreviousTimeMode", ref _previousTimeMode);
            dataStore.SyncData("CoopHeroTimePaused", ref _timePausedForCreator);
            if (_heroRecordJson == null) _heroRecordJson = new List<string>();
            if (_consumedResultIds == null) _consumedResultIds = new List<string>();
            if (string.IsNullOrWhiteSpace(_campaignScopeId)) _campaignScopeId = Guid.NewGuid().ToString("N");
        }

        public static bool CanStart(out string reason)
        {
            if (_instance == null || CampaignSystemRuntime.Current == null) { reason = "The campaign coordinator is not ready yet."; return false; }
            if (!string.IsNullOrWhiteSpace(_instance._activeRequestJson)) { reason = "A previous hero creation session is still active."; return false; }
            if (TaleWorlds.MountAndBlade.Mission.Current != null) { reason = "Finish the current mission first."; return false; }
            if (!DedicatedHelperLauncher.HasDedicatedProcess()) { reason = "The local dedicated server is not running."; return false; }
            reason = string.Empty;
            return true;
        }

        public static bool HasActiveSession =>
            _instance != null && !string.IsNullOrWhiteSpace(_instance._activeRequestJson);

        public static int StatusRevision => _instance?._statusRevision ?? 0;

        public static string CurrentStatusText =>
            _instance == null || string.IsNullOrWhiteSpace(_instance._currentStatusText)
                ? "Hero creation is starting. Waiting for the dedicated server."
                : _instance._currentStatusText;

        public static void PumpApplicationTick()
        {
            if (_instance == null || CampaignSystemRuntime.Current == null) return;
            _instance.PollActiveSession(DateTime.UtcNow);
        }

        public static bool TryStart(out string message)
        {
            string reason;
            if (!CanStart(out reason)) { message = reason; return false; }
            return _instance.StartSession(out message);
        }

        public static bool TryCancelActiveSession(out string message)
        {
            if (_instance == null || CampaignSystemRuntime.Current == null)
            {
                message = "The campaign coordinator is not ready yet.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_instance._activeRequestJson))
            {
                message = "There is no active hero creation session.";
                return false;
            }

            bool serverWasAlive = DedicatedHelperLauncher.HasDedicatedProcess();
            bool endMissionRequested = !serverWasAlive || DedicatedServerCommands.SendEndMission();
            _instance._activeRequestJson = null;
            _instance.ResetProgressState();
            _instance.RestoreCampaignTime();
            message = endMissionRequested
                ? "Hero creation has been canceled."
                : "Hero creation was canceled in the campaign, but the server did not confirm that the mission ended.";
            return true;
        }

        private bool StartSession(out string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_campaignScopeId)) _campaignScopeId = Guid.NewGuid().ToString("N");
                CoopHeroCreationRules rules = BuildRules();
                CoopHeroCreationRequest request = new CoopHeroCreationRequest
                {
                    CampaignScopeId = _campaignScopeId,
                    RequestId = Guid.NewGuid().ToString("N"),
                    SessionId = Guid.NewGuid().ToString("N"),
                    Nonce = Guid.NewGuid().ToString("N"),
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    Rules = rules,
                    RulesHash = rules.ComputeHash(),
                    ExistingPlayerHashes = ReadRecords().Where(r => !string.IsNullOrWhiteSpace(r.PlayerIdentityHash))
                        .Select(r => r.PlayerIdentityHash).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
                CoopHeroCreationBridgeFile.WriteRequest(request);
                _activeRequestJson = JsonConvert.SerializeObject(request, Formatting.None);
                ResetProgressState();
                SetStatus("Hero creation is starting. Waiting for the dedicated server.", false);
                PauseCampaign();
                if (!DedicatedServerCommands.SendStartHeroCreatorMission())
                {
                    _activeRequestJson = null;
                    ResetProgressState();
                    RestoreCampaignTime();
                    message = "The dedicated server did not accept the request to start the hero creation mission.";
                    return false;
                }
                message = "The hero creation mission has started. Connected players will be taken to the hero editor.";
                return true;
            }
            catch (Exception ex)
            {
                _activeRequestJson = null;
                ResetProgressState();
                RestoreCampaignTime();
                message = "Failed to start hero creation: " + ex.Message;
                return false;
            }
        }

        private void OnTick(float dt)
        {
            PollActiveSession(DateTime.UtcNow);
        }

        private void PollActiveSession(DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(_activeRequestJson)) return;
            PauseCampaign();
            if (!_pollGate.TryEnter(utcNow)) return;

            CoopHeroCreationRequest request;
            try { request = JsonConvert.DeserializeObject<CoopHeroCreationRequest>(_activeRequestJson); }
            catch { request = null; }
            if (request == null) { ClearActiveSession("The saved hero creation request is corrupted."); return; }

            DateTime createdUtc;
            if (DateTime.TryParse(request.CreatedUtc, out createdUtc) && utcNow > createdUtc.ToUniversalTime().AddMinutes(12))
            {
                ClearActiveSession("The hero creation session was canceled because it timed out.");
                return;
            }

            CoopHeroCreationResult result;
            string error;
            if (CoopHeroCreationBridgeFile.TryReadResult(out result, out error) &&
                MatchesActiveRequest(request, result) &&
                !_consumedResultIds.Contains(result.ResultId))
            {
                ConsumeResult(request, result);
                return;
            }

            CoopHeroCreationProgressSnapshot progress;
            if (CoopHeroCreationBridgeFile.TryReadProgress(out progress, out error) &&
                CoopHeroCreationProgressContract.MatchesActiveRequest(request, progress, out error))
                ObserveProgress(progress);

            if (!DedicatedHelperLauncher.HasDedicatedProcess())
                ClearActiveSession("The hero creation session was canceled because the dedicated server stopped running.");
        }

        private void ObserveProgress(CoopHeroCreationProgressSnapshot progress)
        {
            string signature = CoopHeroCreationProgressContract.BuildSignature(progress);
            if (string.Equals(signature, _lastObservedProgressSignature, StringComparison.Ordinal)) return;
            _lastObservedProgressSignature = signature;
            SetStatus(CoopHeroCreationProgressContract.BuildHostStatusText(progress), true);
        }

        private void ConsumeResult(CoopHeroCreationRequest request, CoopHeroCreationResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                RememberConsumedResult(result.ResultId);
                string failureMessage = string.Equals(
                    result.FailureReason,
                    "rules_hash_mismatch",
                    StringComparison.OrdinalIgnoreCase)
                    ? "The server rejected the session because the creation rules did not match."
                    : "The server rejected the hero creation session. See the log for details.";
                ClearActiveSession(failureMessage);
                return;
            }

            int created = 0;
            int skipped = 0;
            List<CoopHeroRecord> records = ReadRecords();
            foreach (CoopHeroCreationParticipantResult participant in result.Participants ?? new List<CoopHeroCreationParticipantResult>())
            {
                if (participant == null || participant.State != CoopHeroCreationParticipantState.Completed) continue;
                string expectedLogicalId = CoopHeroCreationContract.BuildLogicalHeroId(request.CampaignScopeId, participant.PlayerIdentityHash);
                string validationError = string.Empty;
                if (!string.Equals(expectedLogicalId, participant.LogicalHeroId, StringComparison.OrdinalIgnoreCase) ||
                    !CoopHeroCreationContract.ValidateDraft(participant.Draft, request.Rules, out validationError))
                {
                    if (string.IsNullOrWhiteSpace(validationError)) validationError = "logical_hero_id_mismatch";
                    ModLogger.Info("PlayerHeroCreationCampaignBehavior: participant rejected by campaign authority. Error=" + validationError);
                    skipped++;
                    continue;
                }

                CoopHeroRecord existing = records.FirstOrDefault(r => string.Equals(r.LogicalHeroId, expectedLogicalId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    Hero existingHero = ResolveHero(existing.ActualHeroId);
                    if (existingHero == null || existingHero.IsDead || existingHero.IsDisabled) existing.IsTombstone = true;
                    else CoopPlayerHeroFactory.TryRepairExistingCompanion(existingHero);
                    skipped++;
                    continue;
                }

                Hero hero;
                string createError;
                if (!CoopPlayerHeroFactory.TryCreateCompanion(participant.Draft, request.Rules, out hero, out createError))
                {
                    ModLogger.Info("PlayerHeroCreationCampaignBehavior: companion creation failed. LogicalHeroId=" + expectedLogicalId + " Error=" + createError);
                    skipped++;
                    continue;
                }
                records.Add(new CoopHeroRecord
                {
                    LogicalHeroId = expectedLogicalId,
                    PlayerIdentityHash = participant.PlayerIdentityHash,
                    ActualHeroId = hero.StringId,
                    IsTombstone = false
                });
                created++;
            }

            WriteRecords(records);
            RememberConsumedResult(result.ResultId);
            _activeRequestJson = null;
            ResetProgressState();
            RestoreCampaignTime();
            InformationManager.DisplayMessage(new InformationMessage(
                "Hero creation completed. Companions added: " + created + ", skipped: " + skipped + "."));
        }

        private void RememberConsumedResult(string resultId)
        {
            _consumedResultIds.Add(resultId);
            while (_consumedResultIds.Count > MaxConsumedResults) _consumedResultIds.RemoveAt(0);
        }

        private CoopHeroCreationRules BuildRules()
        {
            CoopHeroCreationRules rules = new CoopHeroCreationRules();
            try
            {
                rules.Perks = PerkObject.All.Where(p => p != null && p.Skill != null && p.RequiredSkillValue <= 50)
                    .Select(p => new CoopHeroCreationPerkRule
                    {
                        PerkId = p.StringId,
                        Name = p.Name?.ToString() ?? p.StringId,
                        SkillId = NormalizeSkillId(p.Skill.StringId),
                        RequiredSkillValue = (int)p.RequiredSkillValue,
                        AlternativePerkId = p.AlternativePerk?.StringId
                    })
                    .Where(p => rules.SkillIds.Contains(p.SkillId))
                    .OrderBy(p => p.PerkId, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                ModLogger.Info("PlayerHeroCreationCampaignBehavior: perk catalog build failed. Error=" + ex.Message);
                rules.Perks = new List<CoopHeroCreationPerkRule>();
            }
            return rules;
        }

        private static string NormalizeSkillId(string skillId)
        {
            return string.Equals(skillId, "Crafting", StringComparison.OrdinalIgnoreCase) ? "Smithing" : skillId;
        }

        private void PauseCampaign()
        {
            if (CampaignSystemRuntime.Current == null) return;
            if (!_timePausedForCreator)
            {
                _previousTimeMode = (int)CampaignSystemRuntime.Current.TimeControlMode;
                _timePausedForCreator = true;
            }
            CampaignSystemRuntime.Current.TimeControlMode = CampaignTimeControlMode.Stop;
        }

        private void RestoreCampaignTime()
        {
            if (_timePausedForCreator && CampaignSystemRuntime.Current != null)
                CampaignSystemRuntime.Current.TimeControlMode = (CampaignTimeControlMode)_previousTimeMode;
            _timePausedForCreator = false;
        }

        private void ClearActiveSession(string message)
        {
            _activeRequestJson = null;
            ResetProgressState();
            RestoreCampaignTime();
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private void SetStatus(string message, bool showMessage)
        {
            string normalized = message ?? string.Empty;
            if (string.Equals(normalized, _currentStatusText, StringComparison.Ordinal)) return;
            _currentStatusText = normalized;
            _statusRevision++;
            if (showMessage && !string.IsNullOrWhiteSpace(normalized))
                InformationManager.DisplayMessage(new InformationMessage(normalized));
        }

        private void ResetProgressState()
        {
            _pollGate.Reset();
            _lastObservedProgressSignature = null;
            _currentStatusText = null;
            _statusRevision++;
        }

        private static bool MatchesActiveRequest(CoopHeroCreationRequest request, CoopHeroCreationResult result)
        {
            if (result == null || result.ProtocolVersion != CoopHeroCreationContract.ProtocolVersion ||
                string.IsNullOrWhiteSpace(result.ResultId) ||
                !string.Equals(request.CampaignScopeId, result.CampaignScopeId, StringComparison.Ordinal) ||
                !string.Equals(request.RequestId, result.RequestId, StringComparison.Ordinal) ||
                !string.Equals(request.SessionId, result.SessionId, StringComparison.Ordinal) ||
                !string.Equals(request.Nonce, result.Nonce, StringComparison.Ordinal) ||
                !string.Equals(request.RulesHash, result.RulesHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.ResultId, CoopHeroCreationContract.ComputeResultId(result), StringComparison.OrdinalIgnoreCase))
                return false;

            List<CoopHeroCreationParticipantResult> participants = result.Participants ?? new List<CoopHeroCreationParticipantResult>();
            return participants.All(p => p != null && IsSha256Hex(p.PlayerIdentityHash) && CoopHeroCreationContract.IsTerminal(p.State)) &&
                   participants.Select(p => p.PlayerIdentityHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() == participants.Count;
        }

        private static bool IsSha256Hex(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length == 64 &&
                   value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        }

        private List<CoopHeroRecord> ReadRecords()
        {
            List<CoopHeroRecord> records = new List<CoopHeroRecord>();
            foreach (string json in _heroRecordJson ?? new List<string>())
            {
                try
                {
                    CoopHeroRecord record = JsonConvert.DeserializeObject<CoopHeroRecord>(json);
                    if (record != null && !string.IsNullOrWhiteSpace(record.LogicalHeroId)) records.Add(record);
                }
                catch { }
            }
            return records;
        }

        private void WriteRecords(IEnumerable<CoopHeroRecord> records)
        {
            _heroRecordJson = records.Select(r => JsonConvert.SerializeObject(r, Formatting.None)).ToList();
        }

        private static Hero ResolveHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId)) return null;
            Hero hero = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<Hero>(heroId);
            return hero ?? Hero.FindFirst(h => string.Equals(h.StringId, heroId, StringComparison.Ordinal));
        }

        private sealed class CoopHeroRecord
        {
            public string LogicalHeroId { get; set; }
            public string PlayerIdentityHash { get; set; }
            public string ActualHeroId { get; set; }
            public bool IsTombstone { get; set; }
        }
    }
}
