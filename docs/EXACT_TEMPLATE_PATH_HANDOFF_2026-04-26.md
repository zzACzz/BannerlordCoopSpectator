# Exact Template Path Handoff (2026-04-26)

Branch for this work:

- `codex/runtime-regression-checkpoint-2026-04-26`

This note is for the next window.

The goal is no longer "stabilize the current fallback-heavy runtime with local
patches". The goal is:

- move the hosted battle runtime to a full exact-template path,
- treat silent fallback as a temporary emergency behavior, not a target model,
- build the next fixes from low-level native/runtime evidence rather than
  surface hypotheses.

## Bottom-Line Decision

Target architecture:

1. identical game/module data on client and dedicated,
2. exact campaign troop template resolution for battle entries,
3. exact item/equipment resolution for those entries,
4. native MP-safe spawn and replication contract for those exact templates,
5. selection/spawn/commander control built on top of that exact path.

Not target architecture:

- broad allowed-list fallback as the main runtime,
- silent mission-safe character substitution as a normal path,
- "good enough" surrogate weapons for mass AI,
- repeated runtime triage patches without proving the native contract first.

## What Is Already Done

### 1. Structural peer/session cleanup

Implemented on this branch:

- canonical peer session projection in
  `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattlePeerSessionState.cs`
- lifecycle/session read paths moved toward that projection
- reduced circular dependency between fallback selection, spawn queue, and
  lifecycle status

See:

- `C:\dev\projects\BannerlordCoopSpectator3\docs\HOSTED_BATTLE_RUNTIME_ARCHITECTURE_AUDIT_2026-04-26.md`

### 2. Battle snapshot readiness acknowledgement

Implemented:

- client `BattleSnapshotReadyAck`
- server-side tracking of expected and acknowledged `BattleSnapshot`
  transmission ids
- battle data readiness is blocked until the specific peer confirms it has
  assembled the current `BattleSnapshot`

Files:

- `C:\dev\projects\BannerlordCoopSpectator3\Network\Messages\CoopBattleSelectionNetworkMessages.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Confirmed by logs:

- client now receives `BattleDataReady=True` only after
  `BattleSnapshotReadyAck`, which fixed the earlier "ready before full snapshot"
  bug

### 3. Class screen gating cleanup

Implemented:

- class loadout UI no longer opens only because side selection was clicked
- it now waits for `UnitSelection` / `RespawnSelection` readiness for the
  effective side

File:

- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopMissionSelectionView.cs`

This is a UX/status timing fix, not the core exact-template solution.

### 4. Temporary crash isolation for bulk AI pre-spawn

Implemented as a temporary triage measure:

- pre-spawn exact loadout injection for bulk AI no longer injects exact weapons
- exact armor/body/horse/harness remain
- exact local controlled-agent runtime overlay path remains separate

Files:

- `C:\dev\projects\BannerlordCoopSpectator3\Patches\ExactCampaignPreSpawnLoadoutPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

This is not a target state. It is only a temporary isolation of the most likely
`CreateAgent` / `SetWieldedItemIndex` crash trigger.

## What The Logs Proved

### Proven good

1. Early readiness was previously wrong and is now fixed by snapshot ack.
2. The hosted runtime was indeed too fragile because the same peer/session state
   was represented in too many overlapping stores.
3. Remote class UI could open before side-specific unit selection was actually
   ready.

### Proven bad

1. In large battles, the client receives repeated native failures during mass AI
   materialization:
   - `Exception in handler of CreateAgent`
   - `Exception in handler of SetWieldedItemIndex`

   Example log:

   - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_7088.txt`

2. Those failures occur before a stable player spawn flow finishes, so they are
   upstream of later visual corruption and client crash.

3. The current runtime still silently uses mission-safe fallback characters for
   entries that do not resolve to their intended exact runtime character.

4. Problematic test runs also had a client/server game-build mismatch:
   - client: `110062`
   - host/dedicated: `109797`

   This is a dirty environment for native contract debugging and must be
   eliminated for clean exact-path validation.

## Core Technical Problem

The main unresolved problem is not "missing modules" anymore.

The unresolved problem is:

- whether exact campaign troops and exact campaign equipment are admissible to
  the native MP spawn and replication contract.

That contract includes at least:

1. `Mission.SpawnAgent`
2. native `CreateAgent`
3. native `SetWieldedItemIndex`
4. ammo state
5. horse state
6. shield and wield order
7. replication to remote client with the same object catalog

Having the same modules on client and dedicated is necessary, but not
sufficient.

The exact-template path will be correct only if:

- exact character exists on both sides,
- every exact item exists on both sides,
- the resulting exact equipment layout is valid for native spawn,
- the resulting exact equipment layout is valid for native MP replication,
- the runtime no longer needs silent fallback to mission-safe MP templates.

## Why Full Exact Template Path Is Still The Right Target

Because silent fallback causes three architectural failures:

1. fidelity loss:
   - wrong troop archetype
   - wrong weapon set
   - possible balance distortion

2. debugging ambiguity:
   - the system no longer proves whether the exact path works

3. unstable hybrid runtime:
   - template from one layer
   - equipment from another layer
   - visuals/control assumptions from a third layer

That hybrid state is exactly where the hardest bugs have come from.

## What Must Be Done Next

### Phase 1. Freeze the environment

Before deep exact-path work:

1. require the same Bannerlord build on client and dedicated
2. keep using the branch
   `codex/runtime-regression-checkpoint-2026-04-26`
3. do not merge more runtime triage into `main`

Without identical game build numbers, native MP evidence is noisy.

### Phase 2. Build exact-path diagnostics, not guesses

Add a validator for every battle entry:

1. exact character resolved?
2. fallback character used?
3. exact spawn template id?
4. exact item ids resolved for all slots?
5. exact horse/harness resolved?
6. exact loadout compatible with native spawn?
7. exact loadout compatible with native wield/ammo order?

This validator should produce explicit failure reasons per `EntryId`, not just
"fallback happened".

### Phase 3. Make fallback visible and controlled

In exact mode:

- do not silently accept fallback as success
- log exact failure reason
- mark the entry as degraded or unsupported
- keep temporary mitigations only to prevent total crash during diagnosis

Target:

- exact-supported entries go 1:1
- unsupported entries fail loudly in diagnostics

### Phase 4. Audit the native spawn contract at the lowest level

The next window must inspect the lowest-level contract, not just patch around
symptoms.

At minimum:

1. decompile and trace the native/managed path around:
   - `Mission.SpawnAgent`
   - `AgentBuildData.Equipment(...)`
   - `CreateAgent`
   - `SetWieldedItemIndex`
   - spawn equipment sync
   - mount equipment sync
   - initial wield selection

2. compare:
   - exact campaign character
   - fallback mission-safe MP character
   - item slot layout
   - weapon/ammo ordering

3. determine whether the exact path fails because of:
   - invalid template type for MP mission,
   - invalid item-slot layout,
   - invalid mount/harness combination,
   - invalid initial wield order,
   - or game-build mismatch.

### Phase 5. Remove the hybrid model

Long-term target:

- either exact campaign troop templates are fully admissible for MP runtime,
- or a dedicated exact-coop mission-safe clone layer must be built explicitly.

But the runtime must stop pretending that a hidden fallback plus exact overlay
is equivalent to true exact spawn.

## Temporary Measures That Must Be Removed Later

These are temporary, not architectural outcomes:

1. bulk AI `IncludeWeapons=False` pre-spawn exact injection
2. any UI behavior that relies on fallback allowed lists to paper over delayed
   side-specific status
3. any local visual patch that becomes authoritative for identity

These are acceptable only while proving the native exact-path contract.

## Required Working Style For The Next Window

This is mandatory:

1. do not continue with surface-level hypothesis patches
2. inspect the lowest-level runtime contracts first
3. correlate code changes with:
   - exact logs,
   - decompiled engine behavior,
   - and concrete entry/equipment examples
4. if using decompilation tooling such as `ilspycmd`, capture findings as
   explicit notes before changing runtime logic
5. prefer "prove or falsify one native contract assumption" over "try another
   broad fix"

Reason:

- previous deeper low-level investigation produced better results than broad
  rerun-driven patching
- the remaining failures are now inside native spawn/replication boundaries,
  where shallow reasoning is especially dangerous

## Ready-to-Use Prompt For The Next Window

Use this as the starting prompt:

> Continue on branch `codex/runtime-regression-checkpoint-2026-04-26`.
> The target architecture is a full exact-template path for hosted coop battles:
> no silent fallback as the normal solution, no surrogate bulk-AI weapons as the
> end state, and no more broad hypothesis-driven runtime patches.
> Read `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_TEMPLATE_PATH_HANDOFF_2026-04-26.md`
> and `C:\dev\projects\BannerlordCoopSpectator3\docs\HOSTED_BATTLE_RUNTIME_ARCHITECTURE_AUDIT_2026-04-26.md`
> first.
> Work from the lowest-level contracts upward:
> inspect and decompile the native/managed spawn and replication path around
> `Mission.SpawnAgent`, `AgentBuildData.Equipment`, `CreateAgent`,
> `SetWieldedItemIndex`, spawn equipment sync, mount sync, and initial wield
> order. Correlate those findings with the exact logs and exact battle-entry
> equipment examples before changing code.
> Build explicit diagnostics for exact entry compatibility:
> exact character resolution, fallback usage, exact item-slot resolution, mount
> resolution, and native spawn/wield admissibility per `EntryId`.
> The immediate goal is to prove why exact campaign troops/equipment fail in the
> large-battle MP path and replace the hidden fallback model with an explicit,
> evidence-backed exact-path contract.

## Expected Outcome Of The Next Window

The next window should ideally finish with:

1. a concrete compatibility matrix for exact entries,
2. a proven explanation for the current `CreateAgent` /
   `SetWieldedItemIndex` failure pattern,
3. a decision whether full exact spawn is directly admissible or requires a
   mission-safe exact clone layer,
4. and only then the next runtime code change.
