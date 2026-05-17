# Варіанти exact body materialization для strict hero

Дата: 2026-05-02

## Мета

Окремо розкласти, якими legal шляхами strict hero може отримати exact body
на клієнті, з урахуванням того, що native `CreateAgent` не використовує
`BodyPropertiesValue` так прямо, як ми довго припускали.

Це одна з останніх критичних analysis-точок перед новим hero-first кодом.

## Що ми вже знаємо точно

### 1. `CreateAgent.BodyPropertiesValue` не є достатнім сам по собі

У native `HandleServerEventCreateAgent(...)`:

- при `IsPlayerAgent=true` тіло береться з `missionPeer.Peer.BodyProperties`
- при `IsPlayerAgent=false` тіло генерується через
  `BodyProperties.GetRandomBodyProperties(...)`

Тобто on-wire `BodyPropertiesValue` у message:

- існує
- але не є автоматичним exact create-time body truth

### 2. `Mission.SpawnAgent(...)` уміє працювати з `AgentBuildData.BodyProperties(...)`

У decompiled `Mission.SpawnAgent(...)` видно:

- якщо `agentBuildData.BodyPropertiesOverriden == true`,
  agent отримує `agentBuildData.AgentBodyProperties`
- якщо `BodyPropertiesOverriden == false`,
  тіло береться або з `OnComputeTroopBodyProperties`, або з
  `agentCharacter.GetBodyProperties(...)`

Це дуже важливий сигнал:

exact body у native engine можливий,
але питання не в engine загалом, а в тому, як саме ми збираємо `AgentBuildData`
на client network path.

### 3. `CreateAgentVisuals` теж не дає простого exact body path

`CreateAgentVisuals` поводиться так само:

- `VisualsIndex == 0` -> body з `peer.BodyProperties`
- інакше -> random-from-range

Отже `CreateAgentVisuals` не є простим універсальним рішенням exact body problem.

## Що це означає архітектурно

Для strict hero в нас є не "одне очевидне рішення", а кілька можливих
архітектурних варіантів. Їх треба оцінити явно.

## Варіант A: Peer-driven exact body

### Ідея

Прийняти native branch `IsPlayerAgent=true` і зробити так, щоб:

- `peer.BodyProperties` вже дорівнювали exact campaign body
- `CreateAgent` легально читав тіло саме з peer

### Плюси

- працює всередині вже існуючого native branch
- не вимагає ламати сам `Mission.SpawnAgent(...)`

### Мінуси

- exact body тепер залежить від зовнішнього peer state
- треба гарантувати, що peer body оновлюється до spawn на обох машинах
- remote strict hero path стає залежним від lobby/player-data semantics
- дуже легко знову змішати exact hero truth із multiplayer profile truth

### Висновок

Це можливо теоретично, але архітектурно небезпечно.

Для нашої мети `1:1 transfer` цей шлях поганий як primary strategy, бо
authoritative truth опиняється поза самим transfer contract.

## Варіант B: Contract-driven create-time body override

### Ідея

На client network path зібрати `AgentBuildData` так, щоб до `Mission.SpawnAgent(...)`
в ньому вже був exact body override з contract.

Іншими словами:

- не дозволяти native `HandleServerEventCreateAgent(...)` самостійно вибирати
  peer/random body branch для strict hero
- підмінити цей вибір на explicit contract-driven body

### Плюси

- exact body лишається всередині transfer contract
- не треба залежати від `peer.BodyProperties` як primary truth
- це найчистіше лягає в новий hero-first adapter path

### Мінуси

- вимагає чіткого і раннього patch-point на client `CreateAgent` path
- це вже не просто "налаштування", а явний adapter-level control над body branch

### Висновок

Це зараз виглядає як найкращий архітектурний шлях.

Якщо ми будуємо справді чистий strict hero adapter, то exact body має
materialize-итися саме так: з contract, а не з peer fallback.

## Варіант C: `OnComputeTroopBodyProperties` hook

### Ідея

Використати native `Mission.OnComputeTroopBodyProperties`, щоб повернути exact
body під час `Mission.SpawnAgent(...)`.

### Плюси

- це легальний engine-level hook
- логічно призначений для кастомного body computation

### Мінуси

- він спрацьовує лише якщо `BodyPropertiesOverriden == false`
- треба дуже чітко зрозуміти, на яких client/server path він реально доступний
- ще не доведено, що він безпечно покриє саме проблемний network materialization path

### Висновок

Це сильний кандидат на резервний або допоміжний шлях, але поки що не можна
назвати його основним рішенням без окремої перевірки.

Його треба включити в технічне дослідження перед кодом.

## Варіант D: Post-spawn `Agent.UpdateBodyProperties(...)`

### Ідея

Дати native spawn materialize-итися як вийде, а потім оновити тіло агента
через `Agent.UpdateBodyProperties(...)`.

### Плюси

- технічно простіше
- може спрацювати як repair

### Мінуси

- це вже не create-time exact body
- знову повертає нас у late-repair архітектуру
- може залишати короткі або довгі windows з неправильним тілом
- не розв'язує корінь проблеми strict materialization contract

### Висновок

Як primary strategy — неприйнятно.
Може лишитися тільки як recovery path або validation helper.

## Порівняльна таблиця

| Варіант | Exact body як primary truth | Залежність від peer | Архітектурна чистота | Рекомендація |
|---|---:|---:|---:|---|
| `A. Peer-driven` | Часткова | Висока | Низька | Не рекомендується як primary |
| `B. Contract-driven override` | Так | Низька | Висока | Рекомендований primary path |
| `C. OnComputeTroopBodyProperties` | Потенційно так | Низька | Середня/висока | Дослідити як legal hook |
| `D. Post-spawn UpdateBodyProperties` | Ні | Низька | Низька | Тільки recovery |

## Рекомендований напрямок

На поточному знанні найздоровіша стратегія така:

1. основний design target:
   `Contract-driven create-time body override`
2. окремо перевірити, чи `OnComputeTroopBodyProperties` можна використати як
   legal engine hook для цього самого результату
3. не будувати primary path на `Peer.BodyProperties`
4. не повертатися до post-spawn body repair як core механізму

## Що треба перевірити до коду

Перед новою hero-first імплементацією треба відповісти на два конкретні питання:

1. Який найменший і найчистіший patch-point дає нам contract-driven body override
   на client `CreateAgent` path?
2. Чи можна замість цього використати `Mission.OnComputeTroopBodyProperties`
   без побічних ефектів для multiplayer network materialization?

Поки ці два питання не закриті, exact body лишається останнім великим
архітектурним unknown для strict hero path.

## Практичний висновок

Ми вже знаємо достатньо, щоб сказати твердо:

- exact body для strict hero не можна надалі залишати "якось через peer або random"
- новий `1:1` path повинен мати explicit body strategy

Найбільш імовірно правильною виглядає така ціль:

`ExactTransferSpawnContract.Body` -> explicit create-time body override ->
`Mission.SpawnAgent(...)` -> verified strict hero materialization

Саме це і треба вважати цільовим design target до початку нового коду.
