# Dedicated Troubleshooting

## Symptom: `Cannot find game type`
Що перевірити:
1. `GameTypeId` однаковий у коді, конфігу, логах.
2. Реєстрація game mode реально відбулась (runtime marker).
3. Модуль dedicated завантажився, dependencies присутні.

## Symptom: server listed as `Unknown`
Часто це UI mapping issue для custom mode, не обов'язково gameplay баг.
Критично перевіряти:
- чи join працює;
- чи місія відкривається;
- чи game mode реально активний.

## Symptom: crash on mission startup
Кроки:
1. Локалізувати останній успішний lifecycle лог.
2. Перевірити mission behavior stack (required компоненти).
3. Перевірити, що client-only behavior не потрапив у dedicated.
4. Перевірити version mismatch і Harmony fallback.

## Symptom: Harmony apply fails
- Не падати жорстко; увімкнути fallback path.
- Залогувати patch target і причину.
- Зафіксувати, який gameplay path активний без патча.

## Symptom: intermittent desync/control issues
- Перевірити peer lifecycle логи.
- Перевірити agent assignment/ownership transitions.
- Перевірити race між spawn і control assignment.

## Quick health checklist
- Dedicated compiled against dedicated refs.
- Consistent GameTypeId.
- Required server mission components present.
- 3 цикли `start_mission` -> `end_mission` без critical crash.
