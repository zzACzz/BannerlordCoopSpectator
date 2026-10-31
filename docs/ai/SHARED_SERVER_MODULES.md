# Shared SandBox / SandBoxCore resources — 2026-10-29

r72 is now CLOSED: all7 configuration hashes match, all game roles exited and
Steam/saves were preserved. The actual module junctions and committed backup are
retained. The follow-up missing aftermath/input findings have an approved source
correction documented in
[the publication-bundle note](AFTERMATH_PUBLICATION_BUNDLE_2026-10-29.md).
Do not attribute that cache/focus correction to a new base-resource copy or DLC
support; broader shared-scene acceptance remains open.

## Status and scope

User approved implementation after checkpoint `6846fb9`. This work removes base
module resource duplication for the campaign-owned Windows dedicated launch.
It does not implement NavalDLC or arbitrary overhaul support. Protocol and game
authority are unchanged. The Goal mechanism remains paused; the implementation
has direct task approval.

Both isolated sm-b2 builds, all 27 sm-c1 contract projects and the compiled net472
filesystem check pass. Fresh deployment r72 installed nine coherent files after
retaining seven configuration preimages. Native migration/startup and a complete
field battle/map return are accepted below. Broader scene acceptance remains open.
The immutable distributed b299 preview does not contain this feature.

## Launch and path discovery

`DedicatedHelperLauncher.Start` determines the game root from the running
campaign's application base directory and validates Bannerlord.exe / Modules.
There is no runtime hard-coded C drive or Steam folder. Dedicated discovery:

1. Explicit `BANNERLORD_DEDICATED_SERVER_PATH`, root or exact Starter.exe path.
   An invalid/relative override fails; it does not select another installation.
2. The dedicated installation beside the running game.
3. Steam libraries listed by the current game library and HKCU SteamPath, using
   bounded modern/legacy libraryfolders.vdf parsing. App 1863440's manifest
   installdir is honored, with the standard server folder as a fallback.

Only configured libraries are searched, with a 128-library bound and 4 MiB text
file limit. No recursive drive scan or registry mutation. The server executable
must be DedicatedCustomServer.Starter.exe. Game/server can reside on different
local volumes; actual cross-volume acceptance requires such an environment.

## Filesystem ownership and transaction

`SharedServerModules.Prepare` resolves actual directory identities through
GetFinalPathNameByHandle. The exact game and server roots must be local, separate
and non-nested. Server Modules must be an ordinary directory inside the server.
It validates both source descriptors (IDs, bounded XML/no DTD) and ModuleData /
SceneObj directories before moving either destination.

- Server `Modules/SandBox` -> actual game `Modules/SandBox`.
- Server `Modules/SandBoxCore` -> actual game `Modules/SandBoxCore`.
- A matching directory junction is reused. An unknown link, different target,
  missing source or occupying file is rejected without replacing it.
- A server-root exclusive lock is held through process launch. The launcher
  rejects a known running owned server or a matching server executable in the
  installation; unreadable candidate process identity fails closed. It rechecks
  before migration. It never terminates a server to make migration possible.
- New junctions are prepared in an owned transaction directory. Previous ordinary
  folders are **renamed**, not copied/deleted, into
  `CoopSpectator.SharedModuleBackups/<UTC-and-GUID>/`. This is outside Modules so
  the native module scanner cannot load the backup as a duplicate module.
- Older deployment copied 24 exact stock hideout directories into our dedicated
  module's SceneObj. Existing ordinary copies of those names are preserved in
  the same transaction as LegacyHideout-* entries, preventing them from overriding
  the now-shared Sandbox source. Unknown/custom scenes are left alone. Every
  source must exist before moving its old copy; linked legacy directories fail.
- RESTORE.txt records the actual game sources, server destinations, prior-folder
  locations and transaction state. Exceptions reverse installed exact links and
  move originals back. Rollback conflicts are reported with the backup location;
  no foreign file/link is overwritten to conceal them.

`WindowsDirectoryJunction` uses only directory handles and mount-point reparse
buffers. It does not shell out, invoke an installer, recurse-delete module trees,
write game resources or elevate the game. Permission errors produce an actionable
launch error. The original native module/resource lookup remains unchanged.

This is sharing, not an immutable filesystem mount: external tools can write
through a junction. Our preparation/deployment code must not target the shared
base-module paths. Existing backups still consume their previous disk space;
renaming avoids a new copy but does not reclaim old bytes. Remove backups only
after acceptance and a separate deliberate decision to give up rollback.

## Build and packaging

Dedicated deployment no longer creates/copies base module descriptors, XML,
battle_terrain scenes or hideout resource clones. Release Copy-HostPayload no
longer embeds base module subsets in any layout. Machine-specific links must
never be packaged. The campaign client contains the preparation implementation;
the dedicated module alone does not create links during its own load.

Small SandBox.dll / TaleWorlds.CampaignSystem.dll scene-script dependencies stay
in the dedicated mod's private binary folders. Sharing resource directories does
not remove this existing managed runtime requirement. Native/Multiplayer server
modules, credentials, startup configuration and process working directory remain
server-local. No automatic DLC activation or resource redistribution is added.

## Verification and remaining gates

`SharedServerModules.ContractTests` is part of the canonical inventory (27
projects). It performs real Windows junction creation/readback in owned scratch
directories, verifies game source fingerprints, idempotence and exclusive locking,
preservation of previous module/hideout/custom-scene contents, rollback after a
second install failure, running-server gates, invalid/foreign source/link refusal,
overlapping-root refusal, modern/legacy VDF parsing, another Steam library, Unicode
and space-containing paths, explicit overrides, and no-copy deployment/package
guards. It does not start Bannerlord or claim native scene compatibility.

Initial isolated build sm-b1 passed before final legacy-hideout migration and
absolute-input guards. Targeted scratch tests t1/t2 passed. Final sm-b2 compiled
both roles with zero errors and unchanged installed inventories; sm-c1 passed all
27 projects. Client SHA256:
`F98725E515E8845FDF14FBD0BA47138B24708663F24431F78DFB60B26AC2AEB5`;
dedicated SHA256:
`C2BAD7464F69073EE6E95D2D0D2471C4591DA91A0B1BC7003D7D4BF11B6B7599`.
The dedicated hash is unchanged from b302 because launch preparation lives in the
campaign client, and deployment/project changes do not modify its runtime code.

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-SharedServerModules.ps1
-CandidateDirectory <isolated-client-bin>` passed against sm-b2 under Windows
PowerShell 5.1 / .NET Framework 4.7.2. It checks the actual compiled helper's
migration/reuse, exact link targets and source fingerprints in owned scratch
folders, with no Game constructed. Its first harness attempt failed on boxed
PowerShell argument conversion; explicit string arguments corrected the harness.
This was not a native game crash or filesystem migration failure.

Only C: is available in this environment. D:/E: path parsing is tested, and two
separate Steam-library directory trees are exercised; actual cross-volume
junction acceptance remains unverified.

Run `artifacts/runtime/shared-modules-r72-20261029` is OPEN. Exact campaign
first attempt79836 started2026-10-28T23:09:37.9147940Z and exited while Steam was
closed. Its records are retained under attempt1. After user requested retry with
Steam running, campaign96160 started2026-10-28T23:12:26.3813159Z. Nine file hashes
match sm-b2/source assets. Seven fresh configuration preimages and exact prior
module files are retained. Previous r71 preimages were not restored over newer
manual user state. Server/MP are for the user to launch through the supported
campaign menu and working BAT shortcut; do not start duplicates.

### Native r72 acceptance and new aftermath finding

User started dedicated94308 at2026-10-28T23:13:36.916197Z and multiplayer97004 at
2026-10-28T23:13:57.810322Z. These are observed user-started roles, not authorized
cleanup targets. Campaign/MP loaded client MVID7406d20e-f416-4c2b-8cc2-bef26af9d5d3;
dedicated loaded MVID1e43c2fa-a028-4caa-91a5-c1887d0f36a5. Actual server module
entries are Junctions with exact targets in the game installation. Transaction
`20261028T231336699-861e410c41454e969b0d145f10fb0ff0` is committed and retains both
previous ordinary directories. RESTORE.txt and link evidence are saved locally.

The server log loads `Modules/SandBoxCore/SceneObj/battle_terrain_029/scene.xscene`
through the new link at01:19:50.656 local. It reaches PreBattleHold01:20:19.337,
BattleActive01:20:22.757, BattleEnded01:21:36.780, EndBattle01:21:37.944 and
StartMap01:21:38.557. User confirms the field battle passed. Client Main_map
returns; native visual comparison confirms the replica renders. This is local
one-peer field evidence, not village/hideout/siege/citadel/ambush or remote proof.

User then reports missing Prisoners and Loot replicas, plus camera keyboard input
blocked until opening/closing Esc. Host logs repeatedly report
`capture:aftermath-dynamic-anchor-unavailable` from01:22:01.344. The publisher
rejects the anchored aftermath when its retained frame and current dynamic/catalog
caches disagree; the exact revision evolution still needs investigation. The
camera FocusedLayer gate is a separate suspected lifetime issue, not yet proven
to have the same cause. No aftermath/input fix has been made in this change.
Evidence: r72/evidence/field-and-aftermath-observations.json and paired log files.

Native acceptance must verify startup migration/reuse, actual scene resolution
through the game path, field/village/hideout/siege/citadel/ambush loading, and
automatic map -> battle -> map. Include restart and save reload. Paired/remote
client acceptance remains separate. The current archive b299 must remain immutable.

## Manual recovery

Close the exact dedicated process first. Inspect the transaction's RESTORE.txt
and actual destination targets. Remove only a junction whose target is the
recorded game module (remove the link itself, never recursively delete its
contents). Move the recorded previous folder back to its original server path.
Restore moved LegacyHideout-* folders only when their original destinations are
absent. Preserve conflicting data and investigate; do not overwrite it. Do not
restore old copies while the server is running. A crash/power loss can leave a
prepared journal or mixed migration state; inspect the retained transaction rather
than claiming the in-process exception rollback ran.
