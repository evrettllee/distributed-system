using Server.Common;
using Server.Events;
using Server.Models;
using Server.PubSub;
using Server.Replication;
using Shared;
using System.Text.Encodings.Web;
using System.Text.Json;

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

        public Task<Response> EnrollStudent(int studentId, int courseId) =>
            DataAccess.ExecuteAsync(_replication, async (instance, db) =>
            {
                Student? student = db.Students.FirstOrDefault(s => s.Id == studentId);
                if (student == null)
                    return Response.Error($"Студент с id {studentId} не найден");

                Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                if (course == null)
                    return Response.Error($"Курс с id {courseId} не найден");

                await Task.Delay(3_000);

                lock (_enrollLock)
                {
                    db.Entry(course).Reload();

                    bool alreadyEnrolled = db.Enrollments
                        .Any(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

                    if (alreadyEnrolled)
                        return Response.Error("Студент уже записан на этот курс");

                    if (course.AvailableStudents <= 0)
                        return Response.Error($"Курс заполнен: максимум {course.MaxStudents} студентов");

                    Enrollment enrollment = new()
                    {
                        StudentId = studentId,
                        CourseId = courseId,
                        IsCompleted = false
                    };

                    string? error = DataAccess.RunTransaction(db, () =>
                    {
                        db.Enrollments.Add(enrollment);
                        db.SaveChanges();

                        course.AvailableStudents -= 1;
                        db.Courses.Update(course);
                        db.SaveChanges();
                    });

                    if (error != null)
                        return Cancelled(error);

                    Logger.Log($"{instance.Name} -> EnrollStudent SUCCESS (Available = {course.AvailableStudents})");

                    ReplicationChange change = new();
                    change.CourseIds.Add(course.Id);
                    change.EnrollmentIds.Add(enrollment.Id);

                    return ReplicateAndPublish(instance, change,
                        $"Студент '{student.FullName}' записан на курс '{course.Title}' (доступно мест: {course.AvailableStudents})",
                        EventTypes.StudentEnrolled, enrollment.Id, new()
                        {
                            ["StudentId"] = studentId.ToString(),
                            ["StudentName"] = student.FullName,
                            ["CourseId"] = courseId.ToString(),
                            ["CourseTitle"] = course.Title,
                            ["AvailableStudents"] = course.AvailableStudents.ToString()
                        });
                }
            });

        public Task<Response> GetCourse(int courseId) =>
            Task.FromResult(DataAccess.Execute(_replication, (instance, db) =>
            {
                Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                if (course == null)
                    return Response.Error($"Курс с id {courseId} не найден");

                Logger.Log($"{instance.Name} -> GetCourse SUCCESS (Available = {course.AvailableStudents})");

                return Response.Ok(JsonSerializer.Serialize(course, _jsonOptions), $"Экземпляр: {instance.Name}");
            }));

        public Task<Response> SubmitAssignment(int assignmentId) =>
            Task.FromResult(DataAccess.Execute(_replication, (instance, db) =>
            {
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

                    string? error = DataAccess.RunTransaction(db, () =>
                    {
                        assignment.IsSubmitted = true;
                        db.Assignments.Update(assignment);
                        db.SaveChanges();
                    });

                    if (error != null)
                        return Cancelled(error);

                    Logger.Log($"{instance.Name} -> SubmitAssignment SUCCESS");

                    ReplicationChange change = new();
                    change.AssignmentIds.Add(assignment.Id);

                    return ReplicateAndPublish(instance, change,
                        $"Задание '{assignment.Title}' сдано",
                        EventTypes.AssignmentSubmitted, assignment.Id, new()
                        {
                            ["AssignmentTitle"] = assignment.Title,
                            ["StudentId"] = assignment.StudentId.ToString(),
                            ["CourseId"] = assignment.CourseId.ToString()
                        });
                }
            }));

        public Task<Response> CompleteCourse(int studentId, int courseId) =>
            Task.FromResult(DataAccess.Execute(_replication, (instance, db) =>
            {
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

                    int unsubmitted = db.Assignments
                        .Count(a => a.StudentId == studentId && a.CourseId == courseId && !a.IsSubmitted);

                    if (unsubmitted > 0)
                        return Response.Error($"Нельзя завершить курс: не сдано заданий — {unsubmitted}");

                    string? error = DataAccess.RunTransaction(db, () =>
                    {
                        enrollment.IsCompleted = true;
                        db.Enrollments.Update(enrollment);
                        db.SaveChanges();

                        course.AvailableStudents += 1;
                        db.Courses.Update(course);
                        db.SaveChanges();
                    });

                    if (error != null)
                        return Cancelled(error);

                    Logger.Log($"{instance.Name} -> CompleteCourse SUCCESS (Available = {course.AvailableStudents})");

                    ReplicationChange change = new();
                    change.CourseIds.Add(course.Id);
                    change.EnrollmentIds.Add(enrollment.Id);

                    return ReplicateAndPublish(instance, change,
                        $"Студент '{student.FullName}' завершил курс '{course.Title}'",
                        EventTypes.CourseCompleted, enrollment.Id, new()
                        {
                            ["StudentId"] = studentId.ToString(),
                            ["StudentName"] = student.FullName,
                            ["CourseId"] = courseId.ToString(),
                            ["CourseTitle"] = course.Title
                        });
                }
            }));

        public async Task<Response> GetServerTime()
        {
            await Task.Delay(4_000);
            return Response.Ok(DateTime.Now.ToString("O"));
        }

        private static Response Cancelled(string error) =>
            Response.Error($"Операция отменена: {error}");

        private Response ReplicateAndPublish(DataInstance instance, ReplicationChange change, string summary,
            string eventType, int entityId, Dictionary<string, string> data)
        {
            List<string> warnings = new();

            string? replication = DataAccess.RunAfterCommit(instance, "репликация",
                () => _replication.ReplicateChange(instance, change), warnings);

            string? published = DataAccess.RunAfterCommit(instance, "публикация",
                () => _publisher.Publish(Event.Create(eventType, entityId, data)).Message, warnings);

            List<string> lines = new() { summary, $"-- Экземпляр: {instance.Name}" };

            if (replication != null)
                lines.Add($"-- Репликация: {replication}");

            if (published != null)
                lines.Add($"-- Событие: {eventType} -> {published}");

            lines.AddRange(warnings);

            return Response.Ok(message: string.Join("\n", lines));
        }
    }
}
