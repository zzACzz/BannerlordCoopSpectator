## Мета
Заморозити поточний стан робіт по `reconnect / late join` в активну фазу бою, щоб повернутись до нього після MVP-guard для unsupported campaign mission сценаріїв.

## Де зараз стоїмо
- Від старого broad-unsafe перехоплення `MissionNetworkComponent.SendAgentsToPeer` у бойовому шляху вже пішли.
- Поточний коридор reconnect вже крутиться навколо `battle snapshot -> finalize gate -> authoritative status -> selection`.
- Останні зміни в робочому дереві ще не завершені й не визнані стабільними:
  - [Infrastructure/CoopBattleEntryStatusBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleEntryStatusBridgeFile.cs)
  - [UI/CoopMissionSelectionView.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopMissionSelectionView.cs)
  - [UI/CoopSelectionUiHelpers.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopSelectionUiHelpers.cs)

## Що вже доведено
- Проблема більше не виглядає як одна конкретна “важка” мережева пачка.
- Корінь у staged-контракті між:
  - завершенням reconnect finalize,
  - відкриттям selection UI,
  - роботою preview / camera / exact-visual recovery,
  - переходом назад у live control.
- Була окрема підтверджена проблема зі status bridge:
  - клієнтський UI міг втрачати статус або читати не свій стан;
  - для цього вже є локальні незавершені зміни з розділенням `battle_entry_status.client.txt` і `battle_entry_status.server.txt`.

## Останній підтверджений стан пайплайна
У новому креші неправильний `side selection` уже не був головною проблемою.

По [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt>):
- `ReconnectFinalize` активується.
- клієнт шле `BattleReconnectFinalizeReadyAck`.
- приходить авторитетний `RespawnSelection` з уже призначеною стороною.
- відкривається `class loadout`.
- одразу після цього окремий hero exact-visual watchdog лізе в live battlefield agent.

Критичні рядки:
- [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt:20569>) reconnect contract active
- [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt:20603>) finalize ready ack
- [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt:20627>) authoritative `RespawnSelection`
- [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt:20633>) `loaded coop class loadout shell`
- [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt:20687>) destructive exact refresh still required
- [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt:20693>) watchdog applied exact hero visual overlay to live agent

Нативне падіння:
- [watchdog_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_14104.txt:26>) `EXCEPTION_ACCESS_VIOLATION`, читання `0x8`

## Найсильніший поточний висновок
Після стабілізації переходу `finalize -> selection` лишився ще один live-runtime коридор, який selection gate не зупиняє:
- `TryMaintainClientPeerHeroExactVisualOverlays`
- destructive шлях через `UpdateSpawnEquipmentAndRefreshVisuals(...)`

Тобто selection UI уже частково staged, але окремий геройський watchdog exact-візуалів досі мутує live agents у момент, коли reconnect peer ще не повернувся в live control.

## Серверний другорядний дефект
На dedicated усе ще є короткий хибний стан:
- `Stage=Alive`
- `HasAgent=False`
- `SpawnStatus=Spawned`

Приклад:
- [rgl_log_8904.txt](</C:/Users/Admin/AppData/Local/Temp/CoopSpectatorDedicated_logs/logs/rgl_log_8904.txt:25527>)
- виправлення назад:
  - [rgl_log_8904.txt](</C:/Users/Admin/AppData/Local/Temp/CoopSpectatorDedicated_logs/logs/rgl_log_8904.txt:25536>)

Це виглядає як наступний кандидат після UI / exact-visual gate, але не як найсильніший тригер останнього крашу.

## Що робити, коли повернемось до reconnect
1. Не чіпати знову broad interception або старий `SendAgentsToPeer` шлях.
2. Замкнути staged reconnect contract ще й на hero exact-visual watchdog.
3. Поки peer у `RespawnSelection` і `HasAgent=False`, заборонити destructive exact refresh live agent-ів.
4. Після цього окремо нормалізувати серверний stale-стан `Alive без агента` до першого статусу після reconnect.

## Останні опорні логи
- Клієнт:
  - [rgl_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_14104.txt>)
  - [watchdog_log_14104.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_14104.txt>)
- Dedicated:
  - [rgl_log_8904.txt](</C:/Users/Admin/AppData/Local/Temp/CoopSpectatorDedicated_logs/logs/rgl_log_8904.txt>)
  - [watchdog_log_8904.txt](</C:/Users/Admin/AppData/Local/Temp/CoopSpectatorDedicated_logs/logs/watchdog_log_8904.txt>)
