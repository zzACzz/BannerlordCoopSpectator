# Milestone 4A — Field dedicated spawn smoke: source and contracts

Date: **2026-09-09** (Europe/Kyiv; validation IDs retain the approved 20260908 names).
Status: **Milestone 4A source/contracts complete; not runtime verified.**
Approval: **"ок на Milestone 4A source/contracts"**.
Source baseline: branch `codex/v0.1.1-refresh`, HEAD/local upstream `452df7e30d3d8d120488570857134078d6072ecc`, initially clean.
Implementation is an uncommitted delta in `C:\Users\Admin\.codex\worktrees\1b21\BannerlordCoopSpectator3`.

This stage implements a zero-client L2 driver and rejection contracts. It does not establish a live L2 pass or complete Milestone 4. No campaign, multiplayer client, dedicated server, installed-module staging, Git staging, commit, push, or branch operation is performed in 4A.

## 1. Fixed input and authority

Only `FieldDedicatedSpawnSmokeV1` is supported. Admission requires automation enabled, the exact process profile, a valid run/process/token/module envelope, and `ResultPolicy=Suppress`. A nonempty unknown smoke profile fails closed; disabled automation follows production paths.

| Fact | Required value |
|---|---|
| Fixture ID | `field-current-sanitized-v1` |
| Repository directory | `Tests/Fixtures/Automation/field-current` |
| Run-relative directory | `payloads/field-current` |
| Payload | `battle_roster.sanitized.json`, 259,744 bytes |
| Payload SHA-256 | `B47D7AF7FA057C36CA8EF759A6D597C00007158A22E3A556AC57A1299579D49D` |
| Metadata SHA-256 | `06169055E66E4DC0719AF3A8CB5A5E082CAF487DD18B1E4D18317B5C80973950` |
| Independent oracle SHA-256 | `D9F593D17BEA35A8D8717867C6F7AE79BC721C2FA3A9FFD90F474A001FD023DA` |
| Campaign / battle / instance | `fixture-campaign-001` / `fixture-battle-001` / `fixture-battle-instance-001` |
| Scenario / campaign type | `FieldBattle` / `FieldBattle`, not siege |
| Scene / configured mode / shell | `battle_terrain_029` / `CoopBattle` / `MultiplayerBattle` |
| Boundary | `PreBattleHold`, zero connected clients |

The source payload's historical `MultiplayerGameType=Battle` is not rewritten; the configured mode is registered `CoopBattle`.

The three committed fixture files remain byte-identical. The runner validates the allowlist and hashes before fresh copies. The server hashes and deserializes the same admitted bytes and retains immutable JSON for roster reads. Absolute/escaping/alternate-stream paths and existing reparse ancestors are rejected. The runner independently rechecks retained payload, metadata and oracle after server success.

The independent oracle supplies aggregate facts. Per-entry expectations come directly from the hash-pinned raw snapshot, without production normalization. Actual evidence comes from native mission objects and generation-aware mappings. Synthetic observations are contract inputs, never runtime proof.

Expected composition: 47 entries, 74 healthy humans, 2 sides, 3 parties, 4 heroes, and **17 mounted entries containing 21 riders/mounts**. Side populations are 30 and 44. Both teams and attacker/defender orientations must be distinct and internally consistent; no unverified orientation is inferred from sanitized side IDs.

## 2. Ownership and source map

| File / method | Responsibility |
|---|---|
| `Infrastructure/Automation/CoopAutomationSpawnSmokeContract.cs` | Fixed profile, bounded integrity/path admission, raw-fixture expectations, observation/terminal DTOs, once-only start/end claims |
| `Infrastructure/Automation/CoopAutomationSpawnSmokeBridge.cs` | Default-off run binding; admitted roster bytes; run phase directory; mission identity; opening/end/result evidence; protected-result identity; explicit reset |
| `Campaign/BattleRosterFile.cs`: `ReadRoster`, `ReadSnapshot` | Read admitted bytes when smoke is requested. No global fallback before admission. Existing writes and production Documents path remain unchanged. |
| `DedicatedServer/Automation/CoopAutomationDedicatedControlContract.cs` | Preserve command envelope; fixed smoke options/fixture identity; validate terminal observation and native readback |
| `DedicatedServer/Automation/CoopAutomationDedicatedControlBridge.cs` | Admit before native commands; retain seven-step bootstrap; pump observer after start; reflect task-idle readiness; reset on shutdown |
| `DedicatedServer/Automation/CoopAutomationDedicatedSpawnSmokeObserver.cs` | Dedicated main-thread progression, bounded polling, one native scan, one normal early abort, terminal evidence |
| `GameMode/MissionMultiplayerCoopBattleMode.cs`: `StartMultiplayerGame` | Observe/reject resolved scene/shell before mission opening |
| `Mission/CoopMissionBehaviors.cs`: `TryInitializeServerMissionRuntimeState`, `OnMissionResultReady`, `OnEndMission`, `TryWriteBattleResultSnapshot` | Bind actual mission; observe pre-end phase; retain real result-builder/publication evidence |
| `Mission/CoopMissionBehaviors.cs`: `TryCaptureAutomationSpawnSmokeEvidence` | Read native agents/equipment/mounts/teams/formations, origin, cached exact validation, injection marker and generation-aware ledgers once |
| `Mission/CoopMissionBehaviors.cs`: `TryConsumeBattlePhaseRequests` | Reject phase command consumption under smoke |
| `Infrastructure/CoopBattlePhaseBridgeFile.cs` | Reject start-battle writes/consumption under smoke; store phase state below the run root |
| `scripts/CoopAutomationRunner.Core.ps1` | Fixed request/copy/evidence helpers and two-attempt isolation/cleanup contracts |
| `scripts/Invoke-CoopTest.ps1` | Public `DedicatedSpawnSmoke`, two internal child contexts, ownership/leases/locks, zero-client attempts, cleanup and aggregate |
| `DedicatedServer/CoopSpectatorDedicated.csproj` | Explicitly include new shared/dedicated files |
| `Tests/CoopAutomationSpawnSmoke.ContractTests` | Native-free admission/observation/lifecycle/reset/terminal negatives and PowerShell 5.1/7 evidence checks |
| Existing runtime / campaign-result guard tests | Smoke-envelope negatives; nonempty 47-entry result suppression across scenario families |
| `Tests/contract-tests.manifest.json` | Canonical inventory expanded from 23 to 24 projects |

`DedicatedServer/SubModule.cs` needs no edit: its existing application tick pumps the control bridge, and existing shutdown invokes the reset path. Existing fixture and runner suites are reused. No physical spawner, production result writer, battle adapter, deployment target, feature default or installed module is changed.

## 3. Native lifecycle and diagnostic risk

The exact installed 1.4.8 `TaleWorlds.MountAndBlade.ListedServer.dll` was inspected read-only with `ilspycmd`; SHA-256 `C7D27584FCE431B2D3734EB88C8DF52EF3B1BC8C5729F7FCE690CC277DA577E3`, 28,160 bytes. `ServerSideIntermissionManager` establishes:

- `IsPlaying` alone does not prove its asynchronous task is released.
- Private `IsNewTaskAssignable()` checks whether `_currentTask` is null; `OnTick` releases a completed task.
- `StartMission` requires intermission state and awaits `Mission.Current` in `Continuing`.
- `EndMission` enters normal lobby ending behavior and waits for `Mission.Current == null`.

The driver performs existing `start_game`, waits for native task-idle/intermission readiness, and claims `start_mission` **before** dispatch. There is no retry, HTTP fallback, manual mission-loader bypass or spawn repair.

Progression: native bootstrap → `WaitingReady` → `MissionOpening` → `MissionCurrent` → `Materializing` → `PreBattleHold` → `MissionAborting` → `SpawnSmokePassed`. Admission, observation, command, identity, integrity or lifecycle errors are terminal. Busy state waits without consuming a claim. The command envelope is bounded to ten minutes; heartbeat/progress limits also apply.

Retained diagnostic code is enabled only by the approved explicit profile. The observer polls at most every 100 ms and scans agents exactly once after normal `PreBattleHold`. Native reads remain potentially runtime-sensitive. The scan performs no spawning, mapping refresh, equipment repair, phase change, peer-gate bypass, network send or per-tick logging. Exceptions become terminal failure details.

Required controllers: `MissionMultiplayerCoopBattle`, `CoopMissionSpawnLogic`, `CoopMissionNetworkBridge`, `MissionLobbyComponent`, `MissionAgentSpawnLogic`, and existing field support `BannerBearerLogic`.

Validation requires 74 unique active human indices, exact entry multiplicities, origin/generation-ledger agreement, native character/contract identity, hero identity, exact validation/injection, every declared combat slot/modifier/ammunition amount, side/team/formation agreement, 21 distinct reciprocal rider/mount links, horse/harness IDs, completed initial native materialization, 47 materialized result entries, and a clear result guard. Extra active non-human agents and failed invariants prevent success.

## 4. Early abort and result protection

The test stays at the normal zero-client pre-battle boundary. It does not bypass readiness to reach `BattleActive`. Global phase command reads/writes are gated off; phase files are run-owned.

After observation, normal `end_mission` is requested once when the native handler is idle. End callbacks capture the first phase before production code sets `BattleEnded`. The real result builder and `CoopBattleResultBridgeFile.WriteResult` must be attempted, with 47 entries and successful suppression. Disposal without this evidence is not a pass.

The protected global result is hashed/read before launch, at admission, at observation/result publication, after disposal and after process cleanup. `Suppress` prevents publication before the global writer opens its path. Nonempty-result contracts and invalid-policy refusal cover this boundary. A pre-existing global file is preserved, not deleted.

## 5. Two attempts and cleanup

The public command creates `parent-01` and `parent-02`, each with a fresh root, nonce, command ID, server process and evidence. Public parent IDs are limited to 77 characters. Existing child roots are rejected; no automatic deletion/reuse occurs.

Private `SpawnSmokeAttempt`/`ParentRunId` flags require the matching live parent identity, fresh lease, nonce-correlated manifest and attempt intent. Parent loss/cancellation stops the child cooperatively. The parent records child identities, forwards matching cancellation and waits boundedly for cleanup; it never kills a runner while that runner owns product processes. An unresolved child preserves its exact root/identity and blocks attempt two.

Each child acquires existing canonical shared resources, requires clean local/upstream source identity, an explicit installed dedicated hash matching both dedicated module locations, no existing product process, and free required ports. It launches no campaign/client and does not require Steam. Exact provisional ownership precedes enrichment. Dedicated stdout/stderr and PID-correlated native logs are retained. Correlated crash/modal helpers invalidate success; cleanup targets only verified owned identities.

Before child two, the parent requires successful terminal evidence, unchanged global result, no remaining owned process/required-port owner, no fatal helper, and verified runner/shared lock releases. Pair validation rejects reused run IDs, tokens, command IDs and identical process-generation identities.

**Evidence boundary:** two fresh server processes prove cross-run isolation only. They do not prove production static reset across two missions in one process. Pure reset contracts cover the new automation state. Same-process sequential-mission runtime proof remains a gap and is explicitly false in the reports.

## 6. Requirement compliance for approved 4A

`Satisfied` refers to source/contract obligations. Live behavior is separately `Not Verifiable` in this stage.

| Requirement | Implementation / validation | 4A status |
|---|---|---|
| Exact default-off field profile | Fixed admission; wrong profile/type/scene/policy rejects | Satisfied |
| Fixture ID/size/hash/contained path/oracle/identity | Three-file admission; corruption/truncation/path/identity negatives | Satisfied |
| Server-admitted roster; normal Documents behavior | Roster branch, disabled bridge, source inspection | Satisfied |
| Native progression to normal boundary | Observer + existing bootstrap; installed IL and CompileOnly | Satisfied |
| One start request; no retry | Claim-before-dispatch; duplicate/busy contracts | Satisfied |
| Authoritative army/gear/hero/team/mount evidence | Native reader; C#/PowerShell validators and mutation negatives | Satisfied |
| Early abort before active battle | End claim, phase gate, actual end/result/disposal observations | Satisfied |
| No campaign-consumable result | Actual publication decision; nonempty suppression; protected identity | Satisfied |
| Two isolated sequential attempts | Parent/child implementation; pair/token/command/process/cleanup negatives | Satisfied |
| Stale automation flags/phase/command/result state | Fresh roots/processes, envelope rejection, result guard, bridge/lifecycle reset | Satisfied |
| Disabled behavior in other battle types | Shared hook/route inspection and existing scenario/result suites | Satisfied |
| Focused tests, full inventory, non-deploying build | 24/24 aggregate; both final builds; focused rerun after runner-only classification change | Satisfied |
| English living docs and index | This report plus overview/spec/build/flow/risk updates | Satisfied |
| Live L2 materialization/abort proof | No product process permitted in 4A | Not Verifiable — deferred |
| Same-process two-mission static reset | Not established by fresh processes | Not Verifiable — explicit gap |
| Sanitized hero/native compatibility | Strict future check; no rewrite or relaxation | Not Verifiable — deferred |

## 7. Scenario and role coverage

| Scenario family | Source / contract regression | Dedicated live | Campaign/client live |
|---|---|---|---|
| Ordinary field | Reviewed; admitted profile and aggregate suites | Not Run | Not Applicable to zero-client L2 |
| Village | Shared hooks reviewed; smoke rejected; existing contracts | Not Run | Not Run |
| Siege assault / sally-out | Shared hooks reviewed; smoke rejected; existing contracts | Not Run | Not Run |
| Siege ambush / relief (`SiegeOutside`) | Shared hooks reviewed; smoke rejected; existing contracts | Not Run | Not Run |
| Lords hall | Shared hooks reviewed; smoke rejected; result contracts | Not Run | Not Run |
| Day/night hideout | Separate routes/controllers retained; smoke rejected; existing contracts | Not Run | Not Run |
| Blockade / blockade sally-out | Shared boundaries reviewed; smoke rejected; result contracts | Not Run | Not Run |
| Reconnect / sequential missions | Existing authority retained; new run/reset contracts only | Not Run | Not Run |

All listed source/contract checks: **Passed** within the inspected boundaries and the 24-project suite. All listed dedicated/campaign/client runtime checks: **Not Run**, except campaign/client participation in zero-client L2: **Not Applicable**. No disabled-route runtime regression claim is inferred from source guards or compilation.

## 8. Validation evidence

Artifacts remain below `%TEMP%\CoopSpectator\Automation\`:

- `m4a-focused-20260908-01/work`: focused `CoopAutomationSpawnSmoke`, `CoopAutomationRuntime`, `CoopAutomationRunner`, `CoopAutomationFixture` and `CoopBattleResultCampaignGuard` suites. Spawn-smoke tests include 71 C# assertions plus PowerShell evidence/pair/envelope checks on Windows PowerShell 5.1.26100.9168 and PowerShell 7.6.5.
- `m4a-contracts-20260908-01`: 24/24 passed, full inventory selected, zero failures, no product launch.
- `m4a-compile-20260908-01`: initial client/dedicated CompileOnly passed, zero errors, installed inventories unchanged.
- `m4a-compile-20260908-02`: final CompileOnly after terminal-contract tightening passed: client/dedicated exit 0, installed inventories unchanged. Client SHA-256 `AE157792ECCB5E72A2684D9A5B2A63BCC8DFF420D08BA6EDAA35671D05C83F1C`; dedicated SHA-256 `EA3BB683C507F007889EE59BEB8D050F3B1FF31E2A04485CB64A0B4B7A231111`.

Focused commands use `--configuration Release --property:CoopCompileOnly=true --property:CoopCompileOutputRoot=<focused-root>/work`. The campaign-result guard also needs `COOPSPECTATOR_REPOSITORY_ROOT` in its test process when outputs are redirected; the first direct invocation lacked it and was rerun successfully. An initial sandboxed package-configuration read was denied; the approved elevated test invocation succeeded. No package/environment setting was permanently changed.

Aggregate commands:
~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-CoopTest.ps1 -Command Contracts -RunId m4a-contracts-20260908-01 -All
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-CoopTest.ps1 -Command CompileOnly -RunId m4a-compile-20260908-02 -GameRoot 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord' -DedicatedServerRoot 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server'
~~~

`CoopCompileOnly=true` disables the three deployment targets and redirects build/package intermediates below the run root. Before/after installed inventories independently verify preservation. Final logs contain 77 client warnings and 49 dedicated warnings, with zero errors. Success does not mean a warning-free baseline.

## 9. Remaining verification and rollback

A later explicitly approved stage must review/stage exact artifacts and execute the public command with an explicit installed dedicated SHA-256. Sanitized hero/template identifiers may not resolve in native 1.4.8. Such a run must retain the exact failure and stop; it must not rewrite the immutable fixture, bypass materialization, inject substitutes or relax the oracle automatically.

Native mission loading, zero-client `PreBattleHold`, real reciprocal mount observation, normal abort, real 47-entry result construction, disposal, two live clean attempts and runtime regressions remain unverified. No live L2/L3 or complete Milestone 4 claim is made.

Rollback is a targeted reversal of this uncommitted 4A source/doc delta only after explicit approval. No installed-state rollback is needed. The original dirty checkout at `C:\dev\projects\BannerlordCoopSpectator3` and its unrelated changes were not edited.


## 10. Final audit and documentation impact

The aggregate full inventory and final product builds passed. A subsequent runner-only adjustment preserves child cancellation/error classification; the focused spawn-smoke C#/PowerShell suite and both script parsers passed again afterward. Product source was unchanged after the final CompileOnly build.

Read/updated living documents: `README.md`, `BATTLE_TEST_AUTOMATION_SPEC.md` (revision 24), `BATTLE_TEST_AUTOMATION_M3_FIELD_FIXTURE.md`, `ARCHITECTURE.md`, `CODE_MAP.md`, `RUNTIME_FLOWS.md`, `INVARIANTS_AND_RISKS.md`, and `BUILD_TEST_DEBUG.md`. This M4 report is new. Existing M2 feasibility/control reports and historical audit evidence remain unchanged because this stage does not reinterpret their runtime results. The current overview's stale M3-pending/inventory statements and contradictory compile-only guidance were corrected.

Final read-only audit passed: HEAD and branch remain at the stated baseline, no staged changes, 27 changed source/test/document files, no generated outputs in the delta, no CR bytes or LF/CRLF churn, and `git diff --check` clean. All three fixture hashes remain pinned as listed above. No staging, commit, push or branch write was performed.