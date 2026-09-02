using Client.Network;
using Shared;

ServerClient client = new();

while (true)
{
    Console.WriteLine("\nВыберите операцию:");
    Console.WriteLine("1. Записать студента на курс (EnrollStudent)");
    Console.WriteLine("2. Получить курс (GetCourse)");
    Console.WriteLine("3. Сдать задание (SubmitAssignment)");
    Console.WriteLine("4. Завершить курс (CompleteCourse)");
    Console.WriteLine("0. Выход");
    Console.Write("> ");

    string? choice = Console.ReadLine();

    Response response = choice switch
    {
        "1" => EnrollStudent(client),
        "2" => GetCourse(client),
        "3" => SubmitAssignment(client),
        "4" => CompleteCourse(client),
        "0" => null!,
        _ => Response.Error("Неизвестная команда")
    };

    if (choice == "0")
    {
        break;
    }

    Console.WriteLine($"\nСтатус: {response.Status}");
    if (response.Message != null)
    {
        Console.WriteLine($"Сообщение: {response.Message}");
    }
    if (response.Data != null)
    {
        Console.WriteLine($"Данные: {response.Data}");
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