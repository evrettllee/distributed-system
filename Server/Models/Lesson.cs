using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    [Table("lessons")]
    public class Lesson
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("title")]
        public string Title { get; set; } = string.Empty;
        [Column("course_id")]
        public int CourseId { get; set; }
        [Column("duration_minutes")]
        public int DurationMinutes { get; set; }
    }
}