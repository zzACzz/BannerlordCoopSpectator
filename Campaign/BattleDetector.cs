using System; // Підключаємо базові типи .NET (Exception)
using System.Collections.Generic; // Підключаємо List<> для списків у DTO
using CoopSpectator.DedicatedHelper; // SendStartMission / SendEndMission до Dedicated Helper (Етап 3b)
using CoopSpectator.Infrastructure; // Підключаємо логер і UI feedback
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі
using CoopSpectator.Network.Messages; // Підключаємо DTO + кодек для BATTLE_START:{json}
using TaleWorlds.CampaignSystem; // Підключаємо Campaign (для доступу до поточної кампанії)
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty (партія гравця в кампанії)
using TaleWorlds.MountAndBlade; // Підключаємо Mission (детекція входу в місію/битву)
using System.Reflection; // Reflection для mission-safe fallback ids героїв/лордів із кампанії.
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Campaign // Тримаємо battle/campaign логіку в одному namespace
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Детектор старту битви/місії у хоста: // Пояснюємо, що ми детектимо
    /// коли `Mission.Current` переходить з null → не-null, // Описуємо основний сигнал
    /// ми відправляємо клієнтам `BATTLE_START:{json}`. // Описуємо результат
    /// </summary> // Завершуємо XML-коментар
    public sealed class BattleDetector // Оголошуємо sealed сервіс-клас (стан + простий tick)
    { // Починаємо блок класу
        private bool _wasInMissionLastTick; // Пам'ятаємо стан попереднього тіку: чи вже була активна місія
        private bool _hasSentBattleStartForThisMission; // Прапорець, щоб не відправляти BATTLE_START багато разів за одну місію

        public void Tick() // Метод, який треба викликати кожен кадр з головного потоку (наприклад з SubModule.OnApplicationTick)
        { // Починаємо блок методу
            if (GameNetwork.IsClient) // MP-клієнт не повинен керувати dedicated helper під час join/load місії.
            {
                bool isInMissionForClient = Mission.Current != null;
                _wasInMissionLastTick = isInMissionForClient;
                if (!isInMissionForClient)
                    _hasSentBattleStartForThisMission = false;
                return;
            }

            bool isInMissionNow = Mission.Current != null; // Визначаємо чи зараз є активна місія (битва/сцена)

            if (!isInMissionNow) // Якщо місії немає, значить ми не в битві (або вже вийшли з неї)
            { // Починаємо блок if
                if (_wasInMissionLastTick && ShouldNotifyDedicatedHelper()) // Щойно вийшли з місії — сказати Dedicated Helper end_mission (якщо ми не спектатор-клієнт)
                { // Починаємо блок if
                    try { DedicatedServerCommands.SendEndMission(); } catch (Exception ex) { ModLogger.Info("DedicatedServerCommands.SendEndMission: " + ex.Message); }
                } // Завершуємо блок if
                _wasInMissionLastTick = false; // Оновлюємо стан "було в місії" на false
                _hasSentBattleStartForThisMission = false; // Скидаємо прапорець, щоб наступна місія могла знову надіслати BATTLE_START
                ResetIfMissionEnded(); // Тримаємо стан консистентним
                return; // Виходимо, бо поки що нема старту битви для відправки
            } // Завершуємо блок if

            if (_wasInMissionLastTick) // Якщо ми вже були в місії на попередньому тіку, значить "старт" вже минув
            { // Починаємо блок if
                return; // Виходимо, щоб не відправляти повторно
            } // Завершуємо блок if

            _wasInMissionLastTick = true; // Фіксуємо, що ми щойно увійшли в місію
            // Діагностика: лог при вході в місію (битва в кампанії або інша сцена), щоб перевірити чи Tick бачить Mission.Current
            ModLogger.Info("BattleDetector: mission entered (Mission.Current set). Notifying dedicated if applicable.");
            if (ShouldSendBattleStart()) // Якщо ми TCP-хост — відправляємо BATTLE_START клієнтам і start_mission дедику
            { // Починаємо блок if
                TrySendBattleStart(); // Пробуємо сформувати DTO і відправити клієнтам + SendStartMission
            } // Завершуємо блок if
            else if (ShouldNotifyDedicatedHelper()) // Інакше якщо ми не спектатор (кампанія без TCP або TCP-сервер) — лише start_mission дедику
            { // Починаємо блок else if
                ModLogger.Info("BattleDetector: not TCP host — sending start_mission to dedicated (campaign host path).");
                try
                {
                    TryWriteBattleRosterFile(BuildBattleStartPayload()); // Для host-only campaign path теж пишемо battle_roster.json перед start_mission.
                    bool sent = DedicatedServerCommands.SendStartMission();
                    ModLogger.Info("BattleDetector: SendStartMission() returned " + sent + ".");
                }
                catch (Exception ex)
                {
                    ModLogger.Info("DedicatedServerCommands.SendStartMission: " + ex.Message);
                }
            } // Завершуємо блок else if
        } // Завершуємо блок методу

        private static bool ShouldSendBattleStart() // Перевіряємо чи поточний інстанс гри має право надсилати BATTLE_START
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager ініціалізований
            { // Починаємо блок if
                return false; // Повертаємо false, бо без мережі немає куди надсилати
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа реально запущена (сервер активний)
            { // Починаємо блок if
                return false; // Повертаємо false, бо сервер ще не стартував
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Server) // Перевіряємо що ми саме хост/сервер
            { // Починаємо блок if
                return false; // Повертаємо false, бо клієнт не має розсилати battle події
            } // Завершуємо блок if

            return true; // Повертаємо true, бо ми сервер і можемо надсилати BATTLE_START
        } // Завершуємо блок методу

        /// <summary>Чи треба повідомляти Dedicated Helper (start_mission/end_mission). True для хоста кампанії або TCP-сервера, false для спектатор-клієнта.</summary>
        private static bool ShouldNotifyDedicatedHelper()
        {
            if (CoopRuntime.Network == null) return true; // Нема мережі — це кампанія-хост, дедик на цій машині керується тут
            if (CoopRuntime.Network.Role == NetworkRole.Client) return false; // Спектатор не керує дедиком
            return true; // Сервер або інший випадок — керуємо
        }

        private void ResetIfMissionEnded() // Helper: тримаємо внутрішній стан коректним, коли місія завершилась
        { // Починаємо блок методу
            if (Mission.Current != null) // Якщо місія активна, ми нічого не скидаємо
            { // Починаємо блок if
                return; // Виходимо, бо ми все ще в місії
            } // Завершуємо блок if

            _wasInMissionLastTick = false; // Скидаємо "було в місії" — ми вже не в місії
            _hasSentBattleStartForThisMission = false; // Дозволяємо наступній місії знову відправити BATTLE_START
        } // Завершуємо блок методу

        private void TrySendBattleStart() // Формуємо повідомлення та надсилаємо клієнтам (best-effort)
        { // Починаємо блок методу
            if (_hasSentBattleStartForThisMission) // Перевіряємо guard-прапорець
            { // Починаємо блок if
                return; // Виходимо, бо вже відправили для цієї місії
            } // Завершуємо блок if

            _hasSentBattleStartForThisMission = true; // Ставимо прапорець одразу, щоб навіть при винятку не спамити повторними відправками

            try // Захищаємо збір даних від винятків, щоб мод не крашив гру
            { // Починаємо блок try
                BattleStartMessage payload = BuildBattleStartPayload(); // Будуємо DTO з даними про битву/місію
                TryWriteBattleRosterFile(payload); // Варіант A: зберігаємо roster у спільний файл для dedicated на цьому ж ПК.
                string wireMessage = BattleStartMessageCodec.BuildBattleStartMessage(payload); // Серіалізуємо DTO в `BATTLE_START:{json}`
                CoopRuntime.Network.BroadcastToClients(wireMessage); // Відправляємо повідомлення всім підключеним клієнтам

                UiFeedback.ShowMessageDeferred("Host: BATTLE_START sent to clients."); // Даємо короткий UI-індикатор для дебагу/перевірки
                ModLogger.Info("BATTLE_START broadcasted."); // Логуємо факт відправки в лог гри
                try { DedicatedServerCommands.SendStartMission(); } catch (Exception ex) { ModLogger.Info("DedicatedServerCommands.SendStartMission: " + ex.Message); } // Етап 3b: Dedicated Helper переводить у mission mode (поки stub)
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які винятки (API changes/null, тощо)
            { // Починаємо блок catch
                ModLogger.Error("Failed to send BATTLE_START.", ex); // Логуємо помилку, щоб її можна було діагностувати
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static void TryWriteBattleRosterFile(BattleStartMessage payload)
        {
            if (payload == null || payload.Troops == null || payload.Troops.Count == 0)
            {
                ModLogger.Info("BattleDetector: skipping battle_roster.json write (payload troops empty).");
                return;
            }

            try
            {
                var troopIds = new List<string>();
                foreach (TroopStackInfo troop in payload.Troops)
                {
                    if (troop == null || string.IsNullOrWhiteSpace(troop.CharacterId))
                        continue;
                    troopIds.Add(troop.CharacterId);
                }

                bool wrote = BattleRosterFileHelper.WriteRoster(troopIds, payload.Snapshot);
                ModLogger.Info("BattleDetector: battle_roster.json write result = " + wrote + " (troop ids collected = " + troopIds.Count + ", snapshot sides = " + (payload.Snapshot?.Sides?.Count ?? 0) + ").");
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleDetector: failed to write battle_roster.json.", ex);
            }
        }

        private static BattleStartMessage BuildBattleStartPayload() // Будуємо мінімальні дані про старт битви/місії
        { // Починаємо блок методу
            BattleStartMessage message = new BattleStartMessage(); // Створюємо DTO

            // 1) Map scene name (best-effort) // Пояснюємо блок
            message.MapScene = TryGetMapSceneNameSafe(); // Пишемо назву сцени мапи (або "unknown")

            // 2) Map position (campaign world) // Пояснюємо блок
            float x = 0f; // Дефолтне значення X
            float y = 0f; // Дефолтне значення Y

            try // Пробуємо взяти позицію партії хоста
            { // Починаємо блок try
                var pos = MobileParty.MainParty.GetPosition2D; // Беремо позицію партії гравця на мапі (Vec2)
                x = pos.x; // Записуємо X
                y = pos.y; // Записуємо Y
            } // Завершуємо блок try
            catch (Exception) // Якщо ми не в кампанії або API недоступний — лишаємо 0/0
            { // Починаємо блок catch
            } // Завершуємо блок catch

            message.MapX = x; // Записуємо X у DTO
            message.MapY = y; // Записуємо Y у DTO

            // 3) Player side (best-effort) // Пояснюємо блок
            message.PlayerSide = TryGetPlayerSideTextSafe(); // Пишемо "Attacker/Defender/Unknown" як текст

            // 4) Extended battle snapshot (best-effort) // Пояснюємо блок
            message.Snapshot = BuildBattleSnapshotSafe(message.MapScene, message.PlayerSide);
            BattleSnapshotRuntimeState.SetCurrent(message.Snapshot, "host-battle-detector");

            // 5) Legacy fields for transitional clients/runtime // Пояснюємо блок
            message.Troops = BuildLegacyTroopsFromSnapshot(message.Snapshot) ?? BuildPartyTroopStacksSafe();
            message.ArmySize = BuildLegacyArmySizeFromSnapshot(message.Snapshot);
            if (message.ArmySize <= 0)
                message.ArmySize = TryGetArmySizeSafe(); // Записуємо сумарну кількість людей (для UI і тестів)

            return message; // Повертаємо сформований DTO
        } // Завершуємо блок методу

        private static string TryGetMapSceneNameSafe() // Best-effort: намагаємось отримати назву сцени карти кампанії
        { // Починаємо блок методу
            try // Обгортаємо в try/catch, бо MapSceneWrapper може бути недоступний у деяких контекстах
            { // Починаємо блок try
                if (TaleWorlds.CampaignSystem.Campaign.Current == null) // Якщо кампанія не створена (наприклад, ми в меню) — повертаємо "unknown"
                { // Починаємо блок if
                    return "unknown"; // Повертаємо fallback
                } // Завершуємо блок if

                object mapScene = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper; // Беремо map-scene wrapper як object (щоб не залежати від конкретного інтерфейсу)

                if (mapScene == null) // Якщо wrapper відсутній — повертаємо fallback
                { // Починаємо блок if
                    return "unknown"; // Fallback
                } // Завершуємо блок if

                Type mapSceneType = mapScene.GetType(); // Отримуємо runtime-тип wrapper'а

                // 1) Пробуємо метод GetMapSceneName() (як у старих прикладах) // Пояснюємо першу спробу
                var getNameMethod = mapSceneType.GetMethod("GetMapSceneName", Type.EmptyTypes); // Шукаємо метод без параметрів

                if (getNameMethod != null) // Якщо метод знайдено — пробуємо викликати
                { // Починаємо блок if
                    object value = getNameMethod.Invoke(mapScene, null); // Викликаємо метод reflection'ом
                    string text = value != null ? value.ToString() : null; // Перетворюємо результат в string

                    if (!string.IsNullOrEmpty(text)) // Якщо рядок валідний — повертаємо його
                    { // Починаємо блок if
                        return text; // Повертаємо назву сцени
                    } // Завершуємо блок if
                } // Завершуємо блок if

                // 2) Пробуємо властивості з типових назв: SceneName / Name / MapSceneName // Пояснюємо другу спробу
                var nameProperty = // Оголошуємо змінну з першим знайденим property
                    mapSceneType.GetProperty("SceneName") ?? // Варіант 1
                    mapSceneType.GetProperty("Name") ?? // Варіант 2
                    mapSceneType.GetProperty("MapSceneName"); // Варіант 3

                if (nameProperty != null) // Якщо property знайдено — читаємо значення
                { // Починаємо блок if
                    object value = nameProperty.GetValue(mapScene, null); // Читаємо значення property
                    string text = value != null ? value.ToString() : null; // Перетворюємо в string

                    if (!string.IsNullOrEmpty(text)) // Якщо рядок валідний — повертаємо його
                    { // Починаємо блок if
                        return text; // Повертаємо назву сцени
                    } // Завершуємо блок if
                } // Завершуємо блок if

                return mapScene.ToString(); // Останній fallback — ToString() типу wrapper'а
            } // Завершуємо блок try
            catch (Exception) // Якщо API/контекст не підходить — повертаємо "unknown"
            { // Починаємо блок catch
                return "unknown"; // Повертаємо fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static int TryGetArmySizeSafe() // Best-effort: намагаємось отримати загальний розмір партії
        { // Починаємо блок методу
            try // Захищаємо доступ до MobileParty на випадок, якщо ми не в кампанії
            { // Починаємо блок try
                return MobileParty.MainParty.MemberRoster.TotalManCount; // Повертаємо загальну кількість людей у ростері
            } // Завершуємо блок try
            catch (Exception) // Якщо щось не доступно — повертаємо 0
            { // Починаємо блок catch
                return 0; // Fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static List<TroopStackInfo> BuildPartyTroopStacksSafe() // Будуємо список стеків військ з партії (best-effort)
        { // Починаємо блок методу
            List<TroopStackInfo> troops = new List<TroopStackInfo>(); // Створюємо список результатів

            try // Захищаємо доступ до ростеру, щоб не крашнути гру
            { // Починаємо блок try
                var rosterElements = new List<TaleWorlds.CampaignSystem.Roster.TroopRosterElement>();
                var rosterCharacters = new List<object>();
                foreach (var rosterElement in MobileParty.MainParty.MemberRoster.GetTroopRoster())
                {
                    rosterElements.Add(rosterElement);
                    if (rosterElement.Character != null)
                        rosterCharacters.Add(rosterElement.Character);
                }

                foreach (var element in rosterElements) // Проходимо по елементах ростеру партії
                { // Починаємо блок foreach
                    if (element.Character == null) // Перевіряємо що є Character (тип юнита)
                    { // Починаємо блок if
                        continue; // Пропускаємо некоректні елементи
                    } // Завершуємо блок if

                    TroopStackInfo stack = new TroopStackInfo(); // Створюємо DTO стеку
                    string originalCharacterId = TryGetStringId(element.Character);
                    string spawnTemplateId = GetMissionSafeCharacterId(element.Character, rosterCharacters);
                    stack.CharacterId = spawnTemplateId; // Back-compat alias for older runtime readers.
                    stack.OriginalCharacterId = originalCharacterId;
                    stack.SpawnTemplateId = spawnTemplateId;
                    stack.CultureId = TryGetCultureId(element.Character);
                    stack.HasShield = TryGetCharacterHasShield(element.Character);
                    stack.HasThrown = TryGetCharacterHasThrown(element.Character);
                    ApplyCombatEquipmentSnapshot(stack, element.Character);
                    ApplyHeroIdentitySnapshot(stack, element.Character);
                    stack.IsRanged = TryGetCharacterIsRanged(element.Character);
                    stack.TroopName = element.Character.Name != null ? element.Character.Name.ToString() : element.Character.StringId; // Беремо ім'я або fallback на id
                    stack.Tier = element.Character.Tier; // Записуємо tier
                    stack.IsMounted = element.Character.IsMounted; // Записуємо чи верховий
                    stack.IsHero = element.Character.IsHero; // Записуємо чи герой
                    stack.Count = element.Number; // Записуємо кількість у стеку

                    // WoundedNumber може бути відсутній в деяких версіях; // Пояснюємо чому try/catch
                    // тому беремо його best-effort через try/catch. // Пояснюємо підхід
                    int wounded = 0; // Дефолт

                    try // Пробуємо взяти wounded count
                    { // Починаємо блок try
                        wounded = element.WoundedNumber; // Беремо кількість поранених у стеку (якщо API має цю властивість)
                    } // Завершуємо блок try
                    catch (Exception) // Якщо властивості нема — лишаємо 0
                    { // Починаємо блок catch
                    } // Завершуємо блок catch

                    stack.WoundedCount = wounded; // Записуємо wounded count

                    troops.Add(stack); // Додаємо стек до списку
                } // Завершуємо блок foreach
            } // Завершуємо блок try
            catch (Exception ex) // Якщо ростер недоступний — лог і повертаємо те, що є
            { // Починаємо блок catch
                ModLogger.Error("Failed to build troop roster for BATTLE_START.", ex); // Логуємо помилку
            } // Завершуємо блок catch

            LogTroopStackMappings("main-party-roster", troops);
            return troops; // Повертаємо список (може бути порожнім — це ок для MVP)
        } // Завершуємо блок методу

        private static BattleSnapshotMessage BuildBattleSnapshotSafe(string mapScene, string playerSideText)
        {
            try
            {
                object battle = TryGetCurrentBattleObject();
                if (battle == null)
                    return BuildFallbackBattleSnapshot(mapScene, playerSideText);

                ModLogger.Info("BattleDetector: building live battle snapshot from battle type = " + battle.GetType().FullName + ".");

                var snapshot = new BattleSnapshotMessage
                {
                    BattleId = battle.GetType().FullName,
                    BattleType = battle.GetType().Name,
                    MapScene = mapScene,
                    PlayerSide = playerSideText
                };

                BattleSideSnapshotMessage attackerSide = BuildBattleSideSnapshot(
                    TryGetPropertyValue(battle, "AttackerSide"),
                    "attacker",
                    nameof(BattleSideEnum.Attacker),
                    playerSideText);
                BattleSideSnapshotMessage defenderSide = BuildBattleSideSnapshot(
                    TryGetPropertyValue(battle, "DefenderSide"),
                    "defender",
                    nameof(BattleSideEnum.Defender),
                    playerSideText);

                ModLogger.Info(
                    "BattleDetector: live battle side build result. " +
                    "AttackerBuilt=" + (attackerSide != null) +
                    " DefenderBuilt=" + (defenderSide != null) + ".");

                if (attackerSide != null)
                    snapshot.Sides.Add(attackerSide);
                if (defenderSide != null)
                    snapshot.Sides.Add(defenderSide);

                if (snapshot.Sides.Count == 0)
                    return BuildFallbackBattleSnapshot(mapScene, playerSideText);

                LogSnapshotMappings("live-battle", snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: extended battle snapshot build failed: " + ex.Message);
                return BuildFallbackBattleSnapshot(mapScene, playerSideText);
            }
        }

        private static BattleSnapshotMessage BuildFallbackBattleSnapshot(string mapScene, string playerSideText)
        {
            var snapshot = new BattleSnapshotMessage
            {
                BattleId = "fallback",
                BattleType = "Unknown",
                MapScene = mapScene,
                PlayerSide = playerSideText
            };

            var mainPartySide = new BattleSideSnapshotMessage
            {
                SideId = string.Equals(playerSideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase) ? "defender" : "attacker",
                SideText = string.Equals(playerSideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase)
                    ? nameof(BattleSideEnum.Defender)
                    : nameof(BattleSideEnum.Attacker),
                IsPlayerSide = true
            };

            var mainParty = new BattlePartySnapshotMessage
            {
                PartyId = MobileParty.MainParty?.StringId ?? "main_party",
                PartyName = MobileParty.MainParty?.Name?.ToString() ?? "Main Party",
                IsMainParty = true,
                Troops = BuildPartyTroopStacksSafe()
            };
            mainParty.TotalManCount = mainParty.Troops?.Sum(t => t?.Count ?? 0) ?? 0;
            mainPartySide.Parties.Add(mainParty);
            mainPartySide.Troops.AddRange(mainParty.Troops ?? new List<TroopStackInfo>());
            mainPartySide.TotalManCount = mainParty.TotalManCount;
            snapshot.Sides.Add(mainPartySide);

            LogSnapshotMappings("fallback", snapshot);
            return snapshot;
        }

        private static BattleSideSnapshotMessage BuildBattleSideSnapshot(object sideObject, string sideId, string sideText, string playerSideText)
        {
            if (sideObject == null)
            {
                ModLogger.Info("BattleDetector: live side snapshot skipped because side object is null. SideId=" + (sideId ?? "unknown") + ".");
                return null;
            }

            ModLogger.Info(
                "BattleDetector: building side snapshot. " +
                "SideId=" + (sideId ?? "unknown") +
                " SideType=" + sideObject.GetType().FullName + ".");

            var sideSnapshot = new BattleSideSnapshotMessage
            {
                SideId = sideId,
                SideText = sideText,
                IsPlayerSide = string.Equals(playerSideText, sideText, StringComparison.OrdinalIgnoreCase)
            };

            HashSet<string> seenPartyIds = new HashSet<string>(StringComparer.Ordinal);
            int enumeratedPartyCount = 0;
            foreach (object partyObject in EnumerateBattleParties(sideObject))
            {
                enumeratedPartyCount++;
                BattlePartySnapshotMessage partySnapshot = BuildBattlePartySnapshot(partyObject, sideId);
                if (partySnapshot == null)
                    continue;

                string partyId = string.IsNullOrWhiteSpace(partySnapshot.PartyId)
                    ? "party_" + seenPartyIds.Count
                    : partySnapshot.PartyId;
                if (!seenPartyIds.Add(partyId))
                    continue;

                partySnapshot.PartyId = partyId;
                sideSnapshot.Parties.Add(partySnapshot);
                if (partySnapshot.Troops != null && partySnapshot.Troops.Count > 0)
                    sideSnapshot.Troops.AddRange(partySnapshot.Troops);
            }

            sideSnapshot.TotalManCount = sideSnapshot.Parties.Sum(p => p?.TotalManCount ?? 0);
            ModLogger.Info(
                "BattleDetector: side snapshot built. " +
                "SideId=" + (sideId ?? "unknown") +
                " EnumeratedPartyObjects=" + enumeratedPartyCount +
                " SnapshotParties=" + sideSnapshot.Parties.Count +
                " TotalTroopStacks=" + sideSnapshot.Troops.Count + ".");
            return sideSnapshot.Parties.Count > 0 ? sideSnapshot : null;
        }

        private static BattlePartySnapshotMessage BuildBattlePartySnapshot(object partyObject, string sideId)
        {
            object partyBase = UnwrapPartyBase(partyObject);
            if (partyBase == null)
            {
                ModLogger.Info(
                    "BattleDetector: party snapshot skipped because party base unwrap failed. " +
                    "SideId=" + (sideId ?? "unknown") +
                    " PartyObjectType=" + (partyObject?.GetType().FullName ?? "null") + ".");
                return null;
            }

            List<TroopStackInfo> troops = BuildTroopStacksFromPartySafe(partyBase, sideId);
            if (troops.Count == 0)
            {
                ModLogger.Info(
                    "BattleDetector: party snapshot skipped because troop stack build returned 0 entries. " +
                    "SideId=" + (sideId ?? "unknown") +
                    " PartyBaseType=" + partyBase.GetType().FullName +
                    " PartyId=" + (TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty")) ?? "null") + ".");
                return null;
            }

            string partyId = TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty"));
            string partyName = TryGetPartyName(partyBase);
            bool isMainParty = ReferenceEquals(partyBase, MobileParty.MainParty?.Party);

            return new BattlePartySnapshotMessage
            {
                PartyId = partyId,
                PartyName = partyName,
                IsMainParty = isMainParty,
                TotalManCount = troops.Sum(t => t?.Count ?? 0),
                Troops = troops
            };
        }

        private static IEnumerable<object> EnumerateBattleParties(object sideObject)
        {
            if (sideObject == null)
                yield break;

            foreach (string propertyName in new[] { "Parties", "BattleParties", "PartiesOnSide", "MemberParties", "MapEventPartySides", "PartyBases", "MobileParties" })
            {
                object collection = TryGetPropertyValue(sideObject, propertyName);
                if (!(collection is System.Collections.IEnumerable enumerable) || collection is string)
                    continue;

                foreach (object item in enumerable)
                {
                    if (item != null)
                        yield return item;
                }

                ModLogger.Info(
                    "BattleDetector: enumerated battle parties via property '" + propertyName + "'. " +
                    "SideType=" + sideObject.GetType().FullName + ".");

                yield break;
            }

            if (sideObject is System.Collections.IEnumerable selfEnumerable && !(sideObject is string))
            {
                foreach (object item in selfEnumerable)
                {
                    if (item != null)
                        yield return item;
                }

                ModLogger.Info(
                    "BattleDetector: enumerated battle parties via self-enumerable side object. " +
                    "SideType=" + sideObject.GetType().FullName + ".");
            }
        }

        private static object UnwrapPartyBase(object partyObject)
        {
            if (partyObject == null)
                return null;

            foreach (string propertyName in new[] { "Party", "PartyBase", "MobileParty", "MapEventParty", "PartyComponent" })
            {
                object nested = TryGetPropertyValue(partyObject, propertyName);
                if (nested == null)
                    continue;

                object nestedPartyBase = TryGetPropertyValue(nested, "Party") ?? TryGetPropertyValue(nested, "PartyBase");
                if (nestedPartyBase != null)
                    return nestedPartyBase;

                if (TryGetPropertyValue(nested, "MemberRoster") != null || TryGetPropertyValue(nested, "Roster") != null)
                    return nested;
            }

            return TryGetPropertyValue(partyObject, "MemberRoster") != null || TryGetPropertyValue(partyObject, "Roster") != null
                ? partyObject
                : null;
        }

        private static List<TroopStackInfo> BuildTroopStacksFromPartySafe(object partyBase, string sideId)
        {
            var troops = new List<TroopStackInfo>();
            if (partyBase == null)
                return troops;

            try
            {
                object memberRoster = TryGetPropertyValue(partyBase, "MemberRoster");
                if (memberRoster == null)
                {
                    ModLogger.Info(
                        "BattleDetector: party troop build missing MemberRoster. " +
                        "SideId=" + (sideId ?? "unknown") +
                        " PartyBaseType=" + partyBase.GetType().FullName + ".");
                    return troops;
                }

                object troopRoster = TryInvokeMethod(memberRoster, "GetTroopRoster");
                if (troopRoster == null)
                {
                    troopRoster = TryGetPropertyValue(memberRoster, "TroopRoster") ?? TryGetPropertyValue(memberRoster, "_roster");
                }
                if (!(troopRoster is System.Collections.IEnumerable enumerable))
                {
                    ModLogger.Info(
                        "BattleDetector: party troop build missing enumerable troop roster. " +
                        "SideId=" + (sideId ?? "unknown") +
                        " PartyBaseType=" + partyBase.GetType().FullName +
                        " MemberRosterType=" + memberRoster.GetType().FullName +
                        " TroopRosterType=" + (troopRoster?.GetType().FullName ?? "null") + ".");
                    return troops;
                }

                var rosterCharacters = new List<object>();
                var rosterElements = new List<object>();
                foreach (object element in enumerable)
                {
                    if (element == null)
                        continue;

                    rosterElements.Add(element);
                    object character = TryGetPropertyValue(element, "Character");
                    if (character != null)
                        rosterCharacters.Add(character);
                }

                string partyId = TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty"));
                foreach (object element in rosterElements)
                {
                    object character = TryGetPropertyValue(element, "Character");
                    if (character == null)
                        continue;

                    string originalCharacterId = TryGetStringId(character);
                    string spawnTemplateId = GetMissionSafeCharacterId(character, rosterCharacters);
                    var stack = new TroopStackInfo
                    {
                        EntryId = sideId + "|" + (partyId ?? "party") + "|" + (spawnTemplateId ?? "unknown"),
                        SideId = sideId,
                        PartyId = partyId,
                        CharacterId = spawnTemplateId,
                        OriginalCharacterId = originalCharacterId,
                        SpawnTemplateId = spawnTemplateId,
                        HasShield = TryGetCharacterHasShield(character),
                        HasThrown = TryGetCharacterHasThrown(character),
                        TroopName = TryGetPropertyValue(character, "Name")?.ToString() ?? TryGetStringId(character),
                        CultureId = TryGetCultureId(character),
                        Tier = TryGetIntProperty(character, "Tier"),
                        IsMounted = TryGetBoolProperty(character, "IsMounted"),
                        IsRanged = TryGetCharacterIsRanged(character),
                        IsHero = TryGetBoolProperty(character, "IsHero"),
                        Count = TryGetIntProperty(element, "Number"),
                        WoundedCount = TryGetIntProperty(element, "WoundedNumber")
                    };
                    ApplyCombatEquipmentSnapshot(stack, character);
                    ApplyHeroIdentitySnapshot(stack, character);
                    troops.Add(stack);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to build side party troop stacks: " + ex.Message);
            }

            string partyIdForLog = TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty")) ?? "party";
            LogTroopStackMappings("side-party " + (sideId ?? "unknown") + "/" + partyIdForLog, troops);
            return troops;
        }

        private static void LogTroopStackMappings(string source, List<TroopStackInfo> troops)
        {
            if (troops == null || troops.Count == 0)
                return;

            IEnumerable<string> mappings = troops
                .Where(troop => troop != null)
                .Take(24)
                .Select(troop =>
                    (troop.OriginalCharacterId ?? "null") +
                    " -> " +
                    ((!string.IsNullOrWhiteSpace(troop.SpawnTemplateId) ? troop.SpawnTemplateId : troop.CharacterId) ?? "null") +
                    " x" + troop.Count +
                    " culture=" + (troop.CultureId ?? "null") +
                    " ranged=" + troop.IsRanged +
                    " shield=" + troop.HasShield +
                    " thrown=" + troop.HasThrown +
                    FormatHeroIdentitySummary(troop) +
                    FormatCombatEquipmentSummary(troop));

            ModLogger.Info(
                "BattleDetector: troop stack mapping summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", mappings) +
                "].");
        }

        private static void LogSnapshotMappings(string source, BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return;

            IEnumerable<string> mappings = snapshot.Sides
                .Where(side => side?.Troops != null)
                .SelectMany(side => side.Troops)
                .Where(troop => troop != null)
                .Take(32)
                .Select(troop =>
                    (troop.SideId ?? "side") + "/" +
                    (troop.PartyId ?? "party") + "/" +
                    (troop.EntryId ?? "entry") + ": " +
                    (troop.OriginalCharacterId ?? "null") +
                    " -> " +
                    ((!string.IsNullOrWhiteSpace(troop.SpawnTemplateId) ? troop.SpawnTemplateId : troop.CharacterId) ?? "null") +
                    " culture=" + (troop.CultureId ?? "null") +
                    " ranged=" + troop.IsRanged +
                    " shield=" + troop.HasShield +
                    " thrown=" + troop.HasThrown +
                    FormatHeroIdentitySummary(troop) +
                    FormatCombatEquipmentSummary(troop));

            ModLogger.Info(
                "BattleDetector: snapshot mapping summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", mappings) +
                "].");
        }

        private static List<TroopStackInfo> BuildLegacyTroopsFromSnapshot(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return null;

            BattleSideSnapshotMessage playerSide = snapshot.Sides.FirstOrDefault(side => side != null && side.IsPlayerSide);
            BattleSideSnapshotMessage fallbackSide = playerSide ?? snapshot.Sides.FirstOrDefault(side => side != null);
            return fallbackSide?.Troops;
        }

        private static int BuildLegacyArmySizeFromSnapshot(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return 0;

            BattleSideSnapshotMessage playerSide = snapshot.Sides.FirstOrDefault(side => side != null && side.IsPlayerSide);
            BattleSideSnapshotMessage fallbackSide = playerSide ?? snapshot.Sides.FirstOrDefault(side => side != null);
            return fallbackSide?.TotalManCount ?? 0;
        }

        private static object TryGetCurrentBattleObject()
        {
            try
            {
                var campaignAssembly = typeof(TaleWorlds.CampaignSystem.Campaign).Assembly;
                Type typeA = campaignAssembly.GetType("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter");
                Type typeB = campaignAssembly.GetType("TaleWorlds.CampaignSystem.PlayerEncounter");
                Type playerEncounterType = typeA ?? typeB;
                if (playerEncounterType == null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject failed because PlayerEncounter type was not found.");
                    return null;
                }

                object currentEncounter = TryGetStaticPropertyValue(playerEncounterType, "Current");
                object directBattle = TryGetStaticPropertyValue(playerEncounterType, "Battle");
                if (directBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via PlayerEncounter.Battle.");
                    return directBattle;
                }

                object encounteredBattle = TryGetStaticPropertyValue(playerEncounterType, "EncounteredBattle");
                if (encounteredBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via PlayerEncounter.EncounteredBattle.");
                    return encounteredBattle;
                }

                object currentEncounterBattle = TryGetPropertyValue(currentEncounter, "Battle") ?? TryGetPropertyValue(currentEncounter, "_mapEvent");
                if (currentEncounterBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via PlayerEncounter.Current.Battle/_mapEvent.");
                    return currentEncounterBattle;
                }

                object currentEncounteredParty = TryGetPropertyValue(currentEncounter, "EncounteredParty") ?? TryGetPropertyValue(currentEncounter, "_encounteredParty");
                object currentEncounterPartyBattle =
                    TryGetPropertyValue(currentEncounteredParty, "MapEvent") ??
                    TryGetPropertyValue(TryGetPropertyValue(currentEncounteredParty, "MobileParty"), "MapEvent");
                if (currentEncounterPartyBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via encountered party MapEvent.");
                    return currentEncounterPartyBattle;
                }

                object mainPartyBattle = TryGetPropertyValue(MobileParty.MainParty, "MapEvent");
                if (mainPartyBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via MobileParty.MainParty.MapEvent.");
                    return mainPartyBattle;
                }

                ModLogger.Info(
                    "BattleDetector: TryGetCurrentBattleObject returned null. " +
                    "HasCurrentEncounter=" + (currentEncounter != null) +
                    " HasEncounteredParty=" + (currentEncounteredParty != null) +
                    " HasMainPartyMapEvent=" + (mainPartyBattle != null));
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: TryGetCurrentBattleObject failed: " + ex.Message);
                return null;
            }
        }

        private static object TryGetStaticPropertyValue(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return property?.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static object TryInvokeMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            try
            {
                MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                return method?.Invoke(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetPartyName(object partyBase)
        {
            object directName = TryGetPropertyValue(partyBase, "Name");
            if (directName != null)
                return directName.ToString();

            object mobileParty = TryGetPropertyValue(partyBase, "MobileParty");
            object mobilePartyName = TryGetPropertyValue(mobileParty, "Name");
            if (mobilePartyName != null)
                return mobilePartyName.ToString();

            return TryGetStringId(partyBase) ?? "Unknown Party";
        }

        private static string TryGetPlayerSideTextSafe() // Best-effort: намагаємось визначити сторону гравця в поточному encounter
        { // Починаємо блок методу
            // Ми не хардкодимо PlayerEncounter тип напряму, // Пояснюємо чому reflection
            // щоб зменшити ризик несумісності API між версіями гри. // Пояснюємо мету
            try // Обгортаємо в try/catch, бо reflection може впасти
            { // Починаємо блок try
                var campaignAssembly = typeof(TaleWorlds.CampaignSystem.Campaign).Assembly; // Беремо збірку TaleWorlds.CampaignSystem, де зазвичай лежать encounter типи

                // Найчастіші повні імена для PlayerEncounter в Bannerlord. // Пояснюємо список кандидатів
                Type typeA = campaignAssembly.GetType("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter"); // Варіант 1: через namespace Encounters
                Type typeB = campaignAssembly.GetType("TaleWorlds.CampaignSystem.PlayerEncounter"); // Варіант 2: прямо в CampaignSystem
                Type playerEncounterType = typeA ?? typeB; // Беремо перший знайдений варіант

                if (playerEncounterType == null) // Якщо тип не знайдений — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                // Очікуємо static property Battle. // Пояснюємо очікувану структуру
                var battleProperty = playerEncounterType.GetProperty("Battle"); // Беремо property "Battle"

                if (battleProperty == null) // Якщо property немає — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                object battle = battleProperty.GetValue(null, null); // Читаємо static property (екземпляр не потрібен)

                if (battle == null) // Якщо battle null — значить encounter не в стані battle або ще не створений
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                // Очікуємо property PlayerSide на battle-об'єкті. // Пояснюємо очікування
                var playerSideProperty = battle.GetType().GetProperty("PlayerSide"); // Беремо property "PlayerSide"

                if (playerSideProperty == null) // Якщо немає — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                object sideValue = playerSideProperty.GetValue(battle, null); // Читаємо значення PlayerSide (зазвичай enum)

                return sideValue != null ? sideValue.ToString() : "Unknown"; // Повертаємо enum.ToString() або Unknown
            } // Завершуємо блок try
            catch (Exception) // Якщо reflection не вдався — не ламаємо мод, а просто повертаємо Unknown
            { // Починаємо блок catch
                return "Unknown"; // Fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static string GetMissionSafeCharacterId(object characterObject, List<object> rosterCharacters)
        {
            if (characterObject == null)
                return null;

            string originalId = TryGetStringId(characterObject);
            bool isHero = TryGetBoolProperty(characterObject, "IsHero");
            if (isHero)
            {
                string heroRoleSafeId = TryResolveHeroRoleMissionSafeCharacterId(characterObject as TaleWorlds.CampaignSystem.CharacterObject);
                if (!string.IsNullOrWhiteSpace(heroRoleSafeId) && !string.Equals(heroRoleSafeId, originalId, StringComparison.Ordinal))
                {
                    ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to hero-role runtime template '" + heroRoleSafeId + "'.");
                    return heroRoleSafeId;
                }
            }

            if (!isHero)
            {
                string banditFallbackId = TryResolveBanditMissionSafeCharacterId(characterObject);
                if (!string.IsNullOrWhiteSpace(banditFallbackId) && !string.Equals(banditFallbackId, originalId, StringComparison.Ordinal))
                {
                    ModLogger.Info("BattleDetector: mapped bandit campaign troop id '" + originalId + "' to mission-safe bandit fallback '" + banditFallbackId + "'.");
                    return banditFallbackId;
                }
            }

            string multiplayerSafeId = TryResolveMultiplayerSafeCharacterId(characterObject as TaleWorlds.CampaignSystem.CharacterObject);
            if (!string.IsNullOrWhiteSpace(multiplayerSafeId) && !string.Equals(multiplayerSafeId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped campaign troop id '" + originalId + "' to multiplayer-safe id '" + multiplayerSafeId + "'.");
                return multiplayerSafeId;
            }

            if (!isHero)
            {
                string genericFallbackId = TryResolveGenericMissionSafeCharacterId(characterObject);
                if (!string.IsNullOrWhiteSpace(genericFallbackId) && !string.Equals(genericFallbackId, originalId, StringComparison.Ordinal))
                {
                    ModLogger.Info("BattleDetector: mapped unsupported campaign troop id '" + originalId + "' to generic mission-safe fallback '" + genericFallbackId + "'.");
                    return genericFallbackId;
                }

                return originalId;
            }

            LogHeroRosterContext(originalId, rosterCharacters);

            string rosterSurrogateId = TryFindRosterSurrogateId(characterObject, rosterCharacters);
            if (!string.IsNullOrWhiteSpace(rosterSurrogateId) && !string.Equals(rosterSurrogateId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to roster surrogate '" + rosterSurrogateId + "'.");
                return rosterSurrogateId;
            }

            string typedHeroFallbackId = TryResolveTypedHeroFallbackId(characterObject as TaleWorlds.CampaignSystem.CharacterObject);
            if (!string.IsNullOrWhiteSpace(typedHeroFallbackId) && !string.Equals(typedHeroFallbackId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to typed fallback '" + typedHeroFallbackId + "'.");
                return typedHeroFallbackId;
            }

            object originalCharacter = TryGetPropertyValue(characterObject, "OriginalCharacter");
            string originalCharacterId = TryGetStringId(originalCharacter);
            if (!string.IsNullOrWhiteSpace(originalCharacterId) && !string.Equals(originalCharacterId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to OriginalCharacter '" + originalCharacterId + "'.");
                return originalCharacterId;
            }

            object templateCharacter = TryGetPropertyValue(characterObject, "Template");
            string templateCharacterId = TryGetStringId(templateCharacter);
            if (!string.IsNullOrWhiteSpace(templateCharacterId) && !string.Equals(templateCharacterId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to Template '" + templateCharacterId + "'.");
                return templateCharacterId;
            }

            object culture = TryGetPropertyValue(characterObject, "Culture");
            string cultureFallbackId = TryResolveCultureTroopId(culture);
            if (!string.IsNullOrWhiteSpace(cultureFallbackId) && !string.Equals(cultureFallbackId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to culture fallback '" + cultureFallbackId + "'.");
                return cultureFallbackId;
            }

            const string guaranteedVanillaFallbackId = "imperial_infantryman";
            ModLogger.Info("BattleDetector: no mission-safe fallback found for hero troop id '" + originalId + "'. Using guaranteed vanilla fallback '" + guaranteedVanillaFallbackId + "'.");
            return guaranteedVanillaFallbackId;
        }

        private static string TryResolveBanditMissionSafeCharacterId(object characterObject)
        {
            string originalId = TryGetStringId(characterObject);
            if (string.IsNullOrWhiteSpace(originalId))
                return null;

            string normalized = originalId.Trim().ToLowerInvariant();
            if (normalized.Contains("looter"))
                return "mp_skirmisher_empire_troop";
            if (normalized.Contains("sea_raider"))
                return "mp_heavy_infantry_sturgia_troop";
            if (normalized.Contains("forest_bandit"))
                return "mp_light_ranged_battania_troop";
            if (normalized.Contains("mountain_bandit"))
                return "mp_light_infantry_vlandia_troop";
            if (normalized.Contains("desert_bandit"))
                return "mp_skirmisher_aserai_troop";
            if (normalized.Contains("steppe_bandit"))
                return "mp_horse_archer_khuzait_troop";
            if (normalized.Contains("bandit"))
                return "mp_skirmisher_empire_troop";

            return null;
        }

        private static string TryResolveGenericMissionSafeCharacterId(object characterObject)
        {
            if (characterObject == null)
                return null;

            bool isMounted = TryGetBoolProperty(characterObject, "IsMounted");
            bool isRanged = TryGetCharacterIsRanged(characterObject);
            bool hasShield = TryGetCharacterHasShield(characterObject);
            bool hasThrown = TryGetCharacterHasThrown(characterObject);
            int tier = TryGetIntProperty(characterObject, "Tier");
            string cultureToken = TryMapCultureToMultiplayerToken(TryGetCultureId(characterObject));

            if (isMounted)
                return !string.IsNullOrWhiteSpace(cultureToken)
                    ? "mp_light_cavalry_" + cultureToken + "_troop"
                    : "mp_coop_light_cavalry_sturgia_troop";

            if (isRanged)
            {
                if (!string.IsNullOrWhiteSpace(cultureToken))
                    return tier >= 4
                        ? "mp_heavy_ranged_" + cultureToken + "_troop"
                        : "mp_light_ranged_" + cultureToken + "_troop";

                return tier >= 4
                    ? "mp_heavy_ranged_vlandia_troop"
                    : "mp_light_ranged_empire_troop";
            }

            if (!string.IsNullOrWhiteSpace(cultureToken))
            {
                if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier < 4 && hasShield)
                    return "mp_coop_light_infantry_empire_troop";

                if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier < 4 && hasThrown)
                    return "mp_skirmisher_empire_troop";

                if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier >= 4)
                    return "mp_coop_heavy_infantry_empire_troop";

                return tier >= 4
                    ? "mp_heavy_infantry_" + cultureToken + "_troop"
                    : "mp_light_infantry_" + cultureToken + "_troop";
            }

            return tier >= 4
                ? "mp_coop_heavy_infantry_empire_troop"
                : "mp_heavy_infantry_empire_troop";
        }

        private static string TryFindRosterSurrogateId(object heroCharacter, List<object> rosterCharacters)
        {
            if (heroCharacter == null || rosterCharacters == null || rosterCharacters.Count == 0)
                return null;

            bool heroMounted = TryGetBoolProperty(heroCharacter, "IsMounted");
            int heroTier = TryGetIntProperty(heroCharacter, "Tier");
            string heroCultureId = TryGetStringId(TryGetPropertyValue(heroCharacter, "Culture"));

            object bestCandidate = null;
            int bestScore = int.MinValue;

            foreach (object candidate in rosterCharacters)
            {
                if (candidate == null || TryGetBoolProperty(candidate, "IsHero"))
                    continue;

                string candidateId = TryGetStringId(candidate);
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                int score = 0;
                bool candidateMounted = TryGetBoolProperty(candidate, "IsMounted");
                int candidateTier = TryGetIntProperty(candidate, "Tier");
                string candidateCultureId = TryGetStringId(TryGetPropertyValue(candidate, "Culture"));

                if (candidateMounted == heroMounted)
                    score += 50;
                if (!string.IsNullOrWhiteSpace(heroCultureId) && string.Equals(heroCultureId, candidateCultureId, StringComparison.Ordinal))
                    score += 100;

                score -= Math.Abs(candidateTier - heroTier) * 5;

                if (bestCandidate == null || score > bestScore)
                {
                    bestCandidate = candidate;
                    bestScore = score;
                }
            }

            return TryGetStringId(bestCandidate);
        }

        private static string TryResolveTypedHeroFallbackId(TaleWorlds.CampaignSystem.CharacterObject heroCharacter)
        {
            if (heroCharacter == null)
                return null;

            TaleWorlds.Core.BasicCharacterObject[] candidates =
            {
                heroCharacter.OriginalCharacter,
                heroCharacter.Culture?.BasicTroop,
                heroCharacter.Culture?.EliteBasicTroop
            };

            foreach (TaleWorlds.Core.BasicCharacterObject candidate in candidates)
            {
                if (candidate == null)
                    continue;

                string candidateId = candidate.StringId;
                if (!string.IsNullOrWhiteSpace(candidateId))
                    return candidateId;
            }

            return null;
        }

        private static string TryResolveHeroRoleMissionSafeCharacterId(TaleWorlds.CampaignSystem.CharacterObject heroCharacter)
        {
            if (heroCharacter == null || !heroCharacter.IsHero)
                return null;

            string heroRole = TryGetHeroRole(heroCharacter);
            if (string.IsNullOrWhiteSpace(heroRole))
                return null;

            string cultureId = TryGetCultureId(heroCharacter) ?? heroCharacter.Culture?.StringId;
            string cultureToken = TryMapCultureToMultiplayerToken(cultureId);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            bool isMounted = heroCharacter.IsMounted;
            bool isRanged = heroCharacter.IsRanged;
            bool hasShield = TryGetCharacterHasShield(heroCharacter);
            bool hasThrown = TryGetCharacterHasThrown(heroCharacter);
            int effectiveTier = ComputeHeroRuntimeTier(heroCharacter, heroRole);

            string troopTemplateId = TryResolveRoleAwareTroopTemplateId(
                cultureToken,
                heroRole,
                isMounted,
                isRanged,
                hasShield,
                hasThrown,
                effectiveTier);

            return TryConvertTroopTemplateToHeroTemplate(troopTemplateId);
        }

        private static int ComputeHeroRuntimeTier(TaleWorlds.CampaignSystem.CharacterObject heroCharacter, string heroRole)
        {
            int tier = heroCharacter?.Tier ?? 0;
            int heroLevel = TryGetHeroLevel(heroCharacter);

            if (heroLevel >= 24)
                tier = Math.Max(tier, 5);
            else if (heroLevel >= 16)
                tier = Math.Max(tier, 4);
            else if (heroLevel >= 8)
                tier = Math.Max(tier, 3);

            if (string.Equals(heroRole, "lord", StringComparison.OrdinalIgnoreCase))
                return Math.Max(tier, 5);

            if (string.Equals(heroRole, "companion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(heroRole, "wanderer", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(tier, 4);
            }

            if (string.Equals(heroRole, "player", StringComparison.OrdinalIgnoreCase))
                return Math.Max(tier, 3);

            return Math.Max(tier, 1);
        }

        private static string TryResolveRoleAwareTroopTemplateId(
            string cultureToken,
            string heroRole,
            bool isMounted,
            bool isRanged,
            bool hasShield,
            bool hasThrown,
            int tier)
        {
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            bool isLord = string.Equals(heroRole, "lord", StringComparison.OrdinalIgnoreCase);

            if (isMounted)
            {
                string cavalryTemplateId = (isLord ? "mp_heavy_cavalry_" : "mp_light_cavalry_") + cultureToken + "_troop";
                return NormalizeKnownMissionSafeTemplateId(cavalryTemplateId);
            }

            if (isRanged)
            {
                string rangedTemplateId = ((isLord || tier >= 4) ? "mp_heavy_ranged_" : "mp_light_ranged_") + cultureToken + "_troop";
                return NormalizeKnownMissionSafeTemplateId(rangedTemplateId);
            }

            if (hasThrown)
                return NormalizeKnownMissionSafeTemplateId("mp_skirmisher_" + cultureToken + "_troop");

            if (string.Equals(cultureToken, "empire", StringComparison.OrdinalIgnoreCase))
            {
                if (isLord || tier >= 4)
                    return "mp_coop_heavy_infantry_empire_troop";

                if (hasShield)
                    return "mp_coop_light_infantry_empire_troop";
            }

            if (isLord || tier >= 5)
                return NormalizeKnownMissionSafeTemplateId("mp_shock_infantry_" + cultureToken + "_troop");

            return NormalizeKnownMissionSafeTemplateId(
                (tier >= 4 ? "mp_heavy_infantry_" : "mp_light_infantry_") + cultureToken + "_troop");
        }

        private static string TryConvertTroopTemplateToHeroTemplate(string troopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            string normalizedTroopTemplateId = NormalizeKnownMissionSafeTemplateId(troopTemplateId);
            if (string.IsNullOrWhiteSpace(normalizedTroopTemplateId))
                return null;

            if (normalizedTroopTemplateId.EndsWith("_hero", StringComparison.Ordinal))
                return normalizedTroopTemplateId;

            if (!normalizedTroopTemplateId.EndsWith("_troop", StringComparison.Ordinal))
                return normalizedTroopTemplateId;

            string heroTemplateId = normalizedTroopTemplateId.Substring(0, normalizedTroopTemplateId.Length - "_troop".Length) + "_hero";
            if (heroTemplateId.StartsWith("mp_", StringComparison.Ordinal))
                return heroTemplateId;

            try
            {
                BasicCharacterObject heroTemplate = MBObjectManager.Instance.GetObject<BasicCharacterObject>(heroTemplateId);
                if (heroTemplate != null)
                    return heroTemplate.StringId;
            }
            catch
            {
            }

            return normalizedTroopTemplateId;
        }

        private static string TryResolveMultiplayerSafeCharacterId(TaleWorlds.CampaignSystem.CharacterObject character)
        {
            if (character == null)
                return null;

            string cultureId = character.Culture?.StringId;
            string cultureToken = TryMapCultureToMultiplayerToken(cultureId);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            bool isMounted = character.IsMounted;
            bool isRanged = character.IsRanged;
            bool hasShield = TryGetCharacterHasShield(character);
            bool hasThrown = TryGetCharacterHasThrown(character);
            int tier = character.Tier;

            string coopControlTroopId = TryResolveCoopControlTroopId(cultureToken, isMounted, isRanged, tier);
            if (!string.IsNullOrWhiteSpace(coopControlTroopId))
                return coopControlTroopId;

            if (!isMounted && !isRanged && string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier < 4)
            {
                if (hasShield)
                    return "mp_coop_light_infantry_empire_troop";
                if (hasThrown)
                    return "mp_skirmisher_empire_troop";
            }

            var candidates = new List<string>();
            if (isMounted)
            {
                if (isRanged && string.Equals(cultureToken, "khuzait", StringComparison.Ordinal))
                    candidates.Add("mp_horse_archer_khuzait_troop");

                candidates.Add((tier >= 4 ? "mp_heavy_cavalry_" : "mp_light_cavalry_") + cultureToken + "_troop");
                candidates.Add("mp_light_cavalry_" + cultureToken + "_troop");
                candidates.Add("mp_heavy_cavalry_" + cultureToken + "_troop");
            }
            else if (isRanged)
            {
                candidates.Add((tier >= 4 ? "mp_heavy_ranged_" : "mp_light_ranged_") + cultureToken + "_troop");
                candidates.Add("mp_light_ranged_" + cultureToken + "_troop");
                candidates.Add("mp_heavy_ranged_" + cultureToken + "_troop");
                candidates.Add("mp_skirmisher_" + cultureToken + "_troop");
            }
            else
            {
                if (tier >= 5)
                    candidates.Add("mp_shock_infantry_" + cultureToken + "_troop");

                if (tier >= 4)
                    candidates.Add("mp_heavy_infantry_" + cultureToken + "_troop");

                candidates.Add("mp_light_infantry_" + cultureToken + "_troop");
                if (tier < 4)
                    candidates.Add("mp_heavy_infantry_" + cultureToken + "_troop");
                candidates.Add("mp_shock_infantry_" + cultureToken + "_troop");
                candidates.Add("mp_skirmisher_" + cultureToken + "_troop");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (!seen.Add(candidate))
                    continue;
                return NormalizeKnownMissionSafeTemplateId(candidate);
            }

            return null;
        }

        private static string NormalizeKnownMissionSafeTemplateId(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return candidate;

            switch (candidate)
            {
                case "mp_light_infantry_empire_troop":
                    return "mp_coop_light_infantry_empire_troop";
                default:
                    return candidate;
            }
        }

        private static string TryResolveCoopControlTroopId(string cultureToken, bool isMounted, bool isRanged, int tier)
        {
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            if (isMounted)
                return "mp_coop_light_cavalry_" + cultureToken + "_troop";

            if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && !isMounted && !isRanged && tier >= 4)
                return "mp_coop_heavy_infantry_empire_troop";

            return null;
        }

        private static string TryMapCultureToMultiplayerToken(string cultureId)
        {
            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            string normalized = cultureId.Trim().ToLowerInvariant();
            if (normalized.Contains("looter") || normalized.Contains("looters"))
                return "empire";
            if (normalized.Contains("sea_raider"))
                return "sturgia";
            if (normalized.Contains("forest_bandit"))
                return "battania";
            if (normalized.Contains("mountain_bandit"))
                return "vlandia";
            if (normalized.Contains("desert_bandit"))
                return "aserai";
            if (normalized.Contains("steppe_bandit"))
                return "khuzait";
            if (normalized.Contains("bandit"))
                return "empire";
            if (normalized.Contains("empire") || normalized.StartsWith("imperial"))
                return "empire";
            if (normalized.Contains("aserai"))
                return "aserai";
            if (normalized.Contains("battania") || normalized.Contains("battania") || normalized.StartsWith("battanian"))
                return "battania";
            if (normalized.Contains("khuzait"))
                return "khuzait";
            if (normalized.Contains("sturgia") || normalized.StartsWith("sturgian"))
                return "sturgia";
            if (normalized.Contains("vlandia") || normalized.StartsWith("vlandian"))
                return "vlandia";

            return null;
        }

        private static void LogHeroRosterContext(string heroId, List<object> rosterCharacters)
        {
            if (string.IsNullOrWhiteSpace(heroId) || rosterCharacters == null || rosterCharacters.Count == 0)
                return;

            try
            {
                List<string> candidates = new List<string>();
                foreach (object candidate in rosterCharacters)
                {
                    if (candidate == null)
                        continue;

                    string candidateId = TryGetStringId(candidate);
                    bool isHero = TryGetBoolProperty(candidate, "IsHero");
                    int tier = TryGetIntProperty(candidate, "Tier");
                    bool mounted = TryGetBoolProperty(candidate, "IsMounted");
                    candidates.Add((isHero ? "hero:" : "troop:") + candidateId + "/tier=" + tier + "/mounted=" + mounted);
                }

                ModLogger.Info("BattleDetector: hero '" + heroId + "' roster candidates = [" + string.Join(", ", candidates) + "].");
            }
            catch
            {
            }
        }

        private static string TryResolveCultureTroopId(object culture)
        {
            if (culture == null)
                return null;

            string[] candidateProperties =
            {
                "BasicTroop",
                "EliteBasicTroop",
                "MeleeMilitiaTroop",
                "RangedMilitiaTroop",
                "MeleeEliteMilitiaTroop",
                "RangedEliteMilitiaTroop"
            };

            foreach (string propertyName in candidateProperties)
            {
                object troop = TryGetPropertyValue(culture, propertyName);
                string troopId = TryGetStringId(troop);
                if (!string.IsNullOrWhiteSpace(troopId))
                    return troopId;
            }

            return null;
        }

        private static object TryGetPropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(instance, null);

                FieldInfo field = instance.GetType().GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetStringId(object instance)
        {
            if (instance is TaleWorlds.ObjectSystem.MBObjectBase mbObject)
                return mbObject.StringId;

            object value = TryGetPropertyValue(instance, "StringId");
            return value?.ToString();
        }

        private static bool TryGetBoolProperty(object instance, string propertyName)
        {
            object value = TryGetPropertyValue(instance, propertyName);
            return value is bool b && b;
        }

        private static string TryGetCultureId(object instance)
        {
            if (instance is BasicCharacterObject basicCharacter)
                return basicCharacter.Culture?.StringId;

            object culture = TryGetPropertyValue(instance, "Culture");
            return TryGetStringId(culture);
        }

        private static void ApplyHeroIdentitySnapshot(TroopStackInfo troop, object characterObject)
        {
            if (troop == null || characterObject == null)
                return;

            troop.HeroId = TryGetHeroId(characterObject);
            troop.HeroRole = TryGetHeroRole(characterObject);
            troop.HeroOccupationId = TryGetHeroOccupationId(characterObject);
            troop.HeroClanId = TryGetHeroClanId(characterObject);
            troop.HeroTemplateId = TryGetHeroTemplateId(characterObject);
            troop.HeroLevel = TryGetHeroLevel(characterObject);
            troop.HeroAge = TryGetHeroAge(characterObject);
            troop.HeroIsFemale = TryGetHeroIsFemale(characterObject);
        }

        private static Hero TryResolveHeroObject(object instance)
        {
            if (instance is Hero hero)
                return hero;

            if (instance is CharacterObject characterObject)
                return characterObject.HeroObject;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return heroObject as Hero;
        }

        private static string TryGetHeroId(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return hero.StringId;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return TryGetStringId(heroObject);
        }

        private static string TryGetHeroRole(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero == null)
                return null;

            bool isMainHero = false;
            try
            {
                isMainHero = Hero.MainHero != null && ReferenceEquals(hero, Hero.MainHero);
            }
            catch
            {
                isMainHero = TryGetBoolProperty(hero, "IsMainHero");
            }

            if (isMainHero || hero.IsHumanPlayerCharacter)
                return "player";

            if (hero.IsPlayerCompanion || hero.CompanionOf != null)
                return "companion";

            if (hero.IsLord)
                return "lord";

            if (hero.IsWanderer)
                return "wanderer";

            string occupationId = NormalizeHeroIdentityToken(hero.Occupation.ToString());
            return string.IsNullOrWhiteSpace(occupationId)
                ? "hero"
                : occupationId;
        }

        private static string TryGetHeroOccupationId(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return NormalizeHeroIdentityToken(hero.Occupation.ToString());

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object occupation = TryGetPropertyValue(heroObject, "Occupation");
            return NormalizeHeroIdentityToken(occupation?.ToString());
        }

        private static string TryGetHeroClanId(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return hero.Clan?.StringId;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object clan = TryGetPropertyValue(heroObject, "Clan");
            return TryGetStringId(clan);
        }

        private static string TryGetHeroTemplateId(object instance)
        {
            if (instance is CharacterObject characterObject)
            {
                string templateId = TryGetStringId(TryGetPropertyValue(characterObject, "Template"));
                if (!string.IsNullOrWhiteSpace(templateId))
                    return templateId;

                string originalCharacterId = characterObject.OriginalCharacter?.StringId;
                if (!string.IsNullOrWhiteSpace(originalCharacterId))
                    return originalCharacterId;

                if (!string.IsNullOrWhiteSpace(characterObject.StringId))
                    return characterObject.StringId;
            }

            Hero hero = TryResolveHeroObject(instance);
            if (hero?.CharacterObject != null)
            {
                string heroTemplateId = TryGetStringId(TryGetPropertyValue(hero.CharacterObject, "Template"));
                if (!string.IsNullOrWhiteSpace(heroTemplateId))
                    return heroTemplateId;

                if (hero.CharacterObject.OriginalCharacter != null)
                    return hero.CharacterObject.OriginalCharacter.StringId;

                if (!string.IsNullOrWhiteSpace(hero.CharacterObject.StringId))
                    return hero.CharacterObject.StringId;
            }

            object originalCharacter = TryGetPropertyValue(instance, "OriginalCharacter");
            string originalCharacterIdFallback = TryGetStringId(originalCharacter);
            if (!string.IsNullOrWhiteSpace(originalCharacterIdFallback))
                return originalCharacterIdFallback;

            string templateIdFallback = TryGetStringId(TryGetPropertyValue(instance, "Template"));
            if (!string.IsNullOrWhiteSpace(templateIdFallback))
                return templateIdFallback;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object heroCharacter = TryGetPropertyValue(heroObject, "CharacterObject");
            string heroCharacterTemplateId = TryGetStringId(TryGetPropertyValue(heroCharacter, "Template"));
            if (!string.IsNullOrWhiteSpace(heroCharacterTemplateId))
                return heroCharacterTemplateId;

            object heroOriginalCharacter = TryGetPropertyValue(heroCharacter, "OriginalCharacter");
            string heroOriginalCharacterId = TryGetStringId(heroOriginalCharacter);
            if (!string.IsNullOrWhiteSpace(heroOriginalCharacterId))
                return heroOriginalCharacterId;

            string heroCharacterId = TryGetStringId(heroCharacter);
            if (!string.IsNullOrWhiteSpace(heroCharacterId))
                return heroCharacterId;

            return TryGetHeroId(instance);
        }

        private static int TryGetHeroLevel(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return TryGetIntProperty(hero, "Level");

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return TryGetIntProperty(heroObject, "Level");
        }

        private static float TryGetHeroAge(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return TryGetFloatProperty(hero, "Age");

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return TryGetFloatProperty(heroObject, "Age");
        }

        private static bool TryGetHeroIsFemale(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return TryGetBoolProperty(hero, "IsFemale") || TryGetBoolProperty(hero.CharacterObject, "IsFemale");

            if (instance is CharacterObject characterObject)
                return characterObject.IsFemale;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            if (TryGetBoolProperty(heroObject, "IsFemale"))
                return true;

            object heroCharacter = TryGetPropertyValue(heroObject, "CharacterObject");
            return TryGetBoolProperty(heroCharacter, "IsFemale");
        }

        private static string NormalizeHeroIdentityToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = new List<char>(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current) || current == '-')
                {
                    if (normalized.Count > 0 && normalized[normalized.Count - 1] != '_')
                        normalized.Add('_');
                    continue;
                }

                if (char.IsUpper(current) && i > 0)
                {
                    char previous = value[i - 1];
                    char next = i + 1 < value.Length ? value[i + 1] : '\0';
                    if (char.IsLower(previous) || (next != '\0' && char.IsLower(next)))
                        normalized.Add('_');
                }

                normalized.Add(char.ToLowerInvariant(current));
            }

            return new string(normalized.ToArray()).Trim('_');
        }

        private static void ApplyCombatEquipmentSnapshot(TroopStackInfo troop, object characterObject)
        {
            if (troop == null || characterObject == null)
                return;

            object equipment = TryResolvePrimaryCombatEquipment(characterObject);
            if (equipment == null)
                return;

            troop.CombatItem0Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon0);
            troop.CombatItem1Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon1);
            troop.CombatItem2Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon2);
            troop.CombatItem3Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon3);
            troop.CombatHeadId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Head);
            troop.CombatBodyId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Body);
            troop.CombatLegId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Leg);
            troop.CombatGlovesId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Gloves);
            troop.CombatCapeId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Cape);
            troop.CombatHorseId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Horse);
            troop.CombatHorseHarnessId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.HorseHarness);
        }

        private static object TryResolvePrimaryCombatEquipment(object instance)
        {
            if (instance == null)
                return null;

            foreach (string propertyName in new[] { "FirstBattleEquipment", "BattleEquipment", "SecondBattleEquipment", "Equipment" })
            {
                object equipment = TryGetPropertyValue(instance, propertyName);
                if (equipment != null && !TryGetBoolProperty(equipment, "IsCivilian"))
                    return equipment;
            }

            foreach (object equipment in EnumerateCharacterEquipments(instance))
            {
                if (equipment != null && !TryGetBoolProperty(equipment, "IsCivilian"))
                    return equipment;
            }

            foreach (object equipment in EnumerateCharacterEquipments(instance))
            {
                if (equipment != null)
                    return equipment;
            }

            return null;
        }

        private static string TryGetEquipmentSlotItemId(object equipment, EquipmentIndex slot)
        {
            if (equipment == null)
                return null;

            try
            {
                MethodInfo getEquipmentFromSlot = equipment.GetType().GetMethod(
                    "GetEquipmentFromSlot",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(EquipmentIndex) },
                    null);

                if (getEquipmentFromSlot != null)
                {
                    object equipmentElement = getEquipmentFromSlot.Invoke(equipment, new object[] { slot });
                    string itemId = TryGetEquipmentElementItemId(equipmentElement);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        return itemId;
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo getEquipmentElement = equipment.GetType().GetMethod(
                    "GetEquipmentElement",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(EquipmentIndex) },
                    null);

                if (getEquipmentElement != null)
                {
                    object equipmentElement = getEquipmentElement.Invoke(equipment, new object[] { slot });
                    string itemId = TryGetEquipmentElementItemId(equipmentElement);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        return itemId;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo indexer = equipment.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(property =>
                    {
                        ParameterInfo[] parameters = property.GetIndexParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(EquipmentIndex);
                    });

                if (indexer != null)
                {
                    object equipmentElement = indexer.GetValue(equipment, new object[] { slot });
                    string itemId = TryGetEquipmentElementItemId(equipmentElement);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        return itemId;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string TryGetEquipmentElementItemId(object equipmentElement)
        {
            if (equipmentElement == null)
                return null;

            object item = TryGetPropertyValue(equipmentElement, "Item") ?? TryGetPropertyValue(equipmentElement, "ItemObject");
            return TryGetStringId(item);
        }

        private static string FormatCombatEquipmentSummary(TroopStackInfo troop)
        {
            if (troop == null)
                return string.Empty;

            List<string> parts = new List<string>();
            AddCombatEquipmentSummaryPart(parts, "Item0", troop.CombatItem0Id);
            AddCombatEquipmentSummaryPart(parts, "Item1", troop.CombatItem1Id);
            AddCombatEquipmentSummaryPart(parts, "Item2", troop.CombatItem2Id);
            AddCombatEquipmentSummaryPart(parts, "Item3", troop.CombatItem3Id);
            AddCombatEquipmentSummaryPart(parts, "Head", troop.CombatHeadId);
            AddCombatEquipmentSummaryPart(parts, "Body", troop.CombatBodyId);
            AddCombatEquipmentSummaryPart(parts, "Leg", troop.CombatLegId);
            AddCombatEquipmentSummaryPart(parts, "Gloves", troop.CombatGlovesId);
            AddCombatEquipmentSummaryPart(parts, "Cape", troop.CombatCapeId);
            AddCombatEquipmentSummaryPart(parts, "Horse", troop.CombatHorseId);
            AddCombatEquipmentSummaryPart(parts, "HorseHarness", troop.CombatHorseHarnessId);

            return parts.Count == 0
                ? " eq=[]"
                : " eq=[" + string.Join(", ", parts) + "]";
        }

        private static string FormatHeroIdentitySummary(TroopStackInfo troop)
        {
            if (troop == null)
                return string.Empty;

            bool hasHeroIdentity =
                !string.IsNullOrWhiteSpace(troop.HeroId) ||
                !string.IsNullOrWhiteSpace(troop.HeroRole) ||
                !string.IsNullOrWhiteSpace(troop.HeroOccupationId) ||
                !string.IsNullOrWhiteSpace(troop.HeroClanId);

            if (!hasHeroIdentity)
                return troop.IsHero ? " hero_role=hero" : string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(troop.HeroId))
                parts.Add("hero_id=" + troop.HeroId);
            if (!string.IsNullOrWhiteSpace(troop.HeroRole))
                parts.Add("hero_role=" + troop.HeroRole);
            if (!string.IsNullOrWhiteSpace(troop.HeroOccupationId))
                parts.Add("occupation=" + troop.HeroOccupationId);
            if (!string.IsNullOrWhiteSpace(troop.HeroClanId))
                parts.Add("clan=" + troop.HeroClanId);
            if (!string.IsNullOrWhiteSpace(troop.HeroTemplateId))
                parts.Add("template=" + troop.HeroTemplateId);
            if (troop.HeroLevel > 0)
                parts.Add("level=" + troop.HeroLevel);
            if (troop.HeroAge > 0.01f)
                parts.Add("age=" + troop.HeroAge.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            parts.Add("female=" + troop.HeroIsFemale);

            return parts.Count == 0
                ? string.Empty
                : " " + string.Join(" ", parts);
        }

        private static void AddCombatEquipmentSummaryPart(List<string> parts, string label, string itemId)
        {
            if (parts == null || string.IsNullOrWhiteSpace(itemId))
                return;

            parts.Add(label + "=" + itemId);
        }

        private static bool TryGetCharacterIsRanged(object instance)
        {
            if (instance is BasicCharacterObject basicCharacter)
                return basicCharacter.IsRanged;

            return TryGetBoolProperty(instance, "IsRanged");
        }

        private static bool TryGetCharacterHasShield(object instance)
        {
            return TryCharacterHasWeaponClass(instance, WeaponClass.SmallShield) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.LargeShield);
        }

        private static bool TryGetCharacterHasThrown(object instance)
        {
            return TryCharacterHasWeaponClass(instance, WeaponClass.Javelin) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.ThrowingAxe) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.ThrowingKnife) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.Stone) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.SlingStone);
        }

        private static bool TryCharacterHasWeaponClass(object instance, WeaponClass weaponClass)
        {
            foreach (object equipment in EnumerateCharacterEquipments(instance))
            {
                if (equipment == null)
                    continue;

                try
                {
                    MethodInfo hasWeaponOfClass = equipment.GetType().GetMethod(
                        "HasWeaponOfClass",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(WeaponClass) },
                        null);

                    if (hasWeaponOfClass == null)
                        continue;

                    object result = hasWeaponOfClass.Invoke(equipment, new object[] { weaponClass });
                    if (result is bool hasWeapon && hasWeapon)
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static IEnumerable<object> EnumerateCharacterEquipments(object instance)
        {
            if (instance == null)
                yield break;

            var yielded = new HashSet<object>();
            foreach (string propertyName in new[] { "Equipment", "BattleEquipment", "FirstBattleEquipment", "SecondBattleEquipment", "CivilianEquipment" })
            {
                object equipment = TryGetPropertyValue(instance, propertyName);
                if (equipment != null && yielded.Add(equipment))
                    yield return equipment;
            }

            object allEquipments = TryGetPropertyValue(instance, "AllEquipments");
            if (allEquipments is System.Collections.IEnumerable enumerable && !(allEquipments is string))
            {
                foreach (object equipment in enumerable)
                {
                    if (equipment != null && yielded.Add(equipment))
                        yield return equipment;
                }
            }
        }

        private static int TryGetIntProperty(object instance, string propertyName)
        {
            object value = TryGetPropertyValue(instance, propertyName);
            return value is int i ? i : 0;
        }

        private static float TryGetFloatProperty(object instance, string propertyName)
        {
            object value = TryGetPropertyValue(instance, propertyName);
            if (value is float f)
                return f;
            if (value is double d)
                return (float)d;
            if (value is decimal m)
                return (float)m;
            if (value is int i)
                return i;
            if (value is long l)
                return l;

            try
            {
                return value != null ? Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture) : 0f;
            }
            catch
            {
                return 0f;
            }
        }
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

