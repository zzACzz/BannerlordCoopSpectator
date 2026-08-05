# Mount & Blade II: Bannerlord — Coop Campaign (легкий випуск)

Пакет підготовлено для `Mount & Blade II: Bannerlord v1.4.7.117484` (Steam build `24127665`) і відповідної версії `Mount & Blade II Dedicated Server`.

Архів містить лише:

- `CoopSpectator` — клієнтський модуль;
- `CoopSpectatorDedicated` — серверний модуль;
- `README_EN.md`;
- `README_UA.md`;
- `CHANGELOG_v0.2.1_UA.md`;
- `CHANGELOG_v0.2.1_EN.md`.

## Перед розпакуванням

Якщо Windows позначила завантажений ZIP як файл з Інтернету, відкрий його властивості, увімкни `Розблокувати` та натисни `Застосувати`. Після цього розпакуй архів.

## Встановлення

- Клієнт: скопіюй папку `CoopSpectator` у `Mount & Blade II Bannerlord\Modules` із заміною файлів.
- Хост: скопіюй папку `CoopSpectatorDedicated` у `Mount & Blade II Dedicated Server\Modules` із заміною файлів.

На чистому клієнті обов'язково має існувати файл:

- `Modules\CoopSpectator\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll`

## Важливо для сервера

Легкий пакет не містить стандартні модулі `SandBox` і `SandBoxCore` через їхній великий розмір. Для коректного запуску сервера скопіюй ці дві папки з `Modules` встановленої гри до `Modules` виділеного сервера із заміною файлів.

Після встановлення на хості мають існувати:

- `Modules\CoopSpectatorDedicated\SubModule.xml`;
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`;
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`;
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`;
- `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`;
- `Modules\SandBox\SubModule.xml`;
- `Modules\SandBoxCore\SubModule.xml`.
