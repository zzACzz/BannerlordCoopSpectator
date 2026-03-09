# Custom Game Mode Plan

## Мета
Режим, де клієнт обирає юніта з campaign roster, а сервер спавнить агента авторитетно.

## Фаза A: Базова інтеграція (completed/verified)
- Модуль доступний на dedicated.
- Є старт через helper.
- Є основний mission lifecycle.

## Фаза B: Режим CoopBattle/TdmClone
- Явна реєстрація game mode.
- Узгоджений `GameTypeId` у всіх точках.
- Коректний mission behavior stack для dedicated.

## Фаза C: Server-side spawn
- Сервер приймає `troop_id` вибору.
- Валідує по roster.
- Створює `AgentBuildData` з player controller.
- Логує success/fail на peer.

## Фаза D: UI/UX вибору
- Мінімум: використати ванільний team/class flow.
- Розширення: кастомний UI для roster юнітів.

## Критерії готовності
- Клієнт гарантовано отримує контроль агента.
- Немає crash при старті/рестарті місії.
- Після смерті є коректний spectator fallback.
