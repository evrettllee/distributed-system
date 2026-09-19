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

        public ServerClient(string host = "127.0.0.1", int port = 8080)
        {
            _host = host;
            _port = port;
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

                writer.WriteLine(JsonSerializer.Serialize(request));

                string? line = reader.ReadLine();
                if (line == null)
                {
                    return Response.Error("Сервер не вернул ответ");
                }

                return JsonSerializer.Deserialize<Response>(line) ?? Response.Error("Не удалось разобрать ответ");
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
    }
}