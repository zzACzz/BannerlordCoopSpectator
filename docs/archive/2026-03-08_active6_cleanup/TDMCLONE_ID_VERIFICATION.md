# TdmClone ID Verification

## Мета
Підтвердити, що `GameTypeId` для TdmClone однаковий у всіх ключових точках.

## Джерело істини
`Infrastructure/CoopGameModeIds.cs`:
- `public const string TdmClone = "TdmClone";`

## Де має збігатися значення
- Реєстрація game mode на dedicated.
- Startup config (`GameType ...`).
- Хостові лог-повідомлення для `start_mission`.

## Типовий симптом розсинхрону
`Cannot find game type: TdmClone`

## Типові причини
- Модуль dedicated не завантажився.
- Не підтягнулась потрібна DLL залежність.
- `OnSubModuleLoad` не викликав реєстрацію режиму.

## Швидкий чек
1. У dedicated логах є marker реєстрації TdmClone.
2. У config вказано `GameType TdmClone`.
3. У runtime немає missing dependency errors.
