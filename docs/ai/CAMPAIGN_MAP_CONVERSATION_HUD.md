# Campaign-map conversation HUD

## Status and scope (2026-09-29)

Source/build/contract candidate with partial native results: cmp-b59 displayed
two silhouettes; cmp-b60 restores the defender portrait but not the host portrait.
The first three runs are closed with seven configurations and five cmp-b57 module
preimages restored. Fresh cmp-b62/r4 restores BOTH portraits, with matching29/22
counts/power bar, retained portraits after text update, read-only MP answers and
dialogue-close/map/zoom/camera-return checks. Opening and closing a host dialogue
under MP Options also passed, including map input recovery. Native Quit/rejoin,
post-rejoin input, village victory/map return, post-return zoom and user-assisted
host-camera return passed. r4 is closed with seven config preimages matching and
no owned processes remaining. cmp-b62 is retained as the bounded native candidate.
Full HUD parity is NOT accepted. The earlier cmp-b57 paired greetings 20/21 in
`artifacts/runtime/map-lifetime-r1-20260929` prove missing upper party/power HUD.
The new candidate projects that HUD and the speaker banner into the existing
stock SPConversation movie. It does not claim full dialogue parity, persuasion,
facial/voice parity, voting, trade or hero editing.

## Exact installed-code investigation

Generated local research: `artifacts/native/conversation-hud-20260929` (do not
publish proprietary decompilation). All five inspected DLL file versions are
1.0.0.0; these stamps do NOT identify a supported game release by themselves.
Paths are relative to the installed Bannerlord game root.

| File | SHA-256 |
|---|---|
| bin/Win64_Shipping_Client/TaleWorlds.Core.dll | 3E5CECE8CA42FAF3FDDE27632519ED2A6F86D1EC6D1A742867729F00001689BF |
| bin/Win64_Shipping_Client/TaleWorlds.Core.ViewModelCollection.dll | 5A48A04FF42B1B448FB8DEF314608A402FD4F872174C3A4CD858D5E866F44B23 |
| bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.ViewModelCollection.dll | 5D817A46456D07B5E4382A870795FD9F8D6F11B4707A91D8A8B5D5CD50E762A5 |
| Modules/Native/bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.GauntletUI.dll | 75B070E0B1347C3F6C7E4526EDC5370BBA18B4D4B4747C115669E23477AD44E2 |
| Modules/Native/bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.View.dll | FFAAC5ED867CD299F8C14F58FFEE64F86E50D8B197DB5F966759524C3E574BA1 |
| Modules/Native/GUI/Prefabs/Conversation/SPConversation.xml | 7D714940B28ED15B6E0D28BF14C296E0454EC468F1388E72D0339F7E562352E5 |
| Modules/Native/GUI/Prefabs/Conversation/SPConversationAggresivePartyItem.xml | D745902EA915356DA70D0E09F0FAB59643988282D17C812F29ECA5B4369DF61F |
| Modules/Native/GUI/Prefabs/PowerLevelComparer.xml | 8CDC2246BC0CA1C9285436E548D16CC219C1436A9A2384DAC8DFE79EDC50CCC5 |

Native facts:

- MissionConversationVM.OnAgressiveStateUpdated builds ordered leader/other-party
  lists only for hostile PartyEncounter context. Any IsInfoHidden participant
  replaces the actual powers with 0/1 or 1/0. Never recalculate them in the replica.
- ConversationAggressivePartyItemVM already contains HealthyAmount, LeaderVisual
  and up to two quest markers. Capture those fields, not the underlying party.
  CampaignUIHelper.GetCharacterCode returns empty for hidden hero information.
- Non-aggressive native state may retain stale power/lists. Capture none of them
  while IsAggressive is false. Likewise gate banner/clan hint by IsBannerEnabled.
- SPConversation uses its stock six-value PowerLevelComparer prefab, leader and
  other-party templates, with separate speaker BannerImageIdentifierVM.
- CharacterImageTextureProvider accepts a self-contained CharacterCode (equipment,
  body, sex, hero flag, formation, colors, race), not a campaign hero ID. Empty
  identifiers use the native silhouette. CharacterThumbnailCache resolves native
  resources and owns reference-counted thumbnails; this is runtime-sensitive.
- Party hints in the installed conversation party template are disabled. Replica
  commands remain no-op; no campaign tooltip provider is installed.

## Implementation and limits

`CoopCampaignMapConversationCapture.CaptureHud` reads the already admitted native
dialogue VM. Optional unsupported HUD projection falls back to text/tableau.
`Infrastructure/CoopCampaignMapConversationHud.cs` owns detached records, deep
clone/equality, canonical bounded binary encoding and validation. Limits: 32 other
parties per side, two markers, 4096 portrait characters, 300000 encoded HUD bytes.
No truncation of portrait/equipment codes is permitted. Out-of-bound HUD is absent,
not silently reinterpreted as a smaller complete army.

Protocol 31 / bridge schema 26 / catalog 8; payload magic CDP2. Required local
ConversationHud field and native chunked payload carry the same descriptor with
text/tableau under one conversation revision. Close rejects any retained HUD.
No new client command, authority or message direction is introduced.

`UI/CoopCampaignMapConversationHudVM.cs` exposes fixed stock provider bindings;
provider names and custom texture sizes are NOT network-controlled. Portraits
receive structural equipment validation, body/race/resource preflight before
asynchronous native image generation. Unavailable resources yield silhouette;
that is a safety fallback, NOT visual parity acceptance. Banner parsing is bounded
and guarded. Stock thumbnail references remain owned by stock image widgets;
the existing movie removal lifecycle releases them. Detached VM lists are finalized.

No installed prefab changes, global provider patches or default-on diagnostics.
Normal repeated text updates still rebuild the existing read-only conversation VM;
native testing must check portrait retention/cache behavior and no flicker.

## Adjacent scenarios audited before changes

- Map mode alone instantiates CoopCampaignMapConversationView.
- Field/village/relief/sally-out paths use the separate CoopBattlePowerScoreView;
  siege assault does likewise. Neither that view nor shared stock templates change.
- CoopHideoutBossConversationVM retains its separate host choice authority and
  non-aggressive placeholder HUD. No change to its duel/all-battle logic.
- Every battle-return path can reopen the map conversation and remains a native
  regression requirement; the bounded field-victory cmp-b57 pass is older evidence.

## Verification and next gates

- cmp-c59 and cmp-b58 did not reach source verification: sandbox denied reading
  user NuGet.Config. cmp-b58 installed inventory remained unchanged.
- Fresh permitted cmp-c60 passed all 24 projects (1560 HUD assertions); cmp-b59
  passed both isolated builds with installed inventories unchanged. No product
  process launched by these commands. Fresh map-hud-r1-20260929 is prepared for
  native staging and is now launched; native acceptance remains pending.
- ConversationHudTests covers hidden powers/portraits, deep clone/revision changes,
  malformed input, allocation limits, truncation/fuzz, bridge and atomic payload.

Required native gates: paired hostile greeting upper bar/portraits/counts/colors;
ordinary clan speaker banner/hint; non-aggressive/close clears; text update and
repeat opening; read-only answers; Options open/update/close deferral; camera and
tooltip recovery; reconnect and map/battle/map; exact cleanup/config restoration.
Unknown resources and hidden-host-information cases need explicit negative proof.

### First native run and static-mirror correction

cmp-b59/map-hud-r1-20260929 joined AC_COOP and opened the automatic map mission.
Paired Tristania dialogue shows matching speaker banner/text/choices; MP click did
not advance the host. Host advanced/closed normally. Hostile greeting 29/22 and
power bar appeared, but both MP portraits were silhouettes: bounded HUD parity
FAILED. Nonempty host codes were retained in bridge evidence. The new resource
preflight omitted the existing static-mirror resolution used by map/tableau actors.

Follow-up translates only missing campaign equipment identifiers to verified
preloaded mirror items, preserving body/colors/formation/modifiers and rejecting
unresolved resources. No new object registration, provider patch or battle-path
change. Pure tests cover unchanged/mirror/missing/invalid/hidden cases. cmp-b60
both isolated builds and cmp-c61 all24 projects passed. Fresh map-hud-r2-20260929
native acceptance remains pending; this cause is not runtime-confirmed yet.

r1 closed at07:09: all22 exact owned identities absent, seven configs restored,
no new configs/products, five cmp-b57 module preimages restored. Reused PID31904
was preserved because its identity differed. No native battle/modal/rejoin pass
on cmp-b59. r2 is now staged and campaign12236 launched; Steam34536 startup Path
race was recovered from empty baseline, exact path/start and bootstrap log, with
no duplicate launch. All initial descendants recorded.

### Second native run and bounded diagnosis

cmp-b60/r2 loaded campaign12236, menu-owned dedicated22888 and shortcut MP39976.
Safe Mode No, AC_COOP join and automatic map passed. Paired hostile greeting
29/22 retained counts/bar; defender portrait now renders, host still silhouette.
Evidence is local under r2/evidence/01-campaign-hostile.png and
02-mp-hostile-partial-portrait.png with hostile-first bridge copies/final logs.
The host code includes dragon_banner, absent from the static mirror XML; the
installed definition exists in StoryMode/story_mode_items.xml. This is a likely
remaining resource rejection, NOT yet a runtime-confirmed reason.

r2 closed07:25: all22 exact identities absent, seven configs match, no new config
or product processes; allfive cmp-b57 module preimages restored. No native
battle/modal/rejoin acceptance on this candidate.

Follow-up SafePortrait diagnostic preserves every admission/fallback decision.
It logs only a failed existing check's bounded category/item/modifier/exception
type, at most32 times per process, under explicit VerboseDiagnostics. It adds no
native queries, reflection, body codes, player identities or exception messages.
Rationale: determine the actual fallback before changing native resource policy.
Source wiring assertion covers opt-in/bound. cmp-b61 both isolated builds and
cmp-c62 all24 projects passed (1567 HUD assertions). Native r3 confirms the exact
reason below; this diagnostic remains opt-in and bounded for future unavailable
campaign resources without adding risky native queries.

### Third native run and StoryMode resource supplement

cmp-b61/r3 loaded campaign48048, menu-owned dedicated53456 and shortcut MP39580.
Safe Mode No, user Login, native AC_COOP join and automatic map passed. Paired
hostile29/22 HUD still has only the defender portrait. MP log39580 at07:41:47.591
records `Reason=item:dragon_banner`: the remaining rejection is runtime-confirmed.
Evidence: artifacts/runtime/map-hud-r3-20260929/evidence, paired screenshots,
hostile-first bridge copies and final logs. Closed07:43:31 local: all22 exact
identities absent, seven configs restored, no new configs/products, allfive
cmp-b57 installed module preimages restored; test saves retained.

Source follow-up appends one static mirror `cs_mirror_dragon_banner_a60398b5`.
Its source is installed StoryMode/ModuleData/story_mode_items.xml, item dragon_banner;
only id and multiplayer_item differ (empty XML element syntax is normalized).
The suffix is the existing registry's invariant-lowercase FNV-1a hash.
No base-catalog rows/order change, new StoryMode runtime dependency, partial
portrait stripping, runtime object registration or altered fallback policy.
The base generator is not present in this checkout: the supplement is explicitly
marked and must be preserved on future regeneration. Tests cover unique IDs,
append-only placement, exact banner resources, preserved slot4 and safe rejection.

Shared-scenario audit: field/village/siege/hideout consume the same preloaded
catalog, so client and dedicated XML must be staged together before fresh process
startup. The battle mirror equipment builder still maps weapons0..3 and armor/mount
slots, not slot4; no spawn/loadout rule is broadened. The banner effect already
exists on other catalog banners. Native cloth metadata names dragon_banner_weapon,
but actual MP texture/resource availability still requires a native portrait test.
cmp-b62 both isolated builds and cmp-c63 all24 projects passed, installed inventories
unchanged. No full parity, broader battle or resource-regression acceptance yet.

### Fourth native run: both portraits restored (closed)

cmp-b62/r4 fresh campaign21344, menu-owned dedicated9796 and desktop-shortcut
MP11760 loaded verified identities; client/dedicated static mirror catalogs match.
After user Login, native AC_COOP join and automatic map passed. Evidence02/03 in
artifacts/runtime/map-hud-r4-20260929 shows both hostile-greeting portraits and
29/22 counts/power bar. The host no longer falls back to the silhouette with the
Dragon Banner equipped. MP answer click did not advance the host (evidence04).
Host leave answer synchronized the next line and retained portraits (05); closing
returned to the map without the dialogue HUD (06). Zoom detached and the return
button restored host camera follow (07/08). These are bounded visual checks, not
pixel-identical portrait pose or full dialogue parity claims. The run is still
live; modal/rejoin/battle regression and exact cleanup/config restore are pending.

r4 additional bounded modal gate: user opened Escape; Options remained active
while host began a second hostile dialogue29/21. Cancel restored the latest
conversation with both portraits/counts/bar (evidence09-12). Host leave/continue
then closed that dialogue while MP Options remained open (13/14); Cancel returned
to the latest map without retained HUD (15). Wheel zoom/manual detach and explicit
host-camera return passed (16/17). No settings were edited. Reconnect, battle
return and cleanup remain pending; injected Escape still requires user assistance.

r4 native Quit/rejoin subsequently passed without any product restart (18/19).
The original session and 398-entity catalog persisted. Post-rejoin wheel zoom,
host-camera return and settlement selection passed (20-22). A host attack then
automatically transitioned to VillageBattle/empire_village_o, 29 versus 21 (23).
Completion and map return are still pending; do not count this as a full cycle.

Final r4 gate: user H assistance released deployment and the battle began at
08:40:28.811. Dedicated completion at08:41:29.167 records attacker victory with
29 active attackers and zero active/reserve defenders. Agent closed campaign
party/loot aftermath; MP automatically returned to the map without process restart
(25), with original session/398-entity catalog and working zoom/manual detach (26).
Injected host-camera button clicks did not register, but the user's click restored
host mode/framing, independently observed and saved (27). This is a bounded village
cycle, not fresh field/siege/hideout coverage. No battle adapters changed here.
Exact-owned cleanup at05:52:04Z preserved a reused PID with different identity,
retained test saves, and restored only the three inspected config changes. All
seven configuration preimages match and no owned processes remain. Independent
postflight at05:52:42Z also verifies no new config files or product processes, and
all seven installed candidate files match. cmp-b62 is intentionally retained;
full visual/interaction parity remains open.

## Documentation impact

Read: README, GOAL_MODE, focused CODE_MAP, BUILD_TEST_DEBUG and architecture/flow/
risk entries. Create this canonical implementation/evidence document; link from
README, CODE_MAP, ARCHITECTURE, RUNTIME_FLOWS, INVARIANTS_AND_RISKS.
Build procedure, lifetime, input policy and automatic lifecycle documents remain
unchanged: those mechanisms are reused, not corrected or newly accepted here.

Full Goal audit: HUD source/build integration verified, native portrait parity partial;
native visual parity, broader map visuals and adjacent lifecycle regressions remain
open. Earlier completed bounded runtime gates are not invalidated or generalized.
