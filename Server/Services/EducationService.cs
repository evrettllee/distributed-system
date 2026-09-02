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

        public Response EnrollStudent(int studentId, int courseId)
        {
            Student? student = _students.GetById(studentId);
            if (student == null)
            {
                return Response.Error($"Студент с id {studentId} не найден");
            }

            Course? course = _courses.GetById(courseId);
            if (course == null)
            {
                return Response.Error($"Курс с id {courseId} не найден");
            }

            bool alreadyEnrolled = _enrollments.GetAll()
                .Any(e => e.StudentId == studentId && e.CourseId == courseId);

            if (alreadyEnrolled)
            {
                return Response.Error("Студент уже записан на этот курс");
            }

            Enrollment enrollment = new()
            {
                StudentId = studentId,
                CourseId = courseId,
                IsCompleted = false
            };

            _enrollments.Add(enrollment);

            return Response.Ok(message: $"Студент '{student.FullName}' записан на курс '{course.Title}'");
        }

        public Response GetCourse(int courseId)
        {
            Course? course = _courses.GetById(courseId);
            if (course == null)
            {
                return Response.Error($"Курс с id {courseId} не найден");
            }

            return Response.Ok(JsonSerializer.Serialize(course, _jsonOptions));
        }

        public Response SubmitAssignment(int assignmentId)
        {
            Assignment? assignment = _assignments.GetById(assignmentId);
            if (assignment == null)
            {
                return Response.Error($"Задание с id {assignmentId} не найдено");
            }

            if (assignment.IsSubmitted)
            {
                return Response.Error("Задание уже сдано");
            }

            assignment.IsSubmitted = true;
            _assignments.Update(assignment);

            return Response.Ok(message: $"Задание '{assignment.Title}' сдано");
        }

        public Response CompleteCourse(int studentId, int courseId)
        {
            Enrollment? enrollment = _enrollments.GetAll()
                .FirstOrDefault(e => e.StudentId == studentId && e.CourseId == courseId);

            if (enrollment == null)
            {
                return Response.Error("Студент не записан на этот курс");
            }

            if (enrollment.IsCompleted)
            {
                return Response.Error("Курс уже завершён");
            }

            enrollment.IsCompleted = true;
            _enrollments.Update(enrollment);

            return Response.Ok(message: $"Курс с id {courseId} отмечен как завершённый");
        }
    }
}