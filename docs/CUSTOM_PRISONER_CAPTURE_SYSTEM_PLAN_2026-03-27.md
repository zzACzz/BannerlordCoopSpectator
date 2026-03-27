## Goal

Define the fallback implementation for prisoner capture if the dedicated mission never exposes a reliable vanilla-like `Unconscious` outcome.

This is not the first-choice path.
First-choice remains:

1. make dedicated produce real `Unconscious`
2. let existing host writeback consume it
3. let host loot/prisoner aftermath read from vanilla rosters where possible

The custom system below is the explicit fallback if step 1 cannot be made reliable.

---

## Current facts

Recent clean-runtime logs still show:

- dedicated reconcile ends with `Unconscious=0`
- no usable `OnScoreHit`
- no usable `OnAgentRemoved`
- many removals already flattened to `Killed` or `OtherRemoved`

This means host-side prisoner transfer still has no authoritative knockout bucket to consume.

Host is now ready to apply prisoner loot if a valid aftermath bucket appears:

- `LootAftermath.PrisonerEntries`
- `LootApply.PrisonerStacks/PrisonerUnits`
- apply target: `MainParty.PrisonRoster`

So the remaining blocker is still dedicated-side prisoner signal generation.

---

## Decision

Do not fabricate prisoners on host from campaign aftermath guesses alone.

If vanilla-like knockout cannot be recovered from dedicated mission state, build a custom dedicated-side `knockout ledger` and make it the authoritative source for:

- `UnconsciousCount`
- defeated regular troop prisoner candidates

Initial scope should be narrow:

- regular troops only
- defeated side only
- no hero capture
- no rescued prisoners
- no surrendered healthy survivor capture yet

---

## Preferred path before custom ledger

Try to recover the cleaner vanilla-like path first:

1. Ensure `DedicatedKnockoutOutcomeModelOverride` actually installs.
2. Verify whether `MissionGameModels.AgentDecideKilledOrUnconsciousModel` is the live decision point in the clean coop runtime.
3. Retest blunt and horse-charge outcomes.

If this starts producing `Unconscious=1+`, then the custom prisoner system should stay minimal or unnecessary.

---

## Custom prisoner system architecture

### 1. Dedicated-side authoritative runtime state

Introduce a dedicated-only runtime state, for example:

- `CoopKnockoutLedgerRuntimeState`

Per agent entry:

- `AgentIndex`
- `EntryId`
- `PartyId`
- `SideId`
- `CharacterId`
- `IsHero`
- `WasAlive`
- `TerminalOutcome`
- `LastDamageType`
- `LastWeaponFlags`
- `LastWasHorseCharge`
- `LastWasFallDamage`
- `LastWasAlternativeAttack`
- `LastAffectorAgentIndex`
- `LastMissionTime`

`TerminalOutcome` initially needs only:

- `Alive`
- `Killed`
- `Unconscious`
- `OtherRemoved`

### 2. Damage context source

The custom system is only good if it has a reliable terminal-hit context source.

Possible sources in descending preference:

1. `AgentDecideKilledOrUnconsciousModel` override
2. lower-level damage / blow patch before agent state collapse
3. periodic agent-state reconciliation plus separate hit-context tap

Do not use host-only client perception as authority.

### 3. Initial knockout rules

Minimal rules:

- blunt damage without `CanKillEvenIfBlunt` => `Unconscious`
- horse charge => `Unconscious`
- fall damage => `Unconscious`
- everything else => keep vanilla/observed fatal outcome

This is intentionally narrower than full vanilla campaign fidelity, but already covers:

- maces / hammers
- cavalry impact
- fall knockouts

### 4. Snapshot export

At battle result build time, ledger outcome must override or supplement reconcile counts:

- `KilledCount`
- `UnconsciousCount`
- `OtherRemovedCount`

Priority should be:

1. custom ledger terminal outcome if present
2. mission final state fallback

This prevents final `mission.AllAgents` flattening from deleting knockout information.

### 5. Host writeback usage

Host side already has the right semantic mapping:

- `UnconsciousCount` contributes to `DesiredWoundedCount`
- `LootAftermath` can now carry prisoner entries
- `LootApply` can now write them into `MainParty.PrisonRoster`

So once dedicated begins exporting real `UnconsciousCount`, host prisoner work becomes straightforward.

---

## Recommended first implementation scope

### Phase A

Get real dedicated-side `UnconsciousCount` first.

Success condition:

- battle result snapshot contains `Unconscious=1+`

Do not touch hero capture yet.

### Phase B

Convert defeated unconscious regular troops into prisoner candidates on host.

Success condition:

- `LootAftermath.Prisoners > 0`
- `LootApply.PrisonerUnits > 0`

### Phase C

Only after Phase A/B works:

- hero capture / fugitive logic
- rescued prisoner transfer
- surrendered healthy survivor capture

---

## Risks

### Risk 1

If no reliable damage context source exists on dedicated, a custom prisoner system becomes heuristic, not authoritative.

### Risk 2

If final agent replacement / cleanup happens before the ledger records terminal outcome, the custom system will still lose the knockout.

### Risk 3

If hero capture is mixed into the first version, implementation complexity rises sharply and will slow down regular troop prisoner delivery.

---

## Recommendation

The best next move is not full custom prisoners yet.

It is:

1. instrument `DedicatedKnockoutOutcomeModelOverride` until installation is proven in logs
2. if installation succeeds but `Unconscious` is still absent, build a custom dedicated-side knockout ledger for regular troops only
3. feed that into the already-prepared host prisoner writeback path

This keeps the work incremental and avoids inventing host-side prisoners without a dedicated authoritative source.
