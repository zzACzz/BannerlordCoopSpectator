# Battle Test Automation Milestone 2B.2D Dedicated Control Channel

Status: **Source, contract, and compile-only implementation complete; staging and Bannerlord runtime validation pending**
Date: **2026-08-31**
Source baseline: **`dcebfae91e959dec7e072b30b8b617fa2730c272`** (`dcebfae`)
Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 11

## 1. Scope

Milestone 2B.2D replaces redirected dedicated-server standard handles as the authoritative bootstrap control path. The exact dedicated module now exposes a default-off, run-scoped, structured readiness/request/acknowledgement contract. The aggregate runner uses that contract before checking UDP visibility or launching the multiplayer client.

This slice does not stage a module, launch Bannerlord or the dedicated server, open a campaign or mission, connect a client, execute a cooperative battle, consume or publish a campaign result, or establish L2/L3 evidence.

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
| Full canonical contracts, `m2b2d-control-contracts-final-20260831-01` | Passed `22/22`; primary and terminal outcome `Pass`; includes the final server-name command-surface hardening; no product process launched |
| Compile-only, `m2b2d-control-compile-final-20260831-01` | Client and dedicated builds exited `0`; installed client, legacy-client, and dedicated inventories unchanged; no product process launched |
| Compile-only client output | Version `0.3.2`; SHA-256 `683E049B1F1178602A483DA6982993108F74D33E372145B317428A8685807E03` |
| Compile-only dedicated output | Version `0.3.2`; SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` |
| Compiled-output ILSpy inspection | Confirmed lifecycle-event binding, `GameNetwork.HandleConsoleCommand`, fixed profile values, and start-game confirmation path |

Contract coverage includes valid identity, sequence, token, role, process, module hash, creation/expiry/lifetime, supported profile values, exact readiness lifecycle identity, exact seven-step terminal history, and reordered-history rejection. Runner contracts require the structured files and prove the standard-input command path is absent from `Feasibility`.

## 7. Evidence boundary and next gate

The source implementation satisfies the Revision 11 dedicated-control design at the contract and compile-only levels. It does not prove that the staged dedicated runtime can load the new binary, observe the native lifecycle event, apply the seven phases, bind the requested UDP endpoint, launch the client, complete lobby handoff, or connect.

The installed dedicated module still has the previously staged SHA-256 `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`; the newly compiled dedicated output SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` is intentionally not installed by this slice. The next runtime gate therefore requires separate approval for exact dedicated-module staging, followed by a clean published-revision `Feasibility` run with explicit expected hashes. That run must retain the ready status, all seven acknowledgements, exact UDP ownership, both loaded-role identities, client handoff/connection, unchanged protected result, PID-correlated logs, and exact cleanup.

Until that named runtime run passes, no connection, campaign, mission, battle, L2, or L3 claim is permitted.
