using Client.Menu;
using Client.Network;
using Client.UI;
using Shared;

string[] menu =
[
    "1. Записать студента на курс (EnrollStudent)",
    "2. Получить курс (GetCourse)",
    "3. Сдать задание (SubmitAssignment)",
    "4. Завершить курс (CompleteCourse)",
    "5. Показать локальное время",
    "6. Показать разницу с сервером (до синхронизации)",
    "7. Синхронизировать время с сервером",
    "8. Состояние Primary / Replica (GetStatus)",
    "9. Сравнить Primary и Replica (CompareReplicas)",
    "10. Восстановление и синхронизация (RecoverPrimary)",
    "11. Подписчики и их состояние (GetSubscribers)",
    "12. Изменить состояние подписчика (SetSubscriberState)",
    "13. Зарегистрировать нового подписчика (RegisterSubscriber)",
    "14. Изменить состояние Broker (SetBrokerState)",
    "15. Ошибки публикации (GetPublisherErrors)",
    "16. Состояние Broker и очередей (GetBrokers)",
    "17. Повторная доставка из очереди (ProcessQueue)",
    "18. Статистика событий (GetEventStats)",
    "19. Состояние узлов (GetNodes)",
    "20. Изменить состояние узла (SetNodeState)",
    "21. Режим голосования узла (SetNodeVote)",
    "22. Запустить выбор лидера (RunElection)",
    "23. Проверить лидера по heartbeat (CheckHeartbeat)",
    "24. Предложить статус курса (ProposeCourseStatus)",
    "0. Выход"
];

Console.Write("Введите смещение часов (секунды, 0 если нет): ");
int offset = int.TryParse(Console.ReadLine(), out int parsed) ? parsed : 0;

ServerClient client = new(clockOffsetSeconds: offset);

while (true)
{
    Console.WriteLine("\nВыберите операцию:");
    foreach (string item in menu)
        Console.WriteLine(item);
    Console.Write("> ");

    string? choice = Console.ReadLine();

    switch (choice)
    {
        case "0":
            return;
        case "5":
            client.ShowLocalTime();
            continue;
        case "6":
            client.ShowTimeDiff();
            continue;
        case "7":
            client.SyncTime();
            continue;
    }

    Response response = choice switch
    {
        "1" => EducationMenu.EnrollStudent(client),
        "2" => EducationMenu.GetCourse(client),
        "3" => EducationMenu.SubmitAssignment(client),
        "4" => EducationMenu.CompleteCourse(client),
        "8" => ReplicationMenu.GetStatus(client),
        "9" => ReplicationMenu.CompareReplicas(client),
        "10" => ReplicationMenu.RecoverPrimary(client),
        "11" => PubSubMenu.GetSubscribers(client),
        "12" => PubSubMenu.SetSubscriberState(client),
        "13" => PubSubMenu.RegisterSubscriber(client),
        "14" => PubSubMenu.SetBrokerState(client),
        "15" => PubSubMenu.GetPublisherErrors(client),
        "16" => PubSubMenu.GetBrokers(client),
        "17" => PubSubMenu.ProcessQueue(client),
        "18" => PubSubMenu.GetEventStats(client),
        "19" => ConsensusMenu.GetNodes(client),
        "20" => ConsensusMenu.SetNodeState(client),
        "21" => ConsensusMenu.SetNodeVote(client),
        "22" => ConsensusMenu.RunElection(client),
        "23" => ConsensusMenu.CheckHeartbeat(client),
        "24" => ConsensusMenu.ProposeCourseStatus(client),
        _ => Response.Error("Неизвестная команда")
    };

    ResponsePrinter.Print(response);
}
