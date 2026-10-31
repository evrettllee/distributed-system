using Server.Common;
using Server.Models;

namespace Server.Consensus
{
    public class Node
    {
        private readonly Lock _lock = new();
        private readonly List<Node> _peers = new();

        private NodeStatus _status = NodeStatus.Available;
        private NodeRole _role = NodeRole.Follower;
        private bool _forceVoteNo;

        public int Id { get; }
        public string Name => $"Node {Id}";

        public NodeStatus Status
        {
            get { lock (_lock) return _status; }
        }

        public NodeRole Role
        {
            get { lock (_lock) return _role; }
        }

        public bool ForceVoteNo
        {
            get { lock (_lock) return _forceVoteNo; }
        }

        public bool IsAvailable => Status == NodeStatus.Available;

        public IReadOnlyList<Node> Peers
        {
            get { lock (_lock) return _peers.ToList(); }
        }

        public Node(int id)
        {
            if (id <= 0)
                throw new ArgumentException("ID узла должен быть положительным числом");

            Id = id;
        }

        public string StateText => $"{Status.ToString().ToUpper()} {Role.ToString().ToUpper()}";

        public void AddPeer(Node peer)
        {
            lock (_lock)
            {
                if (peer == this || _peers.Any(p => p.Id == peer.Id))
                    return;

                _peers.Add(peer);
            }
        }

        public void SetStatus(NodeStatus status)
        {
            lock (_lock)
                _status = status;

            Logger.Log(Name, $"STATE -> {status.ToString().ToUpper()}");
        }

        public void SetRole(NodeRole role)
        {
            bool changed;

            lock (_lock)
            {
                changed = _role != role;
                _role = role;
            }

            if (changed)
                Logger.Log(Name, $"ROLE -> {role.ToString().ToUpper()}");
        }

        public void SetForceVoteNo(bool value)
        {
            lock (_lock)
                _forceVoteNo = value;

            Logger.Log(Name, value ? "VOTE MODE -> ALWAYS NO" : "VOTE MODE -> NORMAL");
        }

        public NodeReply Send(Node target, NodeMessage message)
        {
            if (!IsAvailable)
            {
                Logger.Log(Name, $"FAILED, отправка сообщения {message.Type} невозможна");
                return NodeReply.NoResponse();
            }

            try
            {
                return target.Receive(message);
            }
            catch (Exception ex)
            {
                Logger.Log(Name, $"не удалось связаться с {target.Name} ({ex.Message})");
                return NodeReply.NoResponse();
            }
        }

        public NodeReply Receive(NodeMessage? message)
        {
            lock (_lock)
            {
                if (_status == NodeStatus.Failed)
                    return NodeReply.NoResponse();

                string? error = Validate(message);
                if (error != null)
                {
                    Logger.Log(Name, $"INVALID MESSAGE ({error})");
                    return NodeReply.Invalid(error);
                }

                return message!.Type switch
                {
                    MessageType.Heartbeat => NodeReply.Alive(),
                    MessageType.Propose => NodeReply.WithVote(Vote(message.Proposal!)),
                    _ => NodeReply.Invalid("неизвестный тип сообщения")
                };
            }
        }

        private string? Validate(NodeMessage? message)
        {
            if (message == null)
                return "пустое сообщение";

            if (!Enum.IsDefined(message.Type))
                return $"неизвестный тип сообщения {(int)message.Type}";

            if (message.FromId <= 0)
                return $"некорректный ID отправителя {message.FromId}";

            if (message.Type == MessageType.Propose)
            {
                if (message.Proposal == null)
                    return "предложение без значения";

                if (!Enum.IsDefined(message.Proposal.Proposed) || !Enum.IsDefined(message.Proposal.Current))
                    return "некорректное значение CourseStatus";

                if (message.Proposal.CourseId <= 0)
                    return $"некорректный Id курса {message.Proposal.CourseId}";
            }

            return null;
        }

        private VoteResult Vote(Proposal proposal)
        {
            if (_forceVoteNo)
                return VoteResult.No;

            return CourseStatusRules.CanChange(proposal.Current, proposal.Proposed)
                ? VoteResult.Yes
                : VoteResult.No;
        }
    }
}