using Shared;
using Xunit;

namespace Tests.Integration
{
    [Trait("Category", "Kubernetes")]
    public class KubernetesTests : IClassFixture<PortForwardFixture>
    {
        private const string Deployment = "education-server";
        private const string Label = "app=education-server";
        private const int Replicas = 3;

        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(2);

        private readonly PortForwardFixture _forward;

        public KubernetesTests(PortForwardFixture forward) => _forward = forward;

        private static Task<Response> SendAsync(string operation, object? data = null) =>
            TcpTestClient.SendAsync(operation, data, PortForwardFixture.LocalPort);

        // Kubernetes-тест 1 — сервер в Pod принимает запрос через Service, узлы ЛР №7 работают
        [Fact]
        public async Task Server_ThroughService_AcceptsRequest()
        {
            Response response = await SendAsync("GetNodes");

            Assert.Equal("ok", response.Status);
            Assert.Contains("Лидер: Node", response.Message);
        }

        // Kubernetes-тест 2 — Pod обращается к обеим БД (внешний компонент)
        [Fact]
        public async Task Server_ReachesBothDatabases_FromPod()
        {
            Response response = await SendAsync("GetStatus");

            Assert.Equal("ok", response.Status);
            Assert.Contains("Primary -> AVAILABLE", response.Message);
            Assert.Contains("Replica -> AVAILABLE", response.Message);
        }

        // Kubernetes-тест 3 — операция предметной области выполняется через Service
        [Fact]
        public async Task Server_GetCourse_ReturnsData()
        {
            Response response = await SendAsync("GetCourse", new { Id = 1 });

            Assert.Equal("ok", response.Status);
            Assert.Contains("Основы программирования", response.Data);
        }

        // Kubernetes-тест 4 — некорректный запрос возвращает понятную ошибку
        [Fact]
        public async Task Server_InvalidRequest_ReturnsError()
        {
            Response response = await SendAsync("UnknownOperation");

            Assert.Equal("error", response.Status);
            Assert.Contains("Неизвестная операция", response.Message);
        }

        // Kubernetes-тест 5 — Deployment поддерживает заданное количество экземпляров
        [Fact]
        public void Deployment_HasExpectedReadyReplicas()
        {
            Assert.Equal(Replicas, KubectlCli.ReadyReplicas(Deployment));
            Assert.Equal(Replicas, KubectlCli.PodNames(Label).Length);
        }

        // Kubernetes-тест 6 — каждый Pod получил APP_MODE из ConfigMap
        [Fact]
        public void Pods_ReceiveAppModeFromConfigMap()
        {
            foreach (string pod in KubectlCli.PodNames(Label))
                Assert.Contains("Режим работы: kubernetes", KubectlCli.Run($"logs {pod}"));
        }

        // Kubernetes-тест 7 — удаление Pod: ReplicaSet создаёт замену, приложение снова доступно
        [Fact]
        public async Task DeletePod_ReplicaSetRestoresReplicas()
        {
            string victim = KubectlCli.PodNames(Label)[0];

            KubectlCli.Run($"delete pod {victim}");
            await KubectlCli.WaitReadyAsync(Deployment, Label, Replicas, ReadyTimeout);

            string[] after = KubectlCli.PodNames(Label);
            _forward.Restart();
            Response response = await SendAsync("GetStatus");

            Assert.DoesNotContain(victim, after);
            Assert.Equal(Replicas, after.Length);
            Assert.Equal("ok", response.Status);
        }
    }
}