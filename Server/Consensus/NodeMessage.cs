using Server.Models;

namespace Server.Consensus
{
    public class Proposal
    {
        public int CourseId { get; }
        public CourseStatus Current { get; }
        public CourseStatus Proposed { get; }

        public Proposal(int courseId, CourseStatus current, CourseStatus proposed)
        {
            CourseId = courseId;
            Current = current;
            Proposed = proposed;
        }

        public override string ToString() => $"CourseStatus Course={CourseId} {Current} -> {Proposed}";
    }

    public class NodeMessage
    {
        public MessageType Type { get; init; }
        public int FromId { get; init; }
        public Proposal? Proposal { get; init; }

        public static NodeMessage Heartbeat(int fromId) => new() { Type = MessageType.Heartbeat, FromId = fromId };

        public static NodeMessage Propose(int fromId, Proposal proposal) =>
            new() { Type = MessageType.Propose, FromId = fromId, Proposal = proposal };
    }

    public class NodeReply
    {
        public bool Responded { get; init; }
        public VoteResult Vote { get; init; } = VoteResult.NoResponse;
        public string? Error { get; init; }

        public static NodeReply NoResponse() => new() { Responded = false };

        public static NodeReply Alive() => new() { Responded = true };

        public static NodeReply WithVote(VoteResult vote) => new() { Responded = true, Vote = vote };

        public static NodeReply Invalid(string error) => new() { Responded = true, Vote = VoteResult.No, Error = error };
    }
}