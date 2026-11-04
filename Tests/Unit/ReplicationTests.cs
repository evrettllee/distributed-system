using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Models;
using Server.Replication;
using Shared;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class ReplicationTests : EducationTestBase
    {
        // Тест 14 — начальное состояние: Primary и Replica созданы и совпадают
        [Fact]
        public void InitialState_PrimaryEqualsReplica()
        {
            Assert.Equal(InstanceState.Available, Primary.CheckState());
            Assert.Equal(InstanceState.Available, Replica.CheckState());

            Assert.Empty(Replication.Compare());
            Assert.Equal(Available(Primary, 1), Available(Replica, 1));
        }

        // Тест 15 — после успешной репликации данные совпадают
        [Fact]
        public async Task Replication_AfterEnroll_ReplicaEqualsPrimary()
        {
            Response response = await Service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Экземпляр: Primary", response.Message);
            Assert.Equal(29, Available(Primary, 1));
            Assert.Equal(29, Available(Replica, 1));
            Assert.Equal(1, EnrolledCount(Replica, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 16 — три последовательных изменения, после каждого Primary = Replica
        [Fact]
        public async Task Replication_ThreeSequentialChanges_DataMatchesAfterEach()
        {
            Response r1 = await Service.EnrollStudent(1, 1);
            Assert.Equal("ok", r1.Status);
            Assert.Equal(29, Available(Replica, 1));
            Assert.Empty(Replication.Compare());

            Response r2 = await Service.EnrollStudent(2, 1);
            Assert.Equal("ok", r2.Status);
            Assert.Equal(28, Available(Replica, 1));
            Assert.Empty(Replication.Compare());

            Response r3 = await Service.CompleteCourse(1, 1);
            Assert.Equal("ok", r3.Status);
            Assert.Equal(29, Available(Replica, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 17 — корректная обработка изменения: сдача задания реплицируется
        [Fact]
        public async Task Replication_SubmitAssignment_ReplicatedToReplica()
        {
            await Service.EnrollStudent(2, 1);
            Response response = await Service.SubmitAssignment(1);

            using EducationDbContext db = Replica.CreateContext();
            Assignment assignment = db.Assignments.AsNoTracking().First(a => a.Id == 1);

            Assert.Equal("ok", response.Status);
            Assert.True(assignment.IsSubmitted);
            Assert.Empty(Replication.Compare());
        }

        // Тест 18 — определение недоступного Primary
        [Fact]
        public void Failover_PrimaryDown_IsDetectedAsUnavailable()
        {
            Primary.IsDown = true;

            Assert.Equal(InstanceState.Unavailable, Primary.CheckState());
            Assert.Equal(InstanceState.Available, Replica.CheckState());
        }

        // Тест 19 — выбор доступной Replica при отказе Primary
        [Fact]
        public void Failover_PrimaryDown_ReplicaIsSelected()
        {
            Primary.IsDown = true;

            DataInstance? selected = Replication.SelectInstance();

            Assert.Same(Replica, selected);
        }

        // Тест 20 — успешное выполнение запросов (Read и Update) после Failover
        [Fact]
        public async Task Failover_RequestsAfterPrimaryFailure_ProcessedByReplica()
        {
            Primary.IsDown = true;

            Response read = await Service.GetCourse(1);
            Response update = await Service.EnrollStudent(2, 1);

            Assert.Equal("ok", read.Status);
            Assert.Contains("Экземпляр: Replica", read.Message);
            Assert.Equal("ok", update.Status);
            Assert.Contains("Экземпляр: Replica", update.Message);

            Assert.Equal(29, Available(Replica, 1));
            Assert.Equal(30, Available(Primary, 1));
            Assert.True(Replication.PrimaryOutdated);
        }

        // Тест 21 — оба экземпляра недоступны: понятная ошибка, без падения
        [Fact]
        public async Task Failover_BothDown_ReturnsClearError()
        {
            Primary.IsDown = true;
            Replica.IsDown = true;

            Response response = await Service.EnrollStudent(1, 1);

            Assert.Equal("error", response.Status);
            Assert.Contains("Primary и Replica недоступны", response.Message);
        }

        // Тест 22 — Primary восстановлен, но устарел: автоматическая синхронизация и возврат на Primary
        [Fact]
        public async Task Recovery_PrimaryBackButOutdated_AutoSyncAndPrimaryUsed()
        {
            Primary.IsDown = true;
            await Service.EnrollStudent(1, 1);

            Primary.IsDown = false;
            Assert.NotEmpty(Replication.Compare());

            DataInstance? selected = Replication.SelectInstance();

            Assert.Same(Primary, selected);
            Assert.False(Replication.PrimaryOutdated);
            Assert.Equal(29, Available(Primary, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 23 — восстановление Primary и синхронизация Replica -> Primary
        [Fact]
        public async Task Recovery_SyncReplicaToPrimary_DataMatches()
        {
            Primary.IsDown = true;
            await Service.EnrollStudent(1, 1);
            await Service.EnrollStudent(2, 1);

            Primary.IsDown = false;
            Assert.NotEmpty(Replication.Compare());

            Response response = Replication.Recover();

            Assert.Equal("ok", response.Status);
            Assert.Empty(Replication.Compare());
            Assert.Equal(28, Available(Primary, 1));
            Assert.Equal(2, EnrolledCount(Primary, 1));
            Assert.False(Replication.PrimaryOutdated);
            Assert.Same(Primary, Replication.SelectInstance());
        }

        // Тест 24 — контрольная операция после восстановления: снова Primary -> Replication
        [Fact]
        public async Task Recovery_ControlOperationAfterSync_ReplicatedNormally()
        {
            Primary.IsDown = true;
            await Service.EnrollStudent(1, 1);
            Primary.IsDown = false;
            Replication.Recover();

            Response response = await Service.EnrollStudent(2, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Экземпляр: Primary", response.Message);
            Assert.Equal(28, Available(Primary, 1));
            Assert.Equal(28, Available(Replica, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 25 — Replica недоступна: работа через Primary, затем синхронизация Primary -> Replica
        [Fact]
        public async Task Recovery_ReplicaDown_ThenSyncPrimaryToReplica()
        {
            Replica.IsDown = true;

            Response response = await Service.EnrollStudent(1, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("Replica недоступна", response.Message);
            Assert.True(Replication.ReplicaOutdated);
            Assert.Equal(30, Available(Replica, 1));

            Replica.IsDown = false;
            Response recover = Replication.Recover();

            Assert.Equal("ok", recover.Status);
            Assert.Equal(29, Available(Replica, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 26 — синхронизация невозможна, пока Primary недоступен
        [Fact]
        public void Recovery_PrimaryStillDown_ReturnsError()
        {
            Primary.IsDown = true;

            Response response = Replication.Recover();

            Assert.Equal("error", response.Status);
            Assert.Contains("Синхронизация невозможна", response.Message);
        }

        // Тест 27 — нарушение согласованности без отказа: расхождение обнаруживается и устраняется
        [Fact]
        public void Recovery_ManualDivergence_DetectedAndFixed()
        {
            using (EducationDbContext db = Replica.CreateContext())
            {
                Course course = db.Courses.First(c => c.Id == 1);
                course.AvailableStudents = 7;
                db.SaveChanges();
            }

            Assert.NotEmpty(Replication.Compare());

            Response response = Replication.Recover();

            Assert.Equal("ok", response.Status);
            Assert.Equal(30, Available(Replica, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 28 — автоматическая синхронизация Primary при первом запросе после восстановления
        [Fact]
        public async Task AutoSync_ReadAfterPrimaryRecovery_ProcessedByPrimary()
        {
            Primary.IsDown = true;
            await Service.EnrollStudent(1, 1);

            Primary.IsDown = false;
            Response read = await Service.GetCourse(1);

            Assert.Equal("ok", read.Status);
            Assert.Contains("Экземпляр: Primary", read.Message);
            Assert.Equal(29, Available(Primary, 1));
            Assert.Empty(Replication.Compare());
        }

        // Тест 29 — автоматическая синхронизация Replica при следующем изменении после её восстановления
        [Fact]
        public async Task AutoSync_ChangeAfterReplicaRecovery_ReplicaSynchronized()
        {
            Replica.IsDown = true;
            await Service.EnrollStudent(1, 1);

            Replica.IsDown = false;
            Response response = await Service.EnrollStudent(2, 1);

            Assert.Equal("ok", response.Status);
            Assert.Contains("автоматическая синхронизация", response.Message);
            Assert.False(Replication.ReplicaOutdated);
            Assert.Equal(28, Available(Replica, 1));
            Assert.Equal(2, EnrolledCount(Replica, 1));
            Assert.Empty(Replication.Compare());
        }
    }
}
