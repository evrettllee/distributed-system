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

            // небольшая пауза чтобы Client 1 успел начать обработку
            await Task.Delay(500);

            Task task2 = Task.Run(async () =>
            {
                start2 = DateTime.Now;
                await _service.EnrollStudent(2, 1);
            });

            await Task.WhenAll(task1, task2);

            // Client 2 начал до завершения Client 1 — значит обработка шла параллельно
            Assert.True(start2 < end1, $"Client 2 начал в {start2:HH:mm:ss.fff}, Client 1 завершил в {end1:HH:mm:ss.fff}");
        }

        // Тест 4 — Race Condition: оба клиента записались на курс с MaxStudents = 1
        [Fact]
        public async Task TwoClients_RaceCondition_BothSucceed()
        {
            // курс только на 1 место
            _courses.Add(new Course { Title = "Маленький курс", TeacherId = 1, MaxStudents = 1 });

            Task<Response> task1 = _service.EnrollStudent(1, 2);
            Task<Response> task2 = _service.EnrollStudent(2, 2);

            Response[] responses = await Task.WhenAll(task1, task2);

            // оба получили ok — Race Condition сработал
            Assert.True(responses.All(r => r.Status == "ok"),
                $"Client 1: {responses[0].Status} | Client 2: {responses[1].Status}");

            // в итоге записалось больше студентов, чем позволяет курс
            int enrolled = _enrollments.GetAll().Count(e => e.CourseId == 2 && !e.IsCompleted);
            Assert.True(enrolled > 1, $"Записано студентов: {enrolled}, а должно быть не более 1");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDataPath))
                Directory.Delete(_testDataPath, recursive: true);
        }
    }
}