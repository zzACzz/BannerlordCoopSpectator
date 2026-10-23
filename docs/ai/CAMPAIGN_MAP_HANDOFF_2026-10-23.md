# Replica development checkpoint / model handoff

This is a user-authorized development checkpoint, not a release or full Goal
completion. Read AGENTS.md, README.md and GOAL_MODE.md, then the focused siege
investigation and CAMPAIGN_MAP_TEST_RUNBOOK.md. Revalidate actual state before action.

## Current candidate and evidence

- Installed b231, protocol60/host bridge55/catalog10, CSF2/CSP4/CSO10.
- Isolated build: siege-flight-cleanup-b231-20261023; both roles passed, installed
  inventories unchanged during compilation. c170 passed all26 contract projects.
- User reports siege shots now correct and smooth, including repeated siege and
  menu opening/closing. Do not infer fast-forward, all projectile types, castle,
  assault-return, multiple remote clients or impact/sound parity from that report.
- Client/campaign MVID aeb78a5c-2a70-4019-8942-e556621cf3b7; dedicated MVID
  a16ae152-3a01-4ca0-b2ce-a0181f969a28. All three loaded identities checked.
- Local r44: artifacts/runtime/map-siege-flight-r44-20261023. Seven module files
  staged with preimages; seven configuration preimages. Run is now CLOSED:
  elevated inventory found no game/server roles; all seven config hashes match
  after restoring the inspected generated server-map change. Steam/saves preserved.
- Last observed campaign19548, dedicated53852, MP52696. These are historical
  identities, not authority to operate on a reused PID. Recheck path/start time.
  Only the agent-launched campaign is in owned-processes.json; user launched
  server and MP. Ask before stopping user roles. Preserve Steam and saves.

## Implementation and launch boundary

Stable shot IDs, bounded detached trajectory capture after native calculation,
parallel-worker handoff, retained delivery history, versioned transport and client
flight evaluation replace sparse visible-position sampling. Orientation follows
the native forward chord, with separately captured prefab scale. No damage or
campaign authority moved to the client. See SIEGE_PRESENTATION_PLAYBACK_2026-10-23.md.

The observer is default-off: COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER=1 must be present
in the campaign before startup, in addition to map and automatic lifecycle flags.
r44 Launch-Campaign.ps1 scopes these flags to the child and restores its parent
environment. Ordinary manual campaign launch does not enable this new observer.
MP uses the user's run_mp_with_mod_from_game_root.bat desktop shortcut. Do not
create duplicate MP clients; agent launch previously sometimes failed Steam login.
Local artifacts/scripts are NOT included in Git; do not assume a clean clone has
them. Audit and recreate a fresh transaction from the runbook when necessary.

## Next actions

1. Current r44 is closed. Recheck process state and create a fresh run before testing.
2. Verify actual flight-bearing payloads and remaining pause/fast-forward, siege
   exit/re-entry, castle, assault-return and remote-peer cases. Keep user reports
   distinct from direct measured evidence. Native hook removal remains unverified.
3. Review projectile trail/impact effects and sounds separately; disappearance
   alone must never synthesize a hit. No broad completion claim is justified.
4. Review observer rollout only after acceptance; do not silently enable it globally.
5. Continue full host-visible 1:1 replica goal: read-only players, host camera with
   temporary manual detach/return, synchronized dialogues, map/battle/map lifecycle.
   Trading, hero editing and voting remain outside this increment.

## Checkpoint scope and cautions

This checkpoint preserves accumulated replica/UI/dialogue/siege implementation,
tests, related battle compatibility fixes and documentation since 0ed6c90. It is
intentionally not presented as one production projectile fix. Local logs, dumps,
decompiled game code, installed binaries and preview archives are excluded.
Chronological README entries describe older stages; newest evidence above wins.
Review found duplicate GauntletUI references in CoopSpectator.csproj; preserved
in this checkpoint rather than mixing an untested project cleanup into it.
Do not run an ordinary build: it deploys. Use Invoke-CoopTest.ps1 CompileOnly.
