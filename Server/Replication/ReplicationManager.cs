using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Server.Common;
using Server.Data;
using Server.Models;
using Shared;
using System.Reflection;

namespace Server.Replication
{
    public class ReplicationManager
    {
        private readonly Lock _syncLock = new();
        private readonly Lock _stateLock = new();

        private bool _primaryOutdated;
        private bool _replicaOutdated;

        public DataInstance Primary { get; }
        public DataInstance Replica { get; }

        public bool PrimaryOutdated
        {
            get { lock (_stateLock) return _primaryOutdated; }
            private set { lock (_stateLock) _primaryOutdated = value; }
        }

        public bool ReplicaOutdated
        {
            get { lock (_stateLock) return _replicaOutdated; }
            private set { lock (_stateLock) _replicaOutdated = value; }
        }

        public ReplicationManager(DataInstance primary, DataInstance replica)
        {
            Primary = primary;
            Replica = replica;
        }

        public DataInstance? SelectInstance()
        {
            if (Primary.IsAvailable())
            {
                if (!PrimaryOutdated)
                    return Primary;

                Logger.Log("Primary содержит устаревшие данные -> автоматическая синхронизация");

                Response sync = Recover();
                if (sync.Status == "ok")
                    return Primary;

                Logger.Log("Автоматическая синхронизация не выполнена, работа продолжается через Replica");
            }

            Logger.Log("Failover -> START");

            if (!Replica.IsAvailable())
            {
                Logger.Log("Failover -> FAIL (нет доступного экземпляра)");
                return null;
            }

            if (ReplicaOutdated)
                Logger.Log("ВНИМАНИЕ: Replica может содержать неполные данные");

            Logger.Log("Request redirected to Replica");
            return Replica;
        }

        public string ReplicateChange(DataInstance used, ReplicationChange change)
        {
            if (used == Replica)
            {
                PrimaryOutdated = true;
                Logger.Log("Replication -> SKIP (Primary недоступен, изменение сохранено только в Replica)");
                return "изменение выполнено через Replica, Primary будет синхронизирован после восстановления";
            }

            Logger.Log("Replication -> START");

            if (!Replica.IsAvailable())
            {
                ReplicaOutdated = true;
                Logger.Log("Replication -> FAIL (Replica недоступна)");
                return "репликация не выполнена: Replica недоступна";
            }

            if (ReplicaOutdated)
            {
                Logger.Log("Replica содержит устаревшие данные -> автоматическая синхронизация");

                Response sync = Recover();
                return sync.Status == "ok"
                    ? "Replica была устаревшей, выполнена автоматическая синхронизация Primary -> Replica"
                    : $"автоматическая синхронизация не выполнена: {sync.Message}";
            }

            lock (_syncLock)
            {
                try
                {
                    using EducationDbContext source = Primary.CreateContext();
                    using EducationDbContext target = Replica.CreateContext();

                    CopyRows<Course>(source, target, Replica.Name, "courses", c => c.Id, change.CourseIds);
                    CopyRows<Enrollment>(source, target, Replica.Name, "enrollments", e => e.Id, change.EnrollmentIds);
                    CopyRows<Assignment>(source, target, Replica.Name, "assignments", a => a.Id, change.AssignmentIds);

                    Logger.Log("Replication -> SUCCESS");
                    return "репликация в Replica выполнена";
                }
                catch (Exception ex)
                {
                    ReplicaOutdated = true;
                    Logger.Log($"Replication -> FAIL ({ex.Message})");
                    return $"ошибка репликации: {ex.Message}";
                }
            }
        }

        public Response Recover()
        {
            lock (_syncLock)
            {
                Logger.Log("Recovery -> START");

                bool primaryOk = Primary.IsAvailable();
                bool replicaOk = Replica.IsAvailable();

                if (!primaryOk || !replicaOk)
                {
                    Logger.Log("Recovery -> FAIL");
                    return Response.Error($"Синхронизация невозможна: Primary {Primary.StateText}, Replica {Replica.StateText}");
                }

                List<string> before;
                try
                {
                    before = Compare();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Recovery -> FAIL (ошибка чтения данных: {ex.Message})");
                    return Response.Error($"Ошибка чтения данных при сравнении копий: {ex.Message}");
                }

                Logger.Log($"Найдено расхождений: {before.Count}");
                foreach (string diff in before)
                    Logger.Log($"  {diff}");

                DataInstance source;
                DataInstance target;

                if (PrimaryOutdated)
                {
                    source = Replica;
                    target = Primary;
                }
                else if (ReplicaOutdated || before.Count > 0)
                {
                    source = Primary;
                    target = Replica;
                }
                else
                {
                    Logger.Log("Recovery -> SUCCESS (Primary = Replica, синхронизация не требуется)");
                    return Response.Ok(message: "Primary и Replica доступны, данные уже согласованы");
                }

                Logger.Log($"Synchronization -> START ({source.Name} -> {target.Name})");

                try
                {
                    SyncAll(source, target);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Synchronization -> FAIL ({ex.Message})");
                    return Response.Error($"Ошибка синхронизации: {ex.Message}");
                }

                List<string> after = Compare();
                if (after.Count > 0)
                {
                    Logger.Log($"Нарушение согласованности после синхронизации: {after.Count}");
                    return Response.Error($"После синхронизации осталось расхождений: {after.Count}");
                }

                PrimaryOutdated = false;
                ReplicaOutdated = false;

                Logger.Log("Synchronization -> SUCCESS");
                Logger.Log("Primary = Replica");

                return Response.Ok(message:
                    $"Синхронизация {source.Name} -> {target.Name} выполнена\n" +
                    $"-- Найдено расхождений: {before.Count}\n" +
                    string.Concat(before.Select(d => $"--   {d}\n")) +
                    "-- После синхронизации: Primary = Replica");
            }
        }

        private void SyncAll(DataInstance source, DataInstance target)
        {
            using EducationDbContext src = source.CreateContext();
            using EducationDbContext dst = target.CreateContext();

            DeleteMissing<Assignment>(src, dst, target.Name, "assignments", a => a.Id);
            DeleteMissing<Enrollment>(src, dst, target.Name, "enrollments", e => e.Id);
            DeleteMissing<Lesson>(src, dst, target.Name, "lessons", l => l.Id);
            DeleteMissing<Course>(src, dst, target.Name, "courses", c => c.Id);
            DeleteMissing<Teacher>(src, dst, target.Name, "teachers", t => t.Id);
            DeleteMissing<Student>(src, dst, target.Name, "students", s => s.Id);

            CopyRows<Student>(src, dst, target.Name, "students", s => s.Id, null);
            CopyRows<Teacher>(src, dst, target.Name, "teachers", t => t.Id, null);
            CopyRows<Course>(src, dst, target.Name, "courses", c => c.Id, null);
            CopyRows<Lesson>(src, dst, target.Name, "lessons", l => l.Id, null);
            CopyRows<Enrollment>(src, dst, target.Name, "enrollments", e => e.Id, null);
            CopyRows<Assignment>(src, dst, target.Name, "assignments", a => a.Id, null);
        }

        public List<string> Compare()
        {
            List<string> differences = new();

            using EducationDbContext primary = Primary.CreateContext();
            using EducationDbContext replica = Replica.CreateContext();

            CompareTable<Student>(primary, replica, "students", s => s.Id, differences);
            CompareTable<Teacher>(primary, replica, "teachers", t => t.Id, differences);
            CompareTable<Course>(primary, replica, "courses", c => c.Id, differences);
            CompareTable<Lesson>(primary, replica, "lessons", l => l.Id, differences);
            CompareTable<Enrollment>(primary, replica, "enrollments", e => e.Id, differences);
            CompareTable<Assignment>(primary, replica, "assignments", a => a.Id, differences);

            return differences;
        }

        public Response CompareReplicas()
        {
            Logger.Log("Compare -> START");

            if (!Primary.IsAvailable() || !Replica.IsAvailable())
                return Response.Error($"Сравнение невозможно: Primary {Primary.StateText}, Replica {Replica.StateText}");

            try
            {
                List<string> differences = Compare();

                using EducationDbContext primary = Primary.CreateContext();
                using EducationDbContext replica = Replica.CreateContext();

                List<Course> primaryCourses = primary.Courses.AsNoTracking().OrderBy(c => c.Id).ToList();
                Dictionary<int, Course> replicaCourses = replica.Courses.AsNoTracking().ToDictionary(c => c.Id);

                List<string> lines = new();
                foreach (Course course in primaryCourses)
                {
                    string replicaValue = replicaCourses.TryGetValue(course.Id, out Course? r)
                        ? r.AvailableStudents.ToString()
                        : "нет";

                    lines.Add($"-- Курс {course.Id} '{course.Title}': Primary Available = {course.AvailableStudents} | Replica Available = {replicaValue}");
                }

                string result = differences.Count == 0 ? "Primary = Replica" : $"Primary != Replica, расхождений: {differences.Count}";
                Logger.Log($"Compare -> {result}");

                foreach (string diff in differences)
                    Logger.Log($"  {diff}");

                return Response.Ok(message:
                    $"{result}\n" +
                    string.Join("\n", lines) +
                    string.Concat(differences.Select(d => $"\n--   {d}")));
            }
            catch (Exception ex)
            {
                Logger.Log($"Compare -> FAIL ({ex.Message})");
                return Response.Error($"Ошибка чтения данных: {ex.Message}");
            }
        }

        public Response GetStatus()
        {
            InstanceState primaryState = Primary.CheckState();
            InstanceState replicaState = Replica.CheckState();

            bool primaryOutdated = PrimaryOutdated;
            bool replicaOutdated = ReplicaOutdated;

            string status =
                $"Primary -> {Primary.StateText}{(primaryOutdated ? " (данные устарели)" : "")}\n" +
                $"-- Replica -> {Replica.StateText}{(replicaOutdated ? " (данные устарели)" : "")}\n";

            if (primaryState == InstanceState.Unavailable && replicaState == InstanceState.Unavailable)
            {
                Logger.Log("Status -> FAIL (оба экземпляра недоступны)");
                return Response.Error(status + "-- Сервис временно недоступен: Primary и Replica недоступны");
            }

            string active = primaryState == InstanceState.Available && !primaryOutdated
                ? Primary.Name
                : Replica.Name;

            return Response.Ok(message: status + $"-- Рабочий экземпляр: {active}");
        }

        private static void CopyRows<T>(EducationDbContext source, EducationDbContext target, string targetName,
            string table, Func<T, int> getId, ICollection<int>? ids) where T : class
        {
            List<T> sourceRows = source.Set<T>().AsNoTracking().AsEnumerable()
                .Where(r => ids == null || ids.Contains(getId(r)))
                .ToList();

            if (sourceRows.Count == 0)
                return;

            Dictionary<int, T> targetRows = target.Set<T>().AsEnumerable().ToDictionary(getId);
            bool hasInserts = false;

            foreach (T row in sourceRows)
            {
                int id = getId(row);

                if (targetRows.TryGetValue(id, out T? existing))
                {
                    EntityEntry<T> entry = target.Entry(existing);
                    entry.CurrentValues.SetValues(row);

                    if (entry.State == EntityState.Modified)
                        Logger.Log($"{targetName} -> UPDATE {table} id={id}");
                }
                else
                {
                    target.Set<T>().Add(row);
                    hasInserts = true;
                    Logger.Log($"{targetName} -> INSERT {table} id={id}");
                }
            }

            SaveRows(target, table, hasInserts);
        }

        private static void DeleteMissing<T>(EducationDbContext source, EducationDbContext target, string targetName,
            string table, Func<T, int> getId) where T : class
        {
            HashSet<int> sourceIds = source.Set<T>().AsNoTracking().AsEnumerable().Select(getId).ToHashSet();
            List<T> extraRows = target.Set<T>().AsEnumerable().Where(r => !sourceIds.Contains(getId(r))).ToList();

            if (extraRows.Count == 0)
                return;

            foreach (T row in extraRows)
                Logger.Log($"{targetName} -> DELETE {table} id={getId(row)}");

            target.Set<T>().RemoveRange(extraRows);
            target.SaveChanges();
        }

        private static void SaveRows(EducationDbContext target, string table, bool hasInserts)
        {
            if (!hasInserts || !target.Database.IsRelational())
            {
                target.SaveChanges();
                return;
            }

            string identityCheck = $"IF OBJECTPROPERTY(OBJECT_ID(N'dbo.{table}'), 'TableHasIdentity') = 1 ";

            using var transaction = target.Database.BeginTransaction();

            target.Database.ExecuteSqlRaw(identityCheck + $"SET IDENTITY_INSERT dbo.{table} ON;");
            target.SaveChanges();
            target.Database.ExecuteSqlRaw(identityCheck + $"SET IDENTITY_INSERT dbo.{table} OFF;");

            transaction.Commit();
        }

        private static void CompareTable<T>(EducationDbContext primary, EducationDbContext replica,
            string table, Func<T, int> getId, List<string> differences) where T : class
        {
            Dictionary<int, T> primaryRows = primary.Set<T>().AsNoTracking().AsEnumerable().ToDictionary(getId);
            Dictionary<int, T> replicaRows = replica.Set<T>().AsNoTracking().AsEnumerable().ToDictionary(getId);

            foreach (int id in primaryRows.Keys.Union(replicaRows.Keys).OrderBy(x => x))
            {
                if (!replicaRows.ContainsKey(id))
                {
                    differences.Add($"{table} id={id}: есть только в Primary");
                }
                else if (!primaryRows.ContainsKey(id))
                {
                    differences.Add($"{table} id={id}: есть только в Replica");
                }
                else
                {
                    List<string> fields = DiffFields(primaryRows[id], replicaRows[id]);
                    if (fields.Count > 0)
                        differences.Add($"{table} id={id}: {string.Join(", ", fields)}");
                }
            }
        }

        private static List<string> DiffFields<T>(T primary, T replica)
        {
            List<string> fields = new();

            foreach (PropertyInfo property in typeof(T).GetProperties())
            {
                object? primaryValue = property.GetValue(primary);
                object? replicaValue = property.GetValue(replica);

                if (!Equals(primaryValue, replicaValue))
                    fields.Add($"{property.Name} (Primary = {primaryValue}, Replica = {replicaValue})");
            }

            return fields;
        }
    }
}