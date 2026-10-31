using Server.Common;
using Server.Consensus;
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

        private string LeaderName => _cluster.Leader?.Name ?? "не выбран";

        private static string NodeLine(Node node) =>
            $"-- {node.Name,-7} {node.Status.ToString().ToUpper(),-10} {node.Role.ToString().ToUpper(),-9}{(node.ForceVoteNo ? " | голосует NO" : "")}";

        public Response GetNodes()
        {
            int available = _cluster.AvailableNodes().Count;

            List<string> lines = new()
            {
                $"Узлов: {_cluster.Nodes.Count}, доступно: {available}, Majority = {_cluster.Majority}",
                $"-- Лидер: {LeaderName}",
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

            string message = $"{node.Name} -> {status.ToString().ToUpper()}\n-- Текущий лидер: {LeaderName}";

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
            lines.Add($"Текущий лидер: {LeaderName}");

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

            lock (_statusLock)
            {
                return DataAccess.Execute(_replication, (instance, db) =>
                {
                    Course? course = db.Courses.FirstOrDefault(c => c.Id == courseId);
                    if (course == null)
                        return Response.Error($"Курс с id {courseId} не найден");

                    Proposal proposal = new(course.Id, course.Status, newStatus);
                    ConsensusResult result = _cluster.Propose(proposal);

                    if (!result.Achieved)
                        return Response.Error(result.ToText() + $"\n-- Статус курса '{course.Title}' не изменён: {course.Status}");

                    string? error = DataAccess.RunTransaction(db, () =>
                    {
                        course.Status = newStatus;
                        db.Courses.Update(course);
                        db.SaveChanges();
                    });

                    if (error != null)
                        return Response.Error($"Консенсус достигнут, но сохранение отменено: {error}");

                    Logger.Log($"{instance.Name} -> ProposeCourseStatus SUCCESS (Status = {course.Status})");

                    ReplicationChange change = new();
                    change.CourseIds.Add(course.Id);

                    List<string> warnings = new();
                    string? replication = DataAccess.RunAfterCommit(instance, "репликация",
                        () => _replication.ReplicateChange(instance, change), warnings);

                    List<string> lines = new()
                    {
                        result.ToText(),
                        $"-- Статус курса '{course.Title}': {proposal.Current} -> {course.Status}",
                        $"-- Экземпляр: {instance.Name}"
                    };

                    if (replication != null)
                        lines.Add($"-- Репликация: {replication}");

                    lines.AddRange(warnings);

                    return Response.Ok(message: string.Join("\n", lines));
                });
            }
        }
    }
}
