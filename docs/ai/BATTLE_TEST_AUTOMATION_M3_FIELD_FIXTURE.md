# Milestone 3 Exact Field Fixture

Status: **3A source and contracts complete; no Bannerlord fixture has been captured, reviewed, sanitized, or committed**
Source verification date: **2026-09-02**
Fixture contract schema: **1**

## 1. Scope and evidence boundary

Milestone 3A adds the minimum default-off recording surface required to capture the existing campaign-to-dedicated field input without introducing a second scenario model. The authoritative boundary remains `Campaign/BattleRosterFileHelper.WriteRoster`: production code serializes `BattleRosterFileDto` through the existing Newtonsoft.Json `Formatting.Indented` path and writes `battle_roster.json` through the existing `File.WriteAllText` call.

The recorder runs only after that production write succeeds. It reads the resulting file bytes and retains those exact bytes under the active run root. It does not reserialize the DTO, change the production file encoding, add a payload field, advance a battle phase, open a mission, start a server, launch a client, or publish a result.

This source/contract change is not a captured fixture and is not L2/L3 evidence. A separately approved controlled staging and campaign-capture step is still required.

## 2. Default-off capture gate

Recording requires all normal Milestone 2B runtime configuration checks plus these explicit process environment values:

- `COOPSPECTATOR_TEST_AUTOMATION=1`;
- `COOPSPECTATOR_AUTOMATION_FIXTURE_RECORD=1`;
- `COOPSPECTATOR_AUTOMATION_FIXTURE_ID=<safe-id>`;
- `COOPSPECTATOR_AUTOMATION_SOURCE_REVISION=<full-40-or-64-hex-revision>`;
- `COOPSPECTATOR_AUTOMATION_GAME_VERSION=<observed-version>`.

The existing run id, exact `%TEMP%\CoopSpectator\Automation\<RunId>` root, run token, expected loaded module SHA-256, and `Suppress` result policy must also validate. When either automation or fixture recording is disabled, `TryRecordCampaignRoster` returns before reading the roster path or creating any artifact.

The recorder remeasures the executing module and requires its SHA-256 to equal the expected module identity before accepting a payload.

## 3. First-field-slice admission

`CoopAutomationFixtureContract.TryQualifyFirstFieldSlice` observes the already-built `BattleSnapshotMessage`. It does not build or repair scenario state. Admission requires:

- exact ordinary `ScenarioKind=FieldBattle`;
- exact `CampaignBattleType=FieldBattle`;
- `IsSiegeBattle=false`;
- two populated sides;
- at least one positive-count unmounted stack;
- at least one positive-count mounted stack;
- at least one positive-count hero or captain stack.

Village, siege assault, sally out, siege ambush, relief (`CampaignBattleType=SiegeOutside`), lords hall, day hideout, and night hideout ambush inputs are skipped with `ScenarioNotFirstFieldSlice`. This is an admission filter for the first SCN-001 sample, not a shared scenario classifier.

## 4. Run-scoped artifacts

An accepted capture is written only below:

```text
%TEMP%\CoopSpectator\Automation\<RunId>\
  artifacts\fixtures\field-current\
    battle_roster.raw.json
    fixture.metadata.json
  state\
    fixture-record.status.json
```

`battle_roster.raw.json` is immutable. If it already exists, its SHA-256 must match the candidate bytes; a different payload is rejected rather than overwritten. Metadata is written by the existing strict atomic JSON helper.

Metadata schema 1 records the fixture/run identity, payload kind, exact source/target boundary, encoding, compression, serializer, payload-schema status, byte length, SHA-256, campaign/battle/stage identity, scenario, full source revision, module version/file/hash, expected module hash, observed game version, UTC timestamp, capture reason, qualification counts, sanitization state, and independent-oracle state.

The current production `BattleRosterFileDto` has no embedded payload schema version. The exact metadata therefore declares:

- `PayloadSchema=BattleRosterFileDto.CurrentUnversioned`;
- `CompatibilityStatus=CurrentOnlyUnversioned`.

Milestone 3A does not add a version field to the production JSON because the milestone requires production serialization output to remain unchanged while recording is off.

## 5. Validation and failure semantics

The fixture contract rejects before replay on stable conditions including:

- unsupported fixture metadata schema;
- unsupported payload schema or compatibility status;
- non-canonical boundary, serializer, payload name, encoding, or compression;
- byte-length mismatch;
- SHA-256 mismatch;
- unsafe fixture/run identity;
- incomplete source/module/game/battle provenance;
- module hash mismatch;
- rooted paths or paths escaping the fixture root.

Recording failure does not alter production gameplay. It publishes a best-effort structured `Failed` status and returns failure to the `BattleRosterFile` integration, which logs only when the explicit recording profile is active. A non-field battle publishes `Skipped` and continues normally.

The recorder is not placed in a per-agent, per-frame, network-message, or native-sensitive hot path. Its only integration call follows the existing campaign roster file write.

## 6. Contract and build evidence

| Verification | Result |
|---|---|
| Focused `CoopAutomationFixture.ContractTests` | Passed |
| Exact CRLF/spacing byte retention | Passed; recorded bytes equal source bytes |
| Recording disabled with a nonexistent source path | Passed; no directory or artifact created |
| Other battle-type admission matrix | Passed; only ordinary SCN-001 accepted |
| Missing cavalry or hero/captain | Rejected with stable reason |
| Same-length byte corruption | Rejected as `PayloadHashMismatch` |
| Length mismatch | Rejected as `PayloadLengthMismatch` |
| Fixture-schema mismatch | Rejected as `FixtureSchemaUnsupported` |
| Payload-schema mismatch | Rejected as `PayloadSchemaUnsupported` |
| Root escape | Rejected as `FixturePathEscapesRoot` |
| Production serializer source guard | Passed; existing Newtonsoft.Json plus `File.WriteAllText` boundary remains before the recorder |
| First canonical aggregate `m3a-contracts-01` | Passed 23/23 |
| First compile-only `m3a-compile-01` | Client passed; dedicated failed because its explicit source list omitted the two new shared files; installed inventories unchanged |
| Dedicated source-list correction | Two exact linked-source entries added; targeted dedicated compile passed |
| Final compile-only `m3a-compile-03` | Passed client and dedicated; installed inventories unchanged; no product process launched |
| Final canonical aggregate `m3a-contracts-03` | Passed 23/23; immutable metadata/idempotence checks included; no product process launched |

Existing unreachable-code warnings remain outside this change. The final builds contain zero errors.

## 7. Requirement audit

| Milestone 3 requirement | 3A status | Evidence or remaining gate |
|---|---|---|
| Opt-in recording at an existing first-slice boundary | Satisfied in source/contracts | Post-write `battle_roster.json` exact-byte capture |
| One current mixed infantry/cavalry fixture with hero/captain | Not satisfied | Requires separately approved controlled Bannerlord campaign capture |
| Exact raw bytes, metadata, hashes, provenance, compatibility | Implemented but not runtime-populated | Contract and synthetic exact-byte proof only |
| Sanitized derivative when sharing is needed | Not satisfied | Real capture must be reviewed; no user save or raw fixture may be committed before review |
| Independent critical-value oracle | Not satisfied | Must be authored/reviewed independently after capture |
| Corruption and schema-mismatch tests | Satisfied | Focused and canonical contract evidence |
| Redacted one-command reproduction descriptor | Not satisfied | Capture/replay command surface belongs to the next approved substage |
| Production serialization unchanged when recording is off | Satisfied in source/contracts | Existing serializer/write call retained; disabled mode performs no recorder I/O |
| No parallel authoritative scenario model | Satisfied | Recorder only observes the existing snapshot and exact written file |

Milestone 3 is therefore **in progress**, not complete.

## 8. Next approved gate

The next substage must be planned separately and must:

1. produce clean build identities and controlled staging with retained pre-images;
2. provide a run-scoped capture launcher/descriptor that supplies the required environment without credentials or UI automation;
3. use a purpose-made or explicitly reviewed campaign sample;
4. capture one qualifying ordinary field boundary without requiring full battle completion;
5. independently review critical scene, side, entry, infantry/cavalry, hero/captain, equipment, and mount facts;
6. decide whether the exact payload is safe to commit or requires a sanitized derivative;
7. retain the exact private raw payload hash even when only a derivative is shared;
8. add the redacted one-command reproduction descriptor and close the remaining requirement rows.

Full controlled and natural battle lifecycle proof remains Milestone 5 work. Fixture capture alone must never be described as a complete battle test.
