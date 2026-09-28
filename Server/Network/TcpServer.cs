using Server.Common;
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
        private int _clientCounter = 0;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public TcpServer(OperationHandler handler, int port = 8080)
        {
            _handler = handler;
            _port = port;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        public void Start()
        {
            _listener.Start();
            Console.WriteLine($"Сервер запущен на {IPAddress.Any}:{_port}\n");

            while (true)
            {
                TcpClient client = _listener.AcceptTcpClient();
                int clientId = Interlocked.Increment(ref _clientCounter);
                Task.Run(() => HandleClient(client, clientId));
            }
        }

        private async Task HandleClient(TcpClient client, int clientId)
        {
            string clientAddress = client.Client.RemoteEndPoint?.ToString() ?? "неизвестный адрес";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> CONNECT ({clientAddress})");

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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> пустой запрос");
                        return;
                    }

                    Request? request = JsonSerializer.Deserialize<Request>(line, _jsonOptions);
                    if (request == null)
                    {
                        Response error = Response.Error("Неверный формат запроса");
                        writer.WriteLine(JsonSerializer.Serialize(error));
                        return;
                    }

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> START | Операция: {request.Operation} | Task ID: {Task.CurrentId}");

                    await Task.Delay(2_000);

                    ClientContext.ClientId = clientId;

                    Response response = await _handler.Handle(request);
                    writer.WriteLine(JsonSerializer.Serialize(response));

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> END   | Операция: {request.Operation} | Task ID: {Task.CurrentId}");
                }
                catch (IOException)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> отключился во время обработки");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> ОШИБКА: {ex.Message}");
                    try
                    {
                        NetworkStream stream2 = client.GetStream();
                        StreamWriter writer2 = new(stream2, Encoding.UTF8) { AutoFlush = true };
                        Response error = Response.Error($"Ошибка сервера: {ex.Message}");
                        writer2.WriteLine(JsonSerializer.Serialize(error));
                    }
                    catch { }
                }
                finally
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Client {clientId} -> DISCONNECT ({clientAddress})");
                }
            }
        }
    }
}