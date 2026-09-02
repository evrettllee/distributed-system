using Shared;

namespace Server.Services
{
    public interface IEducationService
    {
        Response EnrollStudent(int studentId, int courseId);
        Response GetCourse(int courseId);
        Response SubmitAssignment(int assignmentId);
        Response CompleteCourse(int studentId, int courseId);
    }
}
