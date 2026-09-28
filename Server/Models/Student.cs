using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    [Table("students")]
    public class Student
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;
        [Column("email")]
        public string Email { get; set; } = string.Empty;
    }
}