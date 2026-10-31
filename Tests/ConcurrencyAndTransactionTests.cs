using Server.Data;
using Server.Models;
using Server.Replication;
using Server.Services;
using Shared;
using Tests.Helpers;
using Xunit;

namespace Tests
{
    public class ConcurrencyAndTransactionTests : EducationTestBase
    {
        // Тест 1 — один клиент: обычная обработка запроса
        [Fact]
        public async Task OneClient_EnrollStudent_ReturnsOk()
        {
            Response response = await Service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Иванов Иван", response.Message);
        }

        // Тест 2 — два клиента: оба запроса обрабатываются успешно
        [Fact]
        public async Task TwoClients_BothEnrolled_ReturnsOk()
        {
            Task<Response> task1 = Service.EnrollStudent(1, 1);
            Task<Response> task2 = Service.EnrollStudent(2, 1);

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
                await Service.EnrollStudent(1, 1);
                end1 = DateTime.Now;
            });

            await Task.Delay(500);

            Task task2 = Task.Run(async () =>
            {
                start2 = DateTime.Now;
                await Service.EnrollStudent(2, 1);
            });

            await Task.WhenAll(task1, task2);

            Assert.True(start2 < end1, $"Client 2 начал в {start2:HH:mm:ss.fff}, Client 1 завершил в {end1:HH:mm:ss.fff}");
        }

        // Тест 4 — с синхронизацией: Race Condition устранён, курс на 1 место — только один успешен
        [Fact]
        public async Task TwoClients_WithLock_RaceCondition_IsFixed()
        {
            Task<Response> task1 = Service.EnrollStudent(1, 2);
            Task<Response> task2 = Service.EnrollStudent(2, 2);

            Response[] responses = await Task.WhenAll(task1, task2);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = EnrolledCount(Primary, 2);

            Assert.Equal(1, successCount);
            Assert.Equal(1, enrolled);
        }

        // Тест 5 — синхронизация: 10 параллельных клиентов, курс на 3 места — ровно 3 успешных
        [Fact]
        public async Task ManyClients_WithLock_ExactlyMaxStudentsSucceed()
        {
            int maxStudents = 3;

            foreach (DataInstance instance in new DataInstance[] { Primary, Replica })
            {
                using EducationDbContext db = instance.CreateContext();
                db.Courses.Add(new Course { Id = 3, Title = "Ограниченный курс", TeacherId = 1, MaxStudents = maxStudents, AvailableStudents = maxStudents });
                for (int i = 3; i <= 12; i++)
                    db.Students.Add(new Student { Id = i, FullName = $"Студент {i}", Email = $"student{i}@mail.ru" });
                db.SaveChanges();
            }

            List<Task<Response>> tasks = Enumerable.Range(1, 10)
                .Select(i => Service.EnrollStudent(i, 3))
                .ToList();

            Response[] responses = await Task.WhenAll(tasks);

            int successCount = responses.Count(r => r.Status == "ok");
            int enrolled = EnrolledCount(Primary, 3);

            Assert.Equal(maxStudents, successCount);
            Assert.Equal(maxStudents, enrolled);
        }

        // Тест 10 — транзакция COMMIT: обе операции сохранены в БД
        [Fact]
        public async Task EnrollStudent_WithTransaction_Commit_BothOperationsSaved()
        {
            int availableBefore = Available(Primary, 1);

            Response response = await Service.EnrollStudent(1, 1);

            int availableAfter = Available(Primary, 1);
            int enrolled = EnrollmentsOfStudent(Primary, 1);

            Assert.Equal("ok", response.Status);
            Assert.Equal(1, enrolled);
            Assert.Equal(availableBefore - 1, availableAfter);
        }

        // Тест 11 — транзакция: запись на несуществующий курс не оставляет изменений в БД
        [Fact]
        public async Task EnrollStudent_InvalidCourse_NoPartialChanges()
        {
            Response response = await Service.EnrollStudent(1, 999);

            int enrolled = EnrollmentsOfStudent(Primary, 1);

            Assert.NotEqual("ok", response.Status);
            Assert.Equal(0, enrolled);
        }

        // Тест 12 — составная операция: запись невозможна если курс заполнен
        [Fact]
        public async Task EnrollStudent_CourseIsFull_ReturnsFail()
        {
            await Service.EnrollStudent(1, 2);
            Response response = await Service.EnrollStudent(2, 2);

            Assert.NotEqual("ok", response.Status);
        }

        // Тест 13 — повторная запись на курс невозможна
        [Fact]
        public async Task EnrollStudent_AlreadyEnrolled_ReturnsFail()
        {
            await Service.EnrollStudent(1, 1);
            Response response = await Service.EnrollStudent(1, 1);

            Assert.NotEqual("ok", response.Status);
        }

        // Тест 53 — сбой публикации после COMMIT: данные сохранены, ответ Ok с предупреждением
        [Fact]
        public async Task EnrollStudent_PublishFailsAfterCommit_ReturnsOkWithWarning()
        {
            EducationService service = new(Replication, new FailingPublisher());

            Response response = await service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("ВНИМАНИЕ: данные сохранены в Primary, но публикация не выполнена: Publisher недоступен", response.Message);
            Assert.Equal(29, Available(Primary, 1));
            Assert.Equal(29, Available(Replica, 1));
        }
    }
}
