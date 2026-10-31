using Microsoft.EntityFrameworkCore;
using Server.Models;

namespace Server.Data
{
    public class EducationDbContext : DbContext
    {
        public DbSet<Student> Students { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Lesson> Lessons { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<Assignment> Assignments { get; set; }

        public EducationDbContext(DbContextOptions<EducationDbContext> options)
            : base(options) { }
    }
}