using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    [Table("teachers")]
    public class Teacher
    {
        [Column("id")]
        public int Id { get; set; }
        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;
        [Column("subject")]
        public string Subject { get; set; } = string.Empty;
    }
}