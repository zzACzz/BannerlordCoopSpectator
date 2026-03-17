# Build Runbook

## Client build
```powershell
dotnet build CoopSpectator.csproj -c Release
```

## Dedicated build (recommended)
```powershell
dotnet build DedicatedServer\CoopSpectatorDedicated.csproj /p:UseDedicatedServerRefs=true /p:DedicatedServerRootDir="C:\Program Files (x86)\Mount & Blade II Dedicated Server"
```

## Fast deploy checks
1. Переконатися, що модуль скопійований у `Modules\CoopSpectator` (і dedicated module, якщо використовується).
2. Переконатися, що потрібні DLL реально доступні в runtime profile.
3. Перезапустити клієнт/сервер після оновлення бінарників.

## Fast dev loop
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1
```

Що робить за замовчуванням:
1. Білдить `CoopSpectator.csproj`.
2. Білдить `DedicatedServer\CoopSpectatorDedicated.csproj` з `UseDedicatedServerRefs=true`.
3. Показує timestamps deployed DLL.
4. Перевіряє найсвіжіші client/dedicated `rgl_log_*.txt` на ключові маркери.

Корисні режими:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1 -RestartDedicated
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1 -LaunchClient
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1 -RestartDedicated -LaunchClient
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1 -RestartDedicated -RestartClient
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1 -CheckLogs
```

`-LaunchClient` стартує Bannerlord одразу в multiplayer з модулем `CoopSpectator`.
`-RestartClient` спершу завершує поточний `Bannerlord.exe`, потім запускає заново.

Поточні маркери для spawn-handshake:
- `requested vanilla agent visuals before direct spawn`
- `awaiting agent visuals`
- `spawn agent ownership finalized`
- `SpawnFromVisuals=True`
- `HadVisuals=True`

## Runtime smoke test
1. Host: campaign -> `coop.dedicated_start`.
2. Client: запуск MP з модом, join через Custom Server List.
3. Запустити бій і перевірити перехід у місію.
4. Завершити бій і перевірити повернення (`end_mission`).
5. Повторити цикл 3 рази.

## Must-have log markers
- Build marker/version identity.
- Game mode registration result.
- Mission enter/exit.
- Agent assigned/removed.
- Critical exception traces.

## If build/runtime mismatch suspected
- Звірити compile references.
- Звірити runtime loaded assembly paths.
- Зробити rebuild dedicated з `UseDedicatedServerRefs=true`.
