# Release Packaging Standard

## Current b34 two-PC test baseline (2026-10-31)

The user authorized a source checkpoint/push and a fresh local test archive after
accepting b34 water and map rendering. Fresh archive target:
`dist/previews/Warhost_MapReplica_TwoPC_b34_20261031_r2.zip`.
Earlier archives remain immutable. This authorization supersedes the previous
archive-creation hold; no archive upload, release or tag is requested.

Reuse exact successful isolated runs `naval-b34` and `naval-c26` through
`CreateTwoPcPreviewPackage.ps1`; do not rebuild or deploy merely to package.
Client SHA256:B94F4F4488E2ECEC9FFFA2C5F7C92DA018120A79E1B8B1476F64A583656F114B.
Dedicated SHA256:5BA1F212316D90CADA1CAA1991CDE36B734626E5582FF978D399D621F7C2EE13.
Both builds, all27 contract projects and all18 focused compiled checks passed
for that candidate. Package creation rechecks output hashes, default flags,
copied payloads and every ZIP entry. Record the final ZIP hash at delivery;
MANIFEST.json records the source checkpoint and exact payload identities.

Local acceptance covers initial/post-village/post-reload Naval water, hero and
Training Field appearance, and background recovery. A separate base-game b34
siege on empire_town_d completed with2000 initial foot agents, native
reinforcements, final campaign writeback and map return. This was one peer,
attacker-side, not the tester's two-client khuzait_city_b defender reproduction.
Two-PC acceptance and that freeze diagnosis remain open. Preserve background
map updates; retain normal trace-off launchers and exclude private evidence,
PDBs, heavy game resources, machine-local links and abandoned tooltip assets.
See docs/TWO_PC_PREVIEW_TEST_EN.md for installation and matching content profiles.

Earlier package status below is historical and does not override this baseline.

CURRENT WATER ISSUE: b26 preview below is immutable DEVELOPMENT only; r93 reports
black water in MP after connected reload, campaign normal. See adjacent preview
KNOWN_ISSUES notice and NAVAL_WATER_RELOAD_2026-10-30.md. It is not the final
accepted two-PC candidate. No b27 correction/archive exists.

Verified development preview2026-10-30: Warhost_MapReplica_TwoPC_b26_20261030_preview.zip,
11606667bytes, SHA2568011361A85B28946FFDD9D0016341DC6BE83DDBF1393895180F10F88BBC8EF77.
Created from isolated naval-b26/all27 naval-c19; both compiled default checks pass,
50 payload files+manifest, every ZIP entry hash verified. Normal trace-off BAT,
no private diagnostics/PDB/abandoned tooltip/heavy resources. Naval field and base
night-hideout/map return are user-accepted; sustained/reload/remaining scenarios
and two-PC acceptance remain open. This is a development test artifact, not a
public release. Read r93/PACKAGE.md and docs/TWO_PC_PREVIEW_TEST_EN.md.

## Shared base-module source candidate (2026-10-29)

New source no longer embeds SandBox/SandBoxCore resource subsets in any host
layout. The campaign launcher creates validated machine-local directory links
before dedicated startup; no link is archived. Keep private dedicated binaries
and XML payloads. See SHARED_SERVER_MODULES.md for migration, rollback and native
acceptance gates. Existing b299 ZIP below is immutable and predates this change;
do not describe it as a shared-module build. No replacement archive is produced.

## Default-on map replica testing preview (2026-10-28)

User requested automatic replica startup by default and a package for remote testers.
Verified artifact: `dist/previews/Warhost_MapReplica_TwoPC_b299_20261028.zip`,
11,566,650 bytes, SHA256
`E672ADB1393F8F8B832F047EE2EFA51C7114895CB7F0CBDFA58E763F8DF3F4FD`.
Sources cmp-b299 (both isolated builds) and cmp-c202 (all26 projects); eight
compiled client checks and five fresh-process defaults cases on the dedicated
binary pass. Manifest records source checkpoint plus dirty snapshot, build/test
IDs, tested Native v1.4.8 and default-on capabilities. Ten defaults cases across
both binaries are retained in Verification/replica-defaults.txt. All50 ZIP entries
enumerated;49 payload entries length/hash-verified, plus MANIFEST.json.

Package layout remains Client (both game installations) / Host (host dedicated
module only). Includes English current instructions and Harmony license; excludes
the abandoned CoopCampaignLootTooltip.xml experiment and PDB/debug/private files.
It contains no saves, networking credentials or machine configuration. No upload,
tag, public release or Git commit is implied. Metadata remains0.3.2; preview name
and file hashes identify it. Two-PC acceptance remains for the user/testers.

Map replica, owned automatic mission transitions and functional siege-flight
capture now default ON in the compiled mod, not via the BAT. Each previous
environment gate supports explicit0 opt-out. Diagnostics/test automation and
portrait-pose observer remain default OFF. Existing exact native call-site and
process/campaign ownership guards are retained. No separate console map command
is needed for a campaign-launched server. Standalone servers are not adopted.
This overrides historical default-off map guidance below.

## Two-PC development preview exception (2026-10-17)

For a user-requested local test archive, `scripts/CreateTwoPcPreviewPackage.ps1`
accepts explicit successful isolated build and full-contract run roots plus a
fresh package name. It writes only `dist/previews/<name>` and its ZIP, refuses
existing destinations, and does not build, deploy, publish or touch public release
assets. The archive separates Client (both PCs) and Host (dedicated module only).
Source assets exclude stale module bin directories; binaries/dependencies come
from the exact isolated output, with the existing standard multiplayer runtime
copies from the installed game/server. Client ModuleData is mirrored to the host.
Build output hashes, matching descriptors, copied files and ZIP entry hashes are
checked. A manifest, build/test evidence, Harmony license and the English manual
test instructions are included. This is not a clean public release: metadata may
remain v0.3.2, so the preview name and hashes identify it. No speculative diagnosis
may be advertised as a confirmed fix. Document current rollout gates explicitly;
the b299 map defaults above supersede the old opt-in map preview behavior.

Verified preview: `dist/previews/BannerlordCoop_TwoPC_b124_20261017_r4.zip`,
11,338,876 bytes, SHA256
`FDD6A4B27C5C9283E31DC222F9F98E43D937C947000D4E5E0390F1131164ECAB`.
Sources: portrait-render-b124-20261017 (both isolated builds) and
portrait-render-c119-20261017 (all26 contracts). All47 ZIP entries were enumerated;
46 payload entries were length/hash verified against the staging manifest, with
MANIFEST.json as the remaining entry. No installation or publication occurred.
Earlier attempts are NOT delivery artifacts: initial staging stopped at a
PowerShell reserved variable; r2 ZIP failed slash-normalized entry verification;
r3 stopped before ZIP creation because Compression assembly loading was missing.
The corrected r4 writes forward-slash entry names explicitly and passed validation.

This document is the source of truth for creating release archives for
BannerlordCoopCampaign. It covers local artifact creation and validation only.
Publishing, tagging, committing, pushing, or uploading artifacts always requires
separate explicit user approval.

## Version and document sources

- Read the release version from `Module/CoopSpectator/SubModule.xml`.
- The dedicated module version in
  `Module/CoopSpectatorDedicated/SubModule.xml` must match it.
- Release documents must already exist under `dist/releases/<version>`:
  - `CHANGELOG_<version>_EN.md`
  - `CHANGELOG_<version>_UA.md`
  - `README_<version>_EN.md`
  - `README_<version>_UA.md`
- Local release documents are authoritative. If an uploaded asset is edited
  manually, synchronize the local file before recreating archives.

## Preferred commands

Build once and create both GitHub and Nexus assets:

```powershell
pwsh ./scripts/CreateReleasePackage.ps1 -ReleaseAssetsOnly
```

Reuse already built module files and create both asset sets:

```powershell
pwsh ./scripts/CreateReleasePackage.ps1 -SkipBuild -ReleaseAssetsOnly
```

Create only one asset set when needed:

```powershell
pwsh ./scripts/CreateReleasePackage.ps1 -SkipBuild -GitHubAssetsOnly
pwsh ./scripts/CreateReleasePackage.ps1 -SkipBuild -NexusAssetsOnly
```

`NexusAssetsOnly` requires the matching GitHub client and host archives to
already exist because the Nexus payload is derived and verified against them.

## Artifact matrix

### GitHub client

Path:

```text
dist/releases/<version>/BannerlordCoopCampaign_<version>_Client.zip
```

Root entries:

```text
Modules/CoopSpectator/...
run_mp_with_mod_from_game_root.bat
```

The module includes `CoopShaderCacheModeSwitch.ps1`. Standalone README and
CHANGELOG files remain separate GitHub release assets.

### GitHub host

Path:

```text
dist/releases/<version>/BannerlordCoopCampaign_<version>_Host.zip
```

Root entries:

```text
Modules/CoopSpectatorDedicated/...
```

### Nexus client

Path:

```text
dist/releases/<version>/Nexus/BannerlordCoopCampaign_<version>_Client.zip
```

Root entries:

```text
Modules/CoopSpectator/...
CHANGELOG_<version>_EN.md
CHANGELOG_<version>_UA.md
README_<version>_EN.md
README_<version>_UA.md
```

The Nexus client archive must not contain:

```text
run_mp_with_mod_from_game_root.bat
Modules/CoopSpectator/CoopShaderCacheModeSwitch.ps1
```

The launcher is distributed separately on Nexus. Removing the shader-cache
helper does not prevent the mod from loading, but Nexus users do not receive its
automatic Direct3D 11 shader-cache cleanup behavior.

### Nexus HostLite

Path:

```text
dist/releases/<version>/Nexus/BannerlordCoopCampaign_<version>_HostLite.zip
```

Root entries:

```text
Modules/CoopSpectatorDedicated/...
CHANGELOG_<version>_EN.md
CHANGELOG_<version>_UA.md
README_<version>_EN.md
README_<version>_UA.md
```

## Required validation

The packaging script must fail before assigning final Nexus filenames unless all
of these conditions hold:

1. Client and dedicated `SubModule.xml` versions match the requested release.
2. Packaged `CoopSpectator.dll` files match the current module binaries by
   SHA-256.
3. Every Nexus module file matches the corresponding GitHub archive entry by
   path, length, and SHA-256, except for the two explicitly excluded client
   launcher/cache files.
4. All four release documents are present at the archive root and match their
   local source files by SHA-256.
5. Nexus archives contain no `.bat`, `.ps1`, or `.pdb` files.
6. Nexus archives contain no unexpected wrapper directory or extra root entry.
7. Temporary `.partial_*.zip` files are removed after success or failure.

The script prints final SHA-256 values after successful Nexus packaging. Record
those values when handing the artifacts to the user.

## Safe operating procedure

1. Inspect the worktree and preserve unrelated or user-authored changes.
2. Prepare version metadata and all four release documents.
3. Build and test only after explicit approval.
4. Run `ReleaseAssetsOnly` to create both platform-specific asset sets.
5. Inspect the script's validation result and final hashes.
6. Do not publish, tag, stage, commit, or push without separate live approval.

Do not use old Nexus archives as binary sources. They are structure references
only; every new release must use the current version's verified GitHub payload.
