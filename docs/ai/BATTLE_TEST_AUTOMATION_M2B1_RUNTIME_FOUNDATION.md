# Battle Test Automation Milestone 2B.1 Runtime-Safety Foundation

Status: **Source-, contract-, and compile-only complete through Milestone 2B.2C; dedicated loaded identity confirmed; corrected connection rerun pending**

Implementation date: **2026-08-31**

Working-tree base revision: **`f6b776c97ffd70c422e196f81ada91228a10dec3`** (`f6b776c`)

Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 7

## 1. Outcome

Milestone 2B.1 now has a fail-closed runtime source foundation for one bounded local dedicated/client connection probe. The implementation can bind each loaded module role to an exact expected SHA-256, require a run-scoped owned-host identity, suppress campaign-consumable battle-result publication, enforce bounded process ownership and exact cleanup, and inspect or recover an existing run without launching a new product process.

No Bannerlord client, dedicated server, campaign, mission, or battle was launched while implementing or contract-testing this slice. Source completion is not runtime evidence. At implementation completion, the installed `0.3.2` modules still contained the older binaries compiled from revision `f91eeff`; the later [Milestone 2B.2A staging operation](BATTLE_TEST_AUTOMATION_M2B2_STAGING.md) replaced them with the clean committed runtime-safety build without launching a product process. A subsequent [first live feasibility attempt](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md) launched the dedicated role and confirmed its exact loaded hash, but did not reach server visibility, client launch, a campaign, or a battle.

## 2. Native bootstrap correction

The earlier connection-only plan assumed that the dedicated role could become discoverable and bind its local UDP endpoint before any mission-start command. Lowest-level observation disproved that assumption: the dedicated process can load and authenticate, but it does not expose the requested server endpoint or normal lobby listing until standard `start_game` handling creates a server mission.

The corrected `Feasibility` path therefore uses this strict sequence:

1. validate exact installed client and dedicated module hashes;
2. enable the complete run-scoped automation profile with result policy `Suppress`;
3. launch the exact dedicated process and require its loaded-role hash acknowledgement;
4. issue only standard server-name, player-count, `TeamDeathmatch`, map, usable-map, and `start_game` commands;
5. require the UDP owner to be the dedicated process or its verified descendant;
6. publish a token-bound `state/dedicated-host.json` record;
7. launch the exact client through the existing normal-lobby control path and require loaded-role plus `Connected` acknowledgements;
8. stop only recorded exact process identities and verify the global battle result did not change.

This is a vanilla connectivity bootstrap. It uses no campaign save, cooperative battle fixture, battle-phase claim, campaign writeback, or L2/L3 pass classification.

## 3. Implemented safety boundary

### Runtime role and policy contract

- `Infrastructure/Automation/CoopAutomationRuntimeContract.cs` validates the exact temporary run root, sufficiently strong token, expected module SHA-256, and the only supported Milestone 2B result policy: `Suppress`.
- `Infrastructure/Automation/CoopAutomationRuntimeBridge.cs` hashes the actually loaded assembly, writes exact role/process identity under the run root, validates run-scoped owned-host records, and resolves result-publication policy.
- Client and dedicated `SubModule.OnSubModuleLoad` paths remain production no-ops unless the complete automation profile is explicitly enabled. An enabled but invalid profile fails closed before the role can claim readiness.

### Result and production-bridge isolation

- `CoopBattleResultBridgeFile.WriteResult` preserves normal production behavior when automation is disabled.
- A valid automation run records `Suppress` below the run root and returns without writing the campaign-consumable global `battle_result.json`.
- An invalid enabled automation profile rejects publication rather than falling back to the production path.
- Automation does not read, write, or delete the production `host_self_join.marker` or `host_local_peer.marker`. Local-host association instead requires the run-scoped owned-host record plus the live UDP endpoint.

### Runner ownership, cleanup, and recovery

- `scripts/Invoke-CoopTest.ps1` adds `Feasibility`, `Inspect`, and `Recover` commands.
- `Feasibility` blocks pre-existing product processes and occupied target ports, records each created role identity, discovers descendants, and revalidates PID, executable path, and start time before termination.
- `Inspect` is read-only and launches no process.
- `Recover` is read-only by default. `-ApplyRecovery` first acquires the abandoned run lock and then stops only still-matching recorded identities; it never deletes the run root automatically.
- `scripts/Start-CoopBattleTestClient.ps1 -UseExistingRunContract` inherits the runner's token/root/result policy instead of creating a second run authority. It additionally requires the matching `Feasibility` manifest, nonce fingerprint, expected client hash, and an actively held aggregate-runner lock. Standalone live launch is rejected before run-root creation; standalone `-ValidateOnly` remains supported.

## 4. Contract evidence

| Check | Result |
|---|---|
| `CoopAutomationRuntime.ContractTests` | Passed: production publish behavior, strict runtime configuration, loaded-role hash identity, `Suppress`, exact live owned-host identity, mismatched host rejection, and invalid-policy rejection |
| `CoopBattleResultCampaignGuard.ContractTests` | Passed after linking the real runtime bridge source graph; valid `Suppress` left global `battle_result.json` unchanged across field, village, siege assault, sally-out, siege ambush, hideout, and lords-hall result types; invalid automation rejected without production fallback |
| Full reviewed inventory, final run `m2b1-final-contracts-20260831-03` | Passed 21/21 projects; no product process launched |
| Client/dedicated compile-only, final run `m2b1-final-compile-only-20260831-02` | Passed; client SHA-256 `D7C2B71E995065B9CA0D688B5DDF30730DE57F1B9EEF3F63778D9C8C9E98C189`; dedicated SHA-256 `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`; installed inventories unchanged |
| Clean committed revision verification, runs `m2b2-contracts-20260831-01` and `m2b2-prestage-compile-20260831-01` | Passed 21/21 projects; client SHA-256 `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`; dedicated SHA-256 `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`; installed inventories unchanged during compile-only |
| Milestone 2B.2C runner-correction verification, runs `m2b2c-runner-focused-20260831-07`, `m2b2c-full-contracts-20260831-03`, and `m2b2c-compile-only-20260831-02` | Passed in Windows PowerShell 5.1 and PowerShell 7; passed 22/22 projects and client/dedicated compile-only; installed inventories unchanged; no product process launched |
| `Inspect`, recovery preview, and zero-process `Recover -ApplyRecovery` | Passed against a completed run; valid manifest/lease reported; no process action performed; malformed or cross-run recovery metadata is fail-closed by source contract |
| PowerShell parser validation | Passed in PowerShell 7 and Windows PowerShell 5.1 for the aggregate runner and client launcher |
| Repository EOL/hygiene policy | Passed with `-AllowDirty`; all modified and new text files are LF; the only normal hygiene failure is the intentionally uncommitted working tree |

A deliberately invalid-hash `Feasibility` request stopped at `PreconditionsFailed` before any product process launch during the original implementation step. Live run `m2b2-live-feasibility-20260831-01` later confirmed the exact dedicated loaded hash and `Suppress`, then exposed shared runner defects in command emission and descendant discovery. Exact recovery stopped the recorded dedicated identity gracefully; the corrected source and contracts are documented in [BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md).

## 5. Evidence boundary and next gate

This milestone now establishes `ConfirmedLoadedHash` only for the dedicated role and proves exact recovery for the interrupted attempt. It does not establish the client loaded hash, a supported connected client/dedicated version combination, a lobby connection, correct acceptance of the corrected six-command bootstrap, crash-reporter behavior, or result suppression inside a real mission.

The game-side binaries remain the exact clean revision `12abf36` outputs documented in [BATTLE_TEST_AUTOMATION_M2B2_STAGING.md](BATTLE_TEST_AUTOMATION_M2B2_STAGING.md). Clean runner revision `70a40db` proved that `ModuleReady` preceded native console readiness and no UDP listener appeared. Milestone 2B.2C implements and contract-tests authoritative native-output readiness, exact command/start readbacks, singular runner results, and PID-correlated native-log capture without changing game-side source. After review, commit, and push, the next separately approved gate is one clean pre-campaign live rerun. It must preserve the no-L2/L3 boundary. See [BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md](BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md).
