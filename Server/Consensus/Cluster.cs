using Server.Common;

namespace Server.Consensus
{
    public class Cluster
    {
        private readonly List<Node> _nodes;
        private readonly Lock _clusterLock = new();
        private Node? _leader;
        private Timer? _heartbeatTimer;

        public IReadOnlyList<Node> Nodes => _nodes;
        public int Majority => _nodes.Count / 2 + 1;

        public Node? Leader
        {
            get { lock (_clusterLock) return _leader; }
        }

        public Cluster(IEnumerable<int> ids)
        {
            List<int> idList = ids.ToList();

            if (idList.Count == 0)
                throw new ArgumentException("Кластер должен содержать хотя бы один узел");

            int? duplicate = idList.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => (int?)g.Key).FirstOrDefault();
            if (duplicate != null)
                throw new ArgumentException($"ID узла {duplicate} не уникален");

            _nodes = idList.Select(id => new Node(id)).OrderBy(n => n.Id).ToList();

            foreach (Node node in _nodes)
                foreach (Node peer in _nodes)
                    node.AddPeer(peer);

            foreach (Node node in _nodes)
                Logger.Log(node.Name, node.StateText);
        }

        public Cluster(int nodeCount) : this(Enumerable.Range(1, nodeCount))
        {
        }

        public Node? FindNode(int id) => _nodes.FirstOrDefault(n => n.Id == id);

        public Node? FindNode(string name) =>
            _nodes.FirstOrDefault(n => n.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Node> AvailableNodes() => _nodes.Where(n => n.IsAvailable).ToList();

        public Node? RunElection(string title = "Election started")
        {
            lock (_clusterLock)
            {
                Logger.Log("Cluster", title);

                foreach (Node node in _nodes)
                {
                    Logger.Log(node.Name, node.Status.ToString().ToUpper());
                    if (!node.IsAvailable)
                        Logger.Log("Cluster", $"{node.Name} excluded");
                }

                Node? candidate = _nodes.Where(n => n.IsAvailable).MaxBy(n => n.Id);

                foreach (Node node in _nodes.Where(n => n != candidate))
                    node.SetRole(NodeRole.Follower);

                _leader = candidate;

                if (candidate == null)
                {
                    Logger.Log("Cluster", "нет доступных узлов, LEADER NOT ELECTED");
                    return null;
                }

                candidate.SetRole(NodeRole.Leader);
                Logger.Log("Cluster", $"{candidate.Name} elected as LEADER");

                return candidate;
            }
        }

        public void SetNodeStatus(Node node, NodeStatus status)
        {
            lock (_clusterLock)
            {
                node.SetStatus(status);

                if (status == NodeStatus.Available && (_leader == null || node.Id > _leader.Id))
                    RunElection($"Election started ({node.Name} восстановлен)");
            }
        }

        public List<string> CheckHeartbeat(bool verbose = true)
        {
            lock (_clusterLock)
            {
                List<string> lines = new();

                void Write(string source, string message, bool important = false)
                {
                    if (verbose || important)
                        Logger.Log(source, message);
                    lines.Add($"{source} -> {message}");
                }

                if (_leader == null)
                {
                    if (!verbose && !_nodes.Any(n => n.IsAvailable))
                        return lines;

                    Write("Cluster", "лидер отсутствует", true);
                    ElectNewLeader(lines);
                    return lines;
                }

                Node leader = _leader;
                Node? sender = _nodes.Where(n => n != leader && n.IsAvailable).MinBy(n => n.Id);

                if (sender == null)
                {
                    if (leader.IsAvailable)
                    {
                        Write("Cluster", $"{leader.Name} — единственный доступный узел");
                        return lines;
                    }

                    Write(leader.Name, "FAILED", true);
                    RunElection("New election started");
                    lines.Add("нет доступных узлов, LEADER NOT ELECTED");
                    return lines;
                }

                Write(sender.Name, $"HEARTBEAT -> {leader.Name}");
                NodeReply reply = sender.Send(leader, NodeMessage.Heartbeat(sender.Id));

                if (!reply.Responded)
                {
                    Write(leader.Name, "NO RESPONSE", true);
                    Write(leader.Name, "HEARTBEAT TIMEOUT", true);
                    Write(leader.Name, "FAILED", true);

                    ElectNewLeader(lines);
                    return lines;
                }

                Write(leader.Name, "ALIVE");

                foreach (Node follower in _nodes.Where(n => n != leader))
                {
                    NodeReply followerReply = leader.Send(follower, NodeMessage.Heartbeat(leader.Id));
                    if (followerReply.Responded)
                        Write(follower.Name, "ALIVE");
                    else
                        Write(follower.Name, "NO RESPONSE -> FAILED", true);
                }

                return lines;
            }
        }

        private void ElectNewLeader(List<string> lines)
        {
            Node? elected = RunElection("New election started");
            lines.Add(elected != null ? $"{elected.Name} elected as LEADER" : "LEADER NOT ELECTED");
        }

        public ConsensusResult Propose(Proposal proposal)
        {
            lock (_clusterLock)
            {
                if (_leader != null && !_leader.IsAvailable)
                {
                    Logger.Log(_leader.Name, "HEARTBEAT TIMEOUT");
                    Logger.Log(_leader.Name, "FAILED");
                    RunElection("New election started");
                }

                if (_leader == null)
                    RunElection("New election started");

                Node? leader = _leader;
                if (leader == null)
                {
                    Logger.Log("Cluster", "CONSENSUS NOT ACHIEVED (нет лидера)");
                    return new ConsensusResult(proposal, null, new Dictionary<int, VoteResult>(), Majority, _nodes.Count,
                        "нет доступных узлов");
                }

                Logger.Log("Cluster", $"Leader {leader.Name} -> PROPOSE {proposal}");

                NodeMessage message = NodeMessage.Propose(leader.Id, proposal);
                Dictionary<int, VoteResult> votes = new();

                foreach (Node node in _nodes)
                {
                    if (node != leader)
                        Logger.Log("Cluster", $"Leader {leader.Name} -> {node.Name}");

                    NodeReply reply = node == leader ? node.Receive(message) : leader.Send(node, message);
                    VoteResult vote = reply.Responded ? reply.Vote : VoteResult.NoResponse;

                    votes[node.Id] = vote;
                    Logger.Log(node.Name, ConsensusResult.VoteText(vote));
                }

                ConsensusResult result = new(proposal, leader.Id, votes, Majority, _nodes.Count);

                Logger.Log("Cluster", $"YES = {result.Yes}");
                Logger.Log("Cluster", $"NO = {result.No}");
                Logger.Log("Cluster", $"NO RESPONSE = {result.NoResponse}");
                Logger.Log("Cluster", $"Majority = {result.Majority}");
                Logger.Log("Cluster", result.Achieved ? "CONSENSUS ACHIEVED" : "CONSENSUS NOT ACHIEVED");

                return result;
            }
        }

        public void StartHeartbeatMonitor(TimeSpan interval)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = new Timer(_ =>
            {
                try
                {
                    CheckHeartbeat(verbose: false);
                }
                catch (Exception ex)
                {
                    Logger.Log("Cluster", $"ошибка проверки heartbeat: {ex.Message}");
                }
            }, null, interval, interval);
        }
    }
}