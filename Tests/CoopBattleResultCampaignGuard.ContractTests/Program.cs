using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoopSpectator.Campaign;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Automation;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;

namespace TaleWorlds.CampaignSystem
{
    public interface IDataStore
    {
        bool IsSaving { get; }
        bool IsLoading { get; }
        bool SyncData<T>(string key, ref T data);
    }

    public abstract class CampaignBehaviorBase
    {
        public abstract void RegisterEvents();
        public abstract void SyncData(IDataStore dataStore);
    }

    public sealed class Campaign
    {
        private object _behavior;

        public static Campaign Current { get; set; }

        public void SetBehavior(object behavior)
        {
            _behavior = behavior;
        }

        public T GetCampaignBehavior<T>()
        {
            return _behavior is T typed ? typed : default;
        }
    }
}

namespace CoopSpectator.Infrastructure
{
    internal static class ModLogger
    {
        public static void Info(string message) { }
        public static void Error(string message, Exception exception) { }
    }
}

internal static class Program
{
    private sealed class FakeDataStore : TaleWorlds.CampaignSystem.IDataStore
    {
        private readonly Dictionary<string, object> _records;

        public FakeDataStore(bool isSaving)
            : this(isSaving, new Dictionary<string, object>(StringComparer.Ordinal))
        {
        }

        private FakeDataStore(bool isSaving, Dictionary<string, object> records)
        {
            IsSaving = isSaving;
            _records = records;
        }

        public bool IsSaving { get; }
        public bool IsLoading => !IsSaving;

        public bool SyncData<T>(string key, ref T data)
        {
            if (IsSaving)
            {
                _records[key] = CloneValue(data);
                return true;
            }

            if (!_records.TryGetValue(key, out object value))
                return false;

            data = (T)CloneValue(value);
            return true;
        }

        public FakeDataStore CreateLoader()
        {
            var clone = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in _records)
                clone[pair.Key] = CloneValue(pair.Value);
            return new FakeDataStore(isSaving: false, clone);
        }

        public List<string> ReadStringList(string key)
        {
            return _records.TryGetValue(key, out object value) && value is List<string> list
                ? new List<string>(list)
                : new List<string>();
        }

        private static object CloneValue(object value)
        {
            return value is List<string> list ? new List<string>(list) : value;
        }
    }

    private static int Main()
    {
        try
        {
            ValidateCampaignIdIsCreatedAndSurvivesSaveLoad();
            ValidateDifferentCampaignsReceiveDifferentIds();
            ValidateModernCampaignDecisions();
            ValidateLegacyRequiresStrongActiveBattleIdentity();
            ValidateFailureDoesNotJournalAndCanRetry();
            ValidateSuccessJournalsExactlyOnce();
            ValidateConcurrentBeginAllowsOneMutation();
            ValidateCrashRecoveryBeforeAndAfterSave();
            ValidateSnapshotAndResultJsonRoundTrips();
            ValidateAutomationSuppressesCampaignConsumableResultsAcrossBattleTypes();
            ValidateOldJsonUsesControlledLegacyBranch();
            ValidateJournalBoundIsDeterministic();
            ValidateSourceOrderingAndResultFileRetention();
            Console.WriteLine("Coop battle result campaign guard contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            TaleWorlds.CampaignSystem.Campaign.Current = null;
        }
    }

    private static void ValidateCampaignIdIsCreatedAndSurvivesSaveLoad()
    {
        BattleResultWritebackJournalBehavior original = ActivateNewCampaignBehavior();
        Assert(BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string originalId),
            "A new active campaign must receive a campaign id.");
        Assert(CoopBattleResultCampaignGuardContract.IsValidCampaignId(originalId),
            "A generated campaign id must be a GUID in N format.");

        var saveStore = new FakeDataStore(isSaving: true);
        original.SyncData(saveStore);

        var loaded = new BattleResultWritebackJournalBehavior();
        ActivateCampaignBehavior(loaded);
        loaded.SyncData(saveStore.CreateLoader());
        Assert(BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string loadedId) &&
               string.Equals(originalId, loadedId, StringComparison.Ordinal),
            "Save/load must retain the exact campaign id.");
    }

    private static void ValidateDifferentCampaignsReceiveDifferentIds()
    {
        ActivateNewCampaignBehavior();
        Assert(BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string firstId),
            "The first campaign id must be available.");
        ActivateNewCampaignBehavior();
        Assert(BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string secondId),
            "The second campaign id must be available.");
        Assert(!string.Equals(firstId, secondId, StringComparison.Ordinal),
            "Different campaigns must receive different ids.");
    }

    private static void ValidateModernCampaignDecisions()
    {
        string activeCampaignId = Guid.NewGuid().ToString("N");
        string otherCampaignId = Guid.NewGuid().ToString("N");
        string battleInstanceId = Guid.NewGuid().ToString("N");
        Assert(CoopBattleResultCampaignGuardContract.TryBuildStableResultId(
                battleInstanceId, "Battle", out string resultId),
            "A valid battle identity must produce a stable result id.");

        CoopBattleResultCampaignEvaluation allow = EvaluateModern(
            activeCampaignId, activeCampaignId, resultId, battleInstanceId, _ => false);
        Assert(allow.Decision == CoopBattleResultCampaignDecision.AllowModern,
            "Same campaign plus a new result id must be allowed.");

        CoopBattleResultCampaignEvaluation alreadyApplied = EvaluateModern(
            activeCampaignId, activeCampaignId, resultId, battleInstanceId,
            key => string.Equals(key, resultId, StringComparison.Ordinal));
        Assert(alreadyApplied.Decision == CoopBattleResultCampaignDecision.AlreadyApplied,
            "A journaled result id must be a no-op.");

        CoopBattleResultCampaignEvaluation mismatch = EvaluateModern(
            activeCampaignId, otherCampaignId, resultId, battleInstanceId, _ => false);
        Assert(mismatch.Decision == CoopBattleResultCampaignDecision.RejectCampaignMismatch,
            "A result from another campaign must be rejected.");

        CoopBattleResultCampaignEvaluation noCampaign =
            CoopBattleResultCampaignGuardContract.Evaluate(
                CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion,
                hasActiveCampaign: false,
                activeCampaignId: null,
                resultCampaignId: activeCampaignId,
                resultId: resultId,
                resultBattleInstanceId: battleInstanceId,
                hasStrongActiveBattleIdentity: false,
                activeBattleInstanceId: null,
                requireModernBattleInstanceMatch: false,
                isAlreadyApplied: _ => false);
        Assert(noCampaign.Decision == CoopBattleResultCampaignDecision.RejectMissingActiveCampaign,
            "A result must be rejected when no campaign is active.");

        CoopBattleResultCampaignEvaluation malformedCampaign = EvaluateModern(
            activeCampaignId, "not-a-guid", resultId, battleInstanceId, _ => false);
        Assert(malformedCampaign.Decision == CoopBattleResultCampaignDecision.RejectInvalidCampaignId,
            "A malformed campaign id must be rejected.");

        CoopBattleResultCampaignEvaluation emptyCampaign = EvaluateModern(
            activeCampaignId, string.Empty, resultId, battleInstanceId, _ => false);
        Assert(emptyCampaign.Decision == CoopBattleResultCampaignDecision.RejectInvalidCampaignId,
            "An empty modern campaign id must not downgrade to legacy.");

        CoopBattleResultCampaignEvaluation malformedResult = EvaluateModern(
            activeCampaignId, activeCampaignId, "bad-result", battleInstanceId, _ => false);
        Assert(malformedResult.Decision == CoopBattleResultCampaignDecision.RejectInvalidResultId,
            "A malformed modern result id must be rejected.");

        CoopBattleResultCampaignEvaluation battleMismatch =
            CoopBattleResultCampaignGuardContract.Evaluate(
                CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion,
                hasActiveCampaign: true,
                activeCampaignId: activeCampaignId,
                resultCampaignId: activeCampaignId,
                resultId: resultId,
                resultBattleInstanceId: battleInstanceId,
                hasStrongActiveBattleIdentity: true,
                activeBattleInstanceId: Guid.NewGuid().ToString("N"),
                requireModernBattleInstanceMatch: true,
                isAlreadyApplied: _ => false);
        Assert(battleMismatch.Decision == CoopBattleResultCampaignDecision.RejectBattleInstanceMismatch,
            "An active mission must reject an older result from the same campaign.");
    }

    private static void ValidateLegacyRequiresStrongActiveBattleIdentity()
    {
        string campaignId = Guid.NewGuid().ToString("N");
        string battleInstanceId = Guid.NewGuid().ToString("N");
        CoopBattleResultCampaignGuardContract.TryBuildStableResultId(
            battleInstanceId, "Battle", out string resultId);

        CoopBattleResultCampaignEvaluation rejected =
            CoopBattleResultCampaignGuardContract.Evaluate(
                campaignBindingVersion: 0,
                hasActiveCampaign: true,
                activeCampaignId: campaignId,
                resultCampaignId: null,
                resultId: resultId,
                resultBattleInstanceId: battleInstanceId,
                hasStrongActiveBattleIdentity: false,
                activeBattleInstanceId: battleInstanceId,
                requireModernBattleInstanceMatch: false,
                isAlreadyApplied: _ => false);
        Assert(rejected.Decision ==
               CoopBattleResultCampaignDecision.RejectMissingStrongLegacyBattleIdentity,
            "Legacy data without a proven active battle identity must be rejected.");

        CoopBattleResultCampaignEvaluation allowed =
            CoopBattleResultCampaignGuardContract.Evaluate(
                campaignBindingVersion: 0,
                hasActiveCampaign: true,
                activeCampaignId: campaignId,
                resultCampaignId: null,
                resultId: resultId,
                resultBattleInstanceId: battleInstanceId,
                hasStrongActiveBattleIdentity: true,
                activeBattleInstanceId: battleInstanceId,
                requireModernBattleInstanceMatch: false,
                isAlreadyApplied: _ => false);
        Assert(allowed.Decision == CoopBattleResultCampaignDecision.AllowLegacy &&
               string.Equals(allowed.JournalKey, resultId, StringComparison.Ordinal),
            "Legacy data with the exact active battle identity must be allowed once.");

        CoopBattleResultCampaignEvaluation fallbackKey =
            CoopBattleResultCampaignGuardContract.Evaluate(
                campaignBindingVersion: 0,
                hasActiveCampaign: true,
                activeCampaignId: campaignId,
                resultCampaignId: null,
                resultId: null,
                resultBattleInstanceId: battleInstanceId,
                hasStrongActiveBattleIdentity: true,
                activeBattleInstanceId: battleInstanceId,
                requireModernBattleInstanceMatch: false,
                isAlreadyApplied: _ => false);
        Assert(fallbackKey.Decision == CoopBattleResultCampaignDecision.AllowLegacy &&
               fallbackKey.JournalKey == "legacy:" + battleInstanceId,
            "Legacy data without ResultId must use the deterministic battle-instance key.");
    }

    private static void ValidateFailureDoesNotJournalAndCanRetry()
    {
        ActivateNewCampaignBehavior();
        BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId);
        string resultId = BuildResultId("Battle");
        var gate = new CoopBattleResultApplicationGate();

        Assert(gate.TryBegin(campaignId, resultId, out CoopBattleResultApplicationLease lease),
            "The first application attempt must acquire a lease.");
        Assert(gate.Fail(lease), "A failed attempt must release its lease.");
        Assert(!BattleResultWritebackJournalBehavior.IsConsumed(resultId),
            "A failed application must not enter the persistent journal.");
        Assert(gate.TryBegin(campaignId, resultId, out _),
            "A clean failure before mutation must remain retryable.");
    }

    private static void ValidateSuccessJournalsExactlyOnce()
    {
        ActivateNewCampaignBehavior();
        BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId);
        string resultId = BuildResultId("Battle");
        var gate = new CoopBattleResultApplicationGate();

        Assert(gate.TryBegin(campaignId, resultId, out CoopBattleResultApplicationLease lease),
            "A successful application must start with one lease.");
        Assert(BattleResultWritebackJournalBehavior.TryMarkConsumedAfterSuccess(resultId),
            "A successful application must enter the journal.");
        Assert(BattleResultWritebackJournalBehavior.TryMarkConsumedAfterSuccess(resultId),
            "Marking the same successful result twice must remain idempotent.");
        Assert(gate.Complete(lease), "A successful application must complete its lease.");
        Assert(BattleResultWritebackJournalBehavior.IsConsumed(resultId),
            "A completed result must be recognized in the same process.");
    }

    private static void ValidateConcurrentBeginAllowsOneMutation()
    {
        string campaignId = Guid.NewGuid().ToString("N");
        string resultId = BuildResultId("Battle");
        var gate = new CoopBattleResultApplicationGate();
        int acquiredCount = 0;
        CoopBattleResultApplicationLease winningLease = null;

        Parallel.For(0, 32, _ =>
        {
            if (!gate.TryBegin(campaignId, resultId, out CoopBattleResultApplicationLease lease))
                return;

            Interlocked.Increment(ref acquiredCount);
            Interlocked.CompareExchange(ref winningLease, lease, null);
        });

        Assert(acquiredCount == 1,
            "Concurrent reads of one result must permit exactly one campaign mutation.");
        Assert(gate.Complete(winningLease),
            "The winning concurrent lease must complete normally.");
    }

    private static void ValidateCrashRecoveryBeforeAndAfterSave()
    {
        BattleResultWritebackJournalBehavior s0Behavior = ActivateNewCampaignBehavior();
        BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string campaignId);
        var s0 = new FakeDataStore(isSaving: true);
        s0Behavior.SyncData(s0);
        string battleInstanceId = Guid.NewGuid().ToString("N");
        CoopBattleResultCampaignGuardContract.TryBuildStableResultId(
            battleInstanceId, "Battle", out string resultId);

        Assert(BattleResultWritebackJournalBehavior.TryMarkConsumedAfterSuccess(resultId),
            "The in-memory application must update the live journal.");

        var reloadedS0Behavior = new BattleResultWritebackJournalBehavior();
        ActivateCampaignBehavior(reloadedS0Behavior);
        reloadedS0Behavior.SyncData(s0.CreateLoader());
        Assert(BattleResultWritebackJournalBehavior.TryGetActiveCampaignId(out string reloadedCampaignId) &&
               reloadedCampaignId == campaignId,
            "Reloading S0 must restore the same campaign id.");
        Assert(!BattleResultWritebackJournalBehavior.IsConsumed(resultId),
            "Reloading S0 after a crash must not remember an unsaved result.");

        CoopBattleResultCampaignEvaluation recovery = EvaluateModern(
            reloadedCampaignId, reloadedCampaignId, resultId, battleInstanceId, _ => false);
        Assert(recovery.Decision == CoopBattleResultCampaignDecision.AllowModern,
            "The retained result file must be applicable again to the old S0 state.");

        Assert(BattleResultWritebackJournalBehavior.TryMarkConsumedAfterSuccess(resultId),
            "The recovered result must enter the live journal.");
        var s1 = new FakeDataStore(isSaving: true);
        reloadedS0Behavior.SyncData(s1);

        var reloadedS1Behavior = new BattleResultWritebackJournalBehavior();
        ActivateCampaignBehavior(reloadedS1Behavior);
        reloadedS1Behavior.SyncData(s1.CreateLoader());
        Assert(BattleResultWritebackJournalBehavior.IsConsumed(resultId),
            "After saving S1, the same retained file must be a no-op after load.");
    }

    private static void ValidateSnapshotAndResultJsonRoundTrips()
    {
        string campaignId = Guid.NewGuid().ToString("N");
        string battleInstanceId = Guid.NewGuid().ToString("N");
        CoopBattleResultCampaignGuardContract.TryBuildStableResultId(
            battleInstanceId, "SiegeAssault", out string resultId);

        var snapshot = new BattleSnapshotMessage
        {
            BattleId = "battle",
            BattleInstanceId = battleInstanceId,
            CampaignBindingVersion = CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion,
            CampaignId = campaignId
        };
        BattleSnapshotMessage snapshotRoundTrip =
            JsonConvert.DeserializeObject<BattleSnapshotMessage>(
                JsonConvert.SerializeObject(snapshot));
        Assert(snapshotRoundTrip.CampaignBindingVersion ==
               CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion &&
               snapshotRoundTrip.CampaignId == campaignId,
            "Snapshot JSON round trip must preserve the campaign binding.");

        var result = new CoopBattleResultBridgeFile.BattleResultSnapshot
        {
            BattleId = "battle",
            BattleInstanceId = battleInstanceId,
            CampaignBindingVersion = CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion,
            CampaignId = campaignId,
            ResultId = resultId
        };
        CoopBattleResultBridgeFile.BattleResultSnapshot resultRoundTrip =
            JsonConvert.DeserializeObject<CoopBattleResultBridgeFile.BattleResultSnapshot>(
                JsonConvert.SerializeObject(result));
        Assert(resultRoundTrip.CampaignBindingVersion ==
               CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion &&
               resultRoundTrip.CampaignId == campaignId &&
               resultRoundTrip.ResultId == resultId,
            "Result JSON round trip must preserve CampaignId and ResultId.");
    }

    private static void ValidateAutomationSuppressesCampaignConsumableResultsAcrossBattleTypes()
    {
        string[] variableNames =
        {
            CoopAutomationRuntimeBridge.TestAutomationVariable,
            CoopAutomationRuntimeBridge.RunIdVariable,
            CoopAutomationRuntimeBridge.RunRootVariable,
            CoopAutomationRuntimeBridge.RunTokenVariable,
            CoopAutomationRuntimeBridge.ExpectedModuleSha256Variable,
            CoopAutomationRuntimeBridge.ResultPolicyVariable
        };
        var previousValues = variableNames.ToDictionary(
            name => name,
            name => Environment.GetEnvironmentVariable(name),
            StringComparer.Ordinal);
        string runId = "result-suppress-" + Guid.NewGuid().ToString("N");
        string runRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "CoopSpectator",
            "Automation",
            runId));
        string token = "contract-result-token-" + Guid.NewGuid().ToString("N");
        string moduleHash = CoopAutomationRuntimeContract.ComputeFileSha256(
            Assembly.GetExecutingAssembly().Location);
        string globalResultPath = CoopBattleResultBridgeFile.GetResultFilePath();
        FileIdentity globalBefore = ReadFileIdentity(globalResultPath);

        try
        {
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.TestAutomationVariable, "1");
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.RunIdVariable, runId);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.RunRootVariable, runRoot);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.RunTokenVariable, token);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.ExpectedModuleSha256Variable, moduleHash);
            Environment.SetEnvironmentVariable(
                CoopAutomationRuntimeBridge.ResultPolicyVariable,
                CoopAutomationRuntimeContract.SuppressResultPolicy);

            foreach (string battleType in new[]
                     {
                         "Battle",
                         "Village",
                         "SiegeAssault",
                         "SallyOut",
                         "SiegeAmbush",
                         "Hideout",
                         "LordsHall"
                     })
            {
                var result = new CoopBattleResultBridgeFile.BattleResultSnapshot
                {
                    BattleId = "suppressed-" + battleType,
                    BattleType = battleType,
                    Source = "ContractTest"
                };
                Assert(
                    CoopBattleResultBridgeFile.WriteResult(result, out bool suppressed) && suppressed,
                    "A valid automation Suppress policy must absorb the " + battleType +
                    " result without publishing the campaign-consumable file.");
            }

            string publicationStatusPath = CoopAutomationRuntimeContract.CombineRunPath(
                runRoot,
                CoopAutomationRuntimeContract.ResultPublicationRelativePath);
            Assert(
                CoopAutomationProtocolFileIO.TryReadJson(
                    publicationStatusPath,
                    1024 * 1024,
                    out CoopAutomationResultPublicationStatus publicationStatus,
                    out string statusFailureCode,
                    out string statusFailureMessage),
                "The run-scoped result-publication status must be readable: " +
                statusFailureCode + ": " + statusFailureMessage);
            Assert(
                publicationStatus.RunId == runId &&
                publicationStatus.Decision == CoopAutomationResultPublicationDecision.Suppress.ToString() &&
                publicationStatus.BattleId == "suppressed-LordsHall",
                "The final run-scoped status must identify the exact run, suppression decision, and battle.");
            Assert(
                FileIdentity.Equals(globalBefore, ReadFileIdentity(globalResultPath)),
                "Suppressed results for every supported battle type must leave global battle_result.json unchanged.");

            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.ResultPolicyVariable, "Unsupported");
            var invalidResult = new CoopBattleResultBridgeFile.BattleResultSnapshot
            {
                BattleId = "invalid-policy",
                BattleType = "Battle",
                Source = "ContractTest"
            };
            Assert(
                !CoopBattleResultBridgeFile.WriteResult(invalidResult, out bool invalidSuppressed) &&
                !invalidSuppressed,
                "An enabled invalid automation profile must reject publication instead of claiming suppression or falling back to production.");
            Assert(
                FileIdentity.Equals(globalBefore, ReadFileIdentity(globalResultPath)),
                "An invalid enabled automation profile must leave global battle_result.json unchanged.");
        }
        finally
        {
            foreach (KeyValuePair<string, string> pair in previousValues)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
        }
    }

    private sealed class FileIdentity
    {
        public bool Exists { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }

        public static bool Equals(FileIdentity left, FileIdentity right)
        {
            return left.Exists == right.Exists &&
                   left.Length == right.Length &&
                   string.Equals(left.Sha256, right.Sha256, StringComparison.Ordinal);
        }
    }

    private static FileIdentity ReadFileIdentity(string path)
    {
        if (!File.Exists(path))
            return new FileIdentity { Exists = false, Length = 0, Sha256 = string.Empty };
        return new FileIdentity
        {
            Exists = true,
            Length = new FileInfo(path).Length,
            Sha256 = CoopAutomationRuntimeContract.ComputeFileSha256(path)
        };
    }

    private static void ValidateOldJsonUsesControlledLegacyBranch()
    {
        string campaignId = Guid.NewGuid().ToString("N");
        string battleInstanceId = Guid.NewGuid().ToString("N");
        CoopBattleResultCampaignGuardContract.TryBuildStableResultId(
            battleInstanceId, "Battle", out string resultId);
        string oldJson =
            "{\"BattleId\":\"legacy\",\"BattleInstanceId\":\"" +
            battleInstanceId + "\",\"ResultId\":\"" + resultId + "\"}";
        CoopBattleResultBridgeFile.BattleResultSnapshot oldResult =
            JsonConvert.DeserializeObject<CoopBattleResultBridgeFile.BattleResultSnapshot>(oldJson);
        Assert(oldResult.CampaignBindingVersion == 0 && oldResult.CampaignId == null,
            "Old JSON must deserialize without an exception and remain explicitly legacy.");

        CoopBattleResultCampaignEvaluation rejected =
            CoopBattleResultCampaignGuardContract.Evaluate(
                oldResult.CampaignBindingVersion,
                hasActiveCampaign: true,
                activeCampaignId: campaignId,
                resultCampaignId: oldResult.CampaignId,
                resultId: oldResult.ResultId,
                resultBattleInstanceId: oldResult.BattleInstanceId,
                hasStrongActiveBattleIdentity: false,
                activeBattleInstanceId: null,
                requireModernBattleInstanceMatch: false,
                isAlreadyApplied: _ => false);
        Assert(rejected.Decision ==
               CoopBattleResultCampaignDecision.RejectMissingStrongLegacyBattleIdentity,
            "Old JSON must fail closed without the exact active battle identity.");
    }

    private static void ValidateJournalBoundIsDeterministic()
    {
        BattleResultWritebackJournalBehavior behavior = ActivateNewCampaignBehavior();
        var resultIds = new List<string>();
        for (int index = 0; index < 70; index++)
        {
            string resultId = BuildResultId("Battle" + index);
            resultIds.Add(resultId);
            Assert(BattleResultWritebackJournalBehavior.TryMarkConsumedAfterSuccess(resultId),
                "Every successful test result must enter the live journal.");
        }

        var saveStore = new FakeDataStore(isSaving: true);
        behavior.SyncData(saveStore);
        List<string> saved = saveStore.ReadStringList("CoopSpectatorConsumedBattleResultIds");
        Assert(saved.Count == CoopBattleResultCampaignGuardContract.MaxRememberedResultIds,
            "The persistent journal must remain bounded.");
        Assert(saved.First() == resultIds[6] && saved.Last() == resultIds[69],
            "Journal eviction must remove the oldest ids and retain the current file id.");
    }

    private static void ValidateSourceOrderingAndResultFileRetention()
    {
        string repositoryRoot = FindRepositoryRoot();
        string detectorSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Campaign", "BattleDetector.cs"));
        Assert(detectorSource.IndexOf(
                "CoopBattleResultBridgeFile.ClearResult",
                StringComparison.Ordinal) < 0,
            "BattleDetector must retain battle_result.json until a separately proven durable cleanup path exists.");

        string missionSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Mission", "CoopMissionBehaviors.cs"));
        Assert(missionSource.IndexOf(
                "CoopBattleResultBridgeFile.ClearResult",
                StringComparison.Ordinal) < 0,
            "Dedicated mission initialization must not delete the retained battle result.");

        int missionExitStart = detectorSource.IndexOf(
            "private void TryHandleBattleResultMissionExit()",
            StringComparison.Ordinal);
        int missionExitEnd = detectorSource.IndexOf(
            "private void TryCacheHostAftermathRewardProjection",
            missionExitStart,
            StringComparison.Ordinal);
        string missionExitBody = detectorSource.Substring(
            missionExitStart,
            missionExitEnd - missionExitStart);
        int validationIndex = missionExitBody.IndexOf(
            "EvaluateBattleResultForActiveCampaign",
            StringComparison.Ordinal);
        int firstMutationIndex = missionExitBody.IndexOf(
            "TryApplyMissionSiegeEngineResult",
            StringComparison.Ordinal);
        Assert(validationIndex >= 0 && firstMutationIndex > validationIndex,
            "Campaign and result validation must happen before the first mission-exit mutation.");

        int consumeStart = detectorSource.IndexOf(
            "private void TryConsumeBattleResultWritebackAudit()",
            StringComparison.Ordinal);
        int consumeEnd = detectorSource.IndexOf(
            "private static BattleResultWritebackSummary ApplyBattleResultWriteback",
            consumeStart,
            StringComparison.Ordinal);
        string consumeBody = detectorSource.Substring(consumeStart, consumeEnd - consumeStart);
        int applyIndex = consumeBody.IndexOf("ApplyBattleResultWriteback(", StringComparison.Ordinal);
        int journalIndex = consumeBody.IndexOf("TryMarkConsumedAfterSuccess", StringComparison.Ordinal);
        Assert(applyIndex >= 0 && journalIndex > applyIndex,
            "The persistent journal must be updated only after result application.");
    }

    private static CoopBattleResultCampaignEvaluation EvaluateModern(
        string activeCampaignId,
        string resultCampaignId,
        string resultId,
        string battleInstanceId,
        Func<string, bool> isAlreadyApplied)
    {
        return CoopBattleResultCampaignGuardContract.Evaluate(
            CoopBattleResultCampaignGuardContract.CurrentCampaignBindingVersion,
            hasActiveCampaign: true,
            activeCampaignId: activeCampaignId,
            resultCampaignId: resultCampaignId,
            resultId: resultId,
            resultBattleInstanceId: battleInstanceId,
            hasStrongActiveBattleIdentity: false,
            activeBattleInstanceId: null,
            requireModernBattleInstanceMatch: false,
            isAlreadyApplied: isAlreadyApplied);
    }

    private static string BuildResultId(string stage)
    {
        Assert(CoopBattleResultCampaignGuardContract.TryBuildStableResultId(
                Guid.NewGuid().ToString("N"), stage, out string resultId),
            "Test setup must build a valid result id.");
        return resultId;
    }

    private static BattleResultWritebackJournalBehavior ActivateNewCampaignBehavior()
    {
        var behavior = new BattleResultWritebackJournalBehavior();
        ActivateCampaignBehavior(behavior);
        return behavior;
    }

    private static void ActivateCampaignBehavior(BattleResultWritebackJournalBehavior behavior)
    {
        var campaign = new TaleWorlds.CampaignSystem.Campaign();
        campaign.SetBehavior(behavior);
        TaleWorlds.CampaignSystem.Campaign.Current = campaign;
    }

    private static string FindRepositoryRoot()
    {
        string configuredRoot = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string resolvedRoot = Path.GetFullPath(configuredRoot);
            if (File.Exists(Path.Combine(resolvedRoot, "CoopSpectator.csproj")))
                return resolvedRoot;
            throw new InvalidOperationException("COOPSPECTATOR_REPOSITORY_ROOT does not identify this repository.");
        }

        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CoopSpectator.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
