# Dedicated TdmClone Fix Summary

## Виправлено
- Стабілізовано server mission behavior stack.
- Усунуто конфлікт реєстрації `TeamDeathmatch` (duplicate key path).
- Додано fallback логіку для Harmony-помилок.
- Посилено runtime-діагностику версій і binary identity.

## Коренева причина головного крашу
`MissionCustomGameServerComponent` очікував готовий scoreboard component. За його відсутності траплявся NullReference.

## Поточні гарантії
- Серверний стек будується окремо від клієнтського.
- Client-only behaviors не повинні потрапляти на dedicated.
- Логи вказують, чи активний Harmony fallback.

## Що перевірити після білду
1. Маркери `[DedicatedDiag]` у dedicated логах.
2. Реєстрацію `CoopBattle`, `CoopTdm`, `TdmClone`.
3. `HasMissionScoreboardComponent=True` при старті місії.
4. Відсутність критичних винятків в `AfterStart`.

## Обмеження
Якщо Harmony patching не сумісний із конкретним game build, потрібен fallback без жорсткої залежності від цього патча.
