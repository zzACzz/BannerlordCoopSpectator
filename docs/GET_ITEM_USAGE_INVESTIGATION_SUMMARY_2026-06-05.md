# Підсумок розслідування get_item_usage_get_index_with_id

## Статус

Коренева причина все ще **не доведена**.

Зараз доведено більш вузьке:

- довгий пошук навколо suppression для mounted bolt-stick **не** прибрав server assert
- останні докази вказують вище, ніж рівень missile attach visuals
- зараз найсильніші підозри такі:
  - стан exact snapshot / create-agent handoff
  - повторне використання stale server spawn state
  - mismatch item identity / materialization
  - можливий mismatch runtime/build між клієнтом і dedicated server

## Основний симптом

Повторювана серверна помилка така:

- `get_item_usage_get_index_with_id failed`

Свіжі докази з dedicated crash:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_101592.txt`
  - crash tag: `-td95064--get_item_usage_get_index_with_id failed .`
  - шлях до dump: `C:\ProgramData\Mount and Blade II Bannerlord\crashes\2026-06-05_05.08.07\dump.dmp`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_101592.txt:800916`
  - `WARNING: get_item_usage_get_index_with_id failed .`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_101592.txt:800956`
  - `rgl_post_warning_line: RGL WARNING - get_item_usage_get_index_with_id failed .`

Старіший аналіз dump з:

- `C:\dev\projects\BannerlordCoopSpectator3\tmp_cdb_105568.txt`

показав, що warning / hang шлях проходить через:

- `msvcp140!_Cnd_wait`
- `Rgl!rglAnim_subnode_human_riding_set::~rglAnim_subnode_human_riding_set`
- `Rgl!rglLibrary_interface::warning+0x394`
- `Game!Animation_clip_item`

Це означає, що помилка не є просто нешкідливим текстовим warning. Вона доходить до native riding / animation / item-usage state.

## Що вже пробували

Раніше вже були досліджені такі напрямки:

1. Відкат на чистішу release-базу і rebuild під новий патч.
2. Широкі експерименти з suppression для bolt-stick на:
   - `Mission.HandleMissileCollisionReaction`
   - `Agent.AttachWeaponToBone`
   - `Agent.AttachWeaponToWeapon`
3. Вужчі guard-и для mounted bolt-hit:
   - mounted mount-body quarantine
   - mounted shield-block quarantine
   - dead/inactive mounted pair guard
   - вузькі guard-и для `OnWieldedItemIndexChange` / `OnWeaponUsageIndexChange`
4. Широкий mounted ranged lifecycle quarantine навколо:
   - `OnWeaponReloadPhaseChange`
   - `OnWeaponAmmoConsume`
   - `OnWeaponAmountChange`
5. Експерименти з native ammo / exact ammo для арбалетних bolt.
6. Експерименти навколо одразу заряджених арбалетів.
7. Verbose runtime diagnostics і аналіз watchdog dump.
8. Низькорівневий аналіз через WinDbg / cdb.
9. Пошук причини поломки манекенів через binary-search вимкнення частин моду.
10. Вужчі експерименти навколо battle-only відображення імен.

## Що вже доведено як не-коренева причина

### 1. Поломки манекенів були реальні, але окремі

Було доведено і задокументовано дві окремі регресії:

- `docs/BOLT_MANNEQUIN_ATTACH_GUARDRAIL_2026-06-04.md`
- `docs/AGENT_DISPLAY_NAME_MANNEQUIN_GUARDRAIL_2026-06-04.md`

Ці висновки важливі, але вони **не** пояснюють сам server assert.

### 2. Остання теорія про mounted ranged ammo-lifecycle була спростована

Ми прибрали selective suppression шлях, який блокував reload/ammo callback-и для live mounted ranged unit.

Результат:

- assert все одно стався
- `rgl_log_101592.txt` все одно містить `get_item_usage_get_index_with_id failed`

Висновок:

- попередня теорія "ми ламаємо native state, бо suppress-имо лише половину mounted ranged ammo lifecycle" **не** була кореневою причиною

Цей експеримент все одно був корисним, бо прибрав одну велику хибну гіпотезу.

### 3. Баг не обмежується одним сценарієм з mounted bow rider

У попередніх прогонах здавалося, що проблема дуже тісно прив'язана до mounted bow rider і його коня.

Останній серверний лог `rgl_log_101592.txt` показує tracked bolt-hit windows навколо:

- mounted melee pair `Agent=132 / Mount=133`
- mounted thrown pair `Agent=182 / Mount=183`
- mounted thrown pair `Agent=156 / Mount=157`

Отже, поточні докази кажуть так:

- mounted bolt events все ще є важливим trigger corridor
- але збій **не** виглядає як вузький сценарій "тільки mounted bow rider з horse-attached arrow"

## Найсильніші поточні докази

### A. Повторюваний create-agent / exact snapshot mismatch навколо crossbow troop

`C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_94752.txt` багаторазово показує:

- `MissionDiff ... Changed=[Weapon1:bolt_e->bolt_e@17]`

Приклади:

- `rgl_log_94752.txt:792637`
- `rgl_log_94752.txt:792728`
- `rgl_log_94752.txt:792829`
- `rgl_log_94752.txt:801880`

Важливий нюанс:

- `bolt_e@17` у наших логах не є окремим `StringId`
- у поточній діагностиці суфікс `@17` формується як `itemId + runtime data value`
- для `MissionWeapon` це поле є `_dataValue`: для болтів воно означає кількість, для щитів може означати durability / hit points
- тому `bolt_e -> bolt_e@17` у `MissionDiff` не доводить перехід на інший предмет; це може бути той самий `bolt_e`, але вже в live mission state з іншим runtime-станом
- наша exact snapshot / handoff діагностика зараз трактує таку зміну як mismatch, хоча `LayoutIdentityMatch=True` уже одночасно показує, що базовий `itemId` збігається
- отже тут підтверджена проблема не в "іншому bolt id", а в тому, що наш diff змішує item identity і runtime state в одному порівнянні

### B. Stale server spawn state / character mismatch з'являється прямо перед warning

З `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_101592.txt`:

- `800904`
  - `server-create-agent-onwrite-sanitize-skipped`
  - `Reason=stale-server-spawn-state:character-mismatch`
  - `AgentIndex=18`
- `800907`
  - spawn result для crossbow troop
  - у spawn weapons є `Weapon1=bolt_e`
  - у mission weapons вже `Weapon1=bolt_e@17`
- `800916`
  - `WARNING: get_item_usage_get_index_with_id failed .`

Важливе уточнення:

- сам перехід `spawn weapons: bolt_e` -> `mission weapons: bolt_e@17` не виглядає аномалією сам по собі
- ordinary AI corridor у нас навмисно розводить `EntryWeapons`, `PreSpawnWeapons` і live `MissionWeapons` по різних етапах
- тому ці три представлення не повинні бути буквально однаковими на кожному кроці

Зараз це найсильніша причина змістити розслідування від bolt-visual suppression у бік:

- повторного використання stale create-agent payload
- неправильного expected/actual troop identity mapping
- mismatch під час exact snapshot materialization

### C. Може існувати mismatch між dedicated/client sub-build

Останні логи показують:

- dedicated server runtime у `watchdog_log_101592.txt`
  - `Build Version#TW#v1.4.5.114659`
- client runtime у `watchdog_log_94752.txt`
  - `Build Version#TW#v1.4.5.115026`
- client runtime у `watchdog_log_90848.txt`
  - `Build Version#TW#v1.4.5.115026`

Саме по собі це **ще не доводить** несумісність.

Але це достатньо підозріло, щоб у наступному раунді окремо перевірити:

1. чи dedicated server install справді має бути на `114659`
2. чи комбінація client `115026` + dedicated `114659` є підтримуваною
3. чи ця різниця версій може впливати на item usage / weapon materialization / agent creation

## Найкраща поточна інтерпретація

Найімовірніша модель зараз така:

1. якийсь agent / item state вже стає неконсистентним під час exact snapshot handoff або create-agent materialization
2. bolt-hit і mounted combat activity пізніше лише активують цей уже зіпсований state
3. native code потім намагається отримати item-usage index зі state, який більше не відповідає очікуваному identity chain
4. RGL викидає `get_item_usage_get_index_with_id failed`
5. сервер зрештою доходить до native warning / riding / animation wait path, який ми бачили в dump

Отже:

- bolt-hit може бути **тригером**
- але create-agent / materialization / identity mismatch може бути **справжнім попереднім джерелом проблеми**

## Чому попередній пошук, імовірно, був зміщений

Ми витратили багато часу навколо:

- bolt sticking
- horse body hit reactions
- reload/ammo callback suppression
- mounted combat lifecycle guards

Такий пошук був логічним, бо видимий crash часто з'являвся після серії bolt-hit по mounted unit.

Але останні логи тепер натякають, що це, ймовірно, був downstream noise навколо глибшої проблеми:

- stale spawn state
- troop identity mismatch
- exact snapshot / mission weapon identity mismatch

Іншими словами:

- ми могли дебажити момент, коли зіпсований state стає видимим
- а не той ранній момент, коли цей state створюється

## Кращий напрямок для наступного пошуку

Не треба починати наступний раунд з нових bolt-visual suppression експериментів.

Починати краще з create-agent / exact handoff corridor:

- `ExactCreateAgentCorridorDiagnostics`
- `CoopMissionSpawnLogic`
- `ExactCampaignPreSpawnLoadoutPatch`
- генерація exact snapshot для ordinary troop
- повторне використання stale server spawn state / expected entry resolution
- логіка heuristic fallback candidate
- правила порівняння mission weapon identity

Питання, на які варто відповісти далі:

1. Чому `server-create-agent-onwrite-sanitize-skipped` виникає так близько до warning?
2. Чи не перевикористовуються expected troop identity після recycle agent slot?
3. Чи не стоїть dedicated server install на неправильному sub-build відносно клієнта?
4. У якій саме точці `create-agent / exact handoff corridor` item identity ще коректний, а live runtime state вже стає небезпечним для `ItemUsage`

## Guardrails для майбутніх спроб

Поки create-agent / materialization corridor не досліджений:

- не витрачати ще один цикл на broad mounted ranged ammo suppression
- не повертати глобальні hook-и на `Agent.AttachWeaponToBone/Weapon`
- не повертати глобальні hook-и на `Agent.Name` / `Agent.NameTextObject`
- не припускати, що "ще більше bolt quarantine" автоматично наближає до кореня

## Короткий підсумок

Найкращий поточний підсумок такий:

- assert реальний і native
- регресії з манекенами були окремими побічними проблемами, а не коренем
- mounted bolt-hit все ще лишається trigger corridor, але вже не виглядає найглибшою причиною
- `bolt_e@17` у наших логах означає не "інший bolt id", а `bolt_e` плюс runtime data value
- нові логи значно сильніше вказують не на прямий bolt-id mismatch, а на проблему в create-agent / exact snapshot corridor або у подальшому live runtime state
- dedicated server vs client sub-build mismatch виглядає підозріло і його треба окремо перевірити перед новими інвазивними змінами
