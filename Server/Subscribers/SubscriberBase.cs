using Server.Common;
using Server.Events;
using Server.PubSub;
using System.Collections.Concurrent;

namespace Server.Subscribers
{
    public abstract class SubscriberBase : ISubscriber
    {
        private const int UnstableFailures = 2;

        private readonly ConcurrentDictionary<Guid, int> _unstableAttempts = new();
        private readonly ConcurrentDictionary<Guid, byte> _handledEvents = new();
        private volatile SubscriberState _state = SubscriberState.Available;

        public string Name { get; }
        public IReadOnlyList<string> Subscriptions { get; }
        public TimeSpan ProcessingDelay { get; }
        public SubscriberState State => _state;

        protected SubscriberBase(string name, TimeSpan processingDelay, params string[] eventTypes)
        {
            Name = name;
            ProcessingDelay = processingDelay;
            Subscriptions = eventTypes;
        }

        public void SetState(SubscriberState state)
        {
            _state = state;
            Logger.Log(Name, $"STATE -> {state.ToString().ToUpper()}");
        }

        public int ConnectTo(IBroker broker)
        {
            List<string> subscribed = new();
            foreach (string eventType in Subscriptions)
            {
                if (broker.Subscribe(eventType, this))
                    subscribed.Add(eventType);
            }

            if (subscribed.Count > 0)
                Logger.Log(Name, $"SUBSCRIBE {broker.Name}: {string.Join(", ", subscribed)}");

            return subscribed.Count;
        }

        public async Task<string> HandleAsync(Event evt)
        {
            if (_state == SubscriberState.Unavailable)
                throw new SubscriberUnavailableException(Name);

            if (!Subscriptions.Contains(evt.EventType))
                throw new InvalidOperationException($"{Name} не подписан на {evt.EventType}");

            if (!_handledEvents.TryAdd(evt.Id, 0))
            {
                Logger.Log(Name, $"DUPLICATE {evt.Tag} событие уже обработано, повторная обработка пропущена", evt.ClientId);
                return "уже обработано ранее, пропуск";
            }

            try
            {
                await Task.Delay(ProcessingDelay);

                if (_state == SubscriberState.Failing)
                    throw new InvalidOperationException($"ошибка обработки события в {Name}");

                if (_state == SubscriberState.Unstable)
                {
                    int attempt = _unstableAttempts.AddOrUpdate(evt.Id, 1, (_, count) => count + 1);
                    if (attempt <= UnstableFailures)
                        throw new InvalidOperationException($"временный сбой {Name} (попытка {attempt})");

                    _unstableAttempts.TryRemove(evt.Id, out _);
                }

                return await ProcessAsync(evt);
            }
            catch
            {
                _handledEvents.TryRemove(evt.Id, out _);
                throw;
            }
        }

        protected abstract Task<string> ProcessAsync(Event evt);
    }
}