using Server.Events;
using Server.Subscribers;
using System.Collections.Concurrent;

namespace Tests.Helpers
{
    public class TestSubscriber : SubscriberBase
    {
        private readonly ConcurrentQueue<Event> _received = new();

        public TestSubscriber(string name, params string[] eventTypes)
            : base(name, TimeSpan.Zero, eventTypes)
        {
        }

        public IReadOnlyCollection<Event> Received => _received.ToArray();

        protected override Task<string> ProcessAsync(Event evt)
        {
            _received.Enqueue(evt);
            return Task.FromResult("ok");
        }
    }
}
