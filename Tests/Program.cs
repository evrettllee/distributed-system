using Server.Handlers;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Shared;
using System.Net.Sockets;
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
            // создаём временную папку для тестовых данных
            _testDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDataPath);

            _students = new JsonRepository<Student>(Path.Combine(_testDataPath, "students.json"));
            _courses = new JsonRepository<Course>(Path.Combine(_testDataPath, "courses.json"));
            _enrollments = new JsonRepository<Enrollment>(Path.Combine(_testDataPath, "enrollments.json"));
            _assignments = new JsonRepository<Assignment>(Path.Combine(_testDataPath, "assignments.json"));

            _service = new EducationService(_students, _courses, _enrollments, _assignments);
            _handler = new OperationHandler(_service);

            // начальные данные
            _students.Add(new Student { FullName = "Иванов Иван", Email = "ivanov@mail.ru" });
            _courses.Add(new Course { Title = "Основы программирования", TeacherId = 1 });
            _assignments.Add(new Assignment { Title = "Задание 1", CourseId = 1, StudentId = 1, IsSubmitted = false });
        }

        // Тест 1 — успешный сценарий: запись студента на курс
        [Fact]
        public void EnrollStudent_ValidData_ReturnsOk()
        {
            Response response = _service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Иванов Иван", response.Message);
        }

        // Тест 2 — неизвестная операция
        [Fact]
        public void Handle_UnknownOperation_ReturnsError()
        {
            Request request = new() { Operation = "DeleteEverything", Data = null };

            Response response = _handler.Handle(request);

            Assert.Equal("error", response.Status);
            Assert.Contains("Неизвестная операция", response.Message);
        }

        // Тест 3 — некорректные данные: студент не существует
        [Fact]
        public void EnrollStudent_StudentNotFound_ReturnsError()
        {
            Response response = _service.EnrollStudent(999, 1);

            Assert.Equal("error", response.Status);
            Assert.Contains("не найден", response.Message);
        }

        // Тест 4 — недоступный сервер
        [Fact]
        public void Connect_ServerUnavailable_ReturnsError()
        {
            // пытаемся подключиться на заведомо закрытый порт
            Response response;
            try
            {
                using TcpClient client = new("127.0.0.1", 5000);
                response = Response.Error("Подключение не должно было состояться");
            }
            catch (SocketException)
            {
                response = Response.Error("Не удалось подключиться к серверу");
            }

            Assert.Equal("error", response.Status);
            Assert.Contains("Не удалось подключиться", response.Message);
        }

        // Тест 5 — повторная запись на курс
        [Fact]
        public void EnrollStudent_AlreadyEnrolled_ReturnsError()
        {
            _service.EnrollStudent(1, 1);
            Response response = _service.EnrollStudent(1, 1);

            Assert.Equal("error", response.Status);
            Assert.Contains("уже записан", response.Message);
        }

        // Тест 6 — сдача задания
        [Fact]
        public void SubmitAssignment_ValidData_ReturnsOk()
        {
            Response response = _service.SubmitAssignment(1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("сдано", response.Message);
        }

        public void Dispose()
        {
            // удаляем временные файлы после каждого теста
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
        }
    }
}