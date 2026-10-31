namespace Server.PubSub
{
    public enum DeliveryStatus
    {
        Success,
        Failed,
        Unavailable
    }

    public class DeliveryResult
    {
        public string SubscriberName { get; init; } = string.Empty;
        public DeliveryStatus Status { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; }
        public DateTime FinishedAt { get; init; }
        public int Attempts { get; init; }

        public bool Success => Status == DeliveryStatus.Success;
        public TimeSpan Duration => FinishedAt - StartedAt;

        public static DeliveryResult Create(string subscriberName, DeliveryStatus status, string message, DateTime startedAt, int attempts) => new()
        {
            SubscriberName = subscriberName,
            Status = status,
            Message = message,
            StartedAt = startedAt,
            FinishedAt = DateTime.Now,
            Attempts = attempts
        };
    }
}