# Campaign map development checkpoint — 2026-09-28

This is a development checkpoint, not a release or full 1:1 acceptance. It
preserves accumulated Goal-owned work after c2261d8 on codex/v0.1.1-refresh.

## Included work

- Campaign-owned automatic map/battle/map mission lifecycle and session channel.
- Host-authoritative map catalog/dynamic presentation, settlement/body visibility,
  nameplates, camera following with temporary manual detach and explicit return.
- Read-only synchronized dialogue presentation, actor appearance and smooth pose
  progression. Voting, inventory, trading and hero editing remain out of scope
  for this update.
- Host-captured stock tooltip data, bounded request/relay/peer admission, stable
  presenter refresh, and native scene-ray picking for ordinary parties/settlements.
- Focused contracts, source wiring checks and AI knowledge-base updates.

## Verification boundary

Immediately before this checkpoint, cmp-checkpoint-b1 again passed both isolated
builds with unchanged installed inventories and no launched product process.
cmp-checkpoint-c1 again passed all 24 canonical contract projects. Fresh client
output SHA is CDC189DF3487E725EA6D64ADE08DB0D99FB8387B3DCBF8DAEDD48E6D269D3641;
dedicated SHA is 75B37ABF6692AC6E89FD53ADA32B91E981C37E2C69D089C16FB269442623640F.
The fresh client output was not deployed or native-tested; the live evidence below
belongs specifically to cmp-b39, not to an assertion of identical new binary bytes.

cmp-b39 compiled both client and dedicated projects with unchanged installed
inventories. cmp-c41 passed all 24 canonical contract projects. These are managed
verification results, not proof of native runtime completeness.

map-picking-r1-20260928 then loaded the verified candidate in campaign, menu-owned
dedicated and shortcut-launched multiplayer. Automatic map opening and AC_COOP
join worked without a console command. Bounded observations passed ordinary
party/building hover, nearby empty-point clearing, second zoom, camera detach and
return, read-only Tristania dialogue open/text update/close, and Avalyps
entry/body-hide/exit/body-hover restoration. Final multiplayer log contained 967
tooltip requests, zero Denied controls and no picking/presentation-disable message.
No crash was observed. Earlier cmp-b37 flicker correction was confirmed by the
user for normal/extended party cards; this run did not repeat extended Alt cards.

The run was closed: all 22 recorded test-owned identities absent, all seven
configuration hashes restored, no new configuration files or product processes.
The three exact tooltip-channel files were archived and removed. Test saves were
preserved by user policy. cmp-b39 remains installed; module preimages are retained.

## Still open

- Host village hover gold ring is absent in replica.
- Attached-army hover target redirect, main-party-prisoner and non-main raft rules.
- Naval/siege-engine hover and complete host-visible visual parity.
- Latest-candidate battle-return/rejoin across supported battle types and multiple
  peers; bounded land-map checks must not be extrapolated to those cases.
- Existing guarded intermission and distance-cache/FMOD diagnostics prevent an
  exception-free claim.

## Evidence retention and continuation

Raw logs, screenshots, saves, temporary scripts, configuration/module preimages,
installed-game decompilations and generated binaries under artifacts/ are LOCAL
ONLY and intentionally excluded from this checkpoint. Existing documentation
links into artifacts/ are local evidence pointers, not files available in a fresh
GitHub checkout. This summary preserves the acceptance boundary for that checkout.

Local evidence roots: artifacts/verification/cmp-b39,
artifacts/verification/cmp-c41, artifacts/runtime/map-picking-r1-20260928 and
artifacts/runtime/map-tooltip-r2-20260928. The current runtime result has screenshots
01-20, state captures, identity/deployment audits and cleanup/postflight records.
Never reuse recorded historical PIDs for a new test.

Before further native testing, inspect current source and installed identities,
use a fresh evidence directory and preserve exact process/config ownership. The
next raft-rule investigation has not changed source in this checkpoint.
