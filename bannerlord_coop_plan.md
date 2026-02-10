# План розробки моду Bannerlord Coop Spectator
**Мод для кооперативної гри в Mount & Blade II: Bannerlord**

---

## 📋 Огляд проєкту

### Концепція
Створення спрощеного кооперативного моду який дозволяє:
- **Хост** грає звичайну сингплеєрну кампанію
- **Клієнти** пасивно спостерігають за грою хоста на карті
- **У битвах** всі клієнти приєднуються і грають за юнітів з армії хоста

### Переваги підходу
✅ Не ламає існуючу механіку кампанії  
✅ Використовує вбудований мультиплеєр для битв  
✅ Мінімальні зміни = менше багів при оновленнях  
✅ Простіша архітектура ніж повний кооп  

### Цільова аудиторія
- Гравці які хочуть разом грати у битвах без складного full coop
- ~45,000 учасників Discord Bannerlord Coop які чекають робочий мод
- Друзі які хочуть просто "зайти і побитися"

---

## ⏱️ Загальний таймлайн

**При роботі 2-3 години/день:**
- **Місяці 1-2**: Навчання + базова інфраструктура
- **Місяці 3-4**: Spectator mode + перехід до битв
- **Місяці 5-6**: Тестування, полірування, реліз

**Інтенсивна робота (6+ годин/день):**
- **Тижні 1-4**: Навчання + базовий прототип
- **Тижні 5-8**: Spectator + battle integration
- **Тижні 9-12**: Реліз готової версії

---

## 🎯 Детальна розбивка по етапах

---

## ЕТАП 1: Підготовка та навчання (2-4 тижні)

### Тиждень 1-2: Вивчення основ

#### 1.1 Встановлення середовища розробки
**Час: 4-6 годин**

**Що потрібно встановити:**
```
✓ Visual Studio 2022 Community (безкоштовна)
✓ .NET SDK 6.0+
✓ Bannerlord гра (Steam)
✓ Vortex Mod Manager або ручне встановлення модів
```

**Налаштування проєкту:**
```
1. Створити папку проєкту: BannerlordCoopSpectator/
2. Додати посилання на DLL гри:
   - TaleWorlds.Core.dll
   - TaleWorlds.MountAndBlade.dll
   - TaleWorlds.CampaignSystem.dll
   - TaleWorlds.Library.dll
3. Налаштувати компіляцію в папку модів Bannerlord
```

**Команди для Cursor:**
```
"Create C# project structure for Bannerlord mod"
"Show me how to reference Bannerlord DLL files"
"Generate SubModule.xml template for Bannerlord"
```

#### 1.2 Перший "Hello World" мод
**Час: 6-8 годин**

**Завдання:**
Створити мод який виводить повідомлення коли гравець входить у битву.

**Файли які треба створити:**

**SubModule.xml** (кореневий файл моду):
```xml
<Module>
  <Name value="Coop Spectator"/>
  <Id value="CoopSpectator"/>
  <Version value="v1.0.0"/>
  <SingleplayerModule value="true"/>
  <MultiplayerModule value="false"/>
  <DependedModules>
    <DependedModule Id="Native"/>
    <DependedModule Id="SandBoxCore"/>
    <DependedModule Id="Sandbox"/>
    <DependedModule Id="StoryMode"/>
  </DependedModules>
  <SubModules>
    <SubModule>
      <Name value="CoopSpectator"/>
      <DLLName value="CoopSpectator.dll"/>
      <SubModuleClassType value="CoopSpectator.SubModule"/>
      <Tags>
        <Tag key="DedicatedServerType" value="none"/>
      </Tags>
    </SubModule>
  </SubModules>
</Module>
```

**SubModule.cs** (головний клас моду):
```csharp
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace CoopSpectator
{
    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            InformationManager.DisplayMessage(
                new InformationMessage("Coop Spectator mod loaded!")
            );
        }
        
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            
            if (game.GameType is Campaign)
            {
                CampaignGameStarter starter = (CampaignGameStarter)gameStarterObject;
                // Тут будемо додавати behaviours пізніше
            }
        }
    }
}
```

**Тестування:**
1. Скомпілювати мод
2. Запустити Bannerlord
3. Активувати мод у Launcher
4. Завантажити кампанію
5. Побачити повідомлення "Coop Spectator mod loaded!"

**Команди для Cursor:**
```
"How to detect battle start in Bannerlord using Harmony"
"Create Bannerlord behavior that logs when player enters mission"
"Show me Bannerlord Mission lifecycle events"
```

#### 1.3 Вивчення Bannerlord API
**Час: 10-12 годин**

**Ключові класи для вивчення:**

**Campaign система:**
```csharp
Campaign.Current                    // Поточна кампанія
Campaign.Current.MainParty          // Армія гравця
Hero.MainHero                       // Персонаж гравця
MobileParty.MainParty.Position2D    // Позиція на карті
```

**Mission (битва) система:**
```csharp
Mission.Current                     // Поточна місія (битва)
Mission.OnMissionModeChange         // Подія зміни режиму місії
Agent.Main                          // Агент (персонаж) гравця
Mission.Agents                      // Всі агенти у битві
```

**Networking:**
```csharp
GameNetwork.IsServer                // Чи є сервером?
GameNetwork.IsClient                // Чи є клієнтом?
GameNetwork.NetworkPeers            // Підключені гравці
```

**Практичні вправи:**
1. Створити behavior який логує позицію гравця кожні 5 секунд
2. Створити код який витягує склад армії гравця
3. Підписатися на події входу/виходу з битви

**Команди для Cursor:**
```
"Bannerlord CampaignBehaviorBase example"
"How to get player party troops in Bannerlord"
"Bannerlord Mission events documentation"
```

### Тиждень 3-4: Базовий networking

#### 1.4 Простий TCP сервер/клієнт
**Час: 12-15 годин**

**Завдання:**
Створити базовий мережевий код який може передавати дані між двома Bannerlord клієнтами.

**Архітектура:**
```
[Хост Bannerlord] ←→ [TCP Server] ←→ [TCP Client] ←→ [Клієнт Bannerlord]
```

**NetworkManager.cs:**
```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CoopSpectator.Network
{
    public class NetworkManager
    {
        private TcpListener _server;
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _listenThread;
        private bool _isRunning = false;
        
        // Хост створює сервер
        public void StartServer(int port = 7777)
        {
            _server = new TcpListener(IPAddress.Any, port);
            _server.Start();
            _isRunning = true;
            
            _listenThread = new Thread(AcceptClients);
            _listenThread.Start();
            
            InformationManager.DisplayMessage(
                new InformationMessage($"Server started on port {port}")
            );
        }
        
        private void AcceptClients()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = _server.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.Start();
                }
                catch (Exception ex)
                {
                    // Log error
                }
            }
        }
        
        private void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            
            while (_isRunning)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        OnMessageReceived(message);
                    }
                }
                catch (Exception ex)
                {
                    break;
                }
            }
        }
        
        // Клієнт підключається до хоста
        public void ConnectToServer(string ip, int port = 7777)
        {
            try
            {
                _client = new TcpClient(ip, port);
                _stream = _client.GetStream();
                _isRunning = true;
                
                _listenThread = new Thread(ReceiveMessages);
                _listenThread.Start();
                
                InformationManager.DisplayMessage(
                    new InformationMessage($"Connected to {ip}:{port}")
                );
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage($"Failed to connect: {ex.Message}")
                );
            }
        }
        
        private void ReceiveMessages()
        {
            byte[] buffer = new byte[1024];
            
            while (_isRunning)
            {
                try
                {
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        OnMessageReceived(message);
                    }
                }
                catch (Exception ex)
                {
                    break;
                }
            }
        }
        
        // Відправити повідомлення
        public void SendMessage(string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                _stream?.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                // Log error
            }
        }
        
        private void OnMessageReceived(string message)
        {
            // Тут будемо обробляти різні типи повідомлень
            InformationManager.DisplayMessage(
                new InformationMessage($"Received: {message}")
            );
        }
        
        public void Shutdown()
        {
            _isRunning = false;
            _stream?.Close();
            _client?.Close();
            _server?.Stop();
        }
    }
}
```

**Тестування networking:**
1. Запустити дві копії Bannerlord
2. На одній натиснути "Host Server" (консольна команда)
3. На другій "Connect to 127.0.0.1"
4. Відправити тестове повідомлення
5. Перевірити що воно отримане

**Команди для Cursor:**
```
"Add JSON serialization to C# TCP networking"
"Create message protocol for game state sync"
"Handle TCP connection errors and reconnection"
```

---

## ЕТАП 2: Spectator Mode (2-3 тижні)

### 2.1 Broadcaster - відправка даних хоста
**Час: 8-10 годин**

**Завдання:**
Хост періодично відправляє свій стан клієнтам.

**HostStateBroadcaster.cs:**
```csharp
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using Newtonsoft.Json;

namespace CoopSpectator.Campaign
{
    public class HostStateBroadcaster : CampaignBehaviorBase
    {
        private NetworkManager _network;
        private float _lastBroadcastTime = 0f;
        private const float BROADCAST_INTERVAL = 2.0f; // секунди
        
        public HostStateBroadcaster(NetworkManager network)
        {
            _network = network;
        }
        
        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }
        
        public override void SyncData(IDataStore dataStore)
        {
            // Не потрібно серіалізувати
        }
        
        private void OnHourlyTick()
        {
            float currentTime = Campaign.CurrentTime;
            
            if (currentTime - _lastBroadcastTime >= BROADCAST_INTERVAL)
            {
                BroadcastState();
                _lastBroadcastTime = currentTime;
            }
        }
        
        private void BroadcastState()
        {
            var state = new HostGameState
            {
                Position = new Vector2D
                {
                    X = MobileParty.MainParty.Position2D.X,
                    Y = MobileParty.MainParty.Position2D.Y
                },
                CurrentAction = GetCurrentAction(),
                ArmySize = MobileParty.MainParty.MemberRoster.TotalManCount,
                TimeOfDay = Campaign.CurrentTime % 24,
                InBattle = Mission.Current != null
            };
            
            string json = JsonConvert.SerializeObject(state);
            _network.SendMessage("STATE:" + json);
        }
        
        private string GetCurrentAction()
        {
            if (Mission.Current != null)
                return "IN_BATTLE";
            
            if (MobileParty.MainParty.CurrentSettlement != null)
                return "IN_SETTLEMENT";
            
            if (MobileParty.MainParty.IsMoving)
                return "TRAVELING";
            
            return "IDLE";
        }
    }
    
    [Serializable]
    public class HostGameState
    {
        public Vector2D Position { get; set; }
        public string CurrentAction { get; set; }
        public int ArmySize { get; set; }
        public float TimeOfDay { get; set; }
        public bool InBattle { get; set; }
    }
    
    [Serializable]
    public class Vector2D
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
```

**Інтеграція з SubModule:**
```csharp
protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
{
    if (game.GameType is Campaign)
    {
        CampaignGameStarter starter = (CampaignGameStarter)gameStarterObject;
        
        if (IsHosting) // Додати прапорець
        {
            starter.AddBehavior(new HostStateBroadcaster(_networkManager));
        }
    }
}
```

### 2.2 Spectator UI - відображення для клієнтів
**Час: 12-15 годин**

**Завдання:**
Створити UI який показує карту і позицію хоста.

**SpectatorMapView.cs:**
```csharp
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    public class SpectatorMapView : ViewModel
    {
        private float _hostX;
        private float _hostY;
        private string _hostAction;
        private int _armySize;
        
        [DataSourceProperty]
        public float HostX
        {
            get => _hostX;
            set
            {
                if (_hostX != value)
                {
                    _hostX = value;
                    OnPropertyChangedWithValue(value, "HostX");
                }
            }
        }
        
        [DataSourceProperty]
        public float HostY
        {
            get => _hostY;
            set
            {
                if (_hostY != value)
                {
                    _hostY = value;
                    OnPropertyChangedWithValue(value, "HostY");
                }
            }
        }
        
        [DataSourceProperty]
        public string HostAction
        {
            get => _hostAction;
            set
            {
                if (_hostAction != value)
                {
                    _hostAction = value;
                    OnPropertyChangedWithValue(value, "HostAction");
                }
            }
        }
        
        [DataSourceProperty]
        public int ArmySize
        {
            get => _armySize;
            set
            {
                if (_armySize != value)
                {
                    _armySize = value;
                    OnPropertyChangedWithValue(value, "ArmySize");
                }
            }
        }
        
        public void UpdateFromState(HostGameState state)
        {
            HostX = state.Position.X;
            HostY = state.Position.Y;
            HostAction = state.CurrentAction;
            ArmySize = state.ArmySize;
        }
    }
}
```

**SpectatorBehavior.cs:**
```csharp
using TaleWorlds.CampaignSystem;
using Newtonsoft.Json;

namespace CoopSpectator.Campaign
{
    public class SpectatorBehavior : CampaignBehaviorBase
    {
        private NetworkManager _network;
        private SpectatorMapView _mapView;
        
        public SpectatorBehavior(NetworkManager network)
        {
            _network = network;
            _network.OnMessageReceived += HandleMessage;
            _mapView = new SpectatorMapView();
        }
        
        private void HandleMessage(string message)
        {
            if (message.StartsWith("STATE:"))
            {
                string json = message.Substring(6);
                HostGameState state = JsonConvert.DeserializeObject<HostGameState>(json);
                _mapView.UpdateFromState(state);
                
                // Якщо хост вступив у битву - готуватися приєднатися
                if (state.InBattle)
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage("Host entering battle! Prepare to join...")
                    );
                }
            }
        }
        
        public override void RegisterEvents()
        {
            // Підписатися на події якщо потрібно
        }
        
        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
```

### 2.3 Блокування контролю для клієнтів
**Час: 4-6 годин**

**Завдання:**
Заборонити клієнтам керувати грою на карті.

**Використання Harmony для патчингу:**
```csharp
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace CoopSpectator.Patches
{
    [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior))]
    [HarmonyPatch("game_menu_town_menu_on_init")]
    public class BlockTownMenuPatch
    {
        static bool Prefix()
        {
            // Якщо клієнт у spectator mode - блокувати
            if (CoopManager.Instance.IsSpectating)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("You are spectating. Only host can control the campaign.")
                );
                return false; // Скасувати оригінальний метод
            }
            return true; // Дозволити якщо хост
        }
    }
    
    // Блокувати рух партії
    [HarmonyPatch(typeof(MobileParty))]
    [HarmonyPatch("SetMoveGoToPoint")]
    public class BlockPartyMovementPatch
    {
        static bool Prefix()
        {
            if (CoopManager.Instance.IsSpectating)
            {
                return false;
            }
            return true;
        }
    }
}
```

**Інтеграція Harmony в SubModule:**
```csharp
using HarmonyLib;

protected override void OnSubModuleLoad()
{
    base.OnSubModuleLoad();
    
    var harmony = new Harmony("com.coopspectator.mod");
    harmony.PatchAll();
}
```

---

## ЕТАП 3: Battle Integration (4-6 тижнів)

### 3.1 Детекція початку битви у хоста
**Час: 6-8 годин**

**BattleDetector.cs:**
```csharp
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    public class BattleDetector : CampaignBehaviorBase
    {
        private NetworkManager _network;
        private bool _inBattle = false;
        
        public BattleDetector(NetworkManager network)
        {
            _network = network;
        }
        
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }
        
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Підписатися на події місії
        }
        
        public override void SyncData(IDataStore dataStore)
        {
        }
        
        // Викликається перед входом у битву
        [CommandLineFunctionality.CommandLineArgumentFunction("battle_starting", "coop")]
        public static string OnBattleStarting(List<string> args)
        {
            // Сповістити клієнтів
            BattleStartMessage message = new BattleStartMessage
            {
                MapScene = Campaign.Current.MapSceneWrapper.GetMapSceneName(),
                PlayerSide = PlayerEncounter.Battle.PlayerSide,
                Troops = GetPartyTroops()
            };
            
            string json = JsonConvert.SerializeObject(message);
            NetworkManager.Instance.SendMessage("BATTLE_START:" + json);
            
            return "Battle invitation sent to clients";
        }
        
        private static List<TroopInfo> GetPartyTroops()
        {
            List<TroopInfo> troops = new List<TroopInfo>();
            
            foreach (var element in MobileParty.MainParty.MemberRoster.GetTroopRoster())
            {
                for (int i = 0; i < element.Number; i++)
                {
                    troops.Add(new TroopInfo
                    {
                        CharacterId = element.Character.StringId,
                        TroopName = element.Character.Name.ToString(),
                        Tier = element.Character.Tier,
                        IsMounted = element.Character.IsMounted,
                        IsHero = element.Character.IsHero
                    });
                }
            }
            
            return troops;
        }
    }
    
    [Serializable]
    public class BattleStartMessage
    {
        public string MapScene { get; set; }
        public BattleSideEnum PlayerSide { get; set; }
        public List<TroopInfo> Troops { get; set; }
    }
    
    [Serializable]
    public class TroopInfo
    {
        public string CharacterId { get; set; }
        public string TroopName { get; set; }
        public int Tier { get; set; }
        public bool IsMounted { get; set; }
        public bool IsHero { get; set; }
    }
}
```

### 3.2 Конвертація в мультиплеєрну битву
**Час: 15-20 годин** (найскладніша частина!)

**MissionConverter.cs:**
```csharp
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Engine;

namespace CoopSpectator.Mission
{
    public class MissionConverter
    {
        // Взяти поточну сингплеєрну місію і конвертувати у MP
        public static void ConvertToMultiplayer()
        {
            // 1. Зберегти стан поточної битви
            Mission currentMission = Mission.Current;
            if (currentMission == null)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("No active battle to convert")
                );
                return;
            }
            
            // 2. Отримати параметри місії
            string sceneName = currentMission.SceneName;
            string missionName = currentMission.Name;
            
            // 3. Завершити сингплеєрну місію
            currentMission.EndMission();
            
            // 4. Створити мультиплеєрну місію з тими ж параметрами
            MissionInitializerRecord initRecord = new MissionInitializerRecord(sceneName);
            initRecord.PlayingInCampaignMode = false; // MP режим
            initRecord.DoNotUseLoadingScreen = false;
            
            // 5. Додати мультиплеєрні behaviors
            // Це найскладніша частина - треба вивчити як працює MP система
        }
    }
}
```

**ПРИМІТКА:** Ця частина потребує глибокого вивчення того як Bannerlord створює MP битви. Доведеться:
1. Вивчити вихідний код мультиплеєрних модів
2. Зрозуміти різницю між SP і MP missions
3. Навчитися спавнити агентів для клієнтів

**Команди для Cursor:**
```
"Bannerlord how to create multiplayer mission from code"
"Difference between Campaign Mission and Multiplayer Mission"
"Bannerlord Agent spawning in custom missions"
```

### 3.3 Меню вибору юніта для клієнтів
**Час: 10-12 годин**

**TroopSelectionUI.cs:**
```csharp
using TaleWorlds.Library;
using System.Collections.Generic;

namespace CoopSpectator.UI
{
    public class TroopSelectionVM : ViewModel
    {
        private MBBindingList<TroopCardVM> _availableTroops;
        private TroopCardVM _selectedTroop;
        
        [DataSourceProperty]
        public MBBindingList<TroopCardVM> AvailableTroops
        {
            get => _availableTroops;
            set
            {
                if (_availableTroops != value)
                {
                    _availableTroops = value;
                    OnPropertyChangedWithValue(value, "AvailableTroops");
                }
            }
        }
        
        [DataSourceProperty]
        public TroopCardVM SelectedTroop
        {
            get => _selectedTroop;
            set
            {
                if (_selectedTroop != value)
                {
                    _selectedTroop = value;
                    OnPropertyChangedWithValue(value, "SelectedTroop");
                }
            }
        }
        
        public TroopSelectionVM(List<TroopInfo> troops)
        {
            AvailableTroops = new MBBindingList<TroopCardVM>();
            
            foreach (var troop in troops)
            {
                AvailableTroops.Add(new TroopCardVM(troop, OnTroopSelected));
            }
        }
        
        private void OnTroopSelected(TroopCardVM troop)
        {
            SelectedTroop = troop;
        }
        
        public void ConfirmSelection()
        {
            if (SelectedTroop != null)
            {
                // Відправити вибір серверу
                NetworkManager.Instance.SendMessage(
                    $"TROOP_SELECTED:{SelectedTroop.TroopId}"
                );
            }
        }
    }
    
    public class TroopCardVM : ViewModel
    {
        private string _troopName;
        private int _tier;
        private bool _isSelected;
        private Action<TroopCardVM> _onSelect;
        
        public string TroopId { get; }
        
        [DataSourceProperty]
        public string TroopName
        {
            get => _troopName;
            set
            {
                if (_troopName != value)
                {
                    _troopName = value;
                    OnPropertyChangedWithValue(value, "TroopName");
                }
            }
        }
        
        [DataSourceProperty]
        public int Tier
        {
            get => _tier;
            set
            {
                if (_tier != value)
                {
                    _tier = value;
                    OnPropertyChangedWithValue(value, "Tier");
                }
            }
        }
        
        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value, "IsSelected");
                }
            }
        }
        
        public TroopCardVM(TroopInfo troop, Action<TroopCardVM> onSelect)
        {
            TroopId = troop.CharacterId;
            TroopName = troop.TroopName;
            Tier = troop.Tier;
            _onSelect = onSelect;
        }
        
        public void ExecuteSelect()
        {
            _onSelect?.Invoke(this);
        }
    }
}
```

### 3.4 Spawn system - створення агентів для клієнтів
**Час: 12-15 годин**

**ClientAgentSpawner.cs:**
```csharp
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

namespace CoopSpectator.Mission
{
    public class ClientAgentSpawner : MissionLogic
    {
        private Dictionary<NetworkCommunicator, Agent> _clientAgents;
        
        public ClientAgentSpawner()
        {
            _clientAgents = new Dictionary<NetworkCommunicator, Agent>();
        }
        
        public override void AfterStart()
        {
            base.AfterStart();
            
            // Підписатися на події підключення клієнтів
            GameNetwork.AddNetworkHandler(this);
        }
        
        // Викликається коли клієнт вибрав юніта
        public void SpawnAgentForClient(NetworkCommunicator client, string troopId)
        {
            // 1. Знайти відповідний character
            BasicCharacterObject character = Game.Current.ObjectManager
                .GetObject<BasicCharacterObject>(troopId);
            
            if (character == null)
            {
                Debug.Print($"Character {troopId} not found!");
                return;
            }
            
            // 2. Визначити команду (хост завжди на стороні гравця)
            Team team = Mission.PlayerTeam;
            
            // 3. Знайти spawn point
            MatrixFrame spawnFrame = GetSpawnFrame(team);
            
            // 4. Створити екіпіровку
            Equipment equipment = character.Equipment.Clone();
            
            // 5. Spawn agent
            AgentBuildData buildData = new AgentBuildData(character)
                .Team(team)
                .InitialPosition(in spawnFrame.origin)
                .InitialDirection(in spawnFrame.rotation.f)
                .Equipment(equipment)
                .Controller(Agent.ControllerType.Player)
                .OwningAgentNetworkPeer(client);
            
            Agent agent = Mission.Current.SpawnAgent(buildData);
            
            // 6. Зберегти reference
            _clientAgents[client] = agent;
            
            InformationManager.DisplayMessage(
                new InformationMessage($"Client spawned as {character.Name}")
            );
        }
        
        private MatrixFrame GetSpawnFrame(Team team)
        {
            // Знайти spawn точку для команди
            GameEntity spawnEntity = Mission.Scene.FindEntityWithTag("sp_player");
            
            if (spawnEntity != null)
            {
                return spawnEntity.GetGlobalFrame();
            }
            
            // Fallback - spawn біля хоста
            if (Agent.Main != null)
            {
                Vec3 pos = Agent.Main.Position;
                pos.x += MBRandom.RandomFloatRanged(-5f, 5f);
                pos.y += MBRandom.RandomFloatRanged(-5f, 5f);
                
                return new MatrixFrame(Mat3.Identity, pos);
            }
            
            return MatrixFrame.Identity;
        }
        
        public override void OnAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow blow)
        {
            // Обробити смерть агента клієнта
            if (_clientAgents.ContainsValue(affectedAgent))
            {
                // Можна дозволити респавн або показати екран спостереження
            }
        }
    }
}
```

### 3.5 Повернення до spectator mode після битви
**Час: 6-8 годин**

**BattleEndHandler.cs:**
```csharp
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Mission
{
    public class BattleEndHandler : MissionLogic
    {
        private NetworkManager _network;
        
        public BattleEndHandler(NetworkManager network)
        {
            _network = network;
        }
        
        public override void OnMissionResultReady(MissionResult missionResult)
        {
            base.OnMissionResultReady(missionResult);
            
            // Сповістити всіх що битва закінчилася
            BattleEndMessage message = new BattleEndMessage
            {
                Victory = missionResult.BattleResolved && 
                         missionResult.PlayerVictory,
                CasualtiesDealt = missionResult.EnemyCasualties,
                CasualtiesTaken = missionResult.PlayerCasualties
            };
            
            string json = JsonConvert.SerializeObject(message);
            _network.SendMessage("BATTLE_END:" + json);
        }
        
        public override void OnMissionDeactivate()
        {
            base.OnMissionDeactivate();
            
            // Повернутися до campaign map
            // Клієнти знову стають спостерігачами
        }
    }
    
    [Serializable]
    public class BattleEndMessage
    {
        public bool Victory { get; set; }
        public int CasualtiesDealt { get; set; }
        public int CasualtiesTaken { get; set; }
    }
}
```

---

## ЕТАП 4: Тестування та полірування (2-4 тижні)

### 4.1 Базове тестування
**Час: 10-15 годин**

**Тестові сценарії:**

1. **Підключення/відключення:**
   - [ ] Хост може запустити сервер
   - [ ] Клієнт може підключитися
   - [ ] Корректне відключення без крашу
   - [ ] Reconnect після обриву з'єднання

2. **Spectator mode:**
   - [ ] Клієнт бачить позицію хоста на карті
   - [ ] Оновлення кожні 2 секунди
   - [ ] Клієнт не може керувати партією
   - [ ] Клієнт не може входити в міста

3. **Битви:**
   - [ ] Клієнт отримує сповіщення про початок битви
   - [ ] Меню вибору юніта відображається
   - [ ] Клієнт spawn'иться як вибраний юніт
   - [ ] Битва працює з 2+ клієнтами
   - [ ] Після битви повернення до spectator mode

4. **Edge cases:**
   - [ ] Що якщо хост помер у битві?
   - [ ] Що якщо клієнт відключився під час битви?
   - [ ] Що якщо хост втік з битви?
   - [ ] Що якщо битва була програна?

### 4.2 Оптимізація та баг-фікси
**Час: 15-20 годин**

**Типові проблеми які треба вирішити:**

1. **Синхронізація:**
   ```csharp
   // Проблема: Клієнт бачить застарілу позицію
   // Рішення: Інтерполяція між оновленнями
   
   private Vec2 _currentPos;
   private Vec2 _targetPos;
   private float _lerpTime = 0f;
   
   void Update()
   {
       _lerpTime += Time.deltaTime;
       float t = _lerpTime / BROADCAST_INTERVAL;
       _currentPos = Vec2.Lerp(_currentPos, _targetPos, t);
   }
   ```

2. **Networking lag:**
   ```csharp
   // Додати буферизацію для нестабільного з'єднання
   private Queue<HostGameState> _stateBuffer = new Queue<HostGameState>();
   
   void OnStateReceived(HostGameState state)
   {
       _stateBuffer.Enqueue(state);
       
       // Обробляти з затримкою для плавності
       if (_stateBuffer.Count > 3)
       {
           HostGameState bufferedState = _stateBuffer.Dequeue();
           ApplyState(bufferedState);
       }
   }
   ```

3. **Memory leaks:**
   ```csharp
   // Завжди unsubscribe від events
   public override void OnRemoveBehavior()
   {
       base.OnRemoveBehavior();
       
       CampaignEvents.HourlyTickEvent.ClearListeners(this);
       _network.OnMessageReceived -= HandleMessage;
       _clientAgents.Clear();
   }
   ```

### 4.3 UI/UX покращення
**Час: 8-10 годин**

**Додати якісний UI:**

1. **Lobby екран:**
   - Список підключених гравців
   - Їх статус (Ready/Not Ready)
   - Налаштування (friendly fire, difficulty)

2. **In-game HUD:**
   - Індикатор підключення
   - Ping до хоста
   - Кількість підключених гравців

3. **Повідомлення:**
   - "John joined the game"
   - "Battle starting in 5... 4... 3..."
   - "You were killed - spectating"

### 4.4 Документація
**Час: 4-6 годин**

**README.md:**
```markdown
# Bannerlord Coop Spectator

Simple cooperative mod for Mount & Blade II: Bannerlord

## Features
- Spectate host's campaign
- Join battles as host's troops
- No campaign sync needed

## Installation
1. Download latest release
2. Extract to Bannerlord/Modules/
3. Enable in launcher

## How to Play
**Host:**
1. Start campaign
2. Open console (`) and type: coop_host
3. Share your IP with friends

**Clients:**
1. Start Bannerlord
2. Console: coop_join <host_ip>
3. Spectate and wait for battles!

## Troubleshooting
- Port 7777 must be open
- Both players need same game version
- Disable other coop mods

## Known Issues
- See GitHub issues page

## Credits
Created by [Ваше ім'я]
```

---

## ЕТАП 5: Реліз та підтримка

### 5.1 Публікація
**Час: 3-5 годин**

**Де опублікувати:**

1. **Nexus Mods** (головна платформа):
   - Створити сторінку моду
   - Завантажити файли
   - Додати скріншоти/відео
   - Написати опис

2. **ModDB:**
   - Backup platform
   - Інша аудиторія

3. **GitHub:**
   - Вихідний код (якщо open source)
   - Issue tracker
   - Wiki з документацією

4. **Discord:**
   - Власний сервер або
   - Опублікувати в Bannerlord Coop Discord

### 5.2 Збір feedback
**Час: ongoing**

**Перші тижні після релізу:**
- Моніторити crash reports
- Відповідати на питання користувачів
- Фіксити критичні баги
- Збирати ідеї для наступних версій

### 5.3 Майбутні features (v2.0+)

**Можливі покращення:**
1. Voice chat інтеграція
2. Більше налаштувань (respawn, friendly fire)
3. Статистика після битви
4. Підтримка більше ніж 4 гравців
5. Custom scenarios для битв
6. Spectator camera під час битви (якщо помер)

---

## 📊 Технологічний стек

**Мови програмування:**
- C# (основна розробка)
- XML (конфігурація, UI)

**Фреймворки/бібліотеки:**
- .NET Framework 4.7.2+
- Harmony (патчинг методів гри)
- Newtonsoft.Json (серіалізація)
- System.Net.Sockets (networking)

**Інструменти розробки:**
- Visual Studio 2022
- Cursor (AI асистент)
- dnSpy (декомпіляція DLL гри для вивчення)
- ILSpy (альтернатива)

**Тестування:**
- 2+ копії Bannerlord
- Віртуальні машини (опціонально)
- Hamachi/ZeroTier для тестування через інтернет

---

## 🎓 Ресурси для навчання

### Офіційна документація:
- [Bannerlord Modding Documentation](https://docs.bannerlordmodding.com)
- [TaleWorlds Forums - Modding](https://forums.taleworlds.com/index.php?forums/modding.196/)

### Community ресурси:
- [Bannerlord Modding Discord](https://discord.gg/bannerlord)
- [GitHub - Bannerlord Community](https://github.com/BannerlordCommunity)

### YouTube туторіали:
- "Bannerlord Modding Tutorial" series
- "How to create Bannerlord behaviors"

### Приклади відкритого коду:
- [Bannerlord.Harmony](https://github.com/BUTR/Bannerlord.Harmony)
- [Bannerlord.ModuleManager](https://github.com/BUTR/Bannerlord.ModuleManager)
- Інші coop моди на GitHub

---

## ⚠️ Потенційні проблеми та рішення

### Проблема 1: Гра оновлюється і все ламається
**Рішення:**
- Використовувати Harmony для патчинга (більш стійке до оновлень)
- Мінімізувати прямі зміни game files
- Тримати код модульним для легкого оновлення

### Проблема 2: Multiplayer API недокументований
**Рішення:**
- Декомпілювати оригінальний MP код
- Вивчати існуючі MP моди
- Питати на форумах/Discord

### Проблема 3: Складно тестувати з друзями
**Рішення:**
- Використовувати Hamachi для локальної мережі
- Запускати 2 копії гри на одному ПК (потребує потужне залізо)
- Знайти тестерів у Bannerlord Modding спільноті

### Проблема 4: Користувачі скаржаться на краші
**Рішення:**
- Додати детальне логування
- Створити систему crash reports
- Попросити користувачів надавати log files

---

## 📈 Метрики успіху

**Після 1 місяця релізу:**
- [ ] 1000+ завантажень на Nexus
- [ ] Середній рейтинг 4+ зірок
- [ ] <5 критичних багів в issue tracker
- [ ] Хоча б 10 активних користувачів які грають регулярно

**Після 3 місяців:**
- [ ] 5000+ завантажень
- [ ] Згадки у YouTube відео
- [ ] Запити на features від спільноти
- [ ] Можливість співпраці з іншими моддерами

---

## 💡 Поради від досвідчених моддерів

1. **Починайте просто** - не намагайтеся зробити все відразу
2. **Тестуйте часто** - кожна нова feature = тестування
3. **Слухайте спільноту** - користувачі знайдуть баги які ви пропустили
4. **Документуйте код** - через місяць ви забудете що робить ця функція
5. **Бекапи** - використовуйте Git з першого дня
6. **Не здавайтеся** - перші тижні найскладніші, але стає легше

---

## 🎯 Чеклист готовності до релізу

**Технічні вимоги:**
- [ ] Мод компілюється без помилок
- [ ] Працює на чистій версії гри
- [ ] Tested з 2+ гравцями
- [ ] Немає критичних крашів
- [ ] Log файли чисті (без spam warnings)

**Документація:**
- [ ] README з інструкціями
- [ ] CHANGELOG з версіями
- [ ] Known issues список
- [ ] FAQ для типових питань

**Публікація:**
- [ ] Сторінка на Nexus Mods готова
- [ ] Скріншоти/відео демонстрація
- [ ] GitHub repository (якщо open source)
- [ ] Discord сервер для підтримки (опціонально)

---

## 🔮 Довгострокове бачення

**Версія 1.0** (базовий функціонал):
- Spectator mode на карті
- Join до битв
- Вибір юніта

**Версія 1.5** (покращення):
- Voice chat
- Статистика
- Більше налаштувань

**Версія 2.0** (розширений контент):
- Custom scenarios
- Co-op квести (якщо можливо)
- Економіка розділена між гравцями (опціонально)

**Версія 3.0** (повний кооп?):
- Якщо спільнота хоче - можливість еволюції у full coop
- Але це вже інший проєкт на роки вперед

---

## 📞 Контакти та підтримка

**Для користувачів:**
- Nexus Mods коментарі
- GitHub Issues
- Discord сервер

**Для інших розробників:**
- Open source на GitHub
- Детальні коментарі в коді
- Wiki з технічними деталями

---

## ⏰ Фінальний таймлайн

```
Місяць 1: Навчання + базовий networking
├─ Тиждень 1-2: Dev environment + Hello World
├─ Тиждень 3-4: TCP server/client працює
└─ Milestone: Можу відправити повідомлення між клієнтами

Місяць 2: Spectator mode
├─ Тиждень 1: Broadcasting позиції хоста
├─ Тиждень 2: UI для клієнтів
├─ Тиждень 3: Блокування контролю
└─ Milestone: Клієнт може дивитись як грає хост

Місяць 3-4: Battle integration
├─ Тиждень 1: Детекція битв
├─ Тиждень 2-3: Конвертація в MP (складно!)
├─ Тиждень 4: Меню вибору юніта
├─ Тиждень 5: Spawn system
└─ Milestone: Можемо грати разом у битві

Місяць 5: Тестування
├─ Тиждень 1-2: Баг-фікси
├─ Тиждень 3: UI/UX polish
└─ Milestone: Стабільна версія

Місяць 6: Реліз
├─ Тиждень 1: Документація
├─ Тиждень 2: Публікація
├─ Тиждень 3-4: Підтримка користувачів
└─ Milestone: Версія 1.0 на Nexus!
```

---

## 🚀 Наступні кроки

**Цього тижня:**
1. Встановити Visual Studio та .NET SDK
2. Створити базовий Bannerlord мод проєкт
3. Скомпілювати і запустити Hello World

**Цього місяця:**
1. Завершити Етап 1 (Підготовка)
2. Зробити простий TCP networking між двома клієнтами
3. Почати Етап 2 (Spectator)

**Через 3 місяці:**
1. Мати робочий spectator mode
2. Почати роботу над battle integration
3. Знайти перших тестерів у спільноті

**Через 6 місяців:**
1. Реліз версії 1.0 на Nexus Mods
2. Збирати feedback
3. Плануваті версію 1.1

---

## 🎉 Висновок

Цей проєкт **реально здійсненний** за 3-6 місяців при систематичній роботі!

**Ключові фактори успіху:**
✅ Розумний scope - не намагаємося зробити повний кооп  
✅ Використання існуючих систем гри  
✅ Підтримка від AI (Cursor) для прискорення  
✅ Активна спільнота для допомоги та тестування  

**Пам'ятайте:**
- Перші тижні будуть складні (навчання)
- Потім стає легше (є routine)
- Найважливіше - не здаватися при перших багах
- Кожен великий мод починався з простого Hello World

**Готові почати?** 🎮

Наступний крок: Встановити Visual Studio та створити перший Bannerlord мод проєкт!
