# New Chat Prompt - 2026-03-22

Продовжуємо розробку мода BannerlordCoopSpectator3.

Спочатку уважно прочитай handoff:
- [SESSION_HANDOFF_2026-03-22_WRITEBACK_AND_BATTLE_COMPLETION.md](/C:/dev/projects/BannerlordCoopSpectator3/SESSION_HANDOFF_2026-03-22_WRITEBACK_AND_BATTLE_COMPLETION.md)

Поточний стан:
- equipment fidelity закритий
- hero/companion/lord runtime path working
- stats / skills / perks / party modifiers working
- зараз активний етап: `campaign result / casualty / xp / writeback`

Найсвіжіший незавершений вузол:
- реалізований `authoritative battle completion`
- dedicated тепер має сам визначати кінець бою при повному вибутті сторони, писати `battle_result`, а host/client мають повернутися в campaign loop без ручного виходу в main menu
- цей новий flow ще треба live-перевірити

Ключові файли:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)
- [BattleDetector.cs](/C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs)
- [CoopBattleResultBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleResultBridgeFile.cs)
- [CoopBattleEntryStatusBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleEntryStatusBridgeFile.cs)

Останнє очікування від наступного live-прогону:
- dedicated лог має містити:
  - `authoritative battle completion detected`
  - `battle result snapshot written`
- host лог має містити:
  - `BattleDetector: consumed battle_result writeback audit`
  - `Hits=... Damage=... CombatEvents=...`

Якщо цей тест пройде:
- наступний крок не в equipment/stats, а в реальний apply/writeback:
  - casualties/wounded назад у `TroopRoster`
  - troop XP через campaign `CombatXpModel`
  - hero/companion/lord wound + XP через `Hero` / `HeroDeveloper`

Працюй від цього handoff і не повертайся в старі equipment/debug гілки без явної причини.
