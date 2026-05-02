# Рішення по body hook для Exact Transfer

Дата: 2026-05-02

## Питання

Яким саме legal технічним шляхом strict hero path повинен забезпечити
`1:1 body materialization` на клієнті:

- без surrogate primary path
- без peer-driven випадковостей
- без post-spawn body repair як основного механізму

## Коротка відповідь

Основне рішення:

`contract-driven create-time body override` через власний strict-hero adapter
на `MissionNetworkComponent.HandleServerEventCreateAgent(...)`

Необхідний наслідок:

- для strict exact hero ми не можемо покладатися на native body branch
  як є
- ми повинні самі зібрати `AgentBuildData` з exact body policy і лише потім
  викликати `Mission.SpawnAgent(...)`

## Чому саме так

### 1. `CreateAgent.BodyPropertiesValue` не вирішує exact body напряму

У native `HandleServerEventCreateAgent(...)`:

- `IsPlayerAgent=true` -> тіло з `missionPeer.Peer.BodyProperties`
- `IsPlayerAgent=false` -> тіло генерується з `character + seed`

Отже on-wire body value сам по собі не є create-time exact truth.

### 2. `OnComputeTroopBodyProperties` не є primary solution для цього path

У `Mission.SpawnAgent(...)`:

- `OnComputeTroopBodyProperties` викликається лише коли
  `BodyPropertiesOverriden == false`
- native `HandleServerEventCreateAgent(...)` сам викликає
  `agentBuildData.BodyProperties(...)`
- `AgentBuildData.BodyProperties(...)` ставить `BodyPropertiesOverriden=true`

Тобто в стандартному network create path цей hook уже практично обходиться.

Висновок:

- `OnComputeTroopBodyProperties` цікавий як engine-level candidate hook
- але не є прямим primary рішенням для поточного strict remote hero path

### 3. Peer-driven body не підходить як authoritative шлях

Якщо ми будуємо exact body через `Peer.BodyProperties`, то authoritative truth
виходить за межі transfer contract.

Недоліки:

- залежність від зовнішнього peer state
- ризик різного тіла при різному runtime context
- погана архітектурна чистота для `1:1 transfer`

Для нашої цілі це неправильний primary шлях.

### 4. Post-spawn `UpdateBodyProperties(...)` — лише recovery

Технічно це можливо, але:

- це не create-time exact materialization
- це повертає нас у late-repair архітектуру
- це не вирішує корінь strict spawn contract

Отже як основне рішення — неприйнятно.

## Остаточний вибір

### Primary path

Для strict exact hero новий adapter повинен:

1. перехопити `HandleServerEventCreateAgent(...)`
2. розпізнати, що payload належить strict exact hero path
3. не дозволити native handler самому вибрати peer/random body branch
4. самостійно зібрати `AgentBuildData`:
   - exact body
   - exact equipment
   - exact mount contract
   - контрольований peer/banner policy
5. викликати `Mission.SpawnAgent(...)` уже з нашим `AgentBuildData`

Тобто body hook decision остаточно такий:

`strict hero body materialization = adapter-level create-time override`

## Роль `OnComputeTroopBodyProperties` після цього рішення

Після цього рішення `OnComputeTroopBodyProperties` не зникає повністю.

Його новий статус:

- не primary path
- а secondary research candidate / optional engine-level optimization

Можливий майбутній сценарій:

- якщо виявиться, що adapter-level override можна чисто спростити через цей
  hook без втрати контролю, тоді його можна підключити пізніше

Але старт нової архітектури не повинен залежати від цього.

## Як це впливає на майбутній код

### Що тепер треба реалізувати

1. `ExactTransferSpawnContract`
2. `ExactTransferRuntimeState`
3. `ExactTransferContractBuilder`
4. `ExactTransferContractValidator`
5. `ExactTransferCreateAgentAdapter`

### Що саме робитиме `ExactTransferCreateAgentAdapter`

Для strict hero path:

- зупинятиме стандартний body-branch native handler
- будуватиме свій `AgentBuildData`
- подаватиме exact `BodyProperties`
- обиратиме контрольований `Peer` / `Banner` policy
- викликатиме `Mission.SpawnAgent(...)`
- фіксуватиме stage transition або explicit failure

## Що це НЕ означає

Це не означає:

- "переписати весь multiplayer runtime"
- "викинути всі наявні патчі"
- "робити власний spawn engine"

Це означає тільки одне:

для strict exact hero ми більше не довіряємо найкритичнішій частині native
`CreateAgent` наосліп, а вводимо свій adapter-level contract control.

## Чому це безпечніше

Бо тепер exact body:

- живе в transfer contract
- materialize-иться на create-time
- не залежить від випадкового `Peer.BodyProperties`
- не вимагає post-spawn латок, щоб стати “майже правильним”

Саме це і є потрібна архітектурна властивість для реального `1:1 transfer`.

## Ризики цього рішення

Ризики є, але вони контрольовані:

1. Треба акуратно обійти стандартний native body branch лише для strict hero path.
2. Треба чітко відділити strict hero payload від усього іншого, щоб не
   зачепити bulk troop path.
3. Треба окремо зберегти legal mount/peer/banner semantics.

Ці ризики кращі за альтернативу, бо вони явні й проектовані,
а не приховані в пізньому runtime churn.

## Підсумок

`Body hook decision` закрито.

Primary архітектурний вибір:

- `не peer-driven body`
- `не OnComputeTroopBodyProperties як основа`
- `не post-spawn body repair`
- `так: strict-hero adapter-level create-time body override у HandleServerEventCreateAgent`

Після цього analysis gate для старту нового exact-transfer ядра практично
достатній.
