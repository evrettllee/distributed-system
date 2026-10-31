using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Data;
using Server.Events;
using Server.Models;
using Server.PubSub;
using Server.Replication;
using Server.Services;
using Server.Subscribers;
using Shared;
using System.Collections.Concurrent;
using Xunit;

namespace Tests
{
    public class EducationServiceTests
    {
        private readonly TestDataInstance _primary;
        private readonly TestDataInstance _replica;
        private readonly ReplicationManager _replication;
        private readonly EducationService _service;

        private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(10);

        public EducationServiceTests()
        {
            _primary = new TestDataInstance("Primary", CreateOptions());
            _replica = new TestDataInstance("Replica", CreateOptions());

            Seed(_primary);
            Seed(_replica);

            _replication = new ReplicationManager(_primary, _replica);
            _service = new EducationService(_replication, new Publisher(new Broker("TestBroker")));
        }

        private static DbContextOptions<EducationDbContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<EducationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        }

        private static void Seed(DataInstance instance)
        {
            using EducationDbContext db = instance.CreateContext();

            db.Students.AddRange(
                new Student { Id = 1, FullName = "Иванов Иван", Email = "ivanov@mail.ru" },
                new Student { Id = 2, FullName = "Петров Пётр", Email = "petrov@mail.ru" }
            );
            db.Teachers.Add(new Teacher { Id = 1, FullName = "Смирнов", Subject = "Математика" });
            db.Courses.AddRange(
                new Course { Id = 1, Title = "Основы программирования", TeacherId = 1, MaxStudents = 30, AvailableStudents = 30 },
                new Course { Id = 2, Title = "Маленький курс", TeacherId = 1, MaxStudents = 1, AvailableStudents = 1 }
            );
            db.Assignments.Add(new Assignment { Id = 1, Title = "Лабораторная 1", CourseId = 1, StudentId = 2, IsSubmitted = false });
            db.SaveChanges();
        }

        private static int Available(DataInstance instance, int courseId)
        {
            using EducationDbContext db = instance.CreateContext();
            return db.Courses.AsNoTracking().First(c => c.Id == courseId).AvailableStudents;
        }

        private static int EnrolledCount(DataInstance instance, int courseId)
        {
            using EducationDbContext db = instance.CreateContext();
            return db.Enrollments.Count(e => e.CourseId == courseId && !e.IsCompleted);
        }

        private static int EnrollmentsOfStudent(DataInstance instance, int studentId)
        {
            using EducationDbContext db = instance.CreateContext();
            return db.Enrollments.Count(e => e.StudentId == studentId);
        }

        // Тест 1 — один клиент: обычная обработка запроса
        [Fact]
        public async Task OneClient_EnrollStudent_ReturnsOk()
        {
            Response response = await _service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Иванов Иван", response.Message);
        }

        // Тест 2 — два клиента: оба запроса обрабатываются успешно
        [Fact]
        public async Task TwoClients_BothEnrolled_ReturnsOk()
        {
            Task<Response> task1 = _service.EnrollStudent(1, 1);
            Task<Response> task2 = _service.EnrollStudent(2, 1);

            Response[] responses = await Task.WhenAll(task1, task2);

            Assert.Equal("ok", responses[0].Status);
            Assert.Equal("ok", responses[1].Status);
        }

        // Тест 3 — параллельная обработка: Client 2 начал до завершения Client 1
        [Fact]
        public async Task TwoClients_ProcessedInParallel()
        {
            DateTime start1 = DateTime.MinValue;
            DateTime start2 = DateTime.MinValue;
            DateTime end1 = DateTime.MinValue;

            Task task1 = Task.Run(async () =>
            {
                start1 = DateTime.Now;
                await _service.EnrollStudent(1, 1);
                end1 = DateTime.Now;
            });

            await Task.Delay(500);

            Task task2 = Task.Run(async () =>
            {
                start2 = DateTime.Now;
                await _service.EnrollStudent(2, 1);
            });

            await Task.WhenAll(task1, task2);

            Assert.True(start2 < end1, $"Client 2 начал в {start2:HH:mm:ss.fff}, Client 1 завершил в {end1:HH:mm:ss.fff}");
        }

        // Тест 4 — с синхронизацией: Race Condition устранён, курс на 1 место — только один успешен
        [Fact]
        public async Task TwoClients_WithLock_RaceCondition_IsFixed()
        {
            Task<Response> task1 = _service.EnrollStudent(1, 2);
            Task<Response> task2 = _service.EnrollStudent(2, 2);

            Response[] responses = await Task.WhenAll(task1, task2);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = EnrolledCount(_primary, 2);

            Assert.Equal(1, successCount);
            Assert.Equal(1, enrolled);
        }

        // Тест 5 — синхронизация: 10 параллельных клиентов, курс на 3 места — ровно 3 успешных
        [Fact]
        public async Task ManyClients_WithLock_ExactlyMaxStudentsSucceed()
        {
            int maxStudents = 3;

            foreach (DataInstance instance in new DataInstance[] { _primary, _replica })
            {
                using EducationDbContext db = instance.CreateContext();
                db.Courses.Add(new Course { Id = 3, Title = "Ограниченный курс", TeacherId = 1, MaxStudents = maxStudents, AvailableStudents = maxStudents });
                for (int i = 3; i <= 12; i++)
                    db.Students.Add(new Student { Id = i, FullName = $"Студент {i}", Email = $"student{i}@mail.ru" });
                db.SaveChanges();
            }

            List<Task<Response>> tasks = Enumerable.Range(1, 10)
                .Select(i => _service.EnrollStudent(i, 3))
                .ToList();

            Response[] responses = await Task.WhenAll(tasks);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = EnrolledCount(_primary, 3);

            Assert.Equal(maxStudents, successCount);
            Assert.Equal(maxStudents, enrolled);
        }

        // Тест 6 — GetServerTime возвращает корректное время в ISO формате
        [Fact]
        public async Task GetServerTime_ReturnsValidDateTime()
        {
            DateTime before = DateTime.UtcNow;

            Response response = await _service.GetServerTime();

            DateTime after = DateTime.UtcNow;

            Assert.Equal("ok", response.Status);
            Assert.NotNull(response.Data);

            bool parsed = DateTime.TryParse(response.Data, out DateTime serverTime);
            Assert.True(parsed, "Время сервера не удалось распарсить");

            serverTime = serverTime.ToUniversalTime();
            Assert.True(serverTime >= before && serverTime <= after,
                $"Время сервера {serverTime:O} выходит за пределы [{before:O}, {after:O}]");
        }

        // Тест 7 — расчёт смещения: offset = serverTime - clientTime
        [Fact]
        public async Task TimeSync_OffsetCalculation_IsCorrect()
        {
            TimeSpan clockOffset = TimeSpan.FromSeconds(7);

            Response response = await _service.GetServerTime();
            DateTime clientLocalTime = DateTime.UtcNow + clockOffset;
            DateTime serverTime = DateTime.Parse(response.Data!).ToUniversalTime();

            TimeSpan offset = serverTime - clientLocalTime;

            Assert.True(offset.TotalSeconds < -5 && offset.TotalSeconds > -9,
                $"Ожидалось смещение около -7 сек, получено {offset.TotalSeconds:F3} сек");
        }

        // Тест 8 — RTT измеряется корректно: больше нуля и не превышает разумный порог
        [Fact]
        public async Task TimeSync_RTT_IsPositiveAndReasonable()
        {
            DateTime requestTime = DateTime.UtcNow;

            await _service.GetServerTime();

            DateTime responseTime = DateTime.UtcNow;
            TimeSpan rtt = responseTime - requestTime;

            Assert.True(rtt.TotalMilliseconds > 0, "RTT должен быть больше нуля");
            Assert.True(rtt.TotalSeconds < 10, $"RTT слишком большой: {rtt.TotalSeconds:F3} сек");
        }

        // Тест 9 — скорректированное время близко к серверному с учётом RTT/2
        [Fact]
        public async Task TimeSync_CorrectedTime_CloseTo_ServerTime()
        {
            TimeSpan clockOffset = TimeSpan.FromSeconds(7);

            DateTime requestTime = DateTime.UtcNow;
            Response response = await _service.GetServerTime();
            DateTime responseTime = DateTime.UtcNow;

            DateTime serverTime = DateTime.Parse(response.Data!).ToUniversalTime();
            TimeSpan rtt = responseTime - requestTime;
            TimeSpan networkDelay = rtt / 2;

            DateTime clientLocalTime = requestTime + clockOffset;
            TimeSpan timeOffset = (serverTime + networkDelay) - clientLocalTime;
            DateTime correctedTime = clientLocalTime + timeOffset;

            DateTime actualServerNow = serverTime + networkDelay;

            double diffSeconds = Math.Abs((correctedTime - actualServerNow).TotalSeconds);
            Assert.True(diffSeconds < 0.1,
                $"Скорректированное время отличается от серверного на {diffSeconds:F3} сек");
        }

        // Тест 10 — транзакция COMMIT: обе операции сохранены в БД
        [Fact]
        public async Task EnrollStudent_WithTransaction_Commit_BothOperationsSaved()
        {
            int availableBefore = Available(_primary, 1);

            Response response = await _service.EnrollStudent(1, 1);

            int availableAfter = Available(_primary, 1);
            int enrolled = EnrollmentsOfStudent(_primary, 1);

            Assert.Equal("ok", response.Status);
            Assert.Equal(1, enrolled);
            Assert.Equal(availableBefore - 1, availableAfter);
        }

        // Тест 11 — транзакция: запись на несуществующий курс не оставляет изменений в БД
        [Fact]
        public async Task EnrollStudent_InvalidCourse_NoPartialChanges()
        {
            Response response = await _service.EnrollStudent(1, 999);

            int enrolled = EnrollmentsOfStudent(_primary, 1);

            Assert.NotEqual("ok", response.Status);
            Assert.Equal(0, enrolled);
        }

        // Тест 12 — составная операция: запись невозможна если курс заполнен
        [Fact]
        public async Task EnrollStudent_CourseIsFull_ReturnsFail()
        {
            await _service.EnrollStudent(1, 2);
            Response response = await _service.EnrollStudent(2, 2);

            Assert.NotEqual("ok", response.Status);
        }

        // Тест 13 — повторная запись на курс невозможна
        [Fact]
        public async Task EnrollStudent_AlreadyEnrolled_ReturnsFail()
        {
            await _service.EnrollStudent(1, 1);
            Response response = await _service.EnrollStudent(1, 1);

            Assert.NotEqual("ok", response.Status);
        }

        // Тест 14 — начальное состояние: Primary и Replica созданы и совпадают
        [Fact]
        public void InitialState_PrimaryEqualsReplica()
        {
            Assert.Equal(InstanceState.Available, _primary.CheckState());
            Assert.Equal(InstanceState.Available, _replica.CheckState());

            Assert.Empty(_replication.Compare());
            Assert.Equal(Available(_primary, 1), Available(_replica, 1));
        }

        // Тест 15 — после успешной репликации данные совпадают
        [Fact]
        public async Task Replication_AfterEnroll_ReplicaEqualsPrimary()
        {
            Response response = await _service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Экземпляр: Primary", response.Message);
            Assert.Equal(29, Available(_primary, 1));
            Assert.Equal(29, Available(_replica, 1));
            Assert.Equal(1, EnrolledCount(_replica, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 16 — три последовательных изменения, после каждого Primary = Replica
        [Fact]
        public async Task Replication_ThreeSequentialChanges_DataMatchesAfterEach()
        {
            Response r1 = await _service.EnrollStudent(1, 1);
            Assert.Equal("ok", r1.Status);
            Assert.Equal(29, Available(_replica, 1));
            Assert.Empty(_replication.Compare());

            Response r2 = await _service.EnrollStudent(2, 1);
            Assert.Equal("ok", r2.Status);
            Assert.Equal(28, Available(_replica, 1));
            Assert.Empty(_replication.Compare());

            Response r3 = await _service.CompleteCourse(1, 1);
            Assert.Equal("ok", r3.Status);
            Assert.Equal(29, Available(_replica, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 17 — корректная обработка изменения: сдача задания реплицируется
        [Fact]
        public async Task Replication_SubmitAssignment_ReplicatedToReplica()
        {
            await _service.EnrollStudent(2, 1);
            Response response = await _service.SubmitAssignment(1);

            using EducationDbContext db = _replica.CreateContext();
            Assignment assignment = db.Assignments.AsNoTracking().First(a => a.Id == 1);

            Assert.Equal("ok", response.Status);
            Assert.True(assignment.IsSubmitted);
            Assert.Empty(_replication.Compare());
        }

        // Тест 18 — определение недоступного Primary
        [Fact]
        public void Failover_PrimaryDown_IsDetectedAsUnavailable()
        {
            _primary.IsDown = true;

            Assert.Equal(InstanceState.Unavailable, _primary.CheckState());
            Assert.Equal(InstanceState.Available, _replica.CheckState());
        }

        // Тест 19 — выбор доступной Replica при отказе Primary
        [Fact]
        public void Failover_PrimaryDown_ReplicaIsSelected()
        {
            _primary.IsDown = true;

            DataInstance? selected = _replication.SelectInstance();

            Assert.Same(_replica, selected);
        }

        // Тест 20 — успешное выполнение запросов (Read и Update) после Failover
        [Fact]
        public async Task Failover_RequestsAfterPrimaryFailure_ProcessedByReplica()
        {
            _primary.IsDown = true;

            Response read = await _service.GetCourse(1);
            Response update = await _service.EnrollStudent(2, 1);

            Assert.Equal("ok", read.Status);
            Assert.Contains("Экземпляр: Replica", read.Message);
            Assert.Equal("ok", update.Status);
            Assert.Contains("Экземпляр: Replica", update.Message);

            Assert.Equal(29, Available(_replica, 1));
            Assert.Equal(30, Available(_primary, 1));
            Assert.True(_replication.PrimaryOutdated);
        }

        // Тест 21 — оба экземпляра недоступны: понятная ошибка, без падения
        [Fact]
        public async Task Failover_BothDown_ReturnsClearError()
        {
            _primary.IsDown = true;
            _replica.IsDown = true;

            Response response = await _service.EnrollStudent(1, 1);

            Assert.Equal("error", response.Status);
            Assert.Contains("Primary и Replica недоступны", response.Message);
        }

        // Тест 22 — Primary восстановлен, но устарел: автоматическая синхронизация и возврат на Primary
        [Fact]
        public async Task Recovery_PrimaryBackButOutdated_AutoSyncAndPrimaryUsed()
        {
            _primary.IsDown = true;
            await _service.EnrollStudent(1, 1);

            _primary.IsDown = false;
            Assert.NotEmpty(_replication.Compare());

            DataInstance? selected = _replication.SelectInstance();

            Assert.Same(_primary, selected);
            Assert.False(_replication.PrimaryOutdated);
            Assert.Equal(29, Available(_primary, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 23 — восстановление Primary и синхронизация Replica -> Primary
        [Fact]
        public async Task Recovery_SyncReplicaToPrimary_DataMatches()
        {
            _primary.IsDown = true;
            await _service.EnrollStudent(1, 1);
            await _service.EnrollStudent(2, 1);

            _primary.IsDown = false;
            Assert.NotEmpty(_replication.Compare());

            Response response = _replication.Recover();

            Assert.Equal("ok", response.Status);
            Assert.Empty(_replication.Compare());
            Assert.Equal(28, Available(_primary, 1));
            Assert.Equal(2, EnrolledCount(_primary, 1));
            Assert.False(_replication.PrimaryOutdated);
            Assert.Same(_primary, _replication.SelectInstance());
        }

        // Тест 24 — контрольная операция после восстановления: снова Primary -> Replication
        [Fact]
        public async Task Recovery_ControlOperationAfterSync_ReplicatedNormally()
        {
            _primary.IsDown = true;
            await _service.EnrollStudent(1, 1);
            _primary.IsDown = false;
            _replication.Recover();

            Response response = await _service.EnrollStudent(2, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Экземпляр: Primary", response.Message);
            Assert.Equal(28, Available(_primary, 1));
            Assert.Equal(28, Available(_replica, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 25 — Replica недоступна: работа через Primary, затем синхронизация Primary -> Replica
        [Fact]
        public async Task Recovery_ReplicaDown_ThenSyncPrimaryToReplica()
        {
            _replica.IsDown = true;

            Response response = await _service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Replica недоступна", response.Message);
            Assert.True(_replication.ReplicaOutdated);
            Assert.Equal(30, Available(_replica, 1));

            _replica.IsDown = false;
            Response recover = _replication.Recover();

            Assert.Equal("ok", recover.Status);
            Assert.Equal(29, Available(_replica, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 26 — синхронизация невозможна, пока Primary недоступен
        [Fact]
        public void Recovery_PrimaryStillDown_ReturnsError()
        {
            _primary.IsDown = true;

            Response response = _replication.Recover();

            Assert.Equal("error", response.Status);
            Assert.Contains("Синхронизация невозможна", response.Message);
        }

        // Тест 27 — нарушение согласованности без отказа: расхождение обнаруживается и устраняется
        [Fact]
        public void Recovery_ManualDivergence_DetectedAndFixed()
        {
            using (EducationDbContext db = _replica.CreateContext())
            {
                Course course = db.Courses.First(c => c.Id == 1);
                course.AvailableStudents = 7;
                db.SaveChanges();
            }

            Assert.NotEmpty(_replication.Compare());

            Response response = _replication.Recover();

            Assert.Equal("ok", response.Status);
            Assert.Equal(30, Available(_replica, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 28 — автоматическая синхронизация Primary при первом запросе после восстановления
        [Fact]
        public async Task AutoSync_ReadAfterPrimaryRecovery_ProcessedByPrimary()
        {
            _primary.IsDown = true;
            await _service.EnrollStudent(1, 1);

            _primary.IsDown = false;
            Response read = await _service.GetCourse(1);

            Assert.Equal("ok", read.Status);
            Assert.Contains("Экземпляр: Primary", read.Message);
            Assert.Equal(29, Available(_primary, 1));
            Assert.Empty(_replication.Compare());
        }

        // Тест 29 — автоматическая синхронизация Replica при следующем изменении после её восстановления
        [Fact]
        public async Task AutoSync_ChangeAfterReplicaRecovery_ReplicaSynchronized()
        {
            _replica.IsDown = true;
            await _service.EnrollStudent(1, 1);

            _replica.IsDown = false;
            Response response = await _service.EnrollStudent(2, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("автоматическая синхронизация", response.Message);
            Assert.False(_replication.ReplicaOutdated);
            Assert.Equal(28, Available(_replica, 1));
            Assert.Equal(2, EnrolledCount(_replica, 1));
            Assert.Empty(_replication.Compare());
        }

        private static (Broker Broker, Publisher Publisher) CreatePubSub()
        {
            Broker broker = new("TestBroker", FastRetry);
            return (broker, new Publisher(broker));
        }

        private static Event CreateEvent(string eventType) =>
            Event.Create(eventType, 1, new()
            {
                ["StudentId"] = "1",
                ["StudentName"] = "Иванов Иван",
                ["CourseId"] = "1",
                ["CourseTitle"] = "Основы программирования"
            });

        // Тест 30 — корректное создание события
        [Fact]
        public void PubSub_Event_CreatedCorrectly()
        {
            Event evt = CreateEvent(EventTypes.StudentEnrolled);

            Assert.NotEqual(Guid.Empty, evt.Id);
            Assert.Equal(EventTypes.StudentEnrolled, evt.EventType);
            Assert.Equal(1, evt.EntityId);
            Assert.NotEqual(default, evt.Timestamp);
            Assert.Null(evt.Validate());
        }

        // Тест 31 — корректная публикация события
        [Fact]
        public void PubSub_Publish_ReturnsSuccess()
        {
            var (broker, publisher) = CreatePubSub();
            new TestSubscriber("A", EventTypes.StudentEnrolled).ConnectTo(broker);

            PublishResult result = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));

            Assert.True(result.Success);
            Assert.Equal("TestBroker", result.BrokerName);
            Assert.Equal(1, result.SubscriberCount);
        }

        // Тест 32 — регистрация Subscriber
        [Fact]
        public void PubSub_Subscribe_RegistersSubscriber()
        {
            var (broker, _) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);

            subscriber.ConnectTo(broker);

            Assert.Contains(subscriber, broker.GetSubscribers(EventTypes.StudentEnrolled));
        }

        // Тест 33 — получение события подписанным Subscriber
        [Fact]
        public async Task PubSub_SubscribedSubscriber_ReceivesEvent()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Single(subscriber.Received);
        }

        // Тест 34 — отсутствие доставки неподписанному Subscriber
        [Fact]
        public async Task PubSub_UnsubscribedSubscriber_DoesNotReceiveEvent()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.CourseCompleted);
            subscriber.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Empty(subscriber.Received);
        }

        // Тест 35 — доставка одного события нескольким Subscriber
        [Fact]
        public async Task PubSub_OneEvent_DeliveredToSeveralSubscribers()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber a = new("A", EventTypes.StudentEnrolled);
            TestSubscriber b = new("B", EventTypes.StudentEnrolled);
            TestSubscriber c = new("C", EventTypes.StudentEnrolled);
            a.ConnectTo(broker);
            b.ConnectTo(broker);
            c.ConnectTo(broker);

            IReadOnlyList<DeliveryResult> results = await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Equal(3, results.Count(r => r.Success));
            Assert.Single(a.Received);
            Assert.Single(b.Received);
            Assert.Single(c.Received);
        }

        // Тест 36 — обработка нескольких типов событий
        [Fact]
        public async Task PubSub_SeveralEventTypes_RoutedCorrectly()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber logger = new("Logger", EventTypes.StudentEnrolled, EventTypes.AssignmentSubmitted, EventTypes.CourseCompleted);
            TestSubscriber course = new("CourseService", EventTypes.StudentEnrolled, EventTypes.CourseCompleted);
            logger.ConnectTo(broker);
            course.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.AssignmentSubmitted)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.CourseCompleted)).Delivery;

            Assert.Equal(3, logger.Received.Count);
            Assert.Equal(2, course.Received.Count);
            Assert.DoesNotContain(course.Received, e => e.EventType == EventTypes.AssignmentSubmitted);
        }

        // Тест 37 — корректная обработка ошибки Subscriber
        [Fact]
        public async Task PubSub_SubscriberError_HandledCorrectly()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            subscriber.SetState(SubscriberState.Failing);

            PublishResult result = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));
            DeliveryResult delivery = Assert.Single(await result.Delivery);

            Assert.True(result.Success);
            Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        }

        // Тест 38 — продолжение работы Broker при отказе Subscriber
        [Fact]
        public async Task PubSub_Broker_ContinuesWork_WhenSubscriberUnavailable()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber working = new("Working", EventTypes.StudentEnrolled);
            TestSubscriber down = new("Down", EventTypes.StudentEnrolled);
            working.ConnectTo(broker);
            down.ConnectTo(broker);
            down.SetState(SubscriberState.Unavailable);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Equal(2, working.Received.Count);
            Assert.Empty(down.Received);
        }

        // Тест 39 — добавление нового Subscriber
        [Fact]
        public void PubSub_NewSubscriber_IsAdded()
        {
            var (broker, _) = CreatePubSub();
            new TestSubscriber("A", EventTypes.CourseCompleted).ConnectTo(broker);

            new CertificateService().ConnectTo(broker);

            Assert.Equal(2, broker.GetSubscribers(EventTypes.CourseCompleted).Count);
            Assert.NotNull(broker.FindSubscriber("CertificateService"));
        }

        // Тест 40 — получение события новым Subscriber
        [Fact]
        public async Task PubSub_NewSubscriber_ReceivesEvent()
        {
            var (broker, publisher) = CreatePubSub();
            CertificateService certificate = new();
            certificate.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.CourseCompleted)).Delivery;

            Assert.Single(certificate.Certificates);
        }

        // Тест 41 — Publisher не изменяется при добавлении Subscriber
        [Fact]
        public async Task PubSub_NewSubscriber_PublisherNotChanged()
        {
            var (broker, publisher) = CreatePubSub();
            new TestSubscriber("A", EventTypes.CourseCompleted).ConnectTo(broker);

            PublishResult before = publisher.Publish(CreateEvent(EventTypes.CourseCompleted));
            await before.Delivery;

            TestSubscriber added = new("Added", EventTypes.CourseCompleted);
            added.ConnectTo(broker);

            PublishResult after = publisher.Publish(CreateEvent(EventTypes.CourseCompleted));
            await after.Delivery;

            Assert.Equal(1, before.SubscriberCount);
            Assert.Equal(2, after.SubscriberCount);
            Assert.Single(added.Received);
        }

        // Тест 42 — несколько Broker: переключение на Broker-2 и ошибка при отказе всех
        [Fact]
        public async Task PubSub_SeveralBrokers_FailoverToSecondBroker()
        {
            Broker broker1 = new("Broker-1", FastRetry);
            Broker broker2 = new("Broker-2", FastRetry);
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker1);
            subscriber.ConnectTo(broker2);
            Publisher publisher = new(broker1, broker2);

            broker1.SetAvailable(false);
            PublishResult result = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));
            await result.Delivery;

            Assert.True(result.Success);
            Assert.Equal("Broker-2", result.BrokerName);
            Assert.Single(subscriber.Received);

            broker2.SetAvailable(false);
            PublishResult failed = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));

            Assert.False(failed.Success);
            Assert.Contains("все Broker недоступны", failed.Message);
            Assert.Single(publisher.Errors);
        }

        // Тест 43 — повторная доставка: два сбоя, успех с третьей попытки
        [Fact]
        public async Task PubSub_Retry_SucceedsOnThirdAttempt()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            subscriber.SetState(SubscriberState.Unstable);

            DeliveryResult delivery = Assert.Single(await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery);

            Assert.True(delivery.Success);
            Assert.Equal(3, delivery.Attempts);
            Assert.Single(subscriber.Received);
            Assert.Equal(0, broker.QueueCount);
        }

        // Тест 44 — очередь: событие не теряется и доставляется после восстановления Subscriber
        [Fact]
        public async Task PubSub_Queue_EventDeliveredAfterSubscriberRecovery()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            subscriber.SetState(SubscriberState.Unavailable);

            DeliveryResult delivery = Assert.Single(await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery);

            Assert.Equal(DeliveryStatus.Unavailable, delivery.Status);
            Assert.Equal(1, broker.QueueCount);
            Assert.Empty(subscriber.Received);

            subscriber.SetState(SubscriberState.Available);
            int delivered = await broker.ProcessQueueAsync();

            Assert.Equal(1, delivered);
            Assert.Equal(0, broker.QueueCount);
            Assert.Single(subscriber.Received);
        }

        // Тест 45 — AnalyticsService собирает статистику по событиям
        [Fact]
        public async Task PubSub_Analytics_CollectsStatistics()
        {
            var (broker, publisher) = CreatePubSub();
            AnalyticsService analytics = new();
            analytics.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.AssignmentSubmitted)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.CourseCompleted)).Delivery;

            string report = analytics.GetReport();

            Assert.Contains("всего событий 3", report);
            Assert.Contains("Основы программирования", report);
            Assert.Contains("Уникальных студентов: 1", report);
        }

        // Тест 46 — репликация очереди: копия у Broker-2, доставка при отказе Broker-1, без дубля после его возврата
        [Fact]
        public async Task PubSub_QueueReplication_TakeoverWhenOwnerDown()
        {
            Broker broker1 = new("Broker-1", FastRetry);
            Broker broker2 = new("Broker-2", FastRetry);
            broker1.AddPeer(broker2);
            broker2.AddPeer(broker1);

            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker1);
            subscriber.ConnectTo(broker2);
            subscriber.SetState(SubscriberState.Unavailable);

            Publisher publisher = new(broker1, broker2);
            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Equal(1, broker1.QueueCount);
            Assert.Equal(1, broker2.QueueCount);

            broker1.SetAvailable(false);
            subscriber.SetState(SubscriberState.Available);
            int delivered = await broker2.ProcessQueueAsync();

            Assert.Equal(1, delivered);
            Assert.Equal(0, broker2.QueueCount);
            Assert.Single(subscriber.Received);

            broker1.SetAvailable(true);
            await broker1.ProcessQueueAsync();

            Assert.Equal(0, broker1.QueueCount);
            Assert.Single(subscriber.Received);
        }

        // Тест 47 — защита от дублей: повторно доставленное событие не обрабатывается второй раз
        [Fact]
        public async Task PubSub_DuplicateEvent_ProcessedOnlyOnce()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            Event evt = CreateEvent(EventTypes.StudentEnrolled);

            await publisher.Publish(evt).Delivery;
            DeliveryResult second = Assert.Single(await publisher.Publish(evt).Delivery);

            Assert.True(second.Success);
            Assert.Contains("уже обработано", second.Message);
            Assert.Single(subscriber.Received);
        }
    }

    public class TestDataInstance : DataInstance
    {
        public bool IsDown { get; set; }

        public TestDataInstance(string name, DbContextOptions<EducationDbContext> options)
            : base(name, options) { }

        protected override bool CanConnect(EducationDbContext context)
        {
            return !IsDown && base.CanConnect(context);
        }
    }

    public class TestSubscriber : SubscriberBase
    {
        private readonly ConcurrentQueue<Event> _received = new();

        public TestSubscriber(string name, params string[] eventTypes)
            : base(name, TimeSpan.Zero, eventTypes)
        {
        }

        public IReadOnlyCollection<Event> Received => _received.ToArray();

        protected override Task<string> ProcessAsync(Event evt)
        {
            _received.Enqueue(evt);
            return Task.FromResult("ok");
        }
    }
}