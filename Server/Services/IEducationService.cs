using Shared;

namespace Server.Services
{
    public interface IEducationService
    {
        Task<Response> EnrollStudent(int studentId, int courseId);
        Task<Response> GetCourse(int courseId);
        Task<Response> SubmitAssignment(int assignmentId);
        Task<Response> CompleteCourse(int studentId, int courseId);
        Task<Response> GetServerTime();
    }
}