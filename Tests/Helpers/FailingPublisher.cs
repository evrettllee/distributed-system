using Server.Events;
using Server.PubSub;

namespace Tests.Helpers
{
    public class FailingPublisher : IPublisher
    {
        public IReadOnlyList<string> Errors => [];

        public PublishResult Publish(Event evt) =>
            throw new InvalidOperationException("Publisher недоступен");
    }
}
