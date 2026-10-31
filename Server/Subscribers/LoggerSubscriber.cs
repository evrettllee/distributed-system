using Server.Events;

namespace Server.Subscribers
{
    public class LoggerSubscriber : SubscriberBase
    {
        private readonly List<string> _journal = new();
        private readonly Lock _lock = new();

        public LoggerSubscriber()
            : base("Logger", TimeSpan.FromSeconds(1),
                EventTypes.StudentEnrolled, EventTypes.AssignmentSubmitted, EventTypes.CourseCompleted)
        {
        }

        public IReadOnlyList<string> Journal
        {
            get { lock (_lock) return _journal.ToList(); }
        }

        protected override Task<string> ProcessAsync(Event evt)
        {
            string entry = $"[{evt.Timestamp:HH:mm:ss.fff}] Client {evt.ClientId} | {evt.EventType} | EntityId={evt.EntityId} | {evt.DataText}";

            int count;
            lock (_lock)
            {
                _journal.Add(entry);
                count = _journal.Count;
            }

            return Task.FromResult($"записано в журнал, записей: {count}");
        }
    }
}