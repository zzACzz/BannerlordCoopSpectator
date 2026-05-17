# Mount & Blade II: Bannerlord - Coop Campaign

Проєкт кооперативного режиму для Mount & Blade II: Bannerlord.

## Головна мета
Дати гравцям кооп-цикл без full-conversion кампанії:
- хост грає звичайну кампанію;
- друзі підключаються до битв хоста через multiplayer;
- після битви потік повертається в кампанію.

## Активна документація
1. `docs/README.md` — навігатор по актуальних `md` файлах.
2. `PROJECT_CONTEXT.md` — короткі архітектурні правила і hard constraints.
3. `HUMAN_NOTES_MULTIPLAYER_PROGRESS.md` — поточний робочий статус.
4. `docs/BATTLE_MAP_STATUS_AND_HANDOFF_2026-03-30.md` — актуальний battle-map handoff.
5. `NEW_CHAT_PROMPT_2026-03-30_BATTLE_MAP_SPAWN_STABLE.md` — готовий текст для нового вікна.
6. `BUILD_RUNBOOK.md` — build/deploy/run.
7. `DEDICATED_TROUBLESHOOTING.md` — діагностика і фікси.

## Поточний фокус
- battle-map stabilization після успішного великого spawn-handshake;
- server-authoritative spawn/control у великих боях;
- spectator / respawn / repeated control cycles;
- repeatable `start_mission` -> `battle` -> `aftermath` -> campaign loop.

## Архів
Усі неактивні/історичні файли винесені в `docs/archive/...`.

## Support the project 🙌

The mod is free and will always remain free.

If you enjoy it and want to support further development (new features, stability, siege battles), you can do it here:

👉 https://ko-fi.com/zaczua

Every bit of support helps improve the mod.
