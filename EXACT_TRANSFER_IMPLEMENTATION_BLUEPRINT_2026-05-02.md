# Blueprint нової реалізації Exact Transfer

Дата: 2026-05-02

## Мета

Перетворити попередні analysis-документи на практичний каркас майбутньої
реалізації без surrogate primary path.

Це ще не runtime-код. Це специфікація того, які саме нові класи/модулі
мають з'явитися і як вони взаємодіють.

## Базовий принцип

Новий strict hero path не повинен будуватися поверх старого symptom-repair шару.

Він повинен мати окреме ядро:

1. `contract build`
2. `contract validation`
3. `create-time materialization`
4. `stage transitions`
5. `control gate`
6. `cleanup gate`

Все інше — другорядне.

## Нові цільові компоненти

### 1. `ExactTransferSpawnContract`

Файл/модуль:
- новий окремий файл у `Infrastructure` або `Mission`

Відповідальність:
- одна immutable-модель даних для strict hero transfer

Вміст:
- `IdentityContract`
- `BodyContract`
- `EquipmentContract`
- `MountContract`
- `PeerBindingContract`
- `InitialWieldContract`
- `ControlContract`
- `CleanupContract`
- `SpawnPolicyContract`

Примітка:
- ця структура вже описана документально, тепер її треба перенести в код

### 2. `ExactTransferRuntimeState`

Файл/модуль:
- новий окремий runtime-state клас

Відповідальність:
- єдиний source of truth для progress strict hero transfer

Не повинен:
- дублювати campaign truth
- бути черговим фрагментарним кешем

Повинен:
- знати stage
- знати rider/mount indices
- знати failure reason
- знати exact/control readiness

### 3. `ExactTransferContractBuilder`

Файл/модуль:
- новий server-side builder

Відповідальність:
- з `RosterEntryState` і spawn context побудувати повний contract

Вхід:
- `RosterEntryState`
- origin
- team/formation context
- peer context

Вихід:
- `ExactTransferSpawnContract`

### 4. `ExactTransferContractValidator`

Файл/модуль:
- новий validator

Відповідальність:
- перевірити contract до `Mission.SpawnAgent(...)`

Перевірки:
- slot policy
- mounted ranged layout policy
- body policy
- peer/banner policy
- cleanup/control prerequisites

Результат:
- `Valid`
- або explicit validation failure з reason code

### 5. `ExactTransferCreateAgentAdapter`

Файл/модуль:
- новий client-side adapter поверх `HandleServerEventCreateAgent`

Відповідальність:
- інтерпретувати `CreateAgent` через strict contract
- виконати create-time policy
- оновити `ExactTransferRuntimeState`

Не повинен:
- робити late visual repair як primary behavior
- запускати command/control logic

### 6. `ExactTransferStageMachine`

Файл/модуль:
- окремий helper/service

Відповідальність:
- дозволяти або відхиляти stage transition
- централізовано перевіряти інваріанти

Ключова користь:
- прибирає хаотичні локальні `if` по різних патчах

### 7. `ExactTransferCommanderGate`

Файл/модуль:
- окремий helper для commander-control activation

Відповідальність:
- не дозволити order/control path активуватись раніше `ExactReady`
- окремо перевіряти mounted case через `MountLinkVerified`

### 8. `ExactTransferCleanupCoordinator`

Файл/модуль:
- окремий cleanup service

Відповідальність:
- очищати state для whole mounted pair
- блокувати reuse тільки по `AgentIndex` без identity check

## Які поточні місця треба еволюційно використати

### `ExactCampaignPreSpawnLoadoutPatch`

Залишається, але змінює роль:

- було: partial pre-spawn injection + diagnostics
- стане: точка виклику `ContractBuilder` + `ContractValidator`

### `BattleMapSpawnHandoffPatch`

Залишається, але змінює роль:

- було: великий hybrid patch-шар із recovery/fallback логікою
- стане: thin integration layer, який лише делегує в
  `CreateAgentAdapter`, `StageMachine`, `CommanderGate`, `CleanupCoordinator`

### `CoopMissionBehaviors`

Залишається, але змінює роль:

- було: частина transfer truth + частина visual recovery + частина control path
- стане: orchestration layer над новими exact-transfer services

## Що треба винести з поточного коду

З поточного коду треба винести в нові модулі:

1. stage-логіку з `queued/deferred/applied`
2. rider/mount mapping
3. exact entry resolution, якщо він потрібен як частина strict path
4. mounted ranged risk evaluation
5. command/control readiness evaluation

## Що не треба переносити як primary logic

Не треба нести у нове ядро як основну логіку:

- manual mount visual repair
- late wield refresh
- visual-only finalize heuristics
- stale cache cleanup як заміну state machine
- surrogate canonicalization як primary materialization strategy

Це може лишитися тільки як:

- тимчасовий guard
- debugging aid
- recovery-only code

## Рекомендована послідовність кодування

### Крок 1. Структури даних

Додати:

- `ExactTransferSpawnContract`
- `ExactTransferRuntimeState`
- `ExactTransferStage`
- `ExactTransferFailureReason`

Без зміни behavior.

### Крок 2. Builder + Validator

Додати:

- `ExactTransferContractBuilder`
- `ExactTransferContractValidator`

І підв'язати їх до server pre-spawn path.

Поки що:
- лише будують/валідовують
- ще не міняють client runtime flow

### Крок 3. Stage machine

Додати:

- `ExactTransferStageMachine`

І перевести поточні `StrictTransfer={...}` diagnostic transition на новий
runtime state.

### Крок 4. CreateAgent adapter

Додати:

- `ExactTransferCreateAgentAdapter`

Саме тут треба буде реалізувати:

- body policy
- peer policy
- mount materialization expectations

### Крок 5. SetAgentPeer / Equipment / Wield integration

Перевести:

- `HandleServerEventSetAgentPeer`
- `HandleServerEventSynchronizeAgentEquipment`
- `HandleServerEventSetWieldedItemIndex`

на явні stage transition.

### Крок 6. Commander gate

Додати:

- `ExactTransferCommanderGate`

І відокремити commander activation від spawn materialization.

### Крок 7. Cleanup coordinator

Додати:

- `ExactTransferCleanupCoordinator`

І звести death/remove/index-reuse до одного pair-based cleanup contract.

## Який мінімальний milestone вважати першим успіхом

Не треба одразу чекати "всі герої ідеально 1:1".

Перший правильний milestone:

- strict remote mounted hero більше не доходить до `Applied/CommanderReady`
  у broken state
- stage machine чесно показує:
  - або legal materialization
  - або explicit failure reason
- old repair layer більше не маскує materialization failure під success

Це буде означати, що ядро нової архітектури вже реально взяло контроль.

## Що буде другим milestone

Після цього:

- strict hero body path має materialize-итись exact
- mounted ranged layout має проходити validation/canonicalization до spawn
- commander-control має активуватись тільки після `ExactReady`

## Практичний висновок

Analysis-first етап уже дав достатньо, щоб починати не “патчити проблему”,
а будувати окремий кодовий каркас exact-transfer ядра.

Після цього blueprint наступний крок уже майже не дослідницький:

- або ми ще формально закриваємо body hook decision
- або починаємо Крок 1–2 нового adapter path у коді
