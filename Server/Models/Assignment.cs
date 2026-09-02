namespace Server.Models
{
    public class Assignment
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int CourseId { get; set; }
        public int StudentId { get; set; }
        public bool IsSubmitted { get; set; } = false;
    }
}
