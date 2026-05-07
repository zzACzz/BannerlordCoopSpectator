# План повного переходу всієї армії на Exact Transfer `1 в 1`

## 1. Мета

Після стабілізації транспорту даних бою наступний етап — не частковий rollout по класах, а повний перехід усієї армії на одну систему `1 в 1`.

Це означає:

- герої і лорди лишаються на вже побудованому exact path;
- решта війська переводиться на той самий exact contract;
- старий surrogate / vanilla replace-bot шлях перестає бути primary materialization path;
- гравець більше не повинен вселятись у сурогата, на якого потім натягується `1 в 1`.

## 2. На чому базується цей план

План спирається на вже зроблені документи:

- [EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md)
- [EXACT_TRANSFER_SPAWN_CONTRACT_MODEL_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_SPAWN_CONTRACT_MODEL_2026-05-02.md)
- [EXACT_TRANSFER_PATCH_POINT_MAP_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_PATCH_POINT_MAP_2026-05-02.md)
- [EXACT_TRANSFER_IMPLEMENTATION_BLUEPRINT_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_IMPLEMENTATION_BLUEPRINT_2026-05-02.md)
- [EXACT_TRANSFER_PRESPAWN_SLOT_POLICY_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_PRESPAWN_SLOT_POLICY_2026-05-02.md)
- [EXACT_TRANSFER_NATIVE_EVENT_FIELD_MATRIX_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_NATIVE_EVENT_FIELD_MATRIX_2026-05-02.md)
- [EXACT_TRANSFER_PEER_BODY_BANNER_POLICY_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_PEER_BODY_BANNER_POLICY_2026-05-02.md)
- [EXACT_TRANSFER_BODY_HOOK_DECISION_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_BODY_HOOK_DECISION_2026-05-02.md)
- [BATTLE_DATA_NETWORK_ARCHITECTURE_AUDIT_2026-05-03.md](/C:/dev/projects/BannerlordCoopSpectator3/BATTLE_DATA_NETWORK_ARCHITECTURE_AUDIT_2026-05-03.md)
- [BATTLE_DATA_TRANSPORT_TECHNICAL_PLAN_2026-05-03.md](/C:/dev/projects/BannerlordCoopSpectator3/BATTLE_DATA_TRANSPORT_TECHNICAL_PLAN_2026-05-03.md)

Ключовий наслідок цих документів:

- транспорт `BattleSnapshot` уже достатньо стабілізований, щоб перестати бути головним блокером;
- правильний наступний крок — не нові runtime shim-и, а перехід усієї армії на один exact spawn contract.

## 3. Поточний реальний стан системи

Зараз у коді є змішаний стан:

1. Герої і лорди вже значною мірою сидять на exact path.
2. Частина решти війська все ще матеріалізується старим шляхом.
3. Поверх старого шляху місцями вже натягуються exact equipment / visual / identity шари.

Саме ця гібридність і дає конфлікти:

- стартовий AI-агент може народитись одним шляхом;
- peer possession іде іншим шляхом;
- exact overlay доганяє це третім шляхом;
- у результаті клієнт бачить або сурогата, або зламаного агента, або ловить краш на weapon/ammo events.

## 4. Головний архітектурний висновок

Для всієї армії треба зробити не "ще один exact overlay", а один primary path:

1. `Server contract assembly`
2. `Pre-spawn exact materialization`
3. `Client create-time interpretation`
4. `SetAgentPeer / equipment / wield` як stage transitions
5. `Possession` як окремий ownership handoff
6. `Cleanup` як окремий lifecycle gate

Тобто:

- materialization і possession більше не змішуються;
- surrogate path більше не використовується як базовий спосіб народження армії;
- exact contract стає єдиною правдою для heroes, lords і regular troops.

## 5. Що вважаємо "старим шляхом", який треба відключити

Для цього етапу старим шляхом вважається не будь-який native multiplayer API, а конкретно:

- forced preferred troop index перед vanilla spawn;
- materialized army replace-bot як primary spawn path;
- peer-culture surrogate bridge як normal path;
- late exact overlay як компенсація неправильного первинного spawn.

Native події Bannerlord лишаються:

- `CreateAgent`
- `Mission.SpawnAgent`
- `SetAgentPeer`
- `SynchronizeAgentSpawnEquipment`
- `SetWieldedItemIndex`
- `ReplaceBotWithPlayer`

Але вони мають працювати в новому exact contract, а не в старому surrogate-flow.

## 6. Що робимо з `ReplaceBotWithPlayer`

Тут важливе розділення.

`ReplaceBotWithPlayer` не треба автоматично вважати "старою архітектурою". Він може лишитися як вузький native ownership handoff, якщо:

- агент уже народився exact;
- identity, equipment, body і mount уже правильні;
- `ReplaceBotWithPlayer` не використовується для виправлення spawn-даних;
- він лише передає контроль гравцю над already-exact agent.

Тобто ціль не "видалити будь-який ReplaceBotWithPlayer", а:

- прибрати його як materialization path;
- лишити тільки як ownership handoff там, де це справді безпечно.

Якщо в якомусь місці він далі мутує exact state, тоді вже на наступному підетапі його теж доведеться витісняти.

## 7. Цільова архітектура для всієї армії

### 7.1. Server side

Для кожного roster entry сервер будує `ExactTransferSpawnContract`:

- `IdentityContract`
- `BodyContract`
- `EquipmentContract`
- `MountContract`
- `PeerBindingContract`
- `InitialWieldContract`
- `ControlContract`
- `CleanupContract`
- `SpawnPolicyContract`

Далі всі бойові агенти народжуються вже з цього контракту.

### 7.2. Client side

Клієнт більше не "доремонтовує" неправильний агент.

Він:

1. бачить `CreateAgent`;
2. інтерпретує його через exact contract;
3. проходить stage machine;
4. після `SetAgentPeer` і `SynchronizeAgentSpawnEquipment` лише завершує легальні переходи;
5. не намагається перетворити зламаного сурогата в exact-агента постфактум.

### 7.3. Possession

Гравець:

- або входить у вже exact-агента;
- або ownership handoff відбувається над already-exact bot.

Але він не повинен:

- входити в сурогата;
- чекати, поки exact state "догрузиться" поверх нього.

## 8. Новий план впровадження

### Етап 1. Зафіксувати "герої вже працюють" як reference contract

Мета:

- не чіпати стабілізований hero/lord path без потреби;
- використовувати його як reference для решти війська.

Що робимо:

- виписуємо які частини exact contract уже реально працюють для heroes/lords;
- окремо фіксуємо, що з hero path не переносимо як primary logic:
  - temporary guards
  - late visual recovery
  - battle-specific shim-логику

Результат:

- герой стає reference implementation, а не спеціальним винятком.

### Етап 2. Відрізати rollout для non-hero troops від старих surrogate-механізмів

Мета:

- не допустити змішування нового exact path і старого army-possession path.

Що робимо:

- exact troop entries більше не повинні:
  - йти через forced preferred troop bridge;
  - йти через peer-culture surrogate bridge як primary selection path;
  - народжуватись старим materialized replace-bot flow.

Що це означає practically:

- у коді має бути один явний `if exact-supported troop entry -> new exact primary path`;
- старий шлях не видаляємо фізично одразу, але він перестає обслуговувати exact troop entries.

Результат:

- герої/лорди і regular troops починають жити по одному правилу.

### Етап 3. Поширити exact contract builder на весь roster, а не лише на hero/ranged підмножини

Мета:

- builder більше не знає про "частковий rollout";
- він уміє будувати контракт для будь-якого entry.

Що робимо:

- прибираємо логіку на кшталт "first-wave exact ranged";
- builder і validator працюють для:
  - infantry
  - ranged
  - cavalry
  - horse archers
  - regular non-hero troops
  - already working hero/lord entries

Результат:

- система знає не "хто в rollout", а "який exact contract у цього entry".

### Етап 4. Зробити full-army server pre-spawn exact materialization

Мета:

- усі агенти армії мають народжуватись уже exact, а не через vanilla template + overlay.

Що робимо:

- `ExactCampaignPreSpawnLoadoutPatch` перетворюємо з часткової інжекції в повний server-side exact materialization adapter;
- policy по слотах, body, mount і wield беремо з уже затверджених документів;
- startup AI і майбутні possessed troops більше не відрізняються по spawn contract.

Результат:

- усі бійці на полі бою з першої секунди мають правильну identity/equipment/body базу.

### Етап 5. Перебудувати client `CreateAgent` path для whole-army exact

Мета:

- клієнт повинен вміти materialize-ити не лише heroes/lords, а всю exact army без surrogate fallback як primary path.

Що робимо:

- `HandleServerEventCreateAgent` читає exact contract для будь-якого exact entry;
- staged handling поширюється на:
  - infantry
  - ranged
  - cavalry
  - mounted ranged
- snapshot-ready gates залишаються: exact path не стартує на клієнті раніше готового current snapshot generation.

Результат:

- клієнт більше не бачить "правильного хоста у себе, але сурогата на іншій машині".

### Етап 6. Відокремити possession від materialization

Мета:

- смерть і респавн не повинні запускати нову гібридну архітектуру.

Що робимо:

- коли гравець вселяється в regular troop, він входить у вже exact agent;
- якщо потрібен `ReplaceBotWithPlayer`, він дозволений лише як narrow ownership handoff;
- ownership handoff більше не повинен:
  - міняти troop identity;
  - підміняти class bridge;
  - накладати recovery-equipment поверх уже exact spawn.

Результат:

- респавн regular troops перестає бути окремою "другою системою".

### Етап 7. Cleanup і death lifecycle для всієї exact army

Мета:

- друга смерть, респавн, повторне вселення, agent index reuse більше не повинні нести старий state.

Що робимо:

- cleanup переводимо на contract/stage-based model;
- pair-based cleanup для mounted cases;
- controlled troop death не ламає remote client weapon/ammo path.

Результат:

- `death -> respawn -> possession -> death` працює однаково для heroes і regular troops.

## 9. Що не робимо в цьому плані

Не робимо:

- частковий rollout по класах;
- новий тимчасовий surrogate adapter;
- новий overlay-only repair поверх старого spawn;
- ще одну паралельну possession-систему для regular troops.

Причина:

- це знову приведе до гібридного стану, який ми вже бачимо зараз у лучниках.

## 10. Які підсистеми треба змінювати першими

У порядку пріоритету:

1. `ExactTransferContractBuilder`
2. `ExactTransferContractValidator`
3. `ExactCampaignPreSpawnLoadoutPatch`
4. `ExactCampaignNativeArmyBootstrap`
5. `BattleMapSpawnHandoffPatch`
6. `CoopMissionBehaviors`:
   - spawn intent
   - possession
   - cleanup
7. exact runtime state / stage machine

## 11. Що вже можемо вважати достатньо дослідженим

Для цього плану вже достатньо закрито:

- native spawn lifecycle;
- pre-spawn slot policy;
- peer/body/banner policy;
- body hook decision;
- transport shell і binary payload базового battle snapshot;
- patch-point карту;
- exact contract model.

Тобто наступна робота вже не analysis-first, а implementation-first.

## 12. Критерій успіху цього етапу

Етап вважається закритим, коли:

1. Уся армія матеріалізується по exact contract з початку місії.
2. Гравець після смерті входить не в сурогата, а в already-exact troop.
3. Клієнт і хост бачать один одного однаково для:
   - heroes
   - lords
   - infantry
   - ranged
   - cavalry
4. `SetWieldedItemIndex` / ammo / peer events більше не сипляться в неготового або surrogate-агента.
5. Старий surrogate / forced preferred troop / vanilla replace-bot path більше не є primary materialization path для campaign army.

## 13. Рекомендоване практичне рішення

Після transport-етапу більше не дробити rollout по класах.

Правильний наступний крок:

- за один архітектурний етап перевести весь roster materialization на exact contract;
- старий шлях відрізати для exact campaign army;
- heroes/lords лишити як already-working reference;
- rest of army довести до того самого правила.

Коротко:

`не ще одна хвиля часткових фіксів, а повний перехід усієї армії на один exact spawn contract і відчеплення possession від старої surrogate-архітектури`.
