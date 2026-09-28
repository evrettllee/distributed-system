using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    [Table("enrollments")]
    public class Enrollment
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("student_id")]
        public int StudentId { get; set; }
        [Column("course_id")]
        public int CourseId { get; set; }
        [Column("is_completed")]
        public bool IsCompleted { get; set; } = false;
    }
}