using Server.Events;

namespace Server.Subscribers
{
    public class CertificateService : SubscriberBase
    {
        private readonly List<string> _certificates = new();
        private readonly Lock _lock = new();
        private int _counter;

        public CertificateService()
            : base("CertificateService", TimeSpan.FromSeconds(1.5), EventTypes.CourseCompleted)
        {
        }

        public IReadOnlyList<string> Certificates
        {
            get { lock (_lock) return _certificates.ToList(); }
        }

        protected override Task<string> ProcessAsync(Event evt)
        {
            string student = evt.Data.GetValueOrDefault("StudentName") ?? $"id={evt.Data.GetValueOrDefault("StudentId")}";
            string course = evt.Data.GetValueOrDefault("CourseTitle") ?? "неизвестный курс";

            string number;
            lock (_lock)
            {
                _counter++;
                number = $"CERT-{DateTime.Now:yyyy}-{_counter:D4}";
                _certificates.Add($"{number} | {student} | {course}");
            }

            return Task.FromResult($"сертификат {number} выдан: {student}, курс '{course}'");
        }
    }
}