using Microsoft.Extensions.Configuration;
using Server.Consensus;
using Server.Handlers;
using Server.Network;
using Server.Replication;
using Server.Services;
using Server.Startup;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

ReplicationManager? replication = StorageSetup.Create(configuration);
if (replication == null)
    return;

PubSubSetup pubSub = PubSubSetup.Create(queueInterval: TimeSpan.FromSeconds(10));
Cluster cluster = ClusterSetup.Create(nodeCount: 5, heartbeatInterval: TimeSpan.FromSeconds(5));

OperationHandler handler = new(
    new EducationService(replication, pubSub.Publisher),
    replication,
    new PubSubService(pubSub.Brokers, pubSub.Publisher, pubSub.Catalog),
    new ConsensusService(cluster, replication));

new TcpServer(handler).Start();
