# Технічний план переробки транспорту даних бою

Дата: 2026-05-03

## 1. Мета

Побудувати стабільний транспорт доставки `BattleSnapshot` від сервера до клієнтів для навантаження від `2` до `120` гравців без зміни поточного правила UI:

- клієнт не може вибрати сторону, поки не завантажив усі дані бою;
- якщо дані бою ще не дійшли повністю, UI і далі лишається в стані `Loading Battle Data`;
- ми лікуємо не UI, а сам транспорт.

## 2. Що саме не влаштовує зараз

Поточна схема має 4 архітектурні проблеми:

1. `BattleSnapshot` підтверджується тільки один раз наприкінці.
Сервер не знає, яких саме шматків не вистачає клієнту.

2. Повторна відправка занадто груба.
Якщо клієнт не підтвердив snapshot, сервер фактично знову шле весь великий payload.

3. Немає повноцінного керування навантаженням на peer.
Є тільки грубий ліміт “скільки chunk-ів за тик”, але немає поняття вікна, підтверджених діапазонів і дозапиту лише відсутніх шматків.

4. Немає чіткої моделі передачі великих даних.
Зараз це просто `JSON/GZip -> chunk-и -> reliable-канал -> повний ACK`, а не окремий transport protocol.

## 3. Вимоги до нового транспорту

Новий транспорт має виконувати такі вимоги:

1. Сервер має знати, які саме chunk-и клієнт уже отримав.
2. Сервер має пересилати тільки відсутні chunk-и, а не весь snapshot повторно.
3. Один повільний або проблемний клієнт не повинен непропорційно душити heavy-sync інших клієнтів.
4. Клієнт не повинен вічно висіти в “напівзібраному” стані без timeout/restart policy.
5. Поточний `EntryStatusSnapshot` не треба ламати або змішувати з heavy transport.
6. Транспорт має працювати спочатку з поточним `JSON + GZip`, а вже потім дозволяти перейти на двійковий формат.

## 4. Що не змінюємо

На цьому етапі свідомо не змінюємо:

- UI gating по `BattleDataReady`;
- логіку side selection;
- авторитетність battle snapshot;
- transport для дрібних `EntryStatusSnapshot`;
- exact-transfer path героїв і військ.

Тобто новий план ізольовано б’є тільки по доставці великого `BattleSnapshot`.

## 5. Нова архітектура транспорту

### 5.1. Розділення на 2 transport-рівні

Після переробки буде 2 різні шари:

1. `Light transport`
Для `EntryStatusSnapshot` та інших малих контрольних повідомлень.

2. `Heavy snapshot transport`
Тільки для великого `BattleSnapshot`.

Це важливо, щоб не лікувати маленькі статусні пакети логікою, потрібною тільки для важкого snapshot.

### 5.2. Нові повідомлення

Потрібно ввести окремі мережеві повідомлення для важкого snapshot-транспорту.

#### 5.2.1. `BattleSnapshotManifestMessage`

Сервер -> клієнт.

Містить:

- `TransmissionId`
- `SnapshotVersion`
- `SchemaVersion`
- `PayloadEncoding`
- `CompressionKind`
- `LogicalBytes`
- `WireBytes`
- `ChunkSize`
- `ChunkCount`
- `PayloadHash`

Призначення:

- повідомити клієнту, який саме snapshot зараз буде доставлятись;
- дозволити клієнту виділити збірочний буфер;
- дозволити серверу і клієнту працювати з однаковою версією передачі.

#### 5.2.2. `BattleSnapshotChunkMessageV2`

Сервер -> клієнт.

Містить:

- `TransmissionId`
- `ChunkIndex`
- `ChunkCount`
- `PayloadBytes`

Це новий heavy-chunk шлях.
Старий `CoopBattlePayloadChunkMessage` лишається для `EntryStatusSnapshot` і як тимчасовий сумісний шлях, поки V2 не стане повністю стабільним.

#### 5.2.3. `BattleSnapshotRangeAckMessage`

Клієнт -> сервер.

Містить:

- `TransmissionId`
- `HighestContiguousChunkIndex`
- `ReceivedRanges`
- `MissingRanges`
- `ClientAssemblyState`

Призначення:

- не чекати повного завершення snapshot, щоб сервер зрозумів прогрес;
- дозволити серверу дозапитувати і пересилати тільки дірки.

#### 5.2.4. `BattleSnapshotCompleteAckMessage`

Клієнт -> сервер.

Містить:

- `TransmissionId`
- `PayloadHash`
- `AppliedSuccessfully`

Це фінальний ACK після:

- отримання всіх chunk-ів;
- складання payload;
- розпакування;
- десеріалізації;
- `BattleSnapshotRuntimeState.SetCurrent(...)`.

Саме цей ACK і далі рухатиме `BattleDataReady=True`.

#### 5.2.5. `BattleSnapshotAbortMessage`

Сервер -> клієнт або клієнт -> сервер.

Потрібен для випадків:

- snapshot застарів;
- сервер уже має новішу версію;
- клієнт зібрав payload не тієї версії;
- timeout assembly;
- hash mismatch.

## 6. Станові машини

### 6.1. Серверний стан на peer

Для кожного peer потрібен окремий heavy-sync state:

1. `Idle`
Нічого не передається.

2. `ManifestSent`
Клієнт уже знає про snapshot, але heavy-chunk потік ще не почався або лише стартував.

3. `Streaming`
Сервер шле chunk-и по вікну.

4. `WaitingRangeAck`
Сервер чекає наступний прогрес від клієнта.

5. `ResendingMissingRanges`
Сервер пересилає тільки діри.

6. `Completed`
Клієнт повністю підтвердив і застосував snapshot.

7. `TimedOut`
Потрібен restart або abort.

8. `Superseded`
На сервері вже є новіша версія snapshot, стару більше не доганяємо.

### 6.2. Клієнтський стан зборки

На клієнті потрібен окремий assembly state:

1. `WaitingManifest`
Ще немає опису snapshot.

2. `Receiving`
Chunk-и приходять, клієнт накопичує карту отриманих шматків.

3. `Assembling`
Усі chunk-и отримано, збираємо єдиний payload.

4. `Decoding`
Розпаковка і десеріалізація.

5. `Applied`
Snapshot застосовано в runtime state.

6. `Failed`
Hash mismatch / decode error / timeout.

## 7. Вікна, ACK і resend policy

### 7.1. Чому потрібне вікно

Не можна просто “лити всі chunk-и до кінця”.
Для 120 гравців це створює вибух черг у reliable-каналі.

Тому потрібне поняття `вікна передачі`.

### 7.2. Початкове вікно

Рекомендований старт:

- `ChunkSize = 1024`
- `InitialWindowChunks = 16`
- `MaxInflightChunksPerPeer = 32`

Сервер:

- шле manifest;
- шле перше вікно `0..15`;
- чекає range-ACK або timeout;
- потім просуває вікно або дозаливає missing ranges.

### 7.3. ACK-модель

Клієнт не повинен чекати повного snapshot, щоб сказати серверу “я щось уже отримав”.

Клієнт має періодично відправляти:

- найвищий безперервний chunk;
- діапазони отриманих chunk-ів;
- діапазони пропусків у вже відкритому вікні.

Приклад:

- у вікні `0..31`
- клієнт отримав `0..11`, `13..20`, `22..24`
- тоді ACK каже:
  - `HighestContiguous = 11`
  - `MissingRanges = [12], [21], [25..31]`

### 7.4. Resend policy

Сервер пересилає лише відсутні діапазони.

Правила:

- не пересилати весь snapshot без крайньої потреби;
- не пересилати chunk, який уже підтверджений клієнтом;
- якщо peer довго не рухається, робити або restart поточної передачі, або abort із новим manifest.

## 8. Керування навантаженням

### 8.1. Per-peer backpressure

Потрібно обмежити:

- скільки inflight heavy chunk-ів може мати один peer;
- скільки peer-ів одночасно можуть бути в активному heavy-streaming.

Рекомендована схема:

- `MaxConcurrentHeavyPeers = 4` або `8`
- `MaxInflightChunksPerPeer = 32`
- `MaxHeavyBytesPerTickGlobal = конфігурований бюджет`

### 8.2. Пріоритети

Порядок пріоритету:

1. нові peer-и, які ще не мають manifest;
2. peer-и, які вже отримали частину snapshot і потребують невеликого resend;
3. peer-и, яким треба довгий heavy-stream;
4. peer-и зі старою або застарілою версією snapshot.

Так сервер швидше “розморожує” тих, кому бракує небагато, і не залипає тільки на одному повільному клієнті.

## 9. Timeout і cleanup policy

### 9.1. Клієнт

Клієнтський assembly state повинен мати:

- `IdleTimeout`
- `DecodeTimeout`
- `AssemblyVersionGuard`

Якщо chunk-и не приходять занадто довго:

- клієнт шле `Abort` або `RangeAck` з позначкою `Stalled`;
- локальний незавершений буфер очищається;
- клієнт чекає новий manifest або restart.

### 9.2. Сервер

Серверний peer transport state має:

- `ManifestSentUtc`
- `LastProgressUtc`
- `LastRangeAckUtc`
- `RetryCount`

Якщо peer довго не рухається:

- спочатку resend missing ranges;
- потім restart передачі;
- потім abort і новий цикл;
- лише в крайньому випадку повний resend snapshot.

## 10. Формат payload

### 10.1. Що робимо спочатку

Перший етап нового транспорту повинен лишити payload таким самим:

- `JSON`
- `UTF8`
- `GZip`

Чому:

- менше змін за раз;
- можна окремо перевірити сам transport;
- легше довести, що проблема була саме в доставці, а не в форматі даних.

### 10.2. Що робимо потім

Після стабілізації транспорту другий етап:

- заміна JSON payload на двійкову схему.

Переваги двійкового формату:

- менше байтів;
- менше алокацій;
- дешевше парсити;
- нижче навантаження на CPU при великих snapshot-ах.

Ризики:

- треба чітка схема версій;
- треба жорсткі validator-и;
- важче дебажити “очима”, ніж JSON.

Висновок:

- двійковий формат безпечний як другий етап;
- але він не повинен бути першим етапом, поки transport shell ще нестабільний.

## 11. Конкретний план впровадження

### Етап A. Спостережуваність і інваріанти

Мета:

- зафіксувати чіткі лічильники і стани для нового транспорту;
- не змінювати gameplay.

Що додаємо:

- server log по peer state;
- client log по assembly state;
- counters:
  - `ManifestSent`
  - `ChunksSent`
  - `ChunksResent`
  - `RangesAcked`
  - `MissingRangesRequested`
  - `Completed`
  - `TimedOut`
  - `Aborted`

Куди:

- [Mission/CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs)

### Етап B. Нові мережеві повідомлення

Мета:

- ввести V2 heavy transport без видалення старого шляху.

Що додаємо:

- `BattleSnapshotManifestMessage`
- `BattleSnapshotChunkMessageV2`
- `BattleSnapshotRangeAckMessage`
- `BattleSnapshotCompleteAckMessage`
- `BattleSnapshotAbortMessage`

Куди:

- [Network/Messages/CoopBattleSelectionNetworkMessages.cs](/C:/dev/projects/BannerlordCoopSpectator3/Network/Messages/CoopBattleSelectionNetworkMessages.cs)

### Етап C. Серверний scheduler heavy transport

Мета:

- винести `BattleSnapshot` із старого “плоского” потоку в окремий scheduler.

Що робимо:

- окремий `HeavySnapshotTransmissionState` на peer;
- manifest lifecycle;
- вікна;
- resend missing ranges;
- timeout/restart policy;
- per-peer/global budgets.

Куди:

- [Mission/CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs)
- можливо новий файл:
  - `Infrastructure/BattleSnapshotTransportState.cs`

### Етап D. Клієнтська збірка і range-ACK

Мета:

- клієнт збирає snapshot по новій моделі, але UI ще поводиться так само.

Що робимо:

- assembly map по `TransmissionId`;
- bitmap/range map отриманих chunk-ів;
- періодичний `RangeAck`;
- фінальний `CompleteAck` після apply.

Куди:

- [Mission/CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs)

### Етап E. Перемкнення BattleSnapshot на V2 transport

Мета:

- старий heavy-path більше не primary path.

Що робимо:

- `BattleSnapshot` іде тільки через V2 transport;
- `EntryStatusSnapshot` лишається на старому малому шляху;
- UI gating лишається на `CompleteAck`.

### Етап F. Навантажувальні прогони

Мета:

- довести, що transport стабільний.

Сценарії:

1. `2` peer
2. `10` peer
3. `30` peer
4. `60` peer
5. `120` peer

Що міряємо:

- час до `BattleDataReady`
- кількість resend
- кількість timeout/restart
- пікову кількість inflight chunk-ів
- загальний heavy payload на peer
- чи є stuck peer без прогресу

### Етап G. Двійковий payload

Мета:

- зменшити розмір snapshot і навантаження на CPU/GC.

Що робимо:

- новий codec для `BattleSnapshot`;
- manifest уже містить `SchemaVersion` і `PayloadEncoding`, тому transport shell не треба переписувати вдруге.

## 12. Як це впливає на поточний фікс `376b814`

Поточний фікс:

- збільшує `ChunkBytes` до `1024`;
- підвищує heavy budget per tick до `8`.

Оцінка:

- це корисний тактичний крок;
- він може покращити поведінку на малих серверах;
- але він не відповідає цілі “стабільно для 2–120 гравців”, бо не змінює саму ACK/resend модель.

Отже:

- як тимчасове полегшення він нормальний;
- як фінальна архітектура — недостатній.

## 13. Варіанти вирішення

### Варіант 1. Лише підкрутити старий chunk transport

Що входить:

- ще більший `ChunkBytes`
- ще інший `chunks-per-tick`
- повільніший resend

Оцінка:

- швидко;
- дешево;
- не вирішує кореневу проблему.

Висновок:

- тільки як тимчасова страховка.

### Варіант 2. Новий heavy transport поверх поточних модульних повідомлень

Що входить:

- manifest
- range ACK
- resend missing ranges
- backpressure
- timeouts
- scheduler

Оцінка:

- правильний архітектурний шлях;
- не вимагає ламати UI;
- підходить як база і для JSON, і для майбутнього binary payload.

Висновок:

- рекомендований основний варіант.

### Варіант 3. Відразу робити новий heavy transport + binary payload

Що входить:

- усе з варіанта 2;
- одразу нова двійкова схема snapshot.

Оцінка:

- найкращий фінальний стан;
- занадто багато змін за раз;
- важче локалізувати регресії.

Висновок:

- не рекомендовано як перший крок.

## 14. Підсумкове рішення

Рекомендований шлях:

1. Не чіпати UI gating.
2. Ввести новий `heavy snapshot transport`.
3. Лишити payload у `JSON + GZip` на першому етапі.
4. Довести стабільність на `2–120` peer-ах.
5. Лише після цього переводити `BattleSnapshot` на двійковий формат.

Це найздоровіший шлях, бо:

- він лікує реальну мережеву проблему, а не симптом;
- не змішує transport refactor із payload refactor;
- дає контрольований rollout без нової серії випадкових крашів.

## 15. Критерій готовності

Можна вважати задачу транспортного етапу закритою, коли:

1. Клієнт більше не зависає в `Loading Battle Data` без прогресу.
2. Сервер знає, які саме chunk-и не дійшли, і не resend-ить весь snapshot навмання.
3. Один повільний peer не душить heavy-sync інших peer-ів.
4. `BattleDataReady` стає `true` тільки після повного apply snapshot, але доставка до цього стану стабільна.
5. Система проходить контрольні прогони на `2 / 10 / 30 / 60 / 120` peer-ах без транспортного deadlock.
