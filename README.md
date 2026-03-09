# Bannerlord Coop Spectator

Проєкт кооперативного режиму для Mount & Blade II: Bannerlord.

## Головна мета
Дати гравцям кооп-цикл без full-conversion кампанії:
- хост грає звичайну кампанію;
- друзі підключаються до битв хоста через multiplayer;
- після битви потік повертається в кампанію.

## Активна документація (тільки 6 файлів)
1. `README.md` — швидкий вхід.
2. `PROJECT_CONTEXT.md` — архітектура і правила.
3. `HUMAN_NOTES_MULTIPLAYER_PROGRESS.md` — поточний статус.
4. `bannerlord_coop_plan.md` — дорожня карта.
5. `BUILD_RUNBOOK.md` — build/deploy/run.
6. `DEDICATED_TROUBLESHOOTING.md` — діагностика і фікси.

## Поточний фокус
- стабільний вхід клієнтів у battle mission;
- server-authoritative spawn/control;
- spectator fallback після смерті агента;
- повторюваний цикл `start_mission` -> `end_mission`.

## Архів
Усі неактивні/історичні файли винесені в `docs/archive/...`.
