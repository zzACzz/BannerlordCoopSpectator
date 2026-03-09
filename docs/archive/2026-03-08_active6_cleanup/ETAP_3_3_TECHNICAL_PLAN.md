# Stage 3.3 Technical Plan

## Ціль етапу
Стабільний ланцюжок: mission join -> unit control -> spectator fallback.

## Вхідні умови
- Dedicated цикл `start_mission`/`end_mission` стабільний.
- Клієнт заходить через Custom Server List.

## Мінімальна реалізація
1. Додати reliable логування стану peer у місії.
2. Підтвердити, коли з’являється/зникає `Agent.Main`.
3. Зафіксувати team/side transitions.
4. Підтвердити spawn events на сервері.

## Обов’язкові логи
- Mission entered.
- Has agent / no agent.
- Controlled agent changed.
- Returned to spectator.
- Mission result / exit.

## Впровадження
- `CoopMissionClientLogic`: клієнтські стани й transitions.
- `CoopMissionSpawnLogic`: серверні spawn/peer події.
- У TdmClone/CoopBattle додавати behaviors у кінець списку.

## Ризики
- Невірний порядок behaviors.
- Client-only behavior у dedicated stack.
- Відмінності API між білдами.

## Test checklist
1. 2+ клієнти join до однієї місії.
2. Кожен отримує контроль агента.
3. Після смерті — spectator.
4. `end_mission` повертає всіх у intermission.
5. Повторити цикл 3 рази без критичних помилок.
