using Shared;
using System.Text.Json;

namespace Client.UI
{
    public static class ResponsePrinter
    {
        private static readonly string[] ErrorMarkers =
            ["UNAVAILABLE", "FAILING", "UNSTABLE", "FAILED", "NOT ACHIEVED", "NOT ELECTED", "NO RESPONSE", "недоступен"];

        private static readonly string[] SuccessMarkers = ["CONSENSUS ACHIEVED", "LEADER"];

        public static void Print(Response response)
        {
            bool isOk = response.Status == "ok";

            Console.WriteLine();
            WriteColored(isOk ? "SUCCESS" : "ERROR", isOk ? ConsoleColor.Green : ConsoleColor.Red);

            if (response.Message != null)
            {
                string[] lines = response.Message.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = i == 0 ? $"-- {lines[i]}" : lines[i];
                    WriteColored(line, LineColor(line));
                }
            }

            if (response.Data != null)
                PrintCourse(response.Data);
        }

        private static ConsoleColor? LineColor(string line)
        {
            if (line.Contains("Экземпляр: Replica"))
                return ConsoleColor.Yellow;

            if (ErrorMarkers.Any(line.Contains))
                return ConsoleColor.Red;

            if (SuccessMarkers.Any(line.Contains))
                return ConsoleColor.Green;

            return null;
        }

        private static void WriteColored(string text, ConsoleColor? color)
        {
            if (color != null)
                Console.ForegroundColor = color.Value;

            Console.WriteLine(text);
            Console.ResetColor();
        }

        private static void PrintCourse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Title", out _))
                    return;

                Console.WriteLine($"-- Курс: {root.GetProperty("Title").GetString()}");
                Console.WriteLine($"-- Id: {root.GetProperty("Id").GetInt32()}");
                Console.WriteLine($"-- Id преподавателя: {root.GetProperty("TeacherId").GetInt32()}");
                Console.WriteLine($"-- Макс. студентов: {root.GetProperty("MaxStudents").GetInt32()}");
                Console.WriteLine($"-- Доступно мест: {root.GetProperty("AvailableStudents").GetInt32()}");

                if (root.TryGetProperty("Status", out JsonElement status))
                    Console.WriteLine($"-- Статус: {status}");
            }
            catch
            {
                Console.WriteLine($"-- {json}");
            }
        }
    }
}
