using Server.Events;

namespace Server.Subscribers
{
    public class NotificationService : SubscriberBase
    {
        private readonly List<string> _notifications = new();
        private readonly Lock _lock = new();

        public NotificationService()
            : base("NotificationService", TimeSpan.FromSeconds(2),
                EventTypes.StudentEnrolled, EventTypes.AssignmentSubmitted)
        {
        }

        public IReadOnlyList<string> Notifications
        {
            get { lock (_lock) return _notifications.ToList(); }
        }

        protected override Task<string> ProcessAsync(Event evt)
        {
            string text = evt.EventType switch
            {
                EventTypes.StudentEnrolled =>
                    $"Студенту {evt.Data.GetValueOrDefault("StudentName")}: вы записаны на курс '{evt.Data.GetValueOrDefault("CourseTitle")}'",
                EventTypes.AssignmentSubmitted =>
                    $"Преподавателю: студент id={evt.Data.GetValueOrDefault("StudentId")} сдал задание '{evt.Data.GetValueOrDefault("AssignmentTitle")}'",
                _ => throw new InvalidOperationException($"нет шаблона уведомления для {evt.EventType}")
            };

            lock (_lock)
                _notifications.Add(text);

            return Task.FromResult($"уведомление сформировано: {text}");
        }
    }
}