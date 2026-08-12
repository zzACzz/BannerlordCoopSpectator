using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateCampaignPowerFormula();
            ValidateAvailableStackPower();
            ValidateRemovalPolicy();
            ValidateRenderPolicy();
            Console.WriteLine("Coop battle power contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateCampaignPowerFormula()
    {
        AssertEqual(
            800,
            CoopBattlePowerContract.CalculateUnitPower(
                tier: 2,
                isHero: false,
                heroLevel: 0,
                isMounted: false),
            "Tier-two infantry must use the campaign CharacterObject.GetPower formula.");
        AssertEqual(
            960,
            CoopBattlePowerContract.CalculateUnitPower(
                tier: 2,
                isHero: false,
                heroLevel: 0,
                isMounted: true),
            "Mounted non-heroes must receive the campaign 1.2 multiplier.");
        AssertEqual(
            3360,
            CoopBattlePowerContract.CalculateUnitPower(
                tier: 0,
                isHero: true,
                heroLevel: 20,
                isMounted: true),
            "Heroes must derive their tier from level and use the campaign 1.5 multiplier without the mounted multiplier.");
    }

    private static void ValidateAvailableStackPower()
    {
        AssertEqual(
            6400,
            CoopBattlePowerContract.CalculateAvailableStackPower(
                count: 10,
                woundedCount: 2,
                tier: 2,
                isHero: false,
                heroLevel: 0,
                isMounted: false),
            "Pre-battle wounded troops must not contribute to initial power.");
        AssertEqual(
            0,
            CoopBattlePowerContract.CalculateAvailableStackPower(
                count: 2,
                woundedCount: 5,
                tier: 2,
                isHero: false,
                heroLevel: 0,
                isMounted: false),
            "Wounded counts above stack size must clamp to zero available troops.");
    }

    private static void ValidateRemovalPolicy()
    {
        AssertEqual(
            2400,
            CoopBattlePowerContract.SubtractClamped(3200, 800),
            "Killed, unconscious, and routed agents must remove one unit of power.");
        AssertEqual(
            0,
            CoopBattlePowerContract.SubtractClamped(0, 800),
            "Repeated removal must remain clamped at zero.");
        AssertEqual(
            int.MaxValue,
            CoopBattlePowerContract.AddClamped(int.MaxValue - 10, 20),
            "Power accumulation must saturate instead of overflowing.");
    }

    private static void ValidateRenderPolicy()
    {
        Assert(
            CoopBattlePowerContract.CanRender(new CoopBattlePowerState
            {
                IsAvailable = true,
                InitialAttackerPower = 1200,
                CurrentAttackerPower = 1200,
                InitialDefenderPower = 1200,
                CurrentDefenderPower = 0
            }),
            "The comparer must remain visible when one side reaches zero.");
        Assert(
            !CoopBattlePowerContract.CanRender(new CoopBattlePowerState
            {
                IsAvailable = true,
                InitialAttackerPower = 1200,
                InitialDefenderPower = 1200
            }),
            "The comparer must hide when both current powers are zero during mission teardown.");
        Assert(
            !CoopBattlePowerContract.CanRender(new CoopBattlePowerState
            {
                IsAvailable = false,
                InitialAttackerPower = 1200,
                CurrentAttackerPower = 1200,
                InitialDefenderPower = 1200,
                CurrentDefenderPower = 1200
            }),
            "Unavailable authoritative state must fail closed.");
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException(message + " Expected=" + expected + " Actual=" + actual + ".");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
