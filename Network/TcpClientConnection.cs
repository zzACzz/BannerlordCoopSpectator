using System; // Підключаємо базові типи .NET (Exception, Action)
using System.Net; // Підключаємо типи мережі .NET (EndPoint)
using System.Net.Sockets; // Підключаємо TCP типи (TcpClient, NetworkStream)
using System.Text; // Підключаємо StringBuilder для буферизації тексту
using System.Threading; // Підключаємо Thread для фонового читання з сокета

namespace CoopSpectator.Network // Оголошуємо простір імен для мережевого шару
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Обгортка над TcpClient, яка читає дані в окремому потоці і повертає повідомлення рядками. // Пояснюємо навіщо цей клас
    /// </summary> // Завершуємо XML-коментар
    internal sealed class TcpClientConnection // Оголошуємо sealed, бо не плануємо наслідування і хочемо простішу модель
    { // Починаємо блок класу
        private readonly TcpClient _client; // Зберігаємо TcpClient для з'єднання
        private readonly object _sendLock; // Зберігаємо lock-об'єкт для синхронізації відправки (щоб байти не перемішувались)
        private NetworkStream _stream; // Зберігаємо NetworkStream для читання/запису
        private Thread _receiveThread; // Зберігаємо потік, який постійно читає з'єднання
        private volatile bool _isRunning; // Зберігаємо прапорець роботи (volatile для коректного читання між потоками)
        private Action<string> _onMessage; // Зберігаємо callback для отриманих повідомлень
        private Action<TcpClientConnection> _onDisconnected; // Зберігаємо callback для відключення

        internal string RemoteEndPointText { get; private set; } // Зберігаємо текстовий опис віддаленої адреси (для логів)

        internal TcpClientConnection(TcpClient client) // Оголошуємо конструктор, який приймає уже встановлене TCP-з'єднання
        { // Починаємо блок конструктора
            _client = client; // Зберігаємо посилання на клієнт
            _sendLock = new object(); // Створюємо lock-об'єкт для відправки
            _stream = _client.GetStream(); // Отримуємо stream з TcpClient для I/O
            RemoteEndPointText = GetRemoteEndPointTextSafe(_client); // Зчитуємо endpoint в безпечний спосіб (щоб не кинути виняток)
        } // Завершуємо блок конструктора

        internal void Start(Action<string> onMessage, Action<TcpClientConnection> onDisconnected) // Оголошуємо запуск фонового читання
        { // Починаємо блок методу
            _onMessage = onMessage; // Зберігаємо callback для повідомлень
            _onDisconnected = onDisconnected; // Зберігаємо callback для відключення
            _isRunning = true; // Встановлюємо прапорець роботи перед стартом потоку
            _receiveThread = new Thread(ReceiveLoop); // Створюємо окремий потік для читання, щоб не блокувати головний потік гри
            _receiveThread.IsBackground = true; // Робимо потік background, щоб він не заважав завершенню процесу гри
            _receiveThread.Start(); // Запускаємо потік читання
        } // Завершуємо блок методу

        internal void Send(string message) // Оголошуємо метод відправки одного повідомлення (одного рядка)
        { // Починаємо блок методу
            if (!_isRunning) // Перевіряємо чи з'єднання в активному стані, щоб не писати в закритий stream
            { // Починаємо блок if
                return; // Виходимо, бо мережа вже зупинена
            } // Завершуємо блок if

            if (_stream == null) // Перевіряємо чи stream існує (він може бути закритий в Shutdown)
            { // Починаємо блок if
                return; // Виходимо, бо відправити неможливо
            } // Завершуємо блок if

            byte[] data = TcpLineProtocol.EncodeLine(message); // Кодуємо повідомлення в UTF-8 байти з '\n' в кінці

            lock (_sendLock) // Блокуємо відправку, щоб кілька потоків не писали одночасно в один stream
            { // Починаємо блок lock
                try // Захищаємо Write від мережевих винятків
                { // Починаємо блок try
                    _stream.Write(data, 0, data.Length); // Пишемо байти у мережевий потік
                } // Завершуємо блок try
                catch (Exception) // Ловимо будь-яку помилку відправки (обрив з'єднання, disposed, тощо)
                { // Починаємо блок catch
                    Shutdown(); // Закриваємо з'єднання, бо воно скоріш за все вже невалідне
                } // Завершуємо блок catch
            } // Завершуємо блок lock
        } // Завершуємо блок методу

        internal void Shutdown() // Оголошуємо коректне завершення з'єднання
        { // Починаємо блок методу
            _isRunning = false; // Спершу скидаємо прапорець, щоб ReceiveLoop вийшов

            try // Пробуємо закрити stream без крашу
            { // Починаємо блок try
                _stream?.Close(); // Закриваємо stream якщо він існує
            } // Завершуємо блок try
            catch (Exception) // Ігноруємо винятки при закритті, бо ми і так завершуємо з'єднання
            { // Починаємо блок catch
            } // Завершуємо блок catch

            try // Пробуємо закрити TcpClient без крашу
            { // Починаємо блок try
                _client?.Close(); // Закриваємо TCP клієнт якщо він існує
            } // Завершуємо блок try
            catch (Exception) // Ігноруємо винятки при закритті, щоб не ламати shutdown
            { // Починаємо блок catch
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private void ReceiveLoop() // Оголошуємо внутрішній метод, який виконується у фон. потоці та читає з'єднання
        { // Починаємо блок методу
            byte[] buffer = new byte[4096]; // Створюємо буфер для читання з мережі (4KB достатньо для початку)
            StringBuilder textBuffer = new StringBuilder(); // Створюємо буфер тексту, бо TCP може “різати” повідомлення на шматки

            try // Обгортаємо весь цикл читання, щоб у випадку винятку перейти до disconnect логіки
            { // Починаємо блок try
                while (_isRunning) // Поки з'єднання активне, читаємо дані
                { // Починаємо блок while
                    if (_stream == null) // Якщо stream вже закритий, виходимо з циклу
                    { // Починаємо блок if
                        break; // Виходимо, бо читати більше нічого
                    } // Завершуємо блок if

                    int bytesRead = _stream.Read(buffer, 0, buffer.Length); // Читаємо байти з мережі (блокуючий виклик у фон. потоці)

                    if (bytesRead <= 0) // Якщо прочитали 0 байт, це означає що peer закрив з'єднання
                    { // Починаємо блок if
                        break; // Виходимо з циклу читання
                    } // Завершуємо блок if

                    string chunk = TcpLineProtocol.Utf8.GetString(buffer, 0, bytesRead); // Декодуємо отримані байти в текстовий шматок
                    textBuffer.Append(chunk); // Додаємо шматок у загальний буфер, бо повідомлення може бути неповним

                    string line; // Оголошуємо змінну для витягнутого повного рядка

                    while (TcpLineProtocol.TryExtractLine(textBuffer, out line)) // Поки в буфері є повні рядки, витягуємо їх по одному
                    { // Починаємо блок while
                        _onMessage?.Invoke(line); // Викликаємо callback, передаючи готове повідомлення (без '\n')
                    } // Завершуємо блок while
                } // Завершуємо блок while
            } // Завершуємо блок try
            catch (Exception) // Ловимо винятки читання (обрив, disposed, тощо) і трактуємо як відключення
            { // Починаємо блок catch
            } // Завершуємо блок catch
            finally // В finally гарантуємо що повідомимо про відключення
            { // Починаємо блок finally
                _isRunning = false; // Фіксуємо що з'єднання більше не активне
                _onDisconnected?.Invoke(this); // Сповіщаємо власника, що це з'єднання завершилось
            } // Завершуємо блок finally
        } // Завершуємо блок методу

        private static string GetRemoteEndPointTextSafe(TcpClient client) // Оголошуємо helper для отримання endpoint без ризику винятку
        { // Починаємо блок методу
            try // Пробуємо витягнути endpoint
            { // Починаємо блок try
                EndPoint endPoint = client?.Client?.RemoteEndPoint; // Отримуємо endpoint з сокета якщо він існує
                return endPoint != null ? endPoint.ToString() : "unknown"; // Повертаємо текст або "unknown" якщо endpoint відсутній
            } // Завершуємо блок try
            catch (Exception) // Якщо щось пішло не так (disposed), повертаємо "unknown"
            { // Починаємо блок catch
                return "unknown"; // Повертаємо безпечне значення для логів
            } // Завершуємо блок catch
        } // Завершуємо блок методу
    } // Завершуємо блок класу
} // Завершуємо блок простору імен

