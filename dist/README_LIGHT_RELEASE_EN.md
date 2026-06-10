# Mount & Blade II: Bannerlord - Coop Campaign (Light Release)

Optimized for `Mount & Blade II: Bannerlord v1.4.5 (Steam build 23524942)` and `Mount & Blade II Dedicated Server v1.4.5 (Steam build 23232800)`.

This archive contains only:

- `CoopSpectator`
- `CoopSpectatorDedicated`
- `README_EN.md`
- `README_UA.md`
- `CHANGELOG_v0.1.2.md`

## Install

- Client: copy `CoopSpectator` into `Mount & Blade II Bannerlord\Modules`.
- Host: copy `CoopSpectatorDedicated` into `Mount & Blade II Dedicated Server\Modules`.

## Important

This light package does not include `SandBox` or `SandBoxCore`.

`BannerlordCoopCampaign_v0.1.2_HostLarge.zip` was removed from this release because the recent patch increased the size of the base game modules.

To start the server correctly, also copy `SandBox` and `SandBoxCore` from the game's `Modules` folder into the dedicated server `Modules` folder and allow overwrite.

Expected host paths after copy:

- `Modules\CoopSpectatorDedicated\SubModule.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`
- `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`
- `Modules\SandBox\SubModule.xml`
- `Modules\SandBoxCore\SubModule.xml`
