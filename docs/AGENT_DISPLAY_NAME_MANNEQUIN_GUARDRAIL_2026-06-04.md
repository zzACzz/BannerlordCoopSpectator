# Agent Display Name Mannequin Guardrail

## What Was Proven

`AgentDisplayNamePatch` is mannequin-hostile even after aggressive narrowing.

The following variants were tested and still regressed campaign/mannequin preview scenes:

- global `Agent.Name` / `Agent.NameTextObject` / `ITrackableBase.GetName`
- narrowed battle-only runtime gate
- narrowed authoritative/origin-only display-name resolver
- removal of `ITrackableBase.GetName`

The common failure point is the same: patching base `Agent` name getters at all.

## Rule

Do not use global Harmony postfix/prefix hooks on:

- `Agent.Name`
- `Agent.NameTextObject`
- `ITrackableBase.GetName`

for coop exact-name correction.

## Why

Vanilla mannequin/preview pipelines call these base getters in non-battle contexts.
Even when the patch body early-returns and produces no exact-name override, the mere
presence of the global getter hook is enough to regress mannequin stability.

## Safe Direction

If exact troop names are needed, resolve them only in explicit coop battle consumers:

- coop battle selection UI
- coop battle shell / VM text producers
- other battle-only UI/runtime paths that already know they are inside coop battle

Do not solve this at the engine-wide `Agent` getter level.
