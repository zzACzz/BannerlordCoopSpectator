# Policy для mounted ranged weapon layout у Exact Transfer

Дата: 2026-05-02

## Мета

Зафіксувати, як strict hero adapter повинен нормалізувати ranged/mounted
weapon layout до `CreateAgent`, щоб:

- не зламати native initial wield logic
- не повернутися до старого `Weapon2` crash/desync path
- не покладатися на delayed client wield repair як primary strategy

## Чому це окремий документ

Поточний код уже довів, що mounted ranged layout — це не просто ще один набір
слотів. Це окремий ризиковий підконтракт:

- у нас уже є `HasMountedRangedSequentialLayout`
- у нас уже є `HasWeapon2WieldReplicationRisk`
- у логах уже був збіг між live wield candidate у `Weapon2` і проблемами
  `SetWieldedItemIndex`

Тобто тут потрібна не "евристика", а явна policy.

## Вихідні спостереження з поточного коду

У [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs):

- `TryApplyMountedMaterializedWeaponOverrides(...)` уже намагається розкладати
  mounted loadout не просто в snapshot order, а за ролями
- якщо є `ranged + ammo`, частина поточного коду ще допускає sequential order
  `Weapon0..3`
- `BuildExactEntryInitialWieldSummary(...)` використовує
  `equipment.GetInitialWeaponIndicesToEquip(...)`
- `DoesEquipmentContainLiveWeapon2WieldCandidate(...)` уже прямо визнає
  `Weapon2` окремим ризиком

## Головна проблема

Для mounted ranged hero exact snapshot order і legal multiplayer wield order —
це не те саме.

Якщо ми просто копіюємо campaign `Item0..3` в `Weapon0..3`, то виникає ризик:

1. native `CreateAgent` materialize-ить equipment
2. native initial wield вибирає slot, який legal у singleplayer-логіці, але
   нестабільний у MP replication
3. пізніше `SetWieldedItemIndex` або client live state входить у conflict

Отже "точний порядок як у snapshot" не можна вважати абсолютною цінністю,
якщо він руйнує сам legal runtime contract.

## Принцип policy

Для mounted ranged hero adapter повинен зберігати:

- exact item set
- exact semantic roles предметів

але не обов'язково exact raw slot order, якщо raw order ламає legal MP spawn.

Це важливе уточнення:

- `1:1` означає `1:1 identity + function + combat semantics`
- а не `1:1 будь-який небезпечний slot index`, якщо цей index не переживає native MP runtime

## Semantic roles

Для mounted ranged layout нас цікавлять ролі:

- `PrimaryMelee`
- `PrimaryPolearm`
- `PrimaryRanged`
- `Ammo`
- `Shield`
- `SecondaryUtility`

Питання повинно звучати не "що лежало в Item2", а:

- який це тип предмета
- чи він може бути live wield candidate
- чи legal він у цьому slot для mounted MP spawn

## Recommended canonical layout

Для mounted ranged strict hero безпечний canonical layout має бути таким:

| Target slot | Дозволена роль |
|---|---|
| `Weapon0` | `PrimaryPolearm` або `PrimaryMelee` або `PrimaryRanged` |
| `Weapon1` | `Shield` або secondary main item, якщо shield нема |
| `Weapon2` | `Ammo` або non-live secondary utility |
| `Weapon3` | запасний ranged/utility/non-live item |

### Основне правило

`Weapon2` не повинен містити live wield candidate, якщо це можна уникнути
легальною нормалізацією layout.

Тобто:

- `Weapon2 = ammo` — допустимо
- `Weapon2 = passive utility` — допустимо
- `Weapon2 = main live melee/polearm/ranged candidate` — заборонено для
  strict hero path

## Правила нормалізації

### Case 1: `ranged + ammo + shield`

Ціль:

- `Weapon0 = ranged`
- `Weapon1 = shield`
- `Weapon2 = ammo`
- `Weapon3 = запасний utility або melee`

Це найстабільніший layout для MP replication.

### Case 2: `ranged + ammo + melee`

Ціль:

- `Weapon0 = melee` або `ranged`, залежно від expected initial wield policy
- `Weapon1 = ranged`
- `Weapon2 = ammo`
- `Weapon3 = utility`

Тут треба обов'язково перевіряти
`GetInitialWeaponIndicesToEquip(...)` після нормалізації.

### Case 3: `polearm + shield + ranged + ammo`

Це найризикованіший layout, бо предметів більше, ніж простих stable ролей.

Policy:

- strict hero path не повинен бездумно пропускати такий layout
- adapter має або:
  - знайти stable canonical order з безпечним initial wield
  - або визнати contract `not strict-ready`

Тобто небезпечний layout не можна "дотискати потім".

### Case 4: `melee + shield + thrown/ammo`

Thrown item теж треба трактувати як live ranged candidate.

Policy:

- thrown item не можна залишати в `Weapon2`, якщо це веде до live wield risk
- adapter повинен canonicalize такий layout як ranged case, а не як звичайний melee

## Validation rules

Після нормалізації mounted ranged layout adapter повинен перевірити:

1. `GetInitialWeaponIndicesToEquip(...)` не обирає небезпечний main-hand path
2. `Weapon2` не містить live wield candidate, якщо немає explicit exception
3. ammo slot узгоджений з ranged weapon
4. shield не витісняє критичний main item у нестабільний slot

Якщо хоча б одна перевірка не проходить, contract не заходить у strict hero path.

## Explicit exception policy

Єдиний випадок, коли live item у `Weapon2` може бути тимчасово допустимим:

- це документований і окремо протестований native-stable layout
- у нас є явний доказ із коду або стабільних прогонів, що такий layout не
  веде до `SetWieldedItemIndex` проблем

До появи такого доказу:

- `Weapon2 live candidate` вважається червоним прапором

## Що не можна робити

- не можна зберігати raw campaign slot order будь-якою ціною
- не можна виправдовувати небезпечний layout тим, що "потім client refresh
  перевзведе зброю"
- не можна позначати такий hero `strict exact ready`, якщо canonical layout
  не пройшов validation

## Що треба реалізувати в коді

У новому adapter path потрібен окремий крок:

`NormalizeMountedRangedWeaponLayout(ExactTransferEquipmentContract contract)`

Його обов'язки:

1. зібрати роль кожного snapshot weapon item
2. побудувати canonical MP-safe layout
3. запустити `GetInitialWeaponIndicesToEquip(...)`
4. або підтвердити layout
5. або повернути explicit `contract validation failure`

## Висновок

Mounted ranged layout — це не cosmetic detail і не "потім відладимо".
Це одна з ключових legal перевірок до `CreateAgent`.

Новий `1:1` path повинен зберігати exact item set і combat semantics,
але зобов'язаний нормалізувати небезпечний raw slot order до MP-safe canonical
layout до входу в native spawn.
