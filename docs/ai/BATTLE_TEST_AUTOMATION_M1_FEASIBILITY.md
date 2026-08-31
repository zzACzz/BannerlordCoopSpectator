# Battle Test Automation Milestone 1 Feasibility Report

Status: **Complete with blockers**

Run date: **2026-08-31**

Run ID: **`M1-20260831T013501Z-4114d2df`**

Repository revision: **`3c513084ebbe9c99daa0b65849fab7b39b913ee1`** (`3c51308`)

Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), working-tree Revision 5

Evidence root: `C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\M1-20260831T013501Z-4114d2df`

## 1. Verdict

Milestone 1 is complete as a feasibility investigation, but it is not a positive runtime-automation gate.

The installed dedicated and multiplayer client roles can be launched, identified, observed, and stopped through exact process ownership on the named machine profile. Steam is a confirmed prerequisite for the client. The installed modules load successfully, but they are version `0.3.1`; the repository outputs are version `0.3.2` and have different hashes. This run therefore proves only the explicit `UseExistingInstalled` diagnostic mode and makes no source-equivalence claim.

The developer reports that version `0.3.2` was released after stable manual battle runs. That is relevant manual regression evidence for the release decision, but it was not generated or correlated by this bounded M1 probe. The mismatch statement above means only that M1 observed locally loaded `0.3.1` binaries and therefore cannot use its own launch artifacts to prove the repository's `0.3.2` binaries; it does not assert that `0.3.2` is unstable.

Two essential capabilities are `Blocked`:

1. a real client connection to the owned server was not safe to attempt before result isolation;
2. at the time of the M1 run, source did not expose a supported, run-scoped command and acknowledgement path that drove the normal lobby join flow without manual UI interaction.

Milestone 2A may proceed because it is non-runtime foundation work. Milestone 2B and all L2–L5 runtime work remain blocked until an approved revised gate plan resolves the connection/control blockers and proves current-build identity.

A post-M1 source implementation now provides the narrow run-scoped launch/join intent described in Revision 5 and [BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md). Its contract tests do not change either `Blocked` runtime capability row; a connection-only rerun remains required after current-build staging and result isolation are safe.

Nothing in this report is L2, L3, battle-completion, result-correctness, or campaign-writeback evidence.

## 2. Safety boundary

The investigation deliberately did not:

- build or deploy either module;
- write into either installed module tree;
- pass a startup configuration file to the dedicated server;
- issue `start_game` or any mission-open command;
- attempt client connection, UI automation, or campaign startup;
- use the client shader-cache helper;
- read the dedicated authentication token contents;
- terminate processes by name or touch unrelated processes;
- stage, commit, or push repository changes.

The existing global battle bridge files were hashed before and after the runtime probes. `battle_roster.json`, `battle_result.json`, `battle_phase_status.txt`, `battle_phase_start.request`, `battle_runtime_bundle.txt`, and `exact_battle_runtime_bundle.txt` were unchanged.

Normal product launch side effects did occur outside the repository:

- Bannerlord created one 70-byte shader-cache file;
- the shared Bannerlord log directory was updated and rotated/truncated during the probes;
- the existing configuration directory's newest-write time changed while its file count and total size remained unchanged.

These side effects are evidence that a real client launch is not a filesystem-pure operation even when the project build and helper scripts are not used.

## 3. Named machine and version profile

Profile name: **`M1-LAPTOP-4IUGGR23-PUBLIC-2026-08-31`**

| Property | Recorded value |
|---|---|
| Machine / user | `LAPTOP-4IUGGR23` / `Admin` |
| OS | `Microsoft Windows NT 10.0.26200.0` |
| PowerShell | `7.6.4` |
| Session | Interactive session `34` |
| Client Steam app | `261550`, public build ID `24573425` |
| Dedicated Steam app | `1863440`, public build ID `24571419` |
| Client runtime log build | `119303` |
| Client root | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` |
| Dedicated root | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server` |
| Bridge documents root | `C:\Users\Admin\OneDrive\Documents\Mount and Blade II Bannerlord\CoopSpectator` |
| Ports inventoried | TCP/UDP `7210` and `7777` |
| Automation flags | Automation and verbose diagnostic flags unset; campaign-map prototype enabled by the existing environment |

The dedicated token file existed in the resolved OneDrive Documents profile. Only its path, size, and timestamp were recorded; content was not read or copied.

## 4. Binary identity

| Role | Repository output | Installed/loaded runtime | Result |
|---|---|---|---|
| Client | `0.3.2`, SHA-256 `3B273659948D6F58459655D363F6AEC100BDD69C62A7C1095E144A2F75A1039F` | `0.3.1`, SHA-256 `9B271E4E0CFA3AD0FF2DB4B3ACC5A69AE6405E833D52ECB4E1A4C0FDCA8C1B31` | Mismatch; installed-profile diagnosis only |
| Dedicated | `0.3.2`, SHA-256 `29E8F0E5D078834CE5FCB9EEF9D7EC830CF74F83A40CC1462BE97C1FFFD80DC4` | `0.3.1`, SHA-256 `A21ED2F00465584B603FB67DFEA292EAFB37C258E22C1FAC1356B008862E92C1` | Mismatch; installed-profile diagnosis only |

The dedicated role reported its loaded assembly from `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`, even though the normal dedicated output and one preflight installed identity were recorded under `Win64_Shipping_Server`. Both installed copies used the same observed `0.3.1` hash, but the loaded path must be treated as the authoritative runtime path in future identity checks.

The client role also reported MVID `1251539b-89fe-4743-92d5-b2cbb996302e`. Neither role currently emits the complete `ConfirmedLoadedHash` contract required by BLD-006; the probe correlated the role-reported path with an external SHA-256 measurement.

## 5. Capability matrix

| Capability | Status | Evidence and boundary |
|---|---|---|
| Dedicated executable starts and is exactly owned | `Confirmed` | Entry PID `117376`, exact path/start time/arguments, and verified `Watchdog` plus `conhost` descendants were recorded. |
| Dedicated module loads | `Confirmed` | The module marker reported PID `117376` and the loaded `0.3.1` path; module log time was 16.733 seconds after process creation. |
| Client starts and is exactly owned | `Confirmed` | Attempt 2 entry PID `141992` and its `Watchdog` descendant were recorded in interactive session 34. Steam was already running. |
| Client module becomes ready | `Confirmed` | The module emitted its assembly identity after 24.533 seconds and the multiplayer window was responsive. |
| Client connects to the owned server | `Blocked` | No `start_game` or mission was permitted before result isolation. Port `7210` remained unbound, and no network/module correlation event exists. |
| Approved control intent reaches the client/module path | `Blocked` | Lobby diagnostic patches loaded, but the source has no run-scoped normal-lobby command/acknowledgement protocol. The legacy direct-network proof of concept is not an acceptable substitute. |
| At least one safe staging mode works | `Confirmed` | `UseExistingInstalled` safely loaded the selected installed binaries for diagnosis. The mismatch explicitly blocks any current-source claim; the other modes are unproven. |
| Exact cleanup and lock release work | `Confirmed` | Both roles closed gracefully with no forced PID, no owned process remained, both ports were free, and an exclusive probe lock was reacquired after release. This does not yet prove crash recovery. |

No essential capability row is `Unknown`. The two `Blocked` rows are valid M1 findings and activate the specification's plan-revision gate.

## 6. Measured launch observations

### 6.1. Dedicated role

The dedicated probe used:

```text
DedicatedCustomServer.Starter.exe --multihome 0.0.0.0 --port 7210 _MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*CoopSpectatorDedicated*_MODULES_ /LogOutputPath "<RunRoot>\artifacts\logs\dedicated"
```

`BANNERLORD_GAME_ROOT` was the only supplied environment override. No token argument, configuration-file argument, or `start_game` input was used.

Observed facts:

- `DedicatedCustomServer.Starter.exe` itself hosted the module; no separate dedicated child replaced it;
- the official token-file flow logged in and emitted repeated `AliveMessage` events;
- no mission-open or `start_game` log event occurred;
- port `7210` remained free, so module authentication is not proof of a listed/listening game server;
- graceful `CloseMainWindow` cleanup succeeded and no forced termination was needed.

### 6.2. Multiplayer client

Both client attempts used:

```text
Bannerlord.exe /multiplayer _MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*CoopSpectator*_MODULES_
```

Attempt 1 ran before Steam was available. It exited naturally before `OnSubModuleLoad`, with `Start Game Final Cleanup` and `ERC302 Non-Zero Device Reference Count` in the log. It is classified `EnvironmentBlocked`, not a module failure.

Attempt 2 ran after Steam was started in the same user session. It loaded the module, registered the lobby join diagnostic patches, exposed a responsive multiplayer window, and did not open a mission. Graceful cleanup succeeded without forced termination.

## 7. Constraint inventory

| Constraint | Finding |
|---|---|
| Launcher | Direct entry executables worked for the bounded probes; the stock UI launcher was not needed. |
| Steam | Required for the client on this profile. This is proven by the controlled attempt-1/attempt-2 difference. |
| Dedicated authentication | The stock token-file path worked. Token content remained secret. |
| Anti-cheat | No blocker appeared in this exact one-client launch; this is not a general compatibility claim. |
| User/display session | A real interactive user session and responsive client window were present. |
| Watchdog / crash reporter | Both roles created owned `Watchdog` descendants. No new crash artifact, TaleWorlds crash reporter, or blocking modal window appeared. |
| Multi-instance | `Unknown` and intentionally untested; it is not required by the initial one-client profile. |
| Ports | TCP/UDP `7210` and `7777` were free before and after. No mission/listed-server port ownership was proven. |

A generic pre-existing `crashpad_handler.exe` process was recorded before the probes and left untouched because it was not owned by the run.

## 8. Source-drift findings

| Assumption | Status | Current evidence |
|---|---|---|
| Normal client and dedicated builds are compile-only | `Changed` | Both project files contain post-build deployment targets; the client build chains the dedicated build/deploy by default. |
| Existing battle bridges are run-isolated and atomic | `Changed` | Core roster, phase/start, and result paths remain shared under `SpecialFolder.MyDocuments`; they have no automation `RunId`. |
| Ending an early smoke cannot publish a plausible result | `Changed` | `OnEndMission` sets `BattleEnded` and calls `TryWriteBattleResultSnapshot`; the write guard does not require `BattleActive`. |
| A dedicated-only run can prove full lifecycle | `Changed` | Current readiness gates require eligible synchronized peer state before `BattleActive`. |
| Dedicated startup always switches to another game child | `Changed` | The Starter process hosted the loaded module; only support descendants were observed. |
| Installed modules represent the current repository output | `Changed` | Installed/loaded `0.3.1` differs from repository output `0.3.2` for both roles. |
| Direct client launch is independent of Steam | `Changed` | The no-Steam attempt exited before module load; the Steam-present attempt succeeded. |
| A stable automated normal-lobby join intent already exists | `Changed` | Existing patches observe/redirect lobby behavior; the legacy direct-network proof of concept does not satisfy the required path. |
| No-`start_game` launch binds the configured dedicated port | `Changed` | Authentication succeeded while port `7210` remained free. |
| Multiple real clients can share this profile | `Unknown` | Not exercised and not required for the initial profile. |

The single `Unknown` item is not an essential capability row and does not affect the one-client initial profile.

## 9. BLD-005 staging disposition

| Mode | Status | Decision |
|---|---|---|
| `UseExistingInstalled` | `Confirmed`, diagnostic only | Both roles performed a real module load without changing installed module files. The selected installed hashes were recorded and do not match the repository outputs, so no source-equivalence claim is allowed. |
| `StageIsolated` | `Blocked` | No verified alternate module root or equivalent isolated arrangement was found or proven for either role. |
| `DeployWithRestore` | `Blocked` | The current project targets deploy directly, but M1 found no run-owned pre-image/restore protocol and no approved deployment was performed. |

The `UseExistingInstalled` result is enough to diagnose the installed environment. It is not enough to run current-source L2–L5 tests. A later runtime gate must load binaries whose staged and role-confirmed hashes match an identified current build output.

## 10. Required revised gate plan

Before another mission-open or client-connect attempt, the approved implementation plan must split Milestone 2B into explicit blocker-removal gates:

1. **Current-build identity gate** — add a side-effect-free compile path, choose a safe client and dedicated staging strategy, and prove output/selected/loaded hashes for both roles.
2. **Result-isolation gate** — implement run-scoped bridge ownership and the validated `Suppress` result policy before any early mission can end.
3. **Durable ownership gate** — persist exact process identities and add bounded recovery for abnormal runner termination; preserve the exact-process-only cleanup rule confirmed here.
4. **Normal-lobby control gate** — add a default-off, run-scoped command with acknowledgement that asks the real client/module to discover and join the exact owned server through the supported lobby path. The source/contract portion is now implemented; Bannerlord-runtime confirmation remains open.
5. **Connection-only rerun** — after gates 1–4 pass, repeat the capability probe and require server/client/battle identity correlation before promoting the two blocked matrix rows.

Only after those gates are confirmed may the project open the first isolated L2 mission. Natural full-battle coverage remains mandatory in the later scenario milestones defined by Revision 4 of the specification.

## 11. Requirement completion audit

| Milestone 1 requirement | Status | Evidence |
|---|---|---|
| Re-check specification/audit against current source | `Satisfied` | Section 8 and source-linked audit addendum |
| Verify targets, bridges, result hazard, readiness, launch chain, identities | `Satisfied` | Sections 4, 6, 8, and run artifacts |
| Determine exact dedicated launch/stop feasibility | `Satisfied` | Dedicated launch, ownership, and cleanup artifacts |
| Determine real client launch/connect/control feasibility | `Partially Satisfied` | Launch/readiness confirmed; connect/control explicitly blocked with causes |
| Inventory environment and runtime constraints | `Satisfied` | Sections 3 and 7 |
| Determine BLD-005 feasibility | `Satisfied` | Section 9; one diagnostic mode confirmed, two modes blocked |
| Record named machine/version profile and timings | `Satisfied` | Sections 3 and 6 |
| Do not claim L2/L3 evidence | `Satisfied` | Explicit report boundary and no mission-open event |
| Do not open a mission before isolation/cleanup safety | `Satisfied` | No configuration argument, `start_game`, mission-open, join, or bridge mutation |
| No essential capability remains `Unknown` | `Satisfied` | Six confirmed and two blocked rows |
| Revise the plan when connection/source-safe runtime is infeasible | `Satisfied` | Section 10 |

`Partially Satisfied` for the combined client deliverable is the expected factual result of the two `Blocked` capability rows; it does not invalidate completion of the feasibility investigation. It does prevent promotion to runtime implementation without the revised approved plan.

## 12. Artifact index

The run root contains:

- `manifest.json` — bounded-run identity and completion status;
- `artifacts/capability-matrix.json` — machine-readable essential capability decisions;
- `artifacts/identity/environment.json` — repository, installation, manifest, executable, module, and non-secret token metadata;
- `artifacts/identity/source-assumptions.json` — source-drift dispositions;
- `artifacts/processes/` — exact launch and observed ownership records;
- `artifacts/logs/` — selected dedicated/client readiness evidence;
- `artifacts/ports/` — before/active/final port observations;
- `artifacts/constraints/` — environment constraint inventory;
- `artifacts/cleanup/` — lock, graceful cleanup, and final-state evidence;
- `artifacts/identity/bridge-delta.json` — proof that the monitored global battle files did not change.

The artifact root is local, ephemeral feasibility evidence. It is not a portable release package, contains machine-specific paths, and must not be treated as a committed test fixture.

## 13. Post-M1 client-control implementation status

After the feasibility report closed, the approved blocker-removal task added a dedicated launcher and a default-off module path that uses the normal TaleWorlds custom-server list and join requests. The implementation is bound to a fresh `RunId`, run-token hash, expected loaded client hash, exact server identity, persisted local-host marker, and active local UDP port. It writes a strictly atomic run-scoped acknowledgement and does not issue `start_game`, open a mission, or automate the UI.

The implementation passed its isolated source-compilation and contract checks plus launcher validation against the installed `0.3.1` hash. No multiplayer client, server, campaign, or battle was launched by this implementation-validation step. Consequently:

- source and contract feasibility of the control path is established;
- the M1 installed-profile identity facts remain unchanged;
- client-to-owned-server connection remains `Blocked` at runtime;
- approved control intent remains `Blocked` at runtime until a named run observes request, normal-lobby handoff, connection, and exact client/server correlation;
- current-build runtime testing remains blocked until selected and loaded `0.3.2` hashes are proven and result isolation is active.

## 14. Post-M1 Milestone 2A status

Milestone 2A subsequently implemented and verified the non-runtime foundation described in [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md):

- `m2a-contracts-20260831-07` passed the complete 20-project contract inventory;
- `m2a-compile-only-20260831-03` independently compiled current client and dedicated `0.3.2` assemblies below the run root and proved all installed module inventories unchanged;
- `m2a-doctor-20260831-06` retained `EnvironmentBlocked` for the installed/repository hash mismatch and unverified runtime version matrix.

This resolves the side-effect-free current-source compilation prerequisite, not current-build runtime identity. The M1 blocked connection/control rows still require a staged-and-role-confirmed loaded hash, result isolation, exact runtime ownership/cleanup, and a named connection-only rerun in Milestone 2B.

## 15. Milestone 2B.1 source correction

The approved Milestone 2B.1 implementation subsequently closed the source/contract side of result suppression, loaded-role identity, run-scoped owned-host correlation, exact cleanup, inspection, and recovery. Lowest-level investigation also established that the native dedicated server does not bind/list its UDP server before standard `start_game`. Revision 7 therefore permits only the external runner to issue a minimum vanilla `TeamDeathmatch` bootstrap after `Suppress` and dedicated loaded-hash gates. The client launcher/module still does not issue `start_game`.

Final non-runtime evidence is `m2b1-final-contracts-20260831-03` (21/21) and `m2b1-final-compile-only-20260831-02` (both projects passed; installed inventories unchanged). No real `Feasibility` run, client/server connection, loaded new binary, campaign fixture, L2/L3 battle, or campaign result was produced. See [BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md](BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md).
