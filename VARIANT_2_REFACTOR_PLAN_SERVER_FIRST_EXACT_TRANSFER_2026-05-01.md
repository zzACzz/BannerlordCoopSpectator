## Мета

Довести `variant 2` до кінця, замінивши поточний hybrid
symptom-repair підхід на безпечніше server-first ядро exact-transfer для
strict exact hero.

Найближча ціль — не “всі юніти”.
Найближча ціль:

- `main hero`
- `lords`
- інші exact personal hero entry

і насамперед проблемний шлях `remote mounted hero` на клієнті.

## Принцип рефакторингу

Наступна фаза повинна перестати трактувати post-spawn client repair як
основний шлях.

Repair/fallback код може лишитися, але тільки як:

- діагностика
- crash guards
- останній тимчасовий recovery

Основний шлях має стати явною transfer state machine з інваріантами.

## Поточна проблема, яку треба вирішити першою

Найважливіший зламаний шлях зараз такий:

server exact mounted hero валідний ->
клієнт отримує `CreateAgent` для rider із mount index ->
клієнт ламає native materialization до нормального mount linkage ->
пізніше overlay-код трактує rider так, ніби materialization уже успішна ->
remote hero виглядає як infantry або зникає ->
command/control бачить неправильну semantics ->
через певний lifecycle churn клієнт падає

## Цільова архітектура

### Transfer stages

Для strict exact hero runtime повинен явно моделювати такі stages:

1. `EntryResolved`
2. `ClassResolved`
3. `PreSpawnInjected`
4. `ClientCreateAgentObserved`
5. `RiderMaterialized`
6. `MountMaterialized`
7. `MountLinkVerified`
8. `PeerBound`
9. `ExactVisualApplied`
10. `CommanderControlEnabled`
11. `DeathCleanupComplete`

Жоден пізніший stage не має відмічатися completed, якщо ранній обов’язковий
stage ще відсутній.

### Жорсткі інваріанти

Для mounted strict exact hero:

- `ExactVisualApplied` незаконний, поки `MountLinkVerified == false`
- `CommanderControlEnabled` незаконний, поки hero identity не завершена
- remote hero не може входити у formation-selection semantics, поки його
  personal transfer state неповний
- queued refresh ніколи не дорівнює applied state
- death-time cleanup повинна очищати стан rider і mount разом

## Запропоновані work package

### Work package 1: окрема transfer-state model

Створити один явний runtime state object для strict exact hero transfer.

Пропонований вміст:

- `EntryId`
- `AgentIndex`
- `ExpectedMountAgentIndex`
- `ObservedCreateAgent`
- `ObservedSetAgentPeer`
- `ObservedSynchronizeEquipment`
- `RiderMaterialized`
- `MountMaterialized`
- `MountLinkVerified`
- `ExactVisualApplied`
- `CommanderControlEnabled`
- timestamps / retries / failure reason

Цей state має стати source of truth для exact hero transfer progress.

Поточні розкидані cache мають перетворитися лише на implementation detail за
цим state model, а не бути truth самі по собі.

### Work package 2: розділити payload observation і materialization success

Зараз ми часто знаємо mount payload data раніше, ніж знаємо, чи mount взагалі
існує локально як native object.

Цю відмінність треба зробити явною:

- payload observed
- native object materialized

Payload knowledge не повинно означати success.

### Work package 3: зробити `CreateAgent` головним mount-contract checkpoint

Логи вже показують, що справжня поломка сидить на або до client
`HandleServerEventCreateAgent`.

Отже майбутня логіка повинна відштовхуватися від такого правила:

- якщо mounted exact hero дійшов до `CreateAgent`, але локальний mount не
  materialized, стан переходить у `MaterializationFailed`
- пізніші stages не мають маскувати це під success

Саме тут треба вирішувати:

- чи можемо ми recover safely
- чи повинні перейти у контрольований degraded state

### Work package 4: gate exact finalize по stage completion, а не по евристиках

`TryFinalizeClientExactCampaignVisualForAgent(...)` не повинен приймати рішення
з мішанини:

- `SpawnEquipment`
- `MountAgent`
- pending queue state
- локальні спостереження

Натомість він має:

- консультуватися з explicit transfer state
- для mounted strict exact hero відмовляти finalize, поки
  `MountLinkVerified == false`

### Work package 5: відчепити commander-control enablement від нестабільного spawn state

Order UI, formation ownership, general promotion і selection suppression повинні
залежати від transfer-stage readiness.

Для strict exact hero:

- якщо transfer state incomplete, commander-control blocked
- якщо blocked, runtime має мати явний degraded state, а не змішувати troop
  semantics із commander semantics

Це повинно прибрати симптом, де remote host стає “майже troop” і
підсвічується разом із formation.

### Work package 6: cleanup навколо rider+mount як єдиної lifecycle unit

Death, despawn, respawn і agent-index reuse повинні чистити transfer state для
всього mounted pair, а не лише для того індексу, який ми побачили першим.

Сюди входить:

- rider -> mount mapping
- mount -> rider mapping
- entry binding
- applied flags
- pending refresh state
- commander-control state, якщо він прив’язаний до цього hero

### Work package 7: звести діагностику до invariant-based log

Поточна діагностика корисна, але занадто фрагментована.

Наступна ітерація повинна логувати stage transition напряму, наприклад:

- `StrictHeroTransfer Stage=ClientCreateAgentObserved`
- `StrictHeroTransfer Stage=MountMaterialized`
- `StrictHeroTransfer Stage=MountLinkVerified`
- `StrictHeroTransfer Stage=Blocked Reason=CreateAgentExceptionBeforeMountMaterialization`

Це дає набагато кращий сигнал з одного прогона, ніж розкидані low-level логи,
які ще треба вручну зводити.

## Рекомендована послідовність імплементації

### Фаза A: архітектурний каркас

1. додати transfer-state runtime object
2. завести поточний rider/mount payload tracking у нього
3. додати stage transition logging
4. не міняти behavior, окрім того, що потрібно для консистентності

Очікуваний результат:

кожен strict exact hero можна описати одним stage state, а не шматками логів.

### Фаза B: gating для remote mounted hero

1. зробити remote mounted hero finalization залежним від transfer state
2. заборонити `ExactVisualApplied`, поки mount link не verified
3. блокувати commander-control enablement, поки transfer incomplete
4. лишити наявний repair код лише як optional transition attempt

Очікуваний результат:

клієнт перестає “вдавати успіх” для зламаного remote mounted hero spawn.

### Фаза C: контрольована деградація

Якщо remote mounted hero materialization усе ще валиться в native `CreateAgent`,
потрібен один явний degraded state, а не багато implicit half-state.

Наприклад:

- `StrictHeroTransfer State=MaterializationFailed`
- remote commander не пропускається у commander-control path
- visual overlay не позначається applied
- death/health guard використовують цей state напряму

Це безпечніше за частковий удаваний success.

### Фаза D: справжнє server-first завершення

Коли state machine для strict exact hero стабільна:

1. зменшити залежність від client visual recovery
2. підтвердити стабільність mounted hero через кілька death/respawn циклів
3. лише після цього розширювати ту саму модель на safer troop subset

## Чому цей шлях безпечніший

Бо він замінює:

- приховані припущення
- overlapping cache
- post-hoc repair
- двозначність queued-vs-applied

на:

- явну stage progression
- явні blocking condition
- явні degraded state
- одне місце, де взагалі можна міркувати про correctness

## Чого не треба робити далі

Не треба витрачати ще багато прогонів на дрібні локальні фікси типу:

- ще один mount refresh tweak
- ще один wield guard
- ще одна delayed retry
- ще один order UI suppression fix

якщо ця зміна не є прямою частиною state-machine refactor вище.

Цей патерн уже дав diminishing returns.

## Визначення успіху для цієї фази

Ця фаза вважається успішною, коли strict exact hero transfer виконує все:

- remote mounted hero на клієнті або повністю mounted і видимий, або явно
  позначений degraded, але ніколи не напів-materialized
- жоден remote exact hero не доходить до `ExactVisualApplied` без verified
  mount link
- жоден commander-control path не активується на unresolved hero transfer
