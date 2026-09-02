# Milestone 3 Exact Field Fixture

Status: **Milestone 3 complete: private capture, independent content/privacy review, deterministic sanitized derivative, independent oracle, and hash-gated contract replay are verified**
Source verification date: **2026-09-02**
Fixture contract schema: **1**

## 1. Scope and evidence boundary

Milestone 3A adds the minimum default-off recording surface required to capture the existing campaign-to-dedicated field input without introducing a second scenario model. The authoritative boundary remains `Campaign/BattleRosterFileHelper.WriteRoster`: production code serializes `BattleRosterFileDto` through the existing Newtonsoft.Json `Formatting.Indented` path and writes `battle_roster.json` through the existing `File.WriteAllText` call.

The recorder runs only after that production write succeeds. It reads the resulting file bytes and retains those exact bytes under the active run root. It does not reserialize the DTO, change the production file encoding, add a payload field, advance a battle phase, open a mission, start a server, launch a client, or publish a result.

The source/contract implementation alone is not a fixture or L2/L3 evidence. Clean run `m3b-live-capture-02` supplies the exact private runtime sample from the existing boundary. Milestone 3C independently reviewed that payload, retained the raw bytes outside Git, generated a deterministic sanitized derivative with explicit raw-to-derivative provenance, authored a separate critical-value oracle, and verified the derivative through the production message and first-field admission contracts. None of this proves mission opening, multiplayer connection, or battle completion.

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
| Installed launcher inspection | Installed `LauncherVM.GameTypeArgument` returns `/singleplayer`; `LauncherUI.AdditionalArgs` concatenates that value, the topology-sorted `_MODULES_` block, and the optional continue argument |
| Installed campaign selection | `Native`, `SandBoxCore`, `CustomBattle`, `Sandbox`, `StoryMode`, and `CoopSpectator`; the fixed capture launcher reproduces this exact list without changing `LauncherData.xml` |
| `Start-CoopFieldFixtureCapture.ps1 -ValidateOnly` | Passed against installed game `v1.4.8` and installed client SHA-256 `A363B19B...C91C0075`; no product process or run-root write |
| Dirty-source `Record` rejection `m3b-dirty-block-01` | Returned `EnvironmentBlocked`, retained a credential-free one-command reproduction descriptor, and launched no product process |
| Milestone 3B canonical contracts `m3b-contracts-01` | Passed 23/23 in both Windows PowerShell 5.1 and PowerShell 7 where applicable |
| Milestone 3B compile-only `m3b-compile-01` | Client and dedicated passed below the run root; installed inventories unchanged; no product process launched |
| Controlled client-only staging `m3b-stage-01` | Complete 32-file pre-image retained; only the client DLL/PDB changed; installed client SHA-256 `2A1E17E4...250FFB4`; installed dedicated module unchanged |
| First live diagnostic `m3b-live-capture-01` | Recorder produced a hash/length/provenance-qualified private field payload, but the aggregate returned `AssertionFailed` because two runner literals had drifted from the canonical C# payload kind/boundary; no product process or port remained |
| Published runner correction `baf5c69` | Canonical C# values restored in `Confirm-CoopRecordedFixture`; a cross-language contract guard now derives both values from `CoopAutomationFixtureContract`; focused runner tests passed in Windows PowerShell 5.1 and PowerShell 7 and exact field-fixture tests passed |
| Corrected published contracts `m3b-fix-pub-contracts-01` | Passed 23/23 from exact clean local/upstream revision `baf5c69` |
| Corrected published compile-only `m3b-fix-pub-compile-01` | Client and dedicated passed below the run root; installed inventories unchanged; no product process launched |
| Accepted private capture `m3b-live-capture-02` | `Pass`; 451,762 exact bytes; payload SHA-256 `ECF29661E44B64C1AEE77EC2B44E61F63926287A3A05A9BFD6DC545EC073B9C7`; two sides, 30 infantry stacks, 17 mounted stacks, and 4 hero/captain stacks; exact staged client hash and clean runner revision retained; protected result unchanged; zero remaining product processes/port listeners |
| Independent private-content audit | Passed; exact 451,762-byte source hash remained unchanged; 2 sides, 3 parties, 47 positive stacks, 74 units, 17 mounted, 11 ranged, and 4 hero stacks were derived without using recorder qualification as the oracle; references, mission order, and equipment invariants passed |
| Deterministic sanitizer | Windows PowerShell 5.1 and PowerShell 7 produced byte-identical 259,744-byte derivatives with SHA-256 `B47D7AF7FA057C36CA8EF759A6D597C00007158A22E3A556AC57A1299579D49D`; raw payload and logs were not copied |
| Committed derivative and independent oracle | `Tests/Fixtures/Automation/field-current` contains only the sanitized payload, sanitized metadata, and independent oracle; account/path/credential patterns are rejected and the raw capture remains outside Git |
| Focused sanitized-fixture contract replay | Passed; hash and length validated before production-schema deserialization, ordinary-field admission passed, references/composition/equipment matched the independent oracle, and a siege mutation was rejected |
| Final canonical Milestone 3C contracts `m3c-sanitized-contracts-02` | Passed 23/23 after committed encoding/oracle assertions were complete; no product process launched |
| Canonical Milestone 3C compile-only `m3c-sanitized-compile-01` | Client and dedicated passed below the run root; installed inventories unchanged; no product process launched |

Existing unreachable-code warnings remain outside this change. The final builds contain zero errors.

## 7. Requirement audit

| Milestone 3 requirement | Status | Evidence |
|---|---|---|
| Opt-in recording at an existing first-slice boundary | Satisfied in source/contracts | Post-write `battle_roster.json` exact-byte capture |
| One current mixed infantry/cavalry fixture with hero/captain | Satisfied | `m3b-live-capture-02` is retained privately; the reviewed derivative is committed for shareable contract replay |
| Exact raw bytes, metadata, hashes, provenance, compatibility | Satisfied for capture/integrity | `m3b-live-capture-02`; payload hash, staged module hash, clean runner revision, game version, boundary, and current-only unversioned compatibility retained |
| Sanitized derivative when sharing is needed | Satisfied | Deterministic `CoopFieldFixtureSanitizationV1` derivative pins both private-source and derivative hashes and contains no raw capture/log file |
| Independent critical-value oracle | Satisfied | `fixture.oracle.json` records independently audited counts, composition, equipment, references, provenance, and evidence limits without using recorder qualification as its source |
| Hash-gated replay validation | Satisfied at the Milestone 3 contract boundary | The committed derivative hash is checked before production-schema deserialization and ordinary-field qualification; runtime mission replay belongs to Milestone 4 |
| Corruption and schema-mismatch tests | Satisfied | Focused and canonical contract evidence |
| Redacted one-command reproduction descriptor | Satisfied in runtime | Passing `m3b-live-capture-02` descriptor retains fixture id, exact installed hash, roots, timeout, and revision-derived manifest identity without nonce or credentials |
| Production serialization unchanged when recording is off | Satisfied in source/contracts | Existing serializer/write call retained; disabled mode performs no recorder I/O |
| No parallel authoritative scenario model | Satisfied | Recorder only observes the existing snapshot and exact written file |

Milestone 3 is therefore **complete at its exact-fixture and contract-replay boundary**. The raw payload remains private and immutable outside Git. Milestone 3 does not open a mission, start the cooperative dedicated flow, connect a battle client, or prove L2/L3 behavior.

## 8. Next implementation gate

Milestone 4 must:

1. consume only the hash-pinned reviewed derivative selected by the run contract;
2. orchestrate the dedicated mission-open path through `PreBattleHold` and assert SCN-001 identity, scene, team, formation, entry, equipment, mount, hero/captain, and materialization facts;
3. retain result suppression and prove isolated early abort plus exact owned-process cleanup;
4. execute two sequential L2 attempts without stale phase, command, fixture, or result state;
5. preserve the distinction between controlled L2 smoke evidence and later full-battle L3/L4 evidence.

Full controlled and natural battle lifecycle proof remains Milestone 5 work. Fixture capture alone must never be described as a complete battle test.

## 9. Milestone 3B campaign-capture control surface

`scripts/Invoke-CoopTest.ps1 -Command Record` is the only aggregate entry point for a live field-fixture capture. It requires a fresh run id, clean local/upstream identity, explicit installed client SHA-256, running Steam, free required ports, no pre-existing Bannerlord/dedicated/launcher/crash-reporter process, the canonical shared-resource locks, and `ResultPolicy=Suppress`.

`scripts/Start-CoopFieldFixtureCapture.ps1` accepts live launch only through `-UseExistingRunContract`. It validates the aggregate manifest, runner lock, nonce fingerprint, exact run root, installed module hash, source revision, Native module version, and immutable output paths before starting `Bannerlord.exe`. The child receives the token only through its process environment. The launch artifact records `RunTokenPersisted=false`, `CredentialsPersisted=false`, and `UiAutomationUsed=false`; it contains neither the plaintext nonce nor a server password.

The fixed installed-1.4.8 launch shape is:

```text
Bannerlord.exe /singleplayer _MODULES_*Native*SandBoxCore*CustomBattle*Sandbox*StoryMode*CoopSpectator*_MODULES_
```

This shape comes from decompiling the exact installed SHA-256 `2BD46368...6320798` launcher library path and comparing it with the current singleplayer selection in `LauncherData.xml`; it was not inferred from the multiplayer wrapper. The capture launcher does not edit launcher configuration, a campaign save, the dedicated installation, or the protected result.

The aggregate registers provisional campaign ownership before fallible identity enrichment, validates the exact executable/parent/start identity, waits for `Recorded`, tolerates `Skipped` so another ordinary field battle can be attempted in the same campaign process, rejects `Failed`, supports bounded timeout/cancellation, registers only exact descendants, and then performs exact cleanup. It independently rechecks payload length, SHA-256, metadata identity, source/module/game provenance, ordinary-field qualification counts, JSON validity, protected-result immutability, and remaining-process absence. The capture record itself remains `PrivateRawArtifact=true`, `SanitizationReviewed=false`, `IndependentOracleComplete=false`, `FullBattleCompleted=false`, and `L2OrL3PassClaimed=false`; Milestone 3C records review on a distinct derivative instead of rewriting historical evidence.

Clean run `m3b-live-capture-02` exercised this path from runner revision `baf5c69` with the exact client-only staged SHA-256 `2A1E17E4FEC5330345D28387AF1C4E2D412D07F221EBE7EE02705FAAC250FFB4`. It stopped at the post-write pre-mission boundary, returned `Pass`, retained one private raw payload plus metadata/status/reproduction/log/cleanup evidence, forced only the exact verified campaign process after a graceful close request, observed the optional runtime-support descendant as already absent, left zero owned/product processes and no UDP 7210 listener, and preserved the protected global result. The run does not authorize committing or sharing the raw payload.

## 10. Milestone 3C review, sanitization, oracle, and contract replay

The reviewed private source is identified only by length 451,762 and SHA-256 `ECF29661E44B64C1AEE77EC2B44E61F63926287A3A05A9BFD6DC545EC073B9C7`. It remains under the run-owned temporary artifact root and is not copied into the repository. The broader run root is also private because native logs contain account and local-profile data.

`scripts/New-CoopSanitizedFieldFixture.ps1` accepts only that exact reviewed source hash. It refuses to overwrite the source, refuses nonempty output, requires an explicit switch before writing below the repository, and emits only `battle_roster.sanitized.json` plus `fixture.sanitized.metadata.json`. It deterministically replaces campaign, battle, side, party, combat-group, entry, hero, clan, display-name, body-property, banner, and appearance identities while retaining static game content needed by replay. Compact JSON, LF, UTF-8 without BOM, and explicit angle-bracket escaping make the result byte-identical in Windows PowerShell 5.1 and PowerShell 7.

The committed derivative is 259,744 bytes with SHA-256 `B47D7AF7FA057C36CA8EF759A6D597C00007158A22E3A556AC57A1299579D49D`. `fixture.sanitized.metadata.json` links it to the private source hash and records that the sanitizer itself does not create an independent oracle. `fixture.oracle.json` is the separately authored oracle from the read-only content audit. It pins 2 sides, 3 parties, 47 positive stacks, 74 units, side totals 30/44, 17 mounted stacks, 11 ranged stacks, 4 hero stacks, equipment coverage, zero wounded units, and zero referential failures.

`CoopAutomationFixture.ContractTests` verifies the exact three-file allowlist, absence of raw capture names, path/account/credential patterns, both hashes and lengths, evidence limits, canonical identities, mission-ready multiplicities, side/party duplicate projections, equipment/mount invariants, production-schema deserialization, ordinary-field admission, and rejection after a non-field mutation. This is Milestone 3 contract replay only. No Bannerlord process, dedicated mission, multiplayer client, cooperative phase, or campaign result is created.
