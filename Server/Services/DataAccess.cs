using Server.Common;
using Server.Data;
using Server.Replication;
using Shared;

namespace Server.Services
{
    internal static class DataAccess
    {
        public const string UnavailableMessage = "Сервис временно недоступен: Primary и Replica недоступны";

        public static Response Execute(ReplicationManager replication, Func<DataInstance, EducationDbContext, Response> action)
        {
            DataInstance? instance = replication.SelectInstance();
            if (instance == null)
                return Response.Error(UnavailableMessage);

            try
            {
                using EducationDbContext db = instance.CreateContext();
                return action(instance, db);
            }
            catch (Exception ex)
            {
                return DatabaseError(instance, ex);
            }
        }

        public static async Task<Response> ExecuteAsync(ReplicationManager replication, Func<DataInstance, EducationDbContext, Task<Response>> action)
        {
            DataInstance? instance = replication.SelectInstance();
            if (instance == null)
                return Response.Error(UnavailableMessage);

            try
            {
                using EducationDbContext db = instance.CreateContext();
                return await action(instance, db);
            }
            catch (Exception ex)
            {
                return DatabaseError(instance, ex);
            }
        }

        public static string? RunTransaction(EducationDbContext db, Action apply)
        {
            using var transaction = db.Database.BeginTransaction();
            try
            {
                apply();
                transaction.Commit();
                return null;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Logger.Log($"ROLLBACK: {ex.Message}");
                return ex.Message;
            }
        }

        public static string? RunAfterCommit(DataInstance instance, string step, Func<string> action, List<string> warnings)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Logger.Log($"{instance.Name} -> {step} FAIL после COMMIT ({ex.Message})");
                warnings.Add($"-- ВНИМАНИЕ: данные сохранены в {instance.Name}, но {step} не выполнена: {ex.Message}");
                return null;
            }
        }

        private static Response DatabaseError(DataInstance instance, Exception ex)
        {
            Logger.Log($"{instance.Name} -> FAIL ({ex.Message})");
            return Response.Error($"Ошибка работы с экземпляром {instance.Name}: {ex.Message}");
        }
    }
}
