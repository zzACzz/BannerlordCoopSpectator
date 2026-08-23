using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.DedicatedServer
{
    internal enum SandBoxSceneScriptRegistrationDecisionKind
    {
        NoOp,
        Register,
        Reject
    }

    internal sealed class SandBoxSceneScriptRegistrationCandidate
    {
        public SandBoxSceneScriptRegistrationCandidate(
            string name,
            string fullName,
            string sourceAssemblyIdentity,
            bool isManagedEngineType)
        {
            Name = name ?? string.Empty;
            FullName = fullName ?? string.Empty;
            SourceAssemblyIdentity = sourceAssemblyIdentity ?? string.Empty;
            IsManagedEngineType = isManagedEngineType;
        }

        public string Name { get; }

        public string FullName { get; }

        public string SourceAssemblyIdentity { get; }

        public bool IsManagedEngineType { get; }
    }

    internal sealed class SandBoxSceneScriptRegisteredType
    {
        public SandBoxSceneScriptRegisteredType(
            string name,
            string fullName,
            string sourceAssemblyIdentity)
        {
            Name = name ?? string.Empty;
            FullName = fullName ?? string.Empty;
            SourceAssemblyIdentity = sourceAssemblyIdentity ?? string.Empty;
        }

        public string Name { get; }

        public string FullName { get; }

        public string SourceAssemblyIdentity { get; }
    }

    internal sealed class SandBoxSceneScriptRegistrationDecision
    {
        public SandBoxSceneScriptRegistrationDecision(
            SandBoxSceneScriptRegistrationDecisionKind kind,
            string reason,
            IReadOnlyList<string> typeNamesToRegister)
        {
            Kind = kind;
            Reason = reason ?? string.Empty;
            TypeNamesToRegister = typeNamesToRegister ?? Array.Empty<string>();
        }

        public SandBoxSceneScriptRegistrationDecisionKind Kind { get; }

        public string Reason { get; }

        public IReadOnlyList<string> TypeNamesToRegister { get; }
    }

    internal static class SandBoxSceneScriptRegistrationContract
    {
        private const string ExpectedAssemblySimpleName = "SandBox";

        private static readonly string[] RequiredTypeFullNames =
        {
            "SandBox.Objects.AnimationPoints.AnimationPoint",
            "SandBox.Objects.AnimationPoints.ChairUsePoint",
            "SandBox.Objects.AreaMarkers.CommonAreaMarker",
            "SandBox.Objects.AreaMarkers.WorkshopAreaMarker"
        };

        public static SandBoxSceneScriptRegistrationDecision Resolve(
            bool isDedicatedServer,
            string sourceAssemblySimpleName,
            IReadOnlyCollection<SandBoxSceneScriptRegistrationCandidate> candidates,
            IReadOnlyCollection<SandBoxSceneScriptRegisteredType> registeredTypes)
        {
            if (!isDedicatedServer)
                return NoOp("not-dedicated-server");

            if (!string.Equals(
                    sourceAssemblySimpleName ?? string.Empty,
                    ExpectedAssemblySimpleName,
                    StringComparison.Ordinal))
            {
                return Reject("source-assembly-not-sandbox");
            }

            if (candidates == null)
                return Reject("candidate-set-missing");

            List<SandBoxSceneScriptRegistrationCandidate> managedCandidates =
                candidates.Where(candidate => candidate?.IsManagedEngineType == true).ToList();
            if (managedCandidates.Count == 0)
                return Reject("managed-candidate-set-empty");

            if (managedCandidates.Any(candidate =>
                    string.IsNullOrWhiteSpace(candidate.Name) ||
                    string.IsNullOrWhiteSpace(candidate.FullName) ||
                    string.IsNullOrWhiteSpace(candidate.SourceAssemblyIdentity)))
            {
                return Reject("managed-candidate-invalid");
            }

            string duplicateCandidateName = managedCandidates
                .GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(duplicateCandidateName))
                return Reject("managed-candidate-name-duplicate:" + duplicateCandidateName);

            HashSet<string> candidateFullNames = new HashSet<string>(
                managedCandidates.Select(candidate => candidate.FullName),
                StringComparer.Ordinal);
            string missingRequiredType = RequiredTypeFullNames
                .FirstOrDefault(requiredType => !candidateFullNames.Contains(requiredType));
            if (!string.IsNullOrEmpty(missingRequiredType))
                return Reject("required-scene-script-type-missing:" + missingRequiredType);

            Dictionary<string, SandBoxSceneScriptRegisteredType> registeredByName =
                new Dictionary<string, SandBoxSceneScriptRegisteredType>(StringComparer.Ordinal);
            if (registeredTypes != null)
            {
                foreach (SandBoxSceneScriptRegisteredType registeredType in registeredTypes)
                {
                    if (registeredType == null || string.IsNullOrWhiteSpace(registeredType.Name))
                        return Reject("registered-type-invalid");

                    if (registeredByName.ContainsKey(registeredType.Name))
                        return Reject("registered-type-name-duplicate:" + registeredType.Name);

                    registeredByName.Add(registeredType.Name, registeredType);
                }
            }

            List<string> namesToRegister = new List<string>(managedCandidates.Count);
            foreach (SandBoxSceneScriptRegistrationCandidate candidate in managedCandidates)
            {
                if (!registeredByName.TryGetValue(candidate.Name, out SandBoxSceneScriptRegisteredType registeredType))
                {
                    namesToRegister.Add(candidate.Name);
                    continue;
                }

                bool isExactExistingType =
                    string.Equals(candidate.FullName, registeredType.FullName, StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.SourceAssemblyIdentity,
                        registeredType.SourceAssemblyIdentity,
                        StringComparison.Ordinal);
                if (!isExactExistingType)
                    return Reject("registered-type-name-conflict:" + candidate.Name);
            }

            return namesToRegister.Count == 0
                ? NoOp("sandbox-scene-script-types-already-registered")
                : new SandBoxSceneScriptRegistrationDecision(
                    SandBoxSceneScriptRegistrationDecisionKind.Register,
                    "sandbox-scene-script-types-ready",
                    namesToRegister);
        }

        private static SandBoxSceneScriptRegistrationDecision NoOp(string reason)
        {
            return new SandBoxSceneScriptRegistrationDecision(
                SandBoxSceneScriptRegistrationDecisionKind.NoOp,
                reason,
                Array.Empty<string>());
        }

        private static SandBoxSceneScriptRegistrationDecision Reject(string reason)
        {
            return new SandBoxSceneScriptRegistrationDecision(
                SandBoxSceneScriptRegistrationDecisionKind.Reject,
                reason,
                Array.Empty<string>());
        }
    }
}
