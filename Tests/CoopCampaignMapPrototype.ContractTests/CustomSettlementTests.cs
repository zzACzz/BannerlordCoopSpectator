using System;
using CoopSpectator.Infrastructure;

internal static class CustomSettlementTests
{
    private sealed class Root
    {
        internal bool Visible = true;
        internal int Writes;
        internal readonly string Rotation = "0,0,-2.525";
        internal readonly bool[] ChildVisibility = { true, false, true };
    }

    internal static void Run()
    {
        var visible = new CoopCampaignMapPrototypeEntityState {
            EntityId = CoopCampaignMapCustomSettlementPolicy.TrainingFieldId,
            Kind = CoopCampaignMapPrototypeEntityKind.Settlement };
        var catalog = new CoopCampaignMapPrototypeCatalogEntityState {
            EntityId = visible.EntityId, Kind = visible.Kind,
            SettlementKind = CoopCampaignMapPrototypeSettlementKind.Special };
        Check(CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, catalog), "Visible official training field is admitted at level0");
        Check(!CoopCampaignMapCustomSettlementPolicy.IsAdmitted(null, catalog), "Catalog alone cannot reveal a hidden settlement");
        Check(!CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, null), "Visible identity alone cannot select an arbitrary prefab");
        foreach (CoopCampaignMapPrototypeSettlementKind kind in Enum.GetValues(typeof(CoopCampaignMapPrototypeSettlementKind)))
        {
            catalog.SettlementKind = kind;
            Check(CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, catalog) == (kind == CoopCampaignMapPrototypeSettlementKind.Special), "Custom geometry cannot bypass ordinary settlement admission");
        }
        catalog.SettlementKind = CoopCampaignMapPrototypeSettlementKind.Special;
        visible.SettlementLevelFlags = 1;
        Check(!CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, catalog), "Custom prefab does not invent a fortification level");
        visible.SettlementLevelFlags = 0; catalog.EntityId = "settlement:other";
        Check(!CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, catalog), "Catalog identity must match");
        catalog.EntityId = visible.EntityId; visible.Kind = CoopCampaignMapPrototypeEntityKind.MobileParty;
        Check(!CoopCampaignMapCustomSettlementPolicy.IsAdmitted(visible, catalog), "A party cannot select settlement geometry");

        var root = new Root();
        var admission = new CoopCampaignMapSettlementGeometryAdmission<Root>((value, show) => { value.Visible = show; value.Writes++; });
        admission.Register(CoopCampaignMapCustomSettlementPolicy.TrainingFieldPrefab, root);
        Check(!root.Visible, "New native root starts hidden");
        admission.Apply(CoopCampaignMapCustomSettlementPolicy.TrainingFieldId, true);
        Check(root.Visible && root.Rotation == "0,0,-2.525" && !root.ChildVisibility[1], "Prefab rotation and child visibility survive admission");
        admission.Apply(CoopCampaignMapCustomSettlementPolicy.TrainingFieldId, false);
        Check(!root.Visible, "Host revocation hides the root");
        admission.Apply(CoopCampaignMapCustomSettlementPolicy.TrainingFieldId, true);
        Check(root.Visible && !root.ChildVisibility[1], "Re-admission preserves authored child state");
        admission.Clear(); int writes = root.Writes;
        admission.Apply(CoopCampaignMapCustomSettlementPolicy.TrainingFieldId, false);
        Check(root.Writes == writes, "Old scene roots are not touched after teardown");
        Console.WriteLine("Custom settlement disclosure and prefab visibility checks passed.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
