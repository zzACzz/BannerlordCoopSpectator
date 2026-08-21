using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateInspectionScope();
            ValidateAbsentAgentDoesNotInventSlot();
            ValidateSingleMatchIsUnchanged();
            ValidateStoredCoordinatesWin();
            ValidateFallbackIsDeterministic();
            ValidateRepeatedApplicationIsNoOp();
            ValidateOtherAgentsRemainUnchanged();
            Console.WriteLine(
                "Coop siege formation membership safety contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateInspectionScope()
    {
        Assert(
            CoopSiegeFormationMembershipSafetyContract.ShouldInspect(
                isServer: true,
                isExactCampaignSiege: true,
                hasBoundMissionPeer: true),
            "The authoritative exact campaign siege player must be inspected.");
        Assert(
            !CoopSiegeFormationMembershipSafetyContract.ShouldInspect(
                isServer: false,
                isExactCampaignSiege: true,
                hasBoundMissionPeer: true),
            "A client must remain a no-op.");
        Assert(
            !CoopSiegeFormationMembershipSafetyContract.ShouldInspect(
                isServer: true,
                isExactCampaignSiege: false,
                hasBoundMissionPeer: true),
            "A non-campaign-siege mission must remain a no-op.");
        Assert(
            !CoopSiegeFormationMembershipSafetyContract.ShouldInspect(
                isServer: true,
                isExactCampaignSiege: true,
                hasBoundMissionPeer: false),
            "An AI agent or an agent without a bound MissionPeer must remain a no-op.");
    }

    private static void ValidateAbsentAgentDoesNotInventSlot()
    {
        var matches =
            new List<CoopSiegeFormationPositionedMatch>();
        int canonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(-1, -1, matches);

        AssertEqual(
            -1,
            canonicalIndex,
            "An absent agent must not receive an invented canonical slot.");
        AssertEqual(
            0,
            CoopSiegeFormationMembershipSafetyContract
                .ResolveRedundantMatchIndices(
                    canonicalIndex,
                    matches.Count)
                .Length,
            "An absent agent must not produce a repair plan.");
    }

    private static void ValidateSingleMatchIsUnchanged()
    {
        var matches =
            new List<CoopSiegeFormationPositionedMatch>
            {
                new CoopSiegeFormationPositionedMatch(3, 1)
            };
        int canonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(3, 1, matches);

        AssertEqual(
            0,
            canonicalIndex,
            "The only positioned match must remain canonical.");
        AssertEqual(
            0,
            CoopSiegeFormationMembershipSafetyContract
                .ResolveRedundantMatchIndices(
                    canonicalIndex,
                    matches.Count)
                .Length,
            "A healthy single match must remain a no-op.");
    }

    private static void ValidateStoredCoordinatesWin()
    {
        var matches =
            new List<CoopSiegeFormationPositionedMatch>
            {
                new CoopSiegeFormationPositionedMatch(1, 0),
                new CoopSiegeFormationPositionedMatch(4, 1),
                new CoopSiegeFormationPositionedMatch(8, 0)
            };
        int canonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(4, 1, matches);

        AssertEqual(
            1,
            canonicalIndex,
            "Stored file/rank coordinates must win when they match a duplicate.");
        AssertSequence(
            new[] { 0, 2 },
            CoopSiegeFormationMembershipSafetyContract
                .ResolveRedundantMatchIndices(
                    canonicalIndex,
                    matches.Count),
            "Only non-canonical duplicate matches may be removed.");
    }

    private static void ValidateFallbackIsDeterministic()
    {
        var matches =
            new List<CoopSiegeFormationPositionedMatch>
            {
                new CoopSiegeFormationPositionedMatch(8, 0),
                new CoopSiegeFormationPositionedMatch(4, 1),
                new CoopSiegeFormationPositionedMatch(6, 0)
            };
        int canonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(-1, -1, matches);

        AssertEqual(
            1,
            canonicalIndex,
            "Invalid stored coordinates must use the lexicographically lowest existing slot.");
    }

    private static void ValidateRepeatedApplicationIsNoOp()
    {
        var matches =
            new List<CoopSiegeFormationPositionedMatch>
            {
                new CoopSiegeFormationPositionedMatch(4, 1),
                new CoopSiegeFormationPositionedMatch(8, 0)
            };
        int canonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(-1, -1, matches);
        int[] redundant =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveRedundantMatchIndices(
                    canonicalIndex,
                    matches.Count);

        for (int i = redundant.Length - 1; i >= 0; i--)
            matches.RemoveAt(redundant[i]);

        int secondCanonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(
                    matches[0].FileIndex,
                    matches[0].RankIndex,
                    matches);
        AssertEqual(
            0,
            secondCanonicalIndex,
            "The surviving match must remain canonical after repair.");
        AssertEqual(
            0,
            CoopSiegeFormationMembershipSafetyContract
                .ResolveRedundantMatchIndices(
                    secondCanonicalIndex,
                    matches.Count)
                .Length,
            "Repeated application to a repaired state must be a no-op.");
    }

    private static void ValidateOtherAgentsRemainUnchanged()
    {
        string[,] slots =
        {
            { "other-a", "player" },
            { "other-b", "other-c" },
            { "player", "other-d" }
        };
        var matches =
            new List<CoopSiegeFormationPositionedMatch>
            {
                new CoopSiegeFormationPositionedMatch(0, 1),
                new CoopSiegeFormationPositionedMatch(2, 0)
            };
        int canonicalIndex =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveCanonicalMatchIndex(2, 0, matches);
        int[] redundant =
            CoopSiegeFormationMembershipSafetyContract
                .ResolveRedundantMatchIndices(
                    canonicalIndex,
                    matches.Count);

        foreach (int redundantIndex in redundant)
        {
            CoopSiegeFormationPositionedMatch match =
                matches[redundantIndex];
            slots[match.FileIndex, match.RankIndex] = null;
        }

        AssertEqual(
            "other-a",
            slots[0, 0],
            "Repair must not change another agent in slot (0,0).");
        AssertEqual(
            "other-b",
            slots[1, 0],
            "Repair must not change another agent in slot (1,0).");
        AssertEqual(
            "other-c",
            slots[1, 1],
            "Repair must not change another agent in slot (1,1).");
        AssertEqual(
            "other-d",
            slots[2, 1],
            "Repair must not change another agent in slot (2,1).");
        AssertEqual(
            "player",
            slots[2, 0],
            "The canonical player slot must remain present.");
        Assert(
            slots[0, 1] == null,
            "Only the redundant player slot may be cleared.");
    }

    private static void AssertSequence(
        int[] expected,
        int[] actual,
        string message)
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidOperationException(
                message +
                " ExpectedLength=" + expected.Length +
                " ActualLength=" + actual.Length + ".");
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
            {
                throw new InvalidOperationException(
                    message +
                    " Index=" + i +
                    " Expected=" + expected[i] +
                    " Actual=" + actual[i] + ".");
            }
        }
    }

    private static void AssertEqual(
        string expected,
        string actual,
        string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                message +
                " Expected=" + expected +
                " Actual=" + actual + ".");
        }
    }

    private static void AssertEqual(
        int expected,
        int actual,
        string message)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(
                message +
                " Expected=" + expected +
                " Actual=" + actual + ".");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
