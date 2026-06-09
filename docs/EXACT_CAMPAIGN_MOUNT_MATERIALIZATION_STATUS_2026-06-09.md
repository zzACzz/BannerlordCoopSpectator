# Статус exact campaign mount materialization

Дата: 2026-06-09

## Контекст

Після стабілізації exact campaign 1-to-1 transfer (точного перенесення кампанійних бійців 1-в-1) залишалася окрема проблема кінноти:

- вершники часто спавнились на шаблонних MP-конях;
- броня коня теж бралася з шаблонного MP-шляху;
- попередня спроба жорстко інжектити лише mount-профіль (профіль коня і броні коня) ламала спорядження вершника і давала голих райдерів.

Цей документ фіксує поточний робочий результат саме для field battle (польової битви), без змішування з village battle (битвою в селі).

## Що підтверджено в поточному прогоні

У поточному ручному прогоні підтверджено:

- кіннота більше не звалюється в один шаблонний MP-кінь;
- різні типи кампанійної кінноти проходять із різними кампанійськими кіньми;
- броня коня теж проходить із кампанійного знімка;
- крашу в цьому прогоні не було.

Це підтверджено не лише візуально, а й по логах:

- [rgl_log_138540.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_138540.txt:24520>) показує, що dedicated create-time contract (контракт серверного створення агента) для mounted entry (кінного запису) анотується як `Inject=True, Weapons=False, Cape=False, Mount=True`.
- [rgl_log_144936.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_144936.txt:3540>) показує snapshot mapping summary (підсумок зіставлення точного знімка) з різними кампанійськими `Horse` і `HorseHarness`:
  - `aserai_horse`
  - `t2_aserai_horse`
  - `empire_horse`
  - `t2_empire_horse`
  - `t3_empire_horse`
  - `t2_battania_horse`
  - `noble_horse_imperial`
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:4436>) показує `client-create-agent-postfix` (постфікс клієнтського створення агента) для `imperial_cataphract` з `SpawnMount={ArmorItemEndSlot=t3_empire_horse, HorseHarness=imperial_scale_barding}`.
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:4468>) показує `imperial_heavy_horseman` з `SpawnMount={ArmorItemEndSlot=t2_empire_horse, HorseHarness=half_scale_barding}`.
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:4952>) показує Aserai cavalry (асерайську кінноту) з `SpawnMount={ArmorItemEndSlot=aserai_horse, HorseHarness=aseran_village_harness}`.
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:4969>) показує `battanian_horseman` з `SpawnMount={ArmorItemEndSlot=t2_battania_horse, HorseHarness=battania_horse_harness_halfscaled}`.
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:8267>) показує пізніший бойовий стан для `imperial_heavy_horseman`, де `Mount={ArmorItemEndSlot=t2_empire_horse, HorseHarness=half_scale_barding}` не відкотилося назад.
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:8468>) показує такий самий пізній бойовий стан для `battanian_horseman`.
- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:8592>) і [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:13376>) показують той самий результат для `aserai_mameluke_heavy_cavalry`.
- [rgl_log_144936.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_144936.txt:4090>) - [rgl_log_144936.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_144936.txt:4098>) показують `Render Requested` (запит рендеру) для конкретних кампанійських коней і броні коня, а не для одного спільного шаблону.
- [watchdog_log_144936.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_144936.txt:39>) показує штатне завершення процесу без нового крашу.

## Як досягнуто поточного результату

### 1. Серверний safe path (безпечний серверний шлях) перестав повністю вимикати create-time injection для mount-only case (випадку, де на старті треба інжектити лише коня)

Файл:

- [Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs)

Що змінено:

- для `useDedicatedSafeStringIdExactEquipmentPath` (safe-шляху dedicated server) додано вузький виняток;
- якщо entry (запис бійця) є mounted, не strict hero path (не строгий геройський шлях), і потрібні лише mount visuals (візуали коня), сервер дозволяє `injectEquipment=true`;
- при цьому сервер явно вимикає:
  - `includeWeapons`
  - `includeArmorVisuals`
  - `includeCape`

Суть:

- повна exact gear injection (точна інжекція всього спорядження) у dedicated create-agent path (серверному шляху створення агента) залишилася занадто крихкою;
- але повне вимкнення injection залишало кавалерію на шаблонному MP-коні;
- тому було відкрито лише вузький mount-only corridor (вузький коридор лише для коня і броні коня).

### 2. Pre-spawn equipment builder (складач спорядження перед spawn) перестав підміняти весь rider loadout (весь комплект вершника) mount-only набором

Файл:

- [Patches/ExactCampaignPreSpawnLoadoutPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/ExactCampaignPreSpawnLoadoutPatch.cs)

Що змінено:

- у `BuildPreSpawnEquipment(...)` додано mount-only hybrid path (гібридний шлях лише для коня);
- цей шлях:
  - клонує native pre-spawn equipment (нативне стартове спорядження рушія);
  - окремо будує exact mount equipment (точний mount-набір із campaign snapshot);
  - копіює тільки `EquipmentIndex.Horse` і `EquipmentIndex.HorseHarness`;
  - повертає гібридний набір: native rider + exact campaign mount.

Суть:

- попередня спроба передавати mount-only equipment як повний `SpawnEquipment` фактично викидала тіло вершника, його броню і його базовий native spawn profile (нативний стартовий профіль), що давало голих райдерів;
- новий гібридний шлях чіпає лише два слоти:
  - кінь
  - броня коня

## Чому це спрацювало

Поточне рішення закриває обидві попередні поломки одночасно:

1. Якщо нічого не інжектити на create-time stage (етапі стартового створення), рушій залишає шаблонного MP-коня.
2. Якщо підміняти весь pre-spawn equipment лише mount-набором, зникає нормальний rider base (базовий комплект вершника).
3. Якщо взяти native base equipment і замінити лише `Horse` + `HorseHarness`, вершник залишається стабільним, а кінь стає кампанійським.

## Додаткове спостереження по vanilla payload (ванільному вхідному мережевому пакету)

Логи окремо показують важливий факт:

- vanilla/TDM payload (ванільний пакет TDM-режиму) все ще може прийти з шаблонним конем, наприклад `mp_empire_horse` або `mp_sturgia_horse`;
- але після нашого exact handoff (точного handoff-переходу в нашому шарі) агент уже має правильний кампанійський `SpawnMount`.

Прямий приклад:

- [rgl_log_126228.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_126228.txt:4414>)
  - `PayloadMount={ArmorItemEndSlot=mp_empire_horse, HorseHarness=mp_northern_light_harness}`
  - але фактичний `SpawnMount={Horse=noble_horse_imperial, HorseHarness=imperial_scale_barding}`

Тобто зараз підтверджено саме не "відсутність шаблонного payload", а коректне перезаписування шаблонного payload нашим exact mount path (точним шляхом підміни mount-слотів).

## Поточний висновок

Для field battle поточний результат можна вважати зафіксованим:

- коні і броня коня проходять із campaign snapshot (кампанійного знімка);
- масового повернення до одного шаблонного MP-коня в поточному прогоні не видно;
- шлях досягнуто без повернення до проблеми голих райдерів.

Що ще не доведено цим документом:

- що це вже повністю закриває всі можливі mounted case (кінні випадки) у всіх типах місій;
- що village battle також має той самий стабільний результат.

Цей документ фіксує саме поточний робочий стан і механізм, яким його досягнуто.
