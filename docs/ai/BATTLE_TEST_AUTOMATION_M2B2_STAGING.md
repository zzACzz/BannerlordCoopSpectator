# Battle Test Automation Milestone 2B Controlled Staging Evidence

Status: **Historical staging retained; Revision 13 native platform-login client staged with path/hash proof, live proof pending**

Execution dates: **2026-08-31 and 2026-09-01**

Compiled source revisions: **`12abf363ae978d520558b7c3bbd226137a816a8a`** (`12abf36`), dedicated-only **`7628c85aef140e431f29982e016395b8f303a464`** (`7628c85`), corrected resolver client **`729fd2c325019ca026787b4ffcccf91beeced755`** (`729fd2c`), and native-login client **`6ed40e883cd9909f5af8f10a8e9df19376b6ed52`** (`6ed40e8`)

Branch: **`codex/v0.1.1-refresh`**, exact revision present on `origin`

Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 13

## 1. Outcome

Milestone 2B.2A rebuilt the Milestone 2B.1 runtime-safety source from the clean pushed revision, proved all 21 contract projects, and installed the exact resulting client and dedicated assemblies through a bounded `DeployWithRestore` transaction. The installed trees, retained pre-images, run-owned staged inventories, and ignored repository output identities were verified by recursive path/length/SHA-256 inventories.

No Bannerlord client, launcher, dedicated server, crash reporter, campaign, mission, or battle was launched by the staging operation. The staging evidence itself is `ConfirmedPathHashOnly`. A later [live feasibility attempt](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md) established the exact dedicated `ConfirmedLoadedHash` and `Suppress` policy, but did not establish client loading, connectivity, successful mission bootstrap, result suppression inside a mission, or any L2/L3 battle claim.

The latest `m2e-client-stage-r1` transaction installs the Revision 13 one-shot native platform-login client from clean pushed revision `6ed40e8`. It retains the complete prior 32-file client tree and leaves the dedicated installation plus protected result unchanged. This latest candidate remains `ConfirmedPathHashOnly` until a separately approved live feasibility run reports its loaded hash.

## 2. Authoritative runs

| Purpose | Run ID | Outcome |
|---|---|---|
| Clean-revision contract inventory | `m2b2-contracts-20260831-01` | `Pass`; 21/21 projects; no product process launched |
| Clean-revision client/dedicated compile | `m2b2-prestage-compile-20260831-01` | `Pass`; installed inventories unchanged |
| Superseded preparation attempt | `m2b2-stage-20260831-01` | Stopped before manifest, backup, lock, or installed-tree mutation; evidence retained |
| Controlled installation with retained pre-images | `m2b2-stage-20260831-02` | `Installed`; installed inventories exactly equal prepared inventories |
| Post-stage environment doctor | `m2b2-poststage-doctor-20260831-01` | `EnvironmentBlocked`; sole blocker `RuntimeVersionCombinationNotYetVerified` |
| Native-login clean published contracts | `m2e-pub-contracts-r1` | `Pass`; 22/22 projects; no product process launched |
| Native-login clean published compile | `m2e-pub-compile-r1` | `Pass`; both builds; installed inventories unchanged; no product process launched |
| Native-login client-only staging | `m2e-client-stage-r1` | `Installed`; exact 32-file pre-image retained; only client DLL/PDB changed |

The run roots are:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-contracts-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-prestage-compile-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-stage-20260831-02
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-poststage-doctor-20260831-01
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2e-pub-contracts-r1
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2e-pub-compile-r1
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2e-client-stage-r1
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

The first separately approved `Feasibility` attempt used the exact hashes in this document and confirmed the dedicated loaded identity. It stopped before UDP visibility and client launch because the runner concatenated the six bootstrap commands and then stalled in unbounded descendant discovery. Exact recovery completed without a forced dedicated stop, and the staging hashes remained intact.

Clean revision `70a40db` completed that rerun and preserved the staging hashes. It verified the dedicated loaded identity, six separate writes, bounded process discovery, unchanged global result, and exact cleanup, but no UDP listener or client appeared because command emission preceded native console readiness. Milestone 2B.2C subsequently implemented and contract-tested bounded native-output capture, exact readiness/command/start readbacks, singular runner results, and exact PID-correlated native-log retention without changing the staged game-side binaries. Clean validation revision `e62f536` preserved the same installation hashes but failed after successful process creation because immediate executable-path acquisition returned null before provisional ownership/capture. Exact manual recovery left no process or port owner. Revision 10 then corrected process ownership, and clean run `m2b2c-client-handoff-live-20260831-01` live-verified exact dedicated cleanup while disproving redirected output as readiness authority.

Milestone 2B.2D then changed dedicated game-side source to add the structured control channel; see [BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md](BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md). Section 9 records its separately approved dedicated-only staging transaction. Any later rerun must still avoid campaign loading, cooperative battle fixtures, campaign result consumption, and L2/L3 claims.

## 9. Milestone 2B.2D dedicated-only staging

Clean published revision `7628c85` passed all 22 contract projects in `m2b2d-prestage-contracts-20260901-01`. Compile-only run `m2b2d-prestage-compile-20260901-01` built both projects, launched no product process, and proved installed client, legacy-client, and dedicated inventories unchanged. Its dedicated output SHA-256 was `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`.

Controlled run `m2b2d-stage-20260901-01` copied the complete installed dedicated tree and overlaid only the client-bin and server-bin `CoopSpectator.dll`. Preparation proved that those were the only two changed relative paths. Apply revalidated local/remote revision, clean worktree, no product process, free ports, exact pre-image and prepared fingerprints, exclusive DLL access, and the dedicated installation mutex before moving the complete old tree to the run-owned backup and the complete prepared tree into the installation path.

| Tree or file | Before | After |
|---|---|---|
| Dedicated tree | 218 files; fingerprint `89B1630961307D90943ACFCA2936D1C4EC4ADC57FB59979111B26A6812B1F404` | 218 files; fingerprint `AE406714F10354A1B8C437EC4E4D8B2E90DD55B7B8A7CB968822C4E9BC9A465D` |

## 10. Revision 13 corrected-client staging

Published revision `729fd2c325019ca026787b4ffcccf91beeced755` was clean and equal to its upstream before build or installation. Fresh compile-only run `m2d-r13-pub-b1` built both projects with zero errors and proved all installed inventories unchanged. The dedicated output remained byte-identical at SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`.

The fresh client DLL SHA-256 was `1C500501CE25D4A520782F61B338F9D8D0A4C591A4748E80991A521B63379250`, not the earlier pre-publication build hash `2437D2386E306C12154553D641B477E123A247938C9C2F944F870BB36F5D6887`. Both files had the same length. Byte comparison found only 173 differing bytes, including build/debug metadata and the embedded absolute PDB path. ILSpy produced 620/620 identical decompiled C# files; only its generated project `HintPath` reflected the different compile root. The fresh published-revision artifact, rather than the earlier equivalent-code artifact, was therefore selected for staging. Its PDB SHA-256 is `8A63F4F566C8AC650BE923269F885CCD0B9B659661FDC132FEF680967AEF10F0`.

Preparation run `m2d-client-stage-r1` copied and overlaid the exact client tree but stopped before locking or installation because an empty `Compare-Object` result was read through `.Count` without array wrapping. Its failure record proves the installed client remained `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`, no backup move occurred, and no product process existed. Preparation run `m2d-client-stage-r2` corrected that one-time orchestration expression. Its first apply preflight also stopped before the mutex or any move because a helper used `return[ordered]` without the required token separator; `artifacts/failure/apply-preflight-01.json` retains that non-mutating error.

The corrected apply revalidated local/upstream revision, clean worktree, no product process, free ports, Steam presence, source hashes, exact client/dedicated inventories, the protected result, exclusive DLL/PDB access, and the global client-installation mutex. It then moved the complete 32-file installed client tree into the run-owned backup and moved the verified prepared tree into the approved installation path. Exactly these files changed:

- `bin/Win64_Shipping_Client/CoopSpectator.dll`;
- `bin/Win64_Shipping_Client/CoopSpectator.pdb`.

Postflight evidence is:

| Fact | Observed result |
|---|---|
| Installed client tree | 32 files; fingerprint `14DF5E30B7838D72E0F69F70A31C7BE6CF8FF13767A33F48A1C69A539D6A0AF0` |
| Retained client pre-image | 32 files; fingerprint `A53B10EA8413A0AFCDAE90035115F72F6ED6DAD71B5D5A953563A0CDC51597F5` |
| Installed client DLL | SHA-256 `1C500501CE25D4A520782F61B338F9D8D0A4C591A4748E80991A521B63379250` |
| Retained prior client DLL | SHA-256 `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928` |
| Installed client PDB | SHA-256 `8A63F4F566C8AC650BE923269F885CCD0B9B659661FDC132FEF680967AEF10F0` |
| Dedicated installation | 218 files; unchanged; both DLLs `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` |
| Protected result | SHA-256, length, and UTC write ticks unchanged |
| Product/port state | No product process; ports `7210` and `7777` unowned |

The retained exact client pre-image is:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2d-client-stage-r2\backup\client\CoopSpectator
```

Launcher validation `m2d-client-stage-validate-r1` accepted the installed hash, version `0.3.2`, game path, and live Steam session without launching a product process. Clean live run `m2d-live-r3-01` subsequently confirmed the installed client and dedicated hashes from inside their exact processes. The staging gate is therefore closed; the remaining blocker is native multiplayer authentication, not binary identity.
| Dedicated client-bin DLL | `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626` | `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` |
| Dedicated server-bin DLL | `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626` | `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` |
| Client tree | 32 files; fingerprint `19369B25E232718F1291A54853E0FA090B0AE53AC300E731BDD04123AC5BAAEE` | Unchanged |
| Client DLL | `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928` | Unchanged |
| Protected result | SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3` | Length, hash, and UTC write ticks unchanged |

The exact retained pre-image is:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2d-stage-20260901-01\backup\dedicated\CoopSpectatorDedicated
```

The first postflight check produced a false protected-result timestamp mismatch because a deserialized `DateTime` was converted through culture-dependent string formatting. It performed no mutation and is retained at `artifacts/failure/postflight-comparison-01.json`. Exact UTC-tick comparison then passed together with all tree, hash, process, and port checks.

Post-stage doctor run `m2b2d-poststage-doctor-20260901-01` reported `EnvironmentBlocked` with `InstalledDedicatedHashDiffersFromRepositoryOutput` and `RuntimeVersionCombinationNotYetVerified`. The former refers only to the intentionally untouched ignored repository output from the prior build; the installed tree exactly matches the clean run-owned compile artifact. No game process was launched. The new dedicated identity remains `ConfirmedPathHashOnly` until a named live run reports the loaded module hash.

## 11. Revision 13 native platform-login client staging

Clean pushed revision `6ed40e883cd9909f5af8f10a8e9df19376b6ed52` equaled its upstream with a clean worktree before verification or installation. Canonical run `m2e-pub-contracts-r1` passed all 22/22 contract projects. Compile-only run `m2e-pub-compile-r1` then built both product projects, launched no product process, and proved all installed inventories unchanged. Its exact outputs were:

- client DLL SHA-256 `8089FC9FF0DB230AC358D4B5DDE611B73FEEBA16E6B7BFF3EB3126866E7C1FBB`, length `4669440`, product version `0.3.2`;
- client PDB SHA-256 `90CE68CC0C9BF48881C0215C4AB60469EC951A331B95D3F646DD075D293A10F5`, length `1374892`;
- dedicated DLL SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`, unchanged.

Run `m2e-client-stage-r1` copied the complete installed client tree into a run-owned prepared tree and overlaid only the clean-build DLL/PDB. Preparation recorded exact client, dedicated, and protected-result pre-images and proved that exactly these relative paths changed:

- `bin/Win64_Shipping_Client/CoopSpectator.dll`;
- `bin/Win64_Shipping_Client/CoopSpectator.pdb`.

Apply revalidated the clean local/upstream revision, exact prepared and installed fingerprints, dedicated fingerprint, protected-result hash/length/UTC ticks, no Bannerlord/crash-reporter process, free ports `7210`/`7777`, exclusive DLL/PDB access, and the global client-installation mutex. It moved the complete installed tree to the run-owned backup and moved the complete prepared tree into the approved installation path. No rollback was required.

| Fact | Observed result |
|---|---|
| Installed client tree | 32 files; fingerprint `2C5308A0958994AE96BA051D4D5AD5D48C6D8A540C4D88EB56FFF920B384735C` |
| Retained exact pre-image | 32 files; fingerprint `A33B5A27232917F08A91D1993ACAD4593807DFE3B4A51145072EDCE860F63951` |
| Installed client DLL | SHA-256 `8089FC9FF0DB230AC358D4B5DDE611B73FEEBA16E6B7BFF3EB3126866E7C1FBB` |
| Installed client PDB | SHA-256 `90CE68CC0C9BF48881C0215C4AB60469EC951A331B95D3F646DD075D293A10F5` |
| Retained prior client DLL | SHA-256 `1C500501CE25D4A520782F61B338F9D8D0A4C591A4748E80991A521B63379250` |
| Dedicated installation | 218 files; fingerprint `CB85BBFF97FCF03F476E1D252B8A09FEFD7E7248BC36F6279A8D05572C9CD42E`; both DLLs remain `BD328...02A78` |
| Protected result | SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`, length `217264`, and UTC ticks unchanged |
| Product/port state | No product process; ports `7210` and `7777` unowned |
| Steam/launcher validation | Steam was not running; launcher `ValidateOnly` was deferred rather than starting Steam or misclassifying the path/hash proof |

The retained pre-image is:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2e-client-stage-r1\backup\client\CoopSpectator
```

This closes only the client path/hash staging gate for the native-login implementation. It provides no loaded-binary, authentication, lobby, connection, campaign, mission, battle, L2, or L3 evidence. A live run requires Steam to be started explicitly and remains a separately approved operation.
