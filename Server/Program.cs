using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Consensus;
using Server.Data;
using Server.Handlers;
using Server.Network;
using Server.PubSub;
using Server.Replication;
using Server.Services;
using Server.Subscribers;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

string? primaryConnection = configuration.GetConnectionString("Primary");
string? replicaConnection = configuration.GetConnectionString("Replica");

if (string.IsNullOrEmpty(primaryConnection) || string.IsNullOrEmpty(replicaConnection))
{
    Console.WriteLine("Ошибка: строки подключения Primary и Replica не найдены в appsettings.json");
    return;
}

DbContextOptions<EducationDbContext> primaryOptions = new DbContextOptionsBuilder<EducationDbContext>()
    .UseSqlServer(primaryConnection)
    .Options;

DbContextOptions<EducationDbContext> replicaOptions = new DbContextOptionsBuilder<EducationDbContext>()
    .UseSqlServer(replicaConnection)
    .Options;

DataInstance primary = new("Primary", primaryOptions);
DataInstance replica = new("Replica", replicaOptions);
ReplicationManager replication = new(primary, replica);

Console.WriteLine("--- Проверка экземпляров хранения данных ---");
bool primaryOk = primary.IsAvailable();
bool replicaOk = replica.IsAvailable();

if (primaryOk && replicaOk)
{
    try
    {
        List<string> differences = replication.Compare();

        if (differences.Count == 0)
        {
            Console.WriteLine("Начальное состояние: Primary = Replica");
        }
        else
        {
            Console.WriteLine($"ВНИМАНИЕ: Primary != Replica, расхождений: {differences.Count}");
            foreach (string diff in differences)
                Console.WriteLine($"  {diff}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка сравнения копий: {ex.Message}");
    }
}
else if (!primaryOk && !replicaOk)
{
    Console.WriteLine("ВНИМАНИЕ: оба экземпляра недоступны, запросы будут завершаться ошибкой");
}
else
{
    Console.WriteLine($"ВНИМАНИЕ: работа в режиме одного экземпляра (Primary {primary.StateText}, Replica {replica.StateText})");
}

Console.WriteLine();
Console.WriteLine("--- Инициализация Pub/Sub ---");

Broker broker1 = new("Broker-1");
Broker broker2 = new("Broker-2");
Broker[] brokers = [broker1, broker2];

broker1.AddPeer(broker2);
broker2.AddPeer(broker1);

LoggerSubscriber loggerSubscriber = new();
CourseService courseService = new();
NotificationService notificationService = new();

foreach (Broker broker in brokers)
{
    loggerSubscriber.ConnectTo(broker);
    courseService.ConnectTo(broker);
    notificationService.ConnectTo(broker);
}

Publisher publisher = new(broker1, broker2);

Dictionary<string, Func<ISubscriber>> subscriberCatalog = new()
{
    ["CertificateService"] = () => new CertificateService(),
    ["AnalyticsService"] = () => new AnalyticsService()
};

foreach (Broker broker in brokers)
{
    Console.WriteLine();
    Console.WriteLine($"--- Таблица подписок {broker.Name} ---");
    foreach (var (eventType, names) in broker.GetSubscriptionTable())
        Console.WriteLine($"  {eventType,-20} -> {(names.Count > 0 ? string.Join(", ", names) : "нет подписчиков")}");

    broker.StartQueueProcessing(TimeSpan.FromSeconds(10));
}

Console.WriteLine();
Console.WriteLine($"--- Порядок Broker для Publisher: {string.Join(" -> ", brokers.Select(b => b.Name))} ---");
Console.WriteLine($"--- Репликация очереди: {string.Join(" <-> ", brokers.Select(b => b.Name))} ---");
Console.WriteLine($"--- Каталог (не подключены): {string.Join(", ", subscriberCatalog.Keys)} ---");
Console.WriteLine();

Console.WriteLine("--- Инициализация узлов ---");

Cluster cluster = new(5);
cluster.RunElection();
cluster.StartHeartbeatMonitor(TimeSpan.FromSeconds(5));

Console.WriteLine($"--- Узлов: {cluster.Nodes.Count}, Majority = {cluster.Majority}, heartbeat каждые 5 сек ---");
Console.WriteLine();

EducationService service = new(replication, publisher);
PubSubService pubSub = new(brokers, publisher, subscriberCatalog);
ConsensusService consensus = new(cluster, replication);
OperationHandler handler = new(service, replication, pubSub, consensus);
TcpServer server = new(handler);

server.Start();