# Mount & Blade II: Bannerlord - Coop Campaign (Light Release)

Мод оптимізовано під `Mount & Blade II: Bannerlord v1.4.5 (Steam build 23524942)` та `Mount & Blade II Dedicated Server v1.4.5 (Steam build 23232800)`.

У цьому легкому архіві є тільки:

- `CoopSpectator`
- `CoopSpectatorDedicated`
- `README_EN.md`
- `README_UA.md`
- `CHANGELOG_v0.1.2.md`

## Встановлення

- Клієнт: скопіюй `CoopSpectator` у `Mount & Blade II Bannerlord\Modules`.
- Хост: скопіюй `CoopSpectatorDedicated` у `Mount & Blade II Dedicated Server\Modules`.

## Важливо

Цей легкий пакет не містить `SandBox` і `SandBoxCore`.

`BannerlordCoopCampaign_v0.1.2_HostLarge.zip` прибрано з цього релізу через збільшення розміру стандартних модулів після недавнього патчу.

Для коректного запуску сервера також скопіюй `SandBox` та `SandBoxCore` з папки `Modules` гри до папки `Modules` виділеного сервера з заміною.

Після копіювання на хості мають існувати такі шляхи:

- `Modules\CoopSpectatorDedicated\SubModule.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`
- `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`
- `Modules\SandBox\SubModule.xml`
- `Modules\SandBoxCore\SubModule.xml`
