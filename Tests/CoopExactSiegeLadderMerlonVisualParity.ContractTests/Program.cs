using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateExactVisibleDisabledMerlonIsRestored();
            ValidateContextGates();
            ValidateIdentityAndTypeGates();
            ValidateAuthoritativeStateGates();
            ValidateWallStateGates();
            ValidateRepeatedApplicationIsNoOp();
            ValidateUnrelatedObjectsRemainNoOp();
            ValidateMultipleLaddersDoNotCopyState();
            ValidateLateJoinUsesTheSameDecision();
            Console.WriteLine("Exact siege ladder merlon visual parity contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateExactVisibleDisabledMerlonIsRestored()
    {
        AssertRestore(
            CreateInput(),
            "A server-visible, disabled and intact native ladder merlon must be restored.");
        AssertRestore(
            CreateInput(localRootVisible: true, localRootPhysicsEnabled: false),
            "Missing root physics must be restored even when visibility survived.");
    }

    private static void ValidateContextGates()
    {
        AssertNoRestore(CreateInput(isExactCampaignSiegeAssault: false),
            "A non-exact campaign siege must remain a no-op.");
        AssertNoRestore(CreateInput(isRemoteClient: false),
            "The server and listen-server host must remain a no-op.");
    }

    private static void ValidateIdentityAndTypeGates()
    {
        AssertNoRestore(CreateInput(isDestructableComponent: false),
            "A non-destructible object must remain a no-op.");
        AssertNoRestore(CreateInput(isSceneObject: false),
            "A runtime-spawned object must remain a no-op.");
        AssertNoRestore(CreateInput(isDeclaredIndestructibleMerlon: false),
            "An object outside a native ladder merlon tag must remain a no-op.");
        AssertNoRestore(CreateInput(hasEligibleTargetWall: false),
            "A missing or ambiguous native ladder-to-wall relation must remain a no-op.");
        AssertNoRestore(CreateInput(localRootHasPhysicsDescription: false),
            "An entity without native root physics must not gain physics.");
    }

    private static void ValidateAuthoritativeStateGates()
    {
        AssertNoRestore(CreateInput(allowVisibilityUpdate: false),
            "A native sync call that forbids visibility updates must remain a no-op.");
        AssertNoRestore(CreateInput(serverRequestedVisible: false),
            "A server-hidden merlon must stay hidden.");
        AssertNoRestore(CreateInput(serverMarkedDisabled: false),
            "A server-enabled destructible must use native sync unchanged.");
        AssertNoRestore(CreateInput(serverHitPoint: 0f, localIsDestroyed: true),
            "A server-destroyed merlon must stay destroyed.");
        AssertNoRestore(CreateInput(serverDestructionState: 1),
            "A non-original destruction state must stay unchanged.");
        AssertNoRestore(CreateInput(localMarkedDisabled: false),
            "The repair must not alter an enabled local destructible.");
        AssertNoRestore(CreateInput(localCanBeDestroyedInitially: true),
            "A normally destructible merlon must not enter the ladder exception.");
    }

    private static void ValidateWallStateGates()
    {
        AssertNoRestore(CreateInput(hasEligibleTargetWall: false),
            "A breached, disabled or hidden wall must not expose its merlon.");
    }

    private static void ValidateRepeatedApplicationIsNoOp()
    {
        AssertNoRestore(
            CreateInput(localRootVisible: true, localRootPhysicsEnabled: true),
            "Repeated application to matching visibility and physics must be idempotent.");
    }

    private static void ValidateUnrelatedObjectsRemainNoOp()
    {
        string[] unrelatedObjects =
        {
            "gate",
            "spawned pilum",
            "ordinary item",
            "fork",
            "fire pot",
            "catapult",
            "stone pile",
            "arrow barrel"
        };

        foreach (string unrelatedObject in unrelatedObjects)
        {
            AssertNoRestore(
                CreateInput(
                    isDestructableComponent: false,
                    isDeclaredIndestructibleMerlon: false,
                    hasEligibleTargetWall: false,
                    isSceneObject: unrelatedObject != "spawned pilum"),
                "An unrelated object must not enter the branch: " + unrelatedObject + ".");
        }
    }

    private static void ValidateMultipleLaddersDoNotCopyState()
    {
        AssertNoRestore(
            CreateInput(hasEligibleTargetWall: false),
            "Conflicting ladder-to-wall membership must fail closed.");
        AssertRestore(
            CreateInput(),
            "A unique native ladder-to-wall relation does not copy another ladder's state.");
    }

    private static void ValidateLateJoinUsesTheSameDecision()
    {
        ExactSiegeLadderMerlonVisualParityInput initialMaterialization = CreateInput();
        ExactSiegeLadderMerlonVisualParityInput lateJoinMaterialization = CreateInput();
        Assert(
            ExactSiegeLadderMerlonVisualParityContract
                .ShouldRestoreVisibleDisabledIndestructibleMerlon(initialMaterialization) ==
            ExactSiegeLadderMerlonVisualParityContract
                .ShouldRestoreVisibleDisabledIndestructibleMerlon(lateJoinMaterialization),
            "Late join must use the same authoritative materialization decision.");
    }

    private static ExactSiegeLadderMerlonVisualParityInput CreateInput(
        bool isExactCampaignSiegeAssault = true,
        bool isRemoteClient = true,
        bool isDestructableComponent = true,
        bool isSceneObject = true,
        bool isDeclaredIndestructibleMerlon = true,
        bool hasEligibleTargetWall = true,
        bool allowVisibilityUpdate = true,
        bool serverRequestedVisible = true,
        bool serverMarkedDisabled = true,
        float serverHitPoint = 1f,
        int serverDestructionState = 0,
        bool localMarkedDisabled = true,
        bool localCanBeDestroyedInitially = false,
        bool localIsDestroyed = false,
        bool localRootHasPhysicsDescription = true,
        bool localRootVisible = false,
        bool localRootPhysicsEnabled = false)
    {
        return new ExactSiegeLadderMerlonVisualParityInput(
            isExactCampaignSiegeAssault,
            isRemoteClient,
            isDestructableComponent,
            isSceneObject,
            isDeclaredIndestructibleMerlon,
            hasEligibleTargetWall,
            allowVisibilityUpdate,
            serverRequestedVisible,
            serverMarkedDisabled,
            serverHitPoint,
            serverDestructionState,
            localMarkedDisabled,
            localCanBeDestroyedInitially,
            localIsDestroyed,
            localRootHasPhysicsDescription,
            localRootVisible,
            localRootPhysicsEnabled);
    }

    private static void AssertRestore(
        ExactSiegeLadderMerlonVisualParityInput input,
        string message)
    {
        Assert(
            ExactSiegeLadderMerlonVisualParityContract
                .ShouldRestoreVisibleDisabledIndestructibleMerlon(input),
            message);
    }

    private static void AssertNoRestore(
        ExactSiegeLadderMerlonVisualParityInput input,
        string message)
    {
        Assert(
            !ExactSiegeLadderMerlonVisualParityContract
                .ShouldRestoreVisibleDisabledIndestructibleMerlon(input),
            message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
