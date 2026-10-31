using Server.Common;
using Server.Consensus;
using Server.Data;
using Server.Models;
using Server.Replication;
using Shared;
using System.Text.Json;

namespace Server.Services
{
    public class ConsensusService
    {
        private readonly Cluster _cluster;
        private readonly ReplicationManager _replication;

        private static readonly Lock _statusLock = new();

        public ConsensusService(Cluster cluster, ReplicationManager replication)
        {
            _cluster = cluster;
            _replication = replication;
        }

        private static string NodeLine(Node node) =>
            $"-- {node.Name,-7} {node.Status.ToString().ToUpper(),-10} {node.Role.ToString().ToUpper(),-9}{(node.ForceVoteNo ? " | голосует NO" : "")}";

        public Response GetNodes()
        {
            Node? leader = _cluster.Leader;
            int available = _cluster.AvailableNodes().Count;

            List<string> lines = new()
            {
                $"Узлов: {_cluster.Nodes.Count}, доступно: {available}, Majority = {_cluster.Majority}",
                $"-- Лидер: {(leader != null ? leader.Name : "не выбран")}",
                $"-- Консенсус {(available >= _cluster.Majority ? "возможен" : "невозможен")}"
            };
            lines.AddRange(_cluster.Nodes.Select(NodeLine));

            string names = JsonSerializer.Serialize(_cluster.Nodes.Select(n => n.Name).ToArray());

            return Response.Ok(names, string.Join("\n", lines));
        }

        public Response SetNodeState(string name, bool available)
        {
            Node? node = _cluster.FindNode(name);
            if (node == null)
                return Response.Error($"Узел '{name}' не найден");

            NodeStatus status = available ? NodeStatus.Available : NodeStatus.Failed;
            if (node.Status == status)
                return Response.Error($"{node.Name} уже в состоянии {status.ToString().ToUpper()}");

            _cluster.SetNodeStatus(node, status);

            Node? leader = _cluster.Leader;
            string message = $"{node.Name} -> {status.ToString().ToUpper()}\n-- Текущий лидер: {(leader != null ? leader.Name : "не выбран")}";

            if (!available && node.Role == NodeRole.Leader)
                message += "\n-- Отказ лидера будет обнаружен по heartbeat";

            return Response.Ok(message: message);
        }

        public Response SetNodeVote(string name, bool voteNo)
        {
            Node? node = _cluster.FindNode(name);
            if (node == null)
                return Response.Error($"Узел '{name}' не найден");

            node.SetForceVoteNo(voteNo);

            return Response.Ok(message: $"{node.Name} -> {(voteNo ? "всегда голосует NO" : "голосует по правилам")}");
        }

        public Response RunElection()
        {
            Node? leader = _cluster.RunElection();

            if (leader == null)
                return Response.Error("Лидер не выбран: нет доступных узлов");

            List<string> lines = new() { $"{leader.Name} elected as LEADER" };
            lines.AddRange(_cluster.Nodes.Select(NodeLine));

            return Response.Ok(message: string.Join("\n", lines));
        }

        public Response CheckHeartbeat()
        {
            List<string> lines = _cluster.CheckHeartbeat();
            Node? leader = _cluster.Leader;

            lines.Add($"Текущий лидер: {(leader != null ? leader.Name : "не выбран")}");

            return Response.Ok(message: "Проверка heartbeat\n" + string.Join("\n", lines.Select(l => $"-- {l}")));
        }

        public Task<Response> ProposeCourseStatus(int courseId, string status) =>
            Task.FromResult(ProposeCourseStatusCore(courseId, status));

        private Response ProposeCourseStatusCore(int courseId, string status)
        {
            if (courseId <= 0)
                return Response.Error("Некорректный запрос: Id курса должен быть положительным числом");

            if (!Enum.TryParse(status, true, out CourseStatus newStatus) || !Enum.IsDefined(newStatus) || int.TryParse(status, out _))
                return Response.Error($"Неизвестный статус курса: {status}");

            Logger.Log($" START ProposeCourseStatus");

            lock (_statusLock)
            {
                DataInstance? instance = _replication.SelectInstance();
                if (instance == null)
                    return Response.Error("Сервис временно недоступен: Primary и Replica недоступны");

                try
                {
                    using EducationDbContext db = instance.CreateContext();

                    Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                    if (course == null)
                        return Response.Error($"Курс с id {courseId} не найден");

                    Proposal proposal = new(course.Id, course.Status, newStatus);
                    ConsensusResult result = _cluster.Propose(proposal);

                    if (!result.Achieved)
                    {
                        Logger.Log($" Статус курса не изменён");
                        return Response.Error(result.ToText() + $"\n-- Статус курса '{course.Title}' не изменён: {course.Status}");
                    }

                    using var transaction = db.Database.BeginTransaction();
                    try
                    {
                        Logger.Log($" {instance.Name} -> UPDATE");
                        Logger.Log($" BEGIN TRANSACTION");

                        course.Status = newStatus;
                        db.Courses.Update(course);
                        db.SaveChanges();

                        transaction.Commit();
                        Logger.Log($" COMMIT");
                        Logger.Log($" {instance.Name} -> SUCCESS (Status = {course.Status})");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Logger.Log($" ROLLBACK: {ex.Message}");
                        return Response.Error($"Консенсус достигнут, но сохранение отменено: {ex.Message}");
                    }