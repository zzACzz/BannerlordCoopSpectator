# Campaign Map Prototype Runtime Verification — 2026-09-25

## Status

The corrected campaign-map prototype completed a live host/dedicated/client run without a multiplayer crash. The client loaded the isolated `Main_map`, rendered the host-visible settlement and party overlays, preserved the visible issue marker, received later party-position/count changes, and remained alive for more than twenty minutes while the full contract and compile-only checks ran.

This is **not** a literal console-input pass. Windows UI automation could not open the RGL console with `Alt+~`, so the run used the same authenticated web-panel option and `start_mission` path that `coop.map_prototype_start` invokes after command parsing. Therefore mission behavior is runtime-verified, but the literal console parser/hotkey route remains open.

## Runtime candidate

| Role | Installed module SHA-256 |
|---|---|
| Client | `8863874B4B835FABA5861E03AB6644F8FA91233311D7A4EAC24800CA73265A39` |
| Dedicated | `CABE7AF953E876A6FF5E71E7098F805AEC816346045EBEA0A8DEC6EBB8BD191C` |

The dedicated copy exists in both `Win64_Shipping_Client` and `Win64_Shipping_Server` under the installed `CoopSpectatorDedicated` module with the same hash.

The multiplayer client was launched from the user's desktop shortcut whose target is:

`C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\run_mp_with_mod_from_game_root.bat`

No alternate helper executable was used for the successful client launch.

## Procedure and evidence

Evidence root:

`artifacts/runtime/campaign-map-exact-console-20260925/evidence`

| Stage | Result | Evidence |
|---|---|---|
| Campaign started through Steam and loaded | Pass | `09-campaign-loaded-steam-first.jpg` |
| Campaign pause menu opened | Pass | `10-campaign-pause-menu-steam-first.jpg` |
| Coop dedicated panel ready | Pass | `11-server-panel-ready-steam-first.jpg` |
| Dedicated process launched | Pass | `12-dedicated-launched-steam-first.jpg` |
| Dedicated server visible and running | Pass | `13-dedicated-visible-steam-first.jpg` |
| Multiplayer safe-mode prompt reached | Pass | `14-multiplayer-safe-mode-prompt-steam-first.jpg` |
| Client logged in after the user launched the required shortcut | Pass | `15-multiplayer-logged-in-user-launch.jpg` |
| `AC_COOP` visible in the custom server list | Pass | `16-ac-coop-visible.jpg` |
| Client joined and reached `Awaiting Server` | Pass | `17-ac-coop-awaiting-server.jpg` |
| Prototype mission opened and rendered the replica | Pass | `18-multiplayer-replica-marker-parity.jpg` |
| Host comparison showed the same visible marker | Pass | `19-campaign-host-marker-parity.jpg` |
| Host party state changed after a movement order/time advance | Pass | `20-campaign-after-movement.jpg` |
| Client received the same changed party counts/positions | Pass | `21-multiplayer-after-movement.jpg` |
| Dedicated retained/recovered authoritative bridge state | Pass with residual risk | `22-dedicated-live-authoritative-recovery.jpg` |
| Client remained alive after the verification suite | Pass | `23-multiplayer-long-run-stable.jpg` |

Files `01` through `08` retain the first attempt, which stopped at multiplayer login because Steam was not running. They are diagnostic history, not evidence for the successful path.

## Mission-start equivalence

`Commands/CoopCampaignMapPrototypeConsoleCommands.Start` delegates to `DedicatedServerCommands.SendStartCampaignMapPrototypeMission()`. That method:

1. selects `CoopCampaignMapPrototype` and bootstrap map `mp_tdm_map_001` through the authenticated web-panel options;
2. verifies the applied `GameType` and `Map`;
3. calls `/Manager/start_mission` through the same authenticated HTTP channel.

`artifacts/runtime/campaign-map-exact-console-20260925/Invoke-EquivalentMapPrototypeStart.ps1` performed those same option, verification, and start requests without storing or printing the admin password. The successful response at `2026-09-25T21:28:13+03:00` was:

- `SetOptionsStatus=200`
- `VerifiedGameType=CoopCampaignMapPrototype`
- `VerifiedMap=mp_tdm_map_001`
- `StartOutcome=Accepted`
- `StartStatus=200`

This closes the post-parser mission-start path only. A later run still needs a human `Alt+~` plus literal `coop.map_prototype_start` entry if literal console coverage is required.

## Runtime observations

- The client log records successful isolated `Main_map` loading, atmosphere initialization, and render-ready transition. The prior native access violation did not recur.
- The server prepared catalog revision 1 with 397 entities, 98,546 logical bytes, 11,092 wire bytes, and 44 chunks.
- Host and client both displayed Gorcorys, Avalyps, Vostrum, the cyan issue marker at Vostrum, and the relevant party overlays.
- After host movement/time advancement, both views showed the changed purple party count `80` and orange party count `30` in the corresponding region.
- Wheel, pointer, party-click, and `W` input did not produce a clearly visible client camera or selection change. The process remained stable, so this is crash-safety evidence only, not an interaction-feature pass.
- No CrashUploader, WerFault, or crash dialog appeared after mission start.

## Residual runtime risks

1. The client logged twelve swallowed `MPIntermissionVM.OnIntermissionStateUpdated` `ArgumentNullException` instances at `21:16:09`, before the prototype mission opened. The guard prevented a crash, but the upstream null state remains unresolved.
2. The dedicated observer occasionally reported the host-state file as missing and then returned to the same authoritative session approximately 0.1 seconds later. This is consistent with a transient file replacement gap. The visible client stayed synchronized, but the bridge handoff should eventually use a stable-read/atomic-replace contract that avoids observable missing-file intervals.
3. The literal RGL-console hotkey and parser route is not verified.
4. Client camera/selection interaction behavior is not verified.
5. Advancing campaign time triggered Bannerlord autosave of `saveauto1`. No save was restored because there was no exact same-run preimage and replacing user saves would be unsafe.

## Source and contract verification

The full canonical contract run `campaign-map-runtime-contracts-20260925-02` passed `24/24` projects with zero failures. Aggregate result SHA-256:

`85ECBE06FDCF9FC63A563CCA13A8DD3A4AC852A19918BA892E75050CC9774483`

The immediately preceding `campaign-map-runtime-contracts-20260925-01` result was `0/24` because the sandbox denied read access to the existing user `NuGet.Config`; every project stopped before compilation. It is retained as environment-failure evidence and is not a product-test failure.

The final non-deploying compile run `campaign-map-runtime-compile-20260925-01` passed:

| Target | Warnings | Errors | Output SHA-256 |
|---|---:|---:|---|
| Client | 77 | 0 | `90ECA37C622431E9056311AC51311CE4B6F9A99FE69CC6221A8E73091667AC44` |
| Dedicated | 49 | 0 | `C3D5405611CF841A2723AE04132CBF63EDE1FE5393327C525872AB6BCBEA0B3D` |

`compile-only.json` SHA-256 is `9DCCE69C3BE197DF4BC2AE341374B43174CFD874895491831FF03F4472B09C32`. Installed module inventories were byte-for-byte unchanged by this verification.

## Cleanup and restoration

- Stopped only the three test-owned processes: campaign PID `92304`, dedicated PID `92220`, and multiplayer PID `80232`.
- Left Steam and its helper processes running.
- Removed only `Codex Bannerlord Multiplayer Test.lnk`, which was created for an earlier test attempt.
- Preserved the user's desktop `run_mp_with_mod_from_game_root.bat - Shortcut.lnk`.
- Verified `LauncherData.xml` is back to `Singleplayer` and exact SHA-256 `87E5B5F6101A515254622C4E8E5ACC8EAA638182EF074CA18CA057A8656B3ECD`.
- Verified the test-created HKCU `LocalDumps` base/key set is absent.
- Retained the pre-existing `ds_config_coop_listed_test.txt`. The launcher deterministically rewrote it for this run, but no exact same-run preimage existed, so guessing an older value would have been unsafe.
- Final window inventory contained only Codex and Steam.

## Follow-up interaction-candidate run

A later same-day follow-up used the campaign pause-menu button to create exact
dedicated PID `91212`, then retained the exact desktop-shortcut multiplayer
client PID `67452`. The protected Windows-control surface became unavailable
before the client could be rejoined, so this follow-up is server-only evidence
and does not replace the earlier host/client pass.

At `22:49:54`, from a server state with no active mission, the authenticated
post-parser path applied `GameType=CoopCampaignMapPrototype` and
`Map=mp_tdm_map_001`. The exact dedicated log then recorded all of the required
acceptance markers:

- `CoopCampaignMapPrototype: opening isolated network mission`;
- `Opening new mission CoopCampaignMapPrototype`;
- `--Mission is running`;
- catalog revision 1 with 397 entities, 98,558 logical bytes, 11,177 wire
  bytes, and 44 chunks;
- authoritative host session `ef02f7cc9a3a452080fca5daa1a8ca31`.

The dedicated process remained responsive and exception-free until the
controlled `end_mission` request at `22:57:39`, providing more than seven
minutes of fresh server-side stability. No client was connected during this
follow-up (`0 / 100`), so the newly corrected info-prefab layout is installed
but still needs a fresh client rendering/selection pass. Its installed SHA-256
is `B36F2374C5B7B5727A515FE6225821E5E76266898B28AED7919A3A1830C3D4CD`.

The evidence helper
`artifacts/runtime/campaign-map-exact-console-20260925/Invoke-EquivalentMapPrototypeStart.ps1`
was corrected to fail closed. It now binds one exact dedicated executable/PID,
records the starting line count of that PID's native log, and reports success
only after new `opening isolated network mission` and `--Mission is running`
markers appear. A deliberate repeat while the mission was active received HTTP
200 but logged `--Not at correct state, mission is already running or trying to
run`; the corrected helper classified it as `RejectedByDedicated` and exited
nonzero. This supersedes the helper's earlier HTTP-status-only acceptance
classification.

The controlled end request entered native state `Ending`. A second end request
and an immediate start request were rejected with `There is an already working
task`; therefore that attempted repeat is retained as transition diagnostics,
not a successful mission restart.

Fresh non-installing verification after the info-prefab correction passed:

| Run | Result |
|---|---|
| `cmapc3` | All 24 contract projects passed |
| `cmapb3` | Client and dedicated Release builds passed; installed inventories unchanged |

`cmapb3` produced client SHA-256
`5EAFA5452BE3A3F368ED3888E24DA33A3FE0A8A1B8BAFA58F78C72C4C0EBCCDD`
and dedicated SHA-256
`1B8A5833B157A118EA53A41482D07D37E1F76FCBDD596953ED1C4810162B6D68`
under its run-owned compile-only root. These hashes are build evidence only;
the follow-up runtime continued using the already installed client DLL
`A9A5E1DC656295736AEE2DF639A1909D9B309F4514FCEEFFC3268EADE1B3BF7A`
and the same dedicated DLL hash as `cmapb3`.

## Next gate

### 2026-09-26 synchronous-dialogue candidate

The next-update source candidate now adds a display-only synchronous projection of the host's native map dialogue:

- the campaign publisher reads the already formatted `MapConversationVM.DialogController` owned by the active native map-conversation view;
- the host bridge schema is 12 and the map protocol is 16;
- the dedicated server sends a bounded header and indexed answer records using server-to-client-only message types;
- the client applies only a complete revision, supports current-state delivery to a late joiner, and renders the accepted state with the native `SPConversation` movie;
- answer and continue commands on the multiplayer client are no-op, so the campaign host remains the only choice authority;
- barter, voting, progression, inventory/market reservation, and hero editing remain outside this candidate;
- the existing hideout conversation path is unchanged.

Focused `CoopCampaignMapPrototype.ContractTests` passed. Fresh isolated client and dedicated Debug builds both passed with zero errors after the implementation; the client retained one pre-existing `System.Management` assembly-version warning and the dedicated rerun retained zero warnings. These are source/compile results only. The dialogue candidate has not yet been installed as a matching client/dedicated pair or exercised in Bannerlord.

For literal completion, repeat the successful launch/join path, open the RGL console with `Alt+~`, enter `coop.map_prototype_start`, and capture the command, mission open, map interaction, and dialogue open/change/close states on both host and client. Verify that clicking a multiplayer answer has no campaign effect. If the literal path crashes while the equivalent HTTP path remains stable, capture the crash dump and the last client/dedicated log sections before changing mission code.

## 2026-09-26 installed dialogue-candidate run (in progress)

The dialogue candidate was built and installed as a matching pair before this
run. The loaded client module SHA-256 is
`6DEEF5E1609E7BE4CD79F07FB2B78C479046DCC7B7609359AE7DDAA021C5866C`;
both dedicated module copies use SHA-256
`2554804B33815BA96E21E1030809F61DAA4FAF85E764D9EE685023A4F4888820`.

The captured run followed the user workflow through campaign load, the pause-menu
dedicated-server button, the exact desktop multiplayer shortcut, both dependency
and safe-mode prompts, native multiplayer login, exact `AC_COOP` selection, join,
and `Awaiting Server`. Evidence is retained under
`artifacts/runtime/campaign-map-dialogue-20260926/evidence/01-*` through `17-*`.

Because the elevated Windows On-Screen Keyboard retained both Alt keys, protected
UI input could not open the RGL console. The already verified exact authenticated
post-parser helper therefore started the current mission at `01:24:54` against
dedicated PID `60508`. It applied `GameType=CoopCampaignMapPrototype` and
`Map=mp_tdm_map_001`, received HTTP 200 for both the option update and
`start_mission`, then verified new exact-PID log markers for the mission open and
`--Mission is running`. This is mission-runtime evidence, not literal console
hotkey/parser evidence.

The dedicated loaded the current host session and prepared catalog revision 1
with 397 entities, 98,558 logical bytes, 11,177 wire bytes, and 44 chunks. The
connected multiplayer client rendered `Main_map`, host-visible settlements,
parties, counts, labels, the information panel, and the Ukrainian interaction
guide. Wheel input visibly changed the map scale and exposed an additional
Vostrum label while the mission stayed alive. Evidence `18-*`, `19-*`, and `20-*`
captures mission load and the before/after zoom states. Exact-PID log copies are
retained as `21-*`, `22-*`, and `23-*`.

The client processed thousands of monotonically advancing
`CoopCampaignMapPrototypeStateMessage` revisions without a post-open client
exception. It also received the canonical inactive dialogue snapshot
(`Revision=1`, `Active=False`, `Options=0`). An active native host dialogue has
not yet been opened, so open/change/close UI parity remains unverified.

This run reproduced the earlier shared-file gap more clearly: the running
candidate logged 32 transient `missing-file` or `FileNotFoundException` states,
each followed by recovery to the same authoritative host session. Source was
then corrected only in `CoopCampaignMapPrototypeBridgeFile`: state, catalog, and
dynamic readers may reuse only their own last successfully parsed, path-matched,
still-fresh snapshot during a missing/partial/transient read. They still fail
closed after the configured maximum age, and the shared `AtomicBridgeFileIO`
used by adjacent battle flows was intentionally not changed.

The focused bridge contract deletes the live bridge file after populating the
cache, verifies bounded fresh fallback, verifies expired-cache rejection, then
writes a truncated replacement and verifies that it cannot discard the last
validated fresh snapshot. It passes. The dialogue source contract additionally
requires a transient native-view read failure to preserve the previous validated
dialogue rather than emit a false close. Final canonical run `cmdlg-c4` passed
all 24 contract projects. Final canonical CompileOnly run `cmdlg-b4` passed both
products and preserved installed inventories, producing run-owned client
SHA-256 `F6A726C5153E837C4671C2DBDFA2EA701C01CEFE73A255D9EF0A7C734B4416C5`
and dedicated SHA-256
`C6D19880A98FE659637CE9AD4F118E43CCF8D20D0EF60942D3532384DA2549FE`.

Remaining live gates are: remove the external On-Screen Keyboard input blocker;
execute the literal `coop.map_prototype_start`; verify campaign movement/change
propagation; open a native campaign dialogue and verify identical client
open/update/close presentation plus no-op client answers; deploy the final
cache correction and prove that the transient unavailable-state log entries no
longer occur; then restore exact configuration preimages and stop only run-owned
processes.
