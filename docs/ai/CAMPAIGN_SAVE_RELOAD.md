# Campaign save reload without restarting the owned server

## Authoritative user decision — 2026-10-27

Loading an older save after a defeat, loading another save by mistake and switching
to another campaign are normal host actions. Do not permanently reject the new
campaign or require manual server restart. Retire the old battle state and resume
the already-owned server with the loaded world. A fresh load of the same persisted
CampaignId MUST also reset the runtime generation.

The b276 early battle-start preflight only prevents expensive retries against a
faulted old binding. It is not the requested recovery and must not be described as
the completed save-reload fix. It remains a useful temporary readiness barrier
while the explicit reload/acknowledgement transaction is being implemented.

## Evidence from manual r58 — 2026-10-26

Campaign13788, dedicated70356 and multiplayer63568 ran b274. Retained raw logs:
`artifacts/investigations/aftermath-siege-save-reload-20261026/evidence`.

- Siege final result at08:45:27; PartyScreen capture first reports a transient
  data-source mismatch, then repeated `aftermath-tree-invalid` at08:45:39,49,59.
  PartyScreen remains until08:46:00. Loot opens afterward and the user sees it.
  The exact tree rejection constraint is unknown; do not assume it is byte size.
- Save-load begins08:47:13. Old mission host faults on runtime campaign replacement
  at08:47:15. New map publication is active08:47:24. Citadel mission opens08:47:37;
  its damage logic attaches08:47:48. This is not proof of a native loader deadlock.
- The persistent BattleDetector repeatedly rebuilds a roughly550-entry roster and
  rewrites it while SendStartMission rejects the old owned session. Repeated start
  attempts continue through08:49:40. The user closes multiplayer/dedicated around
  08:49:29..32, after which campaign play becomes available. This repeat workload
  is a confirmed defect; it does not prove that all native loading cost disappears
  once the workload is fixed. Shader-cache write warnings also exist, without proof
  they caused this delay. Do not conflate those observations.

## Current candidate; native acceptance pending

- b275 adds fixed codec rejection codes and host capture opt-in
  `COOPSPECTATOR_AFTERMATH_CAPTURE_DIAGNOSTICS=1`. No limits were relaxed; it logs
  only fixed constraint codes through the existing throttled failure message.
  Default behavior does not expose diagnostic details or extra native reads.
- b276 adds `CanAttemptBattleStart` before scene/roster preparation in the shared
  campaign/TCP-host entry. Both isolated builds and detached checks pass. The
  compiled-admission check fails b275 and passes b276. This is temporary preflight,
  not save-reload recovery. Installed b274 remains untouched.
- b278 connects `OnSessionLaunchedEvent` to the host mission/tooltip rebinding.
  The original process-pair session survives. Each loaded Campaign object gets a
  new epoch and increasing reload ordinal, including same-CampaignId rewinds.
- `CoopCampaignMissionReloadChannel` carries bounded reset requests and correlated
  acknowledgements. Ordinary envelopes are now CML2 and carry the epoch. The server
  admits only its original host identity and its own PID/path/start identity.
  Post-reset admission is pinned to the acknowledged epoch/CampaignId/process pair.
- `CoopCampaignMissionReload` closes the exact owned map/battle once and waits for
  native ready intermission. No result application is required for deliberate
  cancellation. Successive loads coalesce without duplicate closes; stale ordinals,
  acknowledgements and retired epochs cannot revive the old battle. The OpenNew
  asynchronous opening gap cannot be acknowledged as completed native teardown.
- The dedicated adapter clears retired snapshot/topology/runtime registries and
  its pinned roster only after native teardown, resets lifecycle/admission and
  persists the exact acknowledgement before committing the reset. Host battle
  preparation remains gated until that acknowledgement. BattleDetector now drops
  old result leases on Campaign OBJECT replacement, not just persisted ID change.
- Build run `cmp-b278`: both roles exit0, installed inventories unchanged.
  Canonical `cmp-c181`: all26 contract projects pass. Detached compiled mission
  admission, loot inspection and aftermath binding checks pass. These do not
  establish native teardown, client reconnection or save-reload acceptance.
  Earlier c180 was25/26 due to an executable-path length failure; it remains failed.
- r59 staged nine coherent b278 files and retained seven config preimages under
  `artifacts/runtime/map-save-reload-r59-20261027`. Campaign63908 started at
  2026-10-27T06:52:16.4511712Z with scoped capture diagnostics1; loaded client MVID
  `d4c79fed-88de-4e4d-af74-250ad1704b89` and all nine installed hashes verified.
  User will start the server/MP and repeat siege -> aftermath -> load citadel save
  without closing the server/MP. No native reset outcome has yet been observed.
  Client SHA256 `D4CD1141C07E360CF1B2B0799DD8C1BB22897C1ACF0E20FC47AABD982F1F5DBC`;
  dedicated SHA256 `A48F3409B1C5CD38AE77DE92C5372B4413AFCFAEEC18BB9EA0E8CFFBC29BAEBF`.
  CML2 is incompatible with b274's CML1 local mission channel: matching candidate
  host/server binaries are required.
  Network protocol65/bridge60/tooltip5/catalog10 and CAF2 remain unchanged.
- r58 cleanup is complete: all three roles already exited and all seven config
  preimage hashes were restored/verified on2026-10-27. Steam and saves preserved.

## Implemented sequence; native verification still required

1. Observe completion of every campaign load, including same-CampaignId rewinds.
   The current publisher's campaign session-launched event is a suitable hook;
   startup RegisterEvents can run before saved behavior state finishes restoring.
2. Preserve the original exact host/dedicated PID/path/start ownership pair. Create
   a fresh load epoch and monotonically increasing reset ordinal. Do not adopt any
   process or change ownership merely because CampaignId changed.
3. Send an explicit reset request on that owned local channel. The server admits
   the process pair, session, freshness, ordinal and epoch. Retain strict rejection
   of unsolicited in-session identity changes; resetting is a separate operation.
4. Cancel the old owned map/battle through the existing native ending flow. A
   deliberate save rewind must NOT require applying the abandoned battle's result.
   Do not close unrelated missions, synthesize victory, sleep a fixed delay, kill
   processes or restart Steam. Coalesce a later load while close is in flight.
5. Once native teardown reaches ready intermission, clear retired battle/runtime/
   topology/result/request stores owned by that generation. Preserve dynamically
   registered native resources until their consumers have finished teardown.
6. Persist an ack for that exact reset epoch after cleanup. Reinitialize the
   lifecycle/admission history for the new epoch and loaded CampaignId. Pin an epoch
   on ordinary mission envelopes so delayed pre-reset files cannot be re-admitted
   merely because the admission history was cleared.
7. Only after the ack may the host queue the loaded world's next battle. This
   converts preflight from permanent rejection into a temporary readiness barrier.
   A pending queue must not repeatedly rebuild its large roster while waiting.
8. Refresh map publication and tooltip bindings. New map sessions revoke client
   menus/cards/interests; keep the process ownership channel alive across loads.
   `CoopCampaignMapTooltipHost.Tick` waits during campaign replacement; its loaded-
   world hook refreshes the campaign while retaining process ownership/round history.
9. Reset BattleDetector's per-world context on runtime campaign OBJECT replacement,
   not only persisted CampaignId change. The context now compares both identities.

## Native/contract acceptance

### r59 observed result — 2026-10-27

Campaign63908, dedicated37308 and MP21772 loaded coherent b278 identities. The
dedicated process PID/start identity remained unchanged across the user's load.

- Siege after battle: capture failed at09:03:23 with the opt-in fixed code
  `aftermath-tree-invalid:byte-limit`. Earlier data-source mismatch was transient.
-09:04:08.423 host queued the loaded world for reset;09:04:08.549 DS closed the
  old owned mission;09:04:08.815 DS cleared retired stores and wrote its ack;
  09:04:09.458 host accepted it;09:04:09.648 DS opened the new map.
-09:04:18.166 campaign opened SiegeLordsHallFightMission. DS started the matching
  `empire_castle_keep_a_l2_interior` battle09:04:26.769; MP admitted its topology
  contract09:04:26.925. The user confirms the battle loaded successfully.
-09:06:17.104 DS ended that battle;09:06:17.668 it started the map again.
- This proves the reported map-backed save-switch sequence on one machine. It
  does not prove every same-ID rewind, rapid-load, mid-active-battle cancellation,
  replacement-process or two-machine case. Keep the broader matrix open.
- The user reports prisoners absent after both stages, a naked main hero on
  citadel Loot and the whole army in the keep unit selector. These are tracked in
  the aftermath document; save-reload success does not accept those features.
- r59 CLOSED after all three roles exited. Exact DS map change restored; all7
  configuration hashes match preimages. Cleanup timestamp07:34:56.5280621Z.
  Steam/saves preserved. Raw final logs and copied roster/result evidence remain
  under `artifacts/runtime/map-save-reload-r59-20261027/evidence`.

Cover load during map, deployment, active battle, after defeat, prisoners, loot and
citadel. Cover same save, older save of the same campaign, another campaign, rapid
successive loads, queued old result/tooltip, and exact process replacement. Field,
village, siege, sally-out, ambush, relief, citadel and both hideout modes share the
reset path. Unit tests of the reconciler do not accept native cleanup or recovery.

The after-siege prisoner rejection remains a separate investigation; no archive
or full Goal acceptance is justified until both problems and broader gates pass.
