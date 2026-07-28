# Village Battle Status And Handoff (2026-06-10)

## Мета цього документа

Цей документ фіксує поточний робочий стан саме для `village battle` (битви в селі) у гілці `codex/v0.1.1-refresh`.

Він не описує окремий тип encounter біля поселення (зустрічі біля поселення), який не заходить у runtime битви в селі, і не дає дозволу змішувати village battle з уже стабільною `field battle` (польовою битвою) без нового лог-підтвердження.

## Поточний підтверджений стан

- `field battle` залишається окремою стабільною базою і не був злитий з village battle в одну спільну гілку логіки.
- `village battle` тепер працює як окремий exact campaign runtime (точний runtime перенесення кампанійного бою):
  - runtime сцена йде напряму як кампанійна village-сцена;
  - `MissionInitializerRecord` отримує `SceneLevels=land_raid`;
  - campaign map patch context (контекст кампанійного патча карти) для village runtime не застосовується;
  - exact materialization (точна матеріалізація) працює для повного складу сторін;
  - commander order UI (інтерфейс наказів командира) у village runtime підтверджено логами;
  - `battle_result writeback` (запис результату бою назад у кампанію) проходить.

## Підтверджений native path

За попереднім decompile-дослідженням з:

- `docs/EXACT_CAMPAIGN_SCENE_BOOTSTRAP_ANALYSIS_2026-03-31.md`
- `docs/CAMPAIGN_SCENE_TO_MP_TRANSFER_ANALYSIS_2026-03-28.md`
- `docs/CAMPAIGN_TO_MP_RUNTIME_CONTRACT_ANALYSIS_2026-03-31.md`

поточний village / villager / caravan path (шлях рушія для village / villager / caravan encounter) заходить через кампанійний бійовий corridor (коридор виконання бою), який приводить нас до `MissionState.OpenNew(...)`.

У нашому поточному runtime це підтверджено свіжими логами:

- dedicated log показує `PendingBattleMissionStartupState: armed pending battle mission startup. Scene=empire_village_004`
- далі `MissionState.OpenNew Battle: applied village battle scene-level context. RuntimeScene=empire_village_004 SceneLevels=land_raid`
- і далі йде запуск exact village runtime саме на `empire_village_004`

Практичний висновок:

- для village battle ми більше не мапимо кампанійну сцену в `battle_terrain_*`;
- ми пропускаємо далі пряму village-сцену;
- але піднімаємо її всередині того ж coop battle shell (бойового каркаса місії), який уже стабілізований для exact campaign battles.

## Що саме було змінено

### 1. Класифікація сцен

Файл:

- `Infrastructure/SceneRuntimeClassifier.cs`

Додано:

- `IsVillageBattleScene(...)`
- `IsExactCampaignBattleScene(...)`
- `RequiresLandRaidSceneLevel(...)`
- `RequiresDedicatedSceneRegistration(...)`

Суть:

- village battle відтепер визначається окремо від `battle_terrain_*`;
- exact campaign scene (точна кампанійна сцена) тепер означає:
  - або `field battle` сцена;
  - або village battle сцена;
- official multiplayer map (офіційна MP-мапа) при цьому лишається окремою категорією.

### 2. Розв'язання campaign scene -> runtime scene

Файл:

- `Infrastructure/CampaignToMultiplayerSceneResolver.cs`

Суть:

- якщо кампанійна сцена є village-сценою, runtime отримує цю ж саму сцену напряму;
- для village battle не робиться перехід у `battle_terrain_*`.

### 3. Ініціалізація `MissionInitializerRecord`

Файл:

- `Infrastructure/CampaignMapPatchMissionInit.cs`

Суть:

- для village battle примусово встановлюється `SceneLevels=land_raid`;
- для village battle свідомо пропускається campaign map patch context.

Це важливо, бо village battle не повинен насильно підганятися під польовий контракт `map patch` (патч ділянки карти).

### 4. Battle snapshot і battle scene context

Файл:

- `Campaign/BattleDetector.cs`

Суть:

- для village battle не передається `PatchEncounterDirection`;
- якщо encounter settlement (поселення зустрічі) є селом і місія вже на village-сцені, battle scene context (контекст бойової сцени) береться прямо з `Mission.Current.SceneName`.

### 5. Dedicated scene registration

Файл:

- `DedicatedHelper/DedicatedServerCommands.cs`

Суть:

- перед `start_mission` dedicated helper (допоміжний процес виділеного сервера) тепер реєструє не лише `battle_terrain_*`, а будь-яку scene-aware battle runtime scene (сцену бою, яку потрібно явно зареєструвати для runtime), включно з village battle.

### 6. Exact bootstrap і hero class resolver

Файли:

- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Infrastructure/CampaignMultiplayerHeroClassResolver.cs`

Суть:

- exact bootstrap і mounted/hero surrogate logic (логіка mounted/hero сумісності) більше не обмежені тільки `field battle`;
- вони тепер працюють на рівні `IsExactCampaignBattleScene(...)`.

### 7. Основна mission-логіка exact materialization

Файл:

- `Mission/CoopMissionBehaviors.cs`

Суть:

- кілька ключових exact-scene gate (умов допуску exact runtime) були розширені з `IsCampaignBattleScene(...)` до `IsExactCampaignBattleScene(...)`;
- фікс `GetInitialMaterializedArmyTargetCount(...)` тепер рахує стартову ціль спавну з урахуванням `per-entry cap` (ліміту на один бойовий запис), а не лише загальної кількості.

Саме це зняло попередню проблему, коли village battle впирався у ліміт на один запис і не піднімав повний кампанійний склад.

### 8. Commander order control на клієнті

Файл:

- `Patches/BattleMapSpawnHandoffPatch.cs`

Суть:

- вузько розширені тільки client-side guard (клієнтські умови захисту) для exact commander order/control handoff (етапу передачі керування наказами командира);
- ті ж самі механізми, які вже стабілізували commander behavior (поведінку командира) у field battle, тепер дозволені й для village battle;
- серверне правило, хто саме отримує `CommanderControl=general`, при цьому не перероблялося.

## Як це працює зараз

Поточний village battle pipeline (ланцюг роботи битви в селі) такий:

1. Кампанія формує battle snapshot (знімок бою) з village map scene.
2. `CampaignToMultiplayerSceneResolver` не мапить village-сцену в `battle_terrain_*`, а лишає її як runtime scene.
3. `CampaignMapPatchMissionInit` виставляє `SceneLevels=land_raid` і не застосовує `map patch`.
4. `DedicatedServerCommands` перед стартом місії реєструє village runtime scene на dedicated.
5. `ExactCampaignArmyBootstrap` і `CoopMissionBehaviors` піднімають exact materialized armies (точно матеріалізовані армії) вже для village runtime.
6. Після possession (вселення у бійця) герой-командир отримує server-side general control (серверне генеральське керування) через уже наявний exact commander path.
7. `BattleMapSpawnHandoffPatch` на клієнті тримає правильний order UI/control handoff і не дає village runtime звалитися назад у поломану field-only поведінку.
8. Після бою `BattleDetector` проводить writeback назад у кампанію.

## Що підтверджено свіжими логами

Прогін від `2026-06-10` підтверджено такими логами:

- dedicated:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_143240.txt`
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_143240.txt`
- host:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_134644.txt`
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_134644.txt`
- client:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_115552.txt`
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_115552.txt`

Підтверджені факти:

### 1. Повний старт сторін у village battle

У dedicated log:

- `Scene=empire_village_004`
- `PhaseState=[Defender{Total=41,InitialPending=0,InitialSpawned=41,...}; Attacker{Total=28,InitialPending=0,InitialSpawned=28,...}]`

Висновок:

- village battle більше не обрізає старт через стару логіку ліміту на запис;
- повний кампанійний склад реально піднявся в місії.

### 2. Командир отримує реальне керування

У dedicated log:

- `promoted exact campaign commander peer to general control`
- `materialized army replace-bot succeeded ... CommanderControl=general ... FormationOwnership=general`

Висновок:

- це не лише локальна візуальна ілюзія на клієнті;
- сервер справді визнав героя-командира генералом.

### 3. Меню наказів у village battle реально працює

У client log:

- `exact commander order menu interaction. Context=OpenToggleOrder ... Mission=empire_village_004`
- `Writing message: Apply order: Charge`

У dedicated log:

- `SetOrder Chargeon team`
- `After set order called, number of selected formations: 3`

Висновок:

- наказ `Charge` не лише відмалювався локально;
- його реально прийняв сервер для village battle.

### 4. Результат бою повернувся в кампанію

У host log:

- `BattleDetector: consumed battle_result writeback audit.`
- `WinnerSide=Attacker`
- у `Summary=[...]` присутні:
  - `main_hero`
  - `CharacterObject_1653`
  - `CharacterObject_1660`

Висновок:

- герой і companion entries (записи компаньйонів) не загубилися;
- aftermath/writeback (післябойовий запис назад у кампанію) відпрацював.

### 5. Падіння процесів не було

У всіх трьох `watchdog` логах є звичайний `EXIT_PROCESS_DEBUG_EVENT`, без crash dump (дампу падіння).

## Що можна безпечно перевикористовувати з field battle

Безпечне перевикористання:

- exact battle snapshot layer (шар точного знімка бою);
- exact roster materialization (точна матеріалізація ростерів);
- hero/class/loadout transfer (перенесення героя, класу і спорядження);
- mount materialization logic (логіка матеріалізації коней);
- replace-bot possession path (шлях заміни бота на керованого бійця);
- commander promotion path (шлях підвищення командира до генерала);
- battle-map client handoff corridor (клієнтський коридор передачі керування);
- battle result writeback layer.

## Що має лишатися окремим саме для village battle

Окрема village-specific логіка:

- класифікація scene як village runtime;
- пряма передача village runtime scene без мапінгу в `battle_terrain_*`;
- `SceneLevels=land_raid`;
- пропуск `campaign map patch context`;
- відсутність `PatchEncounterDirection`;
- окрема обережність до spawn/deployment assumptions (припущень про спавн і розстановку), якщо надалі з'являться village-specific регресії.

## Що поки не покрито цим handoff

Цей документ не покриває:

- окремий тип бою біля поселення, який не переходить у village runtime;
- нову переробку server-side commander reassignment (серверного перепризначення командира) після смерті героя, якщо в майбутньому знадобиться окрема логіка передачі генеральського контролю;
- будь-яке змішування field battle і village battle в одну універсальну модель.

## Залишковий шум, який зараз не вважається blocker

У свіжих логах ще видно:

- ранні `PlayerOrderController.Owner is null` на клієнті;
- `IntermissionVmCrashGuardPatch: swallowed exception ... MPIntermissionVM.OnIntermissionStateUpdated`;
- `Failed to create MissionMatchHistoryComponent` для неофіційного бою.

Поточний висновок:

- це не зламало village battle runtime;
- ці сигнали варто тримати окремо від handoff по самій битві в селі.

## Файли цього етапу

- `Campaign/BattleDetector.cs`
- `DedicatedHelper/DedicatedServerCommands.cs`
- `Infrastructure/CampaignMapPatchMissionInit.cs`
- `Infrastructure/CampaignMultiplayerHeroClassResolver.cs`
- `Infrastructure/CampaignToMultiplayerSceneResolver.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Infrastructure/SceneRuntimeClassifier.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`

## Практичний підсумок

Станом на `2026-06-10` village battle:

- має окремий scene/runtime contract (контракт сцени і runtime);
- безпечно перевикористовує значну частину exact campaign architecture (архітектури точного кампанійного перенесення) з field battle;
- не потребує відкату стабільної польової битви;
- підтверджений свіжими логами як робочий у повному циклі:
  - старт місії;
  - повний спавн;
  - накази командира;
  - завершення бою;
  - writeback у кампанію.

## Оновлення 2026-07-28: окрема оптимізована початкова матеріалізація та межі

Для `VillageBattle` (битви в малому поселенні) додано окремий модуль дозованого
клієнтського відтворення початкових бійців, окреме підтвердження повної
готовності та виправлення плану розстановки за фактичними позиціями армій.

Реалізація не використовує таймери, підтвердження або кеш геометрії польової
вилазки й звичайної польової битви. Докладний опис і критерії наступного прогону
зафіксовані у
`docs/EXACT_VILLAGE_BATTLE_DEPLOYMENT_AND_MATERIALIZATION_2026-07-28.md`.

На момент цього запису обидва проєкти збираються без помилок, але новий шлях ще
не вважається підтвердженим практичним прогоном.
