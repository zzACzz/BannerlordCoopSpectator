# Milestone 4 — Field dedicated spawn smoke: source, contracts, local isolation and live findings

Date: **2026-09-09** (Europe/Kyiv; validation IDs retain the approved 20260908 names).
Status: **Milestone 4 remains open. The clean zero-client live attempt of published `438b434` returned `Timeout/NoHeartbeat` before control readiness/bootstrap, with no mission opened and attempt two Not Run; automatic timeout cleanup and full restoration passed. Section 27 records the subsequent exact-rejection evidence correction: 42 focused cases per PowerShell host and full 24/24 contracts passed, without another native launch. Section 26 preserves the unresolved historical heartbeat observation; sections 24-25 retain mode/clock/peer implementation evidence. No native L2 pass is claimed.**
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

## 20. Recovered scoreboard-candidate run and bounded failure-writer diagnosis (2026-09-10)

### 20.1 Scope and provenance

This section owns the recovered `m4sd-p1-l1` evidence and the separately approved `m4sd-fe-d1` diagnostic. It supersedes section 19's prospective staging/rerun instruction; it does not rewrite that stage's historical source/L1 result or close Milestone 4.

The investigation started at clean local/upstream `dae61f9561c27cb0a4d6b8e751e02dd59b5159f9`, branch `codex/v0.1.1-refresh`; read-only `git ls-remote` confirmed the same remote head. Between the recovered run's `a1e5eee3393e2ce0a0d69bde1e1f176ba67c1bde` and that baseline, only `AGENTS.md`, `docs/ai/BATTLE_TEST_AUTOMATION_SPEC.md`, and `docs/ai/BUILD_TEST_DEBUG.md` changed; no production source changed. Other worktrees were not modified.

Artifacts below are relative to the private `%TEMP%\CoopSpectator\Automation` root. No raw run data, generated probe, or installed binary is added to Git.

### 20.2 Recovered native attempt: staging succeeded, finalization did not

| Retained evidence | Observed fact |
|---|---|
| `m4sd-p1-c1/artifacts/results/contracts.json` | Full inventory: 24 passed, zero failed; historical evidence, not rerun on September 10. |
| `m4sd-p1-b1/artifacts/results/compile-only.json` | Both builds exited zero, installed inventories unchanged; dedicated output SHA-256 `74B03C00987F46AD76A6F21BAF807672FC699A402C3BE5EE5D52CB6CEC2BD4B7`. |
| `m4sd-p1-s1/self-test.json` | Path/hash rejection, junction rejection, first-file-failure restoration, and complete two-file replacement/restoration passed. |
| `m4sd-p1-s1/work/Invoke-M4SDLocalTransaction.ps1` | Retained helper hash matches `DE215D0C938421647716289DABE6458AC41250827DA1716D8AD094518D76D2DC`; helper pins the historical checkout, revision and RunIds and cannot be reused unchanged. |
| `m4sd-p1-l1-01/state/dedicated-control.ready.json` | Exact PID 11448 reports the corrected dedicated hash loaded from the dedicated module's `Win64_Shipping_Client` DLL and authoritative readiness. |
| `m4sd-p1-l1-01/state/dedicated-bootstrap.status.json` | Seven bootstrap acknowledgements, one `start_mission` request, zero end requests, no materialization observation; final status `Failed/RequestExpired`. |
| `m4sd-p1-l1-01/artifacts/processes/runtime-process-tree-snapshot.json` | Bounded collector completed in 1,872 ms, peak private memory 73,031,680 bytes, 313 records; descendant registration completed. |
| `m4sd-p1-l1-01/events/events.jsonl` | Last event: `FailureEvidenceCaptureStarted`, 2026-09-09 16:24:04 UTC; no completion marker or terminal crash/hang report. |
| `m4sd-p1-l1/manifest.json` | Parent terminal `Timeout`, exit 31; child manifest remains unfinished. Attempt 2 was not run. |
| `m4sd-p1-s1/transaction.json` | Initial restoration refused while product processes remained. Subsequently records `Restored=true` at 16:43:53 UTC and zero dedicated/client/legacy differences against 218/32-file pre-images; the earlier `RestorationFailure` text remains retained. |

Do not report automatic live cleanup success: child terminal/cleanup evidence is incomplete and restoration initially encountered live processes. The saved collector result demonstrates that process acquisition had already completed before failure publication stalled; it is not the missing terminal report. The later `RequestExpired` status does not establish the earlier runner timeout's exact triggering condition. `ProtectedResultUnchanged=false` without a completed observation is not proof of a changed result file.

September 10 read-only checks found no matching product processes or owners of UDP 7210/7777. Both installed dedicated DLLs had the original `2E1494BC...C0CA1414` hash and the client retained `2A1E17E4...250FFB4`. The diagnostic's before/after checks independently preserved those three DLL hashes. Full historical inventory restoration is supported by the retained transaction, not claimed as a fresh 250-file inventory verification.

### 20.3 Bounded diagnostic and isolated cause

Approved invocation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work/Invoke-M4FailureEvidenceProbe.ps1 -RunId m4sd-fe-d1 -CaseTimeoutSeconds 15 -MemoryLimitMiB 256
```

The ignored, one-use probe extracts only named function definitions from `scripts/Invoke-CoopTest.ps1` and `scripts/CoopAutomationRunner.Core.ps1`; neither script's top-level entry point runs. It copies the retained six-line event journal and final dedicated status into each fresh case root, uses saved owned identities as data, and supplies an explicitly synthetic empty process snapshot. Historical PIDs are never observed, attached to, registered, or terminated. Final statuses were retained after the initial failure, so this is not an exact reconstruction of every value at 16:24:04 UTC.

The first probe execution exposed a probe-only process-path acquisition race and missing cached exit-code handle. Reading completed, but the controller misclassified it; a subsequent unadmitted worker exited itself. No worker remained. Those incomplete files are preserved at `m4sd-fe-d1/`. The corrected probe used the fresh `m4sd-fe-d1/execution-02/` subtree, cached its process handle, and admitted workers only after bounded path/start-time acquisition.

Each worker has a 15-second deadline, a sampled 256-MiB private-memory stop threshold, and 4-MiB per-stream output threshold. These are polling thresholds, not hard OS memory caps: the two stopped samples overshot 256 MiB slightly. Forced cleanup revalidates exact PID/path/start ticks and waits for exit. No product process or build ran.

| Case | Windows PowerShell 5.1.26100.9444 | PowerShell 7.6.5 |
|---|---|---|
| Read status and event lines | Completed | Completed |
| Assemble failure evidence without serialization | Completed | Completed |
| Serialize role status alone | Completed | Completed |
| Serialize original event lines alone | Memory threshold reached; exact cleanup | Completed |
| Original full failure writer | Memory threshold reached; exact cleanup | Completed |
| Full evidence with empty event list | Completed | Completed |
| Full evidence with text-identical plain event strings | Completed | Completed |

Windows PowerShell 5.1 `EventsOnly` stopped at 9,693 ms / 273,723,392 bytes; `FullOriginal` stopped at 10,009 ms / 272,691,200 bytes. Both reached `AtomicSerializationStarted` but never its completion. Reading, assembly, status-only and both event-list substitutions completed in 2.3–2.7 seconds including shell startup. All seven PowerShell 7 cases completed in 1.3–1.7 seconds.

The failure is isolated to `Write-CoopRuntimeFailureEvidence` retaining the objects returned by `Get-Content -Tail 25` in `LastEvents`, then passing them to `Write-CoopJsonAtomic` / `ConvertTo-Json -Depth 30`. They are `System.String` objects with provider-added `PSPath`, `PSParentPath`, `PSChildName`, `PSDrive`, `PSProvider`, and `ReadCount` properties. In Windows PowerShell 5.1 those extra properties participate in serialization. Replacing only `LastEvents` with plain strings read through `File.ReadAllLines` completes publication; the probe explicitly checks all six texts for ordinal equality. The resulting full report is valid JSON, 12,385 bytes. This substitution is diagnostic evidence, not an approved production implementation: the probe's small-file reader does not establish a bounded production tail-reading design.

Microsoft documents that PowerShell 7.2 and later omit extended properties on `String` and `DateTime` during JSON conversion ([ConvertTo-Json](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/convertto-json?view=powershell-7.6), accessed 2026-09-10; applicable comparison: installed Windows PowerShell 5.1 versus PowerShell 7.6.5). This supports the observed shell difference; the exact local substitution tests are the primary causal evidence.

At the diagnostic baseline, the existing `Tests/CoopAutomationRunner.ContractTests/Program.cs` failure-writer case supplied missing role statuses and a nonexistent event journal. It verified failed-collector publication, but could not expose this nonempty provider-string serialization path. The collector correction remains useful; those tests did not close all finalization risks. Section 21 records the added populated-journal coverage.

### 20.4 Evidence integrity, scope and next boundary

| Artifact | SHA-256 |
|---|---|
| Ignored `work/Invoke-M4FailureEvidenceProbe.ps1` | `0FAD99AA3E49D4376A092820B655FEA585E0F9EE99D0486CF01DE59484D4D6CB` |
| `m4sd-fe-d1/execution-02/summary.json` | `F0CD6A72A09B57815256CC4A819949DAF1278C5021E3599E27D3FD75126F4AD3` |
| `m4sd-fe-d1/execution-02/inputs-before.json` | `FF2A63F37B799F6E7FF2147DE7E0EFB0FFD64ED96C7D93741603966AABB870F2` |

The summary records 12 completed cases and two bounded reproductions, unchanged selected source/retained-input/installed-DLL hashes, and zero remaining exact worker processes. Per-case phase, process identity, memory, publication and cleanup facts remain under the execution root. No production source, test project, project file, installed module, registry, game configuration or Git state changed during this diagnostic task.

The focused diagnostic objective is satisfied: a repeatable failing substep and discriminating successful substitution are identified. This is isolated diagnostic evidence, not a production fix, full contract-suite pass, new build verification, native runtime pass, or game regression pass. The original mission/materialization failure and scoreboard clear/remove lifecycle remain unverified by this task; L2 remains open.

The affected source surface is the shared runner failure writer called by `Invoke-CoopFeasibility` and `Invoke-CoopDedicatedSpawnSmokeAttempt`: dedicated and multiplayer-client failure evidence is relevant. Field dedicated evidence supplied the input. `Record` / `Invoke-CoopFixtureRecord` does not call this writer, so the current campaign-capture path is not directly affected by this particular defect. Village, siege assault, sally-out, siege ambush, relief, lords-hall, day/night hideout, sequential/reconnect and unsupported-blockade/blockade-sally-out guards have no separate writer call site; future automation using the same writer would inherit its risk. This is impact classification, not proof of a failure in each scenario. No mission, spawn, result or scenario contract changed. Native scenario/role regression was not run.

At diagnostic closure, the next independently approved implementation was to make the failure writer's event tail safe for Windows PowerShell 5.1 while retaining bounded reading, all event text, outcome evidence and exact cleanup, and add a populated-journal regression. Section 21 records that implementation. Do not repeat a native run merely to rediscover the reproduced finalization failure.

`DeployPersistent` remains specified, not implemented or verified. It is not a prerequisite for fixing this runner defect: the recovered `DeployWithRestore` transaction already proves loading the exact scoreboard-corrected dedicated candidate for a zero-client run. That does not prove updated client/server joint operation, authorize reuse of a pinned historical helper, or authorize another live run. For future staging, Revision 34's development-owned policy supersedes section 19's unconditional-old-version-restoration requirement when the approved plan explicitly selects that mode and supplies locks, exact targets, identity verification and complete-candidate repair.

Immediate documentation scope: this section owns the detailed finding; `README.md`, `BUILD_TEST_DEBUG.md`, and the specification link to it. Architecture, runtime flows, invariants, code map and test inventory are unchanged because this task introduces no production behavior or contract. This is an atomic investigation closure, not a full substage/milestone compliance audit.

## 21. Event-tail serialization correction and focused contracts (2026-09-10)

### 21.1 Implementation and acceptance boundary

The separately approved atomic correction changes only `Write-CoopRuntimeFailureEvidence` in `scripts/Invoke-CoopTest.ps1`. It retains `Get-Content -Tail 25` and copies each returned line into a new `System.String` from its character array before assigning `LastEvents`. The provider's extended properties therefore cannot expand into JSON object graphs in Windows PowerShell 5.1. Reading/encoding policy, line text/order, primary outcome, role evidence and atomic publication remain unchanged. The tail is bounded by line count; this change introduces no byte limit for an individual event line.

`Tests/CoopAutomationRunner.ContractTests/Program.cs` adds `RunFailureEvidenceContracts`, `FailureEvidenceHarnessScript`, and a `Main` selection for `--failure-evidence-only --artifacts-root <absolute directory>`. The normal runner suite also invokes these cases. The focused selection compiles the contract project but explicitly reports `IsFullSuite=false`. Retried focused executions retain earlier artifacts and use a fresh numbered subtree.

Tests extract the actual writer, shared JSON reader and atomic writer as function definitions, without running either production script's top-level entry point. Nonempty synthetic dedicated/client statuses and supplied synthetic snapshots cover publication without live process collection. Each shell worker has a 15-second deadline, sampled 256-MiB memory threshold, 4-MiB per-stream threshold, asynchronous output draining, admission by PID/path/start time and exact-identity cleanup. These are test-controller limits, not a new production resource cap.

### 21.2 Focused validation and retained evidence

The approved command used .NET SDK 10.0.102 and the existing local 8.0.23 reference/package cache:

```powershell
$fixRoot = Join-Path $env:TEMP 'CoopSpectator\Automation\m4fe-fix-c1'
dotnet run --project Tests/CoopAutomationRunner.ContractTests/CoopAutomationRunner.ContractTests.csproj -c Release --property:CoopCompileOnly=true "--property:CoopCompileOutputRoot=$fixRoot\build" "--property:RestoreConfigFile=$fixRoot\NuGet.Config" --property:NuGetAudit=false -- --failure-evidence-only --artifacts-root "$fixRoot\checks"
```

The run-owned `NuGet.Config` clears package sources and uses only the retained `m4sd-p1-c1/work/contract-build/packages` fallback. Process-only `DOTNET_CLI_HOME` and `NUGET_HTTP_CACHE_PATH` point below `$fixRoot`; telemetry, ASP.NET certificate generation, global-tool PATH changes and workload update notifications are disabled for the final invocation. Restore still attempted to read the user NuGet configuration and required sandbox access escalation; that configuration was not edited. Test build outputs, reports and logs are retained below `$fixRoot`; no client/server build or deployment target ran.

Initial restore failures and the first incomplete test execution remain retained. The first test execution passed all eight PowerShell 7 cases, but PowerShell 5.1 lacked an auto-loaded `Get-FileHash`; the controller also raced process exit while sampling memory. The in-scope harness now hashes through .NET, and the sampler accepts an observed process exit. Final execution `checks/execution-02` exited zero:

| Host | Cases passed | Total worker time | Peak sampled private bytes | Publication time | Largest JSON |
|---|---|---|---|---|---|
| Windows PowerShell 5.1.26100.9444 | 8/8 | 1,878 ms | 137,994,240 | 4–94 ms | 4,897 bytes |
| PowerShell 7.6.5 | 8/8 | 1,523 ms | 59,838,464 | 5–63 ms | 3,469 bytes |

Cases cover missing/empty journals, one Unicode line, exactly 25 lines, 40 LF lines, 40 CRLF lines without a final newline, repeat replacement after appending events, and primary Crash publication with a timed-out collector. Checks verify exact last-25 text/order including an empty line, quotes, backslashes, tabs and non-BMP characters; absent provider metadata; nonempty role-state selection; primary outcome/error retention; collector-failure retention; unchanged event-file hashes; valid small JSON; and no atomic temporary/backup leftovers. Unicode fixtures carry a UTF-8 BOM to preserve the existing cross-shell decoding policy; BOM-less decoding is not claimed newly verified.

| Retained artifact / tested source | SHA-256 |
|---|---|
| `m4fe-fix-c1/checks/execution-02/summary.json` | `4E70EBB011CC931B3E72BFE785021D6802CDCAD779D74513BE5AA69E5A1BE4D1` |
| `scripts/Invoke-CoopTest.ps1` | `C7CE7C85DEBE6F696F85D7EB63421253EC20371EB39E89E6E85C338C9550A6ED` |
| `Tests/CoopAutomationRunner.ContractTests/Program.cs` | `AF6BC67B0C20A30EFB36AFE31A5C87325949CE8238805A3BD41B031FB7470573` |

`m4fe-fix-c1/verification.json` records unchanged hashes for the three installed DLLs, all four workers from both executions absent, and no matching product process. No full installed-file inventory is claimed. Repository diff/hygiene validation is recorded with the final task result.

### 21.3 Scope, completion and next gate

Source inspection, implementation, contract-project compilation and the 16 focused regression cases are complete. Shared Feasibility dedicated/client evidence and zero-client field smoke use the corrected writer. Campaign `Record` does not. Review against `RUNTIME_FLOWS.md` found no separate writer for village, siege assault, sally out, siege ambush, relief, lords hall, day/night hideout, sequential/reconnect, or unsupported blockade variants; their mission, spawn, completion and result paths are unchanged. Native scenario/role regression is Not Run because this atomic change only copies report strings outside mission code.

At this atomic stage's publication as `5d6c4e4`, the acceptance criteria were met at source/focused-contract level. The full contract suite, client/dedicated module builds, native failure cleanup, scoreboard lifecycle and mission materialization were Not Run. The correction, focused tests and immediate safety documentation were recorded together; the source hashes above identify that tested version. Section 22 records the subsequent aggregate failure and compatibility correction. This historical boundary is not a full M4 compliance closure or an L2 pass.

The next separately approved continuation must select required validation gates and a safe native-run strategy. It must verify terminal failure evidence and cleanup in Bannerlord before relying on them to diagnose any remaining mission-opening/materialization failure. `DeployPersistent` remains a separate unimplemented requirement; the retained historical staging helper must not be reused unchanged.

Immediate documentation updates are limited to this canonical safety finding and its links in `README.md`, `BUILD_TEST_DEBUG.md` and `BATTLE_TEST_AUTOMATION_SPEC.md`. Architecture, runtime flows, invariants, code map and broader test inventory remain unchanged; their milestone audit is deferred to M4 closure.

## 22. Aggregate test invocation compatibility correction (2026-09-10)

Clean published `5d6c4e4107038662676c311629b9d856fa2ebb42` ran the full inventory as `m4fe-p1-c1`: 23 projects passed, while `CoopAutomationRunner.ContractTests` exited with an unhandled argument-validation exception before its tests started. The aggregate finished `AssertionFailed` / exit 20 and verified runner-lock release. Postflight found the runner absent, no matching contract-process candidate or product process, unchanged three installed DLL hashes, and a clean repository. The conditional module-build step did not start. Retained `artifacts/results/contracts.json` SHA-256 is `E70C374E9F91184B38BA9751A9B7DBEB562F1EA3572B8DEBD8D4EBDEFBB6B0DF` under that run root.

`Invoke-CoopContracts` already passes `--nologo` to `dotnet run`. The new `Main(string[] args)` introduced for focused selection rejected every nonempty argument list except the focused form; the previous `Main()` ignored application arguments. Microsoft documents forwarding unrecognized `dotnet run` arguments to the application ([dotnet run arguments](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-run#arguments), accessed 2026-09-10; observed SDK 10.0.102). The retained invocation and stack identify this as an introduced test-selection regression, separate from production failure-report serialization.

The correction changes only `Program.Main` in `Tests/CoopAutomationRunner.ContractTests/Program.cs`: the exact singleton `--nologo` is normalized to an empty argument list, selecting the existing normal suite. The focused argument contract and rejection of other unsupported forms remain unchanged. `scripts/Invoke-CoopTest.ps1` retains SHA-256 `C7CE7C85DEBE6F696F85D7EB63421253EC20371EB39E89E6E85C338C9550A6ED`; no game/runner production behavior changed.

Pre-publication verification used the existing process-local .NET service directories under `m4fe-p1-env`, with telemetry, certificate generation, global-tool PATH changes and workload update notifications disabled:

```powershell
$fixRoot = Join-Path $env:TEMP 'CoopSpectator\Automation\m4arg-c1'
$testArgs = @(
    '--project', 'Tests/CoopAutomationRunner.ContractTests/CoopAutomationRunner.ContractTests.csproj',
    '-c', 'Release', '--property:CoopCompileOnly=true',
    "--property:CoopCompileOutputRoot=$fixRoot\build"
)
dotnet run @testArgs --nologo
dotnet run @testArgs -- --failure-evidence-only --artifacts-root "$fixRoot\focused"
```

Both commands exited zero. The first ran all runner-project contracts, including the new failure-evidence cases and existing process/cancellation contracts in Windows PowerShell 5.1.26100.9444 and PowerShell 7.6.5. The second passed 8/8 focused cases per shell, with both workers exited. These are one project's invocation checks, not a full 24-project pass. The normal suite removes its own temporary harness tree; the command output and focused reports remain retained.

| Tested source / retained artifact | SHA-256 |
|---|---|
| `Tests/CoopAutomationRunner.ContractTests/Program.cs` | `9D6700448AF9DB35505CD675AF208D9EFE3D7B9C0685B9EB6960F372C81738DD` |
| `m4arg-c1/aggregate-invocation.log` | `68C8F5748989B1738C66FC9EB6668E1E3128ABAEBD1C92B5E6B895ABC7307C4A` |
| `m4arg-c1/focused/summary.json` | `D154F3D2301CC5A33B188CEAA5B9C3D0969405B88246385CD554F25BA0448B1A` |

The approved continuation publishes this test-only correction with its immediate documentation, then runs clean full contracts as `m4fe-p2-c1` and, only on success, both CompileOnly builds as `m4fe-p1-b1`. Their outcomes, revision identities, output hashes and installed-inventory comparisons belong to their run-scoped reports; this pre-publication section does not predict a pass. Full M4 canonical closure remains deferred.

Scenario impact remains test selection only: neither dedicated/client runtime code nor field, village, siege assault, sally out, siege ambush, relief, lords hall, day/night hideout, sequential/reconnect or unsupported-blockade guards change. No Bannerlord process or native scenario verification belongs to these invocation checks. Native failure finalization, mission materialization and L2 remain open.

## 23. Zero-client startup contract diagnosis (2026-09-10)

### 23.1 Provenance and recovered native boundary

The separately approved offline diagnostic `m4zc-d1` started at clean `ab80784398464906d2ce9eb94f568ce47ab3ab96`. Retained clean gates from that revision passed: `m4fe-p2-c1`, 24/24, and `m4fe-p1-b1`, both CompileOnly builds with unchanged installed inventories. These gates were read, not repeated. The latter produced the same dedicated `74B03C00...2BD4B7` candidate as the historical `m4sd-p1-l1-01` role. There are no changes under `GameMode/`, `Mission/`, `DedicatedServer/`, `Infrastructure/` or `Patches/` between that run's `a1e5eee` and this diagnostic baseline.

The previously uncaptured native `rgl_log_11448.txt` matches the retained role PID, launch time and RunId in its command line. Its SHA-256 is `B0D26ECF77AFC323B20100BCBFB677F436865B977E4057EC498430441ABDC885`. The native log uses local UTC+03:00; the following timestamps are UTC:

| Time on 2026-09-09 | Observation |
|---|---|
| 16:22:14.456 | Initial 16-behavior list includes the corrected scoreboard bridge, scoreboard, CoopBattle owner, lobby, timer and network bridge. |
| 16:22:17.635–17.759 | CoopBattle `AfterStart` exits; `Mission.State.Continuing` is observed; native `--Mission is running` is printed. Mode remains `StartUp`. |
| 16:22:17.952–16:43:25.477 | Repeated spawn-owner deferrals report `MissionMode=StartUp MissionTime=0.002 SynchronizedPeers=0`. The process continues ticking and making service calls. |
| 16:22:29.883 | The dedicated mission observer activates through its existing 15-second fallback despite incomplete mode readiness. |
| 16:22:30.010 | Retained battle phase is `SideSelection`; the snapshot subsequently contains 47 entries. No materialization observation or `PreBattleHold` pass is retained. |
| 16:24:04.303 / 16:29:09.196 | Old runner failure publication begins and stalls; later bootstrap status is `RequestExpired`, one start request, zero end requests and null observation. |

This is a live historical startup stall, not evidence of another scoreboard `AfterStart` crash or a mission that never finished loading. The earlier PID-15968 dump belongs to another run. No PID-11448 dump was found in the configured directory; earlier optional-assembly load messages are not established as this stall's cause. Section 20's missing primary terminal evidence remains a limitation.

### 23.2 Exact conditions and their independence

Installed ILSpy 9.1.0.7988 inspected the exact server `TaleWorlds.MountAndBlade.dll` (`C40E283E72AA90E6ED3BD64D6B8081AC7005DA76437CB3D463A12A2A9148C4EE`, 2,577,920 bytes) and `TaleWorlds.MountAndBlade.ListedServer.dll` (`C7D27584FCE431B2D3734EB88C8DF52EF3B1BC8C5729F7FCE690CC277DA577E3`, 28,160 bytes). Native decompilation is evidence for these exact binaries, not a public API guarantee.

1. **State and mode have different owners.** Native `Mission.AfterStart` assigns `CurrentState=Continuing` without setting the mode. `ServerSideIntermissionManager.StartMissionAux` waits for that state, then prints its success message; it does not establish Battle mode. The current `MissionMultiplayerCoopBattle.AfterStart`, `MissionMultiplayerCoopBattleClient.AfterStart` and minimal `MissionMultiplayerScoreboardServerBridge` provide no Battle-mode initialization. The retained stack never establishes that transition. Read-only inspection of the same exact assembly confirms that the base-client class has no `AfterStart` override, whereas native `MissionMultiplayerTeamDeathmatchClient.AfterStart` explicitly calls `SetMissionMode(Battle, true)`. The scoreboard bridge's minimal dependency correction does not supply a complete game-mode initializer.
2. **Native simulation delta has a participant condition.** `MissionState.TickMission` sets delta to zero if `GameNetwork.DoesDedicatedServerHaveAnyNetworkPeersOrBots()` is false. That predicate is `NetworkPeerCount + NumberOfBotsTeam1 + NumberOfBotsTeam2 != 0`; it does not count the fixture's expected humans. Frozen mission time is consistent with this condition, but the historical network count, both bot options and pause flags were not retained. The predicate is exact-source verified; its exclusive responsibility for the historical zero delta is **Not Verifiable** from these artifacts.
3. **The mod independently requires a synchronized peer.** `CoopMissionSpawnLogic.RunSharedServerBattleLifecycleTick` returns at `ShouldDeferBattleMapStartupRuntime` before materialization. For this ordinary field scene, the guard defers `StartUp`, mission time below 1.5 seconds, and—independently—an exact campaign scene with zero synchronized non-server peers. Its three startup exceptions are siege-specific and also require a peer. There is no admitted zero-client branch. Changing mode, time, bot counts or a timeout alone cannot satisfy the field path.

`MissionLobbyComponent.SetStatePlayingAsServer` only changes lobby state, starts a timer and broadcasts it; it does not call `SetMissionMode`. A lobby `Playing` state must not be substituted for mission-mode or `PreBattleHold` evidence.

The spawn-smoke contract project links fixture/protocol/bridge contracts and compile stubs, not native `MissionState.TickMission` or production `ShouldDeferBattleMapStartupRuntime`. The scoreboard contracts deliberately preserve a minimal bridge without an `AfterStart` override. Their passing results are valid within that scope; they did not prove zero-client mission progression.

### 23.3 Scope, retained evidence and next boundary

Private `%TEMP%\CoopSpectator\Automation\m4zc-d1` retains 25 byte-verified inputs, six selected native types, bounded decompiler process records, `diagnosis.md`, and final `verification.json`. The helper's `Capture`, `Inspect` and `Verify` modes use Windows PowerShell with `-NoProfile -ExecutionPolicy Bypass`; inspection uses `ilspycmd --disable-updatecheck -t <exact type> <exact installed assembly>`, saving stdout/stderr only below that root. The six recorded decompilations and help invocation exited zero; no deadline was reached. Additional same-assembly callee inspection was read-only stdout. `inputs.json` SHA-256 is `6551AE22F75031D30C0FDB4582F8808E16408DDF84EF41FC041AFE8A833192CC`; `native/MissionState.txt` SHA-256 is `F16B905FD2C9FF2EBBA5A9ADCB45FA9BD24DF28E49C58B4F7B779980BFB676BA`.

Affected-scenario classification: ordinary field/dedicated/zero-client has the confirmed historical stall and source mismatch. Village, sally out and relief can share general exact/CoopBattle startup conditions; no runtime pass is inferred. Siege assault has separate deployment ownership and peer-dependent exceptions; siege ambush has its own controller and mount policy. Lords hall and day/night hideout are not admitted by this field fixture and retain their separate lifecycle contracts. Sequential/reconnect gates and same-process reset remain unverified. Unsupported blockade variants remain Not Applicable. Campaign host and local/remote clients were not launched; native regression is Not Run for all supported scenarios.

The diagnostic objective is complete: it identifies the unsatisfied startup conditions and explains why the unchanged zero-client run cannot reach its intended boundary. The exact historical timeout trigger and clock inputs remain disclosed gaps. No production fix, new automated test, build, Bannerlord run, deployment, registry change or Git mutation belongs to this task. Input preservation and recorded tool exit checks belong to `verification.json`, not to a new full installed-file inventory.

The next implementation plan must separate explicit mission-mode initialization from zero-client clock/peer admission. Keep mode ownership out of the minimal scoreboard dependency bridge. A proposed admission contract must bind the exact dedicated role, admitted immutable field fixture, same mission, completed loading and pre-battle phase, while preserving ordinary clients, active-battle reinforcements, reconnect, native-only physical spawning and result suppression. Section 3's no-peer-gate-bypass boundary must be reconciled explicitly before introducing any exception; this diagnosis does not authorize one. Fake peers/agents, relaxed fixture assertions, forced battle phases and arbitrary timeout/bot-count changes are not a justified repair. Do not repeat the unchanged native attempt to rediscover these established blockers.

Immediate documentation changes are this safety finding and links in `README.md`, `BUILD_TEST_DEBUG.md` and the specification. Other living documents remain unchanged pending the separately approved implementation/substage closure. Exact local source and binary evidence answer the active question; Internet research cannot recover the missing historical runtime values. Milestone 4 and native failure-finalization verification remain open.

## 24. Field automation native mode initialization (2026-09-10)

### 24.1 Approved correction and focused acceptance

The approved atomic task addresses only section 23's missing engine-mode initializer. Its baseline is `ab80784398464906d2ce9eb94f568ce47ab3ab96` with the four diagnostic documentation changes already present. Those changes were preserved; this implementation remains uncommitted. No native run, deployment, full 24-project aggregate or Git mutation was included in this approval.

The composition and ownership contract has its canonical home in [RUNTIME_FLOWS.md](RUNTIME_FLOWS.md#field-automation-native-mode-initialization-2026-09-10-source-contract). Implementation is confined to `MissionMultiplayerCoopBattleMode.BuildServerMissionBehaviorsForCoopBattle`, `CoopAutomationSpawnSmokeBridge.ClaimNativeModeInitialization`, its `ObserveInitialized`/`Reset` integration, and the focused test program. Native `BattleMissionStarterLogic.AfterStart` was inspected in both exact reference profiles: server `TaleWorlds.MountAndBlade.dll` SHA-256 `C40E283E72AA90E6ED3BD64D6B8081AC7005DA76437CB3D463A12A2A9148C4EE` and client `19387F31557FF840D14F378F6BBDF1D58FCFF406AB9FF2294DFBB3E49A50B87E`. Both provide the same `SetMissionMode(Battle, true)` implementation. This exact-binary evidence required no Internet workaround or reference-profile changes.

| Focused criterion | Evidence and result |
|---|---|
| One initializer immediately after the CoopBattle owner, only for the admitted dedicated field mission | Source inspection confirms the single insertion and later server filtering retains it; both reference-profile builds pass. Native callback execution is Not Run. |
| Disabled or absent profile adds nothing; invalid requested role/profile/scene/opening is rejected | `ValidateBridge` and `ValidateNativeModeInitialization` pass default-off, absent-profile, unadmitted, unopened, wrong-role, null-mission, wrong-scene and changed-profile cases. |
| Duplicate/replacement claims fail closed; later observer binds the exact mission | Contract cases pass duplicate and replacement factory rejection, no premature binding, missing-claim rejection, mismatched observer rejection and exact binding. |
| Reset permits a fresh claim and result isolation remains intact | Re-admission after reset passes; ended/initialized missions reject another claim; protected local result and existing early-abort suppression checks pass. This is shared-state contract evidence, not a same-process Bannerlord reset proof. |
| Keep cooperative phase, clock, peer gates and native spawning unchanged | The source diff adds no phase assignment, simulation-time override, bot-option mutation, peer exception or physical-agent creator. |

These criteria are satisfied at their approved source/contract/build levels. They do not close M4 or establish a working zero-client battle.

### 24.2 Validation and retained identity

`m4mode-c1` ran `dotnet run --project Tests/CoopAutomationSpawnSmoke.ContractTests/CoopAutomationSpawnSmoke.ContractTests.csproj -c Release --property:CoopCompileOnly=true --property:CoopCompileOutputRoot=<run-root>/build`. The initial sandbox attempt could not read the existing user NuGet configuration and stopped before testing; the authorized access retry passed **141 assertions**, plus the existing runner harness in Windows PowerShell **5.1.26100.9444** and PowerShell **7.6.5**. The contract executable launched no product process and removed its own fresh fixture directory. Initial and retry output are retained separately in `m4mode-env/contracts.log` and `contracts-retry.log`.

`m4mode-b1` ran the approved `scripts/Invoke-CoopTest.ps1 -Command CompileOnly` with the explicit installed client/server roots. Both Release projects compiled into run-owned output: client **77 warnings, 0 errors**; dedicated **49 warnings, 0 errors**. The runner reports `ProductProcessLaunched=false` and `InstalledInventoriesUnchanged=true` for client, legacy-client and dedicated inventories. Deployment was disabled. These are builds of the modified working tree, not of a new clean published revision.

| Build artifact | SHA-256 |
|---|---|
| Client `CoopSpectator.dll` | `33E47A22F3D283DB290775725B02986EAAF5732BC4C32FA8467946C067E8E2E6` |
| Dedicated `CoopSpectator.dll` | `EDF9EACD88C7FFCB562FEF2FD9ED0EB18D7A3A1CD404A9A1308B83CC2667EA68` |
| `m4mode-b1/artifacts/results/compile-only.json` | `F6EB7BAC3D24F11796995BA1A610CDC711114E13564EDBDA9CE50E984B917145` |

Private `%TEMP%/CoopSpectator/Automation/m4mode-env` retains the pre-edit file copies/hashes, validation logs and final verification summary. Build outputs, command identities, installed inventories and logs remain under `m4mode-b1`; focused build/restore outputs remain under `m4mode-c1`. No generated artifact is added to Git.

### 24.3 Scenario coverage and remaining boundary

Only the admitted ordinary-field dedicated test profile receives the new component. Ordinary non-automation field, village, siege assault with deployment, sally out, siege ambush, relief, lords hall, day hideout assault and night hideout ambush receive no new initializer from this change: the profile/fixture/scene contract excludes them, and specialized paths retain their owners. Campaign host and local/remote clients receive no new component. Reinforcements and reconnect do not use this initial claim; replacement/duplicate missions reject it, while reset/re-admission is contract tested. Unsupported blockade and blockade-sally-out guards are unchanged and Not Applicable to this field task. Native regression for every supported scenario and role is **Not Run**.

Source inspection and implementation are complete; focused automated contracts and both module builds are verified. Runtime execution in Bannerlord, native mode callbacks, native sequential reset, failure-finalization regression and field materialization are **not runtime verified**. Calling the native mode setter can dispatch engine callbacks; compilation cannot establish their runtime safety. The independent mission-clock and synchronized-peer conditions from section 23 remain unresolved and require a separate approved plan before any zero-client exception. No peer-gate bypass, fake participant, forced cooperative battle phase or arbitrary bot/timeout change belongs to this correction.

Immediate documentation updates are limited to this evidence section and the mode-ownership contract in `RUNTIME_FLOWS.md`. The pre-existing diagnostic text is retained. `README.md`, `BUILD_TEST_DEBUG.md` and `BATTLE_TEST_AUTOMATION_SPEC.md` remain byte-for-byte unchanged from this task's baseline. Full requirement/scenario auditing and canonical documentation closure remain deferred to the approved M4 milestone boundary.

## 25. Zero-client clock and initial peer admission (2026-09-10)

### 25.1 Approved scope and implementation evidence

This separately approved atomic task follows the mode-only correction in section 24. It starts from `ab80784398464906d2ce9eb94f568ce47ab3ab96` plus the retained uncommitted mode/diagnostic changes. Its objective is source/contract/build proof of one shared zero-client pre-battle admission decision used at two participant checks. The [canonical admission, ownership, lifetime and failure contract](RUNTIME_FLOWS.md#field-automation-zero-client-pre-battle-admission-2026-09-10-source-contract) records the implementation details; this section owns validation evidence and limits.

Changed production locations are `CoopAutomationSpawnSmokeBridge.CanUseZeroClientPreBattle`, new `CoopAutomationZeroClientRuntime` and `CoopAutomationZeroClientClockPatch`, the final peer branch of `CoopMissionSpawnLogic.ShouldDeferBattleMapStartupRuntime`, dedicated control `TryAcceptRequest`/`FailActiveRequest`/`Shutdown`, observer `Tick`/`Fail`, and the dedicated project includes. The client project already includes the shared paths. Installation is explicit after fixture admission and before native bootstrap commands, not attribute-driven `PatchAll`. Server session state is required when applying the runtime exception, not before `start_game` during patch installation.

Read-only inspection confirmed that `TryResolveExactCampaignBootstrapSide` already has deterministic side fallback, and `HaveAllEligiblePeersAcknowledgedCurrentBattleSnapshot` already accepts zero eligible peers. The native bootstrap/materializer, player-side selection, reinforcements, normal phase transitions and result writer were not changed. The peer exception retains the prior mode/time checks and the existing snapshot-readiness check. It is the explicit narrow exception to the former blanket no-peer-gate-bypass wording; production and active-battle requirements remain protected.

| Focused acceptance criterion | Evidence / disposition |
|---|---|
| Same admitted field mission, dedicated role, native readiness, exact snapshot header, no connected clients and allowed pre-battle phase | Shared bridge and runtime-adapter contracts passed; wrong role/session/current mission/scene/mode/state/phase, absent or mismatched snapshot, incomplete sides/troops and connected unsynchronized client are rejected. |
| Preserve ordinary native time and real pause | The production transpiler preserves the native predicate and original instructions. Executable synthetic IL covers all 16 combinations of admission, native participant result, pause and fixed-delta mode. Original real/fixed delta and zero during pause are preserved. |
| Change only the identified native call site; reject unknown/repeated transformations | Exact installed method metadata passes the production transpiler. Synthetic missing/duplicate predicate, inverted branch, nonzero assignment, invalid store, wrong branch target and repeated-transformation cases are rejected. |
| Failure/end/reset withdraw the exception; patch ownership remains isolated | Admission negatives cover failure/end/reset/replacement and active/unknown phases. Real Harmony registration, phase denial, removal and reinstallation passed against the native-free `MissionState` stand-in. Production driver/observer/shutdown integration was source-inspected and compiled. |
| Retain native-only spawning, ordinary readiness, fixture assertions and suppressed result protection | Focused contracts, final source diff and unchanged result/fixture code support this source obligation. Real materialization, callback safety and abort disposal remain Not Run. |

The focused criteria are met at the approved evidence levels. This is not a full M4 requirement audit or runtime completion claim.

### 25.2 Focused contracts, native metadata and module builds

`m4zcfix-c1` ran the existing spawn-smoke contract project in Release with `CoopCompileOnly=true` and run-owned outputs. `COOPSPECTATOR_CONTRACT_NATIVE_CLOCK_ASSEMBLY` explicitly selected the installed dedicated `TaleWorlds.MountAndBlade.dll`. The test reads PE/CLR metadata only, decodes `MissionState.TickMission`, maps the exact native predicate to its test stand-in, passes the instruction list through the production transpiler and checks preservation. It does not load the game assembly for execution or run native mission callbacks. The selected native SHA-256 before and after inspection is `C40E283E72AA90E6ED3BD64D6B8081AC7005DA76437CB3D463A12A2A9148C4EE`.

The focused run passed **432 assertions**, plus the existing runner checks in Windows PowerShell **5.1.26100.9444** and PowerShell **7.6.5**. `Lib.Harmony` **2.4.2** matches the existing module dependency and is used only in the test process for synthetic/stand-in verification. The fresh fixture directory is self-cleaned; logs, build/restore outputs and environment caches remain in the approved private run directories. `m4zcfix-env/contracts-01.log` retains the successful output.

`m4zcfix-b1` ran the approved `scripts/Invoke-CoopTest.ps1 -Command CompileOnly` against explicit installed roots. Client Release passed with **77 warnings, 0 errors**; dedicated Release passed with **49 warnings, 0 errors**. The complete before/after inventories contain **32 client**, **0 legacy-client**, and **218 dedicated** files; the runner reports `InstalledInventoriesUnchanged=true` and `ProductProcessLaunched=false`. No installed candidate was staged. These outputs correspond to the modified working tree, not a new clean published commit.

| Artifact | SHA-256 |
|---|---|
| Client `CoopSpectator.dll` | `8EF59F3D9DCF960DB37BC14D06F058A4E857D6D78588428F0835FEF623F078E5` |
| Dedicated `CoopSpectator.dll` | `E384AC876684C7E986FAD1031A423F2A8657B7421EE372096F038C72404E394C` |
| `m4zcfix-b1/artifacts/results/compile-only.json` | `694DC1EFDEDA78EFB329673EFA667984E26A78E7D6EA42DE2B09F02B7CC787E2` |

Private `%TEMP%/CoopSpectator/Automation/m4zcfix-env` retains the 15 pre-edit file copies/hashes, Git baseline, contract/build logs and final `verification.json`. `m4zcfix-c1` contains contract build/restore outputs; `m4zcfix-b1` contains both module builds, exact command identities, installed inventories and runner results. `git diff --check` and focused final scope/identity checks are recorded in the final summary. No generated artifact, staging, commit, push or Git history change belongs to this task.

### 25.3 Scenario/role coverage and remaining verification

Only the exact ordinary-field dedicated automation profile receives the exception. Ordinary non-automation field, village, siege assault with deployment, sally out, siege ambush, relief, lords hall, day hideout assault and night hideout ambush remain outside its profile/scene/scenario admission. Campaign host and local/remote clients receive no installed clock patch. Active-battle reinforcements and reconnect/replacement paths receive no zero-client exception; pre-battle reset/re-admission is contract tested. Unsupported blockade and blockade-sally-out are Not Applicable. Native regression is **Not Run** for every supported scenario and role; no scenario runtime pass is inferred from the shared contract.

Source inspection, implementation, focused automated contracts and both module builds are verified. Real engine patch installation, mode/clock callbacks, native agent materialization, early-abort cleanup, native same-process sequential reset and failure-finalization regression remain **not runtime verified**. Exact historical clock inputs from section 23 remain unknown. The synthetic/metadata evidence proves neither a live zero-client pass nor complete battle stability. Any further live run needs a separately approved deployment/run/cleanup plan and appropriate source/artifact provenance; the full 24-project aggregate was not repeated here. Milestone 4 remains open.

Immediate documentation updates are limited to the runtime contract, its protected-risk/specification references and this evidence section. Prior sections are retained as dated evidence. The earlier mode factory, `README.md` and `BUILD_TEST_DEBUG.md` are unchanged from this task's baseline. Broader canonical documentation closure and the full requirement-by-requirement audit remain deferred to the approved M4 milestone boundary.

## 26. Clean zero-client live attempt stops before bootstrap (2026-09-10)

### 26.1 Approved scope and clean gates

The user approved publication of the 17 completed mode/clock/peer implementation and safety-documentation files, fresh clean Contracts/CompileOnly gates, one private DeployWithRestore transaction, one public zero-client smoke invocation, exact cleanup/restoration and a separate documentation-only outcome commit. Source revision is **438b434e5f144af2d407fea7f454391c530d16b0**, published to the existing origin/codex/v0.1.1-refresh branch. All pre-existing implementation files matched the prior retained verification before publication. No new production logic, test relaxation, dependency installation, Git history rewrite or persistent deployment was performed.

- **m4zclive-c1:** 24/24 contract projects passed from the clean published revision; the focused spawn-smoke suite includes 432 assertions and metadata-only inspection of exact installed clock IL. This inspection executes no engine method.
- **m4zclive-b1:** both Release CompileOnly builds passed, client 77 warnings / 0 errors, dedicated 49 warnings / 0 errors. Complete installed inventories remained unchanged; outputs stayed under the run root.
- **m4zclive-s1:** the private work/Invoke-M4ZeroClientLiveTransaction.ps1 passed six self-test groups: invalid path/source/current hash rejection; reparse rejection; injected first-file failure restoration; two-file restoration; bounded hidden child success/timeout cleanup; and missing-dump restoration with foreign-content refusal. Only synthetic files and owned PowerShell processes were used in SelfTest.

| Retained identity | SHA-256 |
|---|---|
| Full contract result | 5CE0ABCBB66ADC12B6376E904791A7FCF3E01AF884CF4CA19CDB84F5BF2CC134 |
| CompileOnly result | 5C05F4754CE0BC1A22D974C8343914286AE16E728317D70E648637CB4146C434 |
| Before/after installed inventory | B817EEA278FFFC398A61E0FD92C65EB07314E0A7474D11FC417492C39B8B1E6B |
| Client output (not installed) | 6E98C0D8BEE84FC3E6DE60579CCAA3E9FBF794D67F37F0EEC67F8A822FE3ABBE |
| Dedicated output / loaded candidate | E384AC876684C7E986FAD1031A423F2A8657B7421EE372096F038C72404E394C |
| Private transaction helper | 1FA6FB5B2716BEB0EF7FCAD93EB3AFA73C8B4890BC3CEB5BA69A0563DDB18B9C |
| Immutable private inputs | 0437CF0C8A62D2523D547F149681F1EC02F1AB20CE51A1331874FF0098C16C3A |
| Final transaction record | 3FD46D66C82C8D4CDDBAF8256BD25F8697044FEDBA0369A0875E7FD1B334DBAF |
| Child attempt result | D2C95A4E43D01D2D9C38375F70C2F076075DB90D565C374F1F1F63135399BB33 |
| Timeout artifact | C6BF88416DA165935B3D2FBD0019E3668D879B34EEE1BC53379202B9E424A09D |

### 26.2 Transaction and native result

Artifacts remain below the private %TEMP%\CoopSpectator\Automation root. The helper binds the exact revision, clean local/upstream state, passing reports, native binaries, original module identities and input hashes. It uses bounded lightweight process inspection, rejects occupied ports and reparse paths, acquires the six installation/profile/port resources, and backs up the complete 218-file dedicated module before replacing only its server-bin and client-bin CoopSpectator.dll with the dedicated candidate. The installed client module is not staged.

The existing Windows LocalDumps configuration was read and preserved: the dedicated starter uses C:\dumps, full dumps and a count limit of five. All five existing dumps (PIDs 101340, 101980, 150788, 15968 and 162752) were hashed and copied before native launch. This precaution follows Microsoft's documented oldest-dump replacement when the count limit is exceeded: [Collecting user-mode dumps](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps), accessed 2026-09-10, applied to this Windows LocalDumps configuration. No registry setting changed.

The public command used scripts/Invoke-CoopTest.ps1 with Command=DedicatedSpawnSmoke, RunId=m4zclive-l1, the verified GameRoot and DedicatedServerRoot, Port=7210, RuntimeTimeoutSeconds=420 and ExpectedDedicatedModuleSha256 equal to the dedicated hash above. It ran through Windows PowerShell with -NoProfile -ExecutionPolicy Bypass -File. The helper additionally bounds its public child to 1800 seconds. Private attempt flags were not invoked directly. No campaign or multiplayer client was launched.

Child **m4zclive-l1-01**, dedicated PID **32980**, process start **2026-09-10T08:03:51.0878710Z**, loaded the exact candidate from the dedicated module's Win64_Shipping_Client bin. Native watchdog evidence identifies **v1.4.8.118999**. The initial retained role status is ModuleReady at **08:04:02.5094347Z**. The runner then waited for authoritative dedicated control readiness, returned **Timeout / NoHeartbeat / exit 31**, and produced no bootstrap request, acknowledgements, start_game, start_mission or mission observation. Parent exit is also 31. The public pair report contains one exited child runner; the second child root was never created. The two-attempt acceptance is not satisfied.

The later final status and hang.json contain WaitingForDedicatedReady, state revision 2, last progress **08:04:04.0790096Z** and heartbeat **08:04:22.2308984Z**. The failure artifact was captured at **08:04:22.8562699Z**. Native logs show asset loading and successful backend alive messages through this period. These later observations do not identify the input used at the failing health decision.

Source inspection locates the evidence gap in Assert-CoopRuntimeRoleHealth, Get-CoopRoleHealthClassificationCore and Read-CoopJsonShared: a missing status or any swallowed file-read/JSON error becomes null and therefore NoHeartbeat; a timestamp older than five seconds receives the same classification. The assertion attaches a non-null rejected status to its exception, but the spawn-smoke catch retains only outcome/code/message; failure publication rereads role files later. The exact rejected snapshot, read failure and classifier time are not in the retained result. **Transient unreadability versus a real heartbeat gap is unresolved; neither a dead server nor a proven false timeout may be asserted.**

The native error log also retains Cannot load warnings for ASP.NET dependencies and 0Harmony.dll. The candidate role initialized and continued publishing health, so these lines alone do not establish the cause of this timeout. No dependency repair, file copying workaround or new native run was attempted.

### 26.3 Automatic failure handling and restoration

The bounded process collector completed in **1655 ms**, with **72,617,984 bytes** peak private memory and 335 lightweight records. FailureEvidenceCaptureStarted / Completed occurred at **08:04:22.7444378Z / 08:04:22.8592854Z**. RuntimeCleanupStarted / Completed occurred at **08:04:22.8612788Z / 08:04:40.9509489Z**; child terminal publication completed at **08:04:51.5504292Z**. The original Timeout outcome was preserved without cleanup supersession.

The dedicated process accepted graceful closure (IdentityMatched=true, ForcedStopUsed=false, Outcome=Stopped). Its Watchdog, console support and bounded collector were already stopped when checked. No fatal helper was correlated, no new native dump appeared, and no manual cancellation, recovery or forced termination was needed. This proves the automatic **timeout** path for this exact run; it does not prove native-crash finalization or in-mission cleanup.

The child reports no remaining owned processes or required ports, and its six shared-lock release/reacquisition probes pass. Parent and child runner-lock probes also pass. The transaction reacquired the installation resources, restored both original DLLs (SHA-256 **2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414**) and original timestamps, and compared all **218 dedicated / 32 client / 0 legacy** files without differences. All five dump originals and their backups passed hash verification; no original was rotated away. The run-local 69-byte result sentinel remained unchanged; no actual mission result suppression was exercised because no mission opened. Personal Documents/campaign results were not accessed or claimed measured.

Transaction completion is **08:05:32.5044362Z**, with Restored=true, DumpsRestored=true, no restoration error and the original live failure retained. Independent m4zclive-s1/verification.json confirms the original DLL/dump hashes, zero product processes, zero required-port owners, no second attempt and no new dump. Raw reports/logs, inputs, helper and backups remain private; no temporary diagnostic code was added to production.

### 26.4 Focused requirement audit and coverage

| Approved requirement | Implementation / evidence | Status |
|---|---|---|
| Publish only the completed approved change set | Exact 17-file hash baseline, reviewed diff, LF/hygiene checks, clean published 438b434 | Satisfied |
| Fresh full contracts and non-deploying builds | m4zclive-c1, 24/24; m4zclive-b1, both builds; unchanged installed inventory | Satisfied |
| Validate the private installation/rollback tool | Exact helper and inputs; six SelfTest groups | Satisfied |
| Load the exact dedicated candidate | PID/start/path-bound ModuleReady and matching module SHA-256 | Satisfied |
| Open the field mission and reach PreBattleHold | No bootstrap request or mission; first attempt Timeout | Not Satisfied |
| Verify 47 entries, 74 humans, 21 mounts and exact identities/equipment | Native observation was never reached | Not Verifiable |
| One normal mission end, disposal and actual result suppression | Mission never opened; sentinel preservation alone is insufficient | Not Verifiable |
| Two successful fresh-process attempts | First attempt failed; second correctly blocked | Not Satisfied |
| Preserve failure evidence and clean exact processes/resources | Complete hang artifact, graceful dedicated stop, no residual owner/port, eight runner/shared release probes | Satisfied |
| Restore installed state and protect prior dumps | Full 218/32/0 inventories; two original DLLs; five original/backup dump hashes | Satisfied |
| Preserve original failure and stop at the approved boundary | Exit 31 retained, no retry or source/dependency repair | Satisfied |
| Record outcome, limits and next action in living docs | This section plus focused README/build/spec/flow/risk updates | Satisfied |

Source inspection, implementation publication, build verification and automated/contract verification are **complete at their stated levels**. Runtime verification was executed and **failed before the mission**. Automatic timeout cleanup and installation restoration passed. Native mode/clock/peer progression and battle regression remain unverified. Mandatory live requirements above prevent closure of this validation substage and of Milestone 4.

| Scenario / role | Native status in this invocation |
|---|---|
| Ordinary field, dedicated zero-client profile | Failed — control-readiness boundary, no mission opened |
| Village battle | Not Run |
| Siege assault with deployment | Not Run |
| Sally out | Not Run |
| Siege ambush | Not Run |
| Relief battle | Not Run |
| Lords hall stage | Not Run |
| Day hideout assault | Not Run |
| Night hideout ambush | Not Run |
| Sequential missions in one process / reconnect | Not Run |
| Second fresh-process field attempt | Not Run — blocked by first failure |
| Unsupported blockade / blockade-sally-out | Not Applicable — unsupported, not admitted |
| Campaign host / local or remote mission clients | Not Applicable to this zero-client invocation; no runtime regression claim |

### 26.5 Next boundary and documentation ownership

No unchanged native rerun is justified by this result. The next separately approved investigation must distinguish missing/read-failed/stale role health and preserve the exact failing decision before proposing a correction. Review shared dedicated/client consumers without expanding into battle adapters, altering the five-second guard, accepting an unverified process or relaxing the field fixture. Exact retained local code and artifacts answer the present evidence question; Internet research cannot recover the overwritten historical read.

This approved outcome update reread and changed README.md, BUILD_TEST_DEBUG.md, BATTLE_TEST_AUTOMATION_SPEC.md, RUNTIME_FLOWS.md, INVARIANTS_AND_RISKS.md and this M4 report. Architecture/component location did not change, so ARCHITECTURE.md and CODE_MAP.md are intentionally unchanged. Earlier numbered reports remain historical evidence. This section owns exact run outcomes; other living documents link to it. The documentation-only publication follows the implementation commit and completed safe restoration, without claiming M4 completion.

## 27. Exact role-health rejection source and contract correction (2026-09-10)

### 27.1 Scope and evidence boundary

The approved atomic task starts from clean `da4623037c283524baecab514439700c0139ccaa` and changes only `scripts/Invoke-CoopTest.ps1`, `scripts/CoopAutomationRunner.Core.ps1`, `Tests/CoopAutomationRunner.ContractTests/Program.cs` and the three immediately relevant living documents. The verified defect is loss of the rejected read/decision during failure finalization. The cause of the historical section-26 `NoHeartbeat` is still unknown. Exact local source and retained artifacts answer this reporting question; external research cannot recover that overwritten observation.

The reader now optionally records the actual read result; the assertion captures its decision time and a detached bounded projection; both Feasibility and DedicatedSpawnSmoke retain it through their catch and writer call. Later role-file snapshots remain separate. The [canonical evidence contract](BUILD_TEST_DEBUG.md#exact-role-health-rejection-evidence-2026-09-10) owns field semantics, activation, cost and limits. No classifier rule, heartbeat/progress threshold, readiness acceptance, native module, deployment, fixture, spawning, result suppression or cleanup ownership changed. There is no retry, fallback acceptance or new product diagnostic flag.

### 27.2 Validation and retained artifacts

- **Source inspection:** reader, classifier, both consumers and the existing finalizer reviewed. Native atomic status writing and shared scenario/role impact were classified without changing game code.
- **Focused contracts:** final `m4rh-e1/artifacts/execution-03` passed **42 cases in Windows PowerShell 5.1.26100.9444 and 42 in PowerShell 7.6.5**. Production reader/assertion/helper/writer definitions and both consumer catch/writer call sites are extracted from the AST, without invoking native startup. Both hosts cover fresh, missing/not-visible, empty, JSON null, malformed, locked, stale, stalled-progress, invalid schema/timeline/identity/capability cases; healthy overwrite after rejection; exact five-second boundary; detached/bounded projection; and primary timeout preservation if projection fails. Existing event-tail, collector-failure and crash/timeout publication cases also pass. An initial optional-reference binding failure was corrected in scope; its failed artifacts and the intermediate passing run remain retained.
- **Bounds and cleanup:** final focused workers completed in 3,926 / 3,632 ms, with peak private memory 152,092,672 / 111,439,872 bytes, below their unchanged 15-second / 256-MiB limits. Both exited normally with code 0; no forced stop or product process was used.
- **Full contracts:** `m4rh-c1` passed **24/24 projects**, including both runner shells, actual console-cancellation contracts and the existing **432 spawn-smoke assertions** with metadata-only installed clock IL inspection. The aggregate runner lock was released and independently reacquired. Validation used the approved working changes on the baseline revision; it is not a clean post-commit native run.
- **Build evidence:** the .NET 8 contract executable compiled through the selected .NET SDK and isolated `CoopCompileOnly` output/package roots. Main client/dedicated builds were **Not Run** because no compiled product source changed. Installed modules were not deployed and Bannerlord was not launched. Runtime and native scenario regression are **Not Run**.

Raw artifacts remain under private `%TEMP%/CoopSpectator/Automation`: `m4rh-e1` holds focused output and retained executions, `m4rh-c1` holds full contracts, and `m4rh-env` holds isolated CLI/cache state. Test-owned disposable directories remain governed by the existing harness cleanup. No raw logs or generated build outputs enter Git.

| Retained evidence / validated source | SHA-256 |
|---|---|
| `m4rh-e1/artifacts/execution-03/summary.json` | `264FB6ED6F37BACF8F7AD5C5F48490F44B8A0AB120964E7FF3114A577F3C9A92` |
| Focused Windows PowerShell `cases.json` | `BF167ABB1D6DE13411185FC50B31342FE4894A152BDEC38A7B3E3380821FE3E1` |
| Focused PowerShell 7 `cases.json` | `97AAC17DDA11D55E4C222232DFC627D82B620E4C9277BF8A38F14233A3ECD511` |
| `m4rh-c1/artifacts/results/contracts.json` | `C201F025A235A093F3A4AC286F10C8E1001A447FFC0784350E19A8181FCA31FF` |
| `scripts/Invoke-CoopTest.ps1` | `BC05352102EE9B7C3A5D785AF9AB44DC2549FC85180CD1A31C4C72BFA82B5776` |
| `scripts/CoopAutomationRunner.Core.ps1` | `32920BB7644FC23F5954D898DF3D2D14D1517DA515DC23AF55BE564FF1B799D7` |
| `Tests/CoopAutomationRunner.ContractTests/Program.cs` | `1A9C5B68692B7211CF990ABE3285399DB3713E0774E6EA1CCDCF8BB2306C1DD4` |

### 27.3 Focused acceptance and scenario coverage

| Atomic acceptance criterion | Evidence | Status |
|---|---|---|
| Preserve the actual rejected read and decision despite a later healthy file | Real reader/assertion/finalizer contracts, both roles and shells | Satisfied |
| Preserve existing acceptance, identity rejection and deadlines | Fresh/stale/boundary/progress/schema/timeline/capability/identity cases | Satisfied |
| Bound and detach added diagnostics; preserve primary failure | Fixed scalar projection, graph/oversize/mutation and projection-failure tests | Satisfied |
| Propagate evidence through both consumers; retain existing publication regression | Real catch/writer call extraction plus full 24/24 | Satisfied |

Field zero-client dedicated smoke and dedicated/client Feasibility share the changed diagnostic path: source and contracts **Passed**, native runtime **Not Run**. Village, siege assault with deployment, sally out, siege ambush, relief, lords hall, day hideout and night hideout have no adapter changes; native regression for each is **Not Run**, with only shared diagnostic applicability established. Sequential missions/reconnect retain run-local evidence without a new cache; live regression is **Not Run**. Campaign-host fixture Record uses a separate wait/catch corridor and is **Not Applicable**. Unsupported blockade and blockade-sally-out are **Not Applicable**; their guards remain unchanged.

### 27.4 Documentation and stopping condition

This closes the approved reporting correction at source/contract level. It does **not** close M4 or verify a battle in Bannerlord. A subsequent native attempt requires its own exact plan; do not infer that the historical server was dead, that the timeout was false, or that changing a timeout is justified.

Immediate documentation is limited to this focused evidence section, the canonical field contract in BUILD_TEST_DEBUG.md and the preservation invariant in INVARIANTS_AND_RISKS.md. The approved publication groups these safety/contract documents with the three implementation/test files in one atomic commit. README.md, the active specification, RUNTIME_FLOWS.md, ARCHITECTURE.md and CODE_MAP.md remain unchanged: no milestone closure, native flow, ownership or component-location change is claimed. Earlier sections remain historical evidence; broader canonical closure is deferred to the M4 substage boundary.
