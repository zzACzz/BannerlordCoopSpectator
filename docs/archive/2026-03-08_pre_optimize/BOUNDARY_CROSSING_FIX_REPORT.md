# MissionBoundaryCrossingHandler — Factual Report (TdmClone crash fix)

## 1. Як ваніль створює handler

- **Тип:** `TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler` (базова збірка MountAndBlade, не Multiplayer).
- **Конструктор (API 1.3.4):** `MissionBoundaryCrossingHandler(float leewayTime = 10f)` — один опційний параметр, за замовчуванням 10f.
- **Місце в behaviors:** додається до mission behaviors разом із MissionHardBorderPlacer та MissionBoundaryPlacer; порядок у ванілі — після boundary placer’ів.
- **Призначення:** перевірка виходу гравця за межі місії та відступ з місії через певний час; ванільний UI `BoundaryCrossingVM` викликає `_mission.GetMissionBehavior<MissionBoundaryCrossingHandler>()` і падає, якщо повертається null.

## 2. Чому наш helper його не створив

- Спочатку викликався лише **Multiplayer** тип `TaleWorlds.MountAndBlade.Multiplayer.MissionBoundaryCrossingHandler` через `TryCreateBehavior()` — тобто **лише parameterless** ctor. У Multiplayer збірці такого типу може не бути або він має інший ctor.
- Далі шукали `TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler` по всіх збірках, але пробували лише **ctor(Mission)** і **parameterless**. У ванілі конструктор — **ctor(float)**, тому обидва варіанти могли не підійти (наприклад, рефлексія не знаходила ctor(Mission), а parameterless міг не викликатися коректно залежно від збірки/версії).
- Підсумок: не використовувався правильний **базовий** тип із MountAndBlade і не викликався **ctor(float)** з `leewayTime = 10f`.

## 3. Який фікс внесено

- **MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler:**
  - Спочатку бере тип із **базової збірки** `typeof(Mission).Assembly` за повним ім’ям `TaleWorlds.MountAndBlade.MissionBoundaryCrossingHandler`.
  - Потім пробує тип із **Multiplayer** (якщо є окремий клас).
  - Потім пошук по всіх збірках (allowlist).
- Для знайденого типу викликаються в порядку: **ctor(float)** з `leewayTime = 10f`, **ctor(Mission)** (якщо є), **parameterless**.
- **MissionBoundaryCrossingHandler** тепер **обов’язковий**: у TdmClone, CoopTdm і CoopBattle використовується **AddRequired**. Якщо створити handler не вдається, відкриття місії переривається з логом (без крашу в BoundaryCrossingVM).
- При невдачі створення в лог виводяться **сигнатури конструкторів** знайденого типу для діагностики.

## 4. Чи потрібні ще пов’язані boundary behaviors

- **MissionHardBorderPlacer** і **MissionBoundaryPlacer** уже додаються як **required** перед MissionBoundaryCrossingHandler у всіх наших режимах (TdmClone, CoopTdm, CoopBattle). Цього достатньо разом із handler’ом.
- Додаткові пов’язані типи (на кшталт інших placer’ів) у поточному ванільному TDM/skirmish flow не згадуються як обов’язкові для BoundaryCrossingVM; якщо з’являться нові залежності, їх можна додати за аналогією з allowlist і AddRequired.

---

*Тимчасовий workaround:* якщо в якомусь середовищі handler упродовж часу не вдасться створити коректно, можна тимчасово повернути **AddOptional** і додати Harmony‑patch для `BoundaryCrossingVM`, щоб не викликати `GetMissionBehavior<MissionBoundaryCrossingHandler>()` коли handler == null (з чітким логом "BoundaryCrossingVM workaround: no handler"). Не вважати це постійним рішенням, доки не доведено стабільність ванільнего UI без handler’а.
