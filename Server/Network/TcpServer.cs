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
        private int _clientCounter;

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
            Logger.Log($"Сервер запущен на {IPAddress.Any}:{_port}");

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
                        Logger.Log($"пустой запрос ({clientAddress})");
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
                        Logger.Log($"некорректный запрос ({clientAddress})");
                        Response error = Response.Error("Неверный формат запроса");
                        writer.WriteLine(JsonSerializer.Serialize(error));
                        return;
                    }

                    Logger.Log($"REQUEST {request.Operation} ({clientAddress})");

                    await Task.Delay(2_000);

                    Response response = await _handler.Handle(request);
                    writer.WriteLine(JsonSerializer.Serialize(response));

                    if (response.Status != "ok")
                        Logger.Log($"{request.Operation} -> ERROR ({response.Message?.Split('\n')[^1].TrimStart('-', ' ')})");
                }
                catch (IOException)
                {
                    Logger.Log($"DISCONNECT ({clientAddress}): отключился во время обработки");
                }
                catch (Exception ex)
                {
                    Logger.Log($"ОШИБКА: {ex.Message}");
                    try
                    {
                        Response error = Response.Error($"Ошибка сервера: {ex.Message}");
                        writer.WriteLine(JsonSerializer.Serialize(error));
                    }
                    catch
                    {
                        Logger.Log($"DISCONNECT ({clientAddress}): ответ не отправлен");
                    }
                }
            }
        }
    }
}