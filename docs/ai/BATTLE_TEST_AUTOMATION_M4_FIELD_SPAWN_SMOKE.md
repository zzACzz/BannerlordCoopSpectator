# Milestone 4 — Field dedicated spawn smoke: source, contracts, local isolation and live findings

Date: **2026-09-09** (Europe/Kyiv; validation IDs retain the approved 20260908 names).
Status: **The shared failure-evidence process snapshot is isolated and bounded at source/L1. The recovered full dump localized the mission-opening failure to a null `MissionMultiplayerGameModeBaseClient` dependency inside `MissionScoreboardComponent.AfterStart`; published correction `a6fe74f` now enforces that dependency for direct CoopBattle and full TdmClone and passes source/L1 verification. Controlled staging, native lifecycle confirmation and L2 remain open.**
Original 4A approval: **"ок на Milestone 4A source/contracts"**. The separately approved local-only 4B live validation and restoration are recorded in section 12.
Original 4A source baseline: branch `codex/v0.1.1-refresh`, HEAD/local upstream `452df7e30d3d8d120488570857134078d6072ecc`, initially clean.
The original 4A implementation was subsequently published as 7d75742c338f504499ec01dd8e3b2f189a1f7a03. Section 11 records the local-isolation follow-up's pre-publication validation in `C:\Users\Admin\.codex\worktrees\1b21\BannerlordCoopSpectator3`.

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
| `Campaign/BattleRosterFile.cs`: `ReadRoster`, `ReadSnapshot` | Read admitted bytes when smoke is requested. No global fallback before admission. All roster path selection is now local under the field profile; disabled production behavior remains unchanged. |
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

The field test now creates and protects a run-local result sentinel. Admission requires its exact RunId-derived bytes; observation, publication and cleanup require preservation plus the real suppressed result attempt. Personal Documents are not accessed or claimed measured. See section 11 for the replacement of the original 4A global-file checks and the required v2 evidence scope.

## 5. Two attempts and cleanup

The public command creates `parent-01` and `parent-02`, each with a fresh root, nonce, command ID, server process and evidence. Public parent IDs are limited to 77 characters. Existing child roots are rejected; no automatic deletion/reuse occurs.

Private `SpawnSmokeAttempt`/`ParentRunId` flags require the matching live parent identity, fresh lease, nonce-correlated manifest and attempt intent. Parent loss/cancellation stops the child cooperatively. The parent records child identities, forwards matching cancellation and waits boundedly for cleanup; it never kills a runner while that runner owns product processes. An unresolved child preserves its exact root/identity and blocks attempt two.

Each child acquires existing canonical shared resources, requires clean local/upstream source identity, an explicit installed dedicated hash matching both dedicated module locations, no existing product process, and free required ports. It launches no campaign/client and does not require Steam. Exact provisional ownership precedes enrichment. Dedicated stdout/stderr and PID-correlated native logs are retained. Correlated crash/modal helpers invalidate success; cleanup targets only verified owned identities.

Before child two, the parent requires successful terminal evidence, unchanged run-local result sentinel with matching v2 facts, no remaining owned process/required-port owner, no fatal helper, and verified runner/shared lock releases. Pair validation rejects reused run IDs, tokens, command IDs and identical process-generation identities.

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

Rollback of the current local-isolation source/doc delta requires a targeted, separately approved reversal; the original 4A implementation is already published. No installed-state rollback is needed. The original dirty checkout at `C:\dev\projects\BannerlordCoopSpectator3` and its unrelated changes were not edited.


## 10. Final audit and documentation impact

The aggregate full inventory and final product builds passed. A subsequent runner-only adjustment preserves child cancellation/error classification; the focused spawn-smoke C#/PowerShell suite and both script parsers passed again afterward. Product source was unchanged after the final CompileOnly build.

Read/updated living documents: `README.md`, `BATTLE_TEST_AUTOMATION_SPEC.md` (revision 24), `BATTLE_TEST_AUTOMATION_M3_FIELD_FIXTURE.md`, `ARCHITECTURE.md`, `CODE_MAP.md`, `RUNTIME_FLOWS.md`, `INVARIANTS_AND_RISKS.md`, and `BUILD_TEST_DEBUG.md`. This M4 report is new. Existing M2 feasibility/control reports and historical audit evidence remain unchanged because this stage does not reinterpret their runtime results. The current overview's stale M3-pending/inventory statements and contradictory compile-only guidance were corrected.

Historical 4A pre-publication audit passed: HEAD and branch remained at the then-stated baseline, no staged changes, 27 changed source/test/document files, no generated outputs in the delta, no CR bytes or LF/CRLF churn, and `git diff --check` clean. All three fixture hashes remain pinned as listed above. No staging, commit, push or branch write was performed.

## 11. Local-only file isolation (2026-09-09)

Status: **Source, contracts and compilation verified; native L2 remains pending.**
Approval: the user's explicit "ок" after the local-isolation source/test/CompileOnly plan.
Pre-publication validation baseline: clean published HEAD/upstream 7d75742c338f504499ec01dd8e3b2f189a1f7a03; this section records the subsequent local-isolation source/document delta before its separately approved publication.

### 11.1 Why this correction was required

The original 4A roster and phase isolation did not cover every shared file. Status publication and selection/spawn cleanup still used Windows MyDocuments, which resolves to the user's OneDrive folder. The smoke bridge and runner also read the personal battle_result.json as a preservation check. The user explicitly requested local test files instead.

The preceding 4B preparation passed 24/24 contracts and both builds, but its first staging preflight stopped before installed/shared-file mutation. Later launch requests were rejected by automatic approval review. No native smoke attempt occurred. The previous temporary m4b-stage-20260909-01 transaction wrapper backs up/clears/restores Documents files and is **obsolete for the new local-only requirement; do not reuse it**. This source change does not bypass or resolve permission-review restrictions on installed DLL replacement or server launch.

### 11.2 Local path ownership

CoopAutomationRuntimeBridge.ResolveCoopFolderPath delegates to CoopAutomationRuntimeContract.ResolveCoopFolderPath. The runtime adapter supplies a lazy Documents provider. The provider is never invoked when automation is enabled and a nonempty smoke profile is requested.

- Exact FieldDedicatedSpawnSmokeV1 plus a valid run ID/root, token hash, module hash and Suppress policy selects %TEMP%\CoopSpectator\Automation\<RunId>\state\bridge.
- Unknown nonempty profiles, invalid configuration, an incorrect canonical run root, escaping relative paths and existing reparse ancestors fail closed. There is no production-folder fallback.
- Disabled automation, including a stale profile variable, retains the original Documents path.
- Existing automation modes with no smoke profile retain their established behavior. This correction does not relocate the campaign recorder or connection-feasibility scenario.
- Phase status retains its existing run-owned state/phase path and active-fixture gate. Admitted roster reads retain immutable hash-pinned payload bytes.

The following path factories now use the shared resolver: BattleRosterFile.GetRosterFilePath; CoopBattleResultBridgeFile.GetResultFilePath; GetCoopFolderPath in entry status, selection, spawn, phase, role-matrix progress, exact-agent trace, compatibility report and runtime bundle; CoopHeroCreationBridgeFile.GetDirectoryPath; CoopCampaignMapPrototypeBridgeFile.GetStateFilePath. Auxiliary routing prevents an enabled diagnostic or shared helper from selecting personal Documents during the field profile. No diagnostic feature was enabled and no physical agent/controller logic changed.

This guarantee concerns the module's shared Documents bridges and the field runner's result guard. Native engine logging/configuration and other separately authorized automation scenarios are not redirected by this change.

### 11.3 Result suppression evidence

Before product launch, Initialize-CoopSpawnSmokeLocalResultCore creates state/bridge/battle_result.json with CreateNew semantics and these exact UTF-8, no-BOM bytes (LF line endings):

~~~text
CoopSpectator local result publication sentinel
RunId=<RunId>
~~~

The final LF is required. The file is a control sentinel, not a campaign result. Existing files cannot be silently reused. The module independently computes the expected SHA-256 from RunId and rejects missing/changed bytes before binding the fixture. Its existing observation/result/disposal checks compare this identity; a missing file is not a passing baseline. The real WriteResult method must still report Suppress for the real nonempty result.

Dedicated evidence requires ProtectedResultScope=RunLocal and ProtectedResultRelativePath=state/bridge/battle_result.json. Missing, legacy-global or escaping declarations are rejected by both C# and PowerShell. Attempt schema is now coop-field-spawn-smoke-attempt-v2, with ResultProtectionScope=RunLocal, ProductionBattleResultAccess=NotAccessed and LocalBattleResultBefore/After/Unchanged. The parent independently requires both file facts to exist at the exact child path with that child's expected sentinel hash.

No personal result file is read, hashed, backed up, removed, restored or claimed byte-for-byte measured by this field runner. Local preservation plus suppression evidence must not be described as a global-file checksum measurement. Historical 4A and M2/M3 evidence retains its original meaning.

### 11.4 Validation

- m4-local-contracts-20260909-01: full 24-project inventory, 24 passed, zero failed; runner lock released and reacquired.
- Runtime contracts: production fallback through a synthetic Documents provider, stale disabled profile, existing non-smoke behavior, forbidden provider for field mode, invalid/unknown configuration and root/path rejection.
- Spawn-smoke contracts: actual local status write, selection write/read/consume/clear, spawn write/consume/clear, two separate local roots with first-root preservation, missing/incorrect/changed sentinel rejection, explicit evidence-scope negatives, and lifecycle reset.
- Both Windows PowerShell and PowerShell 7 execute the real sentinel initializer, reject reuse and wrong roots, reject a real temporary NTFS junction, verify C#/PowerShell sentinel bytes, and reject altered/missing local facts.
- Campaign-result guard: the real writer suppresses nonempty 47-entry results for Battle, Village, SiegeAssault, SallyOut, SiegeAmbush, Hideout, HideoutAmbush, SiegeOutside, Blockade, BlockadeSallyOut and LordsHall; invalid policy rejects publication. The pre/post sentinel is entirely local.
- m4-local-compile-20260909-01: independent Release client and dedicated builds passed; zero errors; existing totals remain 77 client / 49 dedicated warnings; recursive installed client/legacy/dedicated inventories unchanged.
- Client DLL SHA-256: CFC8627BECF67D39F31D1780B9CE6E1F0790F88B0006C0485D082D50872E7C41.
- Dedicated DLL SHA-256: DC8E4CA3E0584DCD5CBD623F6FD36A5ADBFC1ED44EF48CB6F1B3F39E07CFE7E9.

Commands, from the documented worktree:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-CoopTest.ps1 -Command Contracts -RunId m4-local-contracts-20260909-01 -All
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-CoopTest.ps1 -Command CompileOnly -RunId m4-local-compile-20260909-01 -GameRoot 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord' -DedicatedServerRoot 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server'
~~~

Build outputs/packages remain under their selected local run roots. Individual existing contract programs also create and clean their own local temporary test roots. RuntimeCompileStubs.cs is test-only and is excluded from product builds. No test resolves the personal result path.

### 11.5 Requirement and scenario audit

| Approved requirement | Evidence | Status |
|---|---|---|
| One local field-profile path authority | Runtime contract/adapter and shared path factories | Satisfied |
| No Documents fallback when field mode is requested | Lazy forbidden-provider and invalid-profile/root contracts | Satisfied |
| Shared status/selection/spawn files remain local | Real native-free filesystem operations and two-root preservation | Satisfied |
| Auxiliary Documents bridges use the same authority | All module MyDocuments call sites reduced to the lazy production provider; both product builds | Satisfied |
| Result publication remains suppressed | Real nonempty writer across 11 battle types; local sentinel mutations | Satisfied |
| Evidence does not claim a personal-file measurement | Required RunLocal scope/path and v2 local facts; legacy/missing negatives | Satisfied |
| Ordinary behavior retained when field mode is off | Synthetic production-provider checks, source route audit, full contracts | Satisfied |
| Full contracts and both non-deploying builds | Named runs and exact hashes above | Satisfied |
| English living documentation updated | Eight approved documents and this audit | Satisfied |
| No installation, native launch, Git write or permanent environment change | Source-only commands; installed inventory verification; final Git audit | Satisfied |
| Live field materialization/abort/cleanup | Not authorized in this source stage | Not Verifiable — deferred |
| Same-process sequential-mission static reset | Two filesystem roots do not establish native reset | Not Verifiable — existing gap |

Ordinary field dedicated: source/contracts Passed; live Not Run. Campaign/client participation: Not Applicable to the intended zero-client smoke. Village, siege assault/deployment, sally-out, siege ambush/relief, lords hall, day/night hideouts, blockade variants and reconnect/sequential missions: relevant shared source/contracts Passed within the stated boundaries; live Not Run. No broad native regression or complete Milestone 4 claim is made.

### 11.6 Documentation, Git and next boundary

Read and updated: README.md, BATTLE_TEST_AUTOMATION_SPEC.md (revision 25), this M4 report, BUILD_TEST_DEBUG.md, RUNTIME_FLOWS.md, INVARIANTS_AND_RISKS.md, ARCHITECTURE.md and CODE_MAP.md. No new repository report was created. The M3 document and three pinned fixture files remain unchanged; historical control/staging reports retain their original evidence.

At the pre-publication source audit, the old dirty checkout at C:\dev\projects\BannerlordCoopSpectator3 had not been edited. Source/tests contained 25 changed/new files and documentation contained eight; the delta had no generated product outputs or LF/CRLF churn. HEAD/upstream were 7d75742 and no Git staging/commit/push had occurred during that source stage. Publication is a separate approved operation. A later native run still requires separately approved staging and launch, using a revised procedure that handles only the installed module, with no personal Documents transaction.


## 12. First clean local-only live attempt (2026-09-09)

### 12.1 Disposition and provenance

**Milestone 4 remains open.** The first native field attempt reached seven successful bootstrap acknowledgements but never opened the mission. The public parent returned Timeout (exit 31). The child stalled while handling failure and did not finalize its manifest or attempt report. Exact manual cleanup was required; the approved outer transaction then restored the complete installed baseline successfully. No product source fix or second native attempt was made in this stage.

All validation used clean local/upstream revision ebf9cd3d44186d6e3ef835f7acfe34589b1a1506 in C:\Users\Admin\.codex\worktrees\1b21\BannerlordCoopSpectator3. The separate checkout at C:\dev\projects\BannerlordCoopSpectator3 and its unrelated changes were not edited. Run artifacts are below C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation:

| Purpose | Run directory | Result |
|---|---|---|
| Fresh full contracts | m4b-local-contracts-20260909-01 | 24/24 Passed, zero failed |
| Fresh client/dedicated CompileOnly | m4b-local-compile-20260909-01 | Both Passed; 77 client / 49 dedicated warnings, zero errors; no installed changes during compilation |
| Local backup/staging/restore transaction | m4b-local-stage-20260909-01 | Self-test Passed; exactly two DLLs temporarily replaced; restoration Passed |
| Public two-attempt driver | m4b-local-live-20260909-01 | Timeout, exit 31; L2PassClaimed=false and L3PassClaimed=false |
| First child | m4b-local-live-20260909-01-01 | Failed before mission; child terminal outcome/report unavailable after runner stall |
| Second child | m4b-local-live-20260909-01-02 | Not Run; directory was not created |

Fresh client DLL SHA-256: 0CC1B70847E3CF5D6841B9163D7A4F71971BBD56B5A95F5981ED3D7B1C84FEEC. Fresh dedicated DLL SHA-256: DC8E4CA3E0584DCD5CBD623F6FD36A5ADBFC1ED44EF48CB6F1B3F39E07CFE7E9.

The stage-local work/Invoke-M4BLocalTransaction.ps1 helper (SHA-256 34171C879FC4A1C56860662E1D45EFF1313F3DDB39C0BEED84AF66E4FC38CB9A) passed path/source-hash/current-target rejection, real junction rejection, restoration after the first replacement fails, and full two-file replacement/restoration checks. It backed up all 218 dedicated files (358,279,173 bytes), verified existing dependency compatibility, and replaced only CoopSpectator.dll in the module's Win64_Shipping_Server and Win64_Shipping_Client bins. The public driver used RuntimeTimeoutSeconds=420, UDP 7210, the exact staged dedicated hash and two fresh child contexts. This helper is a retained transaction artifact, not a reusable deployment command: it pins this revision and these run IDs. The obsolete Documents transaction remains forbidden.

### 12.2 Observed native boundary and confirmed source mismatch

Server PID 57388 loaded the exact fresh dedicated DLL from Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll. It published native control readiness at 2026-09-08T23:53:03.1855699Z. Bootstrap command b0b501ef-d4bd-4dc0-9141-a4a0698bae98 acknowledged ServerName, MaxNumberOfPlayers, GameType, Map, UsableMap, StartGameRequested and StartGameConfirmed. The final acknowledgement at 23:53:04.2441707Z confirmed IsPlaying=true;GameType=CoopBattle;Map=battle_terrain_029.

Progress then remained WaitingForMissionCommandReady. The role kept publishing heartbeats, including after its request expired; this is not evidence that the game engine itself hung. At 2026-09-09T00:00:03.2043112Z the bootstrap status became Failed / RequestExpired. No start_mission acknowledgement, mission opening, native agent observation, early abort, or real result publication attempt was reached.

Read-only inspection of the exact installed TaleWorlds.MountAndBlade.ListedServer.dll with ilspycmd 9.1.0.7988 established:

- Assembly SHA-256: C7D27584FCE431B2D3734EB88C8DF52EF3B1BC8C5729F7FCE690CC277DA577E3.
- The native marker interface is TaleWorlds.MountAndBlade.ListedServer.IIntermissionState.
- ServerSideIntermissionManager.StartMissionAux checks the active state against that interface.
- DedicatedServer/Automation/CoopAutomationDedicatedControlBridge.cs, TryObserveNativeCommandReadiness, instead compares interface FullName with TaleWorlds.MountAndBlade.IIntermissionState. The missing ListedServer namespace is a confirmed source defect: the native interface cannot satisfy this literal.
- Native IsNewTaskAssignable checks that _currentTask is null. This guard remains necessary; the correction must not bypass it or repeat the mission command.

The live status does not expose the private idle and active-state subconditions separately. The interface mismatch is confirmed by the exact binary and source; no direct measurement of the private field, successful mission transition, or claim that this is the only defect is made. Pure contracts and successful compilation did not detect the reflection-string mismatch. No external workaround, tool update, native patch, fixture rewrite, or diagnostic flag was introduced.

### 12.3 Failure handling did not complete automatically

The child's last lease heartbeat was 2026-09-08T23:56:04.1803190Z. Its owned descendant inventory was updated through 23:56:06.5334753Z; artifact progress then stopped while CPU time and working memory continued growing (one observation exceeded 4.6 GB). Its manifest still has no terminal outcome and its lease retains Status=Active. These files were preserved as failed-run evidence rather than rewritten to imply graceful completion.

The timing is consistent with the runner's 180-second no-progress deadline after the final bootstrap progress. The exact stalled instruction is not established: there is no managed stack or completed failure artifact. The relevant source boundary is Invoke-CoopDedicatedSpawnSmokeAttempt's failure handling, Get-CoopCorrelatedFailureProcessesFromSnapshot, and Write-CoopRuntimeFailureEvidence before Stop-CoopOwnedRuntimeProcesses. The parent's independent deadline expired, forwarded cancellation, and eventually reported Timeout. Do not substitute a guessed child Timeout/Crash classification for the missing terminal record.

A separate source risk also remains: both the field driver's fatal-helper check and the common failure-evidence writer treat a correlated Watchdog.exe process as a crash signal. This run's watchdog existed during normal startup. Correlation proves ownership, not a crash; no passing or failing native-crash conclusion is inferred from its presence alone. This common failure path also serves connection feasibility and must be reviewed beyond the field scenario before correction.

### 12.4 Exact cleanup, restoration and retained evidence

Authorized manual cleanup used the existing Stop-CoopExactProcessIdentityCore checks, including PID, start time, executable path and SHA-256. It forcibly stopped owned watchdog PID 54348. By the following checks, server PID 57388 and its console helper PID 51080 were absent; neither required a separate forced-stop call. After verifying that product processes and required ports were clear, it forcibly stopped the hung child PowerShell PID 7108. The parent runner and outer restoration process were left running to finish their own handling.

The outer transaction completed restoration at 2026-09-09T00:10:56.2942998Z:

- Both original dedicated DLLs again have SHA-256 2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414 and length 3,587,584 bytes.
- Complete dedicated (218 files), client (32 files), and legacy inventories match their pre-images; all difference arrays are empty.
- No owned product/runner process or dedicated-install process remains; UDP 7210 and 7777 are free.
- All six shared resource locks were released and reacquired. Independent post-restore probes also exclusively opened/released both runner locks: eight probes passed. Stale child lease metadata is not a live lock and was retained.
- The local sentinel is still the exact RunId-bound 84-byte file, SHA-256 6BF1BAB4BED0FD136E2F5CDBA8FE9D64A8F49476FEAC9873481ADD9C4BB1817A. This proves local preservation only; the real result suppression decision was not reached.
- No personal Documents result was accessed by the module/runner transaction, and no campaign or client was launched.

Authoritative artifacts in m4b-local-stage-20260909-01 are transaction.json, staged-inventory.json, self-test.json, native-readiness-audit.json, manual-exact-cleanup.json, stage-lock-release.json, restore-lock-release.json and post-restore-audit.json. Complete backups remain in backup/module. Three exact-PID native logs were separately retained under native-logs with source/copy SHA-256 verification because the child never completed normal log collection. The native error log contains loader messages; their effect was not established by this attempt. Absence of a finished crash.json/hang.json does not prove absence of a crash or hang.

The parent report is m4b-local-live-20260909-01/artifacts/results/dedicated-spawn-smoke.json. Its two declared AttemptRunIds describe the intended pair; ChildRunners contains only attempt 1, and Attempts is empty. Do not count the second declared ID as an executed run.

### 12.5 Requirement and scenario audit

| Approved requirement / acceptance boundary | Evidence | Status |
|---|---|---|
| Fresh clean full contracts and both non-deploying builds | Named runs and exact hashes above | Satisfied |
| Local helper failure/rollback checks and complete pre-image | Four self-test groups, 218-file backup | Satisfied |
| Exact two-DLL temporary staging and loaded identity | Staged inventory and server role status | Satisfied |
| No personal Documents transaction or campaign/client launch | Local helper, field path authority and run inventory | Satisfied within the module/runner boundary; no personal-file checksum claim |
| Native field mission/materialization/early abort/result suppression | Mission never opened | Not Satisfied |
| Second independent successful attempt | First attempt failed; second directory absent | Not Satisfied |
| Fully automatic failure evidence and cleanup | Child runner stalled; manual exact cleanup required | Not Satisfied |
| Failure rollback and final installed/resource preservation | Transaction and post-restore audit | Satisfied |
| English living documentation and evidence retention | Six approved documents; local failed artifacts retained | Satisfied |
| Same-process sequential-mission reset | No mission opened; fresh processes would not prove it | Not Verifiable — existing gap |

Ordinary field dedicated L2: Failed before mission, not a completed battle-stability test. Campaign and multiplayer client: Not Applicable to this zero-client attempt. Village, siege assault/deployment, sally-out, siege ambush, relief, lords hall, day/night hideouts, blockade variants, reconnect and same-process sequential missions: native Not Run. The readiness method is called only by CoopAutomationDedicatedSpawnSmokeObserver.Tick after explicit FieldDedicatedSpawnSmokeV1 admission; those ordinary production scenarios do not use it. Shared result/isolation contracts passed in the fresh full suite, which does not establish broad native regression.

### 12.6 Documentation and next boundary

Updated existing documents: this report, BATTLE_TEST_AUTOMATION_SPEC.md (revision 26), README.md, BUILD_TEST_DEBUG.md, RUNTIME_FLOWS.md and INVARIANTS_AND_RISKS.md. No new repository document or product source change was made. ARCHITECTURE.md and CODE_MAP.md remain unchanged because ownership and implementation locations did not change. Historical M2/M3 reports and all three pinned fixture files retain their original meaning and bytes.

No Git staging, commit, push or branch operation belongs to this stage. These six documentation edits are an uncommitted failed-validation report, not a production fix.

Before another native attempt, separately approve a source/contract stage to correct the exact readiness contract and reproduce/bound the failure-handling stall, including watchdog classification and cleanup despite diagnostic failure. Review common connection-feasibility consumers as well as field smoke. Preserve the native idle guard, one-shot commands, exact process identity, result suppression and pinned fixture. Native restaging/rerun must follow verified failure-path behavior; Milestone 4 is not complete.

## 13. Bounded failure diagnostic and source/contract correction (2026-09-09)

### 13.1 Disposition and evidence boundary

The separately approved correction stage is complete at source, contract, and non-deploying compilation level. **Milestone 4 remains open.** No Bannerlord game, dedicated server, multiplayer client, campaign, or mission was launched. No installed module, local sentinel, pinned fixture, environment configuration, Git index, branch, commit, or remote was changed.

The correction is based on repository HEAD ebf9cd3d44186d6e3ef835f7acfe34589b1a1506 plus the retained uncommitted documentation audit and the source/test changes described below. Because this is a dirty working-tree build, the resulting assemblies are verification artifacts, not staging candidates until a separate publication and clean-build gate is approved.

The bounded diagnostic root is:

`C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m4-failure-diagnostic-20260909-01`

Its aggregate `artifacts/summary.json` has SHA-256 `04353B3A7EF42A513B89920886D2A744E5D6195EFBA95B2ACE0DD4B8E1E6EC2C` and outcome `PassWithFindings`.

### 13.2 Bounded failure-path diagnostic

The temporary diagnostic exercised the current failure-evidence and exact-cleanup functions against synthetic owned processes under a 45-second worker deadline and a 512-MiB private-memory guard. It did not modify repository or installed files and was removed from the product implementation surface.

| Shell | Failure writer without correlation | Failure writer with owned watchdog correlation | Exact helper cleanup | Maximum private memory | Result |
|---|---:|---:|---:|---:|---|
| PowerShell 7.6.5 | 298.23 ms | 340.26 ms | 17,859.15 ms | 105.16 MiB | Pass |
| Windows PowerShell 5.1.26100.9444 | 363.58 ms | 379.55 ms | 17,866.78 ms | 197.63 MiB | Pass |

Confirmed findings:

- the previous policy classified a normal owned `Watchdog.exe` descendant as a fatal crash helper; ownership alone is not crash evidence;
- `Write-CoopRuntimeFailureEvidence` completed below 0.4 seconds in both supported shells, without runaway memory;
- the former 15-second graceful-close wait dominated cleanup of a non-responsive support helper;
- Windows PowerShell 5.1 under the unrestricted `-ExecutionPolicy Bypass` diagnostic host required an explicit exact-manifest import of `Microsoft.PowerShell.Utility` before `Get-FileHash` was available.

The exact instruction behind the earlier live child-runner stall was **not reproduced**. The earlier observation above 4.6 GiB therefore remains unattributed; this stage does not claim that the original stall or memory behavior was reproduced or conclusively fixed. Diagnostic phase markers improve attribution if a later live failure recurs.

Post-diagnostic checks found every created PID absent, UDP 7210/7777 free, and all eight runtime locks releasable. The installed dedicated server and dedicated client-bin DLLs remained `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`; the installed game client DLL remained `2A1E17E4FEC5330345D28387AF1C4E2D412D07F221EBE7EE02705FAAC250FFB4`; the local sentinel remained `6BF1BAB4BED0FD136E2F5CDBA8FE9D64A8F49476FEAC9873481ADD9C4BB1817A`.

### 13.3 Implemented correction

- `DedicatedServer/Automation/CoopAutomationDedicatedControlBridge.cs`: `TryObserveNativeCommandReadiness` now matches `TaleWorlds.MountAndBlade.ListedServer.IIntermissionState`, the exact interface established from the installed assembly. The `IsNewTaskAssignable` idle guard, field-only admission, and one-shot `start_mission` ownership are unchanged; no retry or broader battle-path hook was added.
- `scripts/CoopAutomationRunner.Core.ps1`: one centralized fatal-helper allowlist now contains only the exact client/dedicated `CrashUploader.exe` paths and system `WerFault.exe`. `Watchdog.exe` remains discoverable and cleanup-owned but cannot alone promote `Timeout` to `Crash`. Support roles receive a one-second graceful-close budget; primary product roles retain 15 seconds.
- `scripts/Invoke-CoopTest.ps1`: both Feasibility and DedicatedSpawnSmoke use the centralized fatal classification, exact per-role cleanup grace, and failure-only `FailureEvidenceCaptureStarted`, `FailureEvidenceCaptureCompleted`, `RuntimeCleanupStarted`, and `RuntimeCleanupCompleted` events. The supported Windows PowerShell 5.1 path explicitly initializes `Get-FileHash` from the exact built-in utility-module manifest when command discovery initially fails.
- `Tests/CoopAutomationSpawnSmoke.ContractTests`: source contracts require the exact ListedServer interface and retain the idle guard.
- `Tests/CoopAutomationRunner.ContractTests`: both supported shells verify the fatal-helper set, watchdog exclusion, one-second support cleanup, 15-second primary cleanup, hash-command initialization, and failure-phase markers.

The readiness change is reachable only through the explicit `FieldDedicatedSpawnSmokeV1` observer, so it does not alter village, siege assault/deployment, sally-out, siege ambush/relief, lords hall, hideout, blockade, reconnect, client, or campaign flows. The runner classification and cleanup policy is shared with connection Feasibility and future runtime scenarios; the full contract inventory is the cross-scenario source regression boundary. Native behavior for every scenario remains Not Run in this stage.

### 13.4 Verification

| Verification | Result |
|---|---|
| Both edited PowerShell scripts parsed | Pass; zero parser errors |
| Focused spawn-smoke contracts | Pass in Windows PowerShell 5.1 and PowerShell 7.6.5; 95 assertions per shell |
| Focused runner contracts | Pass in Windows PowerShell 5.1 and PowerShell 7.6.5 |
| `m4b-readiness-runner-contracts-20260909-01` | Pass; full canonical inventory 24/24, zero failed; `contracts.json` SHA-256 `2AF5DE3B99F39E07487940DAC39F466A1B2C0B507FD5D1A2F509B7A7E19295E6` |
| `m4b-readiness-runner-compile-20260909-01` | Pass; client and dedicated exit 0, 77/49 warning baseline, zero errors, no product process, installed inventories unchanged |
| Compile-only client output | Version 0.3.2; SHA-256 `25D1234F72C0B85EED7AE4C5E4CFC0AA903FEDA2D08520446560293F58E9FB18` |
| Compile-only dedicated output | Version 0.3.2; SHA-256 `0F7FA25AED9C6C7F2D4B3C250D73991B26EAA29189C5703FE753CB12C0D85382` |
| Installed before/after inventories | Byte-identical; both inventory JSON files SHA-256 `35D7049344D13EDCBFEAE2D3E389880AD7EBFA8464EE60AA8A8B83B96AEFBC86` |

### 13.5 Requirement and next boundary

| Requirement / acceptance boundary | Status |
|---|---|
| Exact native readiness contract with idle and one-shot guards | Satisfied at source/L1 |
| Watchdog ownership separated from fatal crash classification | Satisfied at source/L1 across shared runner consumers |
| Bounded support cleanup without weakening primary-role cleanup | Satisfied at source/L1 |
| Failure-path attribution markers and PowerShell 5.1 hash bootstrap | Satisfied at source/L1 |
| Full contracts and both non-deploying builds | Satisfied |
| Reproduction and exact attribution of the prior live stall/high memory | Not Reproduced; remains an observation to monitor |
| Automatic evidence and cleanup during an actual native failure | Not Verifiable — no live process ran |
| Mission opening, materialization, normal early abort, result suppression and second attempt | Not Run |
| Native L2 and Milestone 4 completion | Not Satisfied — fresh staged live evidence required |

The next operation is a separately approved publication/clean-build and installed-module staging transaction followed by one bounded local-only `DedicatedSpawnSmoke` rerun. It must retain the pinned fixture, local-only path authority, exact installed hash, process/port/lock ownership, result suppression, two fresh attempts, and outer restoration guarantees. If failure recurs, the new phase markers must identify whether the stall is before, inside, or after evidence capture or cleanup. No manual action inside the game is expected for the zero-client smoke.

## 14. Clean M4 L2 rerun: parent-liveness cancellation (2026-09-09)

### 14.1 Disposition and clean provenance

**Milestone 4 remains open.** Clean published revision `c3ec15a42d80d47ffbc5aeb6c49cbd115ff1dbda` reached a native dedicated launch with the corrected server binary, but the first child cancelled before module/control readiness when its repeated parent-liveness admission reported that the matching parent was not active. The second child was Not Run. No mission opened, no materialization or early abort occurred, and no L2/L3 pass was claimed.

The source worktree was clean and exactly matched upstream before staging. The separate checkout at `C:\dev\projects\BannerlordCoopSpectator3` was not used or edited. Steam was started as a platform prerequisite; no credential, UI, campaign, or multiplayer-client action was performed.

| Purpose | Run directory | Result |
|---|---|---|
| Fresh clean full contracts | `m4r27-c-01` | 24/24 Passed; `contracts.json` SHA-256 `441FA814FDB44DDEB27E7D74F3A4F614376F220CA4A8528DD2AC96934D4E605B` |
| Fresh clean CompileOnly | `m4r27-b-01` | Client/dedicated Passed; installed inventories unchanged; no product process |
| Transaction and complete backup | `m4r27-stage-01` | Four self-test groups passed in PowerShell 7 and Windows PowerShell 5.1; staging and restoration completed |
| Public two-attempt driver | `m4r27-live-01` | Cancelled, exit 50; first child failed, second Not Run; L2PassClaimed=false |
| First child | `m4r27-live-01-01` | Cancelled, exit 50; automatic cleanup complete |
| Second child | `m4r27-live-01-02` | Not Run; directory absent |

The compile-only client output is version 0.3.2, 4,734,976 bytes, SHA-256 `90EDDB7670CEEA5797F3D6764EBDE637D38C3EFFDC0801A0E94AC80FEF0AB528`. The staged dedicated output is version 0.3.2, 3,646,976 bytes, SHA-256 `0F7FA25AED9C6C7F2D4B3C250D73991B26EAA29189C5703FE753CB12C0D85382`. The compile-only installed before/after inventory artifacts are byte-identical with SHA-256 `35D7049344D13EDCBFEAE2D3E389880AD7EBFA8464EE60AA8A8B83B96AEFBC86`.

### 14.2 New exact transaction

The new temporary helper is `m4r27-stage-01/work/Invoke-M4R27LocalTransaction.ps1`, SHA-256 `31269271CDECD0966BC157B699097179479C303AFB5E422AEE263F35E75BBD51`. It is pinned to revision `c3ec15a`, the exact contracts/build artifacts, installed original DLL SHA-256 `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`, and live RunId `m4r27-live-01`. It does not invoke or reuse either older M4 staging helper.

Both supported PowerShell hosts passed the exact helper's four local self-test groups: path/source/current-target rejection, real junction rejection, injected first-file failure restoration, and full two-file replacement/restoration comparison. The transaction then:

- acquired six independent shared-resource locks;
- captured complete 218-file dedicated, 32-file client, and empty legacy inventories;
- retained a complete verified 218-file backup under `backup/module`;
- changed only `CoopSpectator.dll` in the dedicated `Win64_Shipping_Server` and `Win64_Shipping_Client` bins;
- passed the staged-path diff check before launch;
- invoked the public runner with the exact staged hash and a 420-second runtime budget;
- restored in `finally` after the nonzero live result.

Transaction report `m4r27-stage-01/transaction.json` has SHA-256 `B63A7B6D283F702288D92EC57583F438210C67FAF8D1342E48289315E6411457`, `Restored=true`, and an empty `RestorationFailure`.

### 14.3 Observed cancellation boundary

Parent runner PID 13724 created child Windows PowerShell PID 21800. The child's manifest records parent PID 13724, exact parent RunId, and a matching intent/nonce. Its initial parent admission succeeded: the child completed initialization, copied the pinned fixture, created the run-local result sentinel, acquired shared locks, and started exact dedicated PID 22132.

PID 22132 loaded the expected staged module from the dedicated client bin and published schema-2 role state with the same SHA-256 `0F7FA25...5382`. Before it reached terminal `ModuleReady` or authoritative control readiness, a later `Update-CoopLease` call re-ran `Assert-CoopSpawnSmokeParent` and threw `OperationCanceledException`: `The matching parent smoke runner is not active; aborting this attempt.`

The parent itself remained alive until the child exited and then finalized normally. The rejected gate in `Assert-CoopSpawnSmokeParent` combines all of the following into one non-instrumented condition:

- readable parent manifest, lease, and attempt intent;
- exact command, RunId, nonce, child identity, and parent owner PID;
- lease status exactly `Active`;
- parent lease heartbeat no older than 10 seconds.

The immutable manifest/intent/PID values are matching in the retained artifacts, and the same admission had already succeeded. The lease is finalized as `Completed` only after the child exit. However, no artifact captured the individual values at the rejecting read. The exact trigger therefore cannot be distinguished between a heartbeat older than the hard 10-second window and a transient null/incomplete shared read. Startup timing is consistent with the heartbeat window, but that is an inference, not a direct measurement. Do not claim a specific scheduling stall, file-system race, or parent death.

This gate is specific to the two-process `DedicatedSpawnSmoke` controller. Connection Feasibility does not use `Assert-CoopSpawnSmokeParent`; no campaign, client, village, siege, hideout, or other battle adapter was reached. The corrected ListedServer readiness code was loaded but its behavior was not exercised past this earlier orchestration gate.

### 14.4 Automatic cleanup and corrected helper semantics

Unlike the preceding live attempt, the child completed its terminal report, manifest, lease, native-log capture, exact cleanup, and both lock-release reports without manual recovery:

- `spawn-smoke-attempt.json` outcome is `Cancelled`; primary and terminal reasons match;
- exact dedicated PID 22132 was identity-validated and forcibly stopped after the retained primary-role graceful budget;
- no owned process or required port remained;
- `NoFatalHelpersConfirmed=true`; the captured watchdog log did not promote cancellation to `Crash`;
- required `rgl_log_22132.txt` and `rgl_log_errors_22132.txt` plus optional `watchdog_log_22132.txt` were captured with exact hashes;
- the 71-byte run-local result remained SHA-256 `09148A1537500D29F253BE2669A7F240CFD8C4B8ACFC6F43EFF11AB748F3CC80`;
- personal Documents and the production result were not accessed.

This verifies automatic cleanup for this early `Cancelled` path and the non-fatal watchdog policy. It does **not** exercise the new Crash/Timeout-only failure-evidence phase markers, reproduce the old failure-writer stall, or prove cleanup after a real mission failure.

The parent pair report SHA-256 is `2057402C1D1BC69DAB3B306A7E87E9CECA19A5128472017FF5E554143A645705`; the child attempt report SHA-256 is `8C464E10F35028D6FDB709CF3A9A35E616EFCFE922ED100F793B68B0D06CBFAB`.

### 14.5 Restoration and independent postflight

The outer transaction restored both dedicated DLLs to 3,587,584 bytes and SHA-256 `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`. Independent recursive comparison confirmed:

- dedicated inventory: 218 before, 218 after, zero path/hash/length differences;
- game client inventory: 32 before, 32 after, zero differences;
- legacy inventory: empty before and after;
- installed game client DLL unchanged at 4,703,744 bytes and SHA-256 `2A1E17E4FEC5330345D28387AF1C4E2D412D07F221EBE7EE02705FAAC250FFB4`;
- zero Bannerlord, dedicated, watchdog, crash-uploader, or Windows error-reporting process;
- UDP/TCP 7210 and 7777 free;
- six shared locks and both parent/child runner locks opened exclusively in eight independent probes;
- repository still clean with local HEAD and upstream both `c3ec15a`.

### 14.6 Requirement audit and next boundary

| Requirement / acceptance boundary | Status |
|---|---|
| Clean published source, fresh 24/24 contracts, and both builds | Satisfied |
| New exact helper, self-tests, full backup, and two-DLL-only staging | Satisfied |
| Corrected dedicated binary loaded | Satisfied |
| Parent/child orchestration remains live through startup | Not Satisfied — combined liveness gate cancelled attempt 1 |
| Automatic cleanup for observed cancellation | Satisfied; no manual recovery |
| Watchdog ownership remains non-fatal | Satisfied for observed cancellation evidence |
| Full installed/resource restoration | Satisfied and independently rechecked |
| Corrected native readiness, mission/materialization/abort/result suppression | Not Reached |
| Second fresh successful attempt | Not Run |
| Crash/Timeout failure phase markers in a live failure | Not Exercised |
| L2 and Milestone 4 completion | Not Satisfied |

Before another native attempt, separately approve a narrow source/contract stage for the parent/child liveness contract. It must preserve orphan detection and exact PID/nonce/run binding, record each failed admission fact, test delayed child startup and parent-heartbeat scheduling under Windows PowerShell 5.1 and PowerShell 7, and use a measured/configured freshness rule rather than removing the guard or adding an arbitrary sleep. Review other consumers of the shared lease primitive, but do not change battle adapters or the verified ListedServer readiness correction. No further staging, rerun, code fix, Git operation, or L2 claim belongs to this live-audit stage.

## 15. Parent-liveness source and contract correction (2026-09-09)

### 15.1 Scope and cross-battle audit

The correction is limited to the aggregate runner's process orchestration. `Assert-CoopSpawnSmokeParent` is reachable only when `Command=DedicatedSpawnSmoke` and `SpawnSmokeAttempt` is 1 or 2. Every other command rejects `SpawnSmokeAttempt` and `ParentRunId`; no land, village, siege, hideout, lord's-hall or other battle adapter invokes this admission function. No C# product-runtime file or native readiness observer changed.

The output-capture primitive is shared by Feasibility, Record, client launch/join and dedicated smoke flows. Its previous default allowed one poll to synchronously write as many as 8,192 lines per stream with auto-flush. A sufficiently large output burst could therefore monopolize a caller between lease updates. The retained live artifacts do not prove that this caused `m4r27-live-01-01`; the source correction removes that scheduling hazard without relabelling the old failure.

### 15.2 Implemented contract

`Get-CoopSpawnSmokeParentAdmissionCore` returns schema `coop-spawn-smoke-parent-admission-v1` with a distinct first failure code and a complete fact map for:

- parent manifest, lease and attempt-intent readability;
- exact command, parent and child RunIds, attempt number and nonce relations;
- exact lease owner PID and `Active` status;
- parsed heartbeat timeline, measured age and the configured ten-second deadline.

`Assert-CoopSpawnSmokeParent` additionally requires exactly one matching Runner role and a live exact process identity. After the first fully accepted observation, a transient unreadable manifest/lease/intent snapshot may use the cached exact role only while both conditions remain true: the cached process identity is still live and the last full acceptance is no older than the existing ten-second heartbeat deadline. Immutable mismatch, stale/invalid heartbeat, non-Active status and process loss still cancel immediately. The controller neither raises the deadline nor adds a sleep.

The bounded unreadable-state decision is implemented by the pure `Get-CoopSpawnSmokeParentReadFallbackCore` classifier. It returns schema `coop-spawn-smoke-parent-read-fallback-v1`, whether fallback is allowed, the admission failure code, exact cached-process observation, last accepted timestamp/age, deadline and one of the distinct rejection codes `ParentAdmissionNotRetryable`, `ParentProcessIdentityLost`, or `ParentAdmissionReadGraceExpired`.

On rejection only, the child attempts one atomic `artifacts/identity/spawn-smoke-parent-rejection.json` write with schema `coop-spawn-smoke-parent-rejection-v1`, the effective failure code, fact map, measured heartbeat values, cached acceptance timestamp, process-identity observation and the structured read-fallback result when that path was evaluated. The same code is attached to the cancellation exception. No success-path diagnostic file or continuous logging was added.

`Update-CoopProcessTextCapture` now limits each stdout and stderr drain to 100 ms per poll in addition to its line-count bound. The public smoke parent requests that bound explicitly. Completion still repeatedly drains to EOF and retains every line; the change time-slices work rather than discarding output.

### 15.3 Contract and build evidence

The focused `CoopAutomationRunner.ContractTests` harness passed under Windows PowerShell 5.1 (`5.1.26100.9444`) and PowerShell 7 (`7.6.5`). It verifies accepted exact admission, all three retryable unreadable snapshots, allowed fallback at five seconds with a matching exact process, expired fallback at eleven seconds, immediate rejection after exact-process loss, every immutable/status/timeline failure code, a stale heartbeat, and a synthetic process emitting 4,000 stdout plus 4,000 stderr lines. A single 50 ms-per-stream poll remained bounded, the producer did not deadlock, and all 8,000 lines were retained at completion.

The first post-hardening aggregate rerun used a long RunId and failed one unrelated contract launch because the generated Windows executable path was too long. That run is rejected as final evidence and caused no code change. Final short-path run `m4pl-c3-01` passed all 24 projects after the direct fallback-decision cases were added. `contracts.json` SHA-256 is `4865EB701D1621DBEBF442C3D23A6D9FB091141616476D650D1EDFFC5E35CB3A`.

`m4pl-b3-01` passed both non-deploying Release builds:

| Output | Version | Length | SHA-256 | Warnings / errors |
|---|---:|---:|---|---:|
| Client | 0.3.2 | 4,734,976 | `5FC9028AAD2BB2318E29A03018F761B6A5D136E2E5171110E768D3BB55D3821F` | 77 / 0 |
| Dedicated | 0.3.2 | 3,646,976 | `0F7FA25AED9C6C7F2D4B3C250D73991B26EAA29189C5703FE753CB12C0D85382` | 49 / 0 |

No product process launched. Installed before/after inventories are byte-identical with SHA-256 `35D7049344D13EDCBFEAE2D3E389880AD7EBFA8464EE60AA8A8B83B96AEFBC86`.

### 15.4 Evidence boundary and next action

This satisfies the parent-liveness source/L1 correction gate only. It does not reproduce the exact `m4r27-live-01-01` rejecting read, publish a clean revision, stage a DLL, launch the dedicated server, exercise ListedServer readiness, open a mission, materialize agents, abort the mission, run the second child, or claim L2/L3.

The next separately approved sequence is: source/documentation commit and push, clean published contracts/build provenance, a new exact full-backup two-DLL staging transaction, one bounded two-attempt zero-client `DedicatedSpawnSmoke` rerun, and unconditional full restoration. If rejection recurs, the new failure-only artifact must identify the exact fact instead of returning the former combined message.

## 16. Clean published liveness-corrected rerun: native mission-opening crash and failure-evidence stall (2026-09-09)

### 16.1 Clean provenance and controlled staging

The rerun used clean local/upstream revision `6d38780af05975b76c27af914ad4e0d66b057ae4`. Fresh `m4pl-p1-c1` passed all 24 contract projects; `contracts.json` SHA-256 is `06219FC8F253F2984F655DB173499CFE194E277E266B1FA3B9E8494FEC205A2C`. Fresh `m4pl-p1-b1` passed both non-deploying Release builds with no product process and byte-identical installed inventories, whose before/after JSON SHA-256 is `35D7049344D13EDCBFEAE2D3E389880AD7EBFA8464EE60AA8A8B83B96AEFBC86`.

| Output | Version | Length | SHA-256 | Warnings / errors |
|---|---:|---:|---|---:|
| Client | 0.3.2 | 4,734,976 | `00D103754F70C529491002ADF840C56847E0C68B6BB5BE57DF768114E3160BDF` | 77 / 0 |
| Dedicated | 0.3.2 | 3,646,976 | `0F7FA25AED9C6C7F2D4B3C250D73991B26EAA29189C5703FE753CB12C0D85382` | 49 / 0 |

The new one-use transaction helper under `m4pl-p1-s1` has SHA-256 `2B4D0E75BBF01768D29737705AE6FF0E0B9AD3FD316C5FED26C062B0444F67D1`. It passed path/hash/current-target rejection, junction rejection, injected first-file-failure restoration, and complete two-file replace/restore checks in Windows PowerShell 5.1 and PowerShell 7. It retained a full 218-file dedicated pre-image and replaced only `CoopSpectator.dll` in the dedicated module's `Win64_Shipping_Server` and `Win64_Shipping_Client` directories. The installed original SHA-256 for both targets was `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`. The helper is pinned to this revision and these RunIds; it is evidence, not a reusable deployment command.

### 16.2 Parent-liveness closure and reached runtime boundary

Parent `m4pl-p1-l1` launched child `m4pl-p1-l1-01`, which loaded the exact staged dedicated SHA-256. No `spawn-smoke-parent-rejection.json` exists: the corrected parent/child liveness path remained admitted through native startup, closing the prior `m4r27` blocker.

Dedicated PID 15968 reached control readiness at `2026-09-09T13:07:11.2275887Z`, processed command `fa26b550-ae97-439c-ac7e-d639affc1b07`, produced all seven start-game acknowledgements, and confirmed `StartGameConfirmed=true`. It issued exactly one start-mission request and advanced to `MissionOpening` at `2026-09-09T13:07:16.7520177Z`. It produced no agent observation and no end-mission request. This proves startup, readiness and one-shot mission-opening progression, but not `MissionCurrent`, materialization, `PreBattleHold`, abort, result suppression completion, L2, or L3.

### 16.3 Exact native failure boundary

Windows Application Error event 1000 (record 151395, `2026-09-09T13:07:29.7967601Z`) and Windows Error Reporting event 1001 (record 151396, `2026-09-09T13:07:36.8847143Z`) correlate the terminated `DedicatedCustomServer.Starter.exe` to APPCRASH exception code `0xc0000005`. The Application Error fault module is `unknown`; WER reports bucket module `StackHash_3ede` and report ID `5be077f2-7cc0-4665-b48b-e6c20cf9d312`.

The archived 153,934-byte `Report.wer` has SHA-256 `EE9BD5E5BA75A85A85006066806C608F534C489B9B2A09C72817837B45173B7D` and records the staged `CoopSpectator.dll` among loaded modules, but the archive contains no dump. Therefore the evidence proves a native access-violation-class process failure during the mission-opening interval; it does not identify the faulting native module, stack, instruction, or root cause. WinDbg cannot resolve those missing facts without a dump.

### 16.4 Secondary live failure-evidence stall and exact manual cleanup

The child emitted `FailureEvidenceCaptureStarted` at `2026-09-09T13:07:24.4183216Z` but never emitted `FailureEvidenceCaptureCompleted` and created neither `crash.json` nor `hang.json`. At `2026-09-09T13:14:37.1859321Z`, exact child Runner PID 23808 had consumed 431.8 CPU seconds, 5.81 GiB working set and 6.0 GiB private memory. This reproduces the earlier live failure-finalization stall/high-memory symptom that bounded synthetic tests did not reproduce.

The public `Cancel` operation returned `EnvironmentBlocked` because the child lease heartbeat was stale; this is correct fail-closed behavior and prevented an uncertain-owner kill. After verifying the exact manifest, PID, start time, executable path, executable SHA-256 and parent PID, `Stop-CoopExactProcessIdentityCore` stopped only that Runner using force after the graceful path was unavailable. No product or failure-helper process remained at that point. The parent then returned `RunnerInternalError` with exit 40 because child exit code `-1` had no complete terminal artifact. Child 2 `m4pl-p1-l1-02` was Not Run, and `L2PassClaimed=false`.

The current evidence does not isolate the exact instruction inside `Write-CoopRuntimeFailureEvidence`. The next source investigation must start at the shared lowest-level failure writer and audit all callers/battle types before any fix. A leading risk is retention or serialization of a full heavyweight live `CimInstance` snapshot, but this remains a hypothesis until isolated offline or synthetically.

### 16.5 Unconditional restoration and independent postflight

The transaction returned `Restored=true`; its report SHA-256 is `BD87C14ECD55654E7133919CEAC362CDC70A10AB8EA427870AD03F365F40D6FF`. Independent postflight found 218 expected dedicated files with zero differences, 32 expected client files with zero differences, and the absent legacy location still absent. Both restored dedicated DLL locations have original SHA-256 `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`. No product process, exact Runner, or required-port owner remained. All six shared resource locks plus the parent and child Runner locks opened exclusively, 8/8. The protected 217,264-byte result remained SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`. The repository remained clean and matched upstream before this documentation update.

The private non-shareable manual audit is retained outside Git at `m4pl-p1-s1/manual-live-audit.json`, schema `m4pl-p1-manual-live-audit-v1`, SHA-256 `D27D890C35A412BBF94DD2BBCD6FD7D20A445FB471572560CB153AA7E2DB7B71`. Key retained artifact hashes are:

| Artifact | SHA-256 |
|---|---|
| Parent manifest | `3D9C14425634E19A6EB9B2247C577EF8C4AF91B15A74E147AB6C6DEE859C4439` |
| Pair report | `42BA1E0C54B87AFFCC4ECF750A8C95C70E4B33B7C2A6506046F2C9018AFF0DE4` |
| Child manifest | `562818CD4AF3A4E062C67BA0CAF60F6B56C9C3CB369C1344C5A293DD4856EB69` |
| Child events | `1134F843437CA468FFAD10A21AB71B019A93231EF8D4CC6F2A192C9F65B097C0` |
| Dedicated role status | `E927BFD6BFF4874DC6315EE7437C5989ED1114394785689680B8B11174D48271` |
| Control readiness | `DE1CBD08E267985F010FD957851F54F98F3814E0461819B633A615F8F9BC35CF` |
| Processed bootstrap request | `A63F397C505FE99320B6DFA95E54533367BC80D138ADA762C733698B288AC4A2` |
| Bootstrap status | `FF3A36AACCA2A10EA342B5E2421F1CD0D37DA97B3B55E82DF8B98746D286A7E0` |

### 16.6 Requirement disposition and next correction order

| Requirement / acceptance boundary | Status |
|---|---|
| Clean published source, fresh 24/24 contracts, and both builds | Satisfied |
| New exact helper, self-tests, full backup, and two-DLL-only staging | Satisfied |
| Parent/child liveness through dedicated startup | Satisfied; prior blocker closed |
| Correct dedicated binary, control readiness and seven acknowledgements | Satisfied |
| One-shot start mission and `MissionOpening` | Satisfied |
| Native failure classification | Partial — APPCRASH `0xc0000005`; module/stack/root cause unknown |
| Bounded failure evidence and automatic child cleanup | Not Satisfied — live stall/high memory; exact manual Runner stop required |
| Full installed/resource restoration | Satisfied and independently rechecked |
| `MissionCurrent`, agent materialization, `PreBattleHold`, abort and result suppression completion | Not Reached |
| Second fresh successful attempt | Not Run |
| L2 and Milestone 4 completion | Not Satisfied |

The next separately approved source/contracts task must proceed in this order:

1. Make shared failure-evidence finalization bounded. Do not retain or serialize a full heavyweight live `CimInstance` snapshot; establish the exact low-level cause offline/synthetically, audit Feasibility and every other shared caller/battle path, keep watchdog evidence non-fatal, preserve exact WER/CrashUploader correlation, continue lease/cancellation service, cap resource use, and guarantee terminal artifacts plus exact cleanup even if an optional evidence collector fails.
2. After that correction passes focused dual-shell contracts, 24/24, both CompileOnly builds, publication and controlled staging, rerun once with dump capture enabled or demonstrably available. Use the dump and symbols to locate the native `0xc0000005` during `MissionOpening`. Do not change battle adapters or retry blindly before failure finalization is safe.

No source fix, battle-adapter change, further native attempt, or L2 claim is part of this documentation stage.

## 17. Bounded failure-evidence source and contract closure (2026-09-09)

### 17.1 Scope and cross-scenario audit

The live stall began after `FailureEvidenceCaptureStarted` inside the shared `Write-CoopRuntimeFailureEvidence` path. The retained run root contains only about 294 KiB, and its role/status/event JSON files are small; payload serialization volume does not explain the observed 6.0-GiB private-memory growth. The exact internal CIM instruction cannot be proven without a dump of the stalled Runner, so this stage does not relabel the prior observation with an unproven root cause.

Source audit found three synchronous full `Win32_Process` snapshots in finalization: `Add-CoopOwnedDescendants`, `Write-CoopRuntimeFailureEvidence`, and the dedicated-smoke fatal-helper check. `Add-CoopOwnedDescendants` is shared by Feasibility, DedicatedSpawnSmoke and Record campaign cleanup. The failure writer is shared by Feasibility and DedicatedSpawnSmoke. No field, village, siege assault, sally-out, siege-ambush, relief, lords-hall, hideout, sequential-battle or reconnect adapter calls these functions directly; their exposure is through the shared runner command path. No product C#, battle adapter, fixture, readiness, mission or result-suppression code changed.

### 17.2 Implemented bounded collector

Published revision `c100cb8` adds `Get-CoopBoundedProcessSnapshotCore`. Full CIM acquisition now occurs only in a short-lived child of the same supported PowerShell host. Before JSON crosses the process boundary, each record is projected to only `ProcessId`, `ParentProcessId`, `ExecutablePath`, `CommandLine`, and `CreationDate`; no raw `CimInstance` reaches the Runner.

The Runner enforces all four limits independently:

| Boundary | Limit / behavior |
|---|---|
| Total collection deadline | 5,000 ms in production |
| Collector private memory | 268,435,456 bytes (256 MiB) |
| Captured stdout payload | 4,194,304 bytes (4 MiB) |
| Lightweight records | 4,096 |

States are `Captured`, `TimedOut`, `MemoryLimitExceeded`, `OutputLimitExceeded`, or `CollectorFailed`. A non-Captured state contains a bounded failure description and never exposes a partial record set as authoritative evidence. The parent polls private memory at 50-ms intervals and terminates the exact process handle on timeout, memory excess, parse failure or callback failure.

`Get-CoopBoundedRuntimeProcessSnapshot` registers a provisional `RuntimeFailureSupport` identity immediately after process creation and before waiting. The inventory records the exact PID, expected shell path, Runner parent PID and launch window. Therefore a Runner interruption does not erase recovery ownership of an in-flight collector. Normal and forced collector exits use the existing one-second support cleanup policy.

### 17.3 Failure publication and cleanup semantics

Feasibility and DedicatedSpawnSmoke now acquire one bounded snapshot and reuse its lightweight records for descendant discovery, exact fatal-helper correlation and crash/hang publication. This removes the former repeated full in-process snapshots while preserving the pure tree/correlation rules. Exact CrashUploader and WerFault paths plus owned-tree/command-line PID correlation remain required; Watchdog remains excluded from fatal helpers.

If snapshot collection fails, `Write-CoopRuntimeFailureEvidence` still publishes the primary Crash/Timeout artifact. `ProcessSnapshot` records schema, state, failure, elapsed time, limits, observed peak memory, collector PID, forced-stop use and record count; the records themselves are not serialized. If descendant discovery separately changes the final Runner classification, the original primary Crash/Timeout remains eligible for failure-evidence publication. `FailureEvidenceCaptureCompleted` and subsequent exact cleanup are therefore no longer held behind an unbounded in-process CIM operation. Disk/atomic-write failure remains a distinct `FailureEvidencePublicationFailed` runner error; this stage cannot promise an artifact when its destination itself is unwritable.

### 17.4 Verification evidence

The focused `CoopAutomationRunner.ContractTests` harness passed in Windows PowerShell 5.1 (`5.1.26100.9444`) and PowerShell 7 (`7.6.5`). It directly verifies:

- a valid lightweight payload and collector-start callback;
- absence of raw `CimInstance` records across the boundary;
- a 300-ms synthetic stall timeout and exact collector termination;
- a 32-MiB synthetic memory ceiling and exact collector termination;
- structured non-throwing collector failure;
- the production live lightweight snapshot in both shells;
- no surviving collector PID after every case;
- publication of `crash.json` with the primary failure and bounded snapshot metadata when the optional collector reports `TimedOut`, without serializing `Records`.

Final aggregate run `m4fe-c3` passed all 24 projects. Its `contracts.json` is 121,776 bytes with SHA-256 `67C32070A89CDFF6144C96D59B12787254B2FCEC2E6484902FC9B80C8EEC96A9`.

Final non-deploying run `m4fe-b2` passed both Release builds:

| Output | Version | Length | SHA-256 | Warnings / errors |
|---|---:|---:|---|---:|
| Client | 0.3.2 | 4,734,976 | `30BD9070639CBEA336F6AA916407496B5752E384ABAE4EA6C248272D11A4040C` | 77 / 0 |
| Dedicated | 0.3.2 | 3,646,976 | `0F7FA25AED9C6C7F2D4B3C250D73991B26EAA29189C5703FE753CB12C0D85382` | 49 / 0 |

The compile report SHA-256 is `73994167DA9C96B14915399A6A5CF8E37D909D12B65141B9E7BB27349A7A7E05`. `ProductProcessLaunched=false`. Installed before/after inventories are byte-identical with SHA-256 `B817EEA278FFFC398A61E0FD92C65EB07314E0A7474D11FC417492C39B8B1E6B`. Independent postflight found no Bannerlord/dedicated/crash-helper/watchdog process and no snapshot collector. PowerShell parsing, repository hygiene, LF policy and `git diff --check` passed.

### 17.5 Disposition and next boundary

| Requirement / acceptance boundary | Status |
|---|---|
| Full snapshots isolated from the Runner | Satisfied at source/L1 |
| Lightweight record projection; no raw CIM serialization | Satisfied at source/L1 |
| Time, memory, output and record bounds | Satisfied by dual-shell synthetic contracts |
| Exact collector ownership and termination | Satisfied by source and dual-shell contracts |
| WER/CrashUploader correlation and non-fatal Watchdog policy | Preserved and contract-verified |
| Terminal crash artifact when optional collection fails | Satisfied by dynamic dual-shell contract |
| Full 24-project regression and both CompileOnly builds | Satisfied |
| Installed module preservation and no product launch | Satisfied |
| Real live Crash/Timeout finalization and automatic cleanup | Not Run after correction |
| Native `0xc0000005` root cause, dump, materialization and L2 | Not Satisfied |

This closes the approved failure-evidence source/contracts substage only. The next separately approved stage must start from clean published `c100cb8`, perform fresh 24/24 and both CompileOnly builds, configure or independently prove dump capture before launch, use a new one-use full-backup two-DLL staging transaction, run one bounded zero-client DedicatedSpawnSmoke attempt, and restore unconditionally. The native rerun must test the corrected finalizer and capture a dump for the `MissionOpening` access violation; it must not change battle adapters or retry blindly if dump capture is unavailable.

## 18. Recovered full dump and exact managed failure diagnosis (2026-09-09)

### 18.1 Preflight recovery and no-rerun decision

The approved live/dump stage began from clean branch `codex/v0.1.1-refresh`; local HEAD and upstream both resolved to `143203a1fdadf0f18a7da84f309f35b1409f6713`. No Bannerlord, dedicated-server, launcher, crash-reporter, uploader, WER or watchdog process was running, and drive C had 257.50 GiB free.

Microsoft's [Collecting User-Mode Dumps](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps) documentation requires `LocalDumps` configuration under `HKEY_LOCAL_MACHINE`, defines `DumpType=2` as a full dump, permits per-executable overrides, and states that the dump is written before process termination. Preflight found an existing exact key at `HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\DedicatedCustomServer.Starter.exe` with `DumpFolder=C:\dumps`, `DumpType=2`, and `DumpCount=5`.

The folder contained the previously omitted dump for exact dedicated PID 15968:

| Fact | Value |
|---|---|
| Path | `C:\dumps\DedicatedCustomServer.Starter.exe.15968.dmp` |
| Length | 1,157,623,635 bytes |
| Created UTC | `2026-09-09T13:07:36.8896970Z` |
| Last write UTC | `2026-09-09T13:07:40.5061247Z` |
| SHA-256 | `E5348288E6FE61AAE7442AB32504E61E46E76DC2FA1526F46F56A6CF0E5CF124` |
| Header | Valid `MDMP` |

The PID and timestamp match the prior `m4pl-p1-l1-01` WER event and dedicated role. The actual full dump independently proves that the existing capture mechanism worked during the crash; the previous statement that no dump existed was true only for the archived run/report surface, not for the machine-wide dump folder.

Because the required dump already existed and localized the failure, replaying the same unchanged source and binaries would intentionally reproduce a known crash without exercising a correction. The no-blind-rerun gate therefore cancelled the remaining prelaunch sequence. No contracts/builds were repeated, no module was staged, no game process was launched, and no registry or dump-folder content was changed. The published `m4fe-c3` 24/24 and `m4fe-b2` CompileOnly evidence still matches the unchanged technical revision `c100cb8`; `143203a` changed documentation only.

### 18.2 Lowest-level managed diagnosis

WinDbg was not installed. `dotnet-dump` version `9.0.661903` successfully loaded the full dump and provided the required managed boundary:

- `dumpexceptions` found one meaningful application exception: `System.NullReferenceException` at object `000001af85ee86d0`;
- `clrthreads` placed it on debugger thread 4 / OS thread `0x20f0`;
- `printexception` placed the fault at `DynamicClass.TaleWorlds.MountAndBlade.MissionScoreboardComponent.AfterStart_Patch2(...) + 0x135`, followed by `Mission.AfterStart`, `MissionState.FinishMissionLoading`, and `MissionState.TickLoading`;
- dynamic IL shows the Harmony wrapper calling the CoopSpectator prefix, the original scoreboard body, and then the postfix. The failure occurs inside the original-body sequence before the postfix;
- `dumpobj 000001af849c86f0` shows non-null Mission (`000001af8498f198`), mission lobby (`000001af84993cf8`), mission network (`000001af849bcaf0`) and scoreboard data (`000001af849c86d8`), but `_mpGameModeBase=0000000000000000`.

The exact installed `TaleWorlds.MountAndBlade.dll` is 2,577,920 bytes with SHA-256 `C40E283E72AA90E6ED3BD64D6B8081AC7005DA76437CB3D463A12A2A9148C4EE`. `ilspycmd` confirms that `MissionScoreboardComponent.AfterStart` resolves `_mpGameModeBase` using `Mission.GetMissionBehavior<MissionMultiplayerGameModeBaseClient>()` and, when `GameNetwork.IsServerOrRecorder` is true, dereferences `_mpGameModeBase.RoundComponent` without checking `_mpGameModeBase` itself. The dump field state and exact method therefore identify the root cause: the dedicated field mission includes `MissionScoreboardComponent` without the required `MissionMultiplayerGameModeBaseClient` behavior.

This is a managed null dereference, not an unidentified native instruction. Windows still reported process-level APPCRASH `0xc0000005`, but that code is now only the outer termination classification.

### 18.3 Cross-mode and lifecycle audit

| Construction path | Scoreboard/client dependency disposition |
|---|---|
| `MissionMultiplayerCoopBattleMode` battle-map server | Confirmed defect: retains `MissionScoreboardComponent`, does not add a `MissionMultiplayerGameModeBaseClient`, and `ValidateServerStackSanity` explicitly removes `MissionMultiplayerCoopBattleClient` as client-only. Field, land, village and sally-out support appended to this list share the risk. |
| Coop hideout day/night | Already protected: each dedicated wrapper inserts `MissionMultiplayerCoopBattleClient` when no base-client behavior exists, explicitly for scoreboard lifecycle compatibility. |
| Coop siege assault/deployment | Already protected: the server list contains `MissionMultiplayerCoopSiegeAssaultWithDeploymentClient` before the scoreboard. |
| CoopTdm | Supplies `MissionMultiplayerCoopTdmClient` on the server; dedicated mode also omits its scoreboard. |
| Coop hero creator / campaign-map prototype | Each server list supplies its matching `MissionMultiplayerGameModeBaseClient` derivative before the scoreboard. |
| TdmClone minimal dedicated path | No scoreboard, so this exact dependency is not exercised. |
| TdmClone full server path | Latent same defect: it adds a scoreboard but no `MissionMultiplayerGameModeBaseClient`. |
| Sequential/reconnect orchestration | No independent behavior factory; it inherits the selected scenario's construction-path disposition. |

The exact engine class also dereferences `_mpGameModeBase.RoundComponent` in `OnRemoveBehavior` and `OnClearScene`. A narrow patch that only suppresses the observed `AfterStart` line would leave later lifecycle failures and may break `MissionCustomGameServerComponent` compatibility. The correction must enforce a coherent behavior-stack dependency or safely remove the entire scoreboard contract; it must not merely swallow this exception.

### 18.4 Disposition and next boundary

| Requirement / acceptance boundary | Status |
|---|---|
| Exact full dump correlated to prior crash | Satisfied |
| WER capture mechanism independently proven | Satisfied by actual prior full dump |
| Managed exception, thread, method and null field | Satisfied |
| Cross-mode scoreboard dependency audit | Satisfied at source level |
| New registry/staging/product mutation | Not Performed |
| Redundant unchanged native rerun | Not Run by design |
| Failure-finalizer correction exercised in a live crash | Still Not Run |
| Scoreboard dependency correction | Not Implemented |
| Mission materialization and L2 | Not Satisfied |

The next separately approved stage is a source/contracts correction. It must enforce the `MissionScoreboardComponent` → `MissionMultiplayerGameModeBaseClient` dependency before mission start, cover both the confirmed CoopBattle battle-map path and the latent full TdmClone server path, preserve the already-safe hideout/siege/other-mode ordering, and test `AfterStart`, clear/remove lifecycle expectations without adding default-on hot-path diagnostics. Only a clean-published correction may proceed through fresh 24/24, both CompileOnly builds, controlled staging and one bounded live attempt. The existing dump configuration must remain a preflight fact, not be rewritten unnecessarily.

## 19. Scoreboard server dependency source and contract closure (2026-09-09)

### 19.1 Scope and implementation

The user approved the cross-mode source/contracts correction after the full-dump diagnosis. Published technical revision `a6fe74fec0e596b5466faf477b9713bea0b95e72` (`a6fe74f`) implements the smallest coherent behavior-stack correction:

- `CoopScoreboardBehaviorDependencyContract` is a pure decision: only a server/recorder stack that has a scoreboard and lacks any `MissionMultiplayerGameModeBaseClient` requires insertion;
- `MissionMultiplayerScoreboardServerBridge` derives from the exact required engine base, carries only the selected `MultiplayerGameType`, returns neutral gold/tactical/countdown values, and adds no client visuals, hot-path diagnostics, `AfterStart`, `OnClearScene`, or `OnRemoveBehavior` interception;
- `MissionBehaviorHelpers.EnsureServerScoreboardGameModeDependency` finds the first scoreboard, detects any existing base-client dependency, inserts exactly one bridge immediately before the scoreboard when required, and asserts the resulting invariant;
- direct `MissionMultiplayerCoopBattleMode` construction enables the helper with Battle or TeamDeathmatch identity according to the resolved scene;
- reusable CoopBattle construction used by the day/night hideout wrappers deliberately defers the helper, so those already-safe wrappers retain their established `MissionMultiplayerCoopBattleClient` and do not receive a duplicate bridge;
- full `MissionMultiplayerTdmCloneMode` server construction enables the same helper with TeamDeathmatch identity. Its minimal dedicated path remains unchanged because it has no scoreboard;
- the existing Harmony scoreboard prefix remains a `void` observation hook. The original engine lifecycle is not suppressed or exception-masked.

### 19.2 Cross-mode disposition

| Construction path | Post-correction disposition |
|---|---|
| Direct CoopBattle battle-map server, including field/land/village/sally-out support | One minimal server bridge is inserted before the scoreboard. |
| CoopBattle non-battle direct server path with a scoreboard | The same invariant is enforced with TeamDeathmatch identity. |
| Hideout day/night wrappers | Existing `MissionMultiplayerCoopBattleClient` insertion is retained; generic insertion is deferred and no duplicate is introduced. |
| Siege assault/deployment | Existing siege base-client behavior remains before the scoreboard; unchanged. |
| CoopTdm, hero creator, campaign-map prototype | Existing matching base-client behavior or scoreboard omission remains unchanged. |
| TdmClone minimal dedicated path | No scoreboard and therefore no bridge; unchanged. |
| TdmClone full server path | One minimal server bridge is inserted before the scoreboard. |
| Sequential/reconnect orchestration | Inherits the corrected selected-scenario construction path; no independent factory changed. |

This satisfies all three known engine lifecycle callers: `AfterStart`, `OnClearScene`, and `OnRemoveBehavior` can resolve a non-null `MissionMultiplayerGameModeBaseClient`. Source/L1 evidence does not prove their execution inside a real dedicated process.

### 19.3 Verification evidence

| Verification | Result |
|---|---|
| Focused `CoopBattleStartup.ContractTests` | Passed |
| `m4sd-c1` full manifest | Passed 24/24; zero failed projects |
| `m4sd-c1` `contracts.json` | 121,776 bytes; SHA-256 `22C5CA7630C3453DBF203F46538044EFA1BD6326E6EF43DA238AC18961E2A557` |
| `m4sd-b1` client CompileOnly | Passed; 77 existing warnings, zero errors; DLL 4,736,512 bytes, SHA-256 `A517CED31447A855D680945E979581080331AA7CA2F5C60E29DDA7D76C80C371` |
| `m4sd-b1` dedicated CompileOnly | Passed; 49 existing warnings, zero errors; DLL 3,649,024 bytes, SHA-256 `74B03C00987F46AD76A6F21BAF807672FC699A402C3BE5EE5D52CB6CEC2BD4B7` |
| `m4sd-b1` `compile-only.json` | 10,614 bytes; SHA-256 `38B93F6A8FCB97540D9BABC5C5F42876F166AB740DB2D5B384C0BE3A4973662D` |
| Installed inventories | Byte-for-byte unchanged; no product process launched |
| Installed dedicated DLLs | Both remain SHA-256 `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414` |
| Installed client DLL | Remains SHA-256 `2A1E17E4FEC5330345D28387AF1C4E2D412D07F221EBE7EE02705FAAC250FFB4` |
| Source hygiene | Eight technical files only; `git diff --check` passed; all are LF-only |
| Compiled-DLL inspection | The bridge, pure contract, ordered helper insertion and both mode call sites are present in the dedicated CompileOnly DLL |

### 19.4 Evidence boundary and next action

| Requirement / acceptance boundary | Status |
|---|---|
| Scoreboard/base-client dependency contract | Satisfied at source/L1 |
| Confirmed CoopBattle construction-path correction | Satisfied at source/L1 |
| Latent full TdmClone correction | Satisfied at source/L1 |
| Already-safe mode preservation | Satisfied by source contracts; native regression Not Run |
| New default-on hot-path diagnostics | None |
| Engine lifecycle suppression or exception swallowing | None |
| Installed module staging | Not Performed |
| Native `AfterStart`/clear/remove lifecycle | Not Run |
| Failure-finalizer correction exercised by a live failure | Still Not Run |
| Mission materialization, early abort and L2 | Not Satisfied |

The next stage must start from the clean published technical and documentation revisions, re-run the required clean gates if source identity changes, create a new one-use full-backup staging transaction, stage only the exact candidate binaries, prove existing dump capture without rewriting its working configuration, and execute one bounded zero-client live attempt. Restoration remains unconditional. A second attempt is allowed only according to the existing two-attempt contract and only if the first attempt reaches a valid evidence boundary. This live stage requires a separate approved plan.
