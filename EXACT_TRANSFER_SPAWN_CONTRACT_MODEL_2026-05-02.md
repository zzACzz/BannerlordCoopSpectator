# Модель Exact Transfer Spawn Contract

Дата: 2026-05-02

## Мета

Зафіксувати формальну модель даних і state machine для нового `hero-first`
adapter path, який переносить campaign-unit у native multiplayer spawn contract
без surrogate primary path і без неявних post-spawn "магічних" repair-шарів.

Цей документ є продовженням:

- [EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md](C:/dev/projects/BannerlordCoopSpectator3/EXACT_TRANSFER_CONTRACT_SPEC_2026-05-02.md)
- [ARCHITECTURE_AUDIT_EXACT_TRANSFER_REMOTE_MOUNTED_HERO_2026-05-01.md](C:/dev/projects/BannerlordCoopSpectator3/ARCHITECTURE_AUDIT_EXACT_TRANSFER_REMOTE_MOUNTED_HERO_2026-05-01.md)
- [VARIANT_2_REFACTOR_PLAN_SERVER_FIRST_EXACT_TRANSFER_2026-05-01.md](C:/dev/projects/BannerlordCoopSpectator3/VARIANT_2_REFACTOR_PLAN_SERVER_FIRST_EXACT_TRANSFER_2026-05-01.md)

## Принцип

Новий шлях повинен виглядати так:

1. server збирає один явний `ExactTransferSpawnContract`
2. contract проходить повну валідацію до `Mission.SpawnAgent(...)`
3. client materialization не "вгадується" по окремих подіях, а
   зіставляється з тим самим contract
4. mounted hero не може стати `ExactReady` або `CommanderReady`, поки не
   підтверджений `rider <-> mount` link
5. death / respawn cleanup очищає не розрізнені кеші, а весь transfer-state

## Межі відповідальності

### 1. Campaign snapshot layer

Відповідає тільки за authoritative дані кампанії:

- хто саме є entry
- який exact character/hero state треба перенести
- яке спорядження, horse, harness, body, colors мають бути перенесені

Цей шар не повинен знати про:

- native `CreateAgent`
- `SetAgentPeer`
- network message timing
- client recovery hooks

### 2. Exact transfer adapter layer

Це нове ядро, яке треба побудувати.

Відповідальність:

- зібрати `ExactTransferSpawnContract`
- перевірити всі поля
- визначити, що йде в `pre-spawn`, а що лише в `post-bind`
- відстежити виконання state machine

Цей шар є єдиним місцем, де campaign truth адаптується до native MP runtime.

### 3. Native MP integration layer

Відповідає тільки за стикування з наявними multiplayer event:

- `CreateAgent`
- `SetAgentPeer`
- `SynchronizeAgentSpawnEquipment`
- `SetWieldedItemIndex`
- `ReplaceBotWithPlayer`
- `SetAgentHealth`
- remove / respawn

Його задача:

- не вигадувати нові дані
- не робити secondary truth
- лише оновлювати transfer-state і викликати безпечні stage transitions

## Основна структура даних

Нижче не production-код, а цільова модель.

```csharp
public sealed class ExactTransferSpawnContract
{
    public string EntryId { get; init; }
    public ExactTransferIdentityContract Identity { get; init; }
    public ExactTransferBodyContract Body { get; init; }
    public ExactTransferEquipmentContract Equipment { get; init; }
    public ExactTransferMountContract Mount { get; init; }
    public ExactTransferPeerBindingContract PeerBinding { get; init; }
    public ExactTransferInitialWieldContract InitialWield { get; init; }
    public ExactTransferControlContract Control { get; init; }
    public ExactTransferCleanupContract Cleanup { get; init; }
    public ExactTransferSpawnPolicyContract SpawnPolicy { get; init; }
}
```

## Identity contract

```csharp
public sealed class ExactTransferIdentityContract
{
    public string CampaignCharacterId { get; init; }
    public string CampaignHeroStringId { get; init; }
    public string NativeMultiplayerCharacterId { get; init; }
    public bool IsHero { get; init; }
    public bool IsMainHero { get; init; }
    public bool IsLord { get; init; }
    public bool IsCompanion { get; init; }
    public bool IsPlayerControlledEntry { get; init; }
    public bool IsMountedExpected { get; init; }
}
```

Правило:

- `CampaignCharacterId` і `CampaignHeroStringId` описують authoritative походження
- `NativeMultiplayerCharacterId` описує тільки legal native MP identity для
  materialization
- він не є surrogate truth; він є adapter target

## Body contract

```csharp
public sealed class ExactTransferBodyContract
{
    public BodyProperties BodyProperties { get; init; }
    public BodyPropertiesMinMax BodyPropertyRange { get; init; }
    public int BodyPropertiesSeed { get; init; }
    public bool IsFemale { get; init; }
    public int Age { get; init; }
    public string MonsterId { get; init; }
}
```

Правило:

- усе, що впливає на legal native materialization тіла, повинно бути тут
- body data не можна тягнути неявно з `MissionPeer`, якщо це strict exact hero
- якщо native path вимагає `MissionPeer.Peer.BodyProperties`, це повинно бути
  відображено у `PeerBindingContract`, а не добудовуватись постфактум

## Equipment contract

```csharp
public sealed class ExactTransferEquipmentContract
{
    public Equipment SpawnEquipment { get; init; }
    public Equipment MissionEquipment { get; init; }
    public ExactTransferEquipmentSlotContract[] Slots { get; init; }
    public uint ClothingColor1 { get; init; }
    public uint ClothingColor2 { get; init; }
    public bool IncludeWeaponsInPreSpawn { get; init; }
    public bool IncludeVisualArmorInPreSpawn { get; init; }
}

public sealed class ExactTransferEquipmentSlotContract
{
    public int SlotIndex { get; init; }
    public string ItemId { get; init; }
    public bool IsEmpty { get; init; }
    public bool MustExistAtCreateAgentTime { get; init; }
    public bool CanBeLateSynchronized { get; init; }
}
```

Правило:

- нам потрібен явний поділ: що native повинен побачити вже під час
  `CreateAgent/Mission.SpawnAgent`, а що можна безпечно синхронізувати пізніше
- саме тут повинна зникнути стара неявна логіка `kept-native`, `queued`,
  `visual-only`, бо вона занадто легко бреше про реальний стан

## Mount contract

```csharp
public sealed class ExactTransferMountContract
{
    public bool IsMounted { get; init; }
    public string HorseItemId { get; init; }
    public string HarnessItemId { get; init; }
    public int? ExpectedMountAgentIndex { get; init; }
    public bool RequiresVerifiedMountLink { get; init; }
}
```

Правило:

- для mounted strict exact hero `RequiresVerifiedMountLink` завжди `true`
- `ExpectedMountAgentIndex` не є підтвердженням існування mount
- `MountMaterialized` і `MountLinkVerified` існують окремо від payload

## Peer binding contract

```csharp
public sealed class ExactTransferPeerBindingContract
{
    public string PeerUserName { get; init; }
    public bool IsRemotePeer { get; init; }
    public bool IsLocalPeer { get; init; }
    public bool RequiresSetAgentPeer { get; init; }
    public bool RequiresReplaceBotWithPlayer { get; init; }
    public bool CanCreateAgentUsePeerBody { get; init; }
    public bool CanCreateAgentUsePeerBanner { get; init; }
}
```

Правило:

- тут фіксується, що саме native має право брати з peer ще до `SetAgentPeer`
- якщо відповідь для strict exact remote hero: "нічого", то це має бути
  відображено тут явно і перевірятись до виклику native handler

## Initial wield contract

```csharp
public sealed class ExactTransferInitialWieldContract
{
    public int? WieldedSlotIndex { get; init; }
    public bool RequireImmediateWieldOnSpawn { get; init; }
    public bool AllowDeferredWieldAfterEquipmentSync { get; init; }
}
```

Правило:

- live combat semantics не можуть залежати від того, чи встигла візуальна
  overlay пізніше перевзвести main-hand
- якщо spawn legal лише з deferred wield, це повинно бути визначено тут до бою

## Control contract

```csharp
public sealed class ExactTransferControlContract
{
    public Team Team { get; init; }
    public int FormationIndex { get; init; }
    public bool IsCommanderEntry { get; init; }
    public bool CanReceivePlayerOrders { get; init; }
    public bool EnableCommanderControlOnlyAfterExactReady { get; init; }
}
```

Правило:

- `CommanderControlEnabled` не може бути раннішим за `ExactReady`
- remote hero не має випадково потрапляти у semantics звичайного formation troop

## Cleanup contract

```csharp
public sealed class ExactTransferCleanupContract
{
    public bool ClearTransferStateOnAgentRemoved { get; init; }
    public bool ClearTransferStateOnMountRemoved { get; init; }
    public bool ClearTransferStateOnDeath { get; init; }
    public bool RejectAgentIndexReuseWithoutIdentityMatch { get; init; }
}
```

Правило:

- rider і mount очищаються як одна пов'язана сутність transfer-state
- reuse старого `AgentIndex` не може резолвитись тільки по індексу без
  перевірки identity

## Spawn policy contract

```csharp
public sealed class ExactTransferSpawnPolicyContract
{
    public bool UseStrictExactHeroPath { get; init; }
    public bool RequirePreSpawnInjection { get; init; }
    public bool AllowClientVisualOverlayAsRecoveryOnly { get; init; }
    public bool ForbidSurrogatePrimaryMaterialization { get; init; }
}
```

Правило:

- `ForbidSurrogatePrimaryMaterialization` для нового hero-first path повинен
  бути `true`
- client overlay дозволений тільки як recovery, а не як основна materialization

## Runtime state

```csharp
public sealed class ExactTransferRuntimeState
{
    public string EntryId { get; init; }
    public int? RiderAgentIndex { get; set; }
    public int? MountAgentIndex { get; set; }
    public ExactTransferStage Stage { get; set; }
    public ExactTransferFailureReason FailureReason { get; set; }
    public bool RiderMaterialized { get; set; }
    public bool MountMaterialized { get; set; }
    public bool MountLinkVerified { get; set; }
    public bool PeerBound { get; set; }
    public bool EquipmentSynchronized { get; set; }
    public bool ExactVisualApplied { get; set; }
    public bool CommanderControlEnabled { get; set; }
    public DateTime LastTransitionUtc { get; set; }
}
```

## Stage enum

```csharp
public enum ExactTransferStage
{
    None = 0,
    SnapshotResolved = 10,
    ContractBuilt = 20,
    ContractValidated = 30,
    PreSpawnPrepared = 40,
    CreateAgentPayloadObserved = 50,
    RiderMaterialized = 60,
    MountMaterialized = 70,
    MountLinkVerified = 80,
    PeerBound = 90,
    EquipmentSynchronized = 100,
    ExactReady = 110,
    CommanderReady = 120,
    DeathObserved = 130,
    CleanupComplete = 140,
    Failed = 900
}
```

## Failure enum

```csharp
public enum ExactTransferFailureReason
{
    None = 0,
    MissingContractField,
    InvalidNativeClassResolution,
    CreateAgentHandlerException,
    RiderNotMaterialized,
    MountNotMaterialized,
    MountLinkMissing,
    SetAgentPeerMissing,
    EquipmentSyncMissing,
    InvalidAgentIndexReuse,
    DeathCleanupIncomplete
}
```

## Дозволені переходи

| Звідки | Куди | Умова |
|---|---|---|
| `None` | `SnapshotResolved` | Є валідний `RosterEntryState` |
| `SnapshotResolved` | `ContractBuilt` | Побудовано всі підконтракти |
| `ContractBuilt` | `ContractValidated` | Немає missing/illegal field |
| `ContractValidated` | `PreSpawnPrepared` | Визначено pre-spawn injection |
| `PreSpawnPrepared` | `CreateAgentPayloadObserved` | На клієнті побачили payload |
| `CreateAgentPayloadObserved` | `RiderMaterialized` | Є живий rider agent |
| `RiderMaterialized` | `MountMaterialized` | Для mounted path є живий mount agent |
| `MountMaterialized` | `MountLinkVerified` | `rider.MountAgent == mount` |
| `MountLinkVerified` | `PeerBound` | `SetAgentPeer` завершився легально |
| `PeerBound` | `EquipmentSynchronized` | equipment sync завершився |
| `EquipmentSynchronized` | `ExactReady` | exact visual/state реально застосовані |
| `ExactReady` | `CommanderReady` | дозволено commander-control |
| `CommanderReady` | `DeathObserved` | смерть rider або mount pair |
| `DeathObserved` | `CleanupComplete` | весь pair/state очищений |

Заборонені скорочення:

- `CreateAgentPayloadObserved -> ExactReady`
- `RiderMaterialized -> CommanderReady`
- `PeerBound -> ExactReady`, якщо mounted path ще без `MountLinkVerified`
- `DeathObserved -> None` без `CleanupComplete`

## Практичне значення для коду

Майбутня реалізація повинна:

1. замінити неявні прапори `queued`, `applied`, `deferred` на `ExactTransferStage`
2. тримати один runtime-object на entry, а не багато часткових кешів
3. забороняти будь-якому handler ставити пізній stage, якщо не виконаний ранній
4. логувати не "refresh спробували", а "stage перейшов / stage відхилено"

## Що не можна робити в новій реалізації

- не можна знову робити surrogate primary spawn path для hero
- не можна вважати `MountAgentIndex` доказом того, що mount materialized
- не можна дозволяти `commander-control`, поки hero не пройшов `ExactReady`
- не можна знову трактувати `queued overlay` як успішний finalize
- не можна прив'язувати transfer truth до одного `AgentIndex` без перевірки
  identity

## Які дані ще треба закрити перед кодом

1. Повна таблиця slot-by-slot:
   що native повинен побачити до `Mission.SpawnAgent`, а що можна відкласти.
2. Точний набір body/banner/peer полів, які `CreateAgent` читає для player agent
   ще до `SetAgentPeer`.
3. Формальна policy для `ReplaceBotWithPlayer`:
   коли strict hero path узагалі має його використовувати, а коли ні.
4. Точна очистка mounted pair при смерті mount раніше rider і rider раніше mount.

## Критерій готовності до нового коду

Новий hero-first adapter path можна починати писати тільки коли:

- `ExactTransferSpawnContract` повністю збирається з campaign truth
- кожне поле має джерело або явну policy
- для кожного native handler відома legal stage transition
- для mounted hero немає жодного неявного recovery step, без якого шлях
  вважається "успішним"

Після цього можна переходити до реалізації `Phase A` без повернення до
surrogate primary path.
