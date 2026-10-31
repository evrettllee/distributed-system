namespace Server.PubSub
{
    public class PublishResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? BrokerName { get; init; }
        public int SubscriberCount { get; init; }
        public Task<IReadOnlyList<DeliveryResult>> Delivery { get; init; } =
            Task.FromResult<IReadOnlyList<DeliveryResult>>([]);

        public static PublishResult Ok(string brokerName, int subscriberCount, Task<IReadOnlyList<DeliveryResult>> delivery) => new()
        {
            Success = true,
            BrokerName = brokerName,
            SubscriberCount = subscriberCount,
            Delivery = delivery,
            Message = subscriberCount > 0
                ? $"опубликовано через {brokerName}, подписчиков: {subscriberCount}"
                : $"опубликовано через {brokerName}, подписчиков нет"
        };

        public static PublishResult Fail(string message, string? brokerName = null) => new()
        {
            Success = false,
            BrokerName = brokerName,
            Message = message
        };
    }
}