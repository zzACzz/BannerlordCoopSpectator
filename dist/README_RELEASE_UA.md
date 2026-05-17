# Mount & Blade II: Bannerlord - Coop Campaign

У цьому архіві тепер є тільки:

- `Client`
- `Host`
- `README_EN.md`
- `README_UA.md`

## Для чого кожна папка

- `Client`:
  Скопіюй увесь вміст цієї папки в корінь `Mount & Blade II Bannerlord`.
  Це встановлення для всіх, хто хоче грати або приєднуватися до боїв.

- `Host`:
  Скопіюй увесь вміст цієї папки в корінь `Mount & Blade II Dedicated Server`.
  Це встановлення для official dedicated server tool.
  Не копіюй тільки `Modules\CoopSpectatorDedicated`; host-пакет також містить потрібні battle scene assets.

Якщо ти є хостом і граєш на тому ж ПК, встанови обидві папки:

1. `Client` у корінь гри Bannerlord
2. `Host` у корінь Dedicated Server

## Що має бути після копіювання

Після копіювання `Client` має існувати такий шлях:

- `Modules\CoopSpectator\SubModule.xml`

Після копіювання `Host` має існувати такий шлях:

- `Modules\CoopSpectatorDedicated\SubModule.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`
- `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`
- `Modules\SandBox\ModuleData\sp_battle_scenes.xml`
- `Modules\SandBoxCore\SceneObj\battle_terrain_001\scene.xscene`

## Що потрібно хосту

- Steam tool: `Mount & Blade II Dedicated Server`
- Токен custom server, згенерований у Bannerlord multiplayer командою `customserver.gettoken`
- Для режиму `VPN/Overlay`: твоя IP-адреса Radmin/Tailscale/ZeroTier

## Нотатки по хостингу

- Режим `Public` використовує звичайний listed/public server flow.
- Режим `VPN/Overlay` вимагає ввести overlay IP хоста в coop-меню.
- Remote VPN клієнту зараз також треба один раз відкрити coop-меню в кампанії, переключити режим на `VPN/Overlay`, ввести IP хоста, закрити меню, а потім приєднатися через multiplayer.

## Support the project 🙌

The mod is free and will always remain free.

If you enjoy it and want to support further development (new features, stability, siege battles), you can do it here:

👉 https://ko-fi.com/zaczua

Every bit of support helps improve the mod.
