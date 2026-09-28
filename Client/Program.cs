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
        Console.WriteLine($"-- {response.Message}");

    if (response.Data != null)
        PrintCourse(response.Data);
}

static void PrintCourse(string json)
{
    try
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Console.WriteLine($"-- Курс: {root.GetProperty("Title").GetString()}");
        Console.WriteLine($"-- Id: {root.GetProperty("Id").GetInt32()}");
        Console.WriteLine($"-- Id преподавателя: {root.GetProperty("TeacherId").GetInt32()}");
        Console.WriteLine($"-- Макс. студентов: {root.GetProperty("MaxStudents").GetInt32()}");
    }
    catch
    {
        Console.WriteLine($"-- {json}");
    }
}

static Response EnrollStudent(ServerClient client)
{
    Console.Write("Id студента: ");
    int studentId = int.Parse(Console.ReadLine()!);
    Console.Write("Id курса: ");
    int courseId = int.Parse(Console.ReadLine()!);
    return client.Send("EnrollStudent", new { StudentId = studentId, CourseId = courseId });
}

static Response GetCourse(ServerClient client)
{
    Console.Write("Id курса: ");
    int courseId = int.Parse(Console.ReadLine()!);
    return client.Send("GetCourse", new { Id = courseId });
}

static Response SubmitAssignment(ServerClient client)
{
    Console.Write("Id задания: ");
    int assignmentId = int.Parse(Console.ReadLine()!);
    return client.Send("SubmitAssignment", new { Id = assignmentId });
}

static Response CompleteCourse(ServerClient client)
{
    Console.Write("Id студента: ");
    int studentId = int.Parse(Console.ReadLine()!);
    Console.Write("Id курса: ");
    int courseId = int.Parse(Console.ReadLine()!);
    return client.Send("CompleteCourse", new { StudentId = studentId, CourseId = courseId });
}