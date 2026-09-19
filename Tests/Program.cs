using Server.Handlers;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Shared;
using Xunit;

namespace Tests
{
    public class EducationServiceTests : IDisposable
    {
        private readonly string _testDataPath;
        private readonly IRepository<Student> _students;
        private readonly IRepository<Course> _courses;
        private readonly IRepository<Enrollment> _enrollments;
        private readonly IRepository<Assignment> _assignments;
        private readonly EducationService _service;
        private readonly OperationHandler _handler;

        public EducationServiceTests()
        {
            _testDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDataPath);

            _students = new JsonRepository<Student>(Path.Combine(_testDataPath, "students.json"));
            _courses = new JsonRepository<Course>(Path.Combine(_testDataPath, "courses.json"));
            _enrollments = new JsonRepository<Enrollment>(Path.Combine(_testDataPath, "enrollments.json"));
            _assignments = new JsonRepository<Assignment>(Path.Combine(_testDataPath, "assignments.json"));

            _service = new EducationService(_students, _courses, _enrollments, _assignments);
            _handler = new OperationHandler(_service);

            _students.Add(new Student { FullName = "Иванов Иван", Email = "ivanov@mail.ru" });
            _students.Add(new Student { FullName = "Петров Пётр", Email = "petrov@mail.ru" });
            _courses.Add(new Course { Title = "Основы программирования", TeacherId = 1, MaxStudents = 30 });
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
            _courses.Add(new Course { Title = "Маленький курс", TeacherId = 1, MaxStudents = 1 });

            Task<Response> task1 = _service.EnrollStudent(1, 2);
            Task<Response> task2 = _service.EnrollStudent(2, 2);

            Response[] responses = await Task.WhenAll(task1, task2);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = _enrollments.GetAll().Count(e => e.CourseId == 2 && !e.IsCompleted);

            Assert.Equal(1, successCount);
            Assert.Equal(1, enrolled);
        }

        // Тест 5 — синхронизация: 10 параллельных клиентов, курс на 3 места — ровно 3 успешных
        [Fact]
        public async Task ManyClients_WithLock_ExactlyMaxStudentsSucceed()
        {
            int maxStudents = 3;
            _courses.Add(new Course { Title = "Ограниченный курс", TeacherId = 1, MaxStudents = maxStudents });

            for (int i = 3; i <= 12; i++)
                _students.Add(new Student { FullName = $"Студент {i}", Email = $"student{i}@mail.ru" });

            List<Task<Response>> tasks = Enumerable.Range(1, 10)
                .Select(i => _service.EnrollStudent(i, 2))
                .ToList();

            Response[] responses = await Task.WhenAll(tasks);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = _enrollments.GetAll().Count(e => e.CourseId == 2 && !e.IsCompleted);

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

            double diffSeconds = Math.Abs((correctedTime - serverTime).TotalSeconds);
            Assert.True(diffSeconds < 2.0,
                $"Скорректированное время отличается от серверного на {diffSeconds:F3} сек");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDataPath))
                Directory.Delete(_testDataPath, recursive: true);
        }
    }
}