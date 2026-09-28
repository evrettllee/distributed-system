using Shared;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Client.Network
{
    public class ServerClient
    {
        private readonly string _host;
        private readonly int _port;
        private readonly TimeSpan _clockOffset;
        private TimeSpan _timeOffset = TimeSpan.Zero;

        public DateTime LocalTime => DateTime.Now + _clockOffset;
        public DateTime CorrectedTime => LocalTime + _timeOffset;
        public bool IsSynced => _timeOffset != TimeSpan.Zero;

        public ServerClient(string host = "127.0.0.1", int port = 8080, int clockOffsetSeconds = 0)
        {
            _host = host;
            _port = port;
            _clockOffset = TimeSpan.FromSeconds(clockOffsetSeconds);
        }

        public Response Send(string operation, object? data = null)
        {
            try
            {
                Request request = new()
                {
                    Operation = operation,
                    Data = data != null ? JsonSerializer.Serialize(data) : null
                };

                using TcpClient client = new(_host, _port);
                NetworkStream stream = client.GetStream();
                StreamReader reader = new(stream, Encoding.UTF8);
                StreamWriter writer = new(stream, Encoding.UTF8) { AutoFlush = true };

                Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss.fff}] -> SEND {operation}");

                writer.WriteLine(JsonSerializer.Serialize(request));

                string? line = reader.ReadLine();
                if (line == null)
                    return Response.Error("Сервер не вернул ответ");

                Response response = JsonSerializer.Deserialize<Response>(line)
                    ?? Response.Error("Не удалось разобрать ответ");

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] -> RECEIVED {response.Status?.ToUpper()}");

                return response;
            }
            catch (SocketException)
            {
                return Response.Error("Не удалось подключиться к серверу");
            }
            catch (Exception ex)
            {
                return Response.Error($"Ошибка: {ex.Message}");
            }
        }

        public void ShowLocalTime()
        {
            Console.WriteLine($"\n  Local time:     {LocalTime:HH:mm:ss.fff}");
            if (IsSynced)
            {
                Console.WriteLine($"  Corrected time: {CorrectedTime:HH:mm:ss.fff}");
                Console.WriteLine($"  Offset applied: {_timeOffset.TotalSeconds:F3} sec");
            }
        }

        public void ShowTimeDiff()
        {
            DateTime requestTime = DateTime.Now;
            Response response = Send("GetServerTime");
            DateTime responseTime = DateTime.Now;

            if (response.Status != "ok" || response.Data == null)
            {
                Console.WriteLine("Не удалось получить время сервера");
                return;
            }

            if (!DateTime.TryParse(response.Data, out DateTime serverTime))
            {
                Console.WriteLine("Некорректные данные времени");
                return;
            }

            TimeSpan diff = LocalTime - serverTime;

            Console.WriteLine();
            Console.WriteLine($"  Server time:       {serverTime:HH:mm:ss.fff}");
            Console.WriteLine($"  Client local time: {LocalTime:HH:mm:ss.fff}");
            Console.WriteLine($"  Разница:           {(diff.TotalSeconds >= 0 ? "+" : "")}{diff.TotalSeconds:F3} sec");
        }

        public void SyncTime()
        {
            DateTime requestTime = DateTime.Now;
            Response response = Send("GetServerTime");
            DateTime responseTime = DateTime.Now;

            if (response.Status != "ok" || response.Data == null)
            {
                Console.WriteLine("Не удалось получить время сервера");
                return;
            }

            if (!DateTime.TryParse(response.Data, out DateTime serverTime))
            {
                Console.WriteLine("Некорректные данные времени");
                return;
            }

            TimeSpan rtt = responseTime - requestTime;
            TimeSpan networkDelay = rtt / 2;
            DateTime correctedServerTime = serverTime + networkDelay;

            DateTime localBefore = LocalTime;
            _timeOffset = correctedServerTime - localBefore;

            DateTime actualServerNow = serverTime + networkDelay;

            Console.WriteLine();
            Console.WriteLine("  --- До синхронизации ---");
            Console.WriteLine($"  Local time:                       {localBefore:HH:mm:ss.fff}");
            Console.WriteLine($"  Server time:                      {serverTime:HH:mm:ss.fff}");
            Console.WriteLine($"  Разница до синхронизации:         {(localBefore - serverTime).TotalSeconds:F3} sec");
            Console.WriteLine();
            Console.WriteLine("  --- Параметры сети ---");
            Console.WriteLine($"  Request time:                     {requestTime:HH:mm:ss.fff}");
            Console.WriteLine($"  Response time:                    {responseTime:HH:mm:ss.fff}");
            Console.WriteLine($"  RTT:                              {rtt.TotalSeconds:F3} sec");
            Console.WriteLine($"  Estimated network delay:          {networkDelay.TotalSeconds:F3} sec");
            Console.WriteLine();
            Console.WriteLine("  --- После синхронизации ---");
            Console.WriteLine($"  Calculated offset:                {_timeOffset.TotalSeconds:F3} sec");
            Console.WriteLine($"  Corrected time:                   {CorrectedTime:HH:mm:ss.fff}");
            Console.WriteLine($"  Разница после (с serverTime):     {(CorrectedTime - serverTime).TotalSeconds:F3} sec");
            Console.WriteLine($"  Разница после (с учётом RTT/2):   {(CorrectedTime - actualServerNow).TotalSeconds:F3} sec");
        }
    }
}