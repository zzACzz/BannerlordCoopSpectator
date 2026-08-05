# Mount & Blade II: Bannerlord — Coop Campaign (Light Release)

Prepared for `Mount & Blade II: Bannerlord v1.4.7.117484` (Steam build `24127665`) and the matching `Mount & Blade II Dedicated Server` version.

This archive contains only:

- `CoopSpectator` — client module;
- `CoopSpectatorDedicated` — dedicated server module;
- `README_EN.md`;
- `README_UA.md`;
- `CHANGELOG_v0.2.1_EN.md`;
- `CHANGELOG_v0.2.1_UA.md`.

## Before extracting

If Windows marked the downloaded ZIP as a file from the Internet, open its Properties, enable `Unblock`, click `Apply`, and then extract it.

## Install

- Client: copy `CoopSpectator` into `Mount & Blade II Bannerlord\Modules` and allow overwrite.
- Host: copy `CoopSpectatorDedicated` into `Mount & Blade II Dedicated Server\Modules` and allow overwrite.

The following file must exist on every clean client:

- `Modules\CoopSpectator\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll`

## Important for the server

This light package does not include the base-game `SandBox` and `SandBoxCore` modules because of their size. Copy both folders from the installed game's `Modules` folder into the dedicated server's `Modules` folder and allow overwrite.

Expected host paths after installation:

- `Modules\CoopSpectatorDedicated\SubModule.xml`;
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`;
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`;
- `Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`;
- `Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`;
- `Modules\SandBox\SubModule.xml`;
- `Modules\SandBoxCore\SubModule.xml`.
