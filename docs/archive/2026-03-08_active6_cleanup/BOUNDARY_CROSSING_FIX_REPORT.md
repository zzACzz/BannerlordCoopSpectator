# Boundary Crossing Fix Report

## Проблема
UI boundary crossing звертався до `MissionBoundaryCrossingHandler`, а в нашому стеку behavior він інколи був відсутній -> crash/null path.

## Причина
Helper створював не той тип або не той ctor для конкретної версії збірок.

## Рішення
- Пріоритетно брати базовий `MissionBoundaryCrossingHandler`.
- Пробувати ctor у порядку: `float` -> `Mission` -> parameterless.
- У критичних режимах додавати handler як required.

## Результат
Mission stack став детермінованим для boundary logic.

## Додатково
Як тимчасовий fallback можна робити optional + defensive patch VM, але це не бажаний постійний шлях.
