using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Common;
using Server.Data;
using Server.Replication;

namespace Server.Startup
{
    public static class StorageSetup
    {
        public static ReplicationManager? Create(IConfiguration configuration)
        {
            string? primaryConnection = configuration.GetConnectionString("Primary");
            string? replicaConnection = configuration.GetConnectionString("Replica");

            if (string.IsNullOrEmpty(primaryConnection) || string.IsNullOrEmpty(replicaConnection))
            {
                Logger.Log("Ошибка: строки подключения Primary и Replica не найдены в appsettings.json");
                return null;
            }

            DataInstance primary = new("Primary", SqlServerOptions(primaryConnection));
            DataInstance replica = new("Replica", SqlServerOptions(replicaConnection));
            ReplicationManager replication = new(primary, replica);

            CheckInitialState(replication);
            return replication;
        }

        private static DbContextOptions<EducationDbContext> SqlServerOptions(string connection) =>
            new DbContextOptionsBuilder<EducationDbContext>()
                .UseSqlServer(connection)
                .Options;

        private static void CheckInitialState(ReplicationManager replication)
        {
            bool primaryOk = replication.Primary.IsAvailable();
            bool replicaOk = replication.Replica.IsAvailable();

            if (!primaryOk && !replicaOk)
            {
                Logger.Log("ВНИМАНИЕ: оба экземпляра недоступны, запросы будут завершаться ошибкой");
                return;
            }

            if (!primaryOk || !replicaOk)
            {
                Logger.Log($"ВНИМАНИЕ: работа в режиме одного экземпляра ({replication.StatesText})");
                return;
            }

            try
            {
                List<string> differences = replication.Compare();
                ReplicationManager.LogDifferences($"Начальное состояние: {ReplicationManager.DifferencesText(differences)}", differences);
            }
            catch (Exception ex)
            {
                Logger.Log($"Ошибка сравнения копий: {ex.Message}");
            }
        }
    }
}
