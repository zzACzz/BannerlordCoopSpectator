# Проблема гербів на щитах у битві

## Статус

- Станом на `2026-06-09` правильні кольори сторін у битві вже передаються.
- Герби фракцій на щитах у поточній гілці не з'являються.
- Спроба примусово додати `Banner` і `MissionEquipment` у локальні шляхи materialized spawn не дала результату в ручному прогоні.

## Які прогони і логи перевірені

Перевірені логи останніх ручних прогонів:

- `C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_140408.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_140292.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_123132.txt`
- відповідні `watchdog_log_*.txt`

## Що підтверджено

### 1. Коди гербів сторін у runtime є

У логах підтверджено, що battle snapshot (знімок стану битви) і runtime state (стан рантайму) мають коди гербів сторін:

- `BattleDetector: side snapshot built ... BannerCodeLength=68`
- `BattleDetector: side snapshot built ... BannerCodeLength=64`
- `CoopBattle server: ensured opposing teams exist ... AttackerBannerCodeLength=68 DefenderBannerCodeLength=64`
- `ExactCampaignArmyBootstrap: formation banner-code seed for exact runtime ... BannerCodeLength=68/64`

Висновок:

- проблема не в тому, що кампанія не передає banner code (код герба) у battle runtime;
- проблема виникає далі, у spawn corridor (коридорі створення агента і його бойового спорядження).

### 2. Для ordinary AI на dedicated server exact pre-spawn injection фактично вимкнений

У поточній схемі для звичайних AI-агентів на dedicated server (виділеному сервері) спрацьовує такий ланцюг:

- `UseDedicatedSafeStringIdExactEquipmentPath=True`
- `InjectEquipment=False`
- `Mission.SpawnAgent result ... EquipmentInjected=False`
- `server-spawn-result` уже містить native `SpawnWeapons` і `MissionWeapons` з MP/TDM-шляху

Це підтверджено в `rgl_log_140408.txt` для багатьох ordinary AI entries (звичайних кампанійних записів армії).

Висновок:

- більшість AI-агентів у dedicated battle народжуються не з exact campaign loadout (точною кампанійною викладкою), а з native MP shell loadout (рідною MP-викладкою оболонки);
- кампанійне спорядження далі накладається поверх уже створеного native агента.

### 3. Поточний overlay path не гарантує герб на новому щиті

Поточний шлях накладання спорядження використовує `UpdateSpawnEquipmentAndRefreshVisuals`.

Цей шлях підходить для:

- підміни предметів;
- частини візуальних оновлень;
- корекції бойової викладки після native spawn.

Але він не дає підтвердженого безпечного механізму:

- перебудувати вже існуючий `MissionWeapon` щита з новим `Banner`;
- або перенести герб з native shield (рідного щита оболонки) на новий campaign shield (кампанійний щит), якщо щит був підмінений пізніше.

Висновок:

- герб у рушії прив'язується не до абстрактного слота, а до конкретного `MissionWeapon`, який був створений разом із `Banner`;
- якщо щит змінено пізніше через overlay, герб не зобов'язаний перейти на новий предмет.

### 4. Спроба примусити `Banner + MissionEquipment` у materialized spawn не допомогла

Було перевірено вузьку спробу:

- явно резолвити battle banner (герб сторони битви);
- передавати його через `buildData.Banner(...)`;
- створювати `buildData.MissionEquipment(new MissionEquipment(spawnEquipment, battleBanner))`.

Результат ручного прогону:

- кольори не зламались;
- герби на щитах не з'явились.

Причина:

- цей локальний force path (примусовий локальний шлях) не змінює головний dedicated full-army create corridor, де ordinary AI все одно створюються через native MP shell path.

## Що показало порівняння із замороженою гілкою

Перевірка замороженої гілки показала, що там не було готового механізму:

- перенесення герба на новий кампанійний щит після spawn;
- або безпечного rebuild path (шляху перебудови бойового щита з гербом) поверх уже створеного агента.

Натомість там був інший підхід:

- `native-seed-shield-preserved-over-stringid-overlay`

Його суть:

- якщо native shell (рідна оболонка) уже народилась зі щитом;
- і цей щит уже потенційно отримав герб під час native create-time;
- overlay path не замінює цей щит на інший без потреби.

Висновок:

- заморожена гілка не передавала герб на кампанійський щит після spawn;
- вона радше намагалась не втратити native щит, який уже міг мати герб.

## Архітектурний висновок

На поточний момент підтверджено таке:

1. Кампанія передає коди гербів сторін у battle runtime коректно.
2. Dedicated full-army spawn path для ordinary AI зараз вимикає exact pre-spawn equipment injection.
3. Через це більшість AI-агентів створюються через native MP shell path.
4. Пізній overlay path не має підтвердженого безпечного механізму переносу герба на новий кампанійний щит.

Отже поточна невдала спроба зводиться не до відсутності banner code, а до архітектурного розриву між:

- native create-time heraldry path;
- і post-spawn exact equipment overlay path.

## Що вже відкинуто

- Версія, що в runtime немає banner code сторін.
- Версія, що проблема лише у кольорах.
- Версія, що досить локально додати `buildData.Banner(...)` у materialized spawn path, не змінюючи dedicated full-army create corridor.

## Найбезпечніший напрямок далі

Найбезпечнішим наступним кандидатом виглядає не примусовий перенос герба на новий campaign shield, а один із двох шляхів:

1. `native shield preservation`
   Суть:
   якщо native оболонка вже створилась зі щитом і отримала герб, не втрачати цей щит під час string-id overlay.

2. Вузький `banner-aware create-time path`
   Суть:
   не форсити всю exact weapon layout на create-time, а дослідити, чи можна безпечно подавати в native path щит і banner у тих кейсах, де це не ламає spawn corridor.

## Поточне рішення в цій гілці

Після цього дослідження невдала локальна спроба форсувати герб через `CoopMissionBehaviors.cs` прибрана, щоб не змішувати:

- робочі кольори сторін;
- banner-code seed сторін;
- і непрацюючу локальну спробу створити герби на щитах без зміни головного dedicated create corridor.
