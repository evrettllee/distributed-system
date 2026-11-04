using Microsoft.Extensions.Configuration;
using Server.Consensus;
using Server.Handlers;
using Server.PubSub;
using Server.Replication;
using Server.Services;
using Server.Startup;
using Shared;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class DeploymentTests
    {
        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static OperationHandler CreateHandler(ReplicationManager replication)
        {
            Broker broker = new("TestBroker");
            Publisher publisher = new(broker);

            return new OperationHandler(
                new EducationService(replication, publisher),
                replication,
                new PubSubService([broker], publisher, new Dictionary<string, Func<ISubscriber>>()),
                new ConsensusService(new Cluster(5), replication));
        }

        private static Request GetCourse(int id) => new() { Operation = "GetCourse", Data = $"{{\"Id\":{id}}}" };

        // Тест 54 — порт сервера из конфигурации: значение по умолчанию, корректное и некорректное
        [Theory]
        [InlineData(null, 8080)]
        [InlineData("9000", 9000)]
        [InlineData("abc", null)]
        [InlineData("70000", null)]
        public void Settings_Port_ParsedOrRejected(string? value, int? expected)
        {
            IConfiguration configuration = Config(new() { ["Server:Port"] = value });

            Assert.Equal(expected, ServerSettings.ReadPort(configuration));
        }

        // Тест 55 — строка подключения: нет или некорректна — ошибка конфигурации, корректная — принимается
        [Fact]
        public void Settings_ConnectionString_ValidatedBeforeStart()
        {
            string valid = "Server=db-primary,1433;Database=EDUCATION;User Id=sa;Password=x;TrustServerCertificate=True";

            Assert.Null(ServerSettings.ReadConnection(Config(new()), "Primary"));
            Assert.Null(ServerSettings.ReadConnection(Config(new() { ["ConnectionStrings:Primary"] = "это не строка подключения" }), "Primary"));
            Assert.Equal(valid, ServerSettings.ReadConnection(Config(new() { ["ConnectionStrings:Primary"] = valid }), "Primary"));
        }

        // Тест 56 — переменная окружения перекрывает значение из appsettings (как в Docker)
        [Fact]
        public void Settings_EnvironmentVariable_OverridesJson()
        {
            const string variable = "ConnectionStrings__Replica";
            string fromDocker = "Server=db-replica,1433;Database=EDUCATION_REPLICA;User Id=sa;Password=x";

            Environment.SetEnvironmentVariable(variable, fromDocker);
            try
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Replica"] = "Server=localhost,1435;Database=EDUCATION_REPLICA"
                    })
                    .AddEnvironmentVariables()
                    .Build();

                Assert.Equal(fromDocker, configuration.GetConnectionString("Replica"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
        }

        // Тест 57 — сервер корректно обрабатывает запрос клиента
        [Fact]
        public async Task Handler_ValidRequest_ReturnsData()
        {
            OperationHandler handler = CreateHandler(new ReplicationManager(
                TestDatabase.CreateSeeded("Primary"), TestDatabase.CreateSeeded("Replica")));

            Response response = await handler.Handle(GetCourse(1));

            Assert.Equal("ok", response.Status);
            Assert.Contains("Основы программирования", response.Data);
        }

        // Тест 58 — невалидные запросы: неизвестная операция, битый JSON, нет данных, неверное значение
        [Theory]
        [InlineData("DropDatabase", null, "Неизвестная операция")]
        [InlineData("GetCourse", "{oops", "Некорректный запрос")]
        [InlineData("GetCourse", null, "Данные запроса отсутствуют")]
        [InlineData("ProposeCourseStatus", "{\"CourseId\":1,\"Status\":\"Deleted\"}", "Неизвестный статус курса")]
        public async Task Handler_InvalidRequest_ReturnsClearError(string operation, string? data, string expected)
        {
            OperationHandler handler = CreateHandler(new ReplicationManager(
                TestDatabase.CreateSeeded("Primary"), TestDatabase.CreateSeeded("Replica")));

            Response response = await handler.Handle(new Request { Operation = operation, Data = data });

            Assert.Equal("error", response.Status);
            Assert.Contains(expected, response.Message);
        }

        // Тест 59 — БД недоступна: понятная ошибка без падения; после возврата БД запросы снова выполняются
        [Fact]
        public async Task Handler_DatabaseUnavailable_ClearErrorThenRecovery()
        {
            TestDataInstance primary = TestDatabase.CreateSeeded("Primary");
            TestDataInstance replica = TestDatabase.CreateSeeded("Replica");
            OperationHandler handler = CreateHandler(new ReplicationManager(primary, replica));

            primary.IsDown = true;
            replica.IsDown = true;
            Response failed = await handler.Handle(GetCourse(1));

            primary.IsDown = false;
            replica.IsDown = false;
            Response recovered = await handler.Handle(GetCourse(1));

            Assert.Equal("error", failed.Status);
            Assert.Contains("Сервис временно недоступен", failed.Message);
            Assert.Equal("ok", recovered.Status);
        }

        // Тест 60 — сохранность данных при перезапуске: новый экземпляр сервера видит записанные ранее данные
        [Fact]
        public async Task Restart_DataPersistsForNewServerInstance()
        {
            var primaryOptions = TestDatabase.CreateOptions();
            var replicaOptions = TestDatabase.CreateOptions();

            TestDataInstance primary = new("Primary", primaryOptions);
            TestDataInstance replica = new("Replica", replicaOptions);
            TestDatabase.Seed(primary);
            TestDatabase.Seed(replica);

            OperationHandler beforeRestart = CreateHandler(new ReplicationManager(primary, replica));
            Response enroll = await beforeRestart.Handle(new Request
            {
                Operation = "EnrollStudent",
                Data = "{\"StudentId\":1,\"CourseId\":1}"
            });

            OperationHandler afterRestart = CreateHandler(new ReplicationManager(
                new TestDataInstance("Primary", primaryOptions),
                new TestDataInstance("Replica", replicaOptions)));
            Response course = await afterRestart.Handle(GetCourse(1));

            Assert.Equal("ok", enroll.Status);
            Assert.Equal("ok", course.Status);
            Assert.Contains("\"AvailableStudents\":29", course.Data);
        }
    }
}