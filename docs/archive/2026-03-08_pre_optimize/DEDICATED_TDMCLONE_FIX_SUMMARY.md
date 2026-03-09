# Підсумок виправлень: Dedicated TdmClone — NullReference в MissionCustomGameServerComponent.AfterStart

## 1. Список змінених файлів

| Файл | Що змінено |
|------|-------------|
| `Infrastructure/AssemblyDiagnostics.cs` | BUILD_MARKER, префікси [DedicatedDiag]/[CoopSpectator], LogBuildAndBinaryId (SERVER_BINARY_ID/CLIENT_BINARY_ID), покращене логування ApplicationVersion |
| `GameMode/MissionBehaviorHelpers.cs` | using Multiplayer, TryCreateMissionScoreboardComponent() — безпечне створення scoreboard з логом при помилці |
| `GameMode/MissionMultiplayerTdmCloneMode.cs` | Серверний stack без CoopMissionClientLogic; scoreboard через TryCreateMissionScoreboardComponent; ValidateServerStackSanity розширено (CoopMissionClientLogic, MissionLobbyEquipmentNetworkComponent, MultiplayerMissionAgentVisualSpawnComponent); префікс [TdmCloneStack] у логах; клієнтський scoreboard теж через TryCreate |
| `DedicatedServer/SubModule.cs` | Прибрано AddMultiplayerGameMode(teamDeathmatchOverride) — лише SetTeamDeathmatchOverride (усунено "same key TeamDeathmatch"); [GameModeReg] логи; [HarmonyFallback] при помилці Apply патчів; [DedicatedDiag] при старті |
| `DedicatedServer/Patches/GameModeOverridePatches.cs` | У catch додано [HarmonyFallback] з patchName, originalTarget, "skipped intentionally, fallback active" |
| `DedicatedServer/Patches/DedicatedWebPanelPatches.cs` | У catch додано [HarmonyFallback] з patchName, targetType, "skipped intentionally, fallback active" |

## 2. Кореневі причини

- **NullReference в MissionCustomGameServerComponent.AfterStart:** на dedicated у server mission behavior stack не було MissionScoreboardComponent; DCS підписується на _missionScoreboardComponent.OnRoundPropertiesChanged, тому _missionScoreboardComponent == null давав краш. **Виправлення:** серверний stack тепер завжди формується через BuildServerMissionBehaviorsForTdmClone з додаванням MissionScoreboardComponent (через TryCreateMissionScoreboardComponent); CoopMissionClientLogic та інші client-only behaviors на сервер не додаються; ValidateServerStackSanity видаляє зайві та при відсутності scoreboard додає його (якщо створення вдалося).
- **Client-only behaviors на server:** у server stack потрапляли MissionMultiplayerTdmCloneClient і (згідно з ТЗ) CoopMissionClientLogic. **Виправлення:** окремий BuildServerMissionBehaviorsForTdmClone без цих типів; sanity check видаляє всі перелічені client-only типи.
- **Duplicate key TeamDeathmatch:** виклик AddMultiplayerGameMode(teamDeathmatchOverride) з ключем "TeamDeathmatch" конфліктував з уже зареєстрованим ванільним TDM. **Виправлення:** реєструємо лише CoopBattle, CoopTdm, TdmClone; для "TeamDeathmatch" використовуємо лише Harmony postfix (SetTeamDeathmatchOverride), без другого Add.
- **Harmony MissingMethodException (ILGenerator.MarkSequencePoint):** на деяких dedicated build Harmony apply падає. **Виправлення:** у SubModule та в самих patch-класах при catch логується [HarmonyFallback] з ім’ям патчу, target і "skipped intentionally, fallback active"; процес не падає, гра працює без цих патчів (GetMultiplayerGameMode без патчу поверне ванільний TDM, якщо не використовувати наш override через інший шлях).
- **Build/version mismatch (109797 vs 110062):** діагностика посилена: BUILD_MARKER, SERVER_BINARY_ID/CLIENT_BINARY_ID (path, FileVersion, MVID, LastWriteUtc), ApplicationVersion у логах з префіксами [DedicatedDiag]/[CoopSpectator].

## 3. Очікувані рядки в логах після виправлень

### Dedicated (rgl_log / консоль)

- `[DedicatedDiag] AppContext.BaseDirectory=...`
- `[DedicatedDiag] Process.MainModule.FileName=...`
- `[DedicatedDiag] BUILD_MARKER=COOP_FIX_2026_03_08_A`
- `[DedicatedDiag] SERVER_BINARY_ID path=... FileVersion=... MVID=... LastWriteUtc=...`
- `[DedicatedDiag] ApplicationVersion (Bannerlord build)=109797` (або актуальна версія)
- `[GameModeReg] add CoopBattle id=...`
- `[GameModeReg] add CoopTdm id=...`
- `[GameModeReg] add TdmClone id=...`
- `[GameModeReg] skip AddMultiplayerGameMode(TeamDeathmatch) — use Harmony override only (avoids same key).`
- `[GameModeReg] Registered: CoopBattle, CoopTdm, TdmClone. TeamDeathmatch handled by Harmony postfix.`
- Якщо Harmony Apply вдалося: `GameModeOverridePatches: GetMultiplayerGameMode postfix applied...`
- Якщо Harmony Apply впав: `[HarmonyFallback] GameModeOverridePatches.Apply failed. patchName=... originalTarget=... skipped intentionally, fallback active. ...`
- При відкритті місії TdmClone:
  - `[TdmCloneStack] CreateBehaviorsForMission final count=... IsServer=True IsDedicated=True`
  - `[TdmCloneStack] HasMissionScoreboardComponent=True HasMissionCustomGameServerComponent=... HasClientOnlyBehaviorOnServer=False`
  - `[TdmCloneStack]   [0] ...` … нумерований список типів behaviors без MissionMultiplayerTdmCloneClient та без CoopMissionClientLogic.

### Клієнт

- `[CoopSpectator] BUILD_MARKER=COOP_FIX_2026_03_08_A`
- `[CoopSpectator] CLIENT_BINARY_ID path=...`
- `[CoopSpectator] ApplicationVersion (Bannerlord build)=110062` (або актуальна версія)

## 4. Що не вдалося повністю виправити / обмеження

- **Guard всередині MissionCustomGameServerComponent:** код DCS належить гри (TaleWorlds.MountAndBlade.DedicatedCustomServer); змінити його неможливо. Захист лише з нашого боку: завжди давати коректний server stack з MissionScoreboardComponent.
- **Harmony MarkSequencePoint на dedicated:** якщо на build 109797 Harmony продовжує падати при Apply — патч просто не застосовується, у логах буде [HarmonyFallback]; GetMultiplayerGameMode("TeamDeathmatch") поверне ванільний режим, якщо підміна не активна. Для повноцінної підміни TdmClone при GameType=TeamDeathmatch потрібен робочий Harmony або однакова версія гри (наприклад 110062) на dedicated.
- **Build mismatch:** клієнт 110062 і dedicated 109797 — різні версії; рекомендовано однаковий build для стабільних тестів і збірка dedicated з `/p:UseDedicatedServerRefs=true` та правильним DedicatedServerRootDir.

## 5. Збірка для тесту

```bat
dotnet build DedicatedServer\CoopSpectatorDedicated.csproj /p:UseDedicatedServerRefs=true /p:DedicatedServerRootDir="C:\Program Files (x86)\Mount & Blade II Dedicated Server"
```

Після збірки перезапустити dedicated і перевірити rgl_log на наявність рядків вище; при старті місії TdmClone — відсутність крашу в AfterStart і наявність HasMissionScoreboardComponent=True, HasClientOnlyBehaviorOnServer=False.
