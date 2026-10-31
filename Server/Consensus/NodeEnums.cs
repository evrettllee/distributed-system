namespace Server.Consensus
{
    public enum NodeStatus
    {
        Available,
        Failed
    }

    public enum NodeRole
    {
        Follower,
        Leader
    }

    public enum VoteResult
    {
        Yes,
        No,
        NoResponse
    }

    public enum MessageType
    {
        Heartbeat,
        Propose
    }
}