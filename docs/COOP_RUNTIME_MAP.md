# Карта Coop Runtime

Оновлено: `2026-05-28`

Це єдина актуальна архітектурна карта моду. Історичні аудити, handoff-и, dated-плани та проміжні звіти прибрані з active documentation surface і не повинні використовуватись як поточне джерело правди.

## Зміст

- 1. Мета і поточний напрямок
- 2. Startup/join контракт, який зараз не можна ламати
- 3. Як стартує мод
- 4. Як стартує dedicated server
- 5. Як клієнт підключається через список MP серверів
- 6. Які vanilla MP частини ми поки зберігаємо
- 7. Які runtime частини вже наші
- 8. Як працює side selection
- 9. Як працює materialization агентів
- 10. Як працює campaign battle transfer
- 11. Як працює exact transfer
- 12. Інвентаризація коду по шарах
- 13. Що ще лишається legacy-шаром
- 14. Що вже видалено на першому етапі
- 15. Що ще треба перенести або переписати
- 16. План переходу до clean coop core без TDM

## 1. Мета і поточний напрямок

Ми більше не розглядаємо мод як TDM clone з великою кількістю patch-based обходів.

Поточний напрямок такий:

1. Залишити тільки мінімальний vanilla multiplayer shell, без якого не працюють listed-server registration, server-browser join, lobby bootstrap і прийом клієнта.
2. Виносити coop-логіку в явні coop-owned runtime шари, а не ховати її в TDM-specific fallback path.
3. Видаляти мертвий TDM clone код, мінімальні crash-isolation режими, дубльовані ids та застарілу документацію.

Поточна архітектурна правда:

- official `TeamDeathmatch` все ще лишається listed-server shell;
- official `Battle` вже override-иться в наш `CoopBattle` runtime;
- join flow все ще спирається на native custom-game lobby і native mission bootstrap;
- listed ingress зараз збирається явно всередині `MissionStateOpenNew` interception; це вже не diff-over-vanilla list, але ще не окремий startup entry point без official shell.

## 2. Startup/join контракт, який зараз не можна ламати

Нижче те, що підтверджено поточним кодом і low-level decompile-аналізом native multiplayer stack.

### Контракт lookup-а game mode

- `TaleWorlds.MountAndBlade.Module.GetMultiplayerGameMode(string)` є центральною точкою lookup-а runtime режиму.
- `TaleWorlds.MountAndBlade.Module.AddMultiplayerGameMode(...)` безпечно додає custom ids, але не є чистим механізмом заміни вже зареєстрованого official id.
- Override official `Battle` зараз безпечний через Harmony postfix у `DedicatedServer/Patches/GameModeOverridePatches.cs`.
- Заміна official `TeamDeathmatch` на окремий custom id більше не є частиною clean path.

### Мінімальний mission bootstrap контракт, який ще потрібен

- `MissionLobbyComponent` повинен лишатись у listed-shell mission stack.
- `MultiplayerTimerComponent` повинен лишатись, бо `MissionLobbyComponent` читає його під час native state handling.
- `MultiplayerTeamSelectComponent` більше не входить у `CoopBattle` server або client stack і більше не лишається у wrapped listed shell; listed ingress більше не тримає окремий team-select compatibility shell.
- `MissionScoreboardComponent` повинен лишатись на dedicated listed/custom server, бо native `MissionCustomGameServerComponent.AfterStart()` підписується на його події без null guard.
- listed-shell mission stack більше не несе `SpawnComponent`, `SpawningBehaviorBase` або official TDM spawn-point behavior; direct listed spawn і spawn-frame resolution тепер ідуть напряму через `CoopMissionSpawnLogic` та helper `ListedShellSpawnFrameBehavior` без TDM gold gate, troop-cost deduction або official TDM spawn-point class.
- `MissionLobbyComponent` більше не повинен hard-read `SpawnComponent` ні для `GetSpawnPeriodDurationForPeer(...)`, ні для match-end `OnMissionTick(...)`; обидва контракти тепер перехоплює `MissionLobbySpawnContractPatch`.
- native `MissionMultiplayerTeamDeathmatch` / `MissionMultiplayerTeamDeathmatchClient` теж більше не повинні лишатися у wrapped listed shell; їх місце тепер займають `ListedShellCompatibilityMode` і `ListedShellCompatibilityModeClient`, які зберігають тільки мінімальний mission-mode/team/bootstrap contract без TDM score/gold authority.
- `MultiplayerMissionAgentVisualSpawnComponent` більше не входить у `CoopBattle` client stack; native `CreateAgentVisuals` sender на `MissionNetworkComponent.OnPeerSelectedTeam(...)` тепер глушиться для custom coop runtime.
- native `MissionLobbyEquipmentNetworkComponent` більше не входить у wrapped listed shell; listed ingress більше не має native loadout/perk bootstrap component.
- native `MultiplayerMissionAgentVisualSpawnComponent` більше не входить у wrapped listed shell; listed ingress більше не має native visual-preview/bootstrap component.
- passive `ConsoleMatchStartEndHandler` теж більше не входить у wrapped listed shell, бо без native visual component він лише тягне старий platform-state контракт.
- listed-shell spawn ingress тепер робить прямий authoritative player-agent spawn через `CoopMissionSpawnLogic`; native `CreateAgentVisuals` і local `SpawnAgentVisualsForPeer(...)` більше не є bootstrap corridor, а official `SpawnComponent`/`SpawningBehaviorBase` вже повністю прибрані з listed mission stack.

### Контракт custom-server join flow, який ще потрібен

- Native client custom-game join path проходить через `LobbyGameStateCustomGameClient.StartMultiplayer(...)`.
- Цей native path далі викликає `GameNetwork.StartMultiplayerOnClient(...)` і стартує native custom lobby mission.
- Поточний connectivity shell ще залежить від таких patch-ів:
  - `Patches/LobbyJoinResultSelfJoinArmPatch.cs`
  - `Patches/LobbyCustomGameLocalJoinPatch.cs`
  - `Patches/LocalJoinAddressPatch.cs`
  - `Patches/LobbyRequestJoinDiagnosticsPatch.cs`

Якщо код лежить всередині цього контракту, а ми ще не маємо coop-native replacement, його не можна викидати навмання.

## 3. Як стартує мод

Client/host startup починається в `SubModule.cs`.

Поточна послідовність така:

1. `SubModule.OnSubModuleLoad()` ініціалізує shared runtime state через `CoopRuntime.Initialize()`.
2. Далі ставляться Harmony patch-і для lobby, mission-open wrapping, battle-map UI suppression, spawn handoff, finished-loading gates і exact-transfer hooks.
3. Якщо multiplayer game-mode layer доступний у збірці, мод реєструє `MissionMultiplayerCoopBattleMode` під id `CoopBattle`.
4. Далі озброюється override для official `Battle`, щоб native `GetMultiplayerGameMode("Battle")` повертав наш coop runtime.

Поточний висновок:

- client-side game-mode registration існує тільки для `CoopBattle`;
- client path для `CoopTdm` і `TdmClone` більше не існує.

## 4. Як стартує dedicated server

Dedicated startup починається в `DedicatedServer/SubModule.cs` і `DedicatedHelper/DedicatedHelperLauncher.cs`.

Поточний flow:

1. `DedicatedHelperLauncher` пише startup config з official `TeamDeathmatch`.
2. Dedicated module завантажується і ставить Harmony patch-і.
3. `RegisterCoopBattleGameMode()` реєструє тільки `MissionMultiplayerCoopBattleMode`.
4. `GameModeOverridePatches.SetBattleOverride(...)` озброює official `Battle` на запуск `CoopBattle`.
5. Dedicated observer не запускається одразу, а чекає стабілізацію multiplayer bootstrap contract.

Поточний ready heuristic на dedicated:

- scene name не повинен бути порожнім;
- mission mode не повинен лишатись `StartUp`;
- `MissionLobbyComponent` повинен бути присутній;
- `MultiplayerTimerComponent` повинен бути присутній.

Це зараз найсильніший практичний індикатор того, який мінімальний native shell ще потрібен для безпечного listed startup.

## 5. Як клієнт підключається через список MP серверів

Поточний listed join path такий:

1. Dedicated server потрапляє в native custom server list, бо рекламує official listed-compatible shell: `TeamDeathmatch`.
2. Клієнт робить join через native multiplayer UI.
3. Native join result проходить через наші join-context patch-і для self-join і local/VPN address correction.
4. Native lobby startup викликає `GameNetwork.StartMultiplayerOnClient(...)`.
5. Стартує native custom lobby mission.
6. Коли відкривається місія `MultiplayerTeamDeathmatch`, `Patches/MissionStateOpenNewPatches.cs` більше не diff-ить vanilla behavior list, а збирає explicit listed-ingress stack у native order.
7. У цей explicit stack входять тільки мінімальні native shell behaviors, які ще лишилися потрібними для listed join/bootstrap, плюс наші compatibility replacements:
   - `ListedShellCompatibilityMode` / `ListedShellCompatibilityModeClient`
   - `MissionLobbyComponent` + `MultiplayerTimerComponent` з patched lobby-contract через `MissionLobbySpawnContractPatch`
   - boundary/poll/admin/notifications/options/scoreboard/preload contract
8. Після цього explicit listed ingress додає наші coop runtime behaviors:
   - `Mission/CoopMissionNetworkBridge.cs`
   - `Mission/CoopMissionBehaviors.cs` (`CoopMissionClientLogic` або `CoopMissionSpawnLogic`)
   - `UI/CoopMissionSelectionView.cs`
9. Коли runtime потім просить official `Battle`, override повертає `MissionMultiplayerCoopBattleMode`, і фактична battle mission стартує вже в нашому coop-owned runtime, а не в vanilla battle runtime.

Саме тому поточний безпечний стан такий: "vanilla listed shell + coop battle runtime", а не "повністю custom режим без TDM shell".

## 6. Які vanilla MP частини ми поки зберігаємо

Це не просто legacy-сміття. Це native shell, на якому ще стоїть безпечний startup/join flow.

| Native частина | Чому лишається зараз | Коли можна прибирати |
| --- | --- | --- |
| `MissionLobbyComponent` | native lobby state, peer sync, late-client handling | коли coop runtime візьме на себе еквівалент mission-state і peer bootstrap |
| `MultiplayerTimerComponent` | потрібен lobby lifecycle | коли буде свій lobby shell |
| `MissionScoreboardComponent` | потрібен dedicated `MissionCustomGameServerComponent` | коли dedicated shell більше не буде інстанціювати цей native component |
| `ListedShellCompatibilityMode` | generic listed-shell server mode: team/banner setup, representative bootstrap, без native TDM score/gold/match-end authority | коли listed ingress більше не залежатиме від official `TeamDeathmatch` mission shell |
| `ListedShellCompatibilityModeClient` | generic listed-shell client mode: `MissionMode.Battle` + representative sync, без native gold/sound authority | коли listed ingress більше не залежатиме від official `TeamDeathmatch` mission shell |
| `MissionLobbySpawnContractPatch` | забирає з `MissionLobbyComponent` respawn-period і match-end читання через `SpawnComponent`; listed shell більше не потребує official `SpawnComponent` / `SpawningBehaviorBase` mission behavior | коли listed ingress більше не потребуватиме навіть native `MissionLobbyComponent` shell |
| `CoopMissionSpawnLogic` listed ingress spawn path | custom direct listed ingress без TDM gold gate/cost deduction, без native visual/equipment bootstrap і без mission-stack `SpawnComponent`; саме тут тепер живе active listed player spawn authority | коли listed ingress більше не потребуватиме навіть native `MissionLobbyComponent` shell |
| official `TeamDeathmatch` listed shell | безпечна server-list registration і join bootstrap | тільки після доведеного альтернативного listed/custom startup path без TDM shell |
| official `Battle` id | native entry point для battle mission start | лишається, але вже override-иться в `CoopBattle` |

## 7. Які runtime частини вже наші

Активний coop-owned runtime зараз зосереджений тут.

### Core mission runtime

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `GameMode/MissionMultiplayerCoopBattle.cs`
- `GameMode/MissionMultiplayerCoopBattleClient.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopMissionNetworkBridge.cs`
- `UI/CoopMissionSelectionView.cs`

### Coop runtime state і bridge files

- `Infrastructure/CoopBattleAuthorityState.cs`
- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`
- `Infrastructure/CoopBattlePhaseBridgeFile.cs`
- `Infrastructure/CoopBattleSelectionBridgeFile.cs`
- `Infrastructure/CoopBattleSelectionIntentState.cs`
- `Infrastructure/CoopBattleSelectionRequestState.cs`
- `Infrastructure/CoopBattleSpawnBridgeFile.cs`
- `Infrastructure/CoopBattleSpawnIntentState.cs`
- `Infrastructure/CoopBattleSpawnRequestState.cs`
- `Infrastructure/CoopBattleSpawnRuntimeState.cs`
- `Infrastructure/CoopBattlePeer*`

### Точки інтеграції з native shell

- `Patches/MissionStateOpenNewPatches.cs`
- `Patches/BattleMapHudSuppressionPatch.cs`
- `Patches/MissionScreenCameraPreviewPatch.cs`

## 8. Як працює side selection

Поточний side selection є hybrid-моделлю: native compatibility shell + наша authoritative coop логіка.

Поточний flow:

1. Native mission все ще може тримати `MultiplayerTeamSelectComponent` тільки в listed shell, але `CoopBattle` server/client runtime його вже не несе і він більше не є джерелом authoritative side state.
2. Клієнтський selection path тепер складається з власного overlay через `UI/CoopMissionSelectionView.cs` і гарячих клавіш у `Mission/CoopMissionBehaviors.cs`.
3. UI-дії йдуть у `CoopBattleNetworkRequestTransport` в `Mission/CoopMissionNetworkBridge.cs`.
4. Далі запит летить як `CoopBattleSelectionClientRequestMessage`.
5. Якщо мережеве відправлення ще недоступне, ті самі дії падають у file bridge:
   - `CoopBattleSelectionBridgeFile`
   - `CoopBattleSpawnBridgeFile`
6. Сервер приймає запит у `CoopMissionNetworkBridge.HandleClientSelectionRequest(...)`.
7. Server-side authority застосовується в `CoopMissionSpawnLogic.TryHandleNetworkSelectionRequest(...)`.
8. Native `MissionPeer.Team` тепер моститься від authority state через `CoopMissionSpawnLogic.TryBridgeAuthoritativePeerTeams(...)`, а не через native team-select lifecycle як джерело істини.
9. Selection state і spawn request state зберігаються через:
   - `CoopBattleAuthorityState`
   - `CoopBattleSelectionRequestState`
   - `CoopBattleSpawnRequestState`
10. Reconnect path може повернути peer назад у side selection через `CoopMissionSpawnLogic.TryReturnPeerToReconnectSideSelection(...)`.

Важливе обмеження поточного стану:

- ми вже маємо власну side/entry authority;
- authoritative troop selection більше не читає native `MissionPeer.SelectedTroopIndex` як джерело істини;
- native `MissionPeer.Team` уже моститься server-side з coop authority state;
- native `SelectedTroopIndex` лишається тільки compatibility bridge для vanilla bootstrap/network expectations;
- native `SelectedTroopIndex` bridge більше не активується в custom `CoopBattle` runtime; він лишився тільки listed-shell compatibility path для native bootstrap/network expectations;
- listed-shell `TeamInitialPerkInfoReady` більше не залежить виключно від `MissionLobbyEquipmentNetworkComponent`, але після виносу `SpawnComponent`/equipment shell цей gate уже не має live listed-shell читача й більше не моститься server-side;
- listed-shell `HasSpawnedAgentVisuals` теж більше не піднімається native visual preview-етапом і більше не армується server-side bridge-ем; visual flag тепер лишається очищеним і більше не входить у listed-shell spawn authority;
- listed-shell spawn ingress більше не використовує native `TeamDeathmatchSpawningBehavior`; TDM gold floor, troop-cost gate і spawn-time gold deduction більше не входять у live listed-shell spawn contract;
- listed-shell mission-mode layer більше не використовує native `MissionMultiplayerTeamDeathmatch` / `MissionMultiplayerTeamDeathmatchClient`; TDM score, gold sync, kill gold і score-based match-end більше не входять у live listed-shell authority;
- native `MissionPeer.OnTeamChanged` / `TeamChange` path більше не входить у listed-shell authority; native team-select повністю прибраний із wrapped listed shell і більше не може скинути `SelectedTroopIndex`/culture назад у vanilla path;
- `Infrastructure/CoopBattleEntryPolicy.cs` більше не тримає dead allow-flags для legacy vanilla team/class interaction; живими лишилися тільки authoritative path predicates, які реально читає battle-map handoff/client shell;
- native class compatibility bridge ще резолвиться від authoritative coop selection, а не від `MissionPeer.SelectedTroopIndex` як джерела істини;
- старий server-side `MultiplayerHeroClassOverridePatch` для vanilla TDM spawn/class path уже видалений; лишився тільки явний `SelectedTroopIndex` bridge;
- server-side coop runtime більше не форсить native pending visuals і не переводить їх у vanilla `SpawningBehaviorBase` / `Mission.SpawnAgent(..., spawnFromAgentVisuals: true)` lifecycle;
- `HasSpawnedAgentVisuals` і `ShouldSpawnVisualsForServer(...)` більше не входять у server-side phase/spawn authority; listed shell тепер тримає тільки selected-troop/perk compatibility bootstrap без visual preview lifecycle;
- native visual compatibility state ще чиститься під час possession/reset, але вже не впливає на рішення про spawn або battle phase;
- старий client-side vanilla-selection reflection шар (hint/menu, class-loadout filtering, team-select/scoreboard culture sync, vanilla spawn/team-change mirror paths) уже видалений;
- vanilla team/class gauntlet entry views у wrapped listed `TeamDeathmatch` shell тепер зрізаються структурно в `MissionStateOpenNewPatches`, а не глушаться окремим UI suppression patch;
- local camera preview у `UI/CoopMissionSelectionView.cs` тепер пише тільки в `MissionScreen.SetAgentToFollow(...)`; preview path більше не використовує `LastFollowedAgent` / `MissionPeer.FollowedAgent` network echo;
- `Patches/MissionScreenCameraPreviewPatch.cs` більше не мутує `MissionLobbyComponent.MissionType` або `MissionPeer.HasSpawnedAgentVisuals`; shim тепер робить postfix override для `MissionScreen.GetSpectatingData(...)`;
- custom `CoopBattle` client runtime більше не несе `MultiplayerTeamSelectComponent`; client-side team/class intent повністю йде через overlay + authoritative network/file bridge path;
- passive `ConsoleMatchStartEndHandler` більше не входить у custom `CoopBattle` runtime contract; native platform-state shell приглушений разом із visual bootstrap sender-ом;
- listed-shell більше не тримає окремий team-selection shell; side/team authority повністю заходить через coop-owned bridge path.

## 9. Як працює materialization агентів

Поточний materialization є server-authoritative. Native preview/bootstrap systems ще лишаються поруч, але вже не керують server-side spawn або battle-phase lifecycle.

Основний flow:

1. `CoopMissionSpawnLogic` валідовує side та entry, які запитав peer.
2. Далі обчислюється authoritative allowed entry set і preferred spawn selection.
3. Сервер materialize-ить battlefield agents із authoritative snapshot/entry contract.
4. Коли peer входить у coop life, live runtime зараз намагається передати йому вже materialized agent через `TryReplaceMaterializedBotWithPlayer(...)`.
5. Під час цього шляху exact equipment/body/identity зберігаються й повторно накладаються вже на replace-bot runtime.
6. Сервер будує authoritative materialization snapshot через `BuildAuthoritativeMaterializedAgentEntrySnapshot(...)`.
7. `CoopMissionNetworkBridge.TrySyncMaterializedAgentEntryPayloads()` штовхає цей snapshot клієнтам.

Поточні native залежності в цьому flow:

- native mission peer spawn timers
- native team membership
- listed-shell native selected-troop/class bridge для bootstrap compatibility
- listed-shell native initial-perk readiness тепер уже моститься server-side від authoritative selection і більше не має `MissionLobbyEquipmentNetworkComponent` як єдине джерело істини
- listed-shell native selected-troop/class bridge

Важливе звуження поточного контракту:

- server-side coop runtime більше не має власного коду, який запитує native pending visuals;
- dedicated coop runtime більше не викликає `ShouldSpawnVisualsForServer(...)` і не намагається підлаштовувати свій flow під native server-visual contract;
- server-side finalize hook, який переводив native preview visuals у vanilla `SpawningBehaviorBase` spawn loop через `SetEarlyAgentVisualsDespawning(...)`, уже видалений;
- native `SelectedTroopIndex` compatibility bridge більше не активується в custom `CoopBattle` runtime; у listed-shell path він тепер армується від authoritative pending spawn, а не від native visual/equipment preview window;
- listed-shell `TeamInitialPerkInfoReady` більше не використовується як live bootstrap gate: після виносу `SpawnComponent`/equipment shell server-side bridge для нього теж прибраний;
- listed-shell direct spawn більше не армує `HasSpawnedAgentVisuals` / `EquipmentUpdatingExpired` як bootstrap state; native visual flags тепер лишаються очищеними, а `MissionNetworkComponent.OnPeerSelectedTeam(...)` додатково глушиться як visual bootstrap corridor;
- native `TeamDeathmatchSpawningBehavior` уже прибраний із wrapped listed shell; active listed spawn authority тепер живе в `CoopMissionSpawnLogic`, а official `SpawnComponent`/`SpawningBehaviorBase` вже взагалі прибрані з listed ingress stack;
- `MissionLobbyComponent.GetSpawnPeriodDurationForPeer(...)` більше не читає respawn period через `Mission.GetMissionBehavior<SpawnComponent>()`, а match-end `OnMissionTick(...)` більше не дотягується до `_gameMode.SpawnComponent.SpawningBehavior`; обидва контракти тепер замкнуті в `MissionLobbySpawnContractPatch`;
- native `MissionMultiplayerTeamDeathmatch` / `MissionMultiplayerTeamDeathmatchClient` уже прибрані із wrapped listed shell; live ingress лишився тільки через compatibility modes, які тримають TDM-derived type contract без native economy/score loop;
- phase progression до `Deployment`/`PreBattleHold` тепер спирається на реальний control/materialization readiness, а не на `HasSpawnedAgentVisuals`.

Тобто логіка materialization уже значною мірою наша, але вона ще не відв'язана від native multiplayer bootstrap assumptions.

## 10. Як працює campaign battle transfer

Campaign battle export як і раніше стартує на host-стороні в `Campaign/BattleDetector.cs`.

Поточний flow:

1. `BattleDetector` детектить перехід campaign encounter у battle.
2. Будується `Network/Messages/BattleStartMessage.cs`.
3. Далі резолвиться scene/runtime shell через campaign scene та MP scene helpers.
4. Пишуться bridge/runtime state files для dedicated battle start.
5. Далі той самий шар веде readiness, participation, aftermath і writeback.

Ключові bridge-файли цього шару:

- `Campaign/CampaignFieldBattleExportBridge.cs`
- `Campaign/CampaignFieldBattleImportBridge.cs`
- `Infrastructure/BattleSnapshot*`
- `Infrastructure/CampaignToMultiplayerSceneResolver.cs`
- `Infrastructure/CampaignMapPatchMissionInit.cs`

Це шар, який перетворює live campaign encounter у multiplayer-compatible battle contract.

## 11. Як працює exact transfer

Exact transfer - це спроба зберігати campaign identities, body data і equipment максимально близько до SP truth, а не агресивно ремапити все в coarse MP templates.

Основна exact-transfer інфраструктура зараз живе тут:

- `Infrastructure/ExactTransferContractBuilder.cs`
- `Infrastructure/ExactTransferSpawnContract.cs`
- `Infrastructure/ExactTransferRuntimeState.cs`
- `Infrastructure/ExactTransferStageMachine.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Infrastructure/ExactCampaignObjectCatalogBootstrap.cs`
- `Infrastructure/ExactCampaignRuntimeObjectRegistry.cs`
- `Infrastructure/ExactCampaignRuntimeItemRegistry.cs`
- `Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs`
- `Infrastructure/ExactBattleRuntimeBundleBridgeFile.cs`
- `Infrastructure/ExactBattleEntryCompatibilityBridgeFile.cs`
- `Infrastructure/ExactBattleAgentSpawnTraceBridgeFile.cs`

Зв'язок exact transfer з поточним cleanup такий:

- exact transfer і відв'язування від TDM shell - це не одна й та сама задача;
- exact transfer треба зберігати, але він повинен сидіти поверх clean coop mission core, а не поверх історичного TDM-bootstrap шару.

## 12. Інвентаризація коду по шарах

### Coop-specific core

| Зона | Основні файли |
| --- | --- |
| Module startup | `SubModule.cs`, `DedicatedServer/SubModule.cs` |
| Coop battle runtime | `GameMode/MissionMultiplayerCoopBattle*.cs` |
| Mission authority і spawn | `Mission/CoopMissionBehaviors.cs`, `Mission/CoopMissionNetworkBridge.cs` |
| Coop selection UI | `UI/CoopMissionSelectionView.cs`, `UI/CoopSelectionShellViewModels.cs`, `UI/CoopSelectionUiHelpers.cs` |
| Coop runtime state | `Infrastructure/CoopBattle*`, `Infrastructure/HostSelfJoinRedirectState.cs`, `Infrastructure/CustomGameJoinContextState.cs` |

### Exact transfer і campaign battle bridge

| Зона | Основні файли |
| --- | --- |
| Campaign encounter export | `Campaign/BattleDetector.cs`, `Campaign/CampaignFieldBattleExportBridge.cs` |
| Campaign result import | `Campaign/CampaignFieldBattleImportBridge.cs` |
| Snapshot transport | `Infrastructure/BattleSnapshot*`, `Network/Messages/BattleStartMessage.cs` |
| Exact runtime object/bootstrap | `Infrastructure/Exact*`, `MissionModels/CoopCampaignDerived*.cs` |

### Legacy vanilla MP patching layer

| Зона | Основні файли |
| --- | --- |
| Listed TDM shell wrapping | `Patches/MissionStateOpenNewPatches.cs` |
| Native UI suppression | `Patches/BattleMapHudSuppressionPatch.cs`, `Patches/MissionScreenCameraPreviewPatch.cs` |
| Connectivity і local/self join | `Patches/LobbyCustomGameLocalJoinPatch.cs`, `Patches/LobbyJoinResultSelfJoinArmPatch.cs`, `Patches/LocalJoinAddressPatch.cs`, `Patches/LobbyRequestJoinDiagnosticsPatch.cs` |
| Native class/culture compatibility | `Patches/ClientChangeCultureCanonicalizationPatch.cs`, `Patches/ServerChangeCultureCanonicalizationPatch.cs`, `Patches/MultiplayerCharacterClassFallbackPatch.cs`, `Patches/StartupSafeMpHeroClassBootstrapPatch.cs` |
| Залишковий crash isolation | `Patches/IntermissionVmCrashGuardPatch.cs` |
| Listed-shell startup helper | `DedicatedHelper/DedicatedHelperLauncher.cs` |

### Уже removable або вже видалене

| Зона | Статус |
| --- | --- |
| `GameMode/MissionMultiplayerCoopTdm*.cs` | видалено |
| `GameMode/MissionMultiplayerTdmClone*.cs` | видалено |
| `GameMode/MissionMinimal*DiagnosticMode.cs` | видалено |
| `CoopTdm` і `TdmClone` multiplayer strings | видалено |
| `EnableTdmCloneExperiment` branch | видалено |
| `TeamDeathmatch` override path у `GameModeOverridePatches` | видалено |
| `Patches/VanillaEntryUiSuppressionPatch.cs` | видалено; vanilla entry gauntlets тепер зрізаються в `MissionStateOpenNewPatches.cs` |

## 13. Що ще лишається legacy-шаром

Ці шматки коду ще потрібні для сумісності з native lifecycle і поки що повинні вважатися тимчасовими:

- `Patches/MissionStateOpenNewPatches.cs`
- `Patches/BattleMapHudSuppressionPatch.cs`
- `Patches/MissionScreenCameraPreviewPatch.cs`
- `Patches/IntermissionVmCrashGuardPatch.cs`
- `Patches/LobbyCustomGameLocalJoinPatch.cs`
- `Patches/LobbyJoinResultSelfJoinArmPatch.cs`
- `Patches/LocalJoinAddressPatch.cs`
- `Patches/LobbyRequestJoinDiagnosticsPatch.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`

Ці файли ще не є "сміттям". Вони legacy, бо компенсують native lifecycle assumptions, які ми ще не перебрали у власний архітектурний шар.

## 14. Що вже видалено на першому етапі

На цьому етапі вже зроблено таке:

- видалені `CoopTdm` game-mode файли;
- видалені `TdmClone` game-mode файли;
- видалені minimal mission diagnostic modes, які існували тільки для старого crash isolation;
- прибрані `CoopTdm` і `TdmClone` ids з `Infrastructure/CoopGameModeIds.cs`;
- прибраний `EnableTdmCloneExperiment` з `Infrastructure/ExperimentalFeatures.cs`;
- прибраний `EnableBattleMapClientEquipmentNetworkComponent`; battle-map/client stack decisions тепер фіксуються структурно в `MissionMultiplayerCoopBattleMode`, а не через runtime toggle;
- прибрана мертва wrapped-Battle crash-isolation гілка, яка намагалась вирізати `MissionLobbyEquipmentNetworkComponent` із client stack;
- прибраний dead wrapped-`Battle` client path із `MissionStateOpenNewPatches.cs`; wrapper mission-open тепер обслуговує тільки listed `TeamDeathmatch` shell, а `Battle` йде через `CoopBattle` game-mode override;
- прибрані `MissionState.OpenNew` postfix/handler-contract diagnostics із `MissionStateOpenNewPatches.cs`; у wrapper-і лишився тільки функціональний listed-shell hook без triage-era log-only шару;
- прибраний native spawn gold floor/deduction compatibility path навколо `ReplaceBotWithPlayer(...)`, бо coop runtime і client mode більше не використовують vanilla gold economy як spawn contract;
- прибраний server-only `MultiplayerHeroClassOverridePatch`, який підміняв `MultiplayerClassDivisions.GetMPHeroClassForPeer(...)` для старого vanilla spawn/class path;
- pending native spawn visuals більше не форсяться в coop server path, якщо native `ShouldSpawnVisualsForServer(...)` не вимагає їх для поточного peer/runtime;
- `HasSpawnedAgentVisuals` більше не використовується в server-side phase/deployment або spawn authority; це вже тільки compatibility state для native/client shell;
- `SelectedTroopIndex` compatibility bridge більше не активується в custom `CoopBattle` runtime; у listed shell він тепер живе тільки в authoritative pending-spawn bootstrap window і чистить cached state під час expiry;
- listed-shell native `TeamInitialPerkInfoReady` більше не залежить виключно від `MissionLobbyEquipmentNetworkComponent`, але й більше не моститься server-side, бо live listed-shell spawn reader для цього gate вже прибраний;
- native `MissionLobbyEquipmentNetworkComponent` повністю прибраний із wrapped listed `TeamDeathmatch` shell; listed ingress більше не тримає even passive equipment compatibility component;
- native `MultiplayerTeamSelectComponent` повністю прибраний із wrapped listed `TeamDeathmatch` shell; listed ingress більше не несе окремий team-select compatibility layer;
- native `MultiplayerMissionAgentVisualSpawnComponent` повністю прибраний із wrapped listed `TeamDeathmatch` shell; listed ingress більше не тримає even passive visual compatibility component;
- listed-shell bootstrap більше не піднімає `HasSpawnedAgentVisuals` / `EquipmentUpdatingExpired`; visual-flag lifecycle більше не використовується як compatibility authority навіть у listed ingress;
- native `MissionMultiplayerTeamDeathmatch` і `MissionMultiplayerTeamDeathmatchClient` прибрані із wrapped listed `TeamDeathmatch` shell і замінені на `ListedShellCompatibilityMode` / `ListedShellCompatibilityModeClient`; listed mission-mode layer більше не має TDM score loop, kill gold, respawn gold або score-based match-end, а зберігає тільки team/banner setup, client `MissionMode.Battle` і representative graph;
- native `TeamDeathmatchSpawningBehavior`, native `TeamDeathmatchSpawnFrameBehavior` і сам mission-stack `SpawnComponent` прибрані із wrapped listed `TeamDeathmatch` shell; listed spawn ingress більше не має TDM gold gate, selected-troop fallback-to-zero, troop-cost deduction або official TDM spawn-point class;
- active listed spawn authority повністю сидить у `CoopMissionSpawnLogic`, а `ListedShellSpawnFrameBehavior` лишився лише локальним helper-резолвером spawn frame, а не mission behavior shell;
- `MissionStateOpenNewPatches.cs` більше не модифікує vanilla `TeamDeathmatch` behavior list по місцю; listed ingress тепер збирається явно в native order з мінімального shell-контракту і наших compatibility replacements;
- listed-shell native spawn compatibility state тепер армується server-side тільки для `SelectedTroopIndex`; `TeamInitialPerkInfoReady` і visual flags уже не входять у це bootstrap-вікно;
- `MultiplayerTeamSelectComponent` прибраний з `CoopBattle` server і client stack, а також повністю прибраний з wrapped listed shell;
- `MissionLobbyEquipmentNetworkComponent` прибраний з `CoopBattle` client stack; custom runtime більше не несе native equipment/class bootstrap, лишився тільки listed-shell legacy;
- `MultiplayerMissionAgentVisualSpawnComponent` прибраний з `CoopBattle` client stack; custom runtime більше не несе native agent-visual bootstrap;
- native `MissionNetworkComponent.OnPeerSelectedTeam(...)` більше не шле `CreateAgentVisuals` ні для custom `CoopBattle`, ні для listed ingress shell;
- passive native `ConsoleMatchStartEndHandler` приглушений для custom `CoopBattle` runtime і більше не тримає visual-spawn/platform-state contract;
- passive native `ConsoleMatchStartEndHandler` також прибраний із wrapped listed `TeamDeathmatch` shell, бо listed ingress більше не несе native visual component;
- із wrapped listed `TeamDeathmatch` shell прибраний чисто діагностичний ballast: `MissionBehaviorDiagnostic` і повний wrapper stack-dump у `MissionStateOpenNewPatches.cs`;
- прибрані мертві `AllowLegacyVanillaTeamSelectionInteraction` / `AllowLegacyVanillaClassSelectionInteraction` з `Infrastructure/CoopBattleEntryPolicy.cs`;
- видалений мертвий direct-spawn experiment (`EnableDirectCoopPlayerSpawnExperiment`, `TrySpawnPeersIntoCoopControl(...)`, `SpawnCoopControlledAgent(...)`, `TryEnsurePendingSpawnVisuals(...)`), який уже не входив у live runtime tick path;
- видалений active vanilla spawn-bridge hook (`RunVanillaSpawnBridgeTick(...)` / `TryFinalizePendingNativeSpawnVisualCompatibility(...)`), який переводив native preview visuals у `SpawningBehaviorBase` і `Mission.SpawnAgent(..., spawnFromAgentVisuals: true)`;
- прибраний старий feature-flag `EnableVanillaMissionWrapping`; listed `TeamDeathmatch` shell wrapping тепер є явною частиною поточного join/startup контракту, а не runtime toggle;
- прибрана логіка `TeamDeathmatch` override з `DedicatedServer/Patches/GameModeOverridePatches.cs`;
- прибрані `CoopTdm` і `TdmClone` strings з `Module/CoopSpectator/ModuleData/multiplayer_strings.xml`;
- прибрані dedicated-project compile includes для видалених TDM файлів;
- wrapper vanilla mission-open більше не інжектить `MissionMinimalServerDiagnosticMode` і `MissionMinimalClientDiagnosticMode`;
- з `Mission/CoopMissionBehaviors.cs` прибраний мертвий legacy vanilla-selection/UI reflection шар, включно зі старими hint/menu path, class-loadout filtering, culture-sync і visual auto-confirm splice;
- прибраний compile-time dead native preferred-troop request experiment і пов’язаний passive observer на `MissionPeer.SelectedTroopIndex`, який вже не мав власного runtime-ефекту.

## 15. Що ще треба перенести або переписати

Головні блокери до повністю clean coop runtime зараз такі:

1. Замінити залежність від official listed-shell `TeamDeathmatch` тільки після того, як буде доведений альтернативний server-list registration і join path без нього; live mission-mode/spawn layer всередині wrapper вже більше не повинен тримати native TDM authority.
2. Прибрати решту server-armed native spawn flags у listed-shell bootstrap (`SelectedTroopIndex`) тоді, коли ingress більше не потребуватиме навіть native `MissionLobbyComponent` shell.
3. Прибрати bridge-file fallback-и тоді, коли network transport стане достатньо надійним для selection, spawn, readiness і reconnect flows.
4. Винести вже explicit listed-shell assembly з `MissionStateOpenNew` interception у ще чистіший coop startup entry point, коли native shell interception більше не буде потрібний.
5. Окремо переоцінити `LocalJoinAddressPatch` та інші join patch-і, коли public, VPN і self-host join flows будуть розділені чистіше по відповідальності.
6. Прибрати crash-isolation patch-і на кшталт `IntermissionVmCrashGuardPatch`, коли battle-map lobby/intermission lifecycle буде вже нашим, а не native.

## 16. План переходу до clean coop core без TDM

### Фаза A: стабілізувати listed-shell startup contract

- залишаємо official `TeamDeathmatch` у startup config;
- залишаємо native lobby, timer, scoreboard і team-select shell;
- лишаємо `Battle` override у `CoopBattle`;
- не чіпаємо join patch-і, поки не валідуємо public/VPN/local self-join paths окремо.

### Фаза B: зменшити native mission wrapping

- виносити логіку з `MissionStateOpenNewPatches.cs` у більш явні coop runtime entry points;
- зменшувати кількість UI suppression patch-ів, замінюючи native entry/intermission views власними coop views, а не приховуючи їх постфактум.

### Фаза C: повністю забрати side selection і materialization

- завершити перехід від native team-select stack/UI bootstrap до власної coop-owned authority і readiness-моделі;
- прибрати останній compatibility-only міст через native `SelectedTroopIndex`, коли vanilla bootstrap більше не вимагатиме його;
- прибрати останній listed-shell bootstrap flag через native `SelectedTroopIndex`, коли ingress більше не потребуватиме навіть цієї compatibility expectation.

### Фаза D: відчепити мод від vanilla listed shell

- повернутися до питання server-list registration тільки після того, як coop runtime зможе стартувати і приймати клієнта без TDM mission shell;
- тільки після цього можна реально розглядати прибирання official `TeamDeathmatch` із startup config.

### Робоче правило для наступних cleanup-ів

Якщо шматок коду існує тільки для компенсації TDM або vanilla mission lifecycle, це кандидат на видалення.

Якщо шматок коду лежить усередині перевіреного server-list join contract, його не можна прибирати, доки replacement path не доведений у runtime.
