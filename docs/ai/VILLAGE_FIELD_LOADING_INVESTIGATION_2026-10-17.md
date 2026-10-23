# Village-associated field scene: loading investigation

Status: log/source diagnosis; no runtime fix or deployment yet. The user is
still testing and explicitly forbids touching game processes/installed files.

## Evidence

Supplied host campaign `rgl_log_5412.txt`, dedicated `rgl_log_8048.txt`, host
MP `rgl_log_15076.txt` (Downloads/Telegram Desktop/logs), remote MP
`C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_31520.txt`.
Do not compare absolute client/server timestamps as synchronized clocks.

- First `battle_terrain_biome_022` reached BattleActive and BattleEnded.
- Campaign log line 5353 selects `battle_terrain_biome_040`, patch index 41,
  encounter direction present, source `scene-model-map-patch`.
- Dedicated line 28658 initializes 67 attackers / 23 defenders with
  `ScenarioKind=VillageBattle` on that field scene.
- Dedicated lines 28639/28641 acknowledge both peers' complete battle snapshots.
- Dedicated line 28990 registers all 90 agents; line 29020 tracks 67/23.
- Dedicated line 28664 and following report `InjectEquipment=False`, native
  template equipment, feature-disabled payload profile on village-classified
  agents. This differs from the first battle's full exact land profile.
- Dedicated phase remains SideSelection. Both clients receive Loading status;
  remote client acknowledges the 90-entry materialization map (line 16506).

## Source chain

`BattleDetector.ResolveScenarioKind` selects VillageBattle when the encounter
settlement is a village OR the scene is a village. Encounter settlement lookup
can use PlayerEncounter.EncounterSettlement, MapEventSettlement or current
settlement. The logs do not identify which source supplied this particular value.

`ExactCreateAgentServerPreSpawnContractResolver` requires validated land scenario
before full exact equipment injection. `ExactVillageBattleScenarioContract`
requires a village scene, no map patch index and no encounter direction. All
three conditions conflict with this captured field scene.

`AreBattlefieldArmiesReadyForStart` treats VillageBattle as land battle and
requires exact initial materialization; that validator rejects agents whose
equipment was not injected. `TryResolveBattleDataReadinessForPeer` reports Loading
while phase is before PreBattleHold. This is a concrete incompatible contract
chain, not evidence that increasing a timeout will help.

## Next work / scope

### Confirmed scene-selection defect

Campaign log line 5318 identifies the actual mission as `battania_village`;
line 5324 activates `land_raid`. The field scene at line 5353 is therefore an
incorrect mod fallback, not the actual campaign scene. The shared classifier
accepted `village_` prefix and `_village_` infix but missed `_village` suffix.
The village-specific branch in TryBuildCampaignSceneSelectionContextSafe was
skipped, and scene-model-map-patch produced a contradictory field scene.

Source candidate now recognizes the exact `_village` suffix case-insensitively.
This also routes land_raid activation and dedicated registration consistently.
It does not weaken village contract validation or change result identity.
`village-suffix-b131-20261017` compiled both roles and verified installed module
inventories unchanged. A read-only PowerShell Add-Type probe compiled the exact
production classifier declarations/methods and passed 10 cases: battania_village,
BATTANIA_VILLAGE, village_empire, battania_village_h accepted; field, official MP,
villager suffix, hideout, town and empty names rejected. This is classification
evidence, not native scene acceptance. The startup contract project also guards
shared classifier routing in campaign scene selection and land_raid selection.
`village-suffix-c128-20261017` completed with all 26 contract projects passing.
Native two-PC acceptance remains pending. No game process or installed module
was changed. The earlier settlement-events-c127 run failed the projection test's
missing dynamic-field declaration; c128 includes the corrected declaration and
passes the map project, including settlement event transport tests.

An immutable follow-up preview was produced at
`dist/previews/BannerlordCoop_TwoPC_b131_VillageFix_20261017.zip` (11,350,534 bytes).
The packaging script verified every ZIP payload against the manifest and checked
both build outputs and all 26 contract results. It contains protocol42/bridge37
map changes as well as the village suffix correction, so all roles must update
together. The old b124 r4 archive is unchanged. No installation was performed.

Audit classification and result routing together before correction: field near
a village, genuine village battle, raid/forced recruitment/supplies, relief,
sally-out, siege and hideout. Do not merely admit arbitrary field scenes into
the village contract or remove equipment readiness validation. Determine whether
the captured scene is authoritative actual mission scene versus scene-model
fallback. Preserve campaign result identity independently of visual scene kind.

Add pure classification regression cases and source integration checks. Validate
both builds and contracts in isolated outputs. Runtime acceptance needs a fresh
coordinated two-PC field -> village-associated encounter sequence; user has not
yet authorized interrupting the current test. Current logs do not prove a generic
second-battle state-reset bug.
