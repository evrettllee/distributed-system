using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    [Table("courses")]
    public class Course
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("title")]
        public string Title { get; set; } = string.Empty;
        [Column("teacher_id")]
        public int TeacherId { get; set; }
        [Column("max_students")]
        public int MaxStudents { get; set; } = 30;
    }
}