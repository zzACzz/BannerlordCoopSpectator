using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal sealed class SiegeMissionObjectIdMapEntry
    {
        public SiegeMissionObjectIdMapEntry(
            BattleSideEnum objectSide,
            MissionObjectId missionObjectId,
            string signature,
            string objectTypeName,
            string entityName)
        {
            ObjectSide = objectSide;
            MissionObjectId = missionObjectId;
            Signature = signature ?? string.Empty;
            ObjectTypeName = objectTypeName ?? string.Empty;
            EntityName = entityName ?? string.Empty;
        }

        public BattleSideEnum ObjectSide { get; }
        public MissionObjectId MissionObjectId { get; }
        public string Signature { get; }
        public string ObjectTypeName { get; }
        public string EntityName { get; }
    }

    internal static class SiegeMissionObjectIdMapRuntime
    {
        private const float PositionQuantization = 0.05f;
        private const int EntityPathDepth = 6;
        private static readonly object Sync = new object();
        private static ClientIndexCache _clientIndexCache;

        private sealed class Candidate
        {
            public MissionObject MissionObject;
            public BattleSideEnum ObjectSide;
            public string Signature;
            public string ObjectTypeName;
            public string EntityName;
        }

        private sealed class ClientIndexCache
        {
            public int MissionHash;
            public string SceneName;
            public BattleSideEnum RequestedSide;
            public Dictionary<string, List<MissionObject>> ObjectsBySignature;
            public int CandidateCount;
            public int UniqueSignatureCount;
            public int AmbiguousSignatureCount;
        }

        public static void ClearClientCache(Mission mission)
        {
            lock (Sync)
            {
                if (_clientIndexCache == null)
                    return;

                if (mission == null || _clientIndexCache.MissionHash == RuntimeHelpers.GetHashCode(mission))
                    _clientIndexCache = null;
            }
        }

        public static List<SiegeMissionObjectIdMapEntry> BuildServerEntries(
            Mission mission,
            BattleSideEnum requestedSide,
            out string diagnostics)
        {
            diagnostics = "invalid-context";
            var result = new List<SiegeMissionObjectIdMapEntry>();
            if (mission == null)
                return result;

            List<Candidate> candidates = BuildCandidates(mission, requestedSide);
            List<IGrouping<string, Candidate>> groups = candidates
                .Where(candidate => candidate != null && !string.IsNullOrWhiteSpace(candidate.Signature))
                .GroupBy(candidate => candidate.Signature, StringComparer.Ordinal)
                .ToList();
            int ambiguousCount = groups.Count(group => group.Count() != 1);

            foreach (IGrouping<string, Candidate> group in groups)
            {
                if (group.Count() != 1)
                    continue;

                Candidate candidate = group.First();
                MissionObject missionObject = candidate.MissionObject;
                if (missionObject == null || missionObject.Id.Id < 0)
                    continue;

                result.Add(
                    new SiegeMissionObjectIdMapEntry(
                        candidate.ObjectSide,
                        missionObject.Id,
                        candidate.Signature,
                        candidate.ObjectTypeName,
                        candidate.EntityName));
            }

            diagnostics =
                "Scene=" + (mission.SceneName ?? string.Empty) +
                " RequestedSide=" + requestedSide +
                " Candidates=" + candidates.Count +
                " UniqueEntries=" + result.Count +
                " AmbiguousSignatures=" + ambiguousCount;
            return result;
        }

        public static bool TryApplyClientEntry(
            Mission mission,
            BattleSideEnum objectSide,
            MissionObjectId serverMissionObjectId,
            string signature,
            string objectTypeName,
            out string diagnostics)
        {
            diagnostics = "invalid-context";
            if (mission == null)
                return false;

            if (serverMissionObjectId.Id < 0)
            {
                diagnostics = "invalid-server-id:" + serverMissionObjectId.Id;
                return false;
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                diagnostics = "empty-signature ServerId=" + serverMissionObjectId.Id;
                return false;
            }

            Dictionary<string, List<MissionObject>> index = GetOrBuildClientIndex(
                mission,
                objectSide,
                out string indexDiagnostics);
            if (index == null || !index.TryGetValue(signature, out List<MissionObject> matches))
            {
                diagnostics =
                    "not-found ServerId=" + serverMissionObjectId.Id +
                    " Side=" + objectSide +
                    " Index={" + indexDiagnostics + "}";
                return false;
            }

            if (matches == null || matches.Count != 1)
            {
                diagnostics =
                    "ambiguous-local-match ServerId=" + serverMissionObjectId.Id +
                    " Side=" + objectSide +
                    " Matches=" + (matches?.Count ?? 0) +
                    " Index={" + indexDiagnostics + "}";
                return false;
            }

            MissionObject localMissionObject = matches[0];
            if (!IsObjectTypeMatch(localMissionObject, objectTypeName))
            {
                diagnostics =
                    "type-mismatch ServerId=" + serverMissionObjectId.Id +
                    " Expected=" + (objectTypeName ?? string.Empty) +
                    " Actual=" + (localMissionObject?.GetType().FullName ?? "<null>") +
                    " Index={" + indexDiagnostics + "}";
                return false;
            }

            string registrationDiagnostics = SiegeMissionObjectIdBridge.RegisterMissionObjectMapping(
                serverMissionObjectId,
                localMissionObject,
                objectSide,
                objectTypeName,
                "SiegeMissionObjectIdMapRuntime.TryApplyClientEntry");

            diagnostics =
                "registered ServerId=" + serverMissionObjectId.Id +
                " LocalId=" + localMissionObject.Id.Id +
                " Side=" + objectSide +
                " Type=" + (objectTypeName ?? string.Empty) +
                " Index={" + indexDiagnostics + "}" +
                " Registration={" + registrationDiagnostics + "}";
            return true;
        }

        private static Dictionary<string, List<MissionObject>> GetOrBuildClientIndex(
            Mission mission,
            BattleSideEnum requestedSide,
            out string diagnostics)
        {
            diagnostics = "invalid-context";
            if (mission == null)
                return new Dictionary<string, List<MissionObject>>(StringComparer.Ordinal);

            int missionHash = RuntimeHelpers.GetHashCode(mission);
            string sceneName = mission.SceneName ?? string.Empty;
            lock (Sync)
            {
                if (_clientIndexCache != null &&
                    _clientIndexCache.MissionHash == missionHash &&
                    string.Equals(_clientIndexCache.SceneName, sceneName, StringComparison.Ordinal) &&
                    _clientIndexCache.RequestedSide == requestedSide)
                {
                    diagnostics =
                        "cached Scene=" + sceneName +
                        " Side=" + requestedSide +
                        " Candidates=" + _clientIndexCache.CandidateCount +
                        " Unique=" + _clientIndexCache.UniqueSignatureCount +
                        " Ambiguous=" + _clientIndexCache.AmbiguousSignatureCount;
                    return _clientIndexCache.ObjectsBySignature;
                }
            }

            List<Candidate> candidates = BuildCandidates(mission, requestedSide);
            Dictionary<string, List<MissionObject>> objectsBySignature = candidates
                .Where(candidate => candidate != null && !string.IsNullOrWhiteSpace(candidate.Signature))
                .GroupBy(candidate => candidate.Signature, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(candidate => candidate.MissionObject)
                        .Where(missionObject => missionObject != null)
                        .ToList(),
                    StringComparer.Ordinal);
            int ambiguousCount = objectsBySignature.Count(pair => pair.Value == null || pair.Value.Count != 1);
            int uniqueCount = objectsBySignature.Count - ambiguousCount;

            lock (Sync)
            {
                _clientIndexCache = new ClientIndexCache
                {
                    MissionHash = missionHash,
                    SceneName = sceneName,
                    RequestedSide = requestedSide,
                    ObjectsBySignature = objectsBySignature,
                    CandidateCount = candidates.Count,
                    UniqueSignatureCount = uniqueCount,
                    AmbiguousSignatureCount = ambiguousCount
                };
            }

            diagnostics =
                "rebuilt Scene=" + sceneName +
                " Side=" + requestedSide +
                " Candidates=" + candidates.Count +
                " Unique=" + uniqueCount +
                " Ambiguous=" + ambiguousCount;
            return objectsBySignature;
        }

        private static List<Candidate> BuildCandidates(Mission mission, BattleSideEnum requestedSide)
        {
            var result = new List<Candidate>();
            if (mission == null)
                return result;

            foreach (MissionObject missionObject in EnumerateMissionObjects(mission))
            {
                if (!IsRelevantMissionObject(missionObject, requestedSide, out BattleSideEnum objectSide))
                    continue;

                if (!TryBuildSignature(
                        missionObject,
                        objectSide,
                        out string signature,
                        out string objectTypeName,
                        out string entityName))
                {
                    continue;
                }

                result.Add(
                    new Candidate
                    {
                        MissionObject = missionObject,
                        ObjectSide = objectSide,
                        Signature = signature,
                        ObjectTypeName = objectTypeName,
                        EntityName = entityName
                    });
            }

            return result;
        }

        private static IEnumerable<MissionObject> EnumerateMissionObjects(Mission mission)
        {
            var result = new List<MissionObject>();
            var seenIds = new HashSet<int>();

            AddMissionObjects(result, seenIds, SafeEnumerateMissionObjects(mission?.ActiveMissionObjects));
            AddMissionObjects(result, seenIds, SafeEnumerateMissionObjects(mission?.MissionObjects));
            return result;
        }

        private static IEnumerable<MissionObject> SafeEnumerateMissionObjects(IEnumerable<MissionObject> missionObjects)
        {
            try
            {
                return missionObjects?.Where(missionObject => missionObject != null).ToList() ??
                       new List<MissionObject>();
            }
            catch
            {
                return new List<MissionObject>();
            }
        }

        private static void AddMissionObjects(
            ICollection<MissionObject> output,
            ISet<int> seenIds,
            IEnumerable<MissionObject> missionObjects)
        {
            if (output == null || seenIds == null || missionObjects == null)
                return;

            foreach (MissionObject missionObject in missionObjects)
            {
                if (missionObject == null || missionObject.Id.Id < 0)
                    continue;

                if (!seenIds.Add(missionObject.Id.Id))
                    continue;

                output.Add(missionObject);
            }
        }

        private static bool IsRelevantMissionObject(
            MissionObject missionObject,
            BattleSideEnum requestedSide,
            out BattleSideEnum objectSide)
        {
            objectSide = BattleSideEnum.None;
            if (missionObject == null || missionObject.Id.Id < 0)
                return false;

            if (!TryGetEntityPosition(missionObject, out Vec3 _))
                return false;

            objectSide = ResolveObjectSide(missionObject);
            if (requestedSide != BattleSideEnum.None &&
                objectSide != BattleSideEnum.None &&
                objectSide != requestedSide)
            {
                return false;
            }

            return missionObject is SynchedMissionObject ||
                   missionObject is DeploymentPoint;
        }

        private static BattleSideEnum ResolveObjectSide(MissionObject missionObject)
        {
            try
            {
                if (missionObject is DeploymentPoint deploymentPoint)
                    return deploymentPoint.Side;

                if (missionObject is SiegeWeapon siegeWeapon)
                    return siegeWeapon.Side;
            }
            catch
            {
            }

            return BattleSideEnum.None;
        }

        private static bool TryBuildSignature(
            MissionObject missionObject,
            BattleSideEnum objectSide,
            out string signature,
            out string objectTypeName,
            out string entityName)
        {
            signature = string.Empty;
            objectTypeName = string.Empty;
            entityName = string.Empty;

            if (missionObject == null || !TryGetEntityPosition(missionObject, out Vec3 position))
                return false;

            objectTypeName = missionObject.GetType().FullName ?? missionObject.GetType().Name;
            entityName = SafeGetEntityName(missionObject.GameEntity);
            string positionKey = BuildPositionKey(position);
            string entityPath = BuildEntityPath(missionObject.GameEntity);

            signature =
                "type=" + SanitizeSignaturePart(objectTypeName) +
                "|side=" + objectSide +
                "|entity=" + SanitizeSignaturePart(entityName) +
                "|pos=" + positionKey +
                "|path=" + SanitizeSignaturePart(entityPath);
            return true;
        }

        private static bool TryGetEntityPosition(MissionObject missionObject, out Vec3 position)
        {
            position = Vec3.Zero;
            if (missionObject == null)
                return false;

            try
            {
                WeakGameEntity gameEntity = missionObject.GameEntity;
                if (!gameEntity.IsValid)
                    return false;

                position = gameEntity.GlobalPosition;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsObjectTypeMatch(MissionObject missionObject, string objectTypeName)
        {
            if (missionObject == null || string.IsNullOrWhiteSpace(objectTypeName))
                return false;

            Type actualType = missionObject.GetType();
            return string.Equals(actualType.FullName, objectTypeName, StringComparison.Ordinal) ||
                   string.Equals(actualType.Name, objectTypeName, StringComparison.Ordinal);
        }

        private static string BuildPositionKey(Vec3 position)
        {
            return Quantize(position.x).ToString(CultureInfo.InvariantCulture) + "," +
                   Quantize(position.y).ToString(CultureInfo.InvariantCulture) + "," +
                   Quantize(position.z).ToString(CultureInfo.InvariantCulture);
        }

        private static int Quantize(float value)
        {
            return (int)Math.Round(value / PositionQuantization, MidpointRounding.AwayFromZero);
        }

        private static string BuildEntityPath(WeakGameEntity gameEntity)
        {
            try
            {
                if (!gameEntity.IsValid)
                    return "invalid";

                var names = new List<string>();
                WeakGameEntity current = gameEntity;
                int depth = 0;
                while (current.IsValid && depth < EntityPathDepth)
                {
                    names.Add(SafeGetEntityName(current));
                    current = current.Parent;
                    depth++;
                }

                names.Reverse();
                return string.Join("/", names.ToArray());
            }
            catch
            {
                return "failed";
            }
        }

        private static string SafeGetEntityName(WeakGameEntity gameEntity)
        {
            try
            {
                if (!gameEntity.IsValid)
                    return "invalid";

                return string.IsNullOrWhiteSpace(gameEntity.Name) ? "unnamed" : gameEntity.Name;
            }
            catch
            {
                return "failed";
            }
        }

        private static string SanitizeSignaturePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Trim()
                .Replace("|", "/")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
