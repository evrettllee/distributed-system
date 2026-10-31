namespace Server.Events
{
    public static class EventTypes
    {
        public const string StudentEnrolled = "StudentEnrolled";
        public const string AssignmentSubmitted = "AssignmentSubmitted";
        public const string CourseCompleted = "CourseCompleted";

        public static readonly IReadOnlyList<string> All =
        [
            StudentEnrolled,
            AssignmentSubmitted,
            CourseCompleted
        ];

        public static bool IsKnown(string? eventType) =>
            eventType != null && All.Contains(eventType);
    }
}