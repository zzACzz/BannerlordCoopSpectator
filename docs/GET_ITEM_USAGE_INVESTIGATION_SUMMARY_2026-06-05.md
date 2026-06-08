# Підсумок розслідування `get_item_usage_get_index_with_id`

## Статус

Для конкретного зависання сервера, яке супроводжувалось warning-ом:

- `get_item_usage_get_index_with_id failed`

зараз найкращий підтверджений висновок такий:

- проблема не була в "неправильному болті" як окремому `item id`;
- проблема виникала на native lookup шляху, коли рушій доходив до порожнього `ItemUsage`;
- production-fix, який реально прибрав warning/hang corridor, це guard у `MissionWeapon.HasAnyUsageWithItemUsageSetFlags`.

Поточний fix:

- [Patches/MissionItemUsageSetFlagsGuardPatch.cs](C:/dev/projects/BannerlordCoopSpectator3/Patches/MissionItemUsageSetFlagsGuardPatch.cs)

Його суть проста:

- якщо в `MissionWeapon` чергова usage-entry має порожній `ItemUsage`, ми не віддаємо її в native `MBItem.GetItemUsageSetFlags("")`;
- така entry просто пропускається;
- решта usage-entries перевіряються штатно.

## Що саме тепер доведено

### 1. Warning був реальним native trigger, а не "нешкідливим логом"

Старі crash/log дані показували такий шлях:

- `WARNING: get_item_usage_get_index_with_id failed .`
- далі native stack заходив у riding / animation wait corridor;
- через це сервер доходив до watchdog hang / crash state.

Тобто проблема не була просто текстовим warning-ом у логах. Вона реально ламала native runtime path.

### 2. `bolt_e -> bolt_e@17` не доводить підміну на інший предмет

Раніше один з головних підозрюваних був такий:

- у наших diagnostics видно `bolt_e -> bolt_e@17`;
- з цього робився висновок, що ми десь підміняємо один предмет на інший.

Тепер це треба вважати застарілою інтерпретацією.

У нашій діагностиці суфікс виду `@17` означає не новий `StringId`, а:

- той самий базовий item id;
- плюс runtime `data value`.

Для болтів це зазвичай означає кількість, для інших слотів це може означати інший runtime-state на кшталт durability / hit points.

Отже:

- `bolt_e -> bolt_e@17` саме по собі не є доказом неправильного item identity transition;
- це радше зміна runtime-стану того ж предмета.

### 3. Багато наших попередніх bolt/mount suppression-експериментів не були кореневим фіксом

Ми перепробували:

- широкі suppression для bolt sticking;
- quarantine для mounted pair;
- guard-и навколо reload/ammo/wield callbacks;
- варіанти з native ammo / exact ammo;
- експерименти навколо вже заряджених арбалетів;
- діагностичні обмеження по dead/inactive mounted pair.

Ці експерименти були корисні для звуження, але вони не закрили проблему як production-fix.

Підсумок тут такий:

- болти і влучання по mounted unit були trigger corridor;
- але корисний production-fix виявився не у missile attach / bolt visual layer;
- корисний fix виявився у захисті native item-usage lookup від порожнього `ItemUsage`.

### 4. Поломки манекенів були окремою регресією

Поломка манекенів реально існувала, але вона не була коренем цього server hang.

Окремий guardrail-звіт:

- [docs/BOLT_MANNEQUIN_ATTACH_GUARDRAIL_2026-06-04.md](C:/dev/projects/BannerlordCoopSpectator3/docs/BOLT_MANNEQUIN_ATTACH_GUARDRAIL_2026-06-04.md)

Важливо не змішувати ці дві лінії:

- mannequin regressions;
- `get_item_usage_get_index_with_id` hang corridor.

## Що саме виправило проблему

Поточний робочий guard:

- перехоплює `MissionWeapon.HasAnyUsageWithItemUsageSetFlags`;
- вручну проходить по `WeaponsCount`;
- пропускає usage-записи, де `weapon?.ItemUsage` порожній;
- викликає `MBItem.GetItemUsageSetFlags(itemUsage)` тільки для непорожніх значень.

Практичний сенс:

- ми не даємо рушію намагатися резолвити порожній usage id;
- саме цей lookup і був найкраще підтвердженим місцем падіння warning/hang corridor.

## Які докази це підтримують

Після впровадження `MissionItemUsageSetFlagsGuardPatch`:

- у свіжих успішних прогонах більше не з'являвся `get_item_usage_get_index_with_id failed`;
- watchdog більше не показував той самий crash/hang pattern;
- бій переживав підкріплення і доходив до завершення без старого зависання.

Це не просто нова гіпотеза, а найсильніший на зараз підтверджений практикою висновок.

## Що лишається невирішеним, але вже не є головним lead для цього бага

Нижче речі, які можуть ще вимагати cleanup або окремого розбору, але вже не є найкращим поясненням саме цього warning/hang:

- exact create-agent / handoff diagnostics шум;
- старі corridor comparison rules, які змішували item identity і runtime state;
- battle-only name/display oddities;
- окремі regressions навколо mannequin pipeline;
- будь-який шум від старої diagnostics surface.

Тобто далі правильніше думати так:

- цей конкретний hang path ми закрили guard-ом на empty `ItemUsage`;
- не треба знову повертатись у широкі bolt-suppression експерименти без нових фактів;
- якщо warning повернеться, першим ділом треба перевіряти, чи guard реально завантажився і чи не з'явився новий шлях до порожнього `ItemUsage`.

## Рекомендації на майбутнє

1. Тримати `MissionItemUsageSetFlagsGuardPatch` увімкненим як production-fix.
2. Не трактувати `item -> item@N` у diagnostics як автоматичну підміну предмета.
3. Нову hot-path diagnostics тримати вимкненою за замовчуванням або за explicit verbose gate.
4. Якщо ця помилка колись повернеться, починати не з bolt visuals, а з перевірки:
   - чи є порожній `ItemUsage`;
   - чи guard застосувався на клієнті й dedicated server;
   - чи не з'явився новий native шлях, який обходить цей guard.

## Короткий висновок

Найкращий поточний підсумок такий:

- корінь конкретного server hang був не у "неправильному bolt id", а у пустому `ItemUsage`, який доходив до native lookup;
- `MissionItemUsageSetFlagsGuardPatch` закрив саме цей шлях;
- старі `bolt_e@17` спостереження були корисними для діагностики, але не довели item identity corruption;
- діагностичний шум навколо battle/create-agent corridor треба тримати вимкненим за замовчуванням, щоб не навантажувати бій.
