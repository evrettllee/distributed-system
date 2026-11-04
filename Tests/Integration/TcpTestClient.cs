using Shared;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Tests.Integration
{
    public static class TcpTestClient
    {
        public static string Host => Environment.GetEnvironmentVariable("IT_HOST") ?? "127.0.0.1";

        public static int Port => int.TryParse(Environment.GetEnvironmentVariable("IT_PORT"), out int port) ? port : 8080;

        public static async Task<Response> SendAsync(string operation, object? data = null)
        {
            using TcpClient client = new();
            await client.ConnectAsync(Host, Port);

            NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.UTF8);
            using StreamWriter writer = new(stream, Encoding.UTF8) { AutoFlush = true };

            Request request = new()
            {
                Operation = operation,
                Data = data != null ? JsonSerializer.Serialize(data) : null
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            string? line = await reader.ReadLineAsync();

            if (line == null)
                return Response.Error("Сервер не вернул ответ");

            return JsonSerializer.Deserialize<Response>(line) ?? Response.Error("Не удалось разобрать ответ");
        }
    }
}