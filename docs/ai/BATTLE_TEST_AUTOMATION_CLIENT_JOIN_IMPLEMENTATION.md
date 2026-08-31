# Battle Test Automation Client Join Implementation Report

Status: **Source- and contract-implemented; exact `0.3.2` installed; Bannerlord runtime verification blocked**

Implementation date: **2026-08-31**

Original repository base revision: **`3c513084ebbe9c99daa0b65849fab7b39b913ee1`** (`3c51308`)

Committed implementation revision: **`f91eeff9b710f68fc7bf4b506ec39c2d1c4474bc`** (`f91eeff`)

Current specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 7. The original client-join slice was implemented before the Revision 7 runtime-safety foundation.

## 1. Outcome

The Milestone 1 normal-lobby control blocker now has a narrow source implementation and isolated contract coverage. A dedicated launcher can validate Steam and the exact installed client module, create a fresh run-scoped join request, launch the real multiplayer executable, and let the module discover and request the associated local server through TaleWorlds' normal custom-game lobby APIs.

This report does not promote either blocked Milestone 1 runtime capability. No client, dedicated server, campaign, mission, or battle was launched during implementation validation. A later controlled operation installed exact `0.3.2` binaries from committed revision `f91eeff` and proved their on-disk paths/hashes, but the code has still not demonstrated a real loaded-role acknowledgement, lobby handoff, or connection. See [BATTLE_TEST_AUTOMATION_M2B_STAGING.md](BATTLE_TEST_AUTOMATION_M2B_STAGING.md).

## 2. Implemented boundary

### Launcher

- `run_battle_test_client.bat` is retained as the historical standalone wrapper. Current source permits that path only for validation; a live client must inherit exact server ownership through the aggregate `Feasibility` runner.
- `scripts/Start-CoopBattleTestClient.ps1` validates the `RunId`, server identity, Steam process, Bannerlord executable, module descriptor, module DLL, bundled Harmony DLL, and the expected installed module SHA-256 before process creation.
- `-ValidateOnly` performs those checks without creating a run directory or starting Bannerlord.
- A live call without `-UseExistingRunContract` fails before creating the run root because a standalone client cannot prove exact dedicated-process ownership.
- A server password is accepted only from `COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD` in the launcher environment. It is never accepted as a command-line argument or written to the request, status, launch artifact, or logs.
- A real launch creates a fresh request, launch artifact, and exclusive launcher lock below `%TEMP%\CoopSpectator\Automation\<RunId>`. Existing request or status files cause rejection.
- The child receives the test flag, `RunId`, exact run root, un-hashed run token, and optional password through its environment. The persisted request contains only the token hash.

### Module control path

- `Infrastructure/ExperimentalFeatures.cs` keeps the path disabled unless `COOPSPECTATOR_TEST_AUTOMATION=1` is explicitly set.
- `Infrastructure/Automation/CoopAutomationJoinContract.cs` validates the schema, run identity, sequence, command ID, bounded lifetime, token hash, loaded module hash, and exact server selectors.
- Join request schema 2 uses protocol 1.0 and exact `Runner/runner-01 -> MultiplayerClient/multiplayer-client-01` source/target identity. It shares the general run correlation constants added by Milestone 2A.
- `Infrastructure/Automation/CoopAutomationJoinBridge.cs` restricts the initial profile to the expected temporary run root, hashes the actually loaded assembly, reads the request, and publishes strictly atomic status.
- `Multiplayer/Automation/CoopLobbyAutomationDriver.cs` reflects only the current TaleWorlds lobby surface needed for `GetCustomGameServerList()` and `RequestJoinCustomGame(...)`.
- `Multiplayer/Automation/CoopLobbyAutomationController.cs` runs from the existing application tick, selects exactly one matching server, verifies local-host association, requests the native join, and records state changes. Current automation validates a run-scoped token-bound owned-host record, exact live process identity, and active UDP port; it neither trusts nor mutates the production persisted host marker.
- `Patches/LobbyCustomGameLocalJoinPatch.cs` reports the existing normal lobby-to-network handoff to the controller. Existing loopback rewrite behavior remains the only address rewrite.
- `Commands/CoopAutomationConsoleCommands.cs` exposes `coop.automation_join status`, `coop.automation_join start <RunId>`, and safe pre-join cancellation. The launcher request itself arms the path; the command is an explicit observation/control surface, not a second connection implementation.

### State and deadline semantics

The status path is `state/client-join.status.json`. States distinguish `ModuleReady`, `WaitingForLobby`, `RequestingServerList`, `WaitingForServer`, `JoinRequested`, `JoinAccepted`, `NetworkHandoff`, `Connected`, `Failed`, and `Cancelled`.

Request expiry prevents a new native join from starting. Once TaleWorlds owns the join task, the module does not claim cancellation or terminal expiry because the operation may still complete. A future runner must impose its own bounded connection timeout and clean up only the exact process it owns.

The launcher and client module do not issue `start_game`, fabricate battle readiness, or use UI automation. Revision 7 permits only the external runner, after loaded-role and result-suppression gates, to issue the minimum native vanilla `TeamDeathmatch` bootstrap required for the server to bind and appear in the normal lobby.

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

Contract coverage includes valid and invalid `RunId` values, run-token mismatch, loaded-module hash mismatch, expired and excessive-lifetime requests, exact and optional server filters, no match, ambiguous match, terminal-state classification, strict atomic status replacement, and compilation of the bridge/driver/controller/console-command/lobby-patch source graph against narrow runtime stubs.

## 4. Requirement completion audit

| Requirement | Status | Implementation and validation evidence | Evidence class | Affected roles/scenarios | Documentation impact | Residual risk |
|---|---|---|---|---|---|---|
| SAF-004 default-off profile | `Partially Satisfied` | `ExperimentalFeatures.EnableTestAutomation`; bridge requires the complete run profile; isolated contract compilation passed | Source, contract test | Multiplayer client; battle-type independent | Architecture, build guide, risks, runtime flow updated | Full L2–L5 profile and runtime proof do not exist |
| RUN-001/003/004/005 narrow client protocol | `Satisfied at the non-runtime contract layer; runtime use unverified` | Fixed `RunId` root; protocol 1.0 source/target role identity; sequence/command/token/hash identity; strict atomic status; launcher refuses reused request/status; general manifest/lease/recovery/file-fault contracts pass | Source, contract test | Multiplayer client; battle-type independent | Specification, code map, build guide, M2A report updated | Live role registration, acknowledgement, connection, timeout, and cleanup remain future runtime work |
| BLD-003/006 client identity slice | `Partially Satisfied` | Launcher checks installed hash; module hashes its loaded assembly and rejects mismatch; exact committed `0.3.2` client/dedicated paths match the compile output; the first live feasibility attempt confirmed the dedicated loaded hash | Source, contract test, on-disk staging evidence, dedicated runtime evidence | Multiplayer client | M1 report, build guide, staging report, and feasibility report updated | No real role-reported loaded client `0.3.2` identity proof |
| PROC-001 client launch slice | `Partially Satisfied` | Launch artifact records entry PID/path/start time; validation-only path tested | Source, contract test | Multiplayer client | Build guide and code map updated | No live launch in this step, descendant ownership, exact cleanup, or crash recovery proof |
| CLI-002 initial machine prerequisite | `Partially Satisfied` | Launcher requires Steam in the current interactive session and records non-secret process IDs; validation passed | Source, environment validation | Multiplayer client | Build guide and M1 report updated | Portability, anti-cheat, modal, and other-machine proof remain open |
| CLI-006 early feasibility gate | `Partially Satisfied` | A supported source control path now exists; M1 launch evidence and new contract evidence are separated | Source, contract test | Multiplayer client and local dedicated server | Audit and feasibility report updated | No connection, handoff, or exact cross-role runtime correlation yet |
| CLI-008 run-scoped native-lobby intent | `Partially Satisfied` | (a) default-off complete profile; (b) fresh RunId/token/hash-bound request; (c) exact server filters; (d) run-scoped owned process plus UDP association gate; (e) native list/join APIs; (f) secret excluded from CLI/artifacts; (g) atomic state acknowledgements; (h) launcher/client do not issue `start_game` or use UI automation; source compiled and contracts passed; (i) clean `70a40db` live rerun verified dedicated identity, six writes, and cleanup; (j) Milestone 2B.2C added bounded native-output capture, exact readiness/command/start markers, singular structured results, and exact PID-correlated log retention; (k) clean `e62f536` validation exposed missing provisional ownership when immediate process-path acquisition returned null | Source, contract test, dedicated runtime evidence | Multiplayer client and associated local dedicated server; battle-type independent | Specification Revision 10 and affected living documents updated | The Milestone 2B.2C output mechanics are not yet live-proven; post-start identity/cleanup must be corrected before another run; UDP visibility, client launch, handoff, and connection remain unverified; the external runner's minimum vanilla bootstrap is not L2/L3 battle evidence |
| TST-004 safe default | `Satisfied` for this slice | No complete automation environment means the controller returns without work; feature flag is independent of verbose diagnostics | Source, contract compilation | Production multiplayer client | Architecture and risks updated | Main project was not built or runtime-tested in this step |

## 5. Remaining gates

The first clean Milestone 2B.2C connectivity-only validation exposed another shared runner prerequisite. The remaining gates are:

1. implement and contract-test provisional ownership immediately after successful process creation, bounded exact-path acquisition, correct `RunnerInternalError` classification, and cleanup when enrichment fails;
2. pass focused cross-PowerShell contracts, the full canonical inventory, compile-only verification, review, commit, and push without module restaging;
3. invoke `Feasibility` from that clean revision with explicit expected hashes and require both roles to report those loaded identities;
4. require exact native readiness, every option/start-game/scene readback, the run-owned UDP endpoint, exact lobby selection, network handoff, connected session, unchanged global result state, exact PID-log inventory, and exact cleanup evidence;
5. retain the run root for inspection and make no campaign, L2, or L3 claim.

Only after that bounded runtime gate passes may the project proceed to campaign encounter capture or full battle execution. The vanilla bootstrap used by `Feasibility` is solely a native connectivity prerequisite and does not satisfy a battle milestone.
