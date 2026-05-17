# New Window Prompt: Exact Campaign 1:1 Transfer

Continue work in:

- `C:\dev\projects\BannerlordCoopSpectator3`

Use branch:

- `codex/runtime-regression-checkpoint-2026-04-26`

First read:

1. `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_TEMPLATE_PATH_HANDOFF_2026-04-26.md`
2. `C:\dev\projects\BannerlordCoopSpectator3\docs\HOSTED_BATTLE_RUNTIME_ARCHITECTURE_AUDIT_2026-04-26.md`
3. `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_CAMPAIGN_1_TO_1_TRANSFER_HANDOFF_2026-04-30.md`

Current goal:

- continue the real exact `1:1` transfer path for campaign units into hosted
  multiplayer
- stop treating client-side visual overlay repair as the target architecture
- prove the remaining mounted hero possession contract from the lowest level up

Current validated state:

- hosted large battle runtime is much more stable than before
- bulk AI no longer dominates the failure surface
- exact entry diagnostics exist
- per-agent spawn trace exists
- the main unresolved problem is now the `player-controlled mounted hero`
  client path

Current visible user-facing symptom:

- local client commander is still nearly naked, often keeps only partial
  equipment like a shield
- the local client's horse still shows stale extra-leg mesh corruption
- the client sees the remote host hero with wrong or missing equipment/armor
- the client can still crash after death

Latest relevant logs:

- client:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_27116.txt`
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_27116.txt`
- host:
  - `C:\Users\Admin\Downloads\Telegram Desktop\battle_entry_compatibility.txt`
  - `C:\Users\Admin\Downloads\Telegram Desktop\battle_agent_spawn_trace.txt`
  - `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_11016.txt`
  - `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_11988.txt`
  - `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_18024.txt`

Most important current conclusion:

- in the last validated logs, exact client visual refresh was queued but never
  executed
- `CoopMissionClientLogic` was not injected in the battle-map crash-isolation
  client stack
- a fallback observer was then added in
  `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionBehaviorDiagnostic.cs`
  to run `CoopMissionSpawnLogic.TryRunClientExactCampaignVisualObserver(...)`
  on battle-map clients
- this new fallback must be revalidated from fresh logs before further design
  decisions

Important code areas:

1. `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`
2. `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionBehaviorDiagnostic.cs`
3. `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
4. `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleMapSpawnHandoffPatch.cs`
5. `C:\dev\projects\BannerlordCoopSpectator3\Patches\ExactCampaignPreSpawnLoadoutPatch.cs`

Hard requirements for this new window:

1. Conduct a fresh low-level analysis from scratch before proposing more code
   changes.
2. Assume we may still have missed a lower-level native/runtime contract detail.
3. Use evidence in this order:
   - fresh logs
   - spawn trace / compatibility report
   - decompilation / `ilspycmd`
   - exact entry/equipment examples
   - only then implementation
4. Do not continue broad hypothesis-driven patches.
5. Before any implementation, provide exactly 3 solution options.
6. For each of the 3 options, explain:
   - what layer it changes
   - why it may work
   - its main risk
   - whether it is temporary triage or target architecture
7. Wait for user choice before implementing one of those options.

The first thing to verify in fresh logs:

1. `MissionBehaviorDiagnostic: running battle-map client exact visual observer fallback...`
2. `completed pending client exact visual refresh`
3. or `escalating stuck pending client exact hero visual refresh...`
4. or `watchdog applied client exact hero visual overlay`

If those lines still do not appear, stop and prove why the observer path is not
running.

Then re-analyze the mounted hero contract from the lowest level:

1. `Mission.SpawnAgent`
2. `AgentBuildData.Equipment`
3. horse / harness state
4. `CreateAgent`
5. `SetAgentPeer`
6. `SynchronizeAgentSpawnEquipment`
7. local and remote client exact visual apply
8. death cleanup:
   - `SetAgentHealth`
   - drop path
   - mounted separation
   - weapon/ammo cleanup

Do not assume the current client overlay approach is the final design.

Target architecture remains:

1. server materializes the real exact campaign unit
2. native MP contract accepts that unit safely
3. client possesses that already-correct unit
4. client-side repair becomes minimal or disappears

Avoid:

- restarting bulk AI triage unless fresh evidence points back there
- reopening solved bootstrap issues without logs
- introducing more local visual workaround layers unless the 3-option analysis
  concludes it is the best available temporary choice

Also note:

- `rg` is available again in this environment and should be preferred for
  searching
