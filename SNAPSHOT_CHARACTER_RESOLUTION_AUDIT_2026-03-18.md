# Snapshot Character Resolution Audit

Date: 2026-03-18

## Goal

Understand why the current AI army materialization layer does not produce campaign-like army identities, and where the runtime falls back to generic multiplayer-safe troops.

## Short conclusion

The current loss of troop identity begins at snapshot creation time, not at server-side materialization.

Current pipeline:

1. `BattleDetector` builds a battle snapshot on the host.
2. During snapshot build, campaign troops are intentionally rewritten into mission-safe / multiplayer-safe ids via `GetMissionSafeCharacterId(...)`.
3. That rewritten snapshot is written into `battle_roster.json`.
4. Dedicated reads that snapshot and builds `BattleSnapshotRuntimeState`.
5. Materialization tries to resolve `entry.CharacterId` directly through `MBObjectManager`.
6. If that object is not loaded, it falls back again to guaranteed MP-safe proxy ids.

So the current contract is already MP-safe by design. It is not a lossless campaign troop snapshot.

## Key findings

### 1. Snapshot ids are intentionally rewritten before they ever reach dedicated

In [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs), both:

- `BuildPartyTroopStacksSafe()`
- `BuildTroopStacksFromPartySafe(...)`

write:

- `stack.CharacterId = GetMissionSafeCharacterId(...)`

That means snapshot `CharacterId` is not necessarily the original campaign troop id.

Important references:

- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L303)
- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L557)

### 2. `GetMissionSafeCharacterId(...)` is explicitly MP-oriented

`GetMissionSafeCharacterId(...)` first tries:

- `TryResolveMultiplayerSafeCharacterId(...)`

That method maps by:

- culture
- mounted/ranged
- tier

and prefers ids like:

- `mp_coop_light_cavalry_<culture>_troop`
- `mp_coop_heavy_infantry_empire_troop`
- regular MP troops like `mp_heavy_infantry_<culture>_troop`

Important references:

- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L703)
- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L833)
- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L892)

This is the core reason current army materialization tends to look like multiplayer-template troops instead of true campaign troops.

### 3. Hero handling also collapses into surrogates/fallbacks

For heroes, the host snapshot does not preserve unique hero identity as a first-class runtime-spawnable entity.

`GetMissionSafeCharacterId(...)` for heroes tries, in order:

- multiplayer-safe surrogate
- roster surrogate
- `OriginalCharacter`
- `Template`
- culture fallback
- guaranteed vanilla fallback

Important references:

- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L716)
- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L765)
- [BattleDetector.cs](C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs#L955)

So current snapshot is not suitable as a future exact hero/companion/lord spawn contract.

### 4. Dedicated-side `TryResolveCharacterObject(entryId)` is shallow

In [BattleSnapshotRuntimeState.cs](C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs#L269), `TryResolveCharacterObject(entryId)` only does:

- get entry by `EntryId`
- `MBObjectManager.Instance.GetObject<BasicCharacterObject>(entry.CharacterId)`

There is no secondary resolution strategy there.

This means dedicated can only resolve whatever `CharacterId` is already valid and loaded in MP runtime.

### 5. Materialization falls back again if direct resolution fails

AI army materialization currently does:

- `BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId)`
- else `ResolveAllowedCharacter(entryState.CharacterId)`

Reference:

- [CoopMissionBehaviors.cs](C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L3920)

`ResolveAllowedCharacter(...)` itself:

- tries `MBObjectManager.Instance.GetObject<BasicCharacterObject>(troopId)`
- then falls back to `TryResolveGuaranteedMissionSafeTroopId(...)`

References:

- [CoopMissionBehaviors.cs](C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L3451)
- [CoopMissionBehaviors.cs](C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L3482)

This second fallback is why unresolved ids often collapse to generic proxies like:

- `mp_light_cavalry_*`
- `mp_heavy_infantry_*`
- worst case `imperial_infantryman`

### 6. `battle_roster.json` confirms the contract is already MP-safe

The current live file at:

- `C:\Users\Admin\OneDrive\Documents\Mount and Blade II Bannerlord\CoopSpectator\battle_roster.json`

contains ids such as:

- `mp_coop_light_cavalry_sturgia_troop`
- `mp_heavy_infantry_empire_troop`

and not raw campaign ids like `imperial_recruit` or similar.

It also showed a fallback-shaped snapshot:

- `BattleId = "fallback"`
- only one side in the file
- hero `Yorig` mapped to `mp_coop_light_cavalry_sturgia_troop`

This confirms two things:

1. The snapshot contract is mission-safe/proxy-oriented.
2. At least in that captured file, the host was not exporting a full live battle-side snapshot, but the fallback path.

## Implications

### For current stable runtime

This is fine for:

- coop authority
- side selection
- limited allowed troop selection
- stable vanilla player spawn/respawn

It is not enough for:

- authentic campaign-like army materialization
- preserving true troop identity
- future hero/companion/lord fidelity

### For “normal army spawn”

If the goal is a real campaign-looking AI army layer, materialization should not continue relying on the current `CharacterId` as if it were a lossless battle identity.

Right now `CharacterId` is more like:

- “mission-safe spawn template hint”

than:

- “true campaign troop identity”

## Recommended next steps

### Option A: Keep current snapshot, add explicit spawn-template fields

Best for incremental progress.

Add to snapshot entry DTO:

- `OriginalCharacterId`
- `MissionSafeCharacterId`
- optional `SpawnTemplateId`
- optional `CultureId`
- `IsHero`
- `Tier`
- `Mounted`

Then:

- UI / authority can keep working with mission-safe data
- future army materialization can choose between:
  - authentic identity metadata
  - mission-safe runtime template

This avoids overloading one field for two incompatible meanings.

### Option B: Make snapshot preserve true campaign ids in `CharacterId`

Cleaner domain model, but riskier now.

Then add a separate resolver on dedicated:

- `CampaignCharacterId -> mission-safe spawn template`

This is architecturally cleaner, but it touches more of the current pipeline and is more likely to destabilize the already working spawn flow.

## Recommendation

Do not change the stable player spawn path now.

Next implementation step should be:

1. Keep stable vanilla player spawn untouched.
2. Extend snapshot schema so each entry carries both:
   - true campaign identity
   - mission-safe spawn template identity
3. Rebuild AI army materialization to prefer the explicit mission-safe template field, instead of pretending current `CharacterId` is the original troop identity.

That gives a much better foundation for:

- normal AI army spawn
- later commander/army control
- later hero/companion/lord support

without regressing the stable player spawn runtime.
