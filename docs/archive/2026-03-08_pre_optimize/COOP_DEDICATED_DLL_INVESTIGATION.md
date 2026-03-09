# Технічне розслідування: CoopSpectatorDedicated / CoopSpectator — DLL при збірці та runtime

## Висновок

**Сервер зараз збирається проти DLL клієнта (часто новіших, напр. 110062), а під час запуску Dedicated Server завантажуються DLL з інсталя Dedicated Server (напр. 109797).** Це пояснює MissingMethodException у Harmony-патчах (GameModeOverridePatches.Apply, DedicatedWebPanelPatches.Apply) та попередження про client/server build mismatch. Щоб уникнути цього, потрібно збирати мод для dedicated проти DLL з інсталя Dedicated Server (див. нижче).

---

## Проблемні місця та зміни

### 1. CoopSpectatorDedicated збирався тільки проти клієнтських DLL

- **Файл:** `DedicatedServer/CoopSpectatorDedicated.csproj`
- **Суть:** `BannerlordRootDir` і `BannerlordBinDir` вказують на **клієнт** (Mount & Blade II Bannerlord). Референси TaleWorlds.* бралися з `$(BannerlordBinDir)` = `...\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client`. DedicatedServerRootDir використовувався лише для деплою, не для компіляції.
- **Зміни:**
  - Додано `DedicatedServerBinDir`, `_UseDedicatedRefs`, `_RefBinDir`, `_RefMultiplayerDir` (рядки ~19–26).
  - У блоці референсів при `UseGameDlls=='true'` замість `$(BannerlordBinDir)` та `$(MultiplayerModuleBinDir)` використовуються `$(_RefBinDir)` та `$(_RefMultiplayerDir)`.
  - Якщо передати `/p:UseDedicatedServerRefs=true` і вказати коректний `DedicatedServerRootDir`, збірка йде проти `$(DedicatedServerRootDir)\bin\Win64_Shipping_Server` та Multiplayer з Dedicated Server.

### 2. Відсутня runtime-діагностика завантажених assembly

- **Файл:** `Infrastructure/AssemblyDiagnostics.cs` (новий)
- **Що робить:** логує `AppContext.BaseDirectory`, `Process.MainModule.FileName`, для ключових assembly (TaleWorlds.MountAndBlade, TaleWorlds.Core, TaleWorlds.Library, 0Harmony, виконувана збірка) — FullName, Location, AssemblyVersion, FileVersion, ProductVersion, MVID, LastWriteTimeUtc. Також виводить ApplicationVersion (runtime), якщо доступно. `WarnIfAssemblyPathUnexpected()` логує ERROR, якщо процес Dedicated Server, а TaleWorlds.* завантажена з шляху клієнта.
- **Виклики:** у `DedicatedServer/SubModule.cs` (OnSubModuleLoad) та у клієнтському `SubModule.cs` (OnSubModuleLoad) одразу після base.OnSubModuleLoad().

### 3. Відсутня compile-time діагностика resolved references

- **Файл:** `DedicatedServer/CoopSpectatorDedicated.csproj`
- **Зміни:** додано target `DiagnoseResolvedReferences` (AfterTargets="ResolveAssemblyReferences"): у build-лозі виводяться повні шляхи для TaleWorlds.* та 0Harmony/HarmonyLib з `@(ReferencePath)`, а також `UseGameDlls`, `UseDedicatedServerRefs`, `_RefBinDir`, `_RefMultiplayerDir`.

### 4. Copy Local / Private

- У всіх референсах TaleWorlds.* і Newtonsoft.Json вже вказано `<Private>false</Private>`, тобто мод не копіює DLL гри в свою output-папку — це коректно.

### 5. Fail-fast / попередження

- **Runtime:** `AssemblyDiagnostics.WarnIfAssemblyPathUnexpected()` при запуску (dedicated і клієнт) перевіряє: якщо процес визначено як Dedicated, але якась TaleWorlds.* завантажена з шляху, що містить "Bannerlord" (клієнт), у лог пишеться ERROR.
- **Build mismatch:** існуюче попередження про client/server build mismatch залишається; ApplicationVersion тепер логується в AssemblyDiagnostics, щоб можна було порівняти версії в логах.

---

## Як збирати Dedicated проти DLL Dedicated Server

Щоб compile-time відповідав runtime (одна версія TaleWorlds на сервері):

```bat
dotnet build DedicatedServer\CoopSpectatorDedicated.csproj ^
  /p:UseDedicatedServerRefs=true ^
  /p:DedicatedServerRootDir="C:\Program Files (x86)\Mount & Blade II Dedicated Server"
```

Переконайтеся, що в `DedicatedServerRootDir\bin\Win64_Shipping_Server` є `TaleWorlds.MountAndBlade.dll` (типова структура Dedicated Server). Якщо у вас інший шлях (наприклад, 64ShippingServer), вкажіть корінь інсталя Dedicated Server у `DedicatedServerRootDir`; папка bin має називатися `Win64_Shipping_Server` за замовчуванням.

---

## Файли та рядки змін (коротко)

| Що змінено | Файл | Дія |
|------------|------|-----|
| Runtime діагностика | `Infrastructure/AssemblyDiagnostics.cs` | Новий файл: LogRuntimeLoadPaths, WarnIfAssemblyPathUnexpected |
| Виклик діагностики (dedicated) | `DedicatedServer/SubModule.cs` | Після proof-of-load виклик AssemblyDiagnostics.LogRuntimeLoadPaths + WarnIfAssemblyPathUnexpected |
| Виклик діагностики (клієнт) | `SubModule.cs` | Після base.OnSubModuleLoad виклик тих самих методів |
| Компіляція AssemblyDiagnostics у dedicated | `DedicatedServer/CoopSpectatorDedicated.csproj` | Compile Include ..\Infrastructure\AssemblyDiagnostics.cs |
| Опція збірки проти Dedicated Server | `DedicatedServer/CoopSpectatorDedicated.csproj` | PropertyGroup: DedicatedServerBinDir, _UseDedicatedRefs, _RefBinDir, _RefMultiplayerDir; референси через $(_RefBinDir)/$(_RefMultiplayerDir) |
| Compile-time діагностика | `DedicatedServer/CoopSpectatorDedicated.csproj` | Target DiagnoseResolvedReferences після ResolveAssemblyReferences |

---

## Чи сервер збирається/працює на старих DLL?

- **Runtime:** Dedicated Server процес завантажує DLL з **своєї** інсталі (напр. 109797). Це не "старі" в сенсі репозиторію — це просто інша (часто старіша) версія порівняно з клієнтом.
- **Compile-time (без змін):** мод збирався проти DLL **клієнта** (напр. 110062). Тобто нестачі методів під час виконання на сервері — наслідок саме цього невідповідності версій (compile vs runtime).
- **Після змін:** при збірці з `/p:UseDedicatedServerRefs=true` і правильним `DedicatedServerRootDir` мод компілюється проти тих самих DLL, які під час роботи завантажує Dedicated Server; це усуває причину MissingMethodException за умови однакової версії Dedicated Server і клієнта (або прийнятної сумісності).
