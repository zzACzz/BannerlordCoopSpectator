using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.DedicatedServer;

internal static class Program
{
    private const string SandBoxIdentity = "SandBox, Version=1.0.0.0";

    private static readonly string[] RequiredTypeFullNames =
    {
        "SandBox.Objects.AnimationPoints.AnimationPoint",
        "SandBox.Objects.AnimationPoints.ChairUsePoint",
        "SandBox.Objects.AreaMarkers.CommonAreaMarker",
        "SandBox.Objects.AreaMarkers.WorkshopAreaMarker"
    };

    private static int Main()
    {
        try
        {
            ValidateNonDedicatedProcessIsNoOp();
            ValidateWrongAssemblyIsRejected();
            ValidateMissingRequiredTypeIsRejected();
            ValidateForeignNameCollisionIsRejected();
            ValidateDuplicateCandidateNameIsRejected();
            ValidateManagedTypesAreSelectedExactly();
            ValidateExactReapplicationIsNoOp();
            Console.WriteLine("Coop siege scene-script registration contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateNonDedicatedProcessIsNoOp()
    {
        SandBoxSceneScriptRegistrationDecision decision =
            SandBoxSceneScriptRegistrationContract.Resolve(
                isDedicatedServer: false,
                sourceAssemblySimpleName: "SandBox",
                candidates: Array.Empty<SandBoxSceneScriptRegistrationCandidate>(),
                registeredTypes: Array.Empty<SandBoxSceneScriptRegisteredType>());

        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.NoOp,
            decision.Kind,
            "A non-dedicated process must not register dedicated scene-script types.");
    }

    private static void ValidateWrongAssemblyIsRejected()
    {
        SandBoxSceneScriptRegistrationDecision decision = Resolve(
            candidates: BuildRequiredCandidates(),
            sourceAssemblySimpleName: "StoryMode");

        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.Reject,
            decision.Kind,
            "A similarly shaped foreign assembly must be rejected.");
    }

    private static void ValidateMissingRequiredTypeIsRejected()
    {
        List<SandBoxSceneScriptRegistrationCandidate> candidates = BuildRequiredCandidates();
        candidates.RemoveAt(candidates.Count - 1);

        SandBoxSceneScriptRegistrationDecision decision = Resolve(candidates);
        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.Reject,
            decision.Kind,
            "A partial SandBox scene-script type set must fail closed.");
        Assert(
            decision.Reason.StartsWith("required-scene-script-type-missing:", StringComparison.Ordinal),
            "A missing required type must remain diagnosable.");
    }

    private static void ValidateForeignNameCollisionIsRejected()
    {
        List<SandBoxSceneScriptRegistrationCandidate> candidates = BuildRequiredCandidates();
        SandBoxSceneScriptRegistrationCandidate animationPoint = candidates[0];
        var existing = new[]
        {
            new SandBoxSceneScriptRegisteredType(
                animationPoint.Name,
                "ForeignMod.AnimationPoint",
                "ForeignMod, Version=1.0.0.0")
        };

        SandBoxSceneScriptRegistrationDecision decision = Resolve(candidates, existing);
        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.Reject,
            decision.Kind,
            "A foreign managed type with the same registry name must fail closed.");
        AssertEqual(
            "registered-type-name-conflict:AnimationPoint",
            decision.Reason,
            "A registry collision must identify the conflicting name.");
    }

    private static void ValidateDuplicateCandidateNameIsRejected()
    {
        List<SandBoxSceneScriptRegistrationCandidate> candidates = BuildRequiredCandidates();
        candidates.Add(new SandBoxSceneScriptRegistrationCandidate(
            "AnimationPoint",
            "SandBox.Other.AnimationPoint",
            SandBoxIdentity,
            isManagedEngineType: true));

        SandBoxSceneScriptRegistrationDecision decision = Resolve(candidates);
        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.Reject,
            decision.Kind,
            "Duplicate registry names inside SandBox must fail closed.");
    }

    private static void ValidateManagedTypesAreSelectedExactly()
    {
        List<SandBoxSceneScriptRegistrationCandidate> candidates = BuildRequiredCandidates();
        candidates.Add(new SandBoxSceneScriptRegistrationCandidate(
            "CampaignOnlyHelper",
            "SandBox.CampaignOnlyHelper",
            SandBoxIdentity,
            isManagedEngineType: false));

        SandBoxSceneScriptRegistrationDecision decision = Resolve(candidates);
        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.Register,
            decision.Kind,
            "A complete unregistered SandBox scene-script set must be registered.");
        AssertEqual(
            RequiredTypeFullNames.Length,
            decision.TypeNamesToRegister.Count,
            "Only managed engine types may enter the registration plan.");
        Assert(
            !decision.TypeNamesToRegister.Contains("CampaignOnlyHelper", StringComparer.Ordinal),
            "An ordinary campaign class must never enter the engine type registry.");
    }

    private static void ValidateExactReapplicationIsNoOp()
    {
        List<SandBoxSceneScriptRegistrationCandidate> candidates = BuildRequiredCandidates();
        List<SandBoxSceneScriptRegisteredType> existing = candidates
            .Select(candidate => new SandBoxSceneScriptRegisteredType(
                candidate.Name,
                candidate.FullName,
                candidate.SourceAssemblyIdentity))
            .ToList();

        SandBoxSceneScriptRegistrationDecision decision = Resolve(candidates, existing);
        AssertEqual(
            SandBoxSceneScriptRegistrationDecisionKind.NoOp,
            decision.Kind,
            "Exact reapplication must be idempotent.");
        AssertEqual(
            "sandbox-scene-script-types-already-registered",
            decision.Reason,
            "Exact reapplication must have a stable no-op reason.");
    }

    private static SandBoxSceneScriptRegistrationDecision Resolve(
        IReadOnlyCollection<SandBoxSceneScriptRegistrationCandidate> candidates,
        IReadOnlyCollection<SandBoxSceneScriptRegisteredType> existing = null,
        string sourceAssemblySimpleName = "SandBox")
    {
        return SandBoxSceneScriptRegistrationContract.Resolve(
            isDedicatedServer: true,
            sourceAssemblySimpleName: sourceAssemblySimpleName,
            candidates: candidates,
            registeredTypes: existing ?? Array.Empty<SandBoxSceneScriptRegisteredType>());
    }

    private static List<SandBoxSceneScriptRegistrationCandidate> BuildRequiredCandidates()
    {
        return RequiredTypeFullNames
            .Select(fullName => new SandBoxSceneScriptRegistrationCandidate(
                fullName.Substring(fullName.LastIndexOf('.') + 1),
                fullName,
                SandBoxIdentity,
                isManagedEngineType: true))
            .ToList();
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                message + " Expected=" + expected + " Actual=" + actual + ".");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
