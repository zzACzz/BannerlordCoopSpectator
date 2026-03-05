# Перевірка узгодженості GameTypeId для TdmClone

Якщо в лозі дедика з’являється **"Cannot find game type: TdmClone"**, це означає, що режим не зареєстрований — зазвичай тому, що **DLL модуля не завантажилась** (наприклад, немає поруч `TaleWorlds.MountAndBlade.Multiplayer.dll` у папці модуля).

## Таблиця: однаковий ID у всіх місцях

| Місце | Файл / місце в коді | Очікуване значення | Константа / джерело |
|-------|----------------------|--------------------|----------------------|
| **(a) Реєстрація на дедику** | `DedicatedServer/SubModule.cs` → `AddMultiplayerGameMode(new MissionMultiplayerTdmCloneMode(...))` | `"TdmClone"` | `MissionMultiplayerTdmCloneMode.GameModeId` → `CoopGameModeIds.TdmClone` |
| **(b) Конфіг дедика (стартовий)** | `DedicatedHelperLauncher.TryWriteStartupConfig` → рядок `GameType ...` у `ds_config_coop_start.txt` | `"TdmClone"` | `CoopGameModeIds.TdmClone` |
| **(c) Host (лог при start_mission)** | `DedicatedServerCommands.SendStartMission` — лише лог | `"TdmClone"` | `CoopGameModeIds.TdmClone` |

Єдине джерело правди: **`Infrastructure/CoopGameModeIds.cs`** → `public const string TdmClone = "TdmClone";`

- `MissionMultiplayerTdmCloneMode.GameModeId` повертає `CoopGameModeIds.TdmClone`.
- Конфіг і лог на хостові теж використовують `CoopGameModeIds.TdmClone`.

Усі три значення мають бути однакові; змінювати потрібно лише в `CoopGameModeIds.cs`.

---

## Чому "Cannot find game type: TdmClone"

1. Дедик читає конфіг → `GameType TdmClone` → викликає пошук режиму за іменем `"TdmClone"`.
2. Режим з таким іменем з’являється тільки після виконання `CoopSpectatorDedicated.SubModule.OnSubModuleLoad()` → `AddMultiplayerGameMode(... TdmClone ...)`.
3. Якщо **CoopSpectator.dll** не завантажилась (наприклад, не знайдено `TaleWorlds.MountAndBlade.Multiplayer.dll` у тій самій папці), то `OnSubModuleLoad` не виконується і режим не реєструється → **"Cannot find game type: TdmClone"**.

Що зробити:

- Після збірки деплой копіює **TaleWorlds.MountAndBlade.Multiplayer.dll** з `Dedicated Server\Modules\Multiplayer\bin\Win64_Shipping_Server` (або `...\Win64_Shipping_Client`) у **CoopSpectatorDedicated\bin\Win64_Shipping_Client**.
- Переконайся, що в папці дедика **Mount & Blade II Dedicated Server\Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client** є:
  - `CoopSpectator.dll`
  - `TaleWorlds.MountAndBlade.Multiplayer.dll`
  - при потребі `0Harmony.dll`
- Перезапусти дедик і перевір лог: мають з’явитися повідомлення на кшталт реєстрації режимів (якщо логер виводить у консоль дедика).

---

## Швидка перевірка по логах

Після запуску дедика звіряй:

| Що перевірити | Де дивитися |
|----------------|-------------|
| (a) Реєстрація | Лог дедика при старті: рядок типу `TdmClone registered. [ID check] GameTypeId=TdmClone` (якщо ModLogger виводить у консоль). |
| (b) Конфіг | Файл `Mount & Blade II Dedicated Server\Modules\Native\ds_config_coop_start.txt`: рядок `GameType TdmClone`. |
| (c) Host | Лог гри (хост) при вході в битву: `[ID check] expected GameTypeId on dedicated=TdmClone`. |

У всіх трьох має бути один і той самий рядок **TdmClone**.
