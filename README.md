# Bannerlord Coop Spectator (Foundation прототип)

Мінімальний прототип моду для **Mount & Blade II: Bannerlord 1.3.14 (Steam)**.

На цьому етапі мод робить дві речі:
- показує повідомлення **`CoopSpectator mod loaded!`** після старту кампанії
- додає консольні команди для TCP тесту: `coop.host`, `coop.join`, `coop.send`, `coop.status`

---

## Вимоги
- Bannerlord **v1.3.14** (Steam)
- Visual Studio (у тебе встановлено Visual Studio 2026) + **.NET Framework 4.7.2 Targeting Pack**

---

## Збірка (Build)

### 1) Зібрати через MSBuild (PowerShell)

Запусти в корені репозиторію:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' .\CoopSpectator.csproj /t:Restore /p:Configuration=Release
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' .\CoopSpectator.csproj /t:Build   /p:Configuration=Release
```

Після цього DLL буде тут:
- `Module\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll`

> Примітка: проєкт за замовчуванням референсить `TaleWorlds.*.dll` **з папки гри**, щоб точно відповідати версії 1.3.14. Якщо треба — шлях можна перевизначити:
>
> `... /p:BannerlordRootDir="D:\Steam\steamapps\common\Mount & Blade II Bannerlord"`

---

## Встановлення в гру (Install)

Скопіюй папку:
- `Module\CoopSpectator`

у:
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\CoopSpectator`

У Launcher увімкни мод **CoopSpectator**.

---

## Тест (2 копії гри на одному ПК)

Твій робочий спосіб:
- **Хост**: запусти першу гру через **Steam**
- **Клієнт**: запусти другу гру через ярлик **`Launcher.Native.exe`**

У двох інстансах:
1) Запусти/завантаж кампанію
2) Відкрий консоль
3) Перевір базову команду:
   - `coop.status`

### Хост
```text
coop.host 7777
```

### Клієнт (на тому ж ПК)
```text
coop.join 127.0.0.1 7777
```

### Перевірка повідомлень
```text
coop.send hello
```

Очікування:
- в UI має з’явитися щось на кшталт `NET: MSG:hello`

---

## Команди
- `coop.status` — показати роль і стан
- `coop.host [port]` — підняти TCP сервер
- `coop.join <ip> [port]` — підключитись до хоста
- `coop.send <message...>` — відправити повідомлення (клієнт → хост) або broadcast (хост → клієнти)

