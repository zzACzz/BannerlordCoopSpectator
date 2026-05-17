# Аудит архітектури мережевої доставки даних бою

Дата: 2026-05-03

## 1. Мета аудиту

Цей документ фіксує, як саме зараз у моді доставляються дані бою від сервера до клієнта, які гарантії та обмеження дає Bannerlord на нижчому рівні, чому виникає нескінченне `Loading Battle Data`, і чи придатна поточна схема для навантаження від `2` до `120` гравців.

Ключове питання:

- чи достатньо поточного транспорту й останнього фіксу;
- якщо ні, то які є технічно здорові варіанти переробки.

## 2. Що реально робить Bannerlord на нижньому рівні

### 2.1. Канали доставки

У decompiled коді Bannerlord є два різні серверні шляхи для модульних повідомлень:

- `GameNetwork.BeginModuleEventAsServer(peer)` -> `MBAPI.IMBPeer.BeginModuleEvent(peer.Index, isReliable: true)`
- `GameNetwork.BeginModuleEventAsServerUnreliable(peer)` -> `MBAPI.IMBPeer.BeginModuleEvent(peer.Index, isReliable: false)`

Тобто нижній контракт такий:

- `reliable` канал існує;
- `unreliable` канал існує;
- конкретна надійність визначається не самим класом повідомлення, а тим, через який `Begin.../End...` шлях воно відправлене.

Джерело:

- [GameNetwork.cs](/C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/GameNetwork.cs:917)

### 2.2. Що це означає для нашого моду

Наші battle-data повідомлення зараз ідуть через:

- `GameNetwork.BeginModuleEventAsServer(peer)`
- `GameNetwork.WriteMessage(new CoopBattlePayloadChunkMessage(...))`
- `GameNetwork.EndModuleEventAsServer()`

Отже весь current path для snapshot/status працює по `reliable` каналу.

Джерело:

- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:528)

### 2.3. Важливий наслідок

`Reliable` тут означає, що transport орієнтований на гарантовану доставку, але це не скасовує практичних вузьких місць:

- head-of-line blocking;
- черги на peer;
- накопичення великих пакетів у reliable потоці;
- дорогі повторні відправки великих payload;
- зависання всього higher-level state, якщо великий reliable transfer не завершується.

Тобто сам факт `reliable=true` не означає, що великий snapshot гарантовано “комфортно” проходить під навантаженням.

## 3. Поточна архітектура моду

### 3.1. Є два різні payload типи

Мод зараз шле від сервера до клієнта два окремі типи даних:

- `EntryStatusSnapshot`
- `BattleSnapshot`

Джерело:

- [CoopBattleSelectionNetworkMessages.cs](/C:/dev/projects/BannerlordCoopSpectator3/Network/Messages/CoopBattleSelectionNetworkMessages.cs:15)

### 3.2. Малий payload: EntryStatusSnapshot

Це невеликий стан для UI:

- `BattleDataReady`
- `AssignedSide`
- `SelectedEntryId`
- `AttackerSelectableEntryCount`
- `DefenderSelectableEntryCount`
- серіалізовані списки allowed/selectable entry ids

Цей payload:

- дедуплікується по `comparisonJson`;
- шлеться тільки якщо реально змінився;
- займає мало байтів;
- не є головним transport-ризиком.

Джерело:

- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:285)

### 3.3. Великий payload: BattleSnapshot

Це повний великий snapshot бою:

- battle id / map / scene;
- всі side;
- всі party;
- всі troop/hero entry;
- perks;
- attributes;
- equipment;
- horse / harness;
- body properties;
- counts і metadata.

Структура дуже широка і майже “повний стан ростеру”.

Джерело:

- [BattleStartMessage.cs](/C:/dev/projects/BannerlordCoopSpectator3/Network/Messages/BattleStartMessage.cs:30)

### 3.4. Серіалізація current snapshot

Сервер робить:

1. `JsonConvert.SerializeObject(snapshot)`
2. `Encoding.UTF8.GetBytes(...)`
3. `GZipStream` якщо стиснення вигідне
4. розбиття на `chunk[]`

Джерело:

- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:802)

### 3.5. Поточне розбиття на шматки

Зараз один chunk:

- `MaxChunkBytes = 1024`

Максимальна кількість chunk-ів:

- `MaxChunkCount = 8191`

Джерело:

- [CoopBattleSelectionNetworkMessages.cs](/C:/dev/projects/BannerlordCoopSpectator3/Network/Messages/CoopBattleSelectionNetworkMessages.cs:84)

### 3.6. Темп відправки

Відправка йде в `OnUdpNetworkHandlerTick()` на сервері:

- `TrySyncBattleSnapshotPayloads()`
- `TrySyncEntryStatusPayloads()`

Для budget per tick зараз:

- `EntryStatusSnapshot`: `2` шматки за тик
- `BattleSnapshot`: `8` шматків за тик

Джерело:

- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:154)
- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:543)

### 3.7. ACK-модель

Важливо: клієнт не підтверджує snapshot по chunk-ах.

Він робить ACK тільки після:

- отримання всіх chunk-ів;
- складання payload;
- JSON decode;
- застосування `BattleSnapshotRuntimeState.SetCurrent(...)`.

Лише тоді клієнт шле:

- `BattleSnapshotReadyAck`

Джерело:

- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:733)
- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:740)

## 4. Чому виникає нескінченне Loading Battle Data

### 4.1. Гейтинг UI

Сервер вважає battle data готовими для конкретного peer тільки якщо peer підтвердив поточний `BattleSnapshot`.

Якщо ACK нема:

- `BattleDataReady = false`
- причина = `Loading battle data...`
- сторони в UI лишаються недоступними

Джерело:

- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:20894)
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:20989)

### 4.2. Що реально сталося в проблемному прогоні

У проблемному логу клієнт:

- повністю отримав `EntryStatusSnapshot`
- але отримав тільки `BattleSnapshot Chunk=0..35/374`
- не склав повний snapshot
- не відправив `BattleSnapshotReadyAck`

Джерело:

- [rgl_log_46800.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_46800.txt:4655>)
- [rgl_log_46800.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_46800.txt:5078>)

Сервер при цьому:

- поставив snapshot у чергу для `AC`
- вважає, що завершив відправку всіх `374` chunk-ів
- але так і не отримав ACK

Джерело:

- [rgl_log_9444.txt](</C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_9444.txt:25338>)
- [rgl_log_9444.txt](</C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_9444.txt:25570>)

### 4.3. Практичний висновок

Нинішня логіка робить одну велику архітектурну ставку:

- `UI side selection залежить від повної доставки великого snapshot`

Якщо один великий snapshot не дійшов до кінця:

- користувач не може навіть вибрати сторону;
- уся prebattle-фаза візуально виглядає як “з’єднання зависло”;
- хоча дрібні control/status дані насправді вже працюють.

## 5. Чому поточна схема слабка для 2–120 гравців

### 5.1. O(players * snapshot)

Повний snapshot зараз шлеться окремо кожному peer як окремий reliable unicast transfer.

Це означає складність:

- `O(N * SnapshotBytes)`

На прикладі з логів:

- `LogicalBytes ≈ 1,766,569`
- `WireBytes ≈ 95,512`

Для `120` гравців один повний resend/initial send такого snapshot:

- приблизно `95 KB * 120 ≈ 11.2 MB` корисного payload;
- плюс заголовки;
- плюс надійна доставка;
- плюс паралельно інші mission-повідомлення.

### 5.2. O(players * chunkCount) у reliable повідомленнях

Старий варіант:

- `374` chunk-и на peer
- `374 * 120 = 44,880` модульних повідомлень на повний fan-out

Поточний варіант:

- приблизно `94` chunk-и на peer
- `94 * 120 = 11,280` модульних повідомлень на повний fan-out

Поточний фікс поліпшує це приблизно в `4` рази, але не змінює саму форму проблеми.

### 5.3. Head-of-line blocking

Оскільки snapshot іде по reliable каналу як довга серія шматків:

- один stalled/lost/delayed елемент потоку блокує higher-level завершення всього snapshot;
- клієнт не може ACK-нути частково отриману роботу;
- сервер не знає, який саме chunk відсутній;
- він може лише переслати весь snapshot ще раз.

### 5.4. Whole-snapshot ACK — занадто грубо

Поточний ACK каже тільки:

- “все отримав”
- або “нічого не підтверджено”

Між цими двома станами немає:

- per-range ACK;
- NACK;
- resume;
- missing range request;
- partial readiness.

### 5.5. Немає справжнього backpressure-контролю

У коді є:

- per-tick budget;
- dedupe;
- retry whole snapshot через таймаут.

Але немає:

- глобального ліміту одночасних heavy sync peer-ів;
- адаптивного throttling per peer;
- пріоритетного відокремлення control-plane і snapshot-plane;
- відкладеного “ступінчастого” sync.

### 5.6. UI залежить від найважчого пакета

Це одна з найслабших архітектурних точок.

`EntryStatusSnapshot` вже містить:

- side counts;
- selectable ids;
- readiness metadata.

Але selection UI все одно відмовляється переходити далі, поки не буде `BattleDataReady=true`.

Джерело:

- [CoopSelectionUiHelpers.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopSelectionUiHelpers.cs:97)

Тобто ми тримаємо весь вхід у бій залежним від найважчої доставки в системі.

## 6. Чи відповідає поточний фікс вимогам

### 6.1. Що саме робить поточний фікс

Поточний фікс:

- збільшує `chunk` з `256` до `1024` байт;
- піднімає budget для `BattleSnapshot` до `8` шматків за тик;
- лишає retry цілого snapshot через таймаут.

Джерело:

- [CoopBattleSelectionNetworkMessages.cs](/C:/dev/projects/BannerlordCoopSpectator3/Network/Messages/CoopBattleSelectionNetworkMessages.cs:84)
- [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:114)

### 6.2. Чесна оцінка

Це:

- нормальний тактичний фікс;
- корисний для зменшення кількості повідомлень;
- логічний як short-term hardening.

Але це **не** повна архітектурна відповідь для вимоги:

- `2..120 гравців`
- стабільне отримання battle data
- без блокування входу в бій через один важкий snapshot

### 6.3. Підсумок

Поточний transport + фікс:

- **може** покращити поведінку на малих серверах;
- **не можна вважати достатнім** для стабільного production-рівня на `120` гравців;
- не усуває головний ризик: whole-snapshot reliable gate.

## 7. Варіанти правильного вирішення

## Варіант 1. Підсилити поточний snapshot transport

Що робити:

- ввести manifest для snapshot;
- перейти з `whole ACK` на `window/range ACK`;
- retry тільки відсутніх range;
- додати timeout cleanup client assembly state;
- ліміт одночасних heavy transfer peer-ів;
- adaptive throttle per peer;
- окрема telemetry-діагностика: `firstChunk`, `lastContiguousChunk`, `ackedRanges`, `resendRanges`.

Плюси:

- мінімальні зміни у моделі даних;
- можна еволюційно дотиснути поточний код;
- менший ризик швидкого регресу в логіці UI.

Мінуси:

- вся архітектура все ще тримається на великому snapshot;
- O(N * snapshot) лишається;
- навіть ідеальний range ACK не змінює того, що UI чекає heavy payload.

Мій вердикт:

- хороший проміжний hardening;
- не найкраща фінальна відповідь для `120` гравців.

## Варіант 2. Розділити transport на рівні даних

Суть:

- `Control plane`:
  - very small reliable messages
  - readiness
  - side counts
  - assigned side
  - minimal selection metadata
- `Roster header plane`:
  - entry ids
  - display names
  - basic type tags
  - mounted/ranged/hero flags
- `Detail plane`:
  - повні perks/body/equipment/horse/body properties
  - only on demand
  - тільки для вибраної сторони / вибраного entry / близьких до spawn даних
- `Delta plane`:
  - після initial load шлються лише версійні зміни

Що це дає:

- вибір сторони не залежить від повного snapshot;
- гравець може зайти в UI й взаємодіяти раніше;
- найважчі дані шлються тільки там, де реально потрібні;
- різко зменшується вартість fan-out на 120 peer-ів.

Плюси:

- найздоровіша архітектура;
- найкраще масштабується;
- прямо розв’язує root problem;
- збігається з real-world вимогою “стабільно працювати для 2–120”.

Мінуси:

- це вже серйозний refactor;
- доведеться переписати частину battle data sync contract;
- потрібна чітка state machine для readiness.

Мій вердикт:

- **це рекомендований основний шлях**.

## Варіант 3. Залишити великий snapshot, але перейти на компактний двійковий формат

Суть:

- замість JSON + string-heavy payload
- зробити binary compact schema:
  - таблиці object id / culture id / hero id / item id
  - ints/enums/bit flags
  - packed amounts
  - окремі словники

Плюси:

- сильно зменшує байти;
- менше chunk-ів;
- менший CPU на JSON parse.

Мінуси:

- не вирішує whole-snapshot gate;
- не прибирає head-of-line;
- не знімає O(N * snapshot) природу системи.

Мій вердикт:

- корисно тільки як доповнення до варіанту 2;
- недостатньо як самостійне рішення.

## 8. Рекомендація

Для вимоги:

- `2..120 гравців`
- стабільне отримання battle data
- відсутність зависання входу в бій

я рекомендую таку стратегію:

### Крок A. Не вважати поточний транспорт фінальним

Поточний `376b814` — це тактичний hardening, а не фінальна архітектура.

### Крок B. Винести side selection із залежності від повного snapshot

Треба зробити так, щоб side selection відкривався по:

- `EntryStatusSnapshot`
- або окремому `RosterHeaderSnapshot`

а не по повному `BattleSnapshot`.

### Крок C. Розбити великий snapshot на рівні даних

Найкращий шлях:

- `summary / roster header / detail / delta`

а не `весь бойовий світ одразу`.

### Крок D. Лише після цього вирішувати, чи потрібен binary format

Оптимізація формату важлива, але вона має йти після зміни самої моделі доставки, а не замість неї.

## 9. Фінальний висновок

Теза користувача “поточний фікс зроблений поверхнево” — **частково правильна**.

Коректна формулювання:

- фікс не був випадковим;
- він бив у реальну transport-проблему;
- але він **не покриває повну архітектуру масштабованої доставки battle data**.

Отже:

- **поточне з’єднання й транспорт не відповідають довгостроковій вимозі стабільної роботи для 120 гравців**;
- **поточний фікс — корисний тактичний крок, але не фінальне рішення**;
- **правильний довгостроковий шлях — розділити data planes і перестати гейтити UI повним heavy snapshot**.
