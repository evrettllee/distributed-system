using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Consensus;
using Server.Data;
using Server.Models;
using Server.Replication;
using Server.Services;
using Shared;
using Xunit;

namespace Tests
{
    public class ConsensusTests
    {
        private static Proposal CloseCourse() => new(1, CourseStatus.Active, CourseStatus.Closed);

        private static void Fail(Cluster cluster, params int[] ids)
        {
            foreach (int id in ids)
                cluster.SetNodeStatus(cluster.FindNode(id)!, NodeStatus.Failed);
        }

        // Тест 48 — лидером выбирается доступный узел с максимальным ID
        [Fact]
        public void Election_MaxId_BecomesLeader()
        {
            Cluster cluster = new(5);

            Node? leader = cluster.RunElection();

            Assert.NotNull(leader);
            Assert.Equal(5, leader.Id);
            Assert.Equal(NodeRole.Leader, leader.Role);
        }

        // Тест 49 — недоступный узел не может стать лидером
        [Fact]
        public void Election_FailedNode_NotElected()
        {
            Cluster cluster = new(5);
            cluster.FindNode(5)!.SetStatus(NodeStatus.Failed);

            Node? leader = cluster.RunElection();

            Assert.NotNull(leader);
            Assert.Equal(4, leader.Id);
            Assert.Equal(NodeRole.Follower, cluster.FindNode(5)!.Role);
        }

        // Тест 50 — отказ лидера обнаруживается по heartbeat, выбирается новый лидер, лидер ровно один
        [Fact]
        public void Heartbeat_LeaderFailed_NewSingleLeader()
        {
            Cluster cluster = new(5);
            cluster.RunElection();

            Fail(cluster, 5);
            List<string> log = cluster.CheckHeartbeat();

            Assert.Contains(log, l => l.Contains("HEARTBEAT TIMEOUT"));
            Assert.Equal(4, cluster.Leader!.Id);
            Assert.Single(cluster.Nodes, n => n.Role == NodeRole.Leader);
        }

        // Тест 51 — при большинстве YES консенсус достигнут (один узел голосует NO)
        [Fact]
        public void Consensus_MajorityYes_Achieved()
        {
            Cluster cluster = new(5);
            cluster.RunElection();
            cluster.FindNode(1)!.SetForceVoteNo(true);

            ConsensusResult result = cluster.Propose(CloseCourse());

            Assert.Equal(4, result.Yes);
            Assert.Equal(1, result.No);
            Assert.Equal(3, result.Majority);
            Assert.True(result.Achieved);
        }

        // Тест 52 — без большинства (2 из 5 узлов) консенсус не достигнут и статус курса не изменён
        [Fact]
        public async Task Consensus_NoMajority_NotAchievedAndNotApplied()
        {
            TestDataInstance primary = new("Primary", CreateOptions());
            TestDataInstance replica = new("Replica", CreateOptions());
            Seed(primary);
            Seed(replica);

            Cluster cluster = new(5);
            cluster.RunElection();
            Fail(cluster, 3, 4, 5);

            ConsensusService service = new(cluster, new ReplicationManager(primary, replica));
            Response response = await service.ProposeCourseStatus(1, "Closed");

            Assert.Equal("error", response.Status);
            Assert.Contains("CONSENSUS NOT ACHIEVED", response.Message);

            using EducationDbContext db = primary.CreateContext();
            Assert.Equal(CourseStatus.Active, db.Courses.First(c => c.Id == 1).Status);
        }

        private static DbContextOptions<EducationDbContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<EducationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        }

        private static void Seed(DataInstance instance)
        {
            using EducationDbContext db = instance.CreateContext();

            db.Teachers.Add(new Teacher { Id = 1, FullName = "Смирнов", Subject = "Математика" });
            db.Courses.Add(new Course { Id = 1, Title = "Основы программирования", TeacherId = 1, MaxStudents = 30, AvailableStudents = 30, Status = CourseStatus.Active });
            db.SaveChanges();
        }
    }
}