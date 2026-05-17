## Обсяг аудиту

Цей аудит покриває exact-transfer pipeline для campaign hero-entry, які в бойовій
сцені стають multiplayer-контрольованими агентами. Основний фокус:
проблемний шлях `remote mounted hero` на клієнті.

Це не аудит усього моду. Це вузький аудит саме transfer-контракту:

1. стан entry у snapshot
2. резолв multiplayer class
3. server pre-spawn exact equipment injection
4. native mission spawn
5. client network materialization
6. peer binding, mount linkage, exact visual finalize
7. replace-bot, commander control, death, respawn

## Поточна задумана архітектура

### 1. Authoritative truth живе в entry state

`BattleSnapshotRuntimeState` володіє campaign-derived `RosterEntryState`.
Саме цей стан має бути єдиною authoritative gameplay truth для:

- `character id`
- exact equipment
- body / hero identity
- horse / harness
- hero flags і perks

### 2. Hero class resolution мапить campaign identity у native MP class

`Infrastructure/CampaignMultiplayerHeroClassResolver.cs` мапить
campaign-origin character у native MP class, щоб runtime міг пройти через
multiplayer spawn contract.

Важлива поточна деталь:

- already-MP hero id, наприклад `mp_light_cavalry_battania_hero`, тепер
  пробуються як direct candidate першими
- surrogate troop/hero template лишається тільки fallback-кандидатом

### 3. Server pre-spawn injection є цільовою архітектурою для strict exact hero

`Patches/ExactCampaignPreSpawnLoadoutPatch.cs` патчить `Mission.SpawnAgent`.

Для entry, які проходять strict exact personal hero contract, сервер інжектить:

- exact weapons
- ті visual slots, які вважаються safe для pre-spawn
- body properties

Для bulk AI troops native template equipment навмисно лишається без exact
injection.

Тобто архітектура вже зараз розрізняє:

- `strict exact hero`: цільовий server-first path
- `bulk troop`: навмисно degraded native-template path

### 4. Client network path все ще лишається hybrid contract

`Patches/BattleMapSpawnHandoffPatch.cs` хукає:

- `HandleServerEventCreateAgent`
- `HandleServerEventSetAgentPeer`
- `HandleServerEventSynchronizeAgentEquipment`
- `HandleServerEventSetAgentHealth`
- `HandleServerEventSetWieldedItemIndex`
- formation assignment і commander-control handoff

Після цього `Mission/CoopMissionBehaviors.cs` пробує резолвити exact entry id і
застосувати або поставити в чергу client exact visual overlay.

Зараз цей шар робить одразу дві роботи:

1. recovery після native MP spawn mismatch
2. фактичне завершення hero exact materialization на клієнті

Саме тут і лежить головна архітектурна напруга.

### 5. Commander possession і formation control занадто прив’язані до agent identity

`Mission/CoopMissionBehaviors.cs` використовує `ReplaceBotWithPlayer`,
formation ownership normalization, controlled bot counts і delayed
general-control promotion.

Через це, якщо player-facing hero materialized погано, command/control логіка
теж бачить неправильну identity і може трактувати героя як звичайного formation
unit.

Це повністю збігається з симптомом, де хост виглядає як піхотинець і
підсвічується разом із formation.

## Що доводять логи

Останні прогони показують стабільну асиметрію:

1. На server/host стороні mounted pair для host hero зазвичай валідний.
2. Проблемний локальний клієнт бачить `CreateAgent` для rider `222` і mount `223`.
3. Той самий клієнт падає `Exception in handler of CreateAgent`.
4. Після цього клієнт усе одно продовжує отримувати пізніші network message для rider.
5. Часто клієнт доходить до `SetAgentPeer` і exact visual finalize уже з rider data,
   але без живого `MountAgent`.
6. У результаті:
   - remote host з’являється як infantry або частково materialized
   - потім зникає
   - selection / command логіка спостерігає неправильну agent semantics
   - пізніше через lifecycle churn приходить client crash

Головний висновок: основна поломка не в пізньому visual mismatch.
Основна поломка відбувається раніше:

локальний клієнт ненадійно завершує native materialization для remote mounted
commander path.

## Структурні проблеми поточного дизайну

### A. `Finalize` зараз означає дві різні речі

Поточний client finalize path змішує:

- `queued for later`
- `overlay реально applied`

Через це прогрес у логах виглядає кращим, ніж реальний runtime-state.

### B. Стан розмазаний по багатьох часткових кешах

Mounted hero state зараз розкиданий по:

- tracked rider -> mount mapping
- payload rider -> mount mapping
- entry id cache
- pending overlay queue
- applied overlay flags
- materialized army cache

Це породжує занадто багато сценаріїв “майже правильно”, але не насправді правильно.

### C. Post-spawn repair став майже окремою архітектурою

Client overlay, mount visual refresh, manual fallback, live wield refresh і
death guards були корисними для діагностики і часткової стабілізації.

Але разом вони перетворилися на другу архітектуру поверх задуманої
server-first архітектури.

Для тимчасової інструментації це прийнятно.
Для довгострокового ядра моду — ні.

### D. Command/control сидить занадто близько до нестабільного spawn identity

Formation ownership, selected formations, followed agent, controlled agent і
general-control promotion досі дуже близько прив’язані до того ж нестабільного
hero materialization path.

Саме тому поганий hero spawn протікає в order UI, а не ізолюється всередині
spawn/visual підсистеми.

### E. У нас немає явної stage machine з інваріантами

Transfer path поводиться як state machine, але код не моделює його явно.
Через це багато фіксів додавались як локальні guard-и, а не як stage-checked
transition.

## Самокритика поточної стратегії

### Що було правильним

Діагностична і стабілізаційна робота не була даремною.

Вона дала:

- відтворювані логи
- вузькі failing agent id
- доказ, що сервер і клієнт падають не в одній точці
- доказ, що корінь проблеми саме в remote mounted hero path
- доказ, що багато post-death crash — це downstream, а не primary issue

Без цього великий архітектурний refactor теж був би сліпим.

### Що було неправильним

Стратегія занадто довго лишалася в режимі symptom-repair.

Я занадто довго пробував покращувати:

- mount visual repair
- live wield refresh
- death guards
- stale cache cleanup
- selection/control guards

замість того, щоб раніше зробити жорсткий архітектурний pivot.

На ранніх ітераціях це було виправдано, але після кількох прогонів, де
повторювався один і той самий core pattern
`CreateAgent -> remote mounted host hero -> missing mount`,
pivot треба було робити швидше.

### Найбільша помилка

Я переоцінив, яка частина проблеми ще живе в post-spawn visual recovery.

Зараз логи показують протилежне:

- до моменту запуску overlay-коду клієнт уже міг втратити native mount
  materialization contract
- repair layer не здатен надійно відновити native object lifecycle, який не
  завершився коректно

### Друга помилка

Я надто довго терпів двозначність між `queued`, `deferred` і `applied`.

Через це частина ітерацій виглядала як прогрес, хоча на локальному клієнті
кінцевий результат лишався тим самим.

## Критика діагнозу користувача

Користувач у головному правий.

Справедливо сказати, що велика частина прогонів не так просувала фічу до
завершення, як навчала нас тому, як система реально поводиться.

Так само справедливо сказати, що це виявило неповну mental model transfer
контракту.

Користувач лише трохи помиляється, якщо трактувати це як
“усі попередні прогони були помилкою”.
Це занадто сильне формулювання.

Попередня робота була потрібна, щоб локалізувати межу контракту.
Реальна проблема в іншому: після того, як ця межа стала очевидною,
стратегія не змінилася достатньо швидко.

## Рекомендований безпечний шлях далі

Не продовжувати ще одну довгу серію локальних guard-фіксів.

Безпечніший шлях тепер такий:

1. формалізувати явну stage model для exact hero transfer
2. визначити інваріанти для кожного stage
3. перебудувати remote mounted hero client path навколо цих інваріантів
4. лишити поточну repair-логіку лише як діагностику і crash-guards

### Потрібна stage model

Для strict exact hero pipeline має моделюватися так:

1. entry resolved
2. class resolved
3. pre-spawn exact loadout injected on server
4. native rider materialized on client
5. native mount materialized on client
6. rider <-> mount link verified
7. peer bound
8. exact visual finalize allowed
9. commander control allowed
10. death cleanup complete

Жоден пізніший stage не має вважатися completed, якщо один із ранніх
обов’язкових stage ще відсутній.
