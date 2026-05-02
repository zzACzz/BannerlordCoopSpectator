# Матриця native event і полів для Exact Transfer

Дата: 2026-05-02

## Мета

Зафіксувати, які саме поля native multiplayer runtime Bannerlord читає на кожній
критичній стадії spawn/lifecycle і які з цих полів:

- повинні бути валідні вже до `CreateAgent`
- можуть бути валідні лише після `SetAgentPeer`
- можуть бути оновлені тільки через пізніший sync

Цей документ потрібен, щоб новий `ExactTransferSpawnContract` не будувався
“на око”.

## Джерела

Матриця побудована з:

- [EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md](C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md)
- `.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/CreateAgent.cs`
- `.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/SetAgentPeer.cs`
- `.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/SynchronizeAgentSpawnEquipment.cs`
- `.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/ReplaceBotWithPlayer.cs`
- `.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/MissionNetworkComponent.cs`
- `.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/Mission.cs`

## Ключовий принцип

Native MP path не працює як проста таблиця "дані прийшли -> дані застосували".
Він працює як послідовність контрактів:

1. `CreateAgent` створює rider/mount materialization shell
2. `SetAgentPeer` прив'язує peer до вже існуючого agent
3. `SynchronizeAgentSpawnEquipment` оновлює spawn equipment і рефрешить visuals
4. `SetWieldedItemIndex` припускає, що agent і equipment state уже валідні
5. `ReplaceBotWithPlayer` змінює control ownership, але не вирішує ранню
   materialization problem

Тому поле важливе не тільки саме по собі, а й тим, на якій стадії воно вперше
споживається.

## `CreateAgent` on-wire payload

`CreateAgent` несе такі поля:

| Поле | Джерело з кампанії/адаптера | Має бути валідним до `CreateAgent` | Читається native одразу | Коментар |
|---|---|---:|---:|---|
| `Character` | `Identity.NativeMultiplayerCharacterId` | Так | Так | Legal native MP identity; це не campaign truth, а adapter target |
| `Monster` | `Body.MonsterId` | Так | Так | Впливає на legal body/materialization path |
| `AgentIndex` | network/server assignment | Так | Так | Runtime identity, не можна використовувати як gameplay truth |
| `MountAgentIndex` | `Mount.ExpectedMountAgentIndex` | Так для mounted path | Так | Це лише очікування mount pair, а не доказ існування mount |
| `Peer` | `PeerBinding` | Залежить від policy | Так | Найнебезпечніше поле для strict remote hero; не можна подавати “тому що є” |
| `MissionEquipment[weapon slots]` | `Equipment.MissionEquipment` | Так | Так | Weapon state входить у materialization contract, не лише у visuals |
| `SpawnEquipment[armor+horse+harness]` | `Equipment.SpawnEquipment` | Так | Так | Для mounted hero horse/harness не можна вважати суто late-sync полями |
| `IsPlayerAgent` | `PeerBinding` + `SpawnPolicy` | Так | Так | Змінює body-reading branch усередині native handler |
| `BodyPropertiesSeed` | `Body.BodyPropertiesSeed` | Так, якщо `IsPlayerAgent == false` | Так | Для non-player branch використовується прямо в `CreateAgent` |
| `BodyPropertiesValue` | `Body.BodyProperties` | Так | Так | Навіть якщо пізніше є peer, тіло вже могло бути спожите |
| `IsFemale` | `Body.IsFemale` | Так | Так | Частина legal body contract |
| `TeamIndex` | `Control.Team` | Так | Так | Team/formation semantics формуються одразу |
| `Position` | spawn planner | Так | Так | Частина `Mission.SpawnAgent(...)` |
| `Direction` | spawn planner | Так | Так | Частина `Mission.SpawnAgent(...)` |
| `FormationIndex` | `Control.FormationIndex` | Так | Так | Впливає на ранню formation прив'язку |
| `ClothingColor1/2` | `Equipment` / team policy | Так | Так | Споживається ще у create-time path |

## Найважливіша `CreateAgent` branch-логіка

### `IsPlayerAgent == true`

Тоді native handler має право лізти в peer-driven body semantics раніше за
`SetAgentPeer`.

Практичний висновок:

- для strict remote hero не можна бездумно піднімати `IsPlayerAgent=true`
- спочатку треба формально знати, чи такий branch legal для нашого adapter path

### `IsPlayerAgent == false`

Тоді native handler бере body path із `Character` і `BodyPropertyRange/Seed`.

Практичний висновок:

- якщо ми йдемо в non-player branch, `Character` і `Body` мають бути повністю
  узгоджені ще до `CreateAgent`

### `MountAgentIndex >= 0`

Тоді native handler вважає, що mounted pair уже є частиною materialization
контракту.

Практичний висновок:

- не можна вважати horse/harness простим cosmetic overlay
- якщо mounted path не пройшов `CreateAgent`, пізніший repair не повинен
  вважатись primary success path

## `SetAgentPeer`

`SetAgentPeer` несе:

| Поле | Джерело | Має бути валідним до `SetAgentPeer` | Що робить native |
|---|---|---:|---|
| `AgentIndex` | network/server assignment | Так | Шукає вже існуючого agent |
| `Peer` | `PeerBinding` | Так | Прив'язує peer до вже materialized agent |

Висновок:

- `SetAgentPeer` не створює agent і не рятує зламаний `CreateAgent`
- якщо rider або mount не materialized до цього моменту, `SetAgentPeer` не
  повинен підміняти собою `CreateAgent` success

## `SynchronizeAgentSpawnEquipment`

`SynchronizeAgentSpawnEquipment` несе:

| Поле | Джерело | Має бути валідним до sync | Що робить native |
|---|---|---:|---|
| `AgentIndex` | network/server assignment | Так | Шукає існуючого agent |
| `SpawnEquipment` | `Equipment.SpawnEquipment` | Так | Оновлює spawn equipment і робить visual refresh |

Висновок:

- цей event уже не про "чи можна створити hero"
- це event про "чи можна оновити вже існуючий agent"
- отже він не має права бути primary recovery для повністю зламаного mounted
  materialization

## `SetWieldedItemIndex`

Хоч сам message-клас тут не розписаний окремо, по поведінці native path уже
встановлено:

| Поле/суть | Джерело | Передумова |
|---|---|---|
| `AgentIndex` | network/server assignment | Agent materialized |
| `WieldedSlotIndex` | `InitialWield` | Equipment state узгоджений |

Висновок:

- wield event приходить надто пізно, щоб бути джерелом materialization truth
- він повинен працювати тільки поверх уже валідного rider/equipment state

## `ReplaceBotWithPlayer`

`ReplaceBotWithPlayer` несе:

| Поле | Джерело | Коли legal | Що робить |
|---|---|---:|---|
| `Peer` | `PeerBinding` | Після живого bot/agent | Міняє ownership |
| `BotAgentIndex` | network/server assignment | Після materialized agent | Переприв'язує player до bot agent |
| `Health` | live runtime | Після materialized rider | Оновлює rider health |
| `MountHealth` | live runtime | Після materialized mount | Оновлює mount health |

Висновок:

- `ReplaceBotWithPlayer` не повинен виправдовувати ранній зламаний mounted hero
- якщо він приходить на неповний transfer-state, це вже похідна помилка

## Що саме повинно бути готове до `CreateAgent`

Це поля, які не можна відкладати для strict exact hero:

- legal native MP `Character`
- legal `Monster`
- валідний `BodyProperties` / `BodyPropertyRange` / `Seed`
- узгоджений `IsPlayerAgent` policy
- `MissionEquipment` для weapon slots
- `SpawnEquipment` для armor/body visuals
- `Horse` / `HorseHarness`, якщо `Mount.IsMounted == true`
- `TeamIndex`
- `FormationIndex`
- `Position`
- `Direction`
- `ClothingColor1/2`

Якщо хоча б один із цих блоків невизначений, новий adapter path не має права
взагалі йти в `CreateAgent`.

## Що може бути лише після `SetAgentPeer`

Потенційно відкладені речі:

- peer-specific commander ownership semantics
- order-control enablement
- final personal control state
- певні recovery-only visual sync, якщо базова materialization уже легальна

Але не можна відкладати:

- сам факт mounted materialization
- legal hero identity
- legal body contract

## Що може бути пізнім sync, але не primary truth

- додатковий spawn equipment refresh
- initial wield finalize
- secondary visual consistency refresh

Ці кроки не можуть відповідати за "чи існує взагалі mounted hero як native
object".

## Червоні прапори для майбутньої реалізації

Ознаки, що ми знову дрейфуємо в небезпечний шлях:

1. `CreateAgent` ще не довів materialization, а ми вже вважаємо hero `ExactReady`.
2. `MountAgentIndex` використовується як сурогат `MountMaterialized`.
3. `SetAgentPeer` або `SynchronizeAgentSpawnEquipment` трактуються як
   "можна пізніше доробити те, чого не зробив spawn".
4. `ReplaceBotWithPlayer` використовується, щоб маскувати невдалий strict hero
   materialization.
5. `queued/deferred` знову логічно прирівнюються до `applied`.

## Який analysis output ще треба з цієї матриці

На основі цієї матриці треба добити дві підтаблиці:

1. `PreSpawn slot policy`
   Для кожного equipment slot:
   - чи обов'язковий він до `CreateAgent`
   - чи легальний для пізнішого sync
   - чи небезпечний для mounted hero path

2. `Peer usage policy`
   Для strict hero path:
   - які peer fields legal до `SetAgentPeer`
   - які peer fields заборонені до `SetAgentPeer`
   - коли `IsPlayerAgent` взагалі дозволений на client materialization

## Практичний висновок

Основна помилка попередніх ітерацій була не лише в "поганому полі", а в тому,
що кілька полів, які native читає дуже рано, ми фактично трактували як late
repair concerns.

Новий adapter path має будуватись навпаки:

1. спочатку закрити весь `CreateAgent` contract
2. потім лише `SetAgentPeer`
3. потім лише `equipment sync / wield / control`

Саме це і є безпечний шлях до реального `1:1`, а не до чергової гібридної
схеми.
