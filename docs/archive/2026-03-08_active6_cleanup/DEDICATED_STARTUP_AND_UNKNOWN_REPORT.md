# Dedicated Startup + "Unknown" Report (Short)

## Симптоми
- Можливий startup crash на dedicated.
- У списку серверів game mode показується як `Unknown`.

## Що підтверджено
- `GameTypeId` має бути єдиним у реєстрації, конфігу і логах.
- Навіть при `Unknown` у UI сервер може працювати коректно.

## Чому буває `Unknown`
UI клієнта не завжди має локалізацію/мапінг для custom game mode id або не резолвить його в цьому екрані.

## Що це означає практично
`Unknown` у списку серверів — не автоматично баг gameplay. Важливіше:
- join працює;
- місія відкривається;
- режим реально зареєстрований.

## Startup crash: коротка стратегія діагностики
1. Логи до `create_mission`.
2. Логи входу у фабрику behaviors.
3. Логи `AfterStart` по behavior-ланцюжку.
4. Визначення останньої успішної точки.

## Швидкий checklist
- Є лог реєстрації game mode.
- Є правильний `GameType` в конфігу.
- Немає client-only behavior у dedicated stack.
- Є критичні server behaviors (scoreboard тощо).
