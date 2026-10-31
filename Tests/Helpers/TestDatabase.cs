using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Data;
using Server.Models;
using Server.Replication;

namespace Tests.Helpers
{
    public static class TestDatabase
    {
        public static TestDataInstance CreateSeeded(string name)
        {
            TestDataInstance instance = new(name, CreateOptions());
            Seed(instance);
            return instance;
        }

        public static DbContextOptions<EducationDbContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<EducationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        }

        public static void Seed(DataInstance instance)
        {
            using EducationDbContext db = instance.CreateContext();

            db.Students.AddRange(
                new Student { Id = 1, FullName = "Иванов Иван", Email = "ivanov@mail.ru" },
                new Student { Id = 2, FullName = "Петров Пётр", Email = "petrov@mail.ru" }
            );
            db.Teachers.Add(new Teacher { Id = 1, FullName = "Смирнов", Subject = "Математика" });
            db.Courses.AddRange(
                new Course { Id = 1, Title = "Основы программирования", TeacherId = 1, MaxStudents = 30, AvailableStudents = 30, Status = CourseStatus.Active },
                new Course { Id = 2, Title = "Маленький курс", TeacherId = 1, MaxStudents = 1, AvailableStudents = 1 }
            );
            db.Assignments.Add(new Assignment { Id = 1, Title = "Лабораторная 1", CourseId = 1, StudentId = 2, IsSubmitted = false });
            db.SaveChanges();
        }
    }
}
