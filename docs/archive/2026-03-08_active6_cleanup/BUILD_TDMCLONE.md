# Build TdmClone (Client)

## Мета
Отримати клієнтський білд з підтримкою TdmClone.

## Передумови
- Встановлена гра Mount & Blade II: Bannerlord.
- Доступна `TaleWorlds.MountAndBlade.Multiplayer.dll` (client version).
- Налаштований `BannerlordRootDir` у `CoopSpectator.csproj`.

## Команда збірки
```powershell
dotnet build CoopSpectator.csproj -c Release
```

## Перевірка після build
Шукати в логах моду:
- `HAS_GAMEMODE=true`
- `TdmClone client registration start`
- `TdmClone client registration success`

## Якщо `HAS_GAMEMODE=false`
- Перевір шлях до `TaleWorlds.MountAndBlade.Multiplayer.dll`.
- Переконайся, що це саме client DLL.
- Перезбери проєкт з правильним `BannerlordRootDir`.

## Швидкий smoke test
1. Запусти клієнт з модом.
2. Відкрий MP / Custom Server List.
3. Спробуй join на сервер із TdmClone.
4. Перевір відсутність помилки `Cannot find game type`.
