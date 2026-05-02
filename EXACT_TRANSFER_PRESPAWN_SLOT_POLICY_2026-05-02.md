# Policy pre-spawn слотів для Exact Transfer

Дата: 2026-05-02

## Мета

Зафіксувати policy для кожного equipment slot у strict hero path:

- чи слот має бути готовий до `CreateAgent`
- чи слот дозволено синхронізувати лише пізніше
- які слоти є критичними саме для mounted hero
- на яких слотах поточний runtime уже показав відомі crash/desync ризики

Цей документ є практичним продовженням:

- [EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md)
- [EXACT_TRANSFER_SPAWN_CONTRACT_MODEL_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_SPAWN_CONTRACT_MODEL_2026-05-02.md)
- [EXACT_TRANSFER_NATIVE_EVENT_FIELD_MATRIX_2026-05-02.md](/C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_NATIVE_EVENT_FIELD_MATRIX_2026-05-02.md)

## Основний принцип

Для strict exact hero slot policy не можна будувати від того,
"що потім ще можна підправити".

Її треба будувати від того, що native MP реально споживає до та під час
`CreateAgent`.

Отже policy така:

- усе, що впливає на legal materialization rider/mount, має бути валідне до
  `CreateAgent`
- пізній sync дозволений тільки для того, що не визначає сам факт успішного
  native spawn

## Slot matrix

| Slot | Джерело | Обов'язковий до `CreateAgent` | Дозволений лише пізній sync | Рівень ризику | Policy |
|---|---|---:|---:|---|---|
| `Item0` | `CombatItem0Id` | Так | Ні | Високий | Primary main-hand slot; для strict hero має бути валідний на pre-spawn |
| `Item1` | `CombatItem1Id` | Так, якщо slot не порожній | Ні | Високий | Часто off-hand/shield; впливає на initial wield semantics |
| `Item2` | `CombatItem2Id` | Так, якщо slot не порожній | Ні як primary strategy | Дуже високий | Поточний runtime уже показав `Weapon2` replication risk; adapter має нормалізувати layout до старту |
| `Item3` | `CombatItem3Id` | Так, якщо slot не порожній | Ні як primary strategy | Високий | Часто ammo/додатковий item; для ranged loadout має бути узгоджений із `Item0..2` |
| `Head` | `CombatHeadId` | Так | Теоретично так, але не для strict hero core | Середній | Частина legal body/visual shell героя |
| `Body` | `CombatBodyId` | Так | Теоретично так, але не для strict hero core | Високий | Ключова частина visual identity героя |
| `Leg` | `CombatLegId` | Так | Теоретично так, але не для strict hero core | Середній | Повинен входити в pre-spawn armor contract |
| `Gloves` | `CombatGlovesId` | Так | Теоретично так, але не для strict hero core | Середній | Частина основного exact armor contract |
| `Cape` | `CombatCapeId` | Умовно | Так | Середній/високий | Cloth slot; окрема safety-policy, не можна автоматично прирівнювати до core armor |
| `Horse` | `CombatHorseId` | Так для mounted hero | Ні | Критичний | Без валідного horse strict mounted hero не проходить в adapter path |
| `HorseHarness` | `CombatHorseHarnessId` | Так для mounted hero | Ні як primary strategy | Критичний | Частина mounted materialization contract, не просто cosmetic |

## Детальні правила по слотах

### `Item0`

`Item0` — це слот, який найчастіше стає `main hand`.

Правило:

- для strict exact hero він має бути готовий до `CreateAgent`
- adapter не має права сподіватися, що main-hand потім "підтягнеться" через
  delayed wield refresh

### `Item1`

Це зазвичай shield або secondary hand item.

Правило:

- якщо snapshot містить `Item1`, слот має бути валідним уже на pre-spawn
- якщо snapshot не містить `Item1`, template item треба очистити, а не лишати
  native template

### `Item2`

Це найнебезпечніший слот із практичної точки зору.

Що ми вже знаємо:

- поточний runtime неодноразово показував `Weapon2` risk
- mounted ranged layout у sequential order легко входить у conflict із
  `SetWieldedItemIndex`

Policy:

- `Item2` не можна лишати як "потім подивимось"
- якщо exact layout дає live wield candidate у `Item2`, adapter має або:
  - перерозкласти loadout легальним способом до `CreateAgent`
  - або відхилити contract як неготовий до strict hero path

Тобто новий adapter не повинен повторювати стару тактику:
"запхали як є, а потім hope + repair".

### `Item3`

Часто містить ammo або додатковий weapon.

Policy:

- якщо `Item3` частина exact ranged/mounted loadout, він має бути узгоджений
  ще до `CreateAgent`
- для ranged layout не можна аналізувати `Item3` ізольовано; треба дивитися на
  весь `Item0..3` комплект

### `Head`, `Body`, `Leg`, `Gloves`

Це core armor shell героя.

Policy:

- для strict hero path вони входять у pre-spawn contract
- пізній visual refresh для них може існувати лише як recovery або verification,
  але не як primary materialization strategy

Причина:

- якщо rider уже materialize-ився не з тим armor shell, ми знову ризикуємо
  змішати “legal agent existence” і “пізній cosmetic repair”

### `Cape`

`Cape` — окремий випадок.

Що вже показав поточний код:

- у нас уже є окремий `EvaluateExactRuntimeCapeVisualContract(...)`
- cloth visual slot давно поводиться нестабільніше за базовий armor shell

Policy:

- `Cape` не входить безумовно в стартовий strict core
- для `Cape` має лишитися окрема safety-policy
- якщо `Cape` не проходить safety gate, contract не повинен брехати, що герой
  вже повністю `1:1`

Практично це означає:

- `Cape` можна тимчасово лишити поза Phase A core
- але це має бути явна задокументована деградація, а не мовчазна втрата exactness

### `Horse`

Для mounted hero це не optional visual slot.

Policy:

- `Horse` — критичний pre-spawn slot
- якщо `entryState.IsMounted == true`, але `Horse` не валідний, contract
  провалений ще до `CreateAgent`

### `HorseHarness`

`HorseHarness` теж не можна трактувати як дрібну cosmetic деталь.

Policy:

- для strict mounted hero він є частиною mounted materialization contract
- якщо ми не можемо дати legal `HorseHarness` до `CreateAgent`, не можна
  вважати mounted hero strict-ready

## Додаткові позаслотові поля, які теж pre-spawn-critical

Хоч це не `EquipmentIndex` slots, але вони належать до того ж contract:

- `ClothingColor1`
- `ClothingColor2`
- `TeamIndex`
- `FormationIndex`
- `Position`
- `Direction`

Для strict hero path вони мають бути валідні до `CreateAgent`.

## Правила очищення template-slot

Якщо snapshot не містить item для слота, adapter повинен:

- або явно очистити slot
- або явно довести, чому native template там legal

Не можна лишати template item просто тому, що exact item відсутній.

Інакше виникає фальшивий hybrid state:

- campaign truth каже "слот порожній"
- native runtime лишає template item
- пізніше exact layer намагається пояснити це як "не страшно"

Саме такого стану треба уникнути.

## Validation rules перед `CreateAgent`

Новий adapter path не має права заходити в `CreateAgent`, якщо виконується хоча б
одна умова:

1. mounted hero без валідного `Horse`
2. mounted hero без валідного `HorseHarness`, якщо snapshot його вимагає
3. є live weapon candidate у `Item2`, але layout не нормалізований
4. armor shell героя неповний там, де snapshot вимагає exact item
5. template slot лишається заповненим при порожньому snapshot slot без явної policy

## Що ще треба добити перед кодом

Останній analysis-крок по слотах:

1. Оформити конкретну `weapon layout normalization policy` для mounted ranged hero
2. Вирішити, чи `Cape` входить у Phase A strict core чи лишається окремим
   exact-after-core етапом

## Практичний висновок

Для нового `1:1` adapter path слоти треба ділити не на
"важливі/неважливі", а на:

- `spawn-defining`
- `sync-only`
- `unsafe-until-explicit-policy`

На поточному етапі:

- `Item0..3`, `Head`, `Body`, `Leg`, `Gloves`, `Horse`, `HorseHarness`
  належать до `spawn-defining`
- `Cape` поки що належить до `unsafe-until-explicit-policy`

Це і є безпечна база для наступної hero-first реалізації.
