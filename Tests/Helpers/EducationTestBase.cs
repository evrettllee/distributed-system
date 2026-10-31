using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.PubSub;
using Server.Replication;
using Server.Services;

namespace Tests.Helpers
{
    public abstract class EducationTestBase
    {
        protected readonly TestDataInstance Primary;
        protected readonly TestDataInstance Replica;
        protected readonly ReplicationManager Replication;
        protected readonly EducationService Service;

        protected EducationTestBase()
        {
            Primary = TestDatabase.CreateSeeded("Primary");
            Replica = TestDatabase.CreateSeeded("Replica");

            Replication = new ReplicationManager(Primary, Replica);
            Service = new EducationService(Replication, new Publisher(new Broker("TestBroker")));
        }

        protected static int Available(DataInstance instance, int courseId)
        {
            using EducationDbContext db = instance.CreateContext();
            return db.Courses.AsNoTracking().First(c => c.Id == courseId).AvailableStudents;
        }

        protected static int EnrolledCount(DataInstance instance, int courseId)
        {
            using EducationDbContext db = instance.CreateContext();
            return db.Enrollments.Count(e => e.CourseId == courseId && !e.IsCompleted);
        }

        protected static int EnrollmentsOfStudent(DataInstance instance, int studentId)
        {
            using EducationDbContext db = instance.CreateContext();
            return db.Enrollments.Count(e => e.StudentId == studentId);
        }
    }
}
