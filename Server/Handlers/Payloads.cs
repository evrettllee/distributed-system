namespace Server.Handlers
{
    internal class IdPayload
    {
        public int Id { get; set; }
    }

    internal class StudentCoursePayload
    {
        public int StudentId { get; set; }
        public int CourseId { get; set; }
    }

    internal class NamePayload
    {
        public string Name { get; set; } = string.Empty;
    }

    internal class SubscriberStatePayload
    {
        public string Name { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    internal class AvailabilityPayload
    {
        public string Name { get; set; } = string.Empty;
        public bool Available { get; set; }
    }

    internal class NodeVotePayload
    {
        public string Name { get; set; } = string.Empty;
        public bool VoteNo { get; set; }
    }

    internal class CourseStatusPayload
    {
        public int CourseId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
