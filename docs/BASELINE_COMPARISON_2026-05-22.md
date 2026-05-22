# Baseline Comparison 2026-05-22

## Stable reconnect baseline

- Branch: `dev`
- Commit: `b392f5b`
- Goal: last known baseline where active-battle reconnect handoff already worked
- Intentionally absent:
  - speed fixes
  - companion immediate-spawn fixes
  - runtime quiesce experiments
  - hosted runtime / order-controller diagnostics

## Experiment checkpoint

- Branch: `codex/hosted-runtime-audit-checkpoint-2026-05-22`
- Commit: `248714e`
- Goal: preserve the full post-reconnect investigation state
- Includes:
  - 1.4.5 compatibility changes
  - speed/stat experiments
  - exact pre-spawn / ordinary troop spawn experiments
  - UI popup/list fixes
  - official runtime quiesce patches
  - order-controller and hosted-runtime diagnostics

## Comparison plan

Use `b392f5b` as the gameplay baseline and port only the minimum compatibility layer required by the new `1.4.5.114896` client/server runtime. Compare that against `248714e` only when a bug clearly appears after the reconnect baseline.

## Minimum compatibility layer for the old baseline

These are the files that may legitimately differ from raw `b392f5b` only because the game/server API changed:

- [C:\dev\projects\BannerlordCoopSpectator3\CoopSpectator.csproj](C:/dev/projects/BannerlordCoopSpectator3/CoopSpectator.csproj)
- [C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\CoopSpectatorDedicated.csproj](C:/dev/projects/BannerlordCoopSpectator3/DedicatedServer/CoopSpectatorDedicated.csproj)
- [C:\dev\projects\BannerlordCoopSpectator3\MissionModels\CoopCampaignDerivedStrikeMagnitudeCalculationModel.cs](C:/dev/projects/BannerlordCoopSpectator3/MissionModels/CoopCampaignDerivedStrikeMagnitudeCalculationModel.cs)
- [C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactCampaignArmyBootstrap.cs](C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignArmyBootstrap.cs)
- [C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\BattleMapContractDiagnostics.cs](C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleMapContractDiagnostics.cs)
- [C:\dev\projects\BannerlordCoopSpectator3\Campaign\BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs)
