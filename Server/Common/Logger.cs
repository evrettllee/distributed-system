namespace Server.Common
{
    public static class Logger
    {
        public static void Log(string message)
        {
            string who = ClientContext.ClientId > 0 ? $"Client {ClientContext.ClientId}" : "Server";
            Write($"{who} -> {message}");
        }

        public static void Log(string source, string message, int? clientId = null)
        {
            int id = clientId ?? ClientContext.ClientId;
            string client = id > 0 ? $"Client {id} | " : "";
            Write($"{client}{source} -> {message}");
        }

        private static void Write(string line) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
    }
}
