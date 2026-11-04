using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Server.Common;

namespace Server.Startup
{
    public static class ServerSettings
    {
        public const int DefaultPort = 8080;

        public static int? ReadPort(IConfiguration configuration)
        {
            string? value = configuration["Server:Port"];
            if (string.IsNullOrWhiteSpace(value))
                return DefaultPort;

            if (int.TryParse(value, out int port) && port is > 0 and <= 65535)
                return port;

            Logger.Log($"Ошибка конфигурации: Server__Port = '{value}' не является корректным портом (1-65535)");
            return null;
        }

        public static string? ReadConnection(IConfiguration configuration, string name)
        {
            string? value = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                Logger.Log($"Ошибка конфигурации: не задана строка подключения {name} (ConnectionStrings__{name})");
                return null;
            }

            try
            {
                SqlConnectionStringBuilder builder = new(value);
                Logger.Log($"Конфигурация: {name} -> {builder.DataSource} / {builder.InitialCatalog}");
                return value;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
            {
                Logger.Log($"Ошибка конфигурации: строка подключения {name} некорректна ({ex.Message})");
                return null;
            }
        }
    }
}