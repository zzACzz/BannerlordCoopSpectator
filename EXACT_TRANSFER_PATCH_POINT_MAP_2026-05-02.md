# Карта patch-point для нового Exact Transfer adapter

Дата: 2026-05-02

## Мета

Зафіксувати, у які саме runtime/native точки повинен заходити новий strict
hero adapter path, і які з поточних гачків:

- лишаються корисними
- потребують переробки
- не повинні більше бути primary logic

Це документ між analysis і майбутнім кодом.

## Принцип

Новий adapter path повинен мати мінімальну, але достатню кількість patch-point:

1. `server pre-spawn contract assembly`
2. `client create-time contract materialization`
3. `client post-create verification`
4. `commander-control enablement gate`
5. `death/cleanup gate`

Будь-який гачок поза цими зонами не повинен стати новою primary архітектурою.

## Поточні вже наявні точки

### 1. `Mission.SpawnAgent` prefix на сервері

Файл:
- [ExactCampaignPreSpawnLoadoutPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/ExactCampaignPreSpawnLoadoutPatch.cs)

Що вже робить:
- підхоплює `ExactCampaignSnapshotAgentOrigin`
- будує snapshot equipment
- інжектить `Equipment`
- інжектить `BodyProperties`
- обмежує pre-spawn weapon injection лише strict exact hero path

Статус:
- це корисний базовий patch-point
- його не треба викидати
- але треба перевести з "часткова логіка + діагностика" у повний
  `ExactTransferSpawnContract` builder/validator

### 2. `HandleServerEventCreateAgent` на клієнті

Файл:
- [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Що вже є:
- prefix
- postfix
- finalizer

Що зараз робить:
- runtime diagnostics
- payload tracking
- guard/fallback/recovery
- strict transfer stage logging

Статус:
- це центральний client-side patch-point для нового adapter path
- саме тут має жити нова create-time contract materialization policy
- але нинішній код треба суттєво спростити й перебудувати навколо explicit
  stage machine, а не навколо patch-by-symptom

### 3. `HandleServerEventSetAgentPeer`

Файл:
- [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Статус:
- лишається важливим patch-point
- але тільки як `PeerBound` stage transition
- не повинен більше бути місцем, де ми “доробляємо” зламаний `CreateAgent`

### 4. `HandleServerEventSynchronizeAgentEquipment`

Файл:
- [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Статус:
- лишається корисним
- але тільки як `EquipmentSynchronized` stage transition
- не як джерело truth про саме існування rider/mount pair

### 5. `HandleServerEventSetWieldedItemIndex`

Файл:
- [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Статус:
- лишається потрібним лише як guard/verification stage
- не повинен бути primary recovery механізмом для layout problems

### 6. `HandleServerEventSetAgentHealth`

Файл:
- [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Статус:
- потрібний для safe death/cleanup gate
- не повинен більше нести логіку "визначимо, чи spawn взагалі був успішний"

## Нові цільові patch-point

### A. `ExactTransferSpawnContract` builder на сервері

Ціль:
- перед `Mission.SpawnAgent(...)` зібрати один формальний contract object

Ймовірне місце:
- розвиток [ExactCampaignPreSpawnLoadoutPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/ExactCampaignPreSpawnLoadoutPatch.cs)
- або окремий helper/service, який він викликає

Відповідальність:
- identity mapping
- equipment slot policy validation
- mount contract validation
- body contract validation
- peer/control policy decision

### B. `CreateAgent` contract interpreter на клієнті

Ціль:
- не просто "побачили message", а інтерпретувати її через той самий contract

Ймовірне місце:
- prefix/postfix/finalizer у [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Відповідальність:
- зафіксувати `CreateAgentPayloadObserved`
- застосувати exact create-time policy для body/peer/banner
- відмітити `RiderMaterialized` / `MountMaterialized`
- при провалі позначити `Failed`, а не запускати півархітектури з repair

### C. `Mission.OnComputeTroopBodyProperties` candidate hook

Поточний статус:
- ще не використовується
- потенційно може стати legal engine-level hook для exact body

Відповідальність, якщо буде обраний:
- повернути exact body на create-time path

Статус рішення:
- це ще не вибраний patch-point
- це кандидат, який треба технічно перевірити перед кодом

### D. Commander-control activation gate

Поточні місця:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)
- [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

Новий статус:
- це не spawn patch-point
- це `CommanderReady` gate

Правило:
- увімкнення command/control має стати окремим late stage
- не можна більше дозволяти йому активуватись у напівзламаному hero state

### E. Death/cleanup gate

Поточні місця:
- `OnAgentRemoved`
- `SetAgentHealth`
- різні cleanup helper у [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)

Новий статус:
- ці місця треба звести до одного `CleanupComplete` stage
- cleanup має йти від runtime state pair, а не від окремих кешів і агент-індексів

## Які поточні гачки не повинні більше бути primary logic

### 1. Late visual overlay finalize

Він може лишитися як:
- verification
- recovery
- temporary guard

Але не як primary exact materialization path.

### 2. Manual mount visual repair

Те саме:
- корисно як діагностика
- неприйнятно як фундамент нового `1:1` path

### 3. Delayed wield refresh

Може лишитися як auxiliary fix,
але не як виправдання для unsafe create-time weapon layout.

## Recommended implementation map

### Етап 1. Server contract assembly

Patch-point:
- `Mission.SpawnAgent` prefix

Новий код:
- `BuildExactTransferSpawnContract(entryState, origin, team, peerPolicy, spawnPolicy)`
- `ValidateExactTransferSpawnContract(contract)`

### Етап 2. Client create-time adapter

Patch-point:
- `HandleServerEventCreateAgent`

Новий код:
- `ObserveCreateAgentPayload(contract, message)`
- `ApplyCreateTimeBodyPolicy(...)`
- `ApplyCreateTimePeerPolicy(...)`
- `MarkStageOrFail(...)`

### Етап 3. Client bind/sync transitions

Patch-point:
- `HandleServerEventSetAgentPeer`
- `HandleServerEventSynchronizeAgentEquipment`
- `HandleServerEventSetWieldedItemIndex`

Новий код:
- тільки transition logic
- без secondary truth

### Етап 4. Control gate

Patch-point:
- existing commander-control helper path

Новий код:
- `if (!state.Stage >= ExactReady) block`
- `if mounted && !MountLinkVerified block`

### Етап 5. Cleanup gate

Patch-point:
- `SetAgentHealth`
- `OnAgentRemoved`
- mounted pair cleanup helper

Новий код:
- pair-based state cleanup
- reject index reuse without identity verification

## Найімовірніший body patch-point

На поточному знанні найімовірніше правильний body patch-point один із двох:

1. `HandleServerEventCreateAgent` adapter-level override
2. `Mission.OnComputeTroopBodyProperties`

Поточний пріоритет:
- спочатку перевірити, чи adapter-level override простіший і чистіший
- якщо ні, окремо оцінити `OnComputeTroopBodyProperties`

## Висновок

Карта patch-point уже достатньо зріла, щоб переходити від "аналізуємо все
підряд" до "проектуємо новий pipeline по конкретних методах".

Після цього документа наступний логічний крок уже не загальний аудит,
а `implementation blueprint`:

- які нові класи/структури додаємо
- у яких методах читаємо contract
- у яких методах лише переводимо state machine вперед
