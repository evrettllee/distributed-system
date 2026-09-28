using Shared;
using Server.Models;
using Server.Data;
using System.Text.Encodings.Web;
using System.Text.Json;
using Server.Common;

namespace Server.Services
{
    public class EducationService : IEducationService
    {
        private readonly EducationDbContext _db;

        private static readonly object _enrollLock = new();
        private static readonly object _assignmentLock = new();
        private static readonly object _completeLock = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public EducationService(EducationDbContext db)
        {
            _db = db;
        }

        private static string Label => $"Client {ClientContext.ClientId}";
        private static string Now => DateTime.Now.ToString("HH:mm:ss.fff");

        public async Task<Response> EnrollStudent(int studentId, int courseId)
        {
            Student? student = _db.Students.FirstOrDefault(s => s.Id == studentId);
            if (student == null)
                return Response.Error($"Студент с id {studentId} не найден");

            Course? course = _db.Courses.FirstOrDefault(c => c.Id == courseId);
            if (course == null)
                return Response.Error($"Курс с id {courseId} не найден");

            Console.WriteLine($"[{Now}] {Label} -> START EnrollStudent");

            await Task.Delay(3_000);

            lock (_enrollLock)
            {
                Console.WriteLine($"[{Now}] {Label} -> LOCK");

                bool alreadyEnrolled = _db.Enrollments
                    .Any(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

                if (alreadyEnrolled)
                {
                    Console.WriteLine($"[{Now}] {Label} -> уже записан на курс");
                    Console.WriteLine($"[{Now}] {Label} -> FAIL");
                    Console.WriteLine($"[{Now}] {Label} -> UNLOCK");
                    return Response.Error("Студент уже записан на этот курс");
                }

                int currentStudents = _db.Enrollments
                    .Count(e => e.CourseId == courseId && !e.IsCompleted);

                Console.WriteLine($"[{Now}] {Label} -> Available = {course.MaxStudents - currentStudents}");

                if (currentStudents >= course.MaxStudents)
                {
                    Console.WriteLine($"[{Now}] {Label} -> FAIL");
                    Console.WriteLine($"[{Now}] {Label} -> UNLOCK");
                    return Response.Error($"Курс заполнен: максимум {course.MaxStudents} студентов");
                }

                using var transaction = _db.Database.BeginTransaction();
                try
                {
                    Console.WriteLine($"[{Now}] {Label} -> BEGIN TRANSACTION");

                    Console.WriteLine($"[{Now}] {Label} -> Операция 1: запись студента на курс");
                    _db.Enrollments.Add(new Enrollment
                    {
                        StudentId = studentId,
                        CourseId = courseId,
                        IsCompleted = false
                    });
                    _db.SaveChanges();
                    Console.WriteLine($"[{Now}] {Label} -> Операция 1: SUCCESS");

                    // Console.WriteLine($"[{Now}] {Label} -> ОШИБКА внутри транзакции (симуляция)");
                    // throw new Exception("Искусственная ошибка для демонстрации ROLLBACK");

                    Console.WriteLine($"[{Now}] {Label} -> Операция 2: уменьшение числа мест");
                    course.MaxStudents -= 1;
                    _db.Courses.Update(course);
                    _db.SaveChanges();
                    Console.WriteLine($"[{Now}] {Label} -> Операция 2: SUCCESS");

                    transaction.Commit();
                    Console.WriteLine($"[{Now}] {Label} -> COMMIT");
                    Console.WriteLine($"[{Now}] {Label} -> SUCCESS");
                    Console.WriteLine($"[{Now}] {Label} -> UNLOCK");

                    return Response.Ok(message: $"Студент '{student.FullName}' записан на курс '{course.Title}' ({currentStudents + 1}/{course.MaxStudents})");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[{Now}] {Label} -> ROLLBACK: {ex.Message}");
                    Console.WriteLine($"[{Now}] {Label} -> FAIL");
                    Console.WriteLine($"[{Now}] {Label} -> UNLOCK");
                    return Response.Error($"Операция отменена: {ex.Message}");
                }
            }
        }

        public async Task<Response> GetCourse(int courseId)
        {
            Course? course = _db.Courses.FirstOrDefault(c => c.Id == courseId);
            if (course == null)
                return Response.Error($"Курс с id {courseId} не найден");

            return Response.Ok(JsonSerializer.Serialize(course, _jsonOptions));
        }

        public async Task<Response> SubmitAssignment(int assignmentId)
        {
            lock (_assignmentLock)
            {
                Assignment? assignment = _db.Assignments.FirstOrDefault(a => a.Id == assignmentId);
                if (assignment == null)
                    return Response.Error($"Задание с id {assignmentId} не найдено");

                if (assignment.IsSubmitted)
                    return Response.Error("Задание уже сдано");

                bool isEnrolled = _db.Enrollments
                    .Any(e => e.StudentId == assignment.StudentId && e.CourseId == assignment.CourseId && !e.IsCompleted);

                if (!isEnrolled)
                    return Response.Error("Студент не записан на курс этого задания");

                using var transaction = _db.Database.BeginTransaction();
                try
                {
                    Console.WriteLine($"[{Now}] {Label} -> BEGIN TRANSACTION");

                    assignment.IsSubmitted = true;
                    _db.Assignments.Update(assignment);
                    _db.SaveChanges();

                    transaction.Commit();
                    Console.WriteLine($"[{Now}] {Label} -> COMMIT");

                    return Response.Ok(message: $"Задание '{assignment.Title}' сдано");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[{Now}] {Label} -> ROLLBACK: {ex.Message}");
                    return Response.Error($"Операция отменена: {ex.Message}");
                }
            }
        }

        public async Task<Response> CompleteCourse(int studentId, int courseId)
        {
            Student? student = _db.Students.FirstOrDefault(s => s.Id == studentId);
            if (student == null)
                return Response.Error($"Студент с id {studentId} не найден");

            Course? course = _db.Courses.FirstOrDefault(c => c.Id == courseId);
            if (course == null)
                return Response.Error($"Курс с id {courseId} не найден");

            lock (_completeLock)
            {
                Enrollment? enrollment = _db.Enrollments
                    .FirstOrDefault(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

                if (enrollment == null)
                    return Response.Error("Студент не записан на этот курс или курс уже завершён");

                List<Assignment> courseAssignments = _db.Assignments
                    .Where(a => a.StudentId == studentId && a.CourseId == courseId)
                    .ToList();

                bool hasUnsubmitted = courseAssignments.Any(a => !a.IsSubmitted);
                if (hasUnsubmitted)
                {
                    int count = courseAssignments.Count(a => !a.IsSubmitted);
                    return Response.Error($"Нельзя завершить курс: не сдано заданий — {count}");
                }

                using var transaction = _db.Database.BeginTransaction();
                try
                {
                    Console.WriteLine($"[{Now}] {Label} -> BEGIN TRANSACTION");

                    Console.WriteLine($"[{Now}] {Label} -> Операция 1: завершение записи студента");
                    enrollment.IsCompleted = true;
                    _db.Enrollments.Update(enrollment);
                    _db.SaveChanges();
                    Console.WriteLine($"[{Now}] {Label} -> Операция 1: SUCCESS");

                    Console.WriteLine($"[{Now}] {Label} -> Операция 2: возврат места на курсе");
                    course.MaxStudents += 1;
                    _db.Courses.Update(course);
                    _db.SaveChanges();
                    Console.WriteLine($"[{Now}] {Label} -> Операция 2: SUCCESS");

                    transaction.Commit();
                    Console.WriteLine($"[{Now}] {Label} -> COMMIT");

                    return Response.Ok(message: $"Студент '{student.FullName}' завершил курс '{course.Title}'");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine($"[{Now}] {Label} -> ROLLBACK: {ex.Message}");
                    return Response.Error($"Операция отменена: {ex.Message}");
                }
            }
        }

        public async Task<Response> GetServerTime()
        {
            await Task.Delay(4_000);
            return Response.Ok(DateTime.Now.ToString("O"));
        }
    }
}