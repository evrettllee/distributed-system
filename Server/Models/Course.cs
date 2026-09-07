namespace Server.Models
{
    public class Course
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TeacherId { get; set; }
        public int MaxStudents { get; set; } = 30; 
    }
}
