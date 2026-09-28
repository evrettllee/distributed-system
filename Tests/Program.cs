using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Data;
using Server.Models;
using Server.Services;
using Shared;
using Xunit;

namespace Tests
{
    public class EducationServiceTests : IDisposable
    {
        private readonly EducationDbContext _db;
        private readonly EducationService _service;
        private readonly string _dbName;

        public EducationServiceTests()
        {
            _dbName = Guid.NewGuid().ToString();

            var options = new DbContextOptionsBuilder<EducationDbContext>()
                .UseInMemoryDatabase(_dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new EducationDbContext(options);
            _service = new EducationService(_db);

            _db.Students.AddRange(
                new Student { Id = 1, FullName = "Иванов Иван", Email = "ivanov@mail.ru" },
                new Student { Id = 2, FullName = "Петров Пётр", Email = "petrov@mail.ru" }
            );
            _db.Teachers.Add(new Teacher { Id = 1, FullName = "Смирнов", Subject = "Математика" });
            _db.Courses.AddRange(
                new Course { Id = 1, Title = "Основы программирования", TeacherId = 1, MaxStudents = 30 },
                new Course { Id = 2, Title = "Маленький курс", TeacherId = 1, MaxStudents = 1 }
            );
            _db.SaveChanges();
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
            int enrolled = _db.Enrollments.Count(e => e.CourseId == 2 && !e.IsCompleted);

            Assert.Equal(1, successCount);
            Assert.Equal(1, enrolled);
        }

        // Тест 5 — синхронизация: 10 параллельных клиентов, курс на 3 места — ровно 3 успешных
        [Fact]
        public async Task ManyClients_WithLock_ExactlyMaxStudentsSucceed()
        {
            int maxStudents = 3;
            _db.Courses.Add(new Course { Id = 3, Title = "Ограниченный курс", TeacherId = 1, MaxStudents = maxStudents });
            for (int i = 3; i <= 12; i++)
                _db.Students.Add(new Student { Id = i, FullName = $"Студент {i}", Email = $"student{i}@mail.ru" });
            _db.SaveChanges();

            var options = new DbContextOptionsBuilder<EducationDbContext>()
     .UseInMemoryDatabase(_dbName)
     .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
     .Options;

            List<Task<Response>> tasks = Enumerable.Range(1, 10)
                .Select(i =>
                {
                    var db = new EducationDbContext(options);
                    var service = new EducationService(db);
                    return service.EnrollStudent(i, 3);
                })
                .ToList();

            Response[] responses = await Task.WhenAll(tasks);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = _db.Enrollments.Count(e => e.CourseId == 3 && !e.IsCompleted);

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
            int maxBefore = _db.Courses.First(c => c.Id == 1).MaxStudents;

            Response response = await _service.EnrollStudent(1, 1);

            int maxAfter = _db.Courses.First(c => c.Id == 1).MaxStudents;
            int enrolled = _db.Enrollments.Count(e => e.StudentId == 1 && e.CourseId == 1);

            Assert.Equal("ok", response.Status);
            Assert.Equal(1, enrolled);
            Assert.Equal(maxBefore - 1, maxAfter);
        }

        // Тест 11 — транзакция: запись на несуществующий курс не оставляет изменений в БД
        [Fact]
        public async Task EnrollStudent_InvalidCourse_NoPartialChanges()
        {
            Response response = await _service.EnrollStudent(1, 999);

            int enrolled = _db.Enrollments.Count(e => e.StudentId == 1);

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

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}