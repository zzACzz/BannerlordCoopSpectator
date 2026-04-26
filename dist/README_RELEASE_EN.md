# Mount & Blade II: Bannerlord - Coop Campaign

This archive now contains only:

- `Client`
- `Host`
- `README_EN.md`
- `README_UA.md`

## What each folder is for

- `Client`:
  Copy everything from this folder into the root of `Mount & Blade II Bannerlord`.
  This is the install for anyone who wants to play or join battles.

- `Host`:
  Copy everything from this folder into the root of `Mount & Blade II Dedicated Server`.
  This is the install for the official dedicated server tool.
  Do not copy only `Modules\CoopSpectatorDedicated`; the host package also carries required battle scene assets.

If you are the host player on the same PC, install both:

1. `Client` into the Bannerlord game root
2. `Host` into the Dedicated Server root

## Expected result after copy

After copying `Client`, this path should exist:

- `Modules\CoopSpectator\SubModule.xml`

After copying `Host`, this path should exist:

- `Modules\CoopSpectatorDedicated\SubModule.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`
- `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`
- `Modules\SandBox\ModuleData\sp_battle_scenes.xml`
- `Modules\SandBoxCore\SceneObj\battle_terrain_001\scene.xscene`

## Host requirements

- Steam tool: `Mount & Blade II Dedicated Server`
- A custom server token generated in Bannerlord multiplayer with `customserver.gettoken`
- For `VPN/Overlay` mode: your Radmin/Tailscale/ZeroTier IP

## Hosting notes

- `Public` mode uses the normal listed/public server flow.
- `VPN/Overlay` mode requires entering the host overlay IP in the coop menu.
- Remote VPN clients currently also need to open the coop menu once in campaign, switch to `VPN/Overlay`, enter the host IP, close the menu, then join through multiplayer.

## Support the project 🙌

The mod is free and will always remain free.

If you enjoy it and want to support further development (new features, stability, siege battles), you can do it here:

👉 https://ko-fi.com/zaczua

Every bit of support helps improve the mod.
