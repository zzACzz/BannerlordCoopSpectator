# Battle Test Automation Client Join Implementation Report

Status: **Real client launch, loaded hash, schema-v4 handoff, and automatic cleanup confirmed; exact NetworkMain assembly resolver corrected in source/contracts; installation and connection revalidation pending**

Implementation date: **2026-08-31**

Latest runner-correction date: **2026-09-01**

Original repository base revision: **`3c513084ebbe9c99daa0b65849fab7b39b913ee1`** (`3c51308`)

Committed implementation revision: **`f91eeff9b710f68fc7bf4b506ec39c2d1c4474bc`** (`f91eeff`)

Published post-start ownership revision: **`711f2cac20ca05d3e81233aa6acfa60816dcce99`** (`711f2ca`)

Current specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 12. The original client-join slice was implemented before the Revision 7 runtime-safety foundation; the post-start ownership handoff was hardened against the Revision 10 process contract. Revision 11 records that the dedicated starter's redirected standard handles are not a sufficient live control/evidence channel. Revision 12 incorporates the first real client launch and corrects the aggregate handoff's JSON-date and retained-ownership defects. The subsequent clean rerun exposed a source implementation defect rather than a specification change: the reflection adapter searched the correct `TaleWorlds.MountAndBlade.NetworkMain` type name in the wrong assembly.

## 1. Outcome

The Milestone 1 normal-lobby control blocker now has a narrow source implementation and isolated contract coverage. A dedicated launcher can validate Steam and the exact installed client module, create a fresh run-scoped join request, launch the real multiplayer executable, and let the module discover and request the associated local server through TaleWorlds' normal custom-game lobby APIs.

Implementation validation itself launched no product process. A later controlled operation installed exact `0.3.2` binaries from committed revision `f91eeff` and proved their on-disk paths/hashes. Clean run `m2b2d-live-feasibility-20260901-01` subsequently launched the real client from published revision `2104295`; the client published the selected loaded hash and reached `WaitingForLobby`, while the aggregate runner exposed the corrected UTC/ownership work. Clean Revision 12 rerun `m2d-live-r2-01` then proved schema-v4 identity import, retained automatic cleanup authority, the complete dedicated bootstrap, UDP ownership, and exact client loaded identity. It stopped at `RequestExpired` because `CoopLobbyAutomationDriver` searched assembly `TaleWorlds.MountAndBlade.Multiplayer` for `TaleWorlds.MountAndBlade.NetworkMain`; ILSpy proved that the installed `1.4.8` type is defined in `TaleWorlds.MountAndBlade.dll`. The source resolver now uses that exact assembly and distinguishes missing assembly, type, property, and not-yet-created client states. Lobby selection, handoff, and connection remain unproved until the corrected client is installed and rerun. No campaign, cooperative mission, or battle was launched. See [BATTLE_TEST_AUTOMATION_M2B_STAGING.md](BATTLE_TEST_AUTOMATION_M2B_STAGING.md), [BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md), and [BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md](BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md).

## 2. Implemented boundary

### Launcher

- `run_battle_test_client.bat` is retained as the historical standalone wrapper. Current source permits that path only for validation; a live client must inherit exact server ownership through the aggregate `Feasibility` runner.
- `scripts/Start-CoopBattleTestClient.ps1` validates the `RunId`, server identity, Steam process, Bannerlord executable, module descriptor, module DLL, bundled Harmony DLL, and the expected installed module SHA-256 before process creation.
- `-ValidateOnly` performs those checks without creating a run directory or starting Bannerlord.
- A live call without `-UseExistingRunContract` fails before creating the run root because a standalone client cannot prove exact dedicated-process ownership.
- A server password is accepted only from `COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD` in the launcher environment. It is never accepted as a command-line argument or written to the request, status, launch artifact, or logs.
- A real launch creates a fresh request, provisional launch artifact, verified final launch artifact, and exclusive launcher lock below `%TEMP%\CoopSpectator\Automation\<RunId>`. Existing request or status files cause rejection.
- Immediately after process creation, the launcher binds a provisional identity to the exact PID, requested executable path, aggregate-runner parent PID, launch window, and launch-operation ID. It uses bounded `Process`/`Win32_Process` observation before publishing schema-v4 `client-launch.json` with the complete verified identity, including executable hash and the original launch evidence.
- The aggregate validates and records that complete identity before fallible live revalidation. JSON UTC fields are normalized directly from either strings or `System.DateTime` values; no culture-dependent intermediate string is permitted.
- Any exception after process creation but before final artifact publication triggers PID-reuse-resistant exact cleanup and propagates `RunnerInternalError`. The final atomic artifact is the handoff boundary after which the aggregate runner owns cleanup.
- The child receives the test flag, `RunId`, exact run root, un-hashed run token, and optional password through its environment. The persisted request contains only the token hash.

### Module control path

- `Infrastructure/ExperimentalFeatures.cs` keeps the path disabled unless `COOPSPECTATOR_TEST_AUTOMATION=1` is explicitly set.
- `Infrastructure/Automation/CoopAutomationJoinContract.cs` validates the schema, run identity, sequence, command ID, bounded lifetime, token hash, loaded module hash, and exact server selectors.
- Join request schema 2 uses protocol 1.0 and exact `Runner/runner-01 -> MultiplayerClient/multiplayer-client-01` source/target identity. It shares the general run correlation constants added by Milestone 2A.
- `Infrastructure/Automation/CoopAutomationJoinBridge.cs` restricts the initial profile to the expected temporary run root, hashes the actually loaded assembly, reads the request, and publishes strictly atomic status.
- `Multiplayer/Automation/CoopLobbyAutomationDriver.cs` resolves `TaleWorlds.MountAndBlade.NetworkMain` from its exact defining assembly, `TaleWorlds.MountAndBlade`, and reflects only the current TaleWorlds lobby surface needed for `GetCustomGameServerList()` and `RequestJoinCustomGame(...)`. Resolution fails distinctly for a missing assembly, type, or `GameClient` property instead of misclassifying each as ordinary lobby startup latency.
- `Multiplayer/Automation/CoopLobbyAutomationController.cs` runs from the existing application tick, selects exactly one matching server, verifies local-host association, requests the native join, and records state changes. Current automation validates a run-scoped token-bound owned-host record, exact live process identity, and active UDP port; it neither trusts nor mutates the production persisted host marker.
- `Patches/LobbyCustomGameLocalJoinPatch.cs` reports the existing normal lobby-to-network handoff to the controller. Existing loopback rewrite behavior remains the only address rewrite.
- `Commands/CoopAutomationConsoleCommands.cs` exposes `coop.automation_join status`, `coop.automation_join start <RunId>`, and safe pre-join cancellation. The launcher request itself arms the path; the command is an explicit observation/control surface, not a second connection implementation.

### State and deadline semantics

The status path is `state/client-join.status.json`. States distinguish `ModuleReady`, `WaitingForLobby`, `RequestingServerList`, `WaitingForServer`, `JoinRequested`, `JoinAccepted`, `NetworkHandoff`, `Connected`, `Failed`, and `Cancelled`.

Request expiry prevents a new native join from starting. Once TaleWorlds owns the join task, the module does not claim cancellation or terminal expiry because the operation may still complete. A future runner must impose its own bounded connection timeout and clean up only the exact process it owns.

The launcher and client module do not issue `start_game`, fabricate battle readiness, or use UI automation. Revision 11 keeps the external runner as the sole bootstrap authority after loaded-role and result-suppression gates, but requires a separate run-scoped dedicated command-intent and acknowledgement channel before the minimum vanilla `TeamDeathmatch` bootstrap may be trusted.

Milestone 2A subsequently supplied the general run manifest, nonce fingerprint, role-instance, lease, event, outcome, assertion, file-fault, and compile-only foundation documented in [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md). This closes the general non-runtime protocol/build prerequisites but does not change the unverified live-join status of this client slice.

## 3. Validation evidence

The main game projects were intentionally not built because their current targets deploy into installed module trees. Validation used the isolated `net8.0` contract project only.

| Check | Command or evidence | Result |
|---|---|---|
| Runtime automation source compilation plus contract behavior | `dotnet run --project .\Tests\CoopAutomationJoin.ContractTests\CoopAutomationJoin.ContractTests.csproj --configuration Release` | Passed: `Coop automation join contract tests passed.` |
| PowerShell 7 syntax | `pwsh.exe` parsed `scripts/Start-CoopBattleTestClient.ps1` through `ScriptBlock.Create` | Passed |
| Windows PowerShell 5.1 syntax | `powershell.exe` parsed the same script through `ScriptBlock.Create` | Passed |
| Wrong installed hash rejection | `Start-CoopBattleTestClient.ps1 ... -ExpectedClientModuleSha256 A...A -ValidateOnly` | Rejected with exit code `1`; no run root was created |
| Historical installed-profile validation before current staging | `Start-CoopBattleTestClient.ps1 -RunId M2B-installed-validate-only -ServerName AC_COOP -ExpectedClientModuleSha256 9B271E4E0CFA3AD0FF2DB4B3ACC5A69AE6405E833D52ECB4E1A4C0FDCA8C1B31 -ValidateOnly` | Passed against the then-installed `0.3.1`; Steam found; no game launch |
| BAT argument guard | `cmd.exe /d /c run_battle_test_client.bat` | Usage shown; exit code `2`; no game launch |
| Patch whitespace/error check | `git diff --check` | Passed |
| Full canonical contract inventory after protocol 1.0 integration | `Invoke-CoopTest.ps1 -Command Contracts -RunId m2a-contracts-20260831-07 -All` | Passed 20/20; no product process launched |
| Main client/dedicated compile-only proof | `Invoke-CoopTest.ps1 -Command CompileOnly -RunId m2a-compile-only-20260831-03` | Passed; installed module inventories unchanged; no product process launched |
| Clean committed-revision compile and controlled install | `m2b-prestage-compile-20260831-01`, `m2b-install-20260831-01` | Exact `0.3.2` client/dedicated hashes installed; full `0.3.1` pre-images retained; no product process launched |
| Post-install environment doctor | `m2b-postinstall-doctor-20260831-01` | Installed/repository hashes match; only `RuntimeVersionCombinationNotYetVerified` remains |
| Milestone 2B.1 final contracts and compile-only | `m2b1-final-contracts-20260831-03`, `m2b1-final-compile-only-20260831-02` | Passed 21/21 and both main projects; installed inventories unchanged; no product process launched |
| Post-start handoff focused contracts | `Tests/CoopAutomationRunner.ContractTests` under Windows PowerShell 5.1 and PowerShell 7 | Passed source-order, exact parent/path/start, PID-reuse rejection, synthetic post-start failure, and provisional cleanup coverage |
| Current installed-profile validation | `m2b2c-client-handoff-validate-20260831-01` with `-ValidateOnly` | Exact installed client `0.3.2` hash `B576...7928` validated; Steam found; no run root or product process created |
| Client-handoff full canonical inventory | `m2b2c-client-handoff-contracts-20260831-01` | Passed 22/22; no product process launched |
| Client-handoff compile-only proof | `m2b2c-client-handoff-compile-20260831-01` | Both builds passed; installed inventories unchanged; no product process launched |
| Published ownership revision | `711f2cac20ca05d3e81233aa6acfa60816dcce99` | Client-launcher handoff correction committed and pushed before the clean live attempt |
| Clean live dedicated gate | `m2b2c-client-handoff-live-20260831-01` | Dedicated ownership/cleanup passed; redirected readiness timed out before client launch; installed hashes and protected result unchanged |
| First real client launch | `m2b2d-live-feasibility-20260901-01` | Exact dedicated control, seven acknowledgements, UDP ownership, real client creation, selected client loaded hash, and `WaitingForLobby` confirmed; aggregate UTC import failed before connection |
| Revision 12 focused handoff contracts | `Tests/CoopAutomationRunner.ContractTests` | Passed in Windows PowerShell `5.1.26100.9168` and PowerShell `7.6.4`, including JSON `DateTime`, shifted-time rejection, parent/launch-operation retention, and optional-watchdog evidence |
| Revision 12 canonical inventory | `m2d-c-01` | Passed all 22/22 contract projects; no product process launched |
| Clean Revision 12 live rerun | `m2d-live-r2-01` | Dedicated bootstrap, seven acknowledgements, UDP ownership, schema-v4 client identity, exact loaded hashes, unchanged protected result, and graceful automatic cleanup passed; client join ended at `RequestExpired` because the driver resolved `NetworkMain` from the wrong assembly |
| Exact NetworkMain resolver contracts | `Tests/CoopAutomationJoin.ContractTests`, `m2d-r13-c1` | ILSpy located the installed type in `TaleWorlds.MountAndBlade.dll`; the focused test rejected the historical multiplayer-assembly mismatch, and the full canonical inventory passed 22/22 |
| Exact NetworkMain resolver compile-only | `%LOCALAPPDATA%\Temp\CoopSpectator\CompileOnly\networkmain-resolver-20260901` | Client and dedicated builds passed with zero errors; candidate client SHA-256 is `2437D2386E306C12154553D641B477E123A247938C9C2F944F870BB36F5D6887`; dedicated output and both installed module hashes remained unchanged |

Contract coverage includes valid and invalid `RunId` values, run-token mismatch, loaded-module hash mismatch, expired and excessive-lifetime requests, exact and optional server filters, no match, ambiguous match, terminal-state classification, strict atomic status replacement, exact defining-assembly resolution for `NetworkMain`, rejection of the historical wrong-assembly lookup, and compilation of the bridge/driver/controller/console-command/lobby-patch source graph against narrow runtime stubs.

## 4. Requirement completion audit

| Requirement | Status | Implementation and validation evidence | Evidence class | Affected roles/scenarios | Documentation impact | Residual risk |
|---|---|---|---|---|---|---|
| SAF-004 default-off profile | `Partially Satisfied` | `ExperimentalFeatures.EnableTestAutomation`; bridge requires the complete run profile; isolated contract compilation passed | Source, contract test | Multiplayer client; battle-type independent | Architecture, build guide, risks, runtime flow updated | Full L2–L5 profile and runtime proof do not exist |
| RUN-001/003/004/005 narrow client protocol | `Satisfied at the non-runtime contract layer; partially runtime exercised` | Fixed `RunId` root; protocol 1.0 source/target role identity; sequence/command/token/hash identity; strict atomic status; launcher refuses reused request/status; general manifest/lease/recovery/file-fault contracts pass; `m2d-live-r2-01` confirmed live role registration, acknowledgement, timeout, and exact cleanup | Source, contract test, runtime evidence | Multiplayer client; battle-type independent | Specification, code map, build guide, M2A report updated | Native server-list request, lobby handoff, and connection remain unverified |
| BLD-003/006 client identity slice | `Partially Satisfied` | Launcher checks installed hash; module hashes its loaded assembly and rejects mismatch; `m2b2d-live-feasibility-20260901-01` and `m2d-live-r2-01` confirmed both selected loaded module hashes; compile-only candidate client hash `2437...6887` is not installed yet | Source, contract test, on-disk staging evidence, dedicated and client runtime evidence | Multiplayer client | M1 report, build guide, staging report, and feasibility report updated | Corrected-client staging, connected-session proof, and a clean corrected hash-chain pass remain open |
| PROC-001 client launch slice | `Partially runtime satisfied` | Immediate provisional PID/path/parent/window identity; bounded exact observation; atomic schema-v4 complete-identity handoff; exact cleanup on post-start failure; cross-PowerShell JSON-date contracts; `m2d-live-r2-01` confirmed real launch, loaded hash, aggregate identity acceptance, and graceful automatic cleanup | Source, contract test, runtime evidence | Multiplayer client | Build guide, code map, correction report, and this report updated | Lobby selection, network handoff, and connection still require a staged corrected-client rerun |
| CLI-002 initial machine prerequisite | `Partially Satisfied` | Launcher requires Steam in the current interactive session and records non-secret process IDs; validation passed | Source, environment validation | Multiplayer client | Build guide and M1 report updated | Portability, anti-cheat, modal, and other-machine proof remain open |
| CLI-006 early feasibility gate | `Partially Satisfied` | A supported source control path now exists; M1 launch evidence and new contract evidence are separated | Source, contract test | Multiplayer client and local dedicated server | Audit and feasibility report updated | No connection, handoff, or exact cross-role runtime correlation yet |
| CLI-008 run-scoped native-lobby intent | `Partially Satisfied` | (a) default-off complete profile; (b) fresh RunId/token/hash-bound request; (c) exact server filters; (d) run-scoped owned process plus UDP association gate; (e) native list/join APIs; (f) secret excluded from CLI/artifacts; (g) atomic state acknowledgements; (h) launcher/client do not issue `start_game` or use UI automation; (i) exact dedicated lifecycle and seven acknowledgements; (j) native `start_game` plus run-owned UDP endpoint; (k) real client launch and loaded hash; (l) Revision 12 schema-v4 exact identity, UTC-safe import, retained cleanup authority, and required/optional native-log inventory; (m) exact `NetworkMain` defining-assembly resolution with contract coverage | Source, contract test, on-disk staging, dedicated/client partial runtime evidence | Multiplayer client and associated local dedicated server; battle-type independent | Specification Revision 12, M2B.2D report, and affected living documents updated | Corrected client installation, lobby selection, network handoff, and connection require a new clean live rerun; the minimum vanilla bootstrap is not L2/L3 battle evidence |
| TST-004 safe default | `Satisfied` for this slice | No complete automation environment means the controller returns without work; feature flag is independent of verbose diagnostics; client and dedicated compile-only builds passed with installed inventories unchanged | Source, contract compilation, compile-only build | Production multiplayer client | Architecture and risks updated | The corrected client binary is not installed or runtime-revalidated yet |

## 5. Remaining gates

The dedicated readiness/request/acknowledgement boundary, native bootstrap, UDP ownership, real client process creation, schema-v4 aggregate handoff, exact client cleanup, and client loaded hash are runtime-confirmed. The post-Revision 12 source correction fixes the exact assembly resolver exposed by `m2d-live-r2-01`. The remaining gates are:

1. review and publish the exact `NetworkMain` resolver correction;
2. stage only the resulting exact client module while preserving the current client pre-image; the dedicated output does not change;
3. invoke `Feasibility` from the clean published correction with explicit installed client/dedicated hashes;
4. require exact lobby state, server-list selection, join request/result, network handoff, connected session, unchanged global result state, PID-log inventory, and exact automatic cleanup evidence;
5. retain the run root for inspection and make no campaign, L2, or L3 claim.

Only after that bounded runtime gate passes may the project proceed to campaign encounter capture or full battle execution. The vanilla bootstrap used by `Feasibility` is solely a native connectivity prerequisite and does not satisfy a battle milestone.
