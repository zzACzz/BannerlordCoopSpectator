# Специфікація Exact Transfer Contract

Дата: 2026-05-02

## Мета

Визначити точний adapter-contract, який мапить дані battle snapshot із кампанії
у native multiplayer spawn contract Bannerlord для strict exact hero.

Обрана стратегія:

- не будувати власний multiplayer runtime
- не продовжувати розширювати surrogate runtime shim
- адаптувати campaign data під native multiplayer / TDM lifecycle
- починати нову реалізацію тільки після того, як контракт описаний повністю

Найближчий scope:

- `main hero`
- `lords`
- `companions` / інші exact personal hero entry
- особливо mounted remote hero materialization на клієнті

Що не входить у цей етап:

- повний rollout exact 1:1 для bulk troops
- нові post-spawn visual repair як primary path
- нові speculative guard/fallback патчі без contract-level причини

## Поточний висновок

Проблема не зводиться до простого “не ті поля”.
Проблема складається з:

- field mapping
- legal event sequence
- rider/mount lifecycle
- peer binding timing
- commander-control ownership timing
- death/respawn cleanup timing

Тобто це проблема контракту, а не один окремий баг.

## Native multiplayer spawn contract

### Послідовність client materialization

На клієнті native послідовність для spawned agents виглядає так:

1. `CreateAgent`
2. `Mission.SpawnAgent(agentBuildData)`
3. `SetAgentPeer`
4. `SynchronizeAgentSpawnEquipment`
5. `SetWieldedItemIndex`
6. `SetWeaponNetworkData` / `SetWeaponAmmoData`
7. `ReplaceBotWithPlayer`, якщо це релевантно
8. `SetAgentHealth` / death / remove lifecycle

### Контракт `CreateAgent`

Decompiled `MissionNetworkComponent.HandleServerEventCreateAgent(...)` показує,
що клієнт збирає `AgentBuildData` з такого payload:

- `Character`
- `Peer`
- `Monster`
- `SpawnEquipment`
- `MissionEquipment`
- `BodyPropertiesSeed`
- `IsFemale`
- `TeamIndex`
- `Position`
- `Direction`
- `FormationIndex`
- `MountAgentIndex`
- `IsPlayerAgent`
- `ClothingColor1`
- `ClothingColor2`

Критично важлива branch-логіка:

- якщо `IsPlayerAgent == true`, body properties беруться з
  `missionPeer.Peer.BodyProperties`
- якщо `IsPlayerAgent == false`, body properties derivе-яться з
  `character.GetBodyPropertiesMin/Max()` і `character.BodyPropertyRange`
- якщо formation немає, але `missionPeer != null`, banner береться з
  `missionPeer.Peer.BannerCode`
- в кінці handler викликає `Mission.SpawnAgent(agentBuildData)` і відразу торкає
  `.MountAgent`

Отже `CreateAgent` уже сам по собі є composite contract:

- identity contract
- body contract
- formation/banner contract
- mount-index contract

### Очікування `Mission.SpawnAgent(...)`

З decompiled `Mission.SpawnAgent(...)` видно:

- `AgentCharacter` повинен бути валідним
- age / body / gender нормалізуються ще до завершення повного spawn
- team, colors, origin, formation, position, direction, equipment і mount index
  споживаються прямо під час spawn
- body properties можуть бути застосовані ще до решти lifecycle

Тобто неправильний identity/body/equipment стан — це не просто visual bug.
Він може зламати native materialization ще до того, як взагалі почнуть працювати
пізніші recovery hook.

### Native post-spawn handlers

Ключові decompiled handlers:

- `SetAgentPeer` лише біндить `MissionPeer` до вже існуючого agent
- `SynchronizeAgentSpawnEquipment` викликає
  `UpdateSpawnEquipmentAndRefreshVisuals(...)`
- `ReplaceBotWithPlayer` переприв’язує bot agent до peer, formation і health
- `SetWieldedItemIndex` передбачає, що agent і equipment state уже валідні
- `SetAgentHealth` передбачає, що target agent index розв’язується в живий agent

Отже:

- якщо `CreateAgent` не materialized коректно, пізніший `SetAgentPeer` або visual
  refresh не є заміною spawn success
- пізніші handler — це consumers of success, а не спосіб заднім числом створити success

## Campaign-side contract

### Authoritative entry model

Поточна authoritative battle snapshot entry — це
`Infrastructure/BattleSnapshotRuntimeState.cs::RosterEntryState`.

Поля, які вже є:

- identity:
  - `EntryId`
  - `SideId`
  - `PartyId`
  - `CharacterId`
  - `OriginalCharacterId`
  - `SpawnTemplateId`
  - `CultureId`
- hero identity:
  - `HeroId`
  - `HeroRole`
  - `HeroOccupationId`
  - `HeroClanId`
  - `HeroTemplateId`
  - `HeroBodyProperties`
  - `HeroLevel`
  - `HeroAge`
  - `HeroIsFemale`
  - `IsHero`
- combat profile:
  - `IsMounted`
  - `IsRanged`
  - `HasShield`
  - `HasThrown`
  - `BaseHitPoints`
  - `PerkIds`
- stats:
  - attributes
  - weapon skills
  - riding / athletics
- exact combat equipment:
  - `CombatItem0Id..CombatItem3Id`
  - `CombatItem0Amount..CombatItem3Amount`
  - `CombatHeadId`
  - `CombatBodyId`
  - `CombatLegId`
  - `CombatGlovesId`
  - `CombatCapeId`
  - `CombatHorseId`
  - `CombatHorseHarnessId`

Цього достатньо, щоб описати exact personal hero state, але цього ще недостатньо,
щоб гарантувати коректну подачу цих даних у native multiplayer runtime у
правильний момент і в правильній формі.

### Runtime exact object layer

`Infrastructure/ExactCampaignRuntimeObjectRegistry.cs` уже вміє створювати:

- runtime `BasicCharacterObject` на `EntryId`
- runtime `MPHeroClass` wrapper на `EntryId`

Цей шар уже вміє:

- інжектити battle equipment
- інжектити exact body properties
- виводити mounted/ranged/runtime formation traits

Це важливий будівельний блок для обраної стратегії:

- треба спиратися на exact runtime objects і явні adapter contract
- не треба лишати payload mutation у troop surrogate як довгостроковий primary path

### Поточний direct-spawn reference path

`Mission/CoopMissionBehaviors.cs::SpawnCoopControlledAgent(...)` корисний як
reference implementation, бо вже показує чистіший локальний construction path:

- резолв authoritative team
- обчислити spawn frame
- зібрати exact snapshot equipment
- побудувати `AgentBuildData`
- застосувати entry identity / body
- викликати `Mission.SpawnAgent(...)`
- забіндити ownership і mission peer
- опційно оновити visuals / wield initial weapons

Це ще не фінальне рішення для multiplayer hero transfer, але це найкращий уже
наявний reference path у репо для чистого adapter-style construction.

## Mapping matrix

### Identity і class

Native requirement:

- multiplayer-valid `Character`
- multiplayer-valid `AgentOrigin`
- multiplayer-valid class semantics

Campaign source:

- `CharacterId`
- `OriginalCharacterId`
- `SpawnTemplateId`
- `HeroTemplateId`
- runtime exact object registry

Adapter rule:

- strict exact hero повинен резолвитись у явний runtime exact character/class contract
- цей resolution має бути стабільний до spawn
- runtime spawn не має деградувати до troop surrogate як фінальна архітектура

### Body contract

Native requirement:

- валідні body properties на `CreateAgent`
- узгоджений age / gender
- валідний `BodyPropertyRange`, якщо native іде random-body branch

Campaign source:

- `HeroBodyProperties`
- `HeroAge`
- `HeroIsFemale`

Adapter rule:

- strict exact hero повинен по можливості уникати native random-body derivation
- exact body має бути частиною pre-spawn contract, а не пізнім visual patch

### Equipment contract

Native requirement:

- `SpawnEquipment`
- `MissionEquipment`
- стабільні weapon slots
- валідна horse / harness semantics

Campaign source:

- `CombatItem0..3`
- armor slots
- `CombatHorseId`
- `CombatHorseHarnessId`

Adapter rule:

- exact personal hero повинен мати один canonical snapshot equipment contract
- для кожного slot має бути явна policy:
  - `safe pre-spawn`
  - `safe post-bind sync`
  - `unsafe / deferred`

### Mount contract

Native requirement:

- rider `MountAgentIndex`
- mount materialized як native agent
- rider/mount link існує до exact-ready state

Campaign source:

- `IsMounted`
- `CombatHorseId`
- `CombatHorseHarnessId`

Adapter rule:

- mount contract є first-class частиною spawn, а не просто visual detail
- `ExactReady` незаконний, поки rider і mount не materialized та не linked

### Peer binding contract

Native requirement:

- `SetAgentPeer` після того, як agent уже існує
- peer body/banner/team semantics стають валідними лише після реального peer bind

Campaign source:

- player/peer ownership із battle/session state
- entry claim і commander ownership data

Adapter rule:

- peer binding має бути окремим stage
- `CommanderReady` не може настати до валідного `PeerBound`

### Wield contract

Native requirement:

- weapon slots існують і synchronized
- wield відбувається лише після валідного agent/equipment state

Campaign source:

- exact equipment
- derived initial wield preference

Adapter rule:

- initial wield треба один раз derivе-ити в окремий sub-contract
- primary behavior більше не може базуватись на “оновлювати, поки візуально не схоже”

### Commander-control contract

Native requirement:

- identity controlled agent стабільна
- formation ownership і order UI semantics біндяться до правильного agent

Campaign source:

- entry ownership
- peer selection / selected entry
- side / party / commander identity

Adapter rule:

- commander-control enablement — це пізніший stage, ніж spawn
- remote hero не може входити в commander-control semantics, поки transfer incomplete

### Cleanup contract

Native requirement:

- death/remove/update message приходять на валідні rider і mount index
- respawn/index reuse не протікає старим state у новий lifecycle

Campaign source:

- entry identity
- mounted pair identity
- respawn claims / selected entry state

Adapter rule:

- rider+mount cleanup повинні бути однією lifecycle unit
- state clear має бути на рівні pair, а не набору розкиданих cache

## Жорсткі інваріанти для майбутньої реалізації

Для mounted strict exact hero:

- `CreateAgentAccepted` не дорівнює `RiderMaterialized`
- `RiderMaterialized` не дорівнює `MountMaterialized`
- `MountMaterialized` не дорівнює `MountLinked`
- `ExactReady` незаконний, поки `MountLinked == false`
- `CommanderReady` незаконний, поки `ExactReady == false`
- queued refresh ніколи не дорівнює applied state
- death cleanup повинна чистити state rider і mount разом

## Що доводять останні невдалі прогони

1. Server-side strict exact hero pre-spawn injection уже здатний побудувати
   правильний exact equipment contract для host hero.
2. Локальний клієнт усе ще ламається раніше, всередині native `CreateAgent`,
   до того, як існує валідна rider/mount pair.
3. Surrogate payload mutation погіршує visuals і semantics, але не прибирає
   кореневу поломку.
4. Отже наступний безпечний крок — не новий runtime shim, а чистий
   contract-first redesign exact transfer adapter.

## Analysis work package до нової імплементації

### Пакет A: повна native lifecycle spec

Задокументувати з code reference точний legal order і припущення для:

- `CreateAgent`
- `Mission.SpawnAgent`
- `SetAgentPeer`
- `SynchronizeAgentSpawnEquipment`
- `SetWieldedItemIndex`
- `ReplaceBotWithPlayer`
- `SetAgentHealth`
- death / remove / respawn

Deliverable:

- одна native lifecycle diagram
- один список `що вже має бути валідним на цьому stage`

### Пакет B: повна campaign-source matrix

Для кожного field, який споживає native lifecycle, задокументувати:

- джерело в `RosterEntryState`
- direct / derived / missing
- safe pre-spawn / safe post-bind / unsafe

Deliverable:

- одна mapping matrix без порожніх рядків

### Пакет C: явний adapter contract object

До коду визначити форму нового adapter object:

- `IdentityContract`
- `BodyContract`
- `EquipmentContract`
- `MountContract`
- `PeerBindingContract`
- `InitialWieldContract`
- `CommanderControlContract`
- `CleanupContract`

Deliverable:

- одна C#-орієнтована структурна специфікація

### Пакет D: exact hero state machine

До імплементації визначити legal stages і transitions:

1. `SnapshotResolved`
2. `ClassResolved`
3. `PreSpawnPrepared`
4. `CreateAgentAccepted`
5. `RiderMaterialized`
6. `MountMaterialized`
7. `MountLinked`
8. `PeerBound`
9. `EquipmentSynchronized`
10. `ExactReady`
11. `CommanderReady`
12. `DeathCleaned`

Deliverable:

- transition table
- список forbidden transition
- окремий список `blocked reasons`

### Пакет E: implementation gate review

Нова hero-first імплементація не починається, поки всі пункти нижче не істинні:

- кожне native field має campaign source або явне derivation rule
- кожен lifecycle stage має owner і preconditions
- кожен mounted-hero failure mode має contract-level policy
- surrogate troop fallback не входить у target hero path

## Transition table, яку ще треба реалізувати кодом

| Stage | Вхідні умови | Що заборонено |
| --- | --- | --- |
| `SnapshotResolved` | є валідний `RosterEntryState` | переходити до spawn без identity/body/equipment validation |
| `ClassResolved` | є runtime exact character/class | підміняти troop surrogate як фінальний target |
| `PreSpawnPrepared` | зібрано adapter contract для spawn | запускати hero path без явного mount contract |
| `CreateAgentAccepted` | native handler прийняв payload без exception | вважати rider materialized автоматично |
| `RiderMaterialized` | rider agent реально існує локально | вважати mount materialized автоматично |
| `MountMaterialized` | mount agent реально існує локально | вважати link готовим без верифікації |
| `MountLinked` | rider має валідний `MountAgent` | робити `ExactReady`, якщо peer/equipment ще не готові |
| `PeerBound` | `SetAgentPeer` відбувся на живому agent | включати commander-control без exact-ready |
| `EquipmentSynchronized` | spawn equipment synchronized | робити wield до валідного equipment state |
| `ExactReady` | rider/mount/peer/equipment пройшли базовий контракт | маскувати queued refresh під applied |
| `CommanderReady` | identity і exact-ready стабільні | підсаджувати героя у formation semantics раніше |
| `DeathCleaned` | rider+mount state очищені разом | лишати старий pair state на index reuse |

## Невідомі точки, які треба закрити до коду

Ось що ще треба довизначити перед новою реалізацією:

1. Які поля exact hero безпечно подавати прямо в `CreateAgent`, а які треба
   переносити лише в `SetAgentPeer` / `SynchronizeAgentSpawnEquipment`.
2. Який canonical source of truth має бути для `BodyProperties`:
   - runtime exact character
   - `AgentBuildData.BodyProperties(...)`
   - окремий `BodyContract`
3. Як саме поводитись із `ReplaceBotWithPlayer` для exact hero path:
   - це частина primary contract
   - чи тільки TDM/Troop-controller layer поверх already-correct agent
4. Який мінімальний exact-ready набір потрібен до `CommanderReady`.
5. Який degraded state вважати допустимим тимчасово під час розробки, але не
   фінальною архітектурою.

## Наступна послідовність роботи

1. Не додавати нових runtime-shim patch без contract-level причини.
2. Закрити `Package D` і `Package E` у документах.
3. Описати `ExactTransferSpawnContract` як набір конкретних C#-структур.
4. Лише після цього почати нову hero-first реалізацію.

## Поточний статус

Напрямок уже визначено:

- обраний шлях: adapter до native multiplayer contract
- відкинутий шлях: подальше розширення surrogate runtime shim
- стан імплементації: зупинена до закриття analysis gate

До завершення цього gate не треба додавати нову speculative runtime-repair логіку.
