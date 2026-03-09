# DLL Inventory Report (Short)

## Для чого цей файл
Дати короткий operational висновок по DLL, без великого дампу списків.

## Головні факти
- Клієнт і dedicated мають різні набори DLL та часто різні версії.
- `TaleWorlds.MountAndBlade.Multiplayer.dll` існує в client і dedicated, але це не завжди однаковий бінарний артефакт.

## Практичні правила
1. Dedicated проєкт компілювати проти dedicated DLL.
2. Client проєкт компілювати проти client DLL.
3. Не змішувати посилання між ними в одному build profile.

## Мінімальний аудит перед тестом
- Перевірити `FileVersion` і `LastWriteUtc` ключових DLL.
- Перевірити шляхи завантаження в runtime логах.
- Підтвердити, що compile refs збігаються з runtime expected refs.

## Якщо потрібен повний інвентар
Повну історичну версію звіту дивись в архіві:
- `docs/archive/2026-03-08_pre_optimize/DLL_INVENTORY_REPORT.md`
