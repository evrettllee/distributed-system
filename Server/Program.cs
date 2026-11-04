using Microsoft.Extensions.Configuration;
using Server.Common;
using Server.Consensus;
using Server.Handlers;
using Server.Network;
using Server.Replication;
using Server.Services;
using Server.Startup;
using System.Net.Sockets;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

int? port = ServerSettings.ReadPort(configuration);
ReplicationManager? replication = StorageSetup.Create(configuration);

if (port == null || replication == null)
{
    Logger.Log("Сервер не запущен: исправьте параметры конфигурации");
    return 1;
}

PubSubSetup pubSub = PubSubSetup.Create(queueInterval: TimeSpan.FromSeconds(10));
Cluster cluster = ClusterSetup.Create(nodeCount: 5, heartbeatInterval: TimeSpan.FromSeconds(5));

OperationHandler handler = new(
    new EducationService(replication, pubSub.Publisher),
    replication,
    new PubSubService(pubSub.Brokers, pubSub.Publisher, pubSub.Catalog),
    new ConsensusService(cluster, replication));

try
{
    new TcpServer(handler, port.Value).Start();
}
catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
{
    Logger.Log($"Ошибка: порт {port} уже занят другим процессом");
    return 1;
}
catch (SocketException ex)
{
    Logger.Log($"Ошибка запуска сервера на порту {port}: {ex.Message}");
    return 1;
}

return 0;