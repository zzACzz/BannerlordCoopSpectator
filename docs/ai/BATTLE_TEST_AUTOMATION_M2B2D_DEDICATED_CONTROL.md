# Battle Test Automation Milestone 2B.2D Dedicated Control Channel

Status: **Dedicated control, exact server selection, and native join runtime-verified; formal Connected rerun pending after client observer staging**
Date: **2026-09-01**
Published source revision: **`7628c85aef140e431f29982e016395b8f303a464`** (`7628c85`)
Live-run source revision: **`21042956b7928726dacb50c2de258437d5a65c6e`** (`2104295`)
Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 12

## 1. Scope

Milestone 2B.2D replaces redirected dedicated-server standard handles as the authoritative bootstrap control path. The exact dedicated module now exposes a default-off, run-scoped, structured readiness/request/acknowledgement contract. The aggregate runner uses that contract before checking UDP visibility or launching the multiplayer client.

The source implementation slice did not launch Bannerlord or the dedicated server, open a campaign or mission, connect a client, execute a cooperative battle, consume or publish a campaign result, or establish L2/L3 evidence. Section 7 records the later separately approved on-disk dedicated-only staging transaction. Section 8 records the subsequent bounded live connection-feasibility run, which launched the dedicated server and one multiplayer client but no campaign or cooperative battle.

## 2. Lowest-level native findings

The installed `TaleWorlds.MountAndBlade.ListedServer.dll` inspected for this slice has SHA-256 `C7D27584FCE431B2D3734EB88C8DF52EF3B1BC8C5729F7FCE690CC277DA577E3` and length `28160` bytes.

ILSpy decompilation established the exact native boundaries used by the implementation:

- `InitialListedGameServerState.OnActivate` raises the public static `InitialListedGameServerState.OnActivated` event after the listed-server state becomes active;
- `GameNetwork.HandleConsoleCommand(string)` forwards the command to the active native network handler;
- `ListedServerCommandManager` maps `start_game` and `add_map_to_usable_maps` to `ServerSideIntermissionManager`;
- `ServerSideIntermissionManager.StartGame` applies the native registration/map/game-state gates before starting the selected scene.

The implementation therefore requires neither UI automation nor a Harmony patch. It subscribes to the public lifecycle event through reflection so the project does not gain a private compile-time dependency on the listed-server assembly, then invokes the exact public TaleWorlds command path from the dedicated main tick.

External web documentation was not used as decision evidence. The exact installed binary and its decompiled call path were more authoritative for this version-specific private runtime boundary.

## 3. Structured protocol

The fixed run-root files are:

| Path | Owner | Meaning |
|---|---|---|
| `state/dedicated-control.ready.json` | Dedicated role | Atomic proof that the exact `InitialListedGameServerState.OnActivated` lifecycle event was observed by the expected loaded module and process |
| `commands/dedicated-bootstrap.request.json` | Runner | Fresh allowlisted bootstrap intent |
| `commands/processed/dedicated-bootstrap.request.json` | Dedicated role | Atomically claimed request; duplicate processing is impossible through the inbox path |
| `state/dedicated-bootstrap.status.json` | Dedicated role | Atomic validation, progress, acknowledgement history, or terminal rejection/acceptance |

Schema version `1` accepts only profile `ConnectionFeasibilityV1`, sequence `1`, one GUID command ID, an exact `RunId`, token hash, dedicated role identity, process ID/start time/path, loaded module hash, and a bounded lifetime of at most ten minutes. Its only accepted bootstrap values are:

- server name supplied by the runner and restricted to ASCII letters, digits, dot, underscore, or hyphen;
- maximum players `16`;
- game type `TeamDeathmatch`;
- map `mp_tdm_map_001`.

No arbitrary command string crosses the file boundary. Invalid, expired, cross-run, cross-token, cross-process, wrong-role, wrong-hash, duplicate, or unsupported requests fail closed.

## 4. Dedicated execution sequence

`CoopAutomationDedicatedControlBridge` is initialized only after the existing automation runtime profile validates the run, token, exact loaded dedicated hash, role, and `Suppress` result policy. With automation disabled, its tick returns without file I/O or logging.

After observing `InitialListedGameServerState.OnActivated`, the bridge publishes readiness atomically. It then claims and validates the single request and performs one bounded phase at a time from `SubModule.OnApplicationTick`:

1. apply `ServerName` and read it back from `MultiplayerOptions`;
2. apply `MaxNumberOfPlayers` and read it back;
3. apply `GameType` and read it back;
4. apply `Map` and read it back;
5. invoke `add_map_to_usable_maps` and verify the selected map in the native usable-map collection;
6. invoke `start_game` exactly once;
7. verify the native listed server is playing and revalidate the final option/map/usable-map state.

Only the exact ordered acknowledgement history `ServerName`, `MaxNumberOfPlayers`, `GameType`, `Map`, `UsableMap`, `StartGameRequested`, and `StartGameConfirmed` can produce terminal `BootstrapAccepted`. Any bridge exception produces a structured terminal rejection instead of escaping the game tick.

The runner waits for the ready file and terminal seven-step acknowledgement before starting the UDP-visibility deadline. Redirected stdout/stderr and PID-correlated native logs remain retained diagnostics, but the runner no longer writes bootstrap commands through redirected standard input and does not treat console text as authority.

## 5. Cross-battle impact

This control channel is shared pre-scenario infrastructure. It creates only the fixed vanilla connectivity bootstrap and runs before field, village, siege, sally-out, ambush, relief, lords-hall, hideout, sequential, or reconnect scenario routing. It neither selects nor modifies a cooperative battle adapter. A defect here can block every later scenario, but acceptance here cannot prove any scenario lifecycle or result.

## 6. Verification evidence

| Evidence | Result |
|---|---|
| Focused runtime/runner contracts, `m2b2d-focused-dev-20260831-03` | Passed in Windows PowerShell 5.1 and PowerShell 7.6.4 |
| Clean published-revision contracts, `m2b2d-prestage-contracts-20260901-01` | Passed `22/22`; primary and terminal outcome `Pass`; no product process launched |
| Clean published-revision compile-only, `m2b2d-prestage-compile-20260901-01` | Client and dedicated builds exited `0`; installed client, legacy-client, and dedicated inventories unchanged; no product process launched |
| Compile-only client output | Version `0.3.2`; SHA-256 `7E91F442989DAB426478395052201C2BADBC229EA82FA4B9F8B59FEB4246BEF9` |
| Compile-only dedicated output | Version `0.3.2`; SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` |
| Compiled-output ILSpy inspection | Confirmed lifecycle-event binding, `GameNetwork.HandleConsoleCommand`, fixed profile values, and start-game confirmation path |

Contract coverage includes valid identity, sequence, token, role, process, module hash, creation/expiry/lifetime, supported profile values, exact readiness lifecycle identity, exact seven-step terminal history, and reordered-history rejection. Runner contracts require the structured files and prove the standard-input command path is absent from `Feasibility`.

## 7. Controlled dedicated-only staging

Run `m2b2d-stage-20260901-01` started only after local and remote revision `7628c85` matched, the working tree was clean, both required UDP ports were free, no Bannerlord/dedicated/launcher/crash-reporter process existed, and the clean dedicated output matched SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`.

Preparation copied the complete 218-file installed `CoopSpectatorDedicated` tree, preserving installed-only resources, and changed exactly these two files in the prepared tree:

- `bin/Win64_Shipping_Server/CoopSpectator.dll`;
- `bin/Win64_Shipping_Client/CoopSpectator.dll`.

The apply transaction acquired the dedicated installation mutex, revalidated source, remote, process, port, exclusive-file, pre-image, and prepared-tree identity, moved the complete installed tree to the run-owned backup, and moved the prepared tree into the exact installation path. Postflight evidence is:

| Fact | Observed result |
|---|---|
| Installed dedicated tree | 218 files; fingerprint `AE406714F10354A1B8C437EC4E4D8B2E90DD55B7B8A7CB968822C4E9BC9A465D` |
| Retained pre-image | 218 files; fingerprint `89B1630961307D90943ACFCA2936D1C4EC4ADC57FB59979111B26A6812B1F404` |
| Both installed dedicated DLLs | SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` |
| Both retained prior DLLs | SHA-256 `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626` |
| Installed client tree | 32 files; unchanged fingerprint `19369B25E232718F1291A54853E0FA090B0AE53AC300E731BDD04123AC5BAAEE` |
| Installed client DLL | Unchanged SHA-256 `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928` |
| Protected `battle_result.json` | Length, SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`, and UTC write ticks unchanged |
| Product/port state | No product process; ports `7210` and `7777` unowned |

The retained exact pre-image is:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2d-stage-20260901-01\backup\dedicated\CoopSpectatorDedicated
```

The first postflight check compared a JSON-deserialized `DateTime` through culture-dependent string conversion and raised a false mismatch. No mutation occurred in that check. `artifacts/failure/postflight-comparison-01.json` retains the mistake; the corrected check compared exact length, SHA-256, and UTC ticks and passed.

Post-stage doctor run `m2b2d-poststage-doctor-20260901-01` was `EnvironmentBlocked` only by `InstalledDedicatedHashDiffersFromRepositoryOutput` and `RuntimeVersionCombinationNotYetVerified`. The first reason refers to the intentionally untouched ignored repository output from the earlier build, not the clean build artifact or installed tree. Runtime execution must pass the explicit installed client and dedicated hashes and must not infer identity from that stale ignored output.

## 8. First dedicated-control live proof

Clean published-revision run `m2b2d-live-feasibility-20260901-01` used explicit installed client hash `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928` and dedicated hash `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`. It established all of the following runtime facts:

- the exact staged dedicated module published `ModuleReady` and the authoritative `InitialListedGameServerState.OnActivated`-bound ready status;
- the fixed bootstrap reached terminal `BootstrapAccepted` with all seven ordered acknowledgements;
- `start_game` was confirmed by `IsPlaying=true`, `GameType=TeamDeathmatch`, and `Map=mp_tdm_map_001`;
- the exact dedicated process owned UDP port `7210`;
- the real multiplayer executable launched with the expected parent, path, PID, and module request;
- the client later published exact `ModuleReady` for the selected client hash and reached `WaitingForLobby`;
- `ResultPolicy=Suppress` held, no campaign fixture or cooperative mission opened, and the protected global result remained unchanged.

The aggregate runner stopped before waiting for lobby connection. `ConvertFrom-Json` returned `EntryStartUtc` as an already-UTC `System.DateTime`; the runner converted it to a culture-dependent string, parsed that string as local time, and applied the UTC offset a second time. Exact `22:00:48Z` became false `19:00:48Z`, so a valid client PID was rejected as possible reuse. The same incorrect identity caused automatic cleanup to report `NotRunning`; exact run artifacts later proved the remaining PID/path/parent/start/arguments/status identity, and manual postflight requested graceful close without forced termination.

Final outcome reporting was then superseded by a second runner defect: the collector required `watchdog_log_130464.txt`, but this exact dedicated profile produced only `rgl_log_130464.txt` and `rgl_log_errors_130464.txt`. A verified child `Watchdog.exe` existed, but no server-PID watchdog log was produced. The protected result, all installed module hashes, Git revision, and ports remained unchanged. Final manual postflight confirmed zero product processes and zero owners of ports `7210` and `7777`.

## 9. Revision 12 runner correction

The correction keeps the launcher-owned exact evidence in schema-v4 `client-launch.json`. Its nested process identity includes the original launch operation/window, aggregate-runner parent, exact PID/path/start/hash, role, and path-evidence source. The aggregate runner validates and registers that identity before fallible live revalidation. `ConvertTo-CoopUtcDateTime` handles both ISO strings and already-deserialized `System.DateTime` values without culture-dependent string conversion.

Native-log inventory schema v2 keeps exact `rgl_log` and `rgl_log_errors` files required. A matching watchdog log is captured and validated when present; otherwise the inventory records `Required=false`, `State=NotProduced`, and no false terminal supersession. An existing stale or identity-incompatible file still fails closed.

Focused runner contracts passed in Windows PowerShell `5.1.26100.9168` and PowerShell `7.6.4`. The first canonical attempt used an unnecessarily long run ID and one long-named siege contract exceeded the Windows path limit before test execution; no product process launched. Fresh short-root run `m2d-c-01` then passed all 22 contract projects. These corrections change external scripts, tests, and documentation only; installed game modules require no restaging.

## 10. Evidence boundary and next gate

The dedicated loaded hash, authoritative readiness, seven-step bootstrap, native `start_game`, and UDP ownership are runtime-confirmed. Later clean run `m2e1-live-r1-01` also confirmed exact server selection, native join acceptance, actual `GameNetwork.StartMultiplayerOnClient(...)`, `InCustomGame`, `Join game successful`, server `CreatePlayer`, and visible `Awaiting Server`. The formal controller status remained non-terminal `JoinAccepted`, so the aggregate returned `Timeout`; it did not prove `NetworkHandoff` or `Connected`.

Installed-runtime IL inspection traced that evidence gap to a notification attached only to an absent historical four-argument lobby signature. Published correction `d1af692` moves observation to the actual GameNetwork patch after final address rewrite, and `3fdcda3` retains last-valid non-terminal timeout evidence. Both corrections passed focused tests, full 22/22 contracts, and isolated compilation. The next runtime gate is a separately approved controlled client-only staging transaction followed by a clean hash-pinned `Feasibility` rerun. It must retain both loaded-role identities, the already-proven native join evidence, formal `NetworkHandoff`, terminal `Connected`, required/optional native-log inventory, unchanged protected result, and exact automatic cleanup.

Until that rerun passes, no connection, campaign, cooperative mission, battle, L2, or L3 claim is permitted.
