using Server.Events;

namespace Server.PubSub
{
    public interface ISubscriber
    {
        string Name { get; }
        IReadOnlyList<string> Subscriptions { get; }
        TimeSpan ProcessingDelay { get; }
        SubscriberState State { get; }
        void SetState(SubscriberState state);
        int ConnectTo(IBroker broker);
        Task<string> HandleAsync(Event evt);
    }
}