using Client.Network;
using Client.UI;
using Shared;

namespace Client.Menu
{
    public static class EducationMenu
    {
        public static Response EnrollStudent(ServerClient client) =>
            SendStudentCourse(client, "EnrollStudent");

        public static Response GetCourse(ServerClient client)
        {
            int? courseId = ConsoleInput.ReadId("Id курса: ");
            if (courseId == null) return ConsoleInput.InvalidId();

            return client.Send("GetCourse", new { Id = courseId });
        }

        public static Response SubmitAssignment(ServerClient client)
        {
            int? assignmentId = ConsoleInput.ReadId("Id задания: ");
            if (assignmentId == null) return ConsoleInput.InvalidId();

            return client.Send("SubmitAssignment", new { Id = assignmentId });
        }

        public static Response CompleteCourse(ServerClient client) =>
            SendStudentCourse(client, "CompleteCourse");

        private static Response SendStudentCourse(ServerClient client, string operation)
        {
            int? studentId = ConsoleInput.ReadId("Id студента: ");
            if (studentId == null) return ConsoleInput.InvalidId();

            int? courseId = ConsoleInput.ReadId("Id курса: ");
            if (courseId == null) return ConsoleInput.InvalidId();

            return client.Send(operation, new { StudentId = studentId, CourseId = courseId });
        }
    }
}
