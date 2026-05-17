# Policy для peer, body і banner у Exact Transfer

Дата: 2026-05-02

## Мета

Зафіксувати, як новий strict hero adapter path повинен поводитися з:

- `Peer`
- `IsPlayerAgent`
- `BodyProperties`
- `Banner`

на ранніх native MP стадіях.

Цей документ існує окремо, бо саме тут лежить одна з найнебезпечніших
архітектурних пасток поточного ланцюжка:

`CreateAgent` споживає peer/body/banner раніше, ніж багато інших підсистем
взагалі встигають увімкнутися.

## Критичне спостереження з decompiled native path

У `MissionNetworkComponent.HandleServerEventCreateAgent(...)`:

- якщо `IsPlayerAgent == true`, тіло береться з `missionPeer.Peer.BodyProperties`
- якщо `IsPlayerAgent == false`, тіло генерується через
  `BodyProperties.GetRandomBodyProperties(...)` з `character min/max + seed`
- якщо є `formation`, banner береться з `formation.BannerCode`
- якщо `formation == null`, але `missionPeer != null`, banner береться з
  `missionPeer.Peer.BannerCode`

При цьому on-wire `CreateAgent.BodyPropertiesValue`:

- передається у message
- але в самому `HandleServerEventCreateAgent(...)` не використовується як
  пряме authoritative тіло для materialization

Це один із найважливіших висновків усього analysis-first етапу.

## Що це означає practically

Старе інтуїтивне припущення було хибним:

- "ми передали exact body в message, значить native його використає"

Насправді strict hero adapter повинен жити з іншою реальністю:

- exact body не materialize-иться напряму з `CreateAgent.BodyPropertiesValue`
- exact banner теж може бути взятий не з contract, а з peer fallback

Отже `Peer` і `IsPlayerAgent` — це не другорядні службові прапори.
Це частина самого spawn contract.

## Policy по `Peer`

### Загальне правило

`Peer` не можна передавати в `CreateAgent` просто тому, що він існує.

До `CreateAgent` він legal лише тоді, коли ми точно знаємо:

1. які поля native має право з нього читати
2. що ці поля вже узгоджені з exact contract

Якщо цього знання немає, `Peer` на ранній стадії — це не допомога, а ризик.

### Policy для strict remote hero

Для strict remote hero безпечний policy має бути peer-minimal:

- `CreateAgent` не повинен залежати від `Peer.BodyProperties`
- `CreateAgent` не повинен залежати від `Peer.BannerCode`
- `SetAgentPeer` має залишитися точкою фактичної peer binding

Інакше ми знову повторимо стару проблему:

- native materialization уже зайде в peer-driven branch
- а exact contract ще не встигне стати єдиною truth

### Policy для local controlled hero

Для local hero ситуація складніша, бо native MP очікує player semantics.

Але навіть тут правило таке саме:

- якщо `CreateAgent` використовує peer-driven body/banner path, adapter повинен
  довести, що peer fields уже дорівнюють exact contract

Інакше "локально працює" буде лише випадковістю, а не contract-level гарантією.

## Policy по `IsPlayerAgent`

### Що означає цей прапор насправді

`IsPlayerAgent` не просто "це гравець".

На практиці він перемикає body-materialization branch у native `CreateAgent`.

Тобто він визначає:

- чи тіло береться з peer
- чи тіло генерується з `character + seed`

### Безпечне правило

Новий adapter path не має права ставити `IsPlayerAgent`, доки не визначено:

- чи exact body походить із peer
- чи peer body вже синхронізоване з exact body
- чи banner fallback із peer не зламає exact identity

### Наслідок для strict hero path

Для strict hero path треба формально вибрати один із двох legal режимів:

1. `Peer-driven create-time identity`
   Тоді peer body і peer banner повинні бути authoritative ще до `CreateAgent`.

2. `Contract-driven create-time identity`
   Тоді `CreateAgent` не повинен заходити в peer-driven branch як джерело тіла
   або banner.

Змішувати ці режими в одному path не можна.

## Policy по `BodyProperties`

### Що ми знаємо точно

1. `BodyPropertiesValue` у message не гарантує exact materialization тіла.
2. `IsPlayerAgent=true` веде в peer body branch.
3. `IsPlayerAgent=false` веде в random-from-range branch.

### Жорсткий висновок

На поточному знанні raw native `CreateAgent` сам по собі не дає нам простого
шляху "передати exact body з кампанії як є".

Це означає, що до коду треба закрити окреме питання:

`Який саме legal механізм зробить strict hero body exact на клієнті?`

Поки відповіді на це питання нема, новий strict adapter не можна вважати
повністю спроєктованим.

### Тимчасова архітектурна позиція

До закриття body-question правильний policy такий:

- не вдавати, що `CreateAgent.BodyPropertiesValue` уже вирішує проблему
- не вважати hero `ExactReady`, поки body path не верифікований

## Policy по `Banner`

### Що робить native

Banner обирається так:

1. якщо є `formation`, banner іде з `formation.BannerCode`
2. якщо formation нема, але є `missionPeer`, banner іде з `Peer.BannerCode`

### Безпечний висновок

Для strict hero path banner не повинен випадково залежати від peer fallback.

Безпечний policy:

- strict hero повинен materialize-итися з валідною formation/team banner policy
- peer banner fallback не можна вважати основним exact source для remote hero

Інакше одна й та сама exact entry може отримувати різний banner залежно від того,
на якому етапі та в якому runtime-context materialize-ився agent.

## Policy по `SetAgentPeer`

`SetAgentPeer` повинен залишитися тим, чим він є в native contract:

- binding step
- не body-materialization step
- не banner-materialization step
- не recovery for failed `CreateAgent`

Тому новий adapter path має забороняти таку логіку:

- `CreateAgent` ще не дав legal hero shell
- але ми все одно сподіваємось, що `SetAgentPeer` це виправить

Це заборонений шлях.

## Формальний policy table

| Поле/механізм | Чи можна використовувати до `CreateAgent` | Умови legal використання | Якщо умова не виконана |
|---|---:|---|---|
| `Peer` як handle | Умовно | Лише якщо native не прочитає з нього неузгоджені поля | Не давати в early path |
| `Peer.BodyProperties` | Умовно | Лише якщо exact body уже синхронізоване з peer | Не заходити в `IsPlayerAgent=true` branch |
| `Peer.BannerCode` | Умовно | Лише якщо peer banner already-equals exact banner | Не покладатися на peer banner fallback |
| `IsPlayerAgent=true` | Умовно | Тільки з формально закритим body/banner policy | Інакше illegal для strict hero path |
| `BodyPropertiesValue` у message | Ні як достатня умова | Недостатньо саме по собі | Не вважати exact body вирішеним |
| `SetAgentPeer` | Так, але тільки після legal spawn shell | Rider уже materialized | Не використовувати як substitute для `CreateAgent` success |

## Що це означає для нового hero-first adapter

Перед новим кодом треба закрити три питання:

1. Чи можемо ми законно привести peer body до exact body до `CreateAgent`?
2. Якщо ні, то яким explicit path exact body верифікується після materialization?
3. Чи можемо ми гарантувати formation-based banner для strict hero, щоб не
   залежати від peer banner fallback?

Поки ці три питання не мають формальної відповіді, нову strict hero
імплементацію не можна вважати повністю готовою до безпечного старту.

## Головний висновок

`Peer`, `IsPlayerAgent`, `BodyProperties` і `Banner` — це не технічні дрібниці.
Це одна з центральних частин exact transfer contract.

Саме тут лежить одна з причин, чому старий шлях так довго ламався:

- ми трактували peer/body/banner як службовий супровід до spawn
- а native `CreateAgent` трактує їх як частину самого materialization contract

Новий adapter path має починатися з повної ясності саме в цьому місці.
