# Aftermath publication coherence and map input recovery

## Observed failure

During shared-module run r72, the field battle loaded through the SandBoxCore
junction, completed and returned to the replica map. The user reported both
Prisoners and Loot absent and camera keys blocked until an Escape menu roundtrip.
Campaign96160 repeatedly logged `capture:aftermath-dynamic-anchor-unavailable`
starting at 01:22:01.344 local time. Private evidence is retained under
`artifacts/runtime/shared-modules-r72-20261029/evidence`.

The guard correctly rejected mismatched revisions, but used the wrong inputs.
`PublishTick` remembered only a non-dialogue MapState frame. Subsequent ordinary
map dialogue captures could replace `_cachedCatalog` and `_cachedDynamic` without
replacing that anchor. PartyState/InventoryState then compared the older frame
against newer live dependencies and stopped publication. Actual live revision
numbers were not instrumented; the source sequence and retained failure reason
identify this path without claiming a native-state dump.

## Approved correction

`Campaign/CoopCampaignAftermathMapAnchor.cs` retains detached deep clones of the
frame, catalog and dynamic snapshot as one campaign/session-owned bundle. Only a
complete successful map write remembers it. Schema, session and dependency
revisions must agree. The holder invokes no native campaign method; ownership
uses reference identity only.

PartyState/InventoryState acquire that exact bundle, renew receipt timestamps
and attach the current independently captured aftermath presentation. Publication
writes its retained catalog and dynamic dependencies before the new frame.
Live caches and revision counters remain untouched. Both dirty flags remain set
during retained publication, including partial-write failure, so the first normal
map publication restores live dependencies even when their content is unchanged.
Session launch, publisher teardown and owner/session mismatch revoke the holder.
No freshness/revision guard is removed; no protocol/schema change is needed.
The server already maps changed bridge revisions, including older retained
dependencies, to its own monotonically increasing network revisions.

This is the common aftermath path for field, village, hideout, siege, citadel,
siege-ambush and Send troops outcomes. Avoid a field-battle-only completion hook.
Save reload, repeated battles and auto-resolution use the same ownership rules.

## Input correction and evidence limit

The previous replica called TrySetFocus only when creating its input layer.
CanUseReplicaMapInput rejected every different focused layer, and menu rendering
treated it as a modal. Native ScreenManager can reassign focus; TryLoseFocus
selects an active focus layer by screen order. A native scene owner could therefore
leave this strict gate blocked. The exact focused layer during r72 was NOT
captured: Escape recovery proves the symptom, not that specific native owner.

Recovery now runs before input/menu decisions on a rendered client map with an
active replica layer and this MissionScreen on top. It calls TrySetFocus only if
focus is null or exactly MissionScreen.SceneLayer. It never calls TryLoseFocus,
reactivates another layer, dismisses a menu or claims unknown focus. Host dialogue,
stock Escape, MissionOptions, inquiry or another top screen veto recovery.
Camera/menu/map-bar visibility retain these guards even with null focus. No new
hot-path logging or live native reflection diagnostics were added.

## Verification and next native gate

Final af-b2 both isolated client/dedicated builds pass with installed inventories
unchanged. Final af-c2 all27 contracts and all eight detached compiled checks pass,
including the actual bridge-file regression. Private checks are retained under
artifacts/runtime/af-b2-managed-checks. af-b1/af-c1 are superseded by these gates.

Tests cover cache drift, deep-copy isolation, long-battle receipt renewal, real
bridge-file readback, normal-map restoration, expiry after silence, rejection of
owner/session/schema/revision mismatch and reset. Focus policy exhausts all256
boolean combinations. These are managed/common-route checks, NOT seven native
battle passes or native focus proof.

Repeat a short field battle or Send troops, leaving Prisoners then Loot open.
Verify independent inspection and host changes. Close Loot and test WASD/arrows
immediately without Escape. Check that Escape/options/dialogue retain ownership,
repeat a battle and reload a save while the server/client stay running. Extend to
siege/citadel/ambush after this passes. Do not declare the reported keyboard symptom
fixed until native repetition succeeds. A same-machine run is not two-PC proof.

## Runtime lifecycle

r72 CLOSED: user DS94308/MP97004 and owned campaign96160 all exited. All seven
configuration hashes match after restoring only the inspected dedicated map/mode
switch. Cleanup terminated no processes; Steam95464 and saves were preserved.
Module junctions and the committed old-copy backup remain. Use a fresh staging
run and configuration preimages, not r72. The b299 ZIP is unchanged; no new
archive, commit or push is implied by this correction. Goal remains PAUSED;
the user explicitly approved the normal-mode fix plan.

r73 CLOSED: nine coherent af-b2 files installed with seven fresh configuration
preimages. Campaign51340 started 2026-10-29T00:01:22.6692863Z and loaded client
MVID e5e5c8c8-5445-4bb7-a0d3-507e57dc60e4, SHA256
E7EC538882F5A0939BB1FC6C0758A4C2C6F220703D04971A5943CAF662626135.
Dedicated binary remains SHA256
C2BAD7464F69073EE6E95D2D0D2471C4591DA91A0B1BC7003D7D4BF11B6B7599.
The user reports that Prisoners, Loot and camera input now work. This is bounded
local acceptance, not proof of every battle type or remote clients. Artifacts:
artifacts/runtime/aftermath-bundle-r73-20261029. Cleanup recorded at
2026-10-29T01:06:34.0094005Z: all7 configuration hashes match; no remaining owned
IDs; campaign51340 had already exited. Only the inspected DS map switch was
restored. No process terminated; Steam and test saves remain. Use fresh preimages
for any next run.
