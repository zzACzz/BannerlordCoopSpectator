# Dedicated DLL Investigation

## Ключовий висновок
Якщо dedicated runtime використовує одні DLL, а мод скомпільований проти інших, з’являються runtime падіння (часто Harmony/MissingMethod).

## Що зафіксовано
- Історично dedicated-проєкт іноді компілювався проти client DLL.
- Runtime dedicated завантажує DLL зі своєї інсталяції.
- Це створювало compile/runtime mismatch.

## Правильний підхід
Компілювати dedicated мод тільки проти dedicated DLL:
```powershell
dotnet build DedicatedServer\CoopSpectatorDedicated.csproj /p:UseDedicatedServerRefs=true /p:DedicatedServerRootDir="C:\Program Files (x86)\Mount & Blade II Dedicated Server"
```

## Обов’язкова діагностика
- Логувати build marker.
- Логувати шляхи завантажених assembly.
- Логувати версії (`FileVersion`, `MVID`, `ApplicationVersion`).

## Практичне правило
Перед кожним regression test спочатку перевір:
1. Compile refs.
2. Runtime loaded paths.
3. Однаковість build/client/dedicated наборів.
