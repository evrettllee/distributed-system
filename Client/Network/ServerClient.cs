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

        public DateTime LocalTime => DateTime.UtcNow + _clockOffset;

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

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] -> SEND {operation}");

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

        public void SyncTime()
        {
            DateTime requestTime = DateTime.UtcNow;

            Response response = Send("GetServerTime");

            DateTime responseTime = DateTime.UtcNow;

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

            _timeOffset = correctedServerTime - LocalTime;

            Console.WriteLine();
            Console.WriteLine($"  Local time:        {LocalTime:HH:mm:ss.fff}");
            Console.WriteLine($"  Server time:       {serverTime:HH:mm:ss.fff}");
            Console.WriteLine($"  RTT:               {rtt.TotalSeconds:F3} sec");
            Console.WriteLine($"  Network delay:     {networkDelay.TotalSeconds:F3} sec");
            Console.WriteLine($"  Calculated offset: {_timeOffset.TotalSeconds:F3} sec");
            Console.WriteLine($"  Corrected time:    {(LocalTime + _timeOffset):HH:mm:ss.fff}");
        }
    }
}