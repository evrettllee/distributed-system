using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    [Table("assignments")]
    public class Assignment
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("title")]
        public string Title { get; set; } = string.Empty;
        [Column("course_id")]
        public int CourseId { get; set; }
        [Column("student_id")]
        public int StudentId { get; set; }
        [Column("is_submitted")]
        public bool IsSubmitted { get; set; } = false;
    }
}