# Battle Test Automation Milestone 2B Staging Evidence

Status: **Complete for on-disk installation identity; runtime loading remains unverified**

Execution date: **2026-08-31**

Compiled source revision: **`f91eeff9b710f68fc7bf4b506ec39c2d1c4474bc`** (`f91eeff`)

Branch: **`codex/v0.1.1-refresh`**, pushed to `origin`

Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 6

## 1. Outcome

The installed client and dedicated modules were moved from version `0.3.1` to exact version `0.3.2` binaries compiled from revision `f91eeff`. The installed paths, repository module-output paths, and compile-only result hashes match. Full pre-images of both `0.3.1` module trees remain available for bounded restore.

This is `ConfirmedPathHashOnly` evidence. Neither Bannerlord role was launched after installation, so the operation does not establish `ConfirmedLoadedHash`, a supported runtime version matrix, a lobby connection, a mission, a battle, or campaign writeback.

## 2. Authoritative runs

| Purpose | Run ID | Outcome |
|---|---|---|
| Full pre-commit contract inventory | `m2a-precommit-contracts-20260831-01` | `Pass`; 20/20 projects |
| Clean-revision client/dedicated compile | `m2b-prestage-compile-20260831-01` | `Pass`; four assertions; installed inventories unchanged |
| Controlled installation with retained pre-image | `m2b-install-20260831-01` | `Pass`; installed inventories exactly match staged inventories |
| Post-install environment doctor | `m2b-postinstall-doctor-20260831-01` | `EnvironmentBlocked`; only `RuntimeVersionCombinationNotYetVerified` remains |

The compile-only and doctor run roots are below:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b-prestage-compile-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b-postinstall-doctor-20260831-01
```

The installation evidence and backups are below:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b-install-20260831-01
```

## 3. Binary identity

| Role/path class | Before | Installed after | Intended compile output |
|---|---|---|---|
| Client `CoopSpectator.dll` | `0.3.1`, `9B271E4E0CFA3AD0FF2DB4B3ACC5A69AE6405E833D52ECB4E1A4C0FDCA8C1B31` | `0.3.2`, `3D3C6F1DD8BEAA3CB427108CB30A2B1C69D4CC5F769B3E764EBDCC13126AE532` | Exact match |
| Dedicated server-bin `CoopSpectator.dll` | `0.3.1`, `A21ED2F00465584B603FB67DFEA292EAFB37C258E22C1FAC1356B008862E92C1` | `0.3.2`, `1D2BCAE905A8634D96593BB35D3E8D2AA0636701B40A935D555D472543BFF66C` | Exact match |
| Dedicated client-bin `CoopSpectator.dll` | `0.3.1`, same dedicated hash | `0.3.2`, `1D2BCAE905A8634D96593BB35D3E8D2AA0636701B40A935D555D472543BFF66C` | Exact match |

The clean-revision client hash differs from both the published `0.3.2` archive hash and the earlier dirty-working-tree Milestone 2A compile hash. This confirms that version labels alone are insufficient for source-equivalent testing.

## 4. Controlled installation method

The approved operation applied the specification's `DeployWithRestore` safety policy as a bounded one-time staging operation; it did not add or advertise a reusable runner deployment command.

1. It refused non-fresh run roots and any running Bannerlord, TaleWorlds launcher, dedicated-server, or crash-reporter process.
2. It compiled both projects under the compile-only run root from clean pushed revision `f91eeff`.
3. It copied the current installed module trees into run-owned staging directories. This preserved the dedicated module's 195 existing `SceneObj` files.
4. It overlaid current repository module assets, client `ModuleData`, and exact compile outputs.
5. It validated both `SubModule.xml` files as `v0.3.2`, required runtime dependencies, and all three intended `CoopSpectator.dll` hashes before installation.
6. It captured recursive pre-install and staged path/length/SHA-256 inventories.
7. It acquired exclusive client and dedicated installation mutexes, moved both complete `0.3.1` module directories to the run-owned backup, and moved the verified staged directories into the exact approved module paths.
8. It recomputed recursive installed inventories and required exact equality with the staged inventories.
9. It refreshed only ignored repository module-output binaries so the environment doctor could compare the installed paths with the identified compile outputs.

No save, campaign bridge result, `SandBox`, `SandBoxCore`, other game module, release archive, or tracked source file was changed by the installation operation.

## 5. Restore evidence

The complete pre-images remain at:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b-install-20260831-01\backup\client\CoopSpectator
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b-install-20260831-01\backup\dedicated\CoopSpectatorDedicated
```

The client backup contains 32 files and the dedicated backup contains 218 files. The retained backup DLLs independently identify as the original `0.3.1` hashes listed above.

The first apply command stopped before acquiring locks or moving any directory because a Windows PowerShell logical expression lacked explicit grouping. Inspection confirmed that both installed `0.3.1` trees and both staged trees remained unchanged. The corrected generic expression was then used for the successful transaction; the issue was not battle-type-specific.

## 6. Post-install doctor

`m2b-postinstall-doctor-20260831-01` recorded:

- repository dirty: `false`;
- source-completion eligibility: `true`;
- installed client version/hash equal to the identified repository client output;
- installed dedicated version/hash equal to the identified repository dedicated output;
- required ports inspectable and unowned;
- no product process launched;
- no installed/repository hash blocker;
- sole blocker: `RuntimeVersionCombinationNotYetVerified`.

The doctor outcome remains correctly `EnvironmentBlocked` with process exit code `10`. Installation alone cannot turn an unobserved runtime combination into a supported one.

## 7. Evidence boundary and next step

The installed binaries remain bound to compiled source revision `f91eeff`. The documentation-only successor commit does not change runtime source files, but future evidence must not silently relabel these DLLs as having been compiled from that later documentation revision. A runtime run must either declare `f91eeff` as its intended build revision or perform another clean compile-and-stage cycle from its selected revision.

The next approved runtime step should be connection-only:

1. launch the exactly owned dedicated role and require a role-reported loaded path/hash;
2. launch the exactly owned multiplayer client with the expected client hash;
3. require exact server discovery, join request/acknowledgement, network handoff, connection, and cleanup evidence;
4. do not issue `start_game`, open a mission, or create/consume a battle result.

Only after both roles report the expected loaded hashes can the named version combination move from `ConfirmedPathHashOnly` to `ConfirmedLoadedHash` and the remaining doctor blocker be reconsidered.
