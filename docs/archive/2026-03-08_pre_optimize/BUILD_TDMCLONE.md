# Live-test build для TdmClone (клієнт)

## Перевірені шляхи на цій машині

| Що | Шлях | Результат |
|----|------|-----------|
| **Інсталяція гри (клієнт)** | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` | Знайдено |
| **Клієнтський bin** | `…\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client` | Існує |
| **Клієнтська Multiplayer.dll (root bin)** | `…\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll` | Не в root bin (там лише `TaleWorlds.MountAndBlade.Multiplayer.Test.dll`) |
| **Клієнтська Multiplayer.dll (модуль)** | `…\Modules\Multiplayer\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll` | **Є** — CoopSpectator.csproj тепер підставляє цей шлях автоматично (HasGameModeDll) |
| **Dedicated Server** | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server` | Знайдено; `TaleWorlds.MountAndBlade.Multiplayer.dll` є в `bin\Win64_Shipping_Server` (це **server-side** варіант — для клієнтського build не використовувати). |

Інші перевірені місця: `C:\Program Files\Steam\steamapps\common`, `D:\SteamLibrary\steamapps\common` — папки Bannerlord там не знайдені.

## Як отримати TdmClone build (клієнт)

1. **BannerlordRootDir** у `CoopSpectator.csproj` за замовчуванням вже вказаний на клієнтську інсталяцію Steam:
   - `BannerlordRootDir = C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord`
   - Якщо гра стоїть в іншому місці — передайте збірці:  
     `dotnet build CoopSpectator.csproj -c Release /p:BannerlordRootDir="D:\Games\Mount & Blade II Bannerlord"`

2. **Умова HAS_GAMEMODE / GameMode в Compile:**  
   Потрібна саме клієнтська DLL:  
   `TaleWorlds.MountAndBlade.Multiplayer.dll`  
   у одному з місць:
   - **Варіант A:** у клієнтській папці гри:  
     `$(BannerlordRootDir)\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll`
   - **Варіант B:** у корені проєкту:  
     `c:\dev\projects\BannerlordCoopSpectator3\TaleWorlds.MountAndBlade.Multiplayer.dll`  
     (скопіюйте з інсталяції, де ця DLL є в `bin\Win64_Shipping_Client`).

3. Після того як DLL доступна (A або B), зберіть клієнт:  
   `dotnet build CoopSpectator.csproj -c Release`  
   У логах збірки не має бути помилок; у runtime у `rgl_log` мають з’явитися рядки:
   - `[CoopSpectator] HAS_GAMEMODE=true (build with TdmClone support).`
   - `[CoopSpectator] TdmClone client registration start.`
   - `[CoopSpectator] TdmClone client registration success (ready for joining TdmClone servers).`

## Runtime-лог (підтвердження типу збірки)

При запуску гри з модулем у лог потрапляє один з двох варіантів:

- **Без TdmClone:**  
  `[CoopSpectator] HAS_GAMEMODE=false (build without TdmClone; campaign + listed dedicated only).`
- **З TdmClone:**  
  `[CoopSpectator] HAS_GAMEMODE=true (build with TdmClone support).`  
  далі `TdmClone client registration start` та `success` або `fail`.

Це дозволяє по `rgl_log` перевірити, що клієнт зібраний і запущений у TdmClone-capable build.
