using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private sealed class CombatEventIdentity
    {
        public string VictimEntryId { get; set; }
        public string AttackerEntryId { get; set; }
        public bool IsFatal { get; set; }
    }

    private static int Main()
    {
        try
        {
            ValidateRealSpearMirrorDecode();
            ValidateRealBowMirrorDecode();
            ValidateMalformedMirrorRejection();
            ValidateCampaignWeaponSelection();
            ValidateCandidateOrderingAndDuplicateRemoval();
            ValidateActualSkillXpDelta();
            ValidateFatalEventAttackerMatch();
            ValidateAlreadyFatalEventIsNotReused();
            ValidateMissingFatalEventMatch();
            Console.WriteLine("Coop hero battle progression contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateRealSpearMirrorDecode()
    {
        bool decoded = CoopHeroBattleProgressionContract.TryDecodeStaticMirrorOriginalItemId(
            "cs_mirror_triangluar_spear_t3_67b328d4",
            out string originalItemId);

        Assert(decoded && originalItemId == "triangluar_spear_t3",
            "The real spear mirror id must decode to its campaign item id.");
    }

    private static void ValidateRealBowMirrorDecode()
    {
        bool decoded = CoopHeroBattleProgressionContract.TryDecodeStaticMirrorOriginalItemId(
            "cs_mirror_hunting_bow_6426c1df",
            out string originalItemId);

        Assert(decoded && originalItemId == "hunting_bow",
            "The real bow mirror id must decode to its campaign item id.");
    }

    private static void ValidateMalformedMirrorRejection()
    {
        Assert(!CoopHeroBattleProgressionContract.TryDecodeStaticMirrorOriginalItemId(
                "cs_mirror_hunting_bow_deadbeef",
                out _),
            "A mirror id with an invalid stable hash must be rejected.");
        Assert(!CoopHeroBattleProgressionContract.TryDecodeStaticMirrorOriginalItemId(
                "cs_crafted_hunting_bow_6426c1df",
                out _),
            "A crafted mirror id must not be decoded as a static mirror id.");
    }

    private static void ValidateCampaignWeaponSelection()
    {
        Assert(CoopHeroBattleProgressionContract.SelectCampaignWeaponItemId(
                   "cs_mirror_hunting_bow_6426c1df",
                   "hunting_bow") == "hunting_bow",
            "An explicit registry mapping must win over the battle item id.");
        Assert(CoopHeroBattleProgressionContract.SelectCampaignWeaponItemId(
                   "cs_mirror_hunting_bow_6426c1df",
                   null) == "hunting_bow",
            "A legacy static mirror id must fall back to validated decoding.");
        Assert(CoopHeroBattleProgressionContract.SelectCampaignWeaponItemId(
                   "regular_sword",
                   null) == "regular_sword",
            "A regular campaign item id must remain unchanged.");
    }

    private static void ValidateCandidateOrderingAndDuplicateRemoval()
    {
        IReadOnlyList<string> explicitCandidates =
            CoopHeroBattleProgressionContract.BuildWeaponResolutionCandidates(
                "campaign_weapon",
                "battle_weapon");
        Assert(explicitCandidates.Count == 2 &&
               explicitCandidates[0] == "campaign_weapon" &&
               explicitCandidates[1] == "battle_weapon",
            "Campaign and battle candidates must retain their resolution order.");

        IReadOnlyList<string> duplicateCandidates =
            CoopHeroBattleProgressionContract.BuildWeaponResolutionCandidates(
                "HUNTING_BOW",
                "hunting_bow");
        Assert(duplicateCandidates.Count == 1,
            "Duplicate candidates must be removed case-insensitively.");

        IReadOnlyList<string> legacyCandidates =
            CoopHeroBattleProgressionContract.BuildWeaponResolutionCandidates(
                null,
                "cs_mirror_hunting_bow_6426c1df");
        Assert(legacyCandidates.Count == 2 && legacyCandidates[1] == "hunting_bow",
            "A validated legacy original id must follow the actual battle item id.");
    }

    private static void ValidateActualSkillXpDelta()
    {
        var before = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Polearm"] = 100f,
            ["Athletics"] = 50f
        };
        var after = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["Polearm"] = 112.5f,
            ["Athletics"] = 49f
        };

        Dictionary<string, float> deltas =
            CoopHeroBattleProgressionContract.CalculatePositiveSkillXpDeltas(before, after);
        const float rawXpDelta = 5f;
        Assert(deltas.Count == 1 && Math.Abs(deltas["Polearm"] - 12.5f) < 0.001f,
            "Only the positive post-focus skill XP delta must be reported.");
        Assert(Math.Abs(deltas["Polearm"] - rawXpDelta) > 0.001f,
            "Actual skill XP must remain distinct from raw hero XP.");
    }

    private static void ValidateFatalEventAttackerMatch()
    {
        var events = new List<CombatEventIdentity>
        {
            new CombatEventIdentity { VictimEntryId = "victim-stack", AttackerEntryId = "hero-a" },
            new CombatEventIdentity { VictimEntryId = "victim-stack", AttackerEntryId = "hero-b" }
        };

        Assert(FindFatalMatch(events, "victim-stack", "hero-a") == 0,
            "A newer hit from another attacker must not receive the fatal flag.");
    }

    private static void ValidateMissingFatalEventMatch()
    {
        var events = new List<CombatEventIdentity>
        {
            new CombatEventIdentity { VictimEntryId = "victim-stack", AttackerEntryId = "hero-b" }
        };

        Assert(FindFatalMatch(events, "victim-stack", "hero-a") == -1,
            "A missing exact attacker match must leave the event unmatched for synthetic fallback.");
        Assert(FindFatalMatch(events, "victim-stack", null) == -1,
            "A removal without an attacker identity must not mark an arbitrary victim event fatal.");
    }

    private static void ValidateAlreadyFatalEventIsNotReused()
    {
        var events = new List<CombatEventIdentity>
        {
            new CombatEventIdentity { VictimEntryId = "victim-stack", AttackerEntryId = "hero-a" },
            new CombatEventIdentity { VictimEntryId = "victim-stack", AttackerEntryId = "hero-a", IsFatal = true }
        };

        Assert(FindFatalMatch(events, "victim-stack", "hero-a") == 0,
            "An already-fatal event must be skipped when another unit in the same stack is removed.");
    }

    private static int FindFatalMatch(
        IReadOnlyList<CombatEventIdentity> events,
        string victimEntryId,
        string attackerEntryId)
    {
        for (int i = events.Count - 1; i >= 0; i--)
        {
            if (CoopHeroBattleProgressionContract.IsFatalCombatEventMatch(
                    events[i].VictimEntryId,
                    events[i].AttackerEntryId,
                    victimEntryId,
                    attackerEntryId,
                    events[i].IsFatal))
                return i;
        }

        return -1;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
