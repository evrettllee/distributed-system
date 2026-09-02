namespace Server.Models
{
    public class Enrollment
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int CourseId { get; set; }
        public bool IsCompleted { get; set; } = false;
    }
}
