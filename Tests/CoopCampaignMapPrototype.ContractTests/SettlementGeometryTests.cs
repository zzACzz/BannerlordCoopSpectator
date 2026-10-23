using System;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class SettlementGeometryTests
{
    private sealed class Root
    {
        public bool Visible = true;
        public int Writes;
    }

    private sealed class Helper
    {
        public UIntPtr Id;
        public Helper Parent;
    }

    public static void Run()
    {
        ValidateLevels();
        ValidateAuthoringHelpers();
        ValidatePresentation();
        ValidateRaidPresentation();
        var admission = new CoopCampaignMapSettlementGeometryAdmission<Root>((root, visible) =>
        {
            root.Visible = visible;
            root.Writes++;
        });
        foreach (string name in new[] { "town_A1", "castle_A1", "village_A1", "hideout_forest_1" })
        {
            var root = new Root();
            admission.Register(name, root);
            Check(!root.Visible && root.Writes == 1, "Scene registration must start hidden.");
            admission.Apply("party:" + name, true);
            admission.Apply("settlement:unrelated", true);
            Check(!root.Visible, "A party or unknown identity must not reveal a settlement.");
            admission.Apply("settlement:" + name, true);
            Check(root.Visible && root.Writes == 2, "Admitted settlement must become visible.");
            admission.Apply("settlement:" + name, true);
            Check(root.Writes == 2, "Unchanged admission must not rewrite native state each tick.");
            admission.Apply("settlement:" + name, false);
            Check(!root.Visible, "Hidden delta, removal or changed kind must hide the geometry.");
            admission.Apply("settlement:" + name, true);
            Check(root.Visible, "Reappearing settlement must be revealable again.");
            var duplicate = new Root();
            admission.Register(name.ToUpperInvariant(), duplicate);
            admission.Apply("settlement:" + name, true);
            Check(!root.Visible && !duplicate.Visible, "Ambiguous identities must both stay hidden.");
        }
        var unnamed = new Root();
        admission.Register(null, unnamed);
        admission.Apply(null, true);
        admission.Apply("settlement:", true);
        Check(!unnamed.Visible, "Unnamed roots must remain hidden.");

        var oldRoot = new Root();
        admission.Register("town_reset", oldRoot);
        admission.Apply("settlement:town_reset", true);
        int oldWrites = oldRoot.Writes;
        admission.Clear();
        admission.Apply("settlement:town_reset", false);
        Check(oldRoot.Writes == oldWrites, "Scene disposal must release bindings without old native calls.");
        var nextRoot = new Root();
        admission.Register("town_reset", nextRoot);
        Check(!nextRoot.Visible, "New scene must not inherit old visibility.");
        admission.Apply("settlement:town_reset", true);
        Check(nextRoot.Visible && oldRoot.Writes == oldWrites, "Fresh snapshot must bind only the new scene.");
        admission.Clear();
        for (int i = 0; i < CoopCampaignMapPrototypeContract.MaxCatalogEntities; i++)
            admission.Register("s_" + i, new Root());
        var overflow = new Root();
        bool rejected = false;
        try { admission.Register("overflow", overflow); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !overflow.Visible, "Overflow must fail scene setup after hiding the new root.");

        string repository = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(repository, "CoopSpectator.csproj")))
            repository = Directory.GetParent(repository)?.FullName ??
                throw new InvalidOperationException("Repository not found.");
        string view = File.ReadAllText(Path.Combine(repository, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        int read = view.IndexOf("_mapScene.Read(", StringComparison.Ordinal);
        int admissionInit = view.IndexOf("InitializeSettlementGeometryAdmission();", StringComparison.Ordinal);
        int helpers = view.IndexOf("RemoveFortificationAuthoringHelpers();", StringComparison.Ordinal);
        int layer = view.IndexOf("_sceneLayer = new PassiveSceneLayer(", StringComparison.Ordinal);
        Check(read < admissionInit && admissionInit < layer, "Roots must hide before first scene rendering.");
        Check(read < helpers && helpers < admissionInit &&
            helpers == view.LastIndexOf("RemoveFortificationAuthoringHelpers();", StringComparison.Ordinal),
            "Helper removal must happen once before binding and rendering.");
        string cleanup = Slice(view, "private void RemoveFortificationAuthoringHelpers", "private void CreateSettlementOverlayLayer");
        Check(cleanup.Contains("new[] { \"town\", \"castle\" }") &&
            cleanup.Contains("_mapScene.FindEntitiesWithTag(tag)") &&
            cleanup.Contains("observed.Add(root.Pointer)") && cleanup.Contains("children.Count > 4096") &&
            cleanup.Contains("CoopCampaignMapSettlementAuthoringPolicy.IsFortificationHelper(childTag)") &&
            cleanup.Contains("helper.Remove(112)") && !cleanup.Contains("Mission.Scene") &&
            !cleanup.Contains("Campaign.Current"), "Cleanup must use exact policy on owned fortification children only.");
        Check(cleanup.IndexOf("SelectRemovalRoots(", StringComparison.Ordinal) <
            cleanup.IndexOf("helper.Remove(112)", StringComparison.Ordinal), "Plan all removals before invalidating native references.");
        Check(view.Contains("entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement, entity.SettlementLevelFlags,") &&
            view.Contains("entity.SettlementWallStates, entity.SettlementPreparationState, entity.SettlementRaidState);"),
            "Full and incremental upserts must share typed geometry admission.");
        string remove = Slice(view, "private void RemoveEntityReplica", "private void UpdateEntityReplicas");
        Check(remove.IndexOf("_settlementGeometry.Apply(entityId, false)", StringComparison.Ordinal) <
              remove.IndexOf("if (!updateOverlay)", StringComparison.Ordinal), "Removal must hide geometry even without overlay updates.");
        string setup = Slice(view, "private void InitializeSettlementGeometryAdmission", "private bool ApplySettlementLevels");
        Check(setup.Contains("new HashSet<UIntPtr>()") && setup.Contains("observed.Add(root.Pointer)") &&
              setup.Contains("_mapScene.FindEntitiesWithTag(tag)") && !setup.Contains("Campaign.Current") &&
              !setup.Contains("Mission.Scene"), "Native root bindings must be deduplicated and owned-scene-only.");
        string levels = Slice(view, "private bool ApplySettlementLevels", "private bool ApplySettlementPresentation");
        Check(levels.Contains("_mapScene.GetUpgradeLevelMaskOfLevelName(name)") &&
              levels.Contains("child.SetVisibilityExcludeParents(CoopCampaignMapSettlementLevelPolicy.Matches(") &&
              !levels.Contains("SetPhysicsState("), "Variant application must resolve owned-scene masks without enabling physics.");
        string publisher = File.ReadAllText(Path.Combine(repository, "Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string presentation = Slice(view, "private bool ApplySettlementPresentation", "private void RemoveSettlementAuthoringCircles");
        Check(presentation.Contains("child.HasTag(\"map_breachable_wall\")") &&
            presentation.Contains("IsWallBroken(walls, wallIndex++)") && presentation.Contains("child.GetChildren()") &&
            presentation.Contains("wallPart.HasTag(\"map_solid_wall\")") && presentation.Contains("wallPart.HasTag(\"map_broken_wall\")") &&
            presentation.Contains("child.HasTag(\"siege_preparation\")") && presentation.Contains("preparation == 2") &&
            !presentation.Contains("SetPhysicsState(") && !presentation.Contains("Campaign.Current"), "Native overrides retain stock order and owned visual scope.");
        string capture = Slice(publisher, "private static int CaptureSettlementLevels", "private static void CaptureSettlementPresentation");
        Check(capture.Contains("SettlementLevelMaskField.GetValue(visual)") &&
              !capture.Contains("SetValue(") && !capture.Contains("SetVisualAsDirty(") &&
              !capture.Contains("RefreshLevelMask("), "Host capture must read the chosen mask without refreshing host state.");
        string presentationCapture = Slice(publisher, "private static void CaptureSettlementPresentation", "private void UpdateReplicaSnapshots");
        Check(presentationCapture.Contains("SettlementWallSectionHitPointsRatioList") &&
            presentationCapture.Contains("BattleSideEnum.Attacker") && !presentationCapture.Contains("SetVisualAsDirty("),
            "Capture uses stock authoritative inputs without changing campaign visuals.");
        Console.WriteLine("Settlement geometry admission tests passed.");
    }

    private static void ValidatePresentation()
    {
        Check(CoopCampaignMapSettlementPresentationPolicy.CaptureWalls(new[] { 1f, 0.3f, 0f, -1f }) == "0011",
            "Wall breach threshold must match native <=0.");
        Check(CoopCampaignMapSettlementPresentationPolicy.CaptureWalls(null) == "" &&
            CoopCampaignMapSettlementPresentationPolicy.CaptureWalls(new[] { float.NaN }) == "" &&
            CoopCampaignMapSettlementPresentationPolicy.CaptureWalls(new[] { float.PositiveInfinity }) == "" &&
            CoopCampaignMapSettlementPresentationPolicy.CaptureWalls(new float[33]) == "", "Invalid/oversized wall inputs remain unknown.");
        for (int i = 0; i < 18; i++)
            Check(CoopCampaignMapSettlementPresentationPolicy.IsWallBroken("01", i) == (i % 2 == 1),
                "All wall levels repeat the native section ordering.");
        Check(CoopCampaignMapSettlementPresentationPolicy.CapturePreparation(false, 1f) == 1 &&
            CoopCampaignMapSettlementPresentationPolicy.CapturePreparation(true, null) == 1 &&
            CoopCampaignMapSettlementPresentationPolicy.CapturePreparation(true, 0.999f) == 1 &&
            CoopCampaignMapSettlementPresentationPolicy.CapturePreparation(true, 1f) == 2 &&
            CoopCampaignMapSettlementPresentationPolicy.CapturePreparation(true, float.NaN) == 0,
            "Preparation handles absent/incomplete/complete/invalid native progress.");
        var root = new Root();
        int levels = 0, presentations = 0;
        bool success = true;
        var admission = new CoopCampaignMapSettlementGeometryAdmission<Root>((r, visible) => r.Visible = visible,
            (r, f) => { Check(!r.Visible, "Root hidden during level reset."); levels++; return true; },
            (r, w, p) => { Check(!r.Visible && levels == presentations + 1, "Overrides follow level reset before reveal.");
                presentations++; return success && p != 0; });
        admission.Register("fort", root);
        admission.Apply("settlement:fort", true, 9, "00", 1);
        admission.Apply("settlement:fort", true, 9, "00", 1);
        Check(root.Visible && presentations == 1, "Unchanged presentation must not rewrite native objects.");
        admission.Apply("settlement:fort", true, 9, "01", 1);
        Check(root.Visible && presentations == 2, "Wall-only transition re-applies overrides.");
        admission.Apply("settlement:fort", true, 9, "01", 2);
        Check(root.Visible && presentations == 3, "Preparation-only transition re-applies overrides.");
        admission.Apply("settlement:fort", false, 9, "01", 2);
        Check(!root.Visible, "Disclosure removal hides presentation.");
        admission.Apply("settlement:fort", true, 10, "01", 2);
        Check(root.Visible && presentations == 4, "Reappearance and changed level re-apply state.");
        success = false;
        admission.Apply("settlement:fort", true, 10, "00", 1);
        Check(!root.Visible, "Failed native overrides cannot leave stale visible geometry.");
        success = true;
        admission.Apply("settlement:fort", true, 10, "00", 1);
        Check(root.Visible, "Fresh successful state recovers presentation.");
        admission.Apply("settlement:fort", true, 10, "", 0);
        Check(!root.Visible, "Unknown presentation cannot silently guess defaults.");
    }

    private static void ValidateAuthoringHelpers()
    {
        foreach (string tag in new[] { "main_map_city_gate", "map_banner_placeholder" })
            Check(CoopCampaignMapSettlementAuthoringPolicy.IsFortificationHelper(tag), "Stock startup helper must match.");
        foreach (string tag in new[] { null, "", "MAIN_MAP_CITY_GATE", "main_map_castle_gate", "main_map_city_port",
            "siege_preparation", "map_solid_wall", "map_broken_wall", "map_siege_tower", "map_siege_engine_0",
            "map_camp_area_1", "banner", "map_banner_placeholder_extra", "town", "castle", "village", "hideout" })
            Check(!CoopCampaignMapSettlementAuthoringPolicy.IsFortificationHelper(tag), "Unproven tags must be preserved.");
        var root = new Helper { Id = new UIntPtr(1) };
        var first = new Helper { Id = new UIntPtr(2), Parent = root };
        var inner = new Helper { Id = new UIntPtr(3), Parent = first };
        var nested = new Helper { Id = new UIntPtr(4), Parent = inner };
        var sibling = new Helper { Id = new UIntPtr(5), Parent = root };
        var alias = new Helper { Id = first.Id, Parent = root };
        var removals = CoopCampaignMapSettlementAuthoringPolicy.SelectRemovalRoots(
            new[] { nested, first, sibling, alias }, h => h.Id, h => h.Parent);
        Check(removals.Count == 2 && removals.Exists(h => h.Id == first.Id) && removals.Contains(sibling),
            "Nested/aliased handles must be removed only through their topmost helper.");
        Check(CoopCampaignMapSettlementAuthoringPolicy.SelectRemovalRoots(
            new Helper[0], h => h.Id, h => h.Parent).Count == 0, "Empty plan must be a no-op.");
        inner.Parent = nested;
        bool rejected = false;
        try { CoopCampaignMapSettlementAuthoringPolicy.SelectRemovalRoots(new[] { nested }, h => h.Id, h => h.Parent); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Ancestry cycles must fail before any native deletion.");
        rejected = false;
        try { CoopCampaignMapSettlementAuthoringPolicy.SelectRemovalRoots(new[] { new Helper() }, h => h.Id, h => h.Parent); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Null native identity must fail before deletion.");
    }

    private static void ValidateLevels()
    {
        Func<string, uint> host = name => {
            for (int i = 0; i < 6; i++) if (CoopCampaignMapSettlementLevelPolicy.LevelName(i) == name) return 1u << (i + 3);
            return 0;
        };
        Func<string, uint> client = name => {
            for (int i = 0; i < 6; i++) if (CoopCampaignMapSettlementLevelPolicy.LevelName(i) == name) return 1u << (12 - i);
            return 0;
        };
        for (int flags = -1; flags <= 128; flags++)
        {
            uint mask = CoopCampaignMapSettlementLevelPolicy.Resolve(flags, host);
            bool known = flags == 1 || flags == 9 || flags == 10 || flags == 12 || flags == 17 || flags == 18 || flags == 20 ||
                flags == 33 || flags == 34 || flags == 36;
            Check((mask != 0) == known, "Only known combinations may resolve to native masks.");
            if (!known) continue;
            Check(CoopCampaignMapSettlementLevelPolicy.Capture(mask, host) == flags, "Native mask must encode exactly.");
            uint local = CoopCampaignMapSettlementLevelPolicy.Resolve(flags, client);
            Check(local != mask && CoopCampaignMapSettlementLevelPolicy.Capture(local, client) == flags,
                "Scene-specific bit positions must not cross the wire.");
            Check(CoopCampaignMapSettlementLevelPolicy.Matches(local, local) &&
                CoopCampaignMapSettlementLevelPolicy.Matches(uint.MaxValue, local) &&
                !CoopCampaignMapSettlementLevelPolicy.Matches(0, local), "Use the native subset rule.");
            Check(CoopCampaignMapSettlementLevelPolicy.Capture(mask | 1u, host) == 0, "Unknown native bits must fail closed.");
        }
        Check(CoopCampaignMapSettlementLevelPolicy.Resolve(9, _ => 0) == 0 &&
              CoopCampaignMapSettlementLevelPolicy.Resolve(9, _ => 1) == 0 &&
              CoopCampaignMapSettlementLevelPolicy.Capture(9, _ => 3) == 0 &&
              !CoopCampaignMapSettlementLevelPolicy.Matches(uint.MaxValue, 0), "Missing/alias/non-bit masks cannot reveal geometry.");
        int applications = 0;
        bool applySucceeds = true;
        var root = new Root();
        var admission = new CoopCampaignMapSettlementGeometryAdmission<Root>(
            (r, visible) => r.Visible = visible,
            (r, flags) => { Check(!r.Visible, "Variant application must happen while root is hidden."); applications++; return applySucceeds; });
        admission.Register("town", root);
        admission.Apply("settlement:town", true, 0);
        Check(!root.Visible && applications == 0, "Unavailable host choice cannot reveal default authoring geometry.");
        admission.Apply("settlement:town", true, 9);
        admission.Apply("settlement:town", true, 9);
        Check(root.Visible && applications == 1, "Unchanged choice must not repeat native calls.");
        admission.Apply("settlement:town", true, 10);
        Check(root.Visible && applications == 2, "Level changes must apply before reveal.");
        applySucceeds = false;
        admission.Apply("settlement:town", true, 12);
        Check(!root.Visible, "Failed variant application must hide the previous visible root.");
        applySucceeds = true;
        admission.Apply("settlement:town", true, 12);
        Check(root.Visible, "A later valid capture may recover.");
        admission.Apply("settlement:town", false, 12);
        Check(!root.Visible, "Disclosure removal hides even a known variant.");
        admission.Apply("settlement:town", true, 12);
        Check(root.Visible, "Fresh reveal reapplies the host variant.");
        admission.Apply("settlement:town", true, 0);
        Check(!root.Visible, "Loss of variant capture must not leave stale geometry visible.");
    }

    private static void ValidateRaidPresentation()
    {
        int effects = 0, applied = -1;
        bool geometryOk = true, effectOk = true;
        var root = new Root();
        var admission = new CoopCampaignMapSettlementGeometryAdmission<Root>((r, v) => r.Visible = v,
            (r, f) => geometryOk, null,
            (r, state) => { effects++; applied = state; return effectOk; });
        admission.Register("village", root);
        Check(applied == 0 && !root.Visible, "Registration removes authoring particles before disclosure.");
        admission.Apply("settlement:village", true, 9, "", 1, 2);
        int first = effects;
        admission.Apply("settlement:village", true, 9, "", 1, 2);
        Check(root.Visible && applied == 2 && effects == first, "Unchanged raid must not restart emitters.");
        admission.Apply("settlement:village", true, 9, "", 1, 3);
        Check(applied == 3 && effects == first + 1, "Raid completion replaces effect once.");
        admission.Apply("settlement:village", false);
        Check(applied == 0 && !root.Visible, "Hidden settlement clears particles.");
        admission.Apply("settlement:village", true, 9, "", 1, 2);
        geometryOk = false;
        admission.Apply("settlement:village", true, 10, "", 1, 2);
        Check(applied == 0 && !root.Visible, "Failed geometry admission clears prior raid effect.");
        geometryOk = true; effectOk = false;
        admission.Apply("settlement:village", true, 9, "", 1, 2);
        Check(!root.Visible, "Failed effect cannot reveal settlement.");
        effectOk = true;
        admission.Apply("settlement:village", true, 9, "", 1, 2);
        Check(root.Visible && applied == 2, "Failed application retries on fresh update.");
        int beforeClear = effects;
        admission.Clear();
        admission.Apply("settlement:village", false);
        Check(effects == beforeClear, "Cleared scene bindings never dereference old roots.");
        var catalog = new CoopCampaignMapPrototypeCatalogEntityState { SettlementRaidState = 2 };
        var copy = catalog.Clone();
        Check(copy.SettlementRaidState == 2 && CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(catalog, copy),
            "Catalog clone preserves active raid.");
        copy.SettlementRaidState = 3;
        Check(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(catalog, copy),
            "Raid-only change must publish a catalog delta.");
        var entity = new CoopCampaignMapPrototypeEntityState { SettlementRaidState = 3 };
        Check(entity.Clone().SettlementRaidState == 3, "Replica clone preserves completed raid.");
        for (int flags = 0; flags < 16; flags++)
        {
            bool visible = (flags & 1) != 0, village = (flags & 2) != 0;
            bool active = (flags & 4) != 0, raided = (flags & 8) != 0;
            int state = CoopCampaignMapRaidPresentationPolicy.Capture(visible, village, active, raided);
            Check(CoopCampaignMapRaidPresentationPolicy.IsValid(state), "Raid capture is bounded.");
            string effect = CoopCampaignMapRaidPresentationPolicy.ParticleSystem(state);
            if (!visible) Check(state == 0 && effect == "", "Undisclosed raid cannot produce an effect.");
            else if (!village) Check(state == 1 && effect == "", "Fortification/hideout must not acquire village raid FX.");
            else if (active) Check(state == 2 && effect == "psys_fire_smoke_env_point", "Active raid precedes raided state.");
            else if (raided) Check(state == 3 && effect == "map_icon_village_plunder_fx", "Completed raid uses native plunder effect.");
            else Check(state == 1 && effect == "", "Recovered village clears effects.");
        }
        Check(!CoopCampaignMapRaidPresentationPolicy.IsValid(-1) &&
            !CoopCampaignMapRaidPresentationPolicy.IsValid(4), "Unknown wire values rejected.");
    }

    private static string Slice(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = source.IndexOf(end, first, StringComparison.Ordinal);
        return source.Substring(first, last - first);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
