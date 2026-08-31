# Battle Test Automation Milestone 2B.2A Current-Source Staging Evidence

Status: **Complete for committed-source on-disk identity; Bannerlord runtime loading remains unverified**

Execution date: **2026-08-31**

Compiled source revision: **`12abf363ae978d520558b7c3bbd226137a816a8a`** (`12abf36`)

Branch: **`codex/v0.1.1-refresh`**, exact revision present on `origin`

Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 7

## 1. Outcome

Milestone 2B.2A rebuilt the Milestone 2B.1 runtime-safety source from the clean pushed revision, proved all 21 contract projects, and installed the exact resulting client and dedicated assemblies through a bounded `DeployWithRestore` transaction. The installed trees, retained pre-images, run-owned staged inventories, and ignored repository output identities were verified by recursive path/length/SHA-256 inventories.

No Bannerlord client, launcher, dedicated server, crash reporter, campaign, mission, or battle was launched. This is `ConfirmedPathHashOnly` evidence. It does not establish `ConfirmedLoadedHash`, connectivity, mission bootstrap behavior, result suppression inside a live process, cleanup behavior after a live run, or any L2/L3 battle claim.

## 2. Authoritative runs

| Purpose | Run ID | Outcome |
|---|---|---|
| Clean-revision contract inventory | `m2b2-contracts-20260831-01` | `Pass`; 21/21 projects; no product process launched |
| Clean-revision client/dedicated compile | `m2b2-prestage-compile-20260831-01` | `Pass`; installed inventories unchanged |
| Superseded preparation attempt | `m2b2-stage-20260831-01` | Stopped before manifest, backup, lock, or installed-tree mutation; evidence retained |
| Controlled installation with retained pre-images | `m2b2-stage-20260831-02` | `Installed`; installed inventories exactly equal prepared inventories |
| Post-stage environment doctor | `m2b2-poststage-doctor-20260831-01` | `EnvironmentBlocked`; sole blocker `RuntimeVersionCombinationNotYetVerified` |

The run roots are:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-contracts-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-prestage-compile-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-02
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-poststage-doctor-20260831-01
```

## 3. Binary identity

| Role/path class | Retained pre-image | Installed from `12abf36` |
|---|---|---|
| Client `bin/Win64_Shipping_Client/CoopSpectator.dll` | `3D3C6F1DD8BEAA3CB427108CB30A2B1C69D4CC5F769B3E764EBDCC13126AE532` | `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928` |
| Client `bin/Win64_Shipping_Client/CoopSpectator.pdb` | `A5ADC7D07D73B2C45E348BB58ECB669FF119D320E1B080043BDCA7EE0EDC6258` | `21F98483593B768DB5836D07AC58D87AF8727F6D22FBC88A6F5EFDD8C585982B` |
| Dedicated client-bin `CoopSpectator.dll` | `1D2BCAE905A8634D96593BB35D3E8D2AA0636701B40A935D555D472543BFF66C` | `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626` |
| Dedicated server-bin `CoopSpectator.dll` | `1D2BCAE905A8634D96593BB35D3E8D2AA0636701B40A935D555D472543BFF66C` | `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626` |

The clean committed client hash intentionally differs from the earlier dirty-working-tree Milestone 2B.1 compile hash `D7C2B71E995065B9CA0D688B5DDF30730DE57F1B9EEF3F63778D9C8C9E98C189`. Runtime evidence must identify the clean committed hash above and must not relabel the earlier compile output.

## 4. Tree identity and retained restore boundary

| Tree | Files | Recursive fingerprint before | Recursive fingerprint after |
|---|---:|---|---|
| Client installation | 32 | `424458AC7EF8864109D29B354F307928DC09046CB3B00CC4192534D0D3E74D68` | `19369B25E232718F1291A54853E0FA090B0AE53AC300E731BDD04123AC5BAAEE` |
| Dedicated installation | 218 | `30D488289A686D7FB24E0E319C975107BFF50B41ED5B3F10754F8518C1B18AF0` | `89B1630961307D90943ACFCA2936D1C4EC4ADC57FB59979111B26A6812B1F404` |

The complete immediately preceding `0.3.2` installation trees remain at:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-02\backup\client\CoopSpectator
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-02\backup\dedicated\CoopSpectatorDedicated
```

The older [Milestone 2B staging record](BATTLE_TEST_AUTOMATION_M2B_STAGING.md) separately retains the pre-`0.3.2` trees from its historical installation. Neither backup set may be treated as temporary cleanup data while its corresponding runtime evidence or rollback need remains open.

The pre-sync ignored repository outputs were also copied below the current run root before replacement:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-02\backup\repository-output-pre-sync
```

## 5. Controlled transaction

1. The operation required clean revision `12abf36`, exact agreement with `origin`, no product process, and unowned ports `7210` and `7777`.
2. All 21 contract projects passed, then compile-only produced the identified client DLL/PDB and dedicated DLL without changing installed inventories.
3. Preparation copied each complete installed tree into a fresh run-owned staging tree before overlaying repository module assets and exact compile outputs. This preserved installed-only dedicated `SceneObj` content.
4. The only client deltas were the client DLL and PDB. The only dedicated deltas were the client-bin and server-bin dedicated DLLs. No file was added or removed.
5. Both descriptors remained version `v0.3.2` with the required dependencies.
6. Apply revalidated all recursive inventories and binary hashes, acquired separate global client/dedicated installation mutexes, exclusively opened the installed DLLs, moved both complete installed trees to backup, and moved both prepared trees into the approved installation paths.
7. Post-apply inventories had to equal the prepared inventories exactly, and backup inventories had to equal the captured pre-images exactly. Any partial failure path was prepared to preserve the applied tree and restore only the matching backup.
8. Only ignored repository module-output DLL/PDB files were synchronized afterward. The tracked working tree remained clean before documentation updates.

No production save, global battle result, host marker, other game module, release archive, or tracked runtime source file was changed by this staging operation.

## 6. Non-mutating script failures retained as evidence

Two orchestration-script mistakes were encountered and retained rather than hidden:

- `m2b2-stage-20260831-01` hit a Windows PowerShell empty-array `.Count` edge case after creating staging copies. It stopped before manifest completion, backup creation, lock acquisition, or installed-tree mutation. `artifacts/failure.json` records the unchanged installed hashes.
- the first apply call for `m2b2-stage-20260831-02` used a `Test-Path` logical expression without explicit parentheses. PowerShell rejected it before mutex creation, backup creation, or any directory move. `artifacts/failure/apply-preflight-01.json` records manifest state `Prepared`, both staged trees present, the old installed hashes, no backup root, and zero product processes.

The corrected apply revalidated the full pre-image and staged inventories before performing the successful transaction. Neither issue is battle-type-specific because both occurred entirely in external staging orchestration before a game process existed.

## 7. Post-stage doctor

`m2b2-poststage-doctor-20260831-01` recorded:

- repository revision `12abf363ae978d520558b7c3bbd226137a816a8a` and repository dirty `false`;
- client and dedicated module versions `0.3.2`;
- installed and repository client hash `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`;
- installed and repository dedicated hash `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`;
- required ports inspectable and unowned;
- no product process launched;
- terminal outcome `EnvironmentBlocked` with sole reason `RuntimeVersionCombinationNotYetVerified`.

The nonzero doctor exit is expected. On-disk identity cannot prove which assembly a live role loads.

## 8. Evidence boundary and next gate

Milestone 2B.2A closes the clean-current-source on-disk staging prerequisite for the first Milestone 2B.1 live probe. It does not complete Milestone 2B as a whole.

The next separately approved step is one `Feasibility` connection-only run using the exact hashes in this document. It may perform only the already specified minimum vanilla `TeamDeathmatch`/`start_game` server bootstrap, exact loaded-role acknowledgement, normal-lobby client connection, unchanged global-result proof, and exact owned-process cleanup. It must not load a campaign, run a cooperative battle fixture, consume a campaign result, or claim L2/L3 evidence.
