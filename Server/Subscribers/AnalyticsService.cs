using Server.Events;

namespace Server.Subscribers
{
    public class AnalyticsService : SubscriberBase
    {
        private readonly Dictionary<string, int> _byType = new();
        private readonly Dictionary<int, int> _byClient = new();
        private readonly Dictionary<string, int> _enrollmentsByCourse = new();
        private readonly HashSet<string> _students = new();
        private readonly List<double> _latencies = new();
        private readonly Lock _lock = new();
        private DateTime? _first;
        private DateTime? _last;
        private int _total;

        public AnalyticsService()
            : base("AnalyticsService", TimeSpan.FromSeconds(0.5),
                EventTypes.StudentEnrolled, EventTypes.AssignmentSubmitted, EventTypes.CourseCompleted)
        {
        }

        protected override Task<string> ProcessAsync(Event evt)
        {
            double latency = (DateTime.Now - evt.Timestamp).TotalSeconds;

            lock (_lock)
            {
                _total++;
                _byType[evt.EventType] = _byType.GetValueOrDefault(evt.EventType) + 1;
                _byClient[evt.ClientId] = _byClient.GetValueOrDefault(evt.ClientId) + 1;

                if (evt.EventType == EventTypes.StudentEnrolled)
                {
                    string course = evt.Data.GetValueOrDefault("CourseTitle") ?? "неизвестный курс";
                    _enrollmentsByCourse[course] = _enrollmentsByCourse.GetValueOrDefault(course) + 1;
                }

                string? studentId = evt.Data.GetValueOrDefault("StudentId");
                if (studentId != null)
                    _students.Add(studentId);

                _latencies.Add(latency);
                _first ??= evt.Timestamp;
                _last = evt.Timestamp;

                return Task.FromResult($"учтено событие №{_total}, задержка от создания {latency:F3} сек");
            }
        }

        public string GetReport()
        {
            lock (_lock)
            {
                if (_total == 0)
                    return "AnalyticsService: событий пока нет";

                List<string> lines = new() { $"Статистика AnalyticsService: всего событий {_total}" };

                lines.Add("-- По типам:");
                foreach (string type in EventTypes.All)
                    lines.Add($"--   {type,-20} {_byType.GetValueOrDefault(type)}");

                lines.Add("-- По клиентам:");
                foreach (var (client, count) in _byClient.OrderBy(kv => kv.Key))
                    lines.Add($"--   Client {client}: {count}");

                if (_enrollmentsByCourse.Count > 0)
                {
                    lines.Add("-- Записи по курсам:");
                    foreach (var (course, count) in _enrollmentsByCourse.OrderByDescending(kv => kv.Value))
                        lines.Add($"--   {course}: {count}");
                }

                lines.Add($"-- Уникальных студентов: {_students.Count}");
                lines.Add($"-- Задержка от создания до обработки: средняя {_latencies.Average():F3} сек, мин {_latencies.Min():F3}, макс {_latencies.Max():F3}");
                lines.Add($"-- Период событий: {_first:HH:mm:ss} – {_last:HH:mm:ss}");

                return string.Join("\n", lines);
            }
        }
    }
}