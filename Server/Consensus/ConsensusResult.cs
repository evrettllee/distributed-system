namespace Server.Consensus
{
    public class ConsensusResult
    {
        public Proposal Proposal { get; }
        public int? LeaderId { get; }
        public IReadOnlyDictionary<int, VoteResult> Votes { get; }
        public int Majority { get; }
        public int TotalNodes { get; }
        public string? Error { get; }

        public int Yes => Votes.Values.Count(v => v == VoteResult.Yes);
        public int No => Votes.Values.Count(v => v == VoteResult.No);
        public int NoResponse => Votes.Values.Count(v => v == VoteResult.NoResponse);
        public bool Achieved => Error == null && Yes >= Majority;

        public ConsensusResult(Proposal proposal, int? leaderId, IReadOnlyDictionary<int, VoteResult> votes, int majority, int totalNodes, string? error = null)
        {
            Proposal = proposal;
            LeaderId = leaderId;
            Votes = votes;
            Majority = majority;
            TotalNodes = totalNodes;
            Error = error;
        }

        public string ToText()
        {
            List<string> lines = new();

            if (LeaderId == null)
            {
                lines.Add($"Лидер не выбран: {Error}");
                lines.Add("CONSENSUS NOT ACHIEVED");
                return string.Join("\n", lines);
            }

            lines.Add($"Leader Node {LeaderId} -> PROPOSE {Proposal}");

            foreach (var (id, vote) in Votes.OrderBy(v => v.Key))
                lines.Add($"-- Node {id} -> {VoteText(vote)}");

            lines.Add($"-- YES = {Yes}, NO = {No}, NO RESPONSE = {NoResponse}");
            lines.Add($"-- Majority = {Majority} из {TotalNodes}");
            lines.Add(Achieved ? "-- CONSENSUS ACHIEVED" : "-- CONSENSUS NOT ACHIEVED");

            return string.Join("\n", lines);
        }

        public static string VoteText(VoteResult vote) => vote switch
        {
            VoteResult.Yes => "YES",
            VoteResult.No => "NO",
            _ => "NO RESPONSE"
        };
    }
}