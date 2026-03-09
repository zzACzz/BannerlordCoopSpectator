# Progress Notes

## Current objective
Закрити vertical slice кооп-битви: join -> unit control -> spectator -> return.

## Confirmed working
- Dedicated helper із campaign стартує.
- Сервер видно у Custom Server List.
- Client join працює.
- `start_mission` і `end_mission` відпрацьовують.
- Повторний цикл у межах однієї dedicated-сесії стабільний.

## Active engineering tasks
1. Peer -> troop -> agent pipeline на сервері.
2. Контроль агента для клієнта без race-condition.
3. Returned-to-spectator логіка після смерті.
4. Повний e2e тест на 2-4 клієнти.

## Known risks
- Version mismatch client vs dedicated.
- Patch compatibility між build версіями гри.
- Mission behavior order та side effects.

## Next milestone acceptance
- 3 битви поспіль без critical помилок.
- Кожен клієнт хоча б 1 раз контролює агента за бій.
- Повернення в intermission стабільне.
- Troubleshooting зафіксований у 1 короткому playbook.
