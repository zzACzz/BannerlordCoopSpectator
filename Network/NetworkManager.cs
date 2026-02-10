using System; // Підключаємо базові типи .NET (Action, Exception)
using System.Collections.Generic; // Підключаємо колекції (List)
using System.Net; // Підключаємо IPAddress для listener'а
using System.Net.Sockets; // Підключаємо TCP типи (TcpListener, TcpClient)
using System.Threading; // Підключаємо Thread для accept loop
using CoopSpectator.Infrastructure; // Підключаємо диспетчер головного потоку та логер

namespace CoopSpectator.Network // Оголошуємо простір імен для мережевого шару
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Керує TCP-мережею моду: або хостить сервер, або підключається як клієнт. // Пояснюємо призначення
    /// </summary> // Завершуємо XML-коментар
    public sealed class NetworkManager // Оголошуємо sealed, бо це основний менеджер мережі і ми не плануємо наслідування
    { // Починаємо блок класу
        private readonly object _sync; // Об'єкт для синхронізації доступу до списку клієнтів сервера
        private volatile bool _isRunning; // Прапорець роботи мережі (volatile для коректної синхронізації між потоками)
        private NetworkRole _role; // Поточна роль мережі (None/Server/Client)

        private TcpListener _listener; // TCP listener для режиму сервера
        private Thread _acceptThread; // Потік, який приймає підключення клієнтів
        private readonly List<TcpClientConnection> _serverClients; // Список активних клієнтських з'єднань у режимі сервера

        private TcpClientConnection _clientConnection; // З'єднання до хоста у режимі клієнта

        public event Action<string> MessageReceived; // Подія отримання повідомлення (викликається в головному потоці через dispatcher)

        public NetworkRole Role // Повертаємо поточну роль мережі
        { // Починаємо блок властивості
            get { return _role; } // Повертаємо значення поля
        } // Завершуємо блок властивості

        public bool IsRunning // Повертаємо чи мережа запущена
        { // Починаємо блок властивості
            get { return _isRunning; } // Повертаємо прапорець роботи
        } // Завершуємо блок властивості

        public NetworkManager() // Оголошуємо конструктор менеджера
        { // Починаємо блок конструктора
            _sync = new object(); // Створюємо об'єкт синхронізації
            _serverClients = new List<TcpClientConnection>(); // Ініціалізуємо список клієнтів для серверного режиму
            _role = NetworkRole.None; // Встановлюємо дефолтну роль (мережа вимкнена)
            _isRunning = false; // Встановлюємо дефолтний стан (не запущено)
        } // Завершуємо блок конструктора

        public void StartServer(int port) // Запускаємо TCP сервер на заданому порту
        { // Починаємо блок методу
            Shutdown(); // Спершу завершуємо будь-який попередній стан, щоб уникнути конфліктів портів/потоків

            _role = NetworkRole.Server; // Встановлюємо роль сервера
            _isRunning = true; // Встановлюємо прапорець роботи

            _listener = new TcpListener(IPAddress.Any, port); // Створюємо listener на всіх інтерфейсах
            _listener.Start(); // Запускаємо listener, щоб почати приймати підключення

            _acceptThread = new Thread(AcceptLoop); // Створюємо потік для AcceptTcpClient, щоб не блокувати головний потік гри
            _acceptThread.IsBackground = true; // Робимо потік background, щоб він не блокував завершення процесу
            _acceptThread.Start(); // Запускаємо accept loop

            ModLogger.Info("TCP сервер запущено на порту " + port + "."); // Логуємо старт сервера
            RaiseMessageReceived("SERVER_STARTED:" + port); // Сповіщаємо підписників (в головному потоці) що сервер стартував
        } // Завершуємо блок методу

        public void ConnectToServer(string host, int port) // Підключаємося до TCP сервера як клієнт
        { // Починаємо блок методу
            Shutdown(); // Спершу завершуємо будь-який попередній стан, щоб не було “двох ролей” одночасно

            _role = NetworkRole.Client; // Встановлюємо роль клієнта
            _isRunning = true; // Встановлюємо прапорець роботи

            TcpClient tcpClient = new TcpClient(); // Створюємо TcpClient для підключення до хоста
            tcpClient.NoDelay = true; // Вимикаємо Nagle, щоб зменшити затримку для коротких повідомлень
            tcpClient.Connect(host, port); // Підключаємося (це блокуючий виклик, але виконується з консольної команди, не з tick)

            _clientConnection = new TcpClientConnection(tcpClient); // Обгортаємо TcpClient, щоб читати в окремому потоці та парсити рядки
            _clientConnection.Start(OnAnyMessageFromConnection, OnClientConnectionDisconnected); // Запускаємо receive loop з callback'ами

            ModLogger.Info("Підключено до " + host + ":" + port + "."); // Логуємо успішне підключення
            RaiseMessageReceived("CLIENT_CONNECTED:" + host + ":" + port); // Сповіщаємо підписників (в головному потоці)
        } // Завершуємо блок методу

        public void SendToServer(string message) // Відправляємо повідомлення на сервер (працює тільки у режимі клієнта)
        { // Починаємо блок методу
            if (_role != NetworkRole.Client) // Перевіряємо роль, щоб не відправляти “на сервер” у серверному режимі
            { // Починаємо блок if
                return; // Виходимо, бо ми не клієнт
            } // Завершуємо блок if

            _clientConnection?.Send(message); // Відправляємо повідомлення якщо з'єднання існує
        } // Завершуємо блок методу

        public void BroadcastToClients(string message) // Відправляємо повідомлення всім підключеним клієнтам (працює тільки у режимі сервера)
        { // Починаємо блок методу
            if (_role != NetworkRole.Server) // Перевіряємо роль, щоб broadcast не виконувався у клієнтському режимі
            { // Починаємо блок if
                return; // Виходимо, бо ми не сервер
            } // Завершуємо блок if

            lock (_sync) // Блокуємо список клієнтів, бо він змінюється з accept/receive потоків
            { // Починаємо блок lock
                for (int i = 0; i < _serverClients.Count; i++) // Проходимо по всіх клієнтах
                { // Починаємо блок for
                    _serverClients[i].Send(message); // Відправляємо повідомлення конкретному клієнту
                } // Завершуємо блок for
            } // Завершуємо блок lock
        } // Завершуємо блок методу

        public void Shutdown() // Коректно зупиняємо мережу (сервер/клієнт) та очищаємо ресурси
        { // Починаємо блок методу
            _isRunning = false; // Скидаємо прапорець, щоб потоки вийшли з циклів

            try // Пробуємо зупинити listener якщо він існує
            { // Починаємо блок try
                _listener?.Stop(); // Зупиняємо listener (це також розблокує AcceptTcpClient з винятком)
            } // Завершуємо блок try
            catch (Exception) // Ігноруємо винятки при stop, бо ми робимо best-effort shutdown
            { // Починаємо блок catch
            } // Завершуємо блок catch

            _listener = null; // Обнуляємо listener щоб не використовувати після shutdown

            if (_acceptThread != null) // Перевіряємо чи accept thread був створений
            { // Починаємо блок if
                try // Пробуємо дати потоку завершитись коректно
                { // Починаємо блок try
                    if (_acceptThread.IsAlive) // Якщо потік ще живий, пробуємо почекати трохи
                    { // Починаємо блок if
                        _acceptThread.Join(200); // Чекаємо до 200мс, щоб не зависнути на shutdown
                    } // Завершуємо блок if
                } // Завершуємо блок try
                catch (Exception) // Ігноруємо будь-які винятки, бо shutdown не має крашити гру
                { // Починаємо блок catch
                } // Завершуємо блок catch
            } // Завершуємо блок if

            _acceptThread = null; // Обнуляємо посилання на потік

            if (_clientConnection != null) // Якщо ми були клієнтом, закриваємо клієнтське з'єднання
            { // Починаємо блок if
                _clientConnection.Shutdown(); // Зупиняємо receive loop і закриваємо сокет
                _clientConnection = null; // Обнуляємо посилання на з'єднання
            } // Завершуємо блок if

            lock (_sync) // Блокуємо доступ до списку клієнтів
            { // Починаємо блок lock
                for (int i = 0; i < _serverClients.Count; i++) // Проходимо по всіх серверних клієнтах
                { // Починаємо блок for
                    _serverClients[i].Shutdown(); // Закриваємо з'єднання з конкретним клієнтом
                } // Завершуємо блок for

                _serverClients.Clear(); // Очищаємо список клієнтів
            } // Завершуємо блок lock

            _role = NetworkRole.None; // Повертаємо роль в None, бо мережа зупинена
        } // Завершуємо блок методу

        private void AcceptLoop() // Accept loop для серверного режиму (виконується у фоновому потоці)
        { // Починаємо блок методу
            while (_isRunning && _role == NetworkRole.Server) // Поки сервер активний, приймаємо підключення
            { // Починаємо блок while
                try // Захищаємо AcceptTcpClient від винятків (Stop, network errors)
                { // Починаємо блок try
                    TcpClient tcpClient = _listener.AcceptTcpClient(); // Блокуємося до підключення клієнта (у фоновому потоці це ок)
                    tcpClient.NoDelay = true; // Вимикаємо Nagle для меншої затримки

                    TcpClientConnection connection = new TcpClientConnection(tcpClient); // Створюємо обгортку, яка читає рядки у своєму потоці
                    connection.Start(OnAnyMessageFromConnection, OnServerClientDisconnected); // Запускаємо receive loop для цього клієнта

                    lock (_sync) // Додаємо клієнта у список під lock
                    { // Починаємо блок lock
                        _serverClients.Add(connection); // Додаємо нове з'єднання у список активних клієнтів
                    } // Завершуємо блок lock

                    ModLogger.Info("Клієнт підключився: " + connection.RemoteEndPointText + "."); // Логуємо підключення клієнта
                    RaiseMessageReceived("SERVER_CLIENT_CONNECTED:" + connection.RemoteEndPointText); // Сповіщаємо підписників в головному потоці
                } // Завершуємо блок try
                catch (SocketException) // SocketException часто виникає при Stop(), тому не логуємо як критичну помилку
                { // Починаємо блок catch
                } // Завершуємо блок catch
                catch (ObjectDisposedException) // ObjectDisposedException також можливий при shutdown, і це нормально
                { // Починаємо блок catch
                } // Завершуємо блок catch
                catch (Exception ex) // Інші винятки логуються як помилки, бо можуть вказувати на реальну проблему
                { // Починаємо блок catch
                    ModLogger.Error("Помилка в AcceptLoop сервера.", ex); // Логуємо виняток для дебагу
                } // Завершуємо блок catch
            } // Завершуємо блок while
        } // Завершуємо блок методу

        private void OnAnyMessageFromConnection(string message) // Callback, який викликається з receive-потоків TcpClientConnection
        { // Починаємо блок методу
            if (message == null) // Перевіряємо null для безпеки
            { // Починаємо блок if
                return; // Виходимо, бо немає повідомлення
            } // Завершуємо блок if

            RaiseMessageReceived(message); // Маршалимо подію в головний потік через dispatcher, передаючи "сире" повідомлення протоколу (STATE:/BATTLE_START:/...)
        } // Завершуємо блок методу

        private void OnServerClientDisconnected(TcpClientConnection connection) // Callback, який викликається коли серверний клієнт відключився
        { // Починаємо блок методу
            if (connection == null) // Перевіряємо null для безпеки
            { // Починаємо блок if
                return; // Виходимо, бо нічого обробляти
            } // Завершуємо блок if

            lock (_sync) // Блокуємо список, бо він спільний для кількох потоків
            { // Починаємо блок lock
                _serverClients.Remove(connection); // Видаляємо клієнта зі списку активних
            } // Завершуємо блок lock

            ModLogger.Warn("Клієнт відключився: " + connection.RemoteEndPointText + "."); // Логуємо відключення
            RaiseMessageReceived("SERVER_CLIENT_DISCONNECTED:" + connection.RemoteEndPointText); // Сповіщаємо підписників в головному потоці
        } // Завершуємо блок методу

        private void OnClientConnectionDisconnected(TcpClientConnection connection) // Callback, який викликається коли клієнтське з'єднання до хоста розірвалось
        { // Починаємо блок методу
            ModLogger.Warn("З'єднання з хостом розірвано."); // Логуємо факт відключення клієнта
            RaiseMessageReceived("CLIENT_DISCONNECTED"); // Сповіщаємо підписників в головному потоці
        } // Завершуємо блок методу

        private void RaiseMessageReceived(string message) // Helper який гарантує виклик події в головному потоці
        { // Починаємо блок методу
            MainThreadDispatcher.Enqueue(() => // Ставимо дію в чергу головного потоку, щоб не викликати ігровий API з мережевих потоків
            { // Починаємо блок делегата
                Action<string> handler = MessageReceived; // Копіюємо делегат в локальну змінну для thread-safety патерну

                if (handler == null) // Якщо немає підписників, нічого не робимо
                { // Починаємо блок if
                    return; // Виходимо з делегата
                } // Завершуємо блок if

                try // Захищаємо callback підписників, щоб один handler не крашив гру
                { // Починаємо блок try
                    handler(message); // Викликаємо всіх підписників події
                } // Завершуємо блок try
                catch (Exception ex) // Ловимо винятки з user-коду (підписників)
                { // Починаємо блок catch
                    ModLogger.Error("Помилка в обробнику MessageReceived.", ex); // Логуємо виняток
                } // Завершуємо блок catch
            }); // Завершуємо enqueue дії
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

