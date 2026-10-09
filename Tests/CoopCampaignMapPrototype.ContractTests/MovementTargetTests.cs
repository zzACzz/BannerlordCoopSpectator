using System;
using CoopSpectator.Infrastructure;

internal static class MovementTargetTests
{
    private static int checks;
    internal static void Run()
    {
        Selection();
        Timing();
        Layout();
        Rejection();
        Console.WriteLine("Movement target checks: " + checks);
    }

    private static CoopCampaignMapMovementTargetInput Input() => new CoopCampaignMapMovementTargetInput {
        PartyMoveMode = 1, DefaultBehavior = 1, TargetDistanceSquared = 1,
        HasMoveTargetParty = true, MoveTargetPartyVisible = true, MoveTargetPartyId = "party",
        MoveTargetSettlementId = "village", TargetSettlementId = "town"
    };
    private static CoopCampaignMapMovementTargetSelection Select(CoopCampaignMapMovementTargetInput i) =>
        CoopCampaignMapMovementTargetPolicy.Select(i, id => id == "party" || id == "village" || id == "town");
    private static void Selection()
    {
        Check(Select(null).Kind == CoopCampaignMapMovementTargetKind.None, "null input");
        var i = Input();
        Check(CoopCampaignMapMovementTargetPolicy.Select(i, null).Kind == CoopCampaignMapMovementTargetKind.None, "no disclosure authority");
        Check(Select(i).Kind == CoopCampaignMapMovementTargetKind.Point && Select(i).EntityId == null, "point before entity selection");
        Check(!Select(i).SuppressesHover("party"), "point does not suppress entity hover");
        foreach (Action<CoopCampaignMapMovementTargetInput> gate in new Action<CoopCampaignMapMovementTargetInput>[] {
            x => x.ForceAiNoPathMode = true, x => x.HasAiInteractable = true, x => x.HasMapEvent = true,
            x => x.DefaultBehavior = 0, x => x.TargetDistanceSquared = .01f,
            x => x.TargetDistanceSquared = float.NaN, x => x.TargetDistanceSquared = float.PositiveInfinity,
            x => x.TargetDistanceSquared = -1, x => x.PartyMoveMode = 0 })
        { i = Input(); gate(i); Check(Select(i).Kind == CoopCampaignMapMovementTargetKind.None, "point gate"); }
        i = Input(); i.TargetDistanceSquared = .010001f;
        Check(Select(i).Kind == CoopCampaignMapMovementTargetKind.Point, "distance just above native threshold");
        i = Input(); i.DefaultBehavior = 2;
        Check(Select(i).EntityId == "town", "settlement behavior overrides point");
        i.PartyMoveMode = 2;
        Check(Select(i).EntityId == "party", "visible pursuit overrides settlement behavior");
        i.HasMapEvent = true;
        Check(Select(i).EntityId == "party", "native map event gate is point-only");
        i.MoveTargetHasSettlement = true;
        Check(Select(i).EntityId == "village" && Select(i).Kind == CoopCampaignMapMovementTargetKind.Settlement, "occupied settlement redirect");
        Check(Select(i).SuppressesHover("village") && !Select(i).SuppressesHover("Village") && !Select(i).SuppressesHover(null), "exact hover suppression identity");
        i.MoveTargetSettlementIsHideout = true;
        Check(Select(i).EntityId == "party", "hideout does not redirect");
        i.MoveTargetPartyId = "hidden";
        var denied = Select(i);
        Check(denied.Kind == CoopCampaignMapMovementTargetKind.None && denied.EntityId == null && !denied.SuppressesHover("hidden"), "denied selection cannot leak or fall back");
        i.MoveTargetPartyVisible = false;
        Check(Select(i).EntityId == "town", "native invisible pursuit falls through before disclosure admission");
        i.MoveTargetPartyVisible = true; i.HasMoveTargetParty = false;
        Check(Select(i).EntityId == "town", "absent pursuit falls through");
        i.TargetSettlementId = "";
        Check(Select(i).Kind == CoopCampaignMapMovementTargetKind.None, "empty target absent");
    }

    private static void Timing()
    {
        int[] sequence = { 0, 1, 2, 3, 4, 4, 3, 2, 1, 0 };
        for (int step = 0; step < 100; step++)
            Check(CoopCampaignMapMovementTargetPolicy.TryAnimationIndex(step * .1 + .05, out var index) && index == sequence[step % 10], "reflected atlas animation");
        foreach (float time in new[] { 0f, .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1f, 100000000f })
        {
            int expected = (int)(time / .1f) % 10;
            if (expected >= 5) expected = 9 - expected;
            Check(CoopCampaignMapMovementTargetPolicy.TryAnimationIndex(time, out var index) && index == expected, "native float frame boundary");
        }
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 100000001d })
            Check(!CoopCampaignMapMovementTargetPolicy.TryAnimationIndex(bad, out _), "unsafe elapsed time");
        foreach (double distance in new[] { 0d, 20d, 100d, 400d, 100000d })
        {
            float d = (float)distance + 80f;
            Check(CoopCampaignMapMovementTargetPolicy.TryPointScale(distance, out var scale) && scale == Math.Max(.2f, Math.Min(45f, d * d / 5000f)), "host-camera scale");
        }
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, 100001d })
            Check(!CoopCampaignMapMovementTargetPolicy.TryPointScale(bad, out _), "unsafe camera distance");
    }

    private static CoopCampaignMapMovementTargetPresentation Point() => new CoopCampaignMapMovementTargetPresentation {
        Kind = CoopCampaignMapMovementTargetKind.Point, Color = CoopCampaignMapMovementTargetLayout.PointColor,
        Origin = new[] { 123f, 456f, 7f }, TerrainNormal = new[] { 0f, .6f, .8f }, HostCameraDistance = 20
    };
    private static CoopCampaignMapMovementTargetPresentation Entity(bool fort = false) => new CoopCampaignMapMovementTargetPresentation {
        Kind = CoopCampaignMapMovementTargetKind.Settlement, EntityId = "town", Fortification = fort,
        Color = 4291596077u, Origin = new[] { 123f, 456f, 7f }, TerrainNormal = new[] { 0f, 0f, 1f },
        CircleFrame = new[] { 0f, 2f, 0f, -3f, 0f, 0f, 0f, 0f, 4f, 8f, 9f, 10f }
    };
    private static void Layout()
    {
        var p = Point();
        Check(CoopCampaignMapMovementTargetLayout.TryBuild(p, out var d) && d.Length == 2, "two land point rings");
        Near(d[0].Frame[0], .477692f, "inner scale"); Near(d[1].Frame[0], .557168f, "outer scale");
        Near(d[0].TextureCoordinates[2], .664f, "point inner atlas"); Near(d[1].TextureCoordinates[2], .83f, "point outer atlas");
        Near(d[0].Frame[7], .477692f * .6f, "native normal basis, not orthonormalized");
        Near(d[0].Frame[10], 456, "host origin");
        p.Origin[1] = 900;
        Near(d[0].Frame[10], 456, "output owns origin copy");
        d[0].Frame[0] = 900;
        Near(d[1].Frame[0], .557168f, "two frames independent");
        foreach (var wind in new[] { new[] { 1f, 0f }, new[] { 0f, 1f }, new[] { -1f, 0f }, new[] { 0f, -1f }, new[] { 3f, 4f } })
        {
            p = Point(); p.AtSea = true; p.Wind = wind;
            Check(CoopCampaignMapMovementTargetLayout.TryBuild(p, out d) && d.Length == 1 && d[0].Material == CoopCampaignMapMovementDecalMaterial.Wind, "sea point uses wind only");
            // Independent installed Mat3 oracle: s = cross(forward, up), then
            // rotated s = s*cos(pi/2)+f*sin(pi/2), f = f*cos(pi/2)-s*sin(pi/2).
            double length = Math.Sqrt(wind[0] * wind[0] + wind[1] * wind[1]);
            double fx = wind[0] / length, fy = wind[1] / length;
            double sx = fy, sy = -fx, a = Math.PI / 2, scale = .627692;
            Near(d[0].Frame[0], (float)((sx * Math.Cos(a) + fx * Math.Sin(a)) * scale), "wind side x");
            Near(d[0].Frame[1], (float)((sy * Math.Cos(a) + fy * Math.Sin(a)) * scale), "wind side y");
            Near(d[0].Frame[3], (float)((fx * Math.Cos(a) - sx * Math.Sin(a)) * scale), "wind forward x");
            Near(d[0].Frame[4], (float)((fy * Math.Cos(a) - sy * Math.Sin(a)) * scale), "wind forward y");
            Near(d[0].TextureCoordinates[0], 1, "full wind texture");
        }
        p = Entity(true);
        Check(CoopCampaignMapMovementTargetLayout.TryBuild(p, out d) && d[0].Material == CoopCampaignMapMovementDecalMaterial.Fortification, "authored fort shape");
        for (int j = 0; j < 12; j++) Near(d[0].Frame[j], p.CircleFrame[j], "fort global frame unchanged (no 1.3 scaling)");
        p.CircleFrame[9] = 500;
        Near(d[0].Frame[9], 8, "fort owns frame copy");
        p = Entity();
        Check(CoopCampaignMapMovementTargetLayout.TryBuild(p, out d) && d.Length == 1, "village ring");
        Near(d[0].Frame[0], 2.4f, "side-axis length, not frame diagonal"); Near(d[0].Frame[9], 123, "village uses party origin, not circle offset");
        Near(d[0].TextureCoordinates[2], .83f, "village atlas");
        p.Kind = CoopCampaignMapMovementTargetKind.Party; p.AnimationElapsedSeconds = .35;
        Check(CoopCampaignMapMovementTargetLayout.TryBuild(p, out d), "party ring");
        Near(d[0].TextureCoordinates[2], .498f, "animated party atlas");
        p.AtSea = true;
        Check(CoopCampaignMapMovementTargetLayout.TryBuild(p, out d) && d[0].Material == CoopCampaignMapMovementDecalMaterial.Circle, "sea party still circle");
        Near(d[0].Frame[0], 5, "sea party scaling");
    }

    private static void Rejection()
    {
        Check(!CoopCampaignMapMovementTargetLayout.TryBuild(null, out var d) && d == null, "null recipe");
        foreach (Action<CoopCampaignMapMovementTargetPresentation> corrupt in new Action<CoopCampaignMapMovementTargetPresentation>[] {
            x => x.Kind = CoopCampaignMapMovementTargetKind.None, x => x.Kind = (CoopCampaignMapMovementTargetKind)99,
            x => x.EntityId = "injected", x => x.Fortification = true, x => x.Color = 0,
            x => x.CircleFrame = new float[12], x => x.Origin = null, x => x.Origin = new float[2],
            x => x.Origin[0] = float.NaN, x => x.Origin[2] = 100001,
            x => x.TerrainNormal = new float[3], x => x.TerrainNormal[2] = -1,
            x => x.TerrainNormal[1] = float.PositiveInfinity, x => x.HostCameraDistance = double.NaN,
            x => x.AtSea = true, x => { x.AtSea = true; x.Wind = new float[2]; },
            x => { x.AtSea = true; x.Wind = new[] { float.NaN, 1f }; } })
        { var p = Point(); corrupt(p); Check(!CoopCampaignMapMovementTargetLayout.TryBuild(p, out d) && d == null, "invalid point fails without partial output"); }
        foreach (Action<CoopCampaignMapMovementTargetPresentation> corrupt in new Action<CoopCampaignMapMovementTargetPresentation>[] {
            x => x.EntityId = null, x => x.EntityId = "", x => x.AtSea = true,
            x => x.CircleFrame = null, x => x.CircleFrame[1] = 0, x => x.Color = uint.MaxValue,
            x => { x.Kind = CoopCampaignMapMovementTargetKind.Party; x.Fortification = true; },
            x => { x.Kind = CoopCampaignMapMovementTargetKind.Party; x.AnimationElapsedSeconds = -1; } })
        { var p = Entity(); corrupt(p); Check(!CoopCampaignMapMovementTargetLayout.TryBuild(p, out d) && d == null, "invalid entity fails without partial output"); }
    }
    private static void Near(float actual, float expected, string label) => Check(Math.Abs(actual - expected) < .00001f, label);
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new InvalidOperationException("Movement target: " + label); }
}
