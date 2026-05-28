# Аудит Архітектури Coop Runtime

Дата: `2026-05-28`

## Зміст
- [1. Мета](#1-goal)
- [2. Поточна Форма Runtime](#2-current-runtime-shape)
- [3. Основні Компоненти](#3-main-components)
- [4. Потік Від Початку До Кінця](#4-end-to-end-flow)
- [5. Модель Exact Transfer І Матеріалізації](#5-exact-transfer-and-materialization-model)
- [6. Модель Відкладених Пакетів](#6-deferred-packet-model)
- [7. Поточні Відтворювані Кластери Помилок](#7-current-reproducible-failure-clusters)
- [8. Що Насправді Показує Останній Клієнтський Краш](#8-what-the-latest-client-crash-actually-shows)
- [9. Зчеплення З TDM / Vanilla MP](#9-tdm--vanilla-mp-coupling)
- [10. Інваріанти, Які Ми Повинні Зберігати](#10-invariants-we-need-to-preserve)
- [11. Що Ймовірно Неправильно В Поточному Дизайні](#11-what-is-probably-wrong-in-the-current-design)
- [12. Рекомендовані Наступні Напрямки](#12-recommended-next-directions)
- [13. Пропонований Scope Міграції На Clean Core](#13-suggested-clean-core-migration-scope)
- [14. Опорні Файли](#14-reference-files)

<a id="1-goal"></a>
## 1. Мета
Цей документ є єдиною картою навігації для поточного coop battle runtime.

Він має дати відповідь на такі питання:
- що зараз працює поверх vanilla MP / TDM
- у якій точці стан кампанійної битви заходить у MP runtime
- де саме застосовується exact transfer
- де саме застосовується відкладений network replay
- які інваріанти вже відомо є крихкими
- які збої є окремими, а які є наслідком одного й того самого дизайнерського рішення

Це навмисно аудит runtime/системи, а не нотатка про один конкретний фікс.

<a id="2-current-runtime-shape"></a>
## 2. Поточна Форма Runtime
Мод зараз **не** працює як чисте coop-only mission core.

Поточна схема така:
- vanilla MP / TDM startup місії
- обгорнутий стек mission behaviors
- поверх нього накладений coop network bridge
- поверх нього накладена coop client/server mission logic
- поверх нього накладений exact transfer
- поверх нього накладені battle-map-specific handoff patch-і
- vanilla UI і vanilla network behavior частково приглушуються або вибірково замінюються

Тобто система зараз є **гібридним runtime**:
- частково vanilla MP
- частково coop authoritative snapshot model
- частково exact transfer model
- частково battle-map crash isolation / replay patching

Через це поточна система дуже сильно залежить від **порядку подій**:
- порядку інжекції mission behaviors
- порядку приходу payload-ів
- порядку `CreateAgent` проти follow-up пакетів
- порядку side selection проти battle snapshot bootstrap
- порядку materialization rider/mount link

<a id="3-main-components"></a>
## 3. Основні Компоненти
### Стек mission behaviors
- Інжекція wrapped mission stack відбувається у [MissionStateOpenNewPatches.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/MissionStateOpenNewPatches.cs:235)
- Battle client wrapper зараз додає:
  - `CoopMissionNetworkBridge`
  - `CoopMissionClientLogic`
  - `CoopMissionSelectionView`

### Coop authoritative transport
- Network transport payload-ів живе у [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:180)
- Дві центральні сім'ї payload-ів:
  - `EntryStatusSnapshot`
  - `AuthoritativeMaterializedAgentEntrySnapshot`

### Coop mission runtime logic
- Основна client/server runtime logic живе у [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:32)
- Цей файл зараз володіє дуже великою частиною:
  - exact runtime bootstrap
  - strict hero transfer
  - client materialization tracking
  - selection shell state
  - mounted link repair / exact visual follow-up

### Battle-map interception пакетів і replay
- Важкий handoff і replay patch живе у [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:129)
- Це зараз найкритичніший runtime patch для:
  - client `CreateAgent`
  - deferred packet queues
  - replay після готовності battle snapshot
  - exact diagnostics навколо materialization агентів

### Exact transfer builder-и і runtime item support
- Побудова контрактів: [ExactTransferContractBuilder.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactTransferContractBuilder.cs:12)
- Dedicated pre-spawn resolution: [ExactCreateAgentServerPreSpawnContractResolver.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs:33)
- Runtime item support: [ExactCampaignRuntimeItemRegistry.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignRuntimeItemRegistry.cs:14)

### Приглушення / заміна UI
- Приглушення vanilla intermission: [IntermissionVmCrashGuardPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/IntermissionVmCrashGuardPatch.cs:18)
- Приглушення vanilla team/class UI: [VanillaEntryUiSuppressionPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/VanillaEntryUiSuppressionPatch.cs:17)
- Coop selection shell: [CoopMissionSelectionView.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopMissionSelectionView.cs:16)

<a id="4-end-to-end-flow"></a>
## 4. Потік Від Початку До Кінця
### Сервер
1. Campaign battle конвертується в runtime battle data.
2. Dedicated battle-map mission відкривається через wrapped MP mission startup.
3. Сервер materialize-ить агентів у місії.
4. Сервер періодично надсилає:
   - `EntryStatusSnapshot`
   - `AuthoritativeMaterializedAgentEntrySnapshot`
5. Від клієнта очікується, що першу сім'ю payload-ів він використає для UI state, а другу для authoritative ordinary-agent binding.

### Клієнт
1. Відкривається wrapped MP mission.
2. Vanilla intermission / entry UI частково приглушуються.
3. З'являється coop selection shell.
4. Клієнт отримує battle snapshot і exact runtime catalog.
5. Клієнт отримує network packets, пов'язані з агентами.
6. Ordinary `CreateAgent` може бути відкладений до моменту, коли authoritative materialization data буде готова.
7. Відкладені `CreateAgent` packets replay-яться пізніше.
8. Follow-up packets теж replay-яться пізніше:
   - `SetWieldedItemIndex`
   - `SynchronizeAgentSpawnEquipment`
   - weapon data / reload data / usage data

Цей дизайн працює тільки якщо порядок replay є строгим і повним.

<a id="5-exact-transfer-and-materialization-model"></a>
## 5. Модель Exact Transfer І Матеріалізації
Фактично тут накладаються **три** системи ідентичності:

1. **Vanilla network identity агента**
- нативний `AgentIndex`
- vanilla `CreateAgent`
- vanilla follow-up packets

2. **Ідентичність battle snapshot / entry**
- coop entry ids
- identity через side + troop + layout + source party

3. **Ідентичність exact transfer**
- strict hero contracts
- exact weapon layouts
- exact runtime characters / shells / items

Поточний runtime намагається звіряти їх у реальному часі.

Саме тому `ShouldRequireExplicitMaterializationEntryId(...)` є критичним:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:9599)

Цей метод тепер блокує ordinary-agent exact-binding, доки authoritative materialized map не стане непорожньою.

Це був правильний напрямок, але він вирішив лише **першу** ordering-помилку.

<a id="6-deferred-packet-model"></a>
## 6. Модель Відкладених Пакетів
Поточний handoff patch має окремі deferred queue для багатьох типів пакетів.

Приклади у [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:129):
- deferred `CreateAgent`
- deferred `SetAgentActionSet`
- deferred `AgentSetFormation`
- deferred `SynchronizeAgentSpawnEquipment`
- deferred `SetWieldedItemIndex`
- deferred weapon network data / ammo / reload / usage

Replay helper-и розбиті за сім'ями пакетів.

Важливі приклади:
- `TryReplayDeferredClientCreateAgents(...)`: [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:5768)
- `TryReplayDeferredClientSetWieldedItemIndex(...)`: [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:7030)

Це означає, що коректність replay залежить від:
- повноти queue
- політики видалення елементів
- порядку залежностей між queue
- перевірок типу "агент існує"
- перевірок готовності rider/mount

<a id="7-current-reproducible-failure-clusters"></a>
## 7. Поточні Відтворювані Кластери Помилок
Є дві окремі сім'ї збоїв.

### A. Зависання / краш dedicated server у бою
Це довготривала проблема з bolt / mounted target. Останнє звуження показує:
- це не сирий `HitWorld`
- це не сирий `Stick`
- це не сирий `AttachWeaponToBone`
- це, ймовірно, пізніший native mounted-ranged bookkeeping після bolt hit по mounted target

Ця сім'я ще не вирішена, але вона **не** є тією самою проблемою, що й поточний клієнтський візуальний краш.

### B. Клієнтський side-selection / visual crash
Це поточний активний блокер.

Ця сім'я вже пройшла через кілька видимих симптомів:
- vanilla `MPIntermissionVM` null-path
- timing-проблеми vanilla entry UI suppression
- ordinary exact-binding до приходу authoritative data
- зараз: deferred replay mismatch між `CreateAgent` і follow-up пакетами

<a id="8-what-the-latest-client-crash-actually-shows"></a>
## 8. Що Насправді Показує Останній Клієнтський Краш
Останній релевантний прогін:
- клієнтський crash process: [watchdog_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/watchdog_log_22060.txt:1)
- сервер не впав: [watchdog_log_54660.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/watchdog_log_54660.txt:1)

### Що вже виправлено в цьому прогоні
- vanilla intermission callback приглушений: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:3499)
- exact runtime bootstrap відкладається, поки клієнт ще в selection gate: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:3649)
- ранні authoritative snapshots порожні, і ordinary `CreateAgent` правильно відкладається:
  - порожній snapshot: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:776372)
  - deferred `CreateAgent`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:776461)

### Що змінилось після нового gate
- пізніше приходить уже непорожній authoritative snapshot: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785419)
- replay відкладених `CreateAgent` починає працювати, але перший успіх стартує лише з `AgentIndex=98`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:783794)

### Нова конкретна поломка
Для ранніх відкладених агентів follow-up packets усе одно обробляються пізніше, хоча сам агент локально так і не materialize-ився.

Приклади:
- відсутні агенти `80..86`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785615)
- відсутні агенти `36, 38, 40, 42, 44, 46`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785669)

Конкретний виняток:
- `TaleWorlds.Core.MBNotFoundException`
- `Agent with index ... could not be found while reading reference from packet`

Це означає:
- readiness gating тепер працює
- але coupling між replay queue все ще не працює коректно

### Вторинний сигнал
Серед replay-нутих mounted агентів є випадки, де rider уже існує, а mount runtime tracking ще відсутній під час mounted follow-up events:
- `MountRuntime={... Stage=absent ...}` для ordinary mounted replay: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785339)

Це слабший сигнал, ніж відсутні агенти, але він теж важливий.

<a id="9-tdm--vanilla-mp-coupling"></a>
## 9. Зчеплення З TDM / Vanilla MP
Поточний runtime все ще успадковує забагато припущень від vanilla MP / TDM:
- startup місії
- lifecycle team selection
- порядок network packet-ів для агентів
- mounted lifecycle assumptions
- lobby / entry UI behavior

Потім coop runtime починає поступово ці припущення перекривати:
- приглушує один vanilla callback
- обгортає один vanilla stack
- відкладає один packet
- replay-ить packet пізніше
- перев'язує ordinary agents до authoritative entries

Саме тому після кожного локального фіксу з'являються нові збої:
- кожен локальний patch виправляє один порушений інваріант
- але інші queue або callback-и все ще живуть у припущенні про оригінальний vanilla timeline

<a id="10-invariants-we-need-to-preserve"></a>
## 10. Інваріанти, Які Ми Повинні Зберігати
Runtime повинен зберігати такі інваріанти:

1. Жодного ordinary exact-binding до того, як authoritative materialized map стане непорожньою.
2. Жодного follow-up packet для агента до того, як локальний `CreateAgent` успішно materialize-ить цього агента.
3. Жодного mounted follow-up packet до того, як і rider, і mount link будуть доступні для mounted path-ів, які цього потребують.
4. Vanilla intermission / entry UI не повинні керувати flow після того, як authoritative став coop selection shell.
5. Exact hero path і ordinary AI path не повинні ділити ті самі припущення, якщо це не було явно спроєктовано.

Зараз найчіткіше активне порушення - це інваріант `2`.

<a id="11-what-is-probably-wrong-in-the-current-design"></a>
## 11. Що Ймовірно Неправильно В Поточному Дизайні
Зараз система виглядає так, ніби в неї є **локальна коректність queue**, але немає **глобальної коректності replay**.

Простими словами:
- queue `CreateAgent` знає, коли треба чекати
- queue `SetWieldedItemIndex` знає, що треба чекати, якщо `CreateAgent` ще відкладений
- але після приходу authoritative snapshot ми все одно отримуємо стан, у якому:
  - частина `CreateAgent` повідомлень так і не створює локального агента
  - але їхні follow-up queue все одно продовжують replay

Отже ймовірна design-помилка одна з таких:
- частковий gap у `CreateAgent` replay
- тихий `CreateAgent` replay non-materialization без жорсткого rollback залежностей
- replay ordering, який дозволяє dependent queue просуватись попри відсутніх агентів
- раннє видалення або непостійне збереження `DeferredClientCreateAgentPayload` для частини діапазону агентів

Ключове спостереження таке:
- успішний replay починається з `AgentIndex=98`
- збої follow-up пакетів групуються нижче цього індексу

Це дуже сильно схоже не на випадкову корупцію, а на **системний replay gap у ранньому зрізі deferred агентів**.

<a id="12-recommended-next-directions"></a>
## 12. Рекомендовані Наступні Напрямки
### Короткострокова стабілізація в межах поточної архітектури
Фокус має бути на коректності replay, а не на нових UI suppression чи експериментах із кіньми.

Конкретно:
- перевірити, чому частина deferred `CreateAgent` нижче `98` так і не materialize-иться
- зробити dependent queue жорстко заблокованими на confirmed local agent existence
- не дозволяти follow-up replay працювати для agent index-ів, у яких `CreateAgent` так і не завершився успіхом
- трактувати mounted rider/mount late-bind як окрему залежність другої фази

### Середньострокове очищення архітектури
Явно розділити runtime на:
- strict hero path
- ordinary AI path
- mounted ordinary AI path

Зараз ці шляхи ділять забагато інфраструктури й забагато однакових припущень.

<a id="13-suggested-clean-core-migration-scope"></a>
## 13. Пропонований Scope Міграції На Clean Core
Якщо проєкт піде від TDM-centered runtime patching, ціль clean-core міграції має бути такою:

1. Залишити vanilla MP transport тільки там, де він справді потрібен.
2. Повністю володіти selection flow.
3. Повністю володіти lifecycle materialization ordinary agent-ів.
4. Мати одне authoritative джерело істини для agent-entry identity.
5. Мати одне authoritative джерело істини для mount lifecycle після spawn.
6. Не replay-ити vanilla follow-up packets у напівкастомний lifecycle, якщо їхні передумови не гарантуються coop-owned state.

Цю міграцію треба починати на основі цього runtime inventory, а не всліпу.

<a id="14-reference-files"></a>
## 14. Опорні Файли
- Mission wrapper і behavior injection:
  - [MissionStateOpenNewPatches.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/MissionStateOpenNewPatches.cs:235)
- Coop network bridge:
  - [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:1064)
  - [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:2097)
- Основна mission runtime logic:
  - [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:32)
  - [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:9599)
  - [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:6491)
- Battle-map handoff і replay:
  - [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:5768)
  - [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:7030)
  - [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:10495)
- Exact transfer infrastructure:
  - [ExactTransferContractBuilder.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactTransferContractBuilder.cs:12)
  - [ExactCreateAgentServerPreSpawnContractResolver.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs:33)
  - [ExactCampaignRuntimeItemRegistry.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignRuntimeItemRegistry.cs:14)
- UI suppression / overlay:
  - [IntermissionVmCrashGuardPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/IntermissionVmCrashGuardPatch.cs:18)
  - [VanillaEntryUiSuppressionPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/VanillaEntryUiSuppressionPatch.cs:17)
  - [CoopMissionSelectionView.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopMissionSelectionView.cs:16)
