using Client.Network;
using Shared;
using System.Text.Json;

namespace Client.UI
{
    public static class ConsoleInput
    {
        public static int? ReadId(string prompt)
        {
            Console.Write(prompt);
            return int.TryParse(Console.ReadLine(), out int value) && value > 0 ? value : null;
        }

        public static Response InvalidId() => Response.Error("Некорректный ввод: Id должен быть положительным числом");

        public static bool TryChoose<T>(string prompt, IReadOnlyList<T> values, out T value)
        {
            Console.WriteLine(prompt);
            Console.Write("> ");

            string? input = Console.ReadLine();

            for (int i = 0; i < values.Count; i++)
            {
                if (input == (i + 1).ToString())
                {
                    value = values[i];
                    return true;
                }
            }

            value = default!;
            return false;
        }

        public static Response InvalidChoice(int count) => Response.Error(count == 2
            ? "Некорректный ввод: выберите 1 или 2"
            : $"Некорректный ввод: выберите от 1 до {count}");

        public static (string? Name, Response? Error) SelectByNumber(ServerClient client, string operation, string prompt, string emptyMessage)
        {
            Response list = client.Send(operation);
            if (list.Status != "ok")
                return (null, list);

            ResponsePrinter.Print(list);

            string[] names = ParseNames(list.Data);
            if (names.Length == 0)
                return (null, Response.Error(emptyMessage));

            int? number = ReadId($"\n{prompt} (1-{names.Length}): ");
            if (number == null || number > names.Length)
                return (null, Response.Error($"Некорректный ввод: номер должен быть от 1 до {names.Length}"));

            return (names[number.Value - 1], null);
        }

        private static string[] ParseNames(string? json)
        {
            if (json == null)
                return [];

            try
            {
                return JsonSerializer.Deserialize<string[]>(json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
