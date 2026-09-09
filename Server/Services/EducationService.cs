using Shared;
using Server.Models;
using Server.Repositories;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Server.Services
{
    public class EducationService : IEducationService
    {
        private readonly IRepository<Student> _students;
        private readonly IRepository<Course> _courses;
        private readonly IRepository<Enrollment> _enrollments;
        private readonly IRepository<Assignment> _assignments;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public EducationService(
            IRepository<Student> students,
            IRepository<Course> courses,
            IRepository<Enrollment> enrollments,
            IRepository<Assignment> assignments)
        {
            _students = students;
            _courses = courses;
            _enrollments = enrollments;
            _assignments = assignments;
        }

        public async Task<Response> EnrollStudent(int studentId, int courseId)
        {
            Student? student = _students.GetById(studentId);
            if (student == null)
                return Response.Error($"Студент с id {studentId} не найден");

            Course? course = _courses.GetById(courseId);
            if (course == null)
                return Response.Error($"Курс с id {courseId} не найден");

            bool alreadyEnrolled = _enrollments.GetAll()
                .Any(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

            if (alreadyEnrolled)
                return Response.Error("Студент уже записан на этот курс");

            int currentStudents = _enrollments.GetAll()
                .Count(e => e.CourseId == courseId && !e.IsCompleted);

            if (currentStudents >= course.MaxStudents)
                return Response.Error($"Курс заполнен: максимум {course.MaxStudents} студентов");

            // задержка для демонстрации race condition
            await Task.Delay(5_000);

            _enrollments.Add(new Enrollment
            {
                StudentId = studentId,
                CourseId = courseId,
                IsCompleted = false
            });

            return Response.Ok(message: $"Студент '{student.FullName}' записан на курс '{course.Title}' ({currentStudents + 1}/{course.MaxStudents})");
        }

        public async Task<Response> GetCourse(int courseId)
        {
            Course? course = _courses.GetById(courseId);
            if (course == null)
                return Response.Error($"Курс с id {courseId} не найден");

            return Response.Ok(JsonSerializer.Serialize(course, _jsonOptions));
        }

        public async Task<Response> SubmitAssignment(int assignmentId)
        {
            Assignment? assignment = _assignments.GetById(assignmentId);
            if (assignment == null)
                return Response.Error($"Задание с id {assignmentId} не найдено");

            if (assignment.IsSubmitted)
                return Response.Error("Задание уже сдано");

            bool isEnrolled = _enrollments.GetAll()
                .Any(e => e.StudentId == assignment.StudentId && e.CourseId == assignment.CourseId && !e.IsCompleted);

            if (!isEnrolled)
                return Response.Error("Студент не записан на курс этого задания");

            assignment.IsSubmitted = true;
            _assignments.Update(assignment);

            return Response.Ok(message: $"Задание '{assignment.Title}' сдано");
        }

        public async Task<Response> CompleteCourse(int studentId, int courseId)
        {
            Student? student = _students.GetById(studentId);
            if (student == null)
                return Response.Error($"Студент с id {studentId} не найден");

            Course? course = _courses.GetById(courseId);
            if (course == null)
                return Response.Error($"Курс с id {courseId} не найден");

            Enrollment? enrollment = _enrollments.GetAll()
                .FirstOrDefault(e => e.StudentId == studentId && e.CourseId == courseId && !e.IsCompleted);

            if (enrollment == null)
                return Response.Error("Студент не записан на этот курс или курс уже завершён");

            List<Assignment> courseAssignments = _assignments.GetAll()
                .Where(a => a.StudentId == studentId && a.CourseId == courseId)
                .ToList();

            bool hasUnsubmitted = courseAssignments.Any(a => !a.IsSubmitted);
            if (hasUnsubmitted)
            {
                int count = courseAssignments.Count(a => !a.IsSubmitted);
                return Response.Error($"Нельзя завершить курс: не сдано заданий — {count}");
            }

            enrollment.IsCompleted = true;
            _enrollments.Update(enrollment);

            return Response.Ok(message: $"Студент '{student.FullName}' завершил курс '{course.Title}'");
        }
    }
}