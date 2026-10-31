using Server.Common;

namespace Server.Events
{
    public class Event
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = string.Empty;
        public int EntityId { get; init; }
        public int ClientId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public Dictionary<string, string> Data { get; init; } = new();

        public string ShortId => Id.ToString()[..8];

        public string Tag => $"[{EventType}#{ShortId}]";

        public string DataText => string.Join(", ", Data.Select(kv => $"{kv.Key}={kv.Value}"));

        public static Event Create(string eventType, int entityId, Dictionary<string, string>? data = null) => new()
        {
            EventType = eventType,
            EntityId = entityId,
            ClientId = ClientContext.ClientId,
            Data = data ?? new()
        };

        public string? Validate()
        {
            if (string.IsNullOrWhiteSpace(EventType))
                return "не указан тип события";

            if (!EventTypes.IsKnown(EventType))
                return $"неизвестный тип события '{EventType}'";

            if (EntityId <= 0)
                return $"некорректный идентификатор объекта: {EntityId}";

            if (Timestamp == default)
                return "не указано время создания";

            return null;
        }

        public override string ToString() => $"{EventType}#{ShortId} (EntityId={EntityId})";
    }
}