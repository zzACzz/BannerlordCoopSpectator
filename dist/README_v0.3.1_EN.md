# BannerlordCoopCampaign v0.3.1 — Installation Guide

## Client Installation

1. Right-click the archive `BannerlordCoopCampaign_v0.3.1_Client.zip`.
2. Open **Properties**.
3. Check if there is a Security message at the bottom saying: "This file came from another computer and might be blocked to help protect this computer."
4. If the message exists, click **Unblock**.
5. Click **Apply**.
6. Extract the archive into your `Mount & Blade II Bannerlord` game folder.

## Host Installation

1. Right-click the archive `BannerlordCoopCampaign_v0.3.1_Host.zip`.
2. Open **Properties**.
3. Check if there is a Security message at the bottom saying: "This file came from another computer and might be blocked to help protect this computer."
4. If the message exists, click **Unblock**.
5. Click **Apply**.
6. Extract the archive into the root folder of `Mount & Blade II Dedicated Server`.
7. Open your `Mount & Blade II Bannerlord` game folder.
8. Open the `Modules` folder.
9. Copy the `SandBox` and `SandBoxCore` folders.
10. Open the `Mount & Blade II Dedicated Server` folder.
11. Open the `Modules` folder.
12. Paste `SandBox` and `SandBoxCore`.

## Connecting to a Public Server

1. Run `run_mp_with_mod_from_game_root.bat` from the game root folder.
2. Open the **Play** tab.
3. Open **Custom Server List**.
4. Join the server.

## Connecting to a VPN Server

1. Launch the campaign with the coop mod enabled. Make sure the mod is placed at the bottom of the load order.
2. Start a campaign or load a save.
3. Open the in-game menu.
4. Open the **Coop Dedicated Server** tab.
5. Press the **VPN/Overlay** button.
6. Enter the host VPN IP address below the button.
7. Close the campaign. The server IP is saved and does not need to be entered every launch.
8. Run `run_mp_with_mod_from_game_root.bat`.
9. Open the **Play** tab.
10. Open **Custom Server List**.
11. Join the server.

## Creating a VPN Server

1. Launch multiplayer.
2. Open the multiplayer console using `Alt + ~`.
3. Enter `customserver.gettoken`. This is required for creating a dedicated server.
4. Exit multiplayer.
5. Launch the campaign with the coop mod enabled. Make sure the mod is placed at the bottom of the load order.
6. Start a campaign or load a save.
7. Open the in-game menu.
8. Open the **Coop Dedicated Server** tab.
9. Press the **VPN/Overlay** button.
10. Enter your VPN network IP address.
11. Configure the server name, password, admin password, and maximum player count.
12. Press **Start Server**. A dedicated server console will appear and launch the coop battle server.

Campaign battles will be recreated on the dedicated server, and their results will be transferred back into the campaign.

## Starting a Public Server

1. Launch the campaign with the coop mod enabled.
2. Open **Coop Dedicated Server**.
3. Select **Public**.
4. Configure the server name, optional password, admin password, and maximum player count.
5. Click **Start Server**.
