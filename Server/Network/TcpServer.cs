using Server.Handlers;
using Shared;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Server.Network
{
    public class TcpServer
    {
        private readonly OperationHandler _handler;
        private readonly TcpListener _listener;
        private readonly int _port;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public TcpServer(OperationHandler handler, int port = 5000)
        {
            _handler = handler;
            _port = port;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            Console.WriteLine($"Сервер запущен на {IPAddress.Any}:{_port}");

            while (true)
            {
                TcpClient client = _listener.AcceptTcpClient();
                Thread thread = new(() => HandleClient(client));
                thread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            string clientAddress = client.Client.RemoteEndPoint?.ToString() ?? "неизвестный адрес";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент подключился: {clientAddress}");

            using (client)
            {
                NetworkStream stream = client.GetStream();
                StreamReader reader = new(stream, Encoding.UTF8);
                StreamWriter writer = new(stream, Encoding.UTF8) { AutoFlush = true };

                try
                {
                    string? line = reader.ReadLine();
                    if (line == null)
                    {
                        return;
                    }

                    Request? request = JsonSerializer.Deserialize<Request>(line, _jsonOptions);
                    if (request == null)
                    {
                        Response error = Response.Error("Неверный формат запроса");
                        writer.WriteLine(JsonSerializer.Serialize(error));
                        return;
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Операция от {clientAddress}: {request.Operation}");

                    Response response = _handler.Handle(request);
                    writer.WriteLine(JsonSerializer.Serialize(response));
                }
                catch (Exception ex)
                {
                    Response error = Response.Error($"Ошибка сервера: {ex.Message}");
                    writer.WriteLine(JsonSerializer.Serialize(error));
                }
                finally
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент отключился: {clientAddress}");
                }
            }
        }
    }
}