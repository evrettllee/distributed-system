using Server.Events;

namespace Server.PubSub
{
    public interface IPublisher
    {
        IReadOnlyList<string> Errors { get; }
        PublishResult Publish(Event evt);
    }
}