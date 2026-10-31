using Server.Events;

namespace Server.PubSub
{
    public interface IBroker
    {
        string Name { get; }
        bool IsAvailable { get; }
        int ReceivedCount { get; }
        int DeliveredCount { get; }
        int FailedCount { get; }
        int QueueCount { get; }
        void SetAvailable(bool available);
        void AddPeer(IBroker peer);
        bool Subscribe(string eventType, ISubscriber subscriber);
        IReadOnlyList<ISubscriber> GetSubscribers(string eventType);
        IReadOnlyList<ISubscriber> GetAllSubscribers();
        ISubscriber? FindSubscriber(string name);
        IReadOnlyDictionary<string, IReadOnlyList<string>> GetSubscriptionTable();
        IReadOnlyList<string> GetQueue();
        PublishResult Publish(Event evt);
        void AcceptReplica(PendingDelivery item);
        void RemoveReplica(Guid eventId, string subscriberName);
        Task<int> ProcessQueueAsync();
        void StartQueueProcessing(TimeSpan interval);
    }
}