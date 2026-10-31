using Server.Events;

namespace Server.PubSub
{
    public record PendingDelivery(Event Event, ISubscriber Subscriber, string LastError, DateTime EnqueuedAt, string Owner);
}