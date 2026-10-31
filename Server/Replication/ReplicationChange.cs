namespace Server.Replication
{
    public class ReplicationChange
    {
        public List<int> CourseIds { get; } = new();
        public List<int> EnrollmentIds { get; } = new();
        public List<int> AssignmentIds { get; } = new();
    }
}