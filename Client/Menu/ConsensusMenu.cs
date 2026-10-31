using Client.Network;
using Client.UI;
using Shared;

namespace Client.Menu
{
    public static class ConsensusMenu
    {
        public static Response GetNodes(ServerClient client) => client.Send("GetNodes");

        public static Response RunElection(ServerClient client) => client.Send("RunElection");

        public static Response CheckHeartbeat(ServerClient client) => client.Send("CheckHeartbeat");

        public static Response SetNodeState(ServerClient client)
        {
            var (name, error) = ConsoleInput.SelectByNumber(client, "GetNodes", "Номер узла", "Нет узлов");
            if (error != null)
                return error;

            bool[] values = [true, false];
            if (!ConsoleInput.TryChoose("Состояние узла: 1 - Available, 2 - Failed", values, out bool available))
                return ConsoleInput.InvalidChoice(values.Length);

            return client.Send("SetNodeState", new { Name = name, Available = available });
        }

        public static Response SetNodeVote(ServerClient client)
        {
            var (name, error) = ConsoleInput.SelectByNumber(client, "GetNodes", "Номер узла", "Нет узлов");
            if (error != null)
                return error;

            bool[] values = [false, true];
            if (!ConsoleInput.TryChoose("Голосование: 1 - по правилам, 2 - всегда NO", values, out bool voteNo))
                return ConsoleInput.InvalidChoice(values.Length);

            return client.Send("SetNodeVote", new { Name = name, VoteNo = voteNo });
        }

        public static Response ProposeCourseStatus(ServerClient client)
        {
            int? courseId = ConsoleInput.ReadId("Id курса: ");
            if (courseId == null) return ConsoleInput.InvalidId();

            string[] statuses = ["Draft", "Active", "Closed"];
            if (!ConsoleInput.TryChoose("Новый статус: 1 - Draft, 2 - Active, 3 - Closed", statuses, out string status))
                return ConsoleInput.InvalidChoice(statuses.Length);

            return client.Send("ProposeCourseStatus", new { CourseId = courseId, Status = status });
        }
    }
}
