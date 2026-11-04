using Shared;
using Xunit;

namespace Tests.Integration
{
    [Trait("Category", "Integration")]
    public class ContainerTests
    {
        private static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(2);

        // Интеграционный тест 1 — сервер в контейнере принимает запрос клиента, узлы ЛР №7 работают
        [Fact]
        public async Task Server_InContainer_AcceptsRequest()
        {
            Response response = await TcpTestClient.SendAsync("GetNodes");

            Assert.Equal("ok", response.Status);
            Assert.Contains("Лидер: Node", response.Message);
        }

        // Интеграционный тест 2 — сервер обращается к обеим БД через Docker Network
        [Fact]
        public async Task Server_ReachesBothDatabases_ThroughDockerNetwork()
        {
            Response response = await TcpTestClient.SendAsync("GetStatus");

            Assert.Equal("ok", response.Status);
            Assert.Contains("Primary -> AVAILABLE", response.Message);
            Assert.Contains("Replica -> AVAILABLE", response.Message);
        }

        // Интеграционный тест 3 — некорректный запрос через сеть возвращает понятную ошибку
        [Fact]
        public async Task Server_InvalidRequest_ReturnsError()
        {
            Response response = await TcpTestClient.SendAsync("UnknownOperation");

            Assert.Equal("error", response.Status);
            Assert.Contains("Неизвестная операция", response.Message);
        }

        // Интеграционный тест 4 — остановка контейнера db-primary: работа через Replica, после запуска — снова Primary
        [Fact]
        public async Task PrimaryContainer_StopAndStart_FailoverAndRecovery()
        {
            DockerCli.Run("stop db-primary");

            Response during;
            try
            {
                during = await TcpTestClient.SendAsync("GetStatus");
            }
            finally
            {
                DockerCli.Run("start db-primary");
                await DockerCli.WaitHealthyAsync("db-primary", HealthTimeout);
            }

            Response after = await TcpTestClient.SendAsync("GetStatus");

            Assert.Equal("ok", during.Status);
            Assert.Contains("Primary -> UNAVAILABLE", during.Message);
            Assert.Contains("Рабочий экземпляр: Replica", during.Message);
            Assert.Contains("Рабочий экземпляр: Primary", after.Message);
        }

        // Интеграционный тест 5 — перезапуск контейнеров БД: данные сохраняются в томах
        [Fact]
        public async Task DatabaseContainers_Restart_DataPersists()
        {
            Response before = await TcpTestClient.SendAsync("CompareReplicas");

            DockerCli.Run("restart db-primary db-replica");
            await DockerCli.WaitHealthyAsync("db-primary", HealthTimeout);
            await DockerCli.WaitHealthyAsync("db-replica", HealthTimeout);

            Response after = await TcpTestClient.SendAsync("CompareReplicas");

            Assert.Equal("ok", before.Status);
            Assert.Equal("ok", after.Status);
            Assert.Equal(before.Message, after.Message);
        }
    }
}