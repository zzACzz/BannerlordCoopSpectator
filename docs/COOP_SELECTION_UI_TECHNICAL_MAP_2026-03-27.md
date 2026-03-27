# Coop Selection UI Technical Map

Date: 2026-03-27
Project: `BannerlordCoopSpectator3`
Focus: safe custom mission UI for coop side/unit selection

## 1. Main finding

Bannerlord multiplayer mission UI is not "just an XML".
The real runtime contract is:

1. `MissionView` owner class
2. `GauntletLayer`
3. `LoadMovie("MovieName", viewModel)`
4. `GUI/Prefabs/<MovieName>.xml`
5. bound `ViewModel` with `DataSourceProperty`
6. existing brushes / sprites / custom widgets used by that XML
7. mission-side behaviors that feed the VM and own open/close state

Because of this, the safest path for our coop UI is:

- keep our own `MissionView`
- keep our own `ViewModel`
- keep our own XML movie
- avoid vanilla `TeamSelect` and `ClassLoadout` VMs/callbacks
- reuse only visual assets and layout ideas from vanilla

## 2. Vanilla mission UI lifecycle

### 2.1 Team selection

Vanilla class:
`TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletTeamSelection`

Assembly:
`Modules\Multiplayer\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.dll`

Important findings from decompilation:

- it derives from `MissionView`
- it initializes in `OnMissionScreenInitialize()`
- it opens the movie only after mission/client state says it is time
- it uses:
  - `MissionNetworkComponent`
  - `MultiplayerTeamSelectComponent`
  - `MissionLobbyComponent`
  - `MissionGauntletMultiplayerScoreboard`
- it creates:
  - `GauntletLayer("MultiplayerTeamSelection", priority, false)`
  - `LoadMovie("MultiplayerTeamSelection", vm)`
- it also uses:
  - `MissionScreen.SetDisplayDialog(true/false)`
  - `MissionScreen.SetCameraLockState(true/false)`
  - `InputRestrictions`

### 2.2 Class loadout

Vanilla class:
`TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletClassLoadout`

Important findings:

- it also derives from `MissionView`
- it initializes in `OnMissionScreenInitialize()`
- it depends on:
  - `MissionLobbyComponent`
  - `MissionLobbyEquipmentNetworkComponent`
  - `MissionMultiplayerGameModeBaseClient`
  - `MultiplayerTeamSelectComponent`
  - `MissionNetworkComponent`
  - `MissionGauntletMultiplayerScoreboard`
  - `MissionRepresentativeBase`
- it delays activation until:
  - client synchronized
  - `MissionPeer.HasSpawnedAgentVisuals == true`
- it creates:
  - `GauntletLayer("MultiplayerClassLoadout", priority, false)`
  - `LoadMovie("MultiplayerClassLoadout", vm)`

## 3. Real file/resource dependencies

### 3.1 Movie XML files

Vanilla movie files:

- `Modules\Multiplayer\GUI\Prefabs\TeamSelection\MultiplayerTeamSelection.xml`
- `Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadout.xml`

Our custom movie file:

- `C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\GUI\Prefabs\CoopSelection.xml`

Important rule:

- movie name in `LoadMovie("...")` must match XML file name
- for our overlay that means `LoadMovie("CoopSelection", vm)` expects `CoopSelection.xml`

### 3.2 Brush files we can safely reuse

Confirmed existing brush names:

- `WideButton.Flat` from `Modules\Native\GUI\Brushes\Brush.xml`
- `MPLobby.PlayButton.Text` from `Modules\Native\GUI\Brushes\MPLobby.xml`
- `MPEndOfRound.Description` from `Modules\Native\GUI\Brushes\MPEndOfRound.xml`
- `MPIntermission.Voting.Title.Text` from `Modules\Native\GUI\Brushes\MPIntermission.xml`
- `MPIntermission.Value.Text` from `Modules\Native\GUI\Brushes\MPIntermission.xml`

Other useful vanilla brush packs:

- `Modules\Native\GUI\Brushes\MPTeamSelection.xml`
- `Modules\Native\GUI\Brushes\MPClassLoadout.xml`
- `Modules\Native\GUI\Brushes\MPLobby.xml`
- `Modules\Native\GUI\Brushes\Mission.xml`
- `Modules\Native\GUI\Brushes\Main.xml`

### 3.3 Sprite / widget dependencies

Vanilla class/team UI uses many extra dependencies:

- sprites like `MPTeamSelection\...`, `MPClassLoadout\...`, `MPHud\...`
- widget types like:
  - `ScrollablePanel`
  - `ScrollbarWidget`
  - `NavigationScopeTargeter`
  - `ImageIdentifierWidget`
  - `FillBar`
  - custom multiplayer widgets

This is important because these are much riskier than plain widgets.

## 4. What is safe vs risky for our custom overlay

### 4.1 Safe first-stage elements

These are relatively safe:

- `Widget`
- `ListPanel`
- `TextWidget`
- `ButtonWidget`
- `ImageWidget`
- simple `Sprite="BlankWhiteSquare_9"`
- known brushes from Native GUI brush XMLs

### 4.2 Riskier elements

These should be introduced only later:

- `ScrollablePanel`
- `ScrollbarWidget`
- nested prefab composition
- fancy faction banner widgets
- custom multiplayer-specific widget types
- heavy `ImageIdentifierWidget` item rendering
- direct reuse of vanilla class-loadout VM assumptions

## 5. Current gap in our custom overlay

Our current overlay already moved in the right direction:

- custom `MissionView`
- custom `CoopSelectionVM`
- custom `CoopSelection.xml`

But it still needs a more vanilla-like runtime shell for stability:

- explicit `MissionScreen.SetDisplayDialog(true/false)`
- explicit `InputRestrictions`
- explicit open/close state instead of "always add layer"
- staged init after mission screen and client state are ready

## 6. Recommended safe implementation plan

### Phase A. Crash isolation shell

Goal:
prove `MissionView + empty GauntletLayer` is stable.

Rules:

- no `LoadMovie`
- no XML movie
- no input ownership yet

### Phase B. Minimal movie

Goal:
prove `LoadMovie("CoopSelection")` works.

Rules:

- very small XML only
- static text
- one close-safe root panel
- no lists
- no scrolling
- no custom widgets

### Phase C. Minimal functional overlay

Goal:
show authoritative coop data without native spawn coupling.

Rules:

- side list
- unit list
- selected unit text
- spawn/reset/start buttons
- only plain widgets and known brushes

### Phase D. Input ownership

Goal:
make overlay behave like a real mission dialog.

Rules:

- add `MissionScreen.SetDisplayDialog(true/false)`
- add `InputRestrictions`
- add clean open/close/focus handling

### Phase E. Visual polish

Goal:
approach TDM look without inheriting TDM behavior.

Rules:

- reuse TDM layout ideas
- selectively reuse team/class brushes
- add images/icons only after core flow is stable

## 7. Architectural recommendation

Do not build the coop selector as a patched vanilla `TeamSelection/ClassLoadout`.

Instead:

- use vanilla `MissionView` lifecycle as the template
- use our own `CoopMissionSelectionView`
- use our own `CoopSelectionVM`
- use our own movie XML
- connect buttons only to our authoritative bridge/runtime paths

Short version:

`reuse visuals, not ownership`

That is the safest route and also the cleanest fit for the longer-term custom coop battle mode.
