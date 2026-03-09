# AI Multiplayer Hints

## Ціль
Коротка карта ванільного MP-флоу для реалізації коопу кампанії.

## Ключова ідея
Кооп будується поверх existing MP local/dedicated pipeline:
- Campaign side лише ініціює події битви.
- Реальна битва й контроль юнітів живуть у MP mission.

## Важливі точки ванільного флоу
- Ініціалізація мережі: `MultiplayerMain.Initialize*` + `GameNetwork.Initialize`.
- Запуск сервера: `StartMultiplayerOnServer`.
- Запуск клієнта: `StartMultiplayerOnClient`.
- Join custom/listed server: через lobby/custom flow.

## Чому `MyPeer == null`
Типова причина: MP стек не піднятий правильним шляхом або клієнт не пройшов повний join handshake.

## Що використовувати у коопі
- Для бою: ванільний MP mission lifecycle.
- Для прив’язки до кампанії: детектор бою + dedicated commands (`start_mission`, `end_mission`).
- Для юнітів: серверний spawn на основі campaign roster.

## Антипатерни
- Не спавнити гравцю агента на клієнті напряму.
- Не зав’язувати критичну логіку на UI-only компоненти.
- Не змішувати campaign TCP синхронізацію з core MP battle state.

## Мінімальні події для логування
- Mission opened.
- Peer joined/left.
- Agent assigned/removed.
- Spectator enter/return.
- Mission end reason.

## Критерій готовності етапу 3.x
Клієнт стабільно:
1. заходить у місію,
2. обирає/отримує юніта,
3. керує агентом,
4. після смерті повертається у spectator,
5. без десинків доходить до `end_mission`.
