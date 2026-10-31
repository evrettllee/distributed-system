using Shared;
using Server.Models;
using Server.Data;
using Server.Replication;
using System.Text.Encodings.Web;
using System.Text.Json;
using Server.Common;
using Server.Events;
using Server.PubSub;

namespace Server.Services
{
    public class EducationService : IEducationService
    {
        private readonly ReplicationManager _replication;
        private readonly IPublisher _publisher;

        private static readonly Lock _enrollLock = new();
        private static readonly Lock _assignmentLock = new();
        private static readonly Lock _completeLock = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public EducationService(ReplicationManager replication, IPublisher publisher)
        {
            _replication = replication;
            _publisher = publisher;
        }

        private static Response Unavailable() =>
            Response.Error("Сервис временно недоступен: Primary и Replica недоступны");

        private static Response DatabaseError(DataInstance instance, Exception ex)
        {
            Logger.Log($" {instance.Name} -> FAIL ({ex.Message})");
            return Response.Error($"Ошибка работы с экземпляром {instance.Name}: {ex.Message}");
        }

        private string PublishEvent(string eventType, int entityId, Dictionary<string, string> data)
        {
            Event evt = Event.Create(eventType, entityId, data);
            Logger.Log("Server", $"EVENT {evt}");

            PublishResult result = _publisher.Publish(evt);
            return $"{eventType} -> {result.Message}";
        }

        public async Task<Response> EnrollStudent(int studentId, int courseId)
        {
            Logger.Log($" START EnrollStudent");

            DataInstance? instance = _replication.SelectInstance();
            if (instance == null)
                return Unavailable();

            try
            {
                using EducationDbContext db = instance.CreateContext();

                Student? student = db.Students.FirstOrDefault(s => s.Id == studentId);
                if (student == null)
                    return Response.Error($"Студент с id {studentId} не найден");

                Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                if (course == null)
                    return Response.Error($"Курс с id {courseId} не найден");

                await Task.Delay(3_000);

                lock (_enrollLock)
                {
                    Logger.Log($" LOCK");

                    db.Entry(course).Reload();

                    bool alreadyEnrolled = db.Enrollments
                        .Any(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

                    if (alreadyEnrolled)
                    {
                        Logger.Log($" уже записан на курс");
                        Logger.Log($" FAIL");
                        Logger.Log($" UNLOCK");
                        return Response.Error("Студент уже записан на этот курс");
                    }

                    Logger.Log($" {instance.Name} -> Available = {course.AvailableStudents}");

                    if (course.AvailableStudents <= 0)
                    {
                        Logger.Log($" FAIL");
                        Logger.Log($" UNLOCK");
                        return Response.Error($"Курс заполнен: максимум {course.MaxStudents} студентов");
                    }

                    using var transaction = db.Database.BeginTransaction();
                    try
                    {
                        Logger.Log($" {instance.Name} -> UPDATE");
                        Logger.Log($" BEGIN TRANSACTION");

                        Logger.Log($" Операция 1: запись студента на курс");
                        Enrollment enrollment = new()
                        {
                            StudentId = studentId,
                            CourseId = courseId,
                            IsCompleted = false
                        };
                        db.Enrollments.Add(enrollment);
                        db.SaveChanges();
                        Logger.Log($" Операция 1: SUCCESS");

                        Logger.Log($" Операция 2: уменьшение числа доступных мест");
                        course.AvailableStudents -= 1;
                        db.Courses.Update(course);
                        db.SaveChanges();
                        Logger.Log($" Операция 2: SUCCESS");

                        transaction.Commit();
                        Logger.Log($" COMMIT");
                        Logger.Log($" {instance.Name} -> SUCCESS (Available = {course.AvailableStudents})");

                        ReplicationChange change = new();
                        change.CourseIds.Add(course.Id);
                        change.EnrollmentIds.Add(enrollment.Id);
                        string replication = _replication.ReplicateChange(instance, change);

                        Logger.Log($" UNLOCK");

                        string eventResult = PublishEvent(EventTypes.StudentEnrolled, enrollment.Id, new()
                        {
                            ["StudentId"] = studentId.ToString(),
                            ["StudentName"] = student.FullName,
                            ["CourseId"] = courseId.ToString(),
                            ["CourseTitle"] = course.Title,
                            ["AvailableStudents"] = course.AvailableStudents.ToString()
                        });

                        return Response.Ok(message:
                            $"Студент '{student.FullName}' записан на курс '{course.Title}' (доступно мест: {course.AvailableStudents})\n" +
                            $"-- Экземпляр: {instance.Name}\n" +
                            $"-- Репликация: {replication}\n" +
                            $"-- Событие: {eventResult}");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Logger.Log($" ROLLBACK: {ex.Message}");
                        Logger.Log($" FAIL");
                        Logger.Log($" UNLOCK");
                        return Response.Error($"Операция отменена: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                return DatabaseError(instance, ex);
            }
        }

        public Task<Response> GetCourse(int courseId) =>
            Task.FromResult(GetCourseCore(courseId));

        private Response GetCourseCore(int courseId)
        {
            Logger.Log($" START GetCourse");

            DataInstance? instance = _replication.SelectInstance();
            if (instance == null)
                return Unavailable();

            try
            {
                using EducationDbContext db = instance.CreateContext();

                Logger.Log($" {instance.Name} -> READ");

                Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                if (course == null)
                    return Response.Error($"Курс с id {courseId} не найден");

                Logger.Log($" {instance.Name} -> SUCCESS (Available = {course.AvailableStudents})");

                return Response.Ok(JsonSerializer.Serialize(course, _jsonOptions), $"Экземпляр: {instance.Name}");
            }
            catch (Exception ex)
            {
                return DatabaseError(instance, ex);
            }
        }

        public Task<Response> SubmitAssignment(int assignmentId) =>
            Task.FromResult(SubmitAssignmentCore(assignmentId));

        private Response SubmitAssignmentCore(int assignmentId)
        {
            Logger.Log($" START SubmitAssignment");

            DataInstance? instance = _replication.SelectInstance();
            if (instance == null)
                return Unavailable();

            try
            {
                using EducationDbContext db = instance.CreateContext();

                lock (_assignmentLock)
                {
                    Assignment? assignment = db.Assignments.FirstOrDefault(a => a.Id == assignmentId);
                    if (assignment == null)
                        return Response.Error($"Задание с id {assignmentId} не найдено");

                    if (assignment.IsSubmitted)
                        return Response.Error("Задание уже сдано");

                    bool isEnrolled = db.Enrollments
                        .Any(e => e.StudentId == assignment.StudentId && e.CourseId == assignment.CourseId && !e.IsCompleted);

                    if (!isEnrolled)
                        return Response.Error("Студент не записан на курс этого задания");

                    using var transaction = db.Database.BeginTransaction();
                    try
                    {
                        Logger.Log($" {instance.Name} -> UPDATE");
                        Logger.Log($" BEGIN TRANSACTION");

                        assignment.IsSubmitted = true;
                        db.Assignments.Update(assignment);
                        db.SaveChanges();

                        transaction.Commit();
                        Logger.Log($" COMMIT");
                        Logger.Log($" {instance.Name} -> SUCCESS");

                        ReplicationChange change = new();
                        change.AssignmentIds.Add(assignment.Id);
                        string replication = _replication.ReplicateChange(instance, change);

                        string eventResult = PublishEvent(EventTypes.AssignmentSubmitted, assignment.Id, new()
                        {
                            ["AssignmentTitle"] = assignment.Title,
                            ["StudentId"] = assignment.StudentId.ToString(),
                            ["CourseId"] = assignment.CourseId.ToString()
                        });

                        return Response.Ok(message:
                            $"Задание '{assignment.Title}' сдано\n" +
                            $"-- Экземпляр: {instance.Name}\n" +
                            $"-- Репликация: {replication}\n" +
                            $"-- Событие: {eventResult}");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Logger.Log($" ROLLBACK: {ex.Message}");
                        return Response.Error($"Операция отменена: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                return DatabaseError(instance, ex);
            }
        }

        public Task<Response> CompleteCourse(int studentId, int courseId) =>
            Task.FromResult(CompleteCourseCore(studentId, courseId));

        private Response CompleteCourseCore(int studentId, int courseId)
        {
            Logger.Log($" START CompleteCourse");

            DataInstance? instance = _replication.SelectInstance();
            if (instance == null)
                return Unavailable();

            try
            {
                using EducationDbContext db = instance.CreateContext();

                Student? student = db.Students.FirstOrDefault(s => s.Id == studentId);
                if (student == null)
                    return Response.Error($"Студент с id {studentId} не найден");

                Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                if (course == null)
                    return Response.Error($"Курс с id {courseId} не найден");

                lock (_completeLock)
                {
                    db.Entry(course).Reload();

                    Enrollment? enrollment = db.Enrollments
                        .FirstOrDefault(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

                    if (enrollment == null)
                        return Response.Error("Студент не записан на этот курс или курс уже завершён");

                    List<Assignment> courseAssignments = db.Assignments
                        .Where(a => a.StudentId == studentId && a.CourseId == courseId)
                        .ToList();

                    bool hasUnsubmitted = courseAssignments.Any(a => !a.IsSubmitted);
                    if (hasUnsubmitted)
                    {
                        int count = courseAssignments.Count(a => !a.IsSubmitted);
                        return Response.Error($"Нельзя завершить курс: не сдано заданий — {count}");
                    }

                    using var transaction = db.Database.BeginTransaction();
                    try
                    {
                        Logger.Log($" {instance.Name} -> UPDATE");
                        Logger.Log($" BEGIN TRANSACTION");

                        Logger.Log($" Операция 1: завершение записи студента");
                        enrollment.IsCompleted = true;
                        db.Enrollments.Update(enrollment);
                        db.SaveChanges();
                        Logger.Log($" Операция 1: SUCCESS");

                        Logger.Log($" Операция 2: возврат доступного места");
                        course.AvailableStudents += 1;
                        db.Courses.Update(course);
                        db.SaveChanges();
                        Logger.Log($" Операция 2: SUCCESS");

                        transaction.Commit();
                        Logger.Log($" COMMIT");
                        Logger.Log($" {instance.Name} -> SUCCESS (Available = {course.AvailableStudents})");

                        ReplicationChange change = new();
                        change.CourseIds.Add(course.Id);
                        change.EnrollmentIds.Add(enrollment.Id);
                        string replication = _replication.ReplicateChange(instance, change);

                        string eventResult = PublishEvent(EventTypes.CourseCompleted, enrollment.Id, new()
                        {
                            ["StudentId"] = studentId.ToString(),
                            ["StudentName"] = student.FullName,
                            ["CourseId"] = courseId.ToString(),
                            ["CourseTitle"] = course.Title
                        });

                        return Response.Ok(message:
                            $"Студент '{student.FullName}' завершил курс '{course.Title}'\n" +
                            $"-- Экземпляр: {instance.Name}\n" +
                            $"-- Репликация: {replication}\n" +
                            $"-- Событие: {eventResult}");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Logger.Log($" ROLLBACK: {ex.Message}");
                        return Response.Error($"Операция отменена: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                return DatabaseError(instance, ex);
            }
        }

        public async Task<Response> GetServerTime()
        {
            await Task.Delay(4_000);
            return Response.Ok(DateTime.Now.ToString("O"));
        }
    }
}