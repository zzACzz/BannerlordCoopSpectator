# Bolt Mannequin Attach Guardrail

## Context

During the `codex/v0.1.1-refresh` investigation on June 4, 2026, we introduced server-side Harmony prefixes on:

- `Agent.AttachWeaponToBone`
- `Agent.AttachWeaponToWeapon`
- `Mission.HandleMissileCollisionReaction`

The goal was to suppress bolt stick visuals after the new game patch started producing the server assert:

- `get_item_usage_get_index_with_id failed`

## What went wrong

The `Agent.AttachWeaponToBone` and `Agent.AttachWeaponToWeapon` hooks were too low-level and too global.

Evidence:

- Campaign client startup log:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_66884.txt`
  - shows `prefix applied to Agent.AttachWeaponToWeapon`
  - shows `prefix applied to Agent.AttachWeaponToBone`
- Multiplayer client startup log:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_53200.txt`
  - shows the same two prefixes applied in MP runtime

These methods are not exclusive to battlefield missile stick handling. They also participate in mannequin / preview / attach visual pipelines used by:

- campaign save/load screens
- loot screen
- party screen
- multiplayer result / TDM-style UI mannequins

As a result, patching those two `Agent` methods broke mannequins across unrelated game screens.

## Guardrail

Do **not** patch these methods again as a first-line fix for bolt crashes:

- `Agent.AttachWeaponToBone`
- `Agent.AttachWeaponToWeapon`

Treat both as high-risk global engine helpers.

If bolt suppression is needed, prefer narrower battle-only interception points such as:

- `Mission.HandleMissileCollisionReaction`
- or a more specific exact-battle missile lifecycle hook

Do not reintroduce global `Agent.AttachWeaponToBone/Weapon` suppression unless a future investigation proves:

1. the hook is runtime-isolated to battle only
2. mannequin / preview pipelines are explicitly excluded
3. the fix cannot be achieved at a narrower missile-reaction layer

## Low-level lesson

The mistake was not only in the suppression condition. The real mistake was choosing the wrong abstraction layer.

`Agent.AttachWeaponToBone` / `AttachWeaponToWeapon` are engine-wide attach helpers, not battle-only bolt helpers. Even a narrow condition inside those prefixes is dangerous because the hook itself lives on a globally reused path.
