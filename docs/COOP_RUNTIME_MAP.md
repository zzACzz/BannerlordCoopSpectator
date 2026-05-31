# Карта Coop Runtime

Оновлено: `2026-05-31`

Це єдина актуальна архітектурна карта моду. Історичні аудити, handoff-и, dated-плани та проміжні звіти прибрані з active documentation surface і не повинні використовуватись як поточне джерело правди.

## Зміст

- 1. Як користуватись цією картою
- 2. Поточна робоча ціль
- 3. Архітектура
- 4. Інвентаризація, legacy і cleanup surface
- 5. Roadmap до clean coop core без TDM

## 1. Як користуватись цією картою

Цей файл повинен лишатись єдиним живим джерелом правди по coop runtime. Ідея така:

1. Новий чат або новий етап роботи починається з читання тільки релевантного розділу, а не всього історичного контексту.
2. `Поточна робоча ціль` описує, що саме ми зараз стабілізуємо і що вважається done.
3. `Архітектура` описує, як мод зараз реально влаштований, без dated handoff-ів і разових звітів.
4. `Інвентаризація, legacy і cleanup surface` фіксує борги, legacy tails і вже виконане очищення.
5. `Roadmap` зберігає відкладені фази і напрямки, щоб вони не губились, але не змішувались із поточною runtime-ціллю.

### 1.1 Операційні правила

- Будь-який новий чат або новий етап роботи починається з читання цього файлу, а не зі старих handoff-ів або випадкових чат-логів.
- Під час роботи цей файл треба оновлювати як живу карту: окремо архітектурну правду, окремо поточну робочу ціль, окремо active blocker-и.
- Дослідження завжди починається на найнижчому рівні, який ще може пояснити симптом. Не можна стрибати одразу в high-level припущення, якщо реальну причину ще не перевірено по логах, runtime state, decompile або native call flow.
- Якщо верхній рівень не дає точної причини, треба йти нижче:
  - `ilspycmd` для managed decompile і перевірки реального C# / IL контракту;
  - `IDA Free` для native DLL, engine path і lookup/transport/lifecycle деталей;
  - `WinDbg` для crash dump-ів, managed/native exception corridor і stack truth;
  - runtime логи Bannerlord як перший фактичний шар перед будь-яким rewrite або cleanup.
- Перед будь-яким широким cleanup-зрізом треба спершу довести точний active runtime blocker на найнижчому доступному рівні.

### 1.2 Шаблон старту нового чату

Використовувати такий стартовий текст для нового чату:

```text
Працюємо в репозиторії BannerlordCoopSpectator3.

Почни з файлу:
C:\dev\projects\BannerlordCoopSpectator3\docs\COOP_RUNTIME_MAP.md

Це єдине актуальне джерело правди по модулю. Користуйся ним так:
1. Спочатку прочитай розділ "Поточна робоча ціль".
2. Потім прочитай тільки релевантні підрозділи з "Архітектура".
3. Для боргів і cleanup-контексту дивись "Інвентаризація, legacy і cleanup surface".
4. Для відкладених фаз і великих напрямків дивись "Roadmap".

Поточний фокус:
- зробити стабільне listed підключення;
- зробити стабільну materialization усіх selectable/possessable battlefield units;
- не розблоковувати side selection і unit selection, поки materialization barrier не закритий;
- зробити безпечне possession без race condition і crash-ів.

Робоче правило:
- дослідження завжди починати на найнижчих рівнях, які ще можуть пояснити симптом;
- за потреби використовувати ilspycmd, IDA Free і WinDbg;
- не робити high-level rewrite або cleanup, поки low-level причина не доведена.

Після читання карти продовжуй роботу від поточного active runtime blocker, а не з нуля.
```

## 2. Поточна робоча ціль

Поточна ціль: зробити стабільне підключення клієнта і стабільну materialization усіх selectable/possessable battlefield units перед тим, як клієнту дозволяється side selection, unit selection і вселення.

Поточний робочий принцип:

1. Клієнт повинен стабільно пройти listed join, mission bootstrap і snapshot/data sync без повернення в lobby.
2. Після snapshot/data sync клієнт повинен лишатись у loading/selection gate, поки battlefield units не materialize-нуться достатньо для безпечного preview і possession.
3. Side selection і unit selection не повинні розблоковуватись раніше, ніж authoritative materialization barrier буде закритий.
4. Після розблокування selection клієнт повинен мати можливість безпечно вселитись у selectable unit без post-selection materialization race, crash-ів і silent fallback-ів у чужого агента.

Що зараз вважається done для цього етапу:

- сервер стабільно видно у списку серверів і клієнт стабільно приєднується;
- клієнт заходить у battle mission без раннього `UnloadMission`, crash-у або rollback у lobby;
- loading gate лишається заблокованим, поки всі required selectable/possessable representatives не materialize-нуться;
- side selection і unit selection відкриваються тільки після цього barrier;
- possession працює щонайменше для main hero, companions і ordinary troops без негайного crash-у.
- останній вдалий прогін доходить від `SideSelection`/`PreBattleHold` до `BattleActive` і `BattleEnded` без client crash-у або нового dump-а.

Що зараз не є поточним пріоритетом:

- широкі cleanup-зрізи, які не блокують join/materialization runtime;
- повний transport replacement;
- подальше architectural cleanup нижче за рівень, потрібний для стабільного join + materialization + possession loop.

### 2.1 Активний runtime blocker

Поточний active blocker уже не сидить у materialization barrier або side-selection unlock. Останній вдалий прогін показав, що listed join, authoritative materialization, side selection, unit preview, mounted possession main hero, старт бою і завершення бою можуть пройти без client crash-у.

Стан на зараз:

- loading/selection barrier уже відпрацьовує достатньо стабільно, щоб selection відкривався тільки після `live-prebattle-materialized` state;
- main hero exact materialization, safe side selection, mounted possession, weapon switching і battle start уже не є головним proven blocker-ом;
- proven native crash corridor у `TaleWorlds.Native.dll+0x5e4aa8` зараз containment-иться message-level guard-ами для non-local exact no-shield ranged AI;
- battle після цього реально доходить до `BattleEnded` без нового dump-а;
- активний залишковий blocker тепер вужчий: до `BattleActive` main hero, foot archer companion і looters зі sling усе ще можуть візуально заходити в постійний reload loop, хоча після старту бою вони вже воюють нормально.

Поточна робоча гіпотеза:

1. snapshot/data sync, materialization barrier і selection gate уже не є головною проблемою для останнього підтвердженого crash corridor;
2. reload loop до старту бою тепер виглядає як окремий pre-battle weapon-state corridor, а не як materialization failure;
3. для non-local exact no-shield ranged AI клієнт тепер навмисно suppress-ить до native handler-а:
   - `SetWeaponReloadPhase` у pre-battle hold
   - `SetWeaponAmmoData`
   - ammo-semantic `SetWeaponNetworkData`
4. cohort для цього suppress path тепер резолвиться спочатку через authoritative tracked entry mapping, а не тільки через bootstrap id; саме це дозволило latest successful run реально влучити в companion archer, sling looters і crossbow AI;
5. local hero corridor цим stop-gap-ом навмисно не глушиться, тому pre-battle reload loop main hero лишається окремою незакритою проблемою.

Практичне правило для продовження:

- якщо новий crash знову з’явиться після старту бою, спершу перевіряються suppress-маркери для `SetWeaponReloadPhase` / `SetWeaponAmmoData` / `SetWeaponNetworkData` і окремо local hero path;
- якщо battle знову доходить до `BattleEnded`, але pre-battle reload loop лишається, далі досліджується саме local pre-battle ranged state corridor, а не broader materialization/join flow;
- high-level rewrite hero materialization або broader cleanup не робити, поки не доведено точний low-level сигнал, який тримає pre-battle reload loop живим.

## 3. Архітектура

### 3.1 Архітектурний контекст і базовий напрямок

Ми більше не розглядаємо мод як TDM clone з великою кількістю patch-based обходів.

Поточний напрямок такий:

1. Залишити тільки мінімальний vanilla multiplayer shell, без якого не працюють listed-server registration, server-browser join, lobby bootstrap і прийом клієнта.
2. Виносити coop-логіку в явні coop-owned runtime шари, а не ховати її в TDM-specific fallback path.
3. Видаляти мертвий TDM clone код, мінімальні crash-isolation режими, дубльовані ids та застарілу документацію.

Поточна архітектурна правда:

- official `TeamDeathmatch` все ще лишається listed-server shell;
- official `Battle` вже override-иться в наш `CoopBattle` runtime;
- join flow все ще спирається на native custom-game lobby і native mission bootstrap;
- listed ingress більше не входить у native `MissionBasedMultiplayerGameMode.StartMultiplayerGame()` для `TeamDeathmatch`; основний startup path тепер перехоплює `Module.StartMultiplayerGame("TeamDeathmatch", scene)` і заводить наш `MissionMultiplayerListedShellMode`, без додаткового `MissionStateOpenNew` interception шару.

### 3.2 Startup/join контракт, який зараз не можна ламати

Нижче те, що підтверджено поточним кодом і low-level decompile-аналізом native multiplayer stack.

#### Контракт lookup-а game mode

- `TaleWorlds.MountAndBlade.Module.GetMultiplayerGameMode(string)` є центральною точкою lookup-а runtime режиму.
- `TaleWorlds.MountAndBlade.Module.AddMultiplayerGameMode(...)` безпечно додає custom ids, але не є чистим механізмом заміни вже зареєстрованого official id.
- Override official `Battle` зараз безпечний через Harmony postfix у `DedicatedServer/Patches/GameModeOverridePatches.cs`.
- Low-level decompile показує, що listed/custom-game startup реально йде через `TaleWorlds.MountAndBlade.Module.StartMultiplayerGame(string, string)`, а не через `GetMultiplayerGameMode(...)`; саме тому listed ingress тепер треба owner-ити на рівні `StartMultiplayerGame`, а не тільки lookup-а.
- Заміна official `TeamDeathmatch` на окремий custom id більше не є частиною clean path.

#### Мінімальний mission bootstrap контракт, який ще потрібен

- `MissionLobbyComponent` поки що повинен лишатись у listed-shell mission stack, але вже не як source of truth для server-side match flow.
- `MultiplayerTimerComponent` повинен лишатись, бо `MissionLobbyComponent` і `ListedShellLobbyRuntime` ще читають його для listed-shell state/timer handling.
- `MultiplayerTeamSelectComponent` більше не входить у `CoopBattle` server або client stack і більше не лишається у wrapped listed shell; listed ingress більше не тримає окремий team-select compatibility shell.
- `MissionScoreboardComponent` повинен лишатись тільки на dedicated listed/custom server, але вже не як active score-hit authority: listed shell тепер піднімає наш `ListedShellMissionScoreboardComponent`, який забирає `OnScoreHit(...)` у `ListedShellLobbyRuntime`, а `kill/death/assist/score` header getters уже йдуть через `ListedShellScoreboardData` від `CoopBattlePeerStatsRuntimeState`; side-score і `BotData` тепер канонічно живуть у `CoopBattleScoreboardRuntimeState`, а native scoreboard container лишається тільки як compatibility mirror для старих reader-ів і late-join/storage surface.
- `MultiplayerGameNotificationsComponent` більше не входить ні в listed ingress, ні в `CoopBattle` stack; native team-targeted notification shell більше не є частиною startup/join контракту.
- `MultiplayerPollComponent` більше не входить ні в listed ingress, ні в `CoopBattle` stack; native team-scoped kick/change-game poll shell більше не є частиною coop runtime або listed bootstrap.
- listed-shell mission stack більше не несе `SpawnComponent`, `SpawningBehaviorBase` або official TDM spawn-point behavior; direct listed spawn і spawn-frame resolution тепер ідуть напряму через `CoopMissionSpawnLogic` та helper `ListedShellSpawnFrameBehavior` без TDM gold gate, troop-cost deduction або official TDM spawn-point class.
- `MissionLobbyComponent` більше не повинен hard-read `SpawnComponent` ні для `GetSpawnPeriodDurationForPeer(...)`, ні для server-side `OnMissionTick(...)`; `WaitingFirstPlayers -> Playing`, `Playing -> Ending`, normal playing tick, server-side `MissionStateChange` broadcast, client-side `MissionStateChange` application, culture-selection bootstrap suppression, late-client replay, respawn-period contract, post-match endgame timeout і весь listed bot-death/kill routing тепер перехоплює `ListedShellLobbyRuntime` разом з explicit listed lobby shell.
- native `MissionMultiplayerTeamDeathmatch` / `MissionMultiplayerTeamDeathmatchClient` теж більше не повинні лишатися у wrapped listed shell; їх місце тепер займають `ListedShellCompatibilityMode` і `ListedShellCompatibilityModeClient`, які зберігають тільки мінімальний mission-mode/team/bootstrap contract без TDM score/gold authority.
- `MultiplayerMissionAgentVisualSpawnComponent` більше не входить у `CoopBattle` client stack; native `CreateAgentVisuals` sender на `MissionNetworkComponent.OnPeerSelectedTeam(...)` тепер глушиться для custom coop runtime.
- native `MissionLobbyEquipmentNetworkComponent` більше не входить у wrapped listed shell; listed ingress більше не має native loadout/perk bootstrap component.
- native `MultiplayerMissionAgentVisualSpawnComponent` більше не входить у wrapped listed shell; listed ingress більше не має native visual-preview/bootstrap component.
- passive `ConsoleMatchStartEndHandler` теж більше не входить у wrapped listed shell, бо без native visual component він лише тягне старий platform-state контракт.
- listed-shell spawn ingress тепер робить прямий authoritative player-agent spawn через `CoopMissionSpawnLogic`; native `CreateAgentVisuals` і local `SpawnAgentVisualsForPeer(...)` більше не є bootstrap corridor, а official `SpawnComponent`/`SpawningBehaviorBase` вже повністю прибрані з listed mission stack.

#### Контракт custom-server join flow, який ще потрібен

- Native client custom-game join path проходить через `LobbyGameStateCustomGameClient.StartMultiplayer(...)`.
- Цей native path далі викликає `GameNetwork.StartMultiplayerOnClient(...)` і стартує native custom lobby mission.
- Поточний connectivity shell ще залежить від таких patch-ів:
  - `Patches/ListedShellClientWrapperOwnershipPatch.cs`
  - `Infrastructure/CoopSessionTransportPrimitives.cs`
  - `Patches/LobbyRequestJoinDiagnosticsPatch.cs`

Якщо код лежить всередині цього контракту, а ми ще не маємо coop-native replacement, його не можна викидати навмання.

### 3.3 Як стартує мод

Client/host startup починається в `SubModule.cs`.

Поточна послідовність така:

1. `SubModule.OnSubModuleLoad()` ініціалізує shared runtime state через `CoopRuntime.Initialize()`.
2. Далі ставляться Harmony patch-і для lobby, battle-map UI suppression, spawn handoff, finished-loading gates і exact-transfer hooks.
3. Якщо multiplayer game-mode layer доступний у збірці, мод реєструє `MissionMultiplayerCoopBattleMode` під id `CoopBattle`.
4. Далі озброюється override для official `Battle`, щоб native `GetMultiplayerGameMode("Battle")` повертав наш coop runtime.
5. Паралельно озброюється listed ingress override для official `TeamDeathmatch`, щоб native `Module.StartMultiplayerGame("TeamDeathmatch", scene)` заходив у `MissionMultiplayerListedShellMode`, а не у vanilla `MissionBasedMultiplayerGameMode.StartMultiplayerGame()`.

Поточний висновок:

- client-side game-mode registration існує тільки для `CoopBattle`;
- client path для `CoopTdm` і `TdmClone` більше не існує.

### 3.4 Як стартує dedicated server

Dedicated startup починається в `DedicatedServer/SubModule.cs` і `DedicatedHelper/DedicatedHelperLauncher.cs`.

Поточний flow:

1. `DedicatedHelperLauncher` пише startup config з official `TeamDeathmatch`.
2. Dedicated module завантажується і ставить Harmony patch-і.
3. `RegisterCoopBattleGameMode()` реєструє `MissionMultiplayerCoopBattleMode` і озброює explicit listed ingress override через `MissionMultiplayerListedShellMode`.
4. `GameModeOverridePatches.SetBattleOverride(...)` озброює official `Battle` на запуск `CoopBattle`.
5. `GameModeOverridePatches.SetTeamDeathmatchOverride(...)` озброює official `TeamDeathmatch` на coop-owned listed startup path без vanilla `MissionBasedMultiplayerGameMode.StartMultiplayerGame()`.
6. Dedicated observer не запускається одразу, а чекає стабілізацію multiplayer bootstrap contract.

Поточний ready heuristic на dedicated:

- scene name не повинен бути порожнім;
- mission mode не повинен лишатись `StartUp`;
- `MissionLobbyComponent` повинен бути присутній;
- `MultiplayerTimerComponent` повинен бути присутній.

Це зараз найсильніший практичний індикатор того, який мінімальний native shell ще потрібен для безпечного listed startup.

### 3.5 Як клієнт підключається через список MP серверів

Поточний listed join path такий:

1. Dedicated server потрапляє в native custom server list, бо рекламує official listed-compatible shell: `TeamDeathmatch`.
2. Клієнт робить join через native multiplayer UI.
3. Native join result проходить через наші join-context patch-і для self-join і local/VPN address correction.
4. Native lobby startup викликає `GameNetwork.StartMultiplayerOnClient(...)`.
5. Native `BaseNetworkComponent` викликає `Module.StartMultiplayerGame("TeamDeathmatch", scene)`.
6. `GameModeOverridePatches` перехоплює цей виклик і переводить його в `MissionMultiplayerListedShellMode.StartMultiplayerGame(scene)`.
7. `MissionMultiplayerListedShellMode` відкриває місію `MultiplayerTeamDeathmatch` уже з нашим explicit handler-ом `ListedShellMissionBehaviorFactory.CreateMissionBehaviors`.
8. У цей explicit stack входять тільки мінімальні native shell behaviors, які ще лишилися потрібними для listed join/bootstrap, плюс наші compatibility replacements:
   - `ListedShellCompatibilityMode` / `ListedShellCompatibilityModeClient`
   - explicit coop-owned lobby shell (`ListedShellMissionLobbyClientComponent` або `ListedShellMissionLobbyServerComponent`) + `MultiplayerTimerComponent` з lobby-contract через `ListedShellLobbyRuntime`
   - boundary/admin/options/preload contract + server-side scoreboard compatibility only
9. Після цього explicit listed ingress додає наші coop runtime behaviors:
   - `Mission/CoopMissionNetworkBridge.cs`
   - `Mission/CoopMissionBehaviors.cs` (`CoopMissionClientLogic` або `CoopMissionSpawnLogic`)
   - `UI/CoopMissionSelectionView.cs`
11. Коли runtime потім просить official `Battle`, override повертає `MissionMultiplayerCoopBattleMode`, і фактична battle mission стартує вже в нашому coop-owned runtime, а не в vanilla battle runtime.

Саме тому поточний безпечний стан такий: "vanilla listed shell + coop battle runtime", а не "повністю custom режим без TDM shell".

### 3.6 Які vanilla MP частини ми поки зберігаємо

Це не просто legacy-сміття. Це native shell, на якому ще стоїть безпечний startup/join flow.

| Native частина | Чому лишається зараз | Коли можна прибирати |
| --- | --- | --- |
| `MissionLobbyComponent` | native mission-state message surface; тепер інстанціюється через наші concrete shell-класи `ListedShellMissionLobbyClientComponent` / `ListedShellMissionLobbyServerComponent`, які вже напряму наслідують `MissionLobbyComponent`, а не native `MissionCustomGame*Component`; server-side lobby state transitions, client bootstrap/state application, listed request-culture / request-change-character / class-restriction / `CreateBanner` surface, late-client K/D/bots replay, `ChangeCulture` apply і весь listed death bookkeeping уже перехоплені coop-owned runtime path-ом | коли coop runtime візьме на себе еквівалент mission-state message surface, death/respawn bookkeeping і peer bootstrap |
| `MultiplayerTimerComponent` | потрібен lobby lifecycle | коли буде свій lobby shell |
| `MissionScoreboardComponent` | потрібен тільки dedicated listed lobby shell як compatibility container для `BotData`, round-score readers і частини scoreboard-side surface; active `OnScoreHit(...)`, host-side `kill/death/assist/score` headers, listed MVP selection, side-score і listed `BotData` bookkeeping уже йдуть через наші `CoopBattlePeerStatsRuntimeState` + `CoopBattleScoreboardRuntimeState`, а native `MissionPeer` stats і native scoreboard side storage більше не є source of truth | коли dedicated shell більше не буде залежати від цього native scoreboard container |
| `ListedShellCompatibilityMode` | generic listed-shell server mode: team/banner setup, representative bootstrap, без native TDM score/gold/match-end authority; `AllowCustomPlayerBanners()` примусово вимкнений, тож listed shell більше не приймає peer-driven banner mutation як live contract | коли listed ingress більше не залежатиме від official `TeamDeathmatch` mission shell |
| `ListedShellCompatibilityModeClient` | generic listed-shell client mode: `MissionMode.Battle` + representative sync, без native gold/sound authority; `MissionStateChange` і `KillDeathCountChange` registration/apply вже переїхали в `ListedShellMissionLobbyClientComponent`, тож compatibility mode лишився тільки як client mission-mode/representative shell | коли listed ingress більше не залежатиме від official `TeamDeathmatch` mission shell |
| `MissionMultiplayerListedShellMode` | coop-owned runtime entry point, який тепер займає official `Module.StartMultiplayerGame("TeamDeathmatch", scene)` path і відкриває listed місію з explicit handler-ом замість vanilla `MissionBasedMultiplayerGameMode.StartMultiplayerGame()` | коли official listed startup ingress взагалі перестане бути потрібним |
| `ListedShellMissionBehaviorFactory` | shared explicit listed mission assembly для `MissionMultiplayerListedShellMode`; саме він тепер є джерелом правди для listed mission stack, а не wrapper усередині patch-класу | коли official listed startup ingress взагалі перестане бути потрібним |
| `ListedShellLobbyRuntime` | coop-owned listed lobby runtime helper: він тримає mission-state cache/resolver, `WaitingFirstPlayers -> Playing`, `Playing -> Ending`, server/client `MissionStateChange`, late-client mission-state/K-D/bots replay, post-match endgame timeout/unload path і весь listed death/K-D/`BotData` path у `OnAgentRemoved(...)`; listed flow більше не читає і не оновлює `MissionLobbyComponent.CurrentMultiplayerState`, explicit listed shell уже не входить і в native `MissionLobbyComponent.OnBehaviorInitialize()`, а player K/D/assist/score тепер канонічно живуть у `CoopBattlePeerStatsRuntimeState` з reconnect migration | коли listed ingress більше не потребуватиме навіть native `MissionLobbyComponent` shell |
| `CoopMissionSpawnLogic` listed ingress spawn path | custom direct listed ingress без TDM gold gate/cost deduction, без native visual/equipment bootstrap і без mission-stack `SpawnComponent`; саме тут тепер живе active listed player spawn authority | коли listed ingress більше не потребуватиме навіть native `MissionLobbyComponent` shell |
| official `TeamDeathmatch` listed shell | безпечна server-list registration і join bootstrap | тільки після доведеного альтернативного listed/custom startup path без TDM shell |
| official `Battle` id | native entry point для battle mission start | лишається, але вже override-иться в `CoopBattle` |

### 3.7 Які runtime частини вже наші

Активний coop-owned runtime зараз зосереджений тут.

#### Core mission runtime

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `GameMode/MissionMultiplayerCoopBattle.cs`
- `GameMode/MissionMultiplayerCoopBattleClient.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopMissionNetworkBridge.cs`
- `UI/CoopMissionSelectionView.cs`

#### Coop runtime state і bridge files

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

#### Точки інтеграції з native shell

- `Patches/BattleMapHudSuppressionPatch.cs`
- `Patches/MissionScreenCameraPreviewPatch.cs`

### 3.8 Як працює side selection

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
- coop hero-class resolver, class-restriction sync і listed direct-spawn path більше не читають native `MissionPeer.Team` / `MissionPeer.Culture` як source of truth; вони резолвлять runtime team/culture від authoritative side + entry state, а native peer state лишається compatibility output;
- exact formation-banner seeding теж більше не шукає assigned peer через native `MissionPeer.Team`; banner source тепер резолвиться від coop-owned side authority + player-side fallback, а не від native mission membership cache;
- native `SelectedTroopIndex` більше не входить у live peer-state або late-join contract; і native replay, і локальний listed-shell class-index cache для нього вже прибрані;
- native `SelectedTroopIndex` path більше не активується в custom `CoopBattle` runtime; у listed-shell path він більше не використовується як native late-join sync token, бо цю ділянку вже забрав `CoopMissionNetworkBridge`;
- native peer-state replay більше не покладається на vanilla loop усередині `MissionNetworkComponent.SendExistingObjectsToPeer(...)`; battle-map ingress збирає late-join bootstrap явно через наш patch-layer, а native `ChangeCulture` і `SetPeerTeam` уже прибрані з цього replay повністю;
- live native `SetPeerTeam` / `ChangeCulture` rebroadcast уже прибраний із coop tick-path; native peer-team/culture compatibility більше не є частиною late-join replay, а runtime authority для side/team/culture повністю резолвиться від coop-owned state;
- listed-shell `TeamInitialPerkInfoReady` більше не залежить виключно від `MissionLobbyEquipmentNetworkComponent`, але після виносу `SpawnComponent`/equipment shell цей gate уже не має live listed-shell читача й більше не моститься server-side;
- listed-shell `HasSpawnedAgentVisuals` теж більше не піднімається native visual preview-етапом і більше не армується server-side bridge-ем; visual-flag path у нашому коді вже прибраний повністю й більше не входить у listed-shell spawn authority;
- listed-shell spawn ingress більше не використовує native `TeamDeathmatchSpawningBehavior`; TDM gold floor, troop-cost gate і spawn-time gold deduction більше не входять у live listed-shell spawn contract;
- listed-shell mission-mode layer більше не використовує native `MissionMultiplayerTeamDeathmatch` / `MissionMultiplayerTeamDeathmatchClient`; TDM score, gold sync, kill gold і score-based match-end більше не входять у live listed-shell authority;
- native `MissionPeer.OnTeamChanged` / `TeamChange` path більше не входить у listed-shell authority; native team-select повністю прибраний із wrapped listed shell і більше не може скинути `SelectedTroopIndex`/culture назад у vanilla path;
- `Infrastructure/CoopBattleEntryPolicy.cs` більше не тримає dead allow-flags для legacy vanilla team/class interaction; живими лишилися тільки authoritative path predicates, які реально читає battle-map handoff/client shell;
- старий server-side `MultiplayerHeroClassOverridePatch` для vanilla TDM spawn/class path уже видалений; native troop-index write/broadcast path теж уже прибраний, а разом із ним прибраний і локальний listed-shell class-index cache;
- server-side coop runtime більше не форсить native pending visuals і не переводить їх у vanilla `SpawningBehaviorBase` / `Mission.SpawnAgent(..., spawnFromAgentVisuals: true)` lifecycle;
- `HasSpawnedAgentVisuals` і `ShouldSpawnVisualsForServer(...)` більше не входять у server-side phase/spawn authority; listed shell тепер тримає тільки selected-troop/perk compatibility bootstrap без visual preview lifecycle;
- native visual compatibility state в нашому коді вже прибрана повністю; possession/reset path більше не тримає ні visual shell, ні окремий `SelectedTroopIndex` cache;
- старий client-side vanilla-selection reflection шар (hint/menu, class-loadout filtering, team-select/scoreboard culture sync, vanilla spawn/team-change mirror paths) уже видалений;
- vanilla team/class gauntlet entry views у listed ingress тепер зрізаються структурно explicit mission assembly, а не глушаться окремим UI suppression patch;
- local camera preview у `UI/CoopMissionSelectionView.cs` тепер пише тільки в `MissionScreen.SetAgentToFollow(...)`; preview path більше не використовує `LastFollowedAgent` / `MissionPeer.FollowedAgent` network echo;
- `Patches/MissionScreenCameraPreviewPatch.cs` більше не мутує `MissionLobbyComponent.MissionType` або `MissionPeer.HasSpawnedAgentVisuals`; shim тепер робить postfix override для `MissionScreen.GetSpectatingData(...)`;
- `Patches/MissionScreenCameraPreviewPatch.cs` тепер також більше не читає `MissionPeer.HasSpawnedAgentVisuals` навіть як passive preview gate; camera preview shim залежить тільки від нашого active preview target і result override;
- custom `CoopBattle` client runtime більше не несе `MultiplayerTeamSelectComponent`; client-side team/class intent повністю йде через overlay + authoritative network/file bridge path;
- passive `ConsoleMatchStartEndHandler` більше не входить у custom `CoopBattle` runtime contract; native platform-state shell приглушений разом із visual bootstrap sender-ом;
- listed-shell більше не тримає окремий team-selection shell; side/team authority повністю заходить через coop-owned bridge path.

### 3.9 Як працює materialization агентів

Поточний materialization є server-authoritative. Native preview/bootstrap systems ще лишаються поруч, але вже не керують server-side spawn або battle-phase lifecycle.

Основний flow:

1. `CoopMissionSpawnLogic` валідовує side та entry, які запитав peer.
2. Далі обчислюється authoritative allowed entry set і preferred spawn selection.
3. Сервер materialize-ить battlefield agents із authoritative snapshot/entry contract.
4. Коли peer входить у coop life, live runtime зараз намагається передати йому вже materialized agent через `TryReplaceMaterializedBotWithPlayer(...)`.
5. Під час цього шляху exact equipment/body/identity зберігаються й повторно накладаються вже на replace-bot runtime.
6. Сервер будує authoritative materialization snapshot через `BuildAuthoritativeMaterializedAgentEntrySnapshot(...)`.
7. `CoopMissionNetworkBridge.TrySyncMaterializedAgentEntryPayloads()` штовхає цей snapshot клієнтам.

Поточна pre-battle weapon/runtime логіка поверх цього flow:

- dedicated observer тримає `SideSelection` і `PreBattleHold` з `PauseAITick=True`, але клієнт усе одно може отримувати ranged weapon chatter ще до `BattleActive`:
  - `CreateMissile`
  - `SetWeaponReloadPhase`
  - `SetWeaponAmmoData`
  - `SetWeaponNetworkData`
- для non-local exact no-shield ranged AI цей cohort тепер визначається не тільки bootstrap id-ом, а спочатку authoritative tracked entry mapping-ом з fallback-ом на bootstrap id;
- для цього cohort-а client battle-map handoff suppress-ить before-native handlers:
  - `SetWeaponReloadPhase` під час pre-battle hold
  - `SetWeaponAmmoData`
  - ammo-semantic `SetWeaponNetworkData`
- мета цього шару - не "полагодити reload visuals", а не пустити клієнт назад у proven native ammo mutation corridor, який раніше валив гру в `TaleWorlds.Native.dll+0x5e4aa8`;
- local peer / currently controlled main hero до цього suppress cohort навмисно не входить;
- наслідок current-state: до старту бою можливі visible reload loops для main hero і частини ranged exact AI, але після `BattleActive` battle у latest successful run доходить до `BattleEnded` без crash-а.

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
- native `SelectedTroopIndex` compatibility path більше не активується ні в custom `CoopBattle`, ні у listed-shell ingress; direct listed spawn тепер бере hero class напряму з authoritative coop selection без локального class-index cache;
- listed-shell `TeamInitialPerkInfoReady` більше не використовується як live bootstrap gate: після виносу `SpawnComponent`/equipment shell server-side bridge для нього теж прибраний;
- listed-shell direct spawn більше не армує `HasSpawnedAgentVisuals` / `EquipmentUpdatingExpired` як bootstrap state; visual flags більше взагалі не пишуться з нашого коду, а `MissionNetworkComponent.OnPeerSelectedTeam(...)` додатково глушиться як visual bootstrap corridor;
- native `TeamDeathmatchSpawningBehavior` уже прибраний із wrapped listed shell; active listed spawn authority тепер живе в `CoopMissionSpawnLogic`, а official `SpawnComponent`/`SpawningBehaviorBase` вже взагалі прибрані з listed ingress stack;
- `MissionLobbyComponent.GetSpawnPeriodDurationForPeer(...)` більше не потрібен нашому listed flow взагалі: authoritative respawn period тепер резолвиться напряму через `ListedShellLobbyRuntime.ResolveAuthoritativeRespawnPeriodForPeer(...)`, а server-side `OnMissionTick(...)` більше не керує listed-shell state flow: і `WaitingFirstPlayers -> Playing`, і `Playing -> Ending`, і spawn-session stop/invulnerability path тепер замкнуті в `ListedShellLobbyRuntime`;
- server-side listed-shell `MissionStateChange` broadcast теж більше не йде через native `SetStatePlayingAsServer()` / `SetStateEndingAsServer()`; patch-layer сам піднімає state, timer і broadcast для `Playing`/`Ending`, залишаючи в native lobby тільки message container surface;
- client-side `MissionLobbyComponent.HandleServerEventMissionStateChange(...)` і `OnMyClientSynchronized()` теж більше не є source of truth для listed ingress bootstrap; `MissionStateChange` registration/apply тепер уже живе в `ListedShellMissionLobbyClientComponent`, а state apply, client timer start, warmup removal і suppression native culture-selection request сидять у coop-owned path поверх `ListedShellLobbyRuntime`;
- native `MissionLobbyComponent.AfterStart()` теж більше не тримає live listed-shell bootstrap subscription на `MissionNetworkComponent.OnMyClientSynchronized`; patch-layer відписує цей dead handler одразу після старту місії, бо client bootstrap уже давно наш, і окремий Harmony patch на private `OnMyClientSynchronized()` для listed shell уже прибраний;
- native `MissionLobbyComponent.AddRemoveMessageHandlers(...)` для listed shell взагалі більше не викликається через base path: наші `ListedShellMissionLobbyClientComponent` / `ListedShellMissionLobbyServerComponent` не реєструють native request/culture/class/banner handlers, тому цей legacy surface вже не народжується як live network contract;
- listed-shell `CreateBanner` apply/request теж більше не можуть оживити peer-driven banner mutation через native lobby container: native handler-и вже не реєструються в listed shell, а `ListedShellCompatibilityMode.AllowCustomPlayerBanners()` повертає `false`;
- native `MissionLobbyComponent.AddRemoveMessageHandlers(...)` для listed shell також більше не тримає `MissionStateChange` як live client apply handler: цей registration тепер вирізається з lobby container, а власний coop-owned handler реєструється в `ListedShellMissionLobbyClientComponent`;
- native `MissionLobbyComponent.AddRemoveMessageHandlers(...)` для listed shell також більше не тримає `KillDeathCountChange` як live client apply handler: apply player K/D/score payload-ів на клієнті тепер іде через `ListedShellMissionLobbyClientComponent`, а не через native lobby container;
- peer-driven banner authority теж звужена: listed shell більше не приймає `CreateBanner` request/apply, `ListedShellCompatibilityMode.AllowCustomPlayerBanners()` повертає `false`, а `ExactCampaignArmyBootstrap` більше не сіє formation banner-и від `MissionPeer.Peer.BannerCode`; exact bootstrap тепер бере side banner з canonical battle snapshot і тільки потім падає назад у `team.Banner`;
- concrete lobby shell теж уже наш: `MissionBehaviorHelpers.TryCreateMissionLobbyComponent()` інстанціює `ListedShellMissionLobbyClientComponent` / `ListedShellMissionLobbyServerComponent`, і вони вже напряму наслідують `MissionLobbyComponent`, а не native `MissionCustomGameClientComponent` / `MissionCustomGameServerComponent`; `OnBehaviorInitialize`, `AfterStart`, `AddRemoveMessageHandlers`, `OnMissionTick`, `OnAgentRemoved`, `HandleLateNewClientAfterLoadingFinished(...)`, client `QuitMission()`, client inactivity-critical-state tick, server `SetStateEndingAsServer()`, server `OnUdpNetworkHandlerTick()` і server `EndGameAsServer()` уже проходять через наш explicit shell;
- `OnMissionTick`, `OnAgentRemoved`, `HandleLateNewClientAfterLoadingFinished(...)` і `OnUdpNetworkHandlerTick()` для listed lobby shell теж уже не перехоплюються Harmony prefix-ами на базовому `MissionLobbyComponent`: ці lifecycle вузли переведені в override-и наших `ListedShellMissionLobbyClientComponent` / `ListedShellMissionLobbyServerComponent`, які тепер викликають coop-owned logic напряму без fallback-а назад у native base path;
- server-side `OnMissionTick` для listed lobby shell більше не падає назад у native `MissionLobbyComponent` навіть під час normal `Playing` tick: waiting/start, active playing loop, match-end transition і ending timeout тепер цілком замкнуті в `ListedShellLobbyRuntime` + explicit listed server shell;
- client-side `OnMissionTick` для listed lobby shell теж більше не падає назад у native `MissionLobbyComponent`: inactivity-critical-state path тепер локально веде `ListedShellMissionLobbyClientComponent` через той самий `ElapsedTimeSinceLastUdpPacketArrived()` контракт з decompile, але вже через reflected `MBAPI.IMBNetwork` field, а base tick більше не входить у listed client shell взагалі;
- `HandleLateNewClientAfterLoadingFinished(...)` для listed lobby shell теж уже не веде в private native `SendPeerInformationsToPeer(...)`: late-client `MissionStateChange` + `KillDeathCountChange` / `BotsControlledChange` replay тепер стартує з override-ів наших `ListedShellMissionLobbyClientComponent` / `ListedShellMissionLobbyServerComponent`, а native private replay path більше не патчиться Harmony-ом;
- listed-shell mission-state readers у нашому коді теж більше не читають `MissionLobbyComponent.CurrentMultiplayerState` напряму; `CoopMissionBehaviors`, `ListedShellCompatibilityModeClient` і `BattleShellSuppressionPatch` тепер ідуть через resolver усередині `ListedShellLobbyRuntime`;
- сам `ListedShellLobbyRuntime` для listed flow теж більше не читає і більше не оновлює `MissionLobbyComponent.CurrentMultiplayerState`: state cache ініціалізується на `OnBehaviorInitialize`, client `QuitMission()` теж іде через цей resolver, а native lobby property випала з listed runtime contract повністю;
- native `MissionLobbyComponent.OnBehaviorInitialize()` теж більше не входить у listed shell: explicit client/server shell-класи самі додають себе в `GameNetwork` як network handlers і локально підіймають свій listed init, тому `CurrentMultiplayerState = WaitingFirstPlayers`, `_usingFixedBanners` і native inactivity-timer setup більше не є частиною listed runtime contract;
- listed-shell player K/D/assist/score теж уже мають свій coop-owned source of truth: `ListedShellLobbyRuntime` тепер веде ці значення через `CoopBattlePeerStatsRuntimeState`, late-join replay і live `KillDeathCountChange` вже йдуть від цього runtime state, а reflected `MissionPeer` setters лишилися тільки як compatibility mirror для native scoreboard/peer surface;
- native `MissionLobbyComponent.AfterStart()` теж більше не входить у listed shell взагалі: explicit client/server lobby shell-класи беруть із цього low-level контракту тільки `DeploymentPlan.MakeDefaultDeploymentPlans()`, тому reflected unsubscribe від private `OnMyClientSynchronized` уже прибраний разом із historical `AfterListedShellLobbyStart(...)` helper-ом;
- `MissionLobbyComponent.OnPostMatchEnded` теж більше не входить у listed shell contract: low-level аудит local Bannerlord DLL не показав живих subscriber-ів на цей event, тому client/server ending path у `ListedShellLobbyRuntime` більше не викликає private `SetStateEndingAsClient()` і не піднімає reflected `OnPostMatchEnded` delegate;
- наш runtime також більше не викликає native `MissionLobbyComponent.GetSpawnPeriodDurationForPeer(...)` напряму; authoritative respawn-period тепер резолвиться через `ListedShellLobbyRuntime.ResolveAuthoritativeRespawnPeriodForPeer(...)`, а сам останній Harmony hook на native respawn-period helper уже прибраний;
- native `MissionScoreboardComponent.OnScoreHit(...)` теж більше не є listed live authority: dedicated listed shell тепер інстанціює `ListedShellMissionScoreboardComponent`, який переводить damage-score path у `ListedShellLobbyRuntime` і `CoopBattlePeerStatsRuntimeState`, а native `missionPeer.Score += num` / penalty path уже не мутує runtime state напряму;
- host-side `kill/death/assist/score` scoreboard headers теж більше не мають player-stat fallback у native `MissionPeer`: `ListedShellMissionScoreboardComponent` тепер використовує `ListedShellScoreboardData`, який бере player values тільки з `CoopBattlePeerStatsRuntimeState`, а bot values лишає на native `BotData`;
- native listed scoreboard round-end MVP path теж більше не читає `MissionPeer.Score`: `ListedShellMissionScoreboardComponent` після `AfterStart()` знімає native `OnPreRoundEnding` subscriber з `MultiplayerRoundComponent` і підставляє власний runtime-backed MVP selection через private `SetPeerAsMVP(...)`;
- reflected `MissionPeer` stats mirror у listed scoreboard runtime уже прибраний повністю: `KillCount`, `AssistCount`, `DeathCount` і `Score` більше не пишуться назад у native peer model, бо score-hit, live K/D transport, header getters і listed MVP selection уже йдуть від `CoopBattlePeerStatsRuntimeState`;
- listed-shell player stats runtime тепер також більше не seed-иться з native `MissionPeer.KillCount/AssistCount/DeathCount/Score`: якщо runtime state ще відсутній, `ListedShellLobbyRuntime` ініціалізує його від нульових `kill/assist/score` і authoritative lifecycle `DeathCount`, а не від ванільного peer cache;
- listed-shell client scoreboard refresh теж більше не повинен заходити в native `MissionScoreboardComponent.PlayerPropertiesChanged(...) -> CalculateTotalNumbers()`: наші direct refresh call sites тепер йдуть через `ListedShellMissionScoreboardComponent`, який напряму тригерить `OnPlayerPropertiesChanged` event без native totals walk по `MissionPeer` stats;
- listed-shell client scoreboard message registration теж частково вийшла з native `MissionScoreboardComponent`: `ListedShellMissionScoreboardComponent` тепер сам реєструє `UpdateRoundScores`, `SetRoundMVP` і `BotData`, а `SetRoundMVP` apply більше не заводить native `HandleServerSetRoundMVP(...) -> PlayerPropertiesChanged(...)`, а напряму викликає `OnMVPSelected` і наш listed-shell player-property refresh notifier;
- listed-shell client apply для `UpdateRoundScores` і `BotData` теж більше не делегується в native `MissionScoreboardComponent` handlers: `ListedShellMissionScoreboardComponent` тепер спершу пише ці значення в `CoopBattleScoreboardRuntimeState`, а native `SideScore` / `BotScores` оновлює лише як compatibility mirror перед `OnRoundPropertiesChanged` / `OnBotPropertiesChanged`;
- native listed scoreboard `OnRoundEnding() -> UpdateRoundScores()` теж більше не є server-side score loop authority: `ListedShellMissionScoreboardComponent` після `AfterStart()` знімає native `OnRoundEnding` subscriber з `MultiplayerRoundComponent`, сам інкрементить runtime-backed side score в `CoopBattleScoreboardRuntimeState`, дописує `RoundWinnerList` і сам broadcast-ить `UpdateRoundScores`;
- listed-shell scoreboard runtime тепер більше не seed-иться з native `MissionScoreboardSide.SideScore` / `BotScores`: `ListedShellLobbyRuntime.InitializeListedShellLobbyState(...)` одразу ініціалізує `CoopBattleScoreboardRuntimeState` від deterministic bot-count defaults (`NumberOfBotsTeam1/2`), а `ListedShellMissionScoreboardComponent.AfterStart()` лише синхронізує native side storage як compatibility mirror;
- late join для listed scoreboard state теж більше не покладається на native side container як прихований cache: `ListedShellLobbyRuntime.SendListedShellScoreboardStateToPeer(...)` тепер окремо дограє `UpdateRoundScores` і `BotData` з `CoopBattleScoreboardRuntimeState`, тож round score і bot scoreboard більше не залежать від native seed path;
- listed-shell round history теж більше не живе в native `_roundWinnerList` як runtime storage: `ListedShellMissionScoreboardComponent` тепер дописує winner history в `CoopBattleScoreboardRuntimeState`, а private native список лише синхронізує як compatibility mirror для можливих старих reader-ів;
- `CoopBattle` і explicit listed ingress теж більше не покладаються на global `MissionLobbyComponent.CreateBehavior()` factory; stack builder-и тепер явно створюють `ListedShellMissionLobbyClientComponent` або `ListedShellMissionLobbyServerComponent`, тож lobby shell більше не залежить ні від прихованої `LobbyMissionType` registration path, ні від native custom-game lobby base-класів;
- `MissionMatchHistoryComponent` більше не входить у explicit listed ingress; client listed stack більше не має живого consumer-а `BannerlordNetwork.LobbyMissionType`, який потрібен тільки для native local match-history/presence surface, а не для battle startup/join contract;
- `BaseNetworkComponent.HandleNewClientConnect(...)` для listed shell теж більше не покладається ні на native `BannerlordNetwork.LobbyMissionType == Custom/Community`, ні на native intermission/custom-lobby replay; explicit listed shell тепер сам шле мінімальний bootstrap (`MultiplayerOptionsInitial`, `MultiplayerOptionsImmediate`, за потреби `MultiplayerOptionsDefault`, `InitializeCustomGameMessage`) напряму, з official `TeamDeathmatch` id, поточною сценою і власним listed mission-session token, без `BaseNetworkComponentData.CurrentBattleIndex` fallback-а;
- receive-side `BaseNetworkComponent.InitializeCustomGameAux(...)`, `HandleServerEventLoadMission(...)` і `HandleServerEventUnloadMission(...)` для listed shell більше не owner-ять startup/mission-open/teardown authority через окремі patch-и; `ListedShellBaseNetworkTransportOwnershipPatch` тепер лише перехоплює listed graph, а весь send/receive ownership для `InitializeCustomGame`, `LoadMission` і `UnloadMission` сидить у `ListedShellNetworkBootstrapRuntime`, включно з mission-session token adopt, duplicate mission-open suppression і listed unload без native coroutine;
- listed-shell `FinishedLoading` server validation теж більше не читає native `BaseNetworkComponentData.CurrentBattleIndex` навіть як fallback: `ListedShellBaseNetworkTransportOwnershipPatch` тепер маршрутизує explicit listed handshake через `ListedShellMissionSessionState` і порівнює client `BattleIndex` тільки з нашим mission-session token;
- official `LobbyGameStateCustomGameClient.StartMultiplayer(...)`, `LobbyGameStateCommunityClient.StartMultiplayer(...)`, `LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer()` і Diamond `LobbyClient.OnJoinCustomGameResultMessage(...)` для listed `TeamDeathmatch` тепер owner-яться через один `ListedShellClientWrapperOwnershipPatch`: explicit listed shell сам arm-ить wrapper-entry від join-result, а далі сам викликає `GameNetwork.StartMultiplayerOnClient(...)` або `GameNetwork.PreStartMultiplayerOnServer()` + `Module.CurrentModule.StartMultiplayerGame(...)` + `GameNetwork.StartMultiplayerOnServer(...)` без native `StartMultiplayerLobbyMission(Custom|Community)` arm;
- listed client wrapper-entry і receive ownership більше не висять на time-based `CustomGameJoinContextState`: `ListedShellClientWrapperOwnershipPatch` arm-ить явний `ListedShellClientSessionOwnershipState` на join-result, споживає його як wrapper-start gate перед `GameNetwork.StartMultiplayerOnClient(...)`, а transport runtime підвищує той самий state до receive-bootstrap ownership для `InitializeCustomGame` / `LoadMission` / `UnloadMission`;
- listed client wrapper-entry більше не читає private native wrapper fields `_address`, `_port`, `_sessionKey`, `_peerIndex`: `ListedShellClientSessionOwnershipState` тепер захоплює весь transport start context ще на Diamond join-result і віддає його `ListedShellWrapperInteropRuntime`, тому official custom/community wrapper-и лишилися тільки interception points, а не source of truth для listed client start;
- hosted listed server wrapper теж більше не читає private `_gameClient`: `LobbyGameStatePlayerBasedCustomServer.SetStartingParameters(...)` тепер лише arm-ить `ListedShellHostedServerStartContextState` через `ListedShellWrapperInteropRuntime`, а `HandleServerStartMultiplayer()` уже бере `CustomGameType`, `CustomGameScene` і `IsInGame` з нашого explicit state замість native private field cache;
- native `MissionLobbyComponent.SendPeerInformationsToPeer(...)` теж більше не є late-client replay джерелом для listed ingress; `HandleLateNewClientAfterLoadingFinished(...)` у наших listed lobby shell-класах тепер сам шле `MissionStateChange`, `KillDeathCountChange` і `BotsControlledChange` через `ListedShellLobbyRuntime`, а не через native private replay path;
- listed-shell player death теж більше не чекає polling-only `lost-controlled-agent` path: `ListedShellLobbyRuntime` тепер перехоплює `MissionLobbyComponent.OnAgentRemoved(...)`, одразу переводить dead peer у coop-owned spectator/`DeadAwaitingRespawn` transition з authoritative respawn-timer normalization і сам веде listed player death / player kill / suicide `KillDeathCountChange` path без native `OnPlayerDies(...)` / `OnPlayerKills(...)`;
- listed-shell respawn period у `ListedShellLobbyRuntime` тепер теж більше не читає `MissionPeer.Team.Side`; він резолвиться від `CoopBattleAuthorityState.GetAssignedSide(...)`, тому lobby timer contract уже не залежить від native peer-team cache;
- native `MissionMultiplayerTeamDeathmatch` / `MissionMultiplayerTeamDeathmatchClient` уже прибрані із wrapped listed shell; live ingress лишився тільки через compatibility modes, які тримають TDM-derived type contract без native economy/score loop;
- phase progression до `Deployment`/`PreBattleHold` тепер спирається на реальний control/materialization readiness, а не на `HasSpawnedAgentVisuals`.

Тобто логіка materialization уже значною мірою наша, але вона ще не відв'язана від native multiplayer bootstrap assumptions.

### 3.10 Як працює campaign battle transfer

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

### 3.11 Як працює exact transfer

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

## 4. Інвентаризація, legacy і cleanup surface

### 4.1 Інвентаризація коду по шарах

#### Coop-specific core

| Зона | Основні файли |
| --- | --- |
| Module startup | `SubModule.cs`, `DedicatedServer/SubModule.cs` |
| Coop battle runtime | `GameMode/MissionMultiplayerCoopBattle*.cs` |
| Mission authority і spawn | `Mission/CoopMissionBehaviors.cs`, `Mission/CoopMissionNetworkBridge.cs` |
| Coop selection UI | `UI/CoopMissionSelectionView.cs`, `UI/CoopSelectionShellViewModels.cs`, `UI/CoopSelectionUiHelpers.cs` |
| Coop runtime state | `Infrastructure/CoopBattle*`, `Infrastructure/HostSelfJoinRedirectState.cs`, `Infrastructure/CustomGameJoinContextState.cs` |

#### Exact transfer і campaign battle bridge

| Зона | Основні файли |
| --- | --- |
| Campaign encounter export | `Campaign/BattleDetector.cs`, `Campaign/CampaignFieldBattleExportBridge.cs` |
| Campaign result import | `Campaign/CampaignFieldBattleImportBridge.cs` |
| Snapshot transport | `Infrastructure/BattleSnapshot*`, `Network/Messages/BattleStartMessage.cs` |
| Exact runtime object/bootstrap | `Infrastructure/Exact*`, `MissionModels/CoopCampaignDerived*.cs` |

#### Legacy vanilla MP patching layer

| Зона | Основні файли |
| --- | --- |
| Listed TDM startup ingress | `GameMode/MissionMultiplayerListedShellMode.cs`, `GameMode/ListedShellMissionBehaviorFactory.cs`, `DedicatedServer/Patches/GameModeOverridePatches.cs` |
| Listed mission assembly | `GameMode/MissionMultiplayerListedShellMode.cs`, `GameMode/ListedShellMissionBehaviorFactory.cs` |
| Native UI suppression | `Patches/BattleMapHudSuppressionPatch.cs`, `Patches/MissionScreenCameraPreviewPatch.cs` |
| Connectivity і local/self join | `Patches/ListedShellClientWrapperOwnershipPatch.cs`, `Infrastructure/ListedShellWrapperInteropRuntime.cs`, `Infrastructure/CoopSessionTransportPrimitives.cs`, `Patches/LobbyRequestJoinDiagnosticsPatch.cs` |
| Native class compatibility | `Patches/MultiplayerCharacterClassFallbackPatch.cs`, `Patches/StartupSafeMpHeroClassBootstrapPatch.cs` |
| Залишковий crash isolation | `Patches/IntermissionVmCrashGuardPatch.cs` |
| Listed-shell startup helper | `DedicatedHelper/DedicatedHelperLauncher.cs` |

#### Уже removable або вже видалене

| Зона | Статус |
| --- | --- |
| `GameMode/MissionMultiplayerCoopTdm*.cs` | видалено |
| `GameMode/MissionMultiplayerTdmClone*.cs` | видалено |
| `GameMode/MissionMinimal*DiagnosticMode.cs` | видалено |
| `CoopTdm` і `TdmClone` multiplayer strings | видалено |
| `EnableTdmCloneExperiment` branch | видалено |
| старий `TeamDeathmatch` override path, який підміняв live TDM runtime | видалено; замість нього тепер лишився тільки explicit listed startup override на `Module.StartMultiplayerGame("TeamDeathmatch", scene)` |
| `Patches/VanillaEntryUiSuppressionPatch.cs` | видалено; vanilla entry gauntlets тепер зрізаються структурно explicit listed mission assembly, а не окремим suppression patch-ем |

### 4.2 Що ще лишається legacy-шаром

Ці шматки коду ще потрібні для сумісності з native lifecycle і поки що повинні вважатися тимчасовими:

- `Patches/BattleMapHudSuppressionPatch.cs`
- `Patches/MissionScreenCameraPreviewPatch.cs`
- `Patches/IntermissionVmCrashGuardPatch.cs`
- видалений `Patches/LobbyCustomGameLocalJoinPatch.cs`; low-level decompile active DLL не показує live `LobbyGameStateCustomGameClient.StartMultiplayer(string,int,int,int)`, а one-shot localhost rewrite тепер owner-иться безпосередньо в `CoopSessionTransportPrimitives.StartClientTransport(...)`;
- `Patches/ListedShellClientWrapperOwnershipPatch.cs`
- `Patches/LobbyRequestJoinDiagnosticsPatch.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`

Ці файли ще не є "сміттям". Вони legacy, бо компенсують native lifecycle assumptions, які ми ще не перебрали у власний архітектурний шар.

### 4.3 Що вже видалено на першому етапі

На цьому етапі вже зроблено таке:

- видалені `CoopTdm` game-mode файли;
- видалені `TdmClone` game-mode файли;
- видалені minimal mission diagnostic modes, які існували тільки для старого crash isolation;
- видалений `Patches/MissionStateOpenNewPatches.cs`; після low-level підтвердження native call graph listed startup повністю owner-иться на `Module.StartMultiplayerGame("TeamDeathmatch", scene)` -> `MissionMultiplayerListedShellMode`, тому окремий mission-open fallback більше не потрібен;
- видалений listed-shell `MissionMatchHistoryComponent` і helper `MissionBehaviorHelpers.TryCreateMissionMatchHistoryComponent()`; hidden consumer `BannerlordNetwork.LobbyMissionType` для local match-history surface більше не входить у наш explicit listed mission stack;
- `ListedShellBaseNetworkTransportOwnershipPatch` більше не несе listed `BaseNetworkComponent` bootstrap logic у власному тілі: send-side `HandleNewClientConnect(...)`, receive-side `InitializeCustomGame`, `LoadMission` і `UnloadMission` тепер owner-яться `ListedShellNetworkBootstrapRuntime`, а patch лишив собі тільки native interception;
- `ListedShellClientWrapperOwnershipPatch` більше не несе listed wrapper-entry lifecycle у власному тілі: Diamond join-result arm, official custom/community client `StartMultiplayer(...)`, player-based hosted `SetStartingParameters(...)`/`HandleServerStartMultiplayer()` і platform-privilege interop тепер централізує `ListedShellWrapperInteropRuntime`, а patch лишив собі тільки native interception;
- `ListedShellMissionSessionState` забирає listed mission-instance token з native `BaseNetworkComponentData.CurrentBattleIndex`: explicit listed startup тепер arm-ить власний session token на server start, передає його через `InitializeCustomGameMessage`, приймає його на client receive path і використовує його ж у server-side `FinishedLoading` validation без `CurrentBattleIndex` fallback-а або mirror-write з нашого боку;
- `ListedShellClientSessionOwnershipState` тепер тримає listed client ingress як один явний lifecycle: join-result arm для wrapper-entry, promotion у receive-bootstrap після `GameNetwork.StartMultiplayerOnClient(...)` і фінальний disarm після listed transport teardown; `CustomGameJoinContextState` після цього лишився тільки join-address/local-roster fallback state, а не listed startup ownership gate;
- direct listed session bring-up примітиви `GameNetwork.StartMultiplayerOnClient(...)`, `GameNetwork.PreStartMultiplayerOnServer()`, `GameNetwork.StartMultiplayerOnServer(...)`, `BannerlordNetwork.CreateServerPeer()`, listed `ClientFinishedLoading(...)`, `UnloadMission(...)` і send-side bootstrap messages більше не висять прямо в listed runtime helper-ах; `CoopSessionTransportPrimitives` тепер локалізує цей нижній native MP transport layer, а `ListedShellSessionTransportRuntime`, `ListedShellNetworkBootstrapRuntime` і `PendingBattleFinishedLoadingTransportRuntime` лишили собі orchestration та ownership;
- deferred non-listed `FinishedLoading` для `PendingBattleMissionStartupState` більше не читає native `BaseNetworkComponentData.CurrentBattleIndex` взагалі; explicit pending-battle startup state тепер захоплює `LoadMission.BattleIndex` прямо на server-side `GameNetwork.WriteMessage(LoadMission)` send path, активує цей token при резолюції місії і далі порівнює client `FinishedLoading.BattleIndex` уже тільки з own-ed mission-session state;
- listed server message fan-out теж більше не розмазаний по runtime helper-ах: `KillDeathCountChange`, `BotsControlledChange`, `BotData`, `UpdateRoundScores`, listed `UnloadMission` broadcast і reflected `MissionStateChange` send path тепер проходять через `CoopSessionTransportPrimitives`, тому `ListedShellLobbyRuntime` і `ListedShellMissionScoreboardComponent` уже не тримають сирі `BeginBroadcastModuleEvent` / `BeginModuleEventAsServer` як власний transport contract;
- hosted listed server start/shutdown finalize теж більше не збирається вручну в listed runtime helper-ах: старт server transport, optional `CreateServerPeer()` і local-host `ClientFinishedLoading()` тепер owner-яться `CoopSessionTransportPrimitives.FinalizeHostedServerTransportStart(...)`, а listed server endgame `UnloadMission` broadcast + `UnSynchronizeEveryone()` + `EndMultiplayerLobbyMission()` тепер схлопнуті в `CoopSessionTransportPrimitives.CompleteServerLobbyMissionShutdown(...)` замість розбитого runtime sequence;
- `LateJoinPeerStateReplayOwnershipPatch` теж більше не тримає сирі `ExistingObjectsBegin`, `SynchronizeMissionTimeTracker` і `ExistingObjectsEnd` send-и; replay boundary для `SendExistingObjectsToPeer(...)` тепер проходить через `CoopSessionTransportPrimitives`, тож у patch-layer уже не лишилося прямих `GameNetwork.BeginModuleEventAsServer(...)` / `WriteMessage(...)` викликів;
- listed client `LoadMission` / `UnloadMission` receive path теж менше керує carrier-кроками вручну: `ListedShellNetworkBootstrapRuntime` тепер входить у mission transition через `CoopSessionTransportPrimitives.BeginClientMissionReceiveTransition()` і `BeginClientLobbyMissionUnload()` / `CompleteClientLobbyMissionUnload()`, тому unsync/chat/loading-window tail ще сильніше замкнений у нижньому session-lifecycle шарі;
- listed і deferred `FinishedLoading` transport step теж більше не дублюють lower-level branch `UnloadMission` vs `ClientFinishedLoading`; обидва runtime-и тепер делегують цей decision `CoopSessionTransportPrimitives.CompletePeerFinishedLoadingTransportStep(...)`, тому нижній native carrier ще сильніше звужений до одного transport boundary;
- `ListedShellMissionLobbyClientComponent` теж більше не тримає reflected `NetworkMain.GameClient` / `LoggedIn` / `CurrentState` / `EndCustomGame` / `QuitFromCustomGame` interop усередині concrete lobby shell; цей native lobby-client хвіст тепер винесений у `ListedShellLobbyClientInteropRuntime`, тому сам client lobby component залишив собі тільки mission shell і inactivity/tick orchestration;
- client/server session teardown дрібниці теж зібрані там само: local `MyPeer.IsSynchronized = false`, `BannerlordNetwork.EndMultiplayerLobbyMission()`, `UnSynchronizeEveryone()`, chat mute reset і `LoadingWindow.DisableGlobalLoadingWindow()` більше не викликаються напряму з listed runtime helper-ів, а проходять через `CoopSessionTransportPrimitives` як один нижній session-lifecycle шар;
- навіть усередині `CoopSessionTransportPrimitives` raw native send-scope тепер ще менше роздубльований: `BeginModuleEventAsServer` / `BeginBroadcastModuleEvent` envelopes для listed `UnloadMission`, typed server messages і reflected server messages схлопнуті в спільні private carrier helpers, тому нижній transport boundary має вже не кілька паралельних send-path-ів, а один локалізований message envelope contract;
- listed new-client bootstrap bundle теж більше не збирається руками в `ListedShellNetworkBootstrapRuntime`: `MultiplayerOptionsInitial`, `MultiplayerOptionsImmediate`, optional `MultiplayerOptionsDefault` і `InitializeCustomGameMessage` тепер шлються через один carrier primitive `CoopSessionTransportPrimitives.SendInitializeCustomGameBootstrapBundle(...)`, тому bootstrap message pack уже живе в нижньому transport layer, а listed runtime лишив собі ownership, mission-session token і logging;
- native `Module.CurrentModule.StartMultiplayerGame(...)` для listed bring-up теж більше не викликається напряму з кількох runtime helper-ів: hosted listed server start, listed `InitializeCustomGame` receive і listed `LoadMission` receive тепер заходять у цей engine start-step через `CoopSessionTransportPrimitives.TryStartMissionSessionGame(...)`, тому even mission-session bring-up primitive уже локалізований біля нижнього carrier шару;
- hosted listed server bring-up після цього теж зібраний ще щільніше: `PreStartMultiplayerOnServer()`, `StartMultiplayerGame(...)`, wait-until-mission-active і `FinalizeHostedServerTransportStart(...)` тепер схлопнуті в один нижній primitive `CoopSessionTransportPrimitives.TryBringUpHostedMissionSessionAsync(...)`, а `ListedShellSessionTransportRuntime` лишив собі лише ownership/result logging;
- видалений `Patches/LocalJoinAddressPatch.cs`; one-shot localhost self-join rewrite більше не owner-иться global Harmony hook-ом на `GameNetwork.StartMultiplayerOnClient`, а виконується прямо в `CoopSessionTransportPrimitives.StartClientTransport(...)` як частина explicit listed client transport bring-up;
- видалений `Patches/FinishedLoadingMissionReadyGatePatch.cs`; `HandleClientEventFinishedLoading(...)` тепер входить до того ж explicit `BaseNetworkComponent` transport shell, що й `HandleNewClientConnect(...)`, `InitializeCustomGame`, `LoadMission` і `UnloadMission`: listed validation маршрутизується в `ListedShellSessionTransportRuntime`, а deferred validation для `PendingBattleMissionStartupState` — у `PendingBattleFinishedLoadingTransportRuntime`, без окремого historical patch-файлу;
- listed `BaseNetworkComponent` bootstrap message graph теж більше не розмазаний по трьох receive patch-ах і send patch-у: `HandleNewClientConnect`, `InitializeCustomGame`, `LoadMission` і `UnloadMission` тепер owner-яться через `ListedShellNetworkBootstrapRuntime`, а patch-класи лишили собі тільки interception і ownership-gate;
- прибрані `CoopTdm` і `TdmClone` ids з `Infrastructure/CoopGameModeIds.cs`;
- прибраний `EnableTdmCloneExperiment` з `Infrastructure/ExperimentalFeatures.cs`;
- прибраний `EnableBattleMapClientEquipmentNetworkComponent`; battle-map/client stack decisions тепер фіксуються структурно в `MissionMultiplayerCoopBattleMode`, а не через runtime toggle;
- прибрана мертва wrapped-Battle crash-isolation гілка, яка намагалась вирізати `MissionLobbyEquipmentNetworkComponent` із client stack;
- прибраний dead wrapped-`Battle` client path із старого `MissionStateOpenNewPatches.cs`; `Battle` тепер іде через `CoopBattle` game-mode override без окремого mission-open wrapper-а;
- прибраний увесь `MissionStateOpenNew` fallback-шар; listed mission open тепер іде тільки через explicit `MissionMultiplayerListedShellMode` + `ListedShellMissionBehaviorFactory` без triage-era wrapper-а;
- прибраний native spawn gold floor/deduction compatibility path навколо `ReplaceBotWithPlayer(...)`, бо coop runtime і client mode більше не використовують vanilla gold economy як spawn contract;
- прибраний server-only `MultiplayerHeroClassOverridePatch`, який підміняв `MultiplayerClassDivisions.GetMPHeroClassForPeer(...)` для старого vanilla spawn/class path;
- pending native spawn visuals більше не форсяться в coop server path, якщо native `ShouldSpawnVisualsForServer(...)` не вимагає їх для поточного peer/runtime;
- `HasSpawnedAgentVisuals` більше не використовується в server-side phase/deployment або spawn authority; окремий visual-flag compatibility tail у нашому коді вже видалений;
- `SelectedTroopIndex` compatibility path більше не активується ні в custom `CoopBattle`, ні у listed shell; pending-spawn bootstrap window більше не армує навіть локальний class-index cache;
- native `MissionNetworkComponent.SendTroopSelectionInformation(...)` більше не входить у listed late-join contract; authoritative selection для late join тепер приходить через `CoopMissionNetworkBridge` payloads, а native `UpdateSelectedTroopIndex` replay / suppression patch уже прибрані разом із самим native troop-index output path;
- live native `UpdateSelectedTroopIndex` broadcast теж більше не входить у coop runtime; після виносу native troop-index output path не лишилося навіть локального listed-shell class-index compatibility surface;
- native `MissionLobbyComponent.SendPeerInformationsToPeer(...)` більше не входить у listed late-join bootstrap contract; replay `KillDeathCountChange` / `BotsControlledChange` тепер іде через `ListedShellLobbyRuntime`, а не через native lobby shell;
- native `MissionLobbyComponent.OnAgentRemoved(...)` більше не керує listed player-death path: immediate peer move у spectator holding, clear active control/runtime requests, lifecycle `DeadAwaitingRespawn`, respawn timer, player death counter, player kill/score updates і suicide penalty тепер уже ставляться нашим coop runtime / patch-layer;
- listed assist/count path теж більше не тягне private native `RemoveHittersAndGetAssistorPeer(...)`: assistor resolution тепер іде напряму через `Agent.GetAssistingHitter(...)`, а не через reflected helper усередині `MissionLobbyComponent`;
- listed commander-owned bot death і `BotsControlledChange` теж більше не ставляться native `MissionLobbyComponent.OnAgentRemoved(...)`: controlling peer death counter, controlling-bot alive count, scoreboard refresh, `KillDeathCountChange` і `BotsControlledChange` тепер ідуть через `ListedShellLobbyRuntime`;
- listed controlled-bot owner path теж більше не тягне reflected `Formation.PlayerOwner` getter/backing field: owner resolution тепер іде через прямий `formation.PlayerOwner` contract;
- pure side-bot `BotData` bookkeeping теж більше не ставиться native `MissionLobbyComponent.OnAgentRemoved(...)`: listed bot-vs-bot і bot-vs-player kills/deaths тепер оновлюють `CoopBattleScoreboardRuntimeState`, а native `MissionScoreboardComponent` side `BotScores` лишаються тільки compatibility mirror перед `BotPropertiesChanged(...)` і `NetworkMessages.FromServer.BotData`, без reflected `OnBotDies(...)` / `OnBotKills(...)` fallback у live path;
- native `HandleClientEventRequestCultureChange(...)`, `HandleClientEventRequestChangeCharacterMessage(...)`, `HandleServerEventChangeClassRestrictions(...)` і `HandleServerEventChangeCulture(...)` теж більше не входять у listed shell: цей request/message surface уже не реєструється нашими listed lobby shell-компонентами, тож live native handler-и більше не присутні в network graph;
- live native `SetPeerTeam` / `ChangeCulture` compatibility broadcasts теж більше не ллються в snapshot-unready peer-и; якщо `SendExistingObjectsToPeer` спрацював до snapshot readiness, late-join bootstrap gate тепер просто відкладається й дограється вже через `CoopMissionNetworkBridge` readiness ack без native peer-state replay;
- live native `SetPeerTeam` / `ChangeCulture` compatibility broadcasts взагалі прибрані з active coop runtime; late-join peer-state replay теж більше не шле native peer-team/culture state, а ownership лишається тільки за snapshot-ready bootstrap gate;
- `ClientChangeCultureCanonicalizationPatch` і `ServerChangeCultureCanonicalizationPatch` видалені; fixed `Attacker=empire / Defender=vlandia` canonicalization більше не входить у runtime, бо culture replay тепер already-authoritative і не повинен переписуватись legacy TDM/fixed-culture шаром;
- fixed mission culture experiment усередині `CoopMissionBehaviors` теж прибраний; runtime culture тепер резолвиться від authoritative preferred entry і, якщо треба, від snapshot-side culture, а не від жорсткого `Attacker=empire / Defender=vlandia`;
- coop hero-class resolution, allowed-class sync, authoritative troop fallback і listed direct authoritative spawn більше не читають native `MissionPeer.Team` / `MissionPeer.Culture` як runtime input; прямий listed-spawn color path теж уже бере colors від authoritative side/culture, тому native peer state звузився до пізнього compatibility surface навколо late-join peer replay;
- локальний battle-map handoff і exact pre-spawn loadout side-resolution теж більше не беруть `MissionPeer.Team` як fallback; вони тепер спираються на `controlledAgent.Team`, `ExactCampaignSnapshotAgentOrigin.Side` і `CoopBattleAuthorityState`, а не на native peer-state cache;
- active coop runtime side/team logic теж більше не бере `MissionPeer.Team` як source of truth: `ResolveAuthoritativeSide(...)`, materialized replace-bot, commander-control promotion, pending-spawn/lost-life path і server-side select-all suppression тепер спираються на `controlledAgent.Team` або authoritative mission team; native `MissionPeer.Team` лишився головно в compatibility write/replay шарі та логах.
- `CoopBattlePeerSessionState` і spectator fallback у native late-join replay теж більше не читають runtime side з `MissionPeer.Team`; session/runtime side тепер йде від active `controlledAgent.Team`, а spectator replay — від explicit coop session stage `NoSide`.
- `SelectedTroopIndex` compatibility path теж більше не читає native `MissionPeer.SelectedTroopIndex` для прийняття рішень; listed hero-class sync і direct spawn тепер ідуть напряму від authoritative coop selection без локального class-index cache, native troop-index або `UpdateSelectedTroopIndex`.
- presentation/helper layer теж далі чиститься від native peer-team читань: dead `CoopSelectionUiHelpers` banner helper-и прибрані, а live exact banner seeding більше не використовує `MissionPeer.Team` для assigned-peer lookup.
- listed-shell native `TeamInitialPerkInfoReady` більше не залежить виключно від `MissionLobbyEquipmentNetworkComponent`, але й більше не моститься server-side, бо live listed-shell spawn reader для цього gate вже прибраний;
- native `MissionLobbyEquipmentNetworkComponent` повністю прибраний із wrapped listed `TeamDeathmatch` shell; listed ingress більше не тримає even passive equipment compatibility component;
- native `MultiplayerTeamSelectComponent` повністю прибраний із wrapped listed `TeamDeathmatch` shell; listed ingress більше не несе окремий team-select compatibility layer;
- native `MultiplayerMissionAgentVisualSpawnComponent` повністю прибраний із wrapped listed `TeamDeathmatch` shell; listed ingress більше не тримає even passive visual compatibility component;
- `MissionRecentPlayersComponent` прибраний із `CoopBattle` і listed ingress stack-ів; останній native `OnTeamChanged`-driven recent-player shell більше не входить у live bootstrap path;
- listed-shell bootstrap більше не піднімає `HasSpawnedAgentVisuals` / `EquipmentUpdatingExpired`; visual-flag lifecycle більше не існує в нашому compatibility layer навіть у listed ingress;
- native `MissionMultiplayerTeamDeathmatch` і `MissionMultiplayerTeamDeathmatchClient` прибрані із wrapped listed `TeamDeathmatch` shell і замінені на `ListedShellCompatibilityMode` / `ListedShellCompatibilityModeClient`; listed mission-mode layer більше не має TDM score loop, kill gold, respawn gold або score-based match-end, а зберігає тільки team/banner setup, client `MissionMode.Battle` і representative graph;
- native `TeamDeathmatchSpawningBehavior`, native `TeamDeathmatchSpawnFrameBehavior` і сам mission-stack `SpawnComponent` прибрані із wrapped listed `TeamDeathmatch` shell; listed spawn ingress більше не має TDM gold gate, selected-troop fallback-to-zero, troop-cost deduction або official TDM spawn-point class;
- active listed spawn authority повністю сидить у `CoopMissionSpawnLogic`, а `ListedShellSpawnFrameBehavior` лишився лише локальним helper-резолвером spawn frame, а не mission behavior shell;
- старий `MissionStateOpenNewPatches.cs` більше не модифікує vanilla `TeamDeathmatch` behavior list по місцю; listed ingress тепер збирається явно в native order з мінімального shell-контракту і наших compatibility replacements;
- listed-shell direct spawn ingress уже не має ні native troop-index output, ні локального class-index cache; `TeamInitialPerkInfoReady` і visual flags теж уже не входять у це bootstrap-вікно;
- `MultiplayerTeamSelectComponent` прибраний з `CoopBattle` server і client stack, а також повністю прибраний з wrapped listed shell;
- `MissionLobbyEquipmentNetworkComponent` прибраний з `CoopBattle` client stack; custom runtime більше не несе native equipment/class bootstrap, лишився тільки listed-shell legacy;
- `MultiplayerMissionAgentVisualSpawnComponent` прибраний з `CoopBattle` client stack; custom runtime більше не несе native agent-visual bootstrap;
- native `MissionNetworkComponent.OnPeerSelectedTeam(...)` більше не шле `CreateAgentVisuals` ні для custom `CoopBattle`, ні для listed ingress shell;
- passive native `ConsoleMatchStartEndHandler` приглушений для custom `CoopBattle` runtime і більше не тримає visual-spawn/platform-state contract;
- passive native `ConsoleMatchStartEndHandler` також прибраний із wrapped listed `TeamDeathmatch` shell, бо listed ingress більше не несе native visual component;
- із старого wrapped listed `TeamDeathmatch` shell прибраний чисто діагностичний ballast: `MissionBehaviorDiagnostic` і повний wrapper stack-dump із `MissionStateOpenNewPatches.cs`;
- прибрані мертві `AllowLegacyVanillaTeamSelectionInteraction` / `AllowLegacyVanillaClassSelectionInteraction` з `Infrastructure/CoopBattleEntryPolicy.cs`;
- видалений мертвий direct-spawn experiment (`EnableDirectCoopPlayerSpawnExperiment`, `TrySpawnPeersIntoCoopControl(...)`, `SpawnCoopControlledAgent(...)`, `TryEnsurePendingSpawnVisuals(...)`), який уже не входив у live runtime tick path;
- видалений active vanilla spawn-bridge hook (`RunVanillaSpawnBridgeTick(...)` / `TryFinalizePendingNativeSpawnVisualCompatibility(...)`), який переводив native preview visuals у `SpawningBehaviorBase` і `Mission.SpawnAgent(..., spawnFromAgentVisuals: true)`;
- прибраний старий feature-flag `EnableVanillaMissionWrapping`; listed `TeamDeathmatch` shell wrapping тепер є явною частиною поточного join/startup контракту, а не runtime toggle;
- прибрана стара логіка `TeamDeathmatch` override, яка підміняла live TDM runtime; замість неї тепер живе explicit listed startup override на `Module.StartMultiplayerGame("TeamDeathmatch", scene)` з `MissionMultiplayerListedShellMode`;
- прибрані `CoopTdm` і `TdmClone` strings з `Module/CoopSpectator/ModuleData/multiplayer_strings.xml`;
- прибрані dedicated-project compile includes для видалених TDM файлів;
- старий wrapper vanilla mission-open більше не інжектить `MissionMinimalServerDiagnosticMode` і `MissionMinimalClientDiagnosticMode`;
- з `Mission/CoopMissionBehaviors.cs` прибраний мертвий legacy vanilla-selection/UI reflection шар, включно зі старими hint/menu path, class-loadout filtering, culture-sync і visual auto-confirm splice;
- прибраний compile-time dead native preferred-troop request experiment і пов’язаний passive observer на `MissionPeer.SelectedTroopIndex`, який вже не мав власного runtime-ефекту.

### 4.4 Що ще треба перенести або переписати

Головні блокери до повністю clean coop runtime зараз такі:

1. Замінити залежність від official listed-shell `TeamDeathmatch` тільки після того, як буде доведений альтернативний server-list registration і join path без нього; live mission-mode/spawn layer всередині listed ingress уже більше не повинен тримати native TDM authority.
2. Прибрати решту native `MissionLobbyComponent` shell тоді, коли listed ingress уже не потребуватиме його для server-list startup/join contract.
3. Прибрати bridge-file fallback-и тоді, коли network transport стане достатньо надійним для selection, spawn, readiness і reconnect flows.
4. Після переносу listed startup у `Module.StartMultiplayerGame("TeamDeathmatch", scene)` -> `MissionMultiplayerListedShellMode` добивати решту native ingress shell уже без `MissionStateOpenNew` interception: далі ціллю є повний відрив від official listed startup/join path.
5. Окремо переоцінити `CoopSessionTransportPrimitives.StartClientTransport(...)` і решту join patch-ів, коли public, VPN і self-host join flows будуть розділені чистіше по відповідальності.
6. Прибрати crash-isolation patch-і на кшталт `IntermissionVmCrashGuardPatch`, коли battle-map lobby/intermission lifecycle буде вже нашим, а не native.
7. Переоцінити і прибрати тимчасовий pre-battle ranged exact crash-containment у `BattleMapSpawnHandoffPatch.cs`, коли буде доведений чистий ammo/reload contract без suppress `SetWeaponReloadPhase`, `SetWeaponAmmoData` і ammo-semantic `SetWeaponNetworkData`.

## 5. Roadmap до clean coop core без TDM

### 5.1 Фаза A: стабілізувати listed-shell startup contract

- залишаємо official `TeamDeathmatch` у startup config;
- залишаємо native lobby, timer і scoreboard shell; team-select уже прибраний із live ingress stack;
- лишаємо `Battle` override у `CoopBattle`;
- не чіпаємо join patch-і, поки не валідуємо public/VPN/local self-join paths окремо.

### 5.2 Фаза B: зменшити native mission wrapping

- `MissionStateOpenNew` fallback уже прибраний; далі зменшувати решту native UI/intermission suppression шарів, які ще сидять навколо official listed ingress;
- зменшувати кількість UI suppression patch-ів, замінюючи native entry/intermission views власними coop views, а не приховуючи їх постфактум.

### 5.3 Фаза C: повністю забрати side selection і materialization

- завершити перехід від native team-select stack/UI bootstrap до власної coop-owned authority і readiness-моделі;
- прибрати решту native `MissionLobbyComponent`-залежного ingress shell, коли listed startup/join contract уже буде зібраний повністю нашими coop-owned entry points;
- завершити винос late-join peer sync із native `MissionNetworkComponent` corridor у повністю власний coop-owned ingress path без `MissionState.OpenNew` interception.
- після цього переоцінити, чи можна повністю прибрати навіть server-side native `MissionPeer.Team` compatibility writes, коли залишковий lobby/native shell перестане їх читати.

### 5.4 Фаза D: відчепити мод від vanilla listed shell

- повернутися до питання server-list registration тільки після того, як coop runtime зможе стартувати і приймати клієнта без TDM mission shell;
- тільки після цього можна реально розглядати прибирання official `TeamDeathmatch` із startup config.

### 5.5 Відкладений шлях 2: повний transport replacement

- це свідомо не поточний пріоритет; зараз ми йдемо шляхом 1 і лишаємо native `GameNetwork` / `BaseNetworkComponent` як нижній carrier, але послідовно забираємо з нього orchestration, listed bootstrap, lobby flow і runtime authority;
- шлях 2 стане доречним тільки якщо шлях 1 упреться в системні обмеження під час подальшої розробки модa або нових battle-family runtime-ів;
- що він може дати:
  - повний контроль над session startup/join/bootstrap lifecycle без official listed/custom-game wrapper-ів;
  - власний transport/container contract замість залежності від native `BaseNetworkComponent` message graph;
  - чистішу основу для майбутніх siege/ambush/raid/hideout runtime-ів, якщо native MP carrier почне жорстко обмежувати архітектуру;
  - простіше reasoning про coop state, бо transport ownership і gameplay ownership житимуть в одному модульному шарі;
- що він коштує:
  - найвищий ризик зламати server-browser visibility, hosted/dedicated join і late-client bootstrap;
  - майже напевно це буде не один cleanup-зріз, а окрема велика фаза;
  - до перших повноцінних runtime-прогонів цей шлях брати передчасно, бо шлях 1 уже дає значно безпечніший маршрут до working clean field-battle validation.

### 5.6 Робоче правило для наступних cleanup-ів

Якщо шматок коду існує тільки для компенсації TDM або vanilla mission lifecycle, це кандидат на видалення.

Якщо шматок коду лежить усередині перевіреного server-list join contract, його не можна прибирати, доки replacement path не доведений у runtime.
