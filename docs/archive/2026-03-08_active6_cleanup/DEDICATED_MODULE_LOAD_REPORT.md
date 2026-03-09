# Dedicated Module Load Report (Short)

## Мета
Зрозуміти, чи реально dedicated завантажує наш модуль і в якому launch flow.

## Ключовий факт
Доказ завантаження — не наявність DLL на диску, а виконання `OnSubModuleLoad` (лог/маркер у runtime).

## Що перевіряти
- Фактичний command line процесу, який крутить рушій.
- Який `_MODULES_` блок реально застосований.
- Чи є в логах маркери нашого SubModule.

## Типова проблема
Starter може формувати дочірній процес з власним `_MODULES_`; очікуваний список модулів у вашому коді не завжди збігається з реальним runtime.

## Висновок
Для стабільності потрібен контрольований launch profile:
- передбачуваний `_MODULES_`;
- узгоджений GameType;
- runtime підтвердження `OnSubModuleLoad`.

## Рекомендована практика
Після кожної зміни launch flow:
1. Зняти command line активного dedicated process.
2. Перевірити module load markers.
3. Перевірити `start_game` і `start_mission` ланцюжок.
