using Client.Network;
using Shared;
using System.Text.Json;

Console.Write("Введите смещение часов (секунды, 0 если нет): ");
int offset = int.TryParse(Console.ReadLine(), out int parsed) ? parsed : 0;

ServerClient client = new(clockOffsetSeconds: offset);

while (true)
{
    Console.WriteLine("\nВыберите операцию:");
    Console.WriteLine("1. Записать студента на курс (EnrollStudent)");
    Console.WriteLine("2. Получить курс (GetCourse)");
    Console.WriteLine("3. Сдать задание (SubmitAssignment)");
    Console.WriteLine("4. Завершить курс (CompleteCourse)");
    Console.WriteLine("5. Показать локальное время");
    Console.WriteLine("6. Показать разницу с сервером (до синхронизации)");
    Console.WriteLine("7. Синхронизировать время с сервером");
    Console.WriteLine("8. Состояние Primary / Replica (GetStatus)");
    Console.WriteLine("9. Сравнить Primary и Replica (CompareReplicas)");
    Console.WriteLine("10. Восстановление и синхронизация (RecoverPrimary)");
    Console.WriteLine("11. Подписчики и их состояние (GetSubscribers)");
    Console.WriteLine("12. Изменить состояние подписчика (SetSubscriberState)");
    Console.WriteLine("13. Зарегистрировать нового подписчика (RegisterSubscriber)");
    Console.WriteLine("14. Изменить состояние Broker (SetBrokerState)");
    Console.WriteLine("15. Ошибки публикации (GetPublisherErrors)");
    Console.WriteLine("16. Состояние Broker и очередей (GetBrokers)");
    Console.WriteLine("17. Повторная доставка из очереди (ProcessQueue)");
    Console.WriteLine("18. Статистика событий (GetEventStats)");
    Console.WriteLine("0. Выход");
    Console.Write("> ");

    string? choice = Console.ReadLine();

    if (choice == "0") break;

    if (choice == "5")
    {
        client.ShowLocalTime();
        continue;
    }

    if (choice == "6")
    {
        client.ShowTimeDiff();
        continue;
    }

    if (choice == "7")
    {
        client.SyncTime();
        continue;
    }

    Response response = choice switch
    {
        "1" => EnrollStudent(client),
        "2" => GetCourse(client),
        "3" => SubmitAssignment(client),
        "4" => CompleteCourse(client),
        "8" => client.Send("GetStatus"),
        "9" => client.Send("CompareReplicas"),
        "10" => client.Send("RecoverPrimary"),
        "11" => client.Send("GetSubscribers"),
        "12" => SetSubscriberState(client),
        "13" => RegisterSubscriber(client),
        "14" => SetBrokerState(client),
        "15" => client.Send("GetPublisherErrors"),
        "16" => client.Send("GetBrokers"),
        "17" => client.Send("ProcessQueue"),
        "18" => client.Send("GetEventStats"),
        _ => Response.Error("Неизвестная команда")
    };

    PrintResponse(response);
}

static void PrintResponse(Response response)
{
    bool isOk = response.Status == "ok";

    Console.WriteLine();
    Console.ForegroundColor = isOk ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(isOk ? "SUCCESS" : "ERROR");
    Console.ResetColor();

    if (response.Message != null)
    {
        string[] lines = response.Message.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = i == 0 ? $"-- {lines[i]}" : lines[i];

            if (line.Contains("Экземпляр: Replica"))
                Console.ForegroundColor = ConsoleColor.Yellow;
            else if (line.Contains("UNAVAILABLE") || line.Contains("FAILING") || line.Contains("UNSTABLE") || line.Contains("недоступен"))
                Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine(line);
            Console.ResetColor();
        }
    }

    if (response.Data != null)
        PrintCourse(response.Data);
}

static void PrintCourse(string json)
{
    try
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Title", out _))
            return;

        Console.WriteLine($"-- Курс: {root.GetProperty("Title").GetString()}");
        Console.WriteLine($"-- Id: {root.GetProperty("Id").GetInt32()}");
        Console.WriteLine($"-- Id преподавателя: {root.GetProperty("TeacherId").GetInt32()}");
        Console.WriteLine($"-- Макс. студентов: {root.GetProperty("MaxStudents").GetInt32()}");
        Console.WriteLine($"-- Доступно мест: {root.GetProperty("AvailableStudents").GetInt32()}");
    }
    catch
    {
        Console.WriteLine($"-- {json}");
    }
}

static int? ReadId(string prompt)
{
    Console.Write(prompt);
    return int.TryParse(Console.ReadLine(), out int value) && value > 0 ? value : null;
}

static Response InvalidInput() => Response.Error("Некорректный ввод: Id должен быть положительным числом");

static Response EnrollStudent(ServerClient client)
{
    int? studentId = ReadId("Id студента: ");
    if (studentId == null) return InvalidInput();

    int? courseId = ReadId("Id курса: ");
    if (courseId == null) return InvalidInput();

    return client.Send("EnrollStudent", new { StudentId = studentId, CourseId = courseId });
}

static Response GetCourse(ServerClient client)
{
    int? courseId = ReadId("Id курса: ");
    if (courseId == null) return InvalidInput();

    return client.Send("GetCourse", new { Id = courseId });
}

static Response SubmitAssignment(ServerClient client)
{
    int? assignmentId = ReadId("Id задания: ");
    if (assignmentId == null) return InvalidInput();

    return client.Send("SubmitAssignment", new { Id = assignmentId });
}

static Response CompleteCourse(ServerClient client)
{
    int? studentId = ReadId("Id студента: ");
    if (studentId == null) return InvalidInput();

    int? courseId = ReadId("Id курса: ");
    if (courseId == null) return InvalidInput();

    return client.Send("CompleteCourse", new { StudentId = studentId, CourseId = courseId });
}

static string[] ParseNames(string? json)
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

static (string? Name, Response? Error) SelectByNumber(ServerClient client, string operation, string prompt, string emptyMessage)
{
    Response list = client.Send(operation);
    if (list.Status != "ok")
        return (null, list);

    PrintResponse(list);

    string[] names = ParseNames(list.Data);
    if (names.Length == 0)
        return (null, Response.Error(emptyMessage));

    int? number = ReadId($"\n{prompt} (1-{names.Length}): ");
    if (number == null || number > names.Length)
        return (null, Response.Error($"Некорректный ввод: номер должен быть от 1 до {names.Length}"));

    return (names[number.Value - 1], null);
}

static Response SetSubscriberState(ServerClient client)
{
    var (name, error) = SelectByNumber(client, "GetSubscribers", "Номер подписчика", "Нет зарегистрированных подписчиков");
    if (error != null)
        return error;

    Console.WriteLine("Состояние: 1 - Available, 2 - Unavailable, 3 - Failing, 4 - Unstable (2 сбоя, затем успех)");
    Console.Write("> ");

    string? state = Console.ReadLine() switch
    {
        "1" => "Available",
        "2" => "Unavailable",
        "3" => "Failing",
        "4" => "Unstable",
        _ => null
    };

    if (state == null)
        return Response.Error("Некорректный ввод: выберите от 1 до 4");

    return client.Send("SetSubscriberState", new { Name = name, State = state });
}

static Response RegisterSubscriber(ServerClient client)
{
    var (name, error) = SelectByNumber(client, "GetSubscriberCatalog", "Номер подписчика для регистрации", "Нет доступных для регистрации подписчиков");
    if (error != null)
        return error;

    return client.Send("RegisterSubscriber", new { Name = name });
}

static Response SetBrokerState(ServerClient client)
{
    var (name, error) = SelectByNumber(client, "GetBrokers", "Номер Broker", "Нет Broker");
    if (error != null)
        return error;

    Console.WriteLine("Состояние Broker: 1 - Available, 2 - Unavailable");
    Console.Write("> ");

    bool? available = Console.ReadLine() switch
    {
        "1" => true,
        "2" => false,
        _ => null
    };

    if (available == null)
        return Response.Error("Некорректный ввод: выберите 1 или 2");

    return client.Send("SetBrokerState", new { Name = name, Available = available });
}