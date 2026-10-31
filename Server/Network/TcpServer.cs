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
            ClientContext.ClientId = clientId;

            string clientAddress = client.Client.RemoteEndPoint?.ToString() ?? "неизвестный адрес";

            Logger.Log($"CONNECT ({clientAddress})");

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
                        Logger.Log("пустой запрос");
                        return;
                    }

                    Request? request;
                    try
                    {
                        request = JsonSerializer.Deserialize<Request>(line, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        request = null;
                    }

                    if (request == null)
                    {
                        Logger.Log("некорректный запрос");
                        Response error = Response.Error("Неверный формат запроса");
                        writer.WriteLine(JsonSerializer.Serialize(error));
                        return;
                    }

                    Logger.Log($"START | Операция: {request.Operation} | Task ID: {Task.CurrentId}");

                    await Task.Delay(2_000);

                    Response response = await _handler.Handle(request);
                    writer.WriteLine(JsonSerializer.Serialize(response));

                    Logger.Log($"END   | Операция: {request.Operation} | Task ID: {Task.CurrentId}");
                }
                catch (IOException)
                {
                    Logger.Log("отключился во время обработки");
                }
                catch (Exception ex)
                {
                    Logger.Log($"ОШИБКА: {ex.Message}");
                    try
                    {
                        Response error = Response.Error($"Ошибка сервера: {ex.Message}");
                        writer.WriteLine(JsonSerializer.Serialize(error));
                    }
                    catch { }
                }
                finally
                {
                    Logger.Log($"DISCONNECT ({clientAddress})");
                }
            }
        }
    }
}