# Release Packaging Standard

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
