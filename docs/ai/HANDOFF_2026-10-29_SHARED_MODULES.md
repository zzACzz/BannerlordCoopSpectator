# Development checkpoint before shared server module work — 2026-10-29

## Authority and sequence

The user requested a Git commit and push of the accumulated replica work, then
removal of the need to copy SandBox and SandBoxCore into the dedicated server.
The Goal tool currently reports **paused**. Older index entries saying active
describe historical turns; do not infer current autonomous authority from them.
Read AGENTS.md and obtain approval for the concrete shared-module implementation
plan before editing that subsystem. This checkpoint is not a production fix or
an announcement that War Sails or arbitrary overhaul mods are supported.

## Source, installed binaries and preview are different baselines

- Source candidate: cmp-b302. Both isolated client/dedicated builds passed;
  cmp-c203 passed all 26 canonical contract projects. Eight compiled detached
  checks passed, including prepared auto-battle reward closure/row admission.
  Evidence was reread before this checkpoint; it was not rerun for this commit.
  Native reward formatter/restoration/rendering acceptance remains open. See
  [auto-battle details](CAMPAIGN_AUTO_BATTLE_WINDOW.md).
- Installed and distributed baseline: cmp-b299, not b302. The exact preview is
  `dist/previews/Warhost_MapReplica_TwoPC_b299_20261028.zip`, 11,566,650 bytes,
  SHA256 `E672ADB1393F8F8B832F047EE2EFA51C7114895CB7F0CBDFA58E763F8DF3F4FD`.
  Client MVID: `52babf56-9963-42d3-ad0a-e04315bc2acf`; dedicated MVID:
  `1e43c2fa-a028-4caa-91a5-c1887d0f36a5`. Metadata still says 0.3.2.
- Protocol 71 / host bridge 66 / tooltip 6 / catalog 10 / CMN12 / CAS1 / CAF3 /
  CML2. All interacting roles must be coherent.

Accumulated changes include save-reload owned-session reset, native-ended mission
closure, selected citadel cohorts, crafted hero portrait resources, independent
prisoner inspection, settlement top panels/tooltips, auto-battle presentation,
English camera UI and default-on map/automatic transitions/siege flights.
Read each focused living document for bounded native acceptance and open gates;
this list does not claim every combination was tested.

## Local native evidence and cleanup boundary

Run `artifacts/runtime/map-default-start-r71-20261028` retained b299 role identities
and `evidence/default-map-hideout-map.json`. No explicit map-start command or
process-local enabling environment overrides were supplied. Logs prove owned
save reload/reset, automatic map -> bandit_forest_sv hideout -> map. The user
confirmed automatic startup and return. This was **one local multiplayer peer**.

That run's final cleanup report was not present when reread. Do not claim r71 is
closed or restore its old preimages blindly after subsequent manual tests or
reboots. Reconcile current processes/configuration and inspected differences first.
The earlier r69/r70 closures are separate and do not close r71.

## First remote two-PC report — read-only diagnosis

The later tester report supersedes the earlier statement that no two-PC test had
been attempted. The attempt **failed**; there is no successful two-PC acceptance.
Private supplied logs are not committed. Reproduction/evidence references:

| Role | Log | Bounded observation |
| --- | --- | --- |
| Campaign | host/rgl_log_2592.txt | 10:48:17.376 command modules include NavalDLC and MyAgeFix; 10:48:23.022 DLC overrides main-map resources |
| Dedicated | host/rgl_log_17644.txt | Correct b299 dedicated MVID; command modules omit NavalDLC; engine build 118999 versus game 119303 |
| Host multiplayer | host/rgl_log_25372.txt | Correct b299 client MVID; command modules omit NavalDLC; Main_map loaded from Sandbox |
| Remote multiplayer | client/rgl_log_8560.txt | Correct b299 client MVID; command modules omit NavalDLC; Main_map loaded from Sandbox |

Both multiplayer peers joined. This is not the previous missing-host-client case.
Both received map state/dynamic data and catalog chunks through 85/86. Campaign
publication later became stale; missing party visibility needs separate diagnosis.
The different campaign/client map resources strongly explain displaced labels,
but exact map parity is not yet demonstrated by a controlled correction.

The field battle was `battle_terrain_029`, 179 attacker / 56 defender active
fighters. The server materialized armies but remained in SideSelection. Server
and clients report ten unavailable equipment IDs, including `nord_spear_4_t5`,
`nord_king_armor_b` and `northman_raider_cape`, and crafted dependency
`naval_axe_head_08` unresolved. Missing strict pre-spawn equipment blocks the
shared land-battle readiness predicate in CoopMissionBehaviors. The clients'
initial-materialization acknowledgements alone do not override that predicate.
This is a confirmed missing-resource blocker, not proof of the only possible
failure. Engine build difference and unrelated campaign mod are additional
compatibility variables, not established root causes.

## Next requested scope: share base modules without content duplication

Implement SandBox/SandBoxCore sharing first; keep DLC/overhaul support separate.
Current relevant paths:

- `DedicatedHelper/DedicatedHelperLauncher.cs`: Start, shared launch environment,
  explicit game root, exact module argument set and process working directory.
- `Infrastructure/ModulePathHelper.cs`: existing managed XML-path fallback can
  find the game installation; it does not make native resource discovery use it.
- `scripts/CreateReleasePackage.ps1`: Copy-HostPayload still constructs base
  module subsets; packaging/run instructions need a deliberate migration policy.
- `Infrastructure/ExactCampaignRuntimeItemRegistry.cs`: fixed source-module list
  excludes NavalDLC; sharing base folders will not itself fix DLC equipment.

The global replica scene is client-rendered; the dedicated server uses a bootstrap
scene and relays campaign state. Actual battle scenes still require server-side
scene/physics/navigation resources. Shared on-disk storage does not merge process
memory or guarantee that arbitrary campaign DLLs can run on a dedicated server.

Research native module/resource path discovery before choosing direct path
registration versus directory links. Preserve existing copied folders, handle
game/server installations on different drives, validate exact targets, avoid
recursive deletion or broad native-module replacement, and keep server-specific
configuration/binaries private. A missing target must yield an actionable error.
Check field/village/hideout/siege/citadel/ambush and owned map transitions, not only
successful process startup. No module sharing or DLC resource fix exists yet.

## Commit exclusions

Do not stage raw artifacts, user logs, dumps, generated previews, local binary
outputs or the abandoned `CoopCampaignLootTooltip.xml` experiment. Preserve those
local files without publishing them. The checkpoint contains project source,
tests, scripts and living documentation only; no new release/tag is requested.
