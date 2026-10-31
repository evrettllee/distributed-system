using Server.Common;
using Server.Events;

namespace Server.PubSub
{
    public class Broker : IBroker
    {
        public const int MaxAttempts = 3;

        private readonly Dictionary<string, List<ISubscriber>> _subscriptions = new();
        private readonly Lock _lock = new();
        private readonly List<PendingDelivery> _queue = new();
        private readonly Lock _queueLock = new();
        private readonly List<IBroker> _peers = new();
        private readonly Lock _peersLock = new();
        private readonly SemaphoreSlim _queueProcessing = new(1, 1);
        private readonly TimeSpan _retryDelay;
        private volatile bool _isAvailable = true;
        private int _received;
        private int _delivered;
        private int _failed;

        public string Name { get; }
        public bool IsAvailable => _isAvailable;
        public int ReceivedCount => Volatile.Read(ref _received);
        public int DeliveredCount => Volatile.Read(ref _delivered);
        public int FailedCount => Volatile.Read(ref _failed);

        public int QueueCount
        {
            get { lock (_queueLock) return _queue.Count; }
        }

        public Broker(string name, TimeSpan? retryDelay = null)
        {
            Name = name;
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);

            foreach (string eventType in EventTypes.All)
                _subscriptions[eventType] = new();
        }

        public void SetAvailable(bool available)
        {
            _isAvailable = available;
            Logger.Log(Name, $"STATE -> {(available ? "AVAILABLE" : "UNAVAILABLE")}");
        }

        public void AddPeer(IBroker peer)
        {
            if (peer == this)
                return;

            lock (_peersLock)
            {
                if (_peers.Contains(peer))
                    return;

                _peers.Add(peer);
            }
        }

        public bool Subscribe(string eventType, ISubscriber subscriber)
        {
            if (!_isAvailable)
            {
                Logger.Log(Name, $"SUBSCRIBE FAIL ({subscriber.Name}: {Name} недоступен)");
                return false;
            }

            if (!EventTypes.IsKnown(eventType))
            {
                Logger.Log(Name, $"SUBSCRIBE FAIL ({subscriber.Name}: неизвестный тип события '{eventType}')");
                return false;
            }

            lock (_lock)
            {
                List<ISubscriber> list = _subscriptions[eventType];

                if (list.Any(s => s.Name == subscriber.Name))
                {
                    Logger.Log(Name, $"SUBSCRIBE SKIP ({subscriber.Name} уже подписан на {eventType})");
                    return false;
                }

                list.Add(subscriber);
            }

            return true;
        }

        public IReadOnlyList<ISubscriber> GetSubscribers(string eventType)
        {
            lock (_lock)
            {
                return _subscriptions.TryGetValue(eventType, out List<ISubscriber>? list)
                    ? list.ToList()
                    : [];
            }
        }

        public IReadOnlyList<ISubscriber> GetAllSubscribers()
        {
            lock (_lock)
            {
                return _subscriptions.Values
                    .SelectMany(list => list)
                    .DistinctBy(s => s.Name)
                    .ToList();
            }
        }

        public ISubscriber? FindSubscriber(string name) =>
            GetAllSubscribers().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyDictionary<string, IReadOnlyList<string>> GetSubscriptionTable()
        {
            lock (_lock)
            {
                return _subscriptions.ToDictionary(
                    kv => kv.Key,
                    kv => (IReadOnlyList<string>)kv.Value.Select(s => s.Name).ToList());
            }
        }

        public IReadOnlyList<string> GetQueue()
        {
            lock (_queueLock)
            {
                return _queue
                    .Select(p => $"{p.Subscriber.Name} <- {p.Event} | {(p.Owner == Name ? "своё" : $"копия от {p.Owner}")} | в очереди с {p.EnqueuedAt:HH:mm:ss} | {p.LastError}")
                    .ToList();
            }
        }

        public PublishResult Publish(Event evt)
        {
            if (!_isAvailable)
                throw new BrokerUnavailableException(Name);

            string? error = evt.Validate();
            if (error != null)
            {
                Logger.Log(Name, $"REJECT ({error})", evt.ClientId);
                return PublishResult.Fail($"некорректное событие: {error}", Name);
            }

            Interlocked.Increment(ref _received);

            IReadOnlyList<ISubscriber> subscribers = GetSubscribers(evt.EventType);

            if (subscribers.Count == 0)
                return PublishResult.Ok(Name, 0, Task.FromResult<IReadOnlyList<DeliveryResult>>([]));

            Task<IReadOnlyList<DeliveryResult>> delivery = Task.Run(() => DeliverAllAsync(evt, subscribers));

            return PublishResult.Ok(Name, subscribers.Count, delivery);
        }

        public void AcceptReplica(PendingDelivery item)
        {
            if (!_isAvailable)
                throw new BrokerUnavailableException(Name);

            TryAddToQueue(item);
        }

        public void RemoveReplica(Guid eventId, string subscriberName)
        {
            int removed;
            lock (_queueLock)
                removed = _queue.RemoveAll(p => p.Event.Id == eventId && p.Subscriber.Name == subscriberName);

            if (removed > 0)
                Logger.Log(Name, $"REPLICA REMOVED {subscriberName} [#{eventId.ToString()[..8]}] (доставлено другим Broker)");
        }

        public async Task<int> ProcessQueueAsync()
        {
            if (!_isAvailable || !await _queueProcessing.WaitAsync(0))
                return 0;

            try
            {
                List<PendingDelivery> ready;
                lock (_queueLock)
                {
                    ready = _queue
                        .Where(p => p.Subscriber.State != SubscriberState.Unavailable)
                        .Where(p => p.Owner == Name || !IsOwnerAvailable(p.Owner))
                        .ToList();
                }

                if (ready.Count == 0)
                    return 0;

                int delivered = 0;
                foreach (PendingDelivery item in ready)
                {
                    string label = "Queue";

                    if (item.Owner != Name)
                    {
                        label = $"Takeover({item.Owner})";
                        Logger.Log(Name, $"TAKEOVER {item.Subscriber.Name} {item.Event.Tag} (хозяин {item.Owner} недоступен)", item.Event.ClientId);
                    }

                    DeliveryResult result = await TryDeliverAsync(item.Event, item.Subscriber, label, DateTime.Now, 1);
                    if (!result.Success)
                        continue;

                    lock (_queueLock)
                        _queue.Remove(item);

                    RemoveFromPeers(item);
                    Interlocked.Increment(ref _delivered);
                    delivered++;
                }

                Logger.Log(Name, $"QUEUE PROCESSED (доставлено: {delivered} из {ready.Count}, осталось в очереди: {QueueCount})");
                return delivered;
            }
            finally
            {
                _queueProcessing.Release();
            }
        }

        public void StartQueueProcessing(TimeSpan interval)
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(interval);

                    try
                    {
                        await ProcessQueueAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(Name, $"QUEUE ERROR ({ex.Message})");
                    }
                }
            });
        }

        private async Task<IReadOnlyList<DeliveryResult>> DeliverAllAsync(Event evt, IReadOnlyList<ISubscriber> subscribers)
        {
            DeliveryResult[] results = await Task.WhenAll(
                subscribers.Select(s => Task.Run(() => DeliverAsync(evt, s))));

            int succeeded = results.Count(r => r.Success);
            Logger.Log(Name, $"DELIVERY COMPLETE {evt.Tag} (успешно: {succeeded}/{results.Length})", evt.ClientId);

            return results;
        }

        private async Task<DeliveryResult> DeliverAsync(Event evt, ISubscriber subscriber)
        {
            DateTime startedAt = DateTime.Now;
            DeliveryResult result = await TryDeliverAsync(evt, subscriber, $"Attempt 1/{MaxAttempts}", startedAt, 1);

            for (int attempt = 2; attempt <= MaxAttempts && !result.Success; attempt++)
            {
                await Task.Delay(_retryDelay);
                result = await TryDeliverAsync(evt, subscriber, $"Attempt {attempt}/{MaxAttempts}", startedAt, attempt);
            }

            if (result.Success)
            {
                Interlocked.Increment(ref _delivered);
            }
            else
            {
                Interlocked.Increment(ref _failed);
                Enqueue(evt, subscriber, result.Message);
            }

            return result;
        }

        private static async Task<DeliveryResult> TryDeliverAsync(Event evt, ISubscriber subscriber, string label, DateTime startedAt, int attempts)
        {
            try
            {
                string message = await subscriber.HandleAsync(evt);
                DeliveryResult result = DeliveryResult.Create(subscriber.Name, DeliveryStatus.Success, message, startedAt, attempts);
                Logger.Log(subscriber.Name, $"{label} {evt.Tag} -> SUCCESS {message} ({result.Duration.TotalSeconds:F3} сек)", evt.ClientId);
                return result;
            }
            catch (SubscriberUnavailableException ex)
            {
                Logger.Log(subscriber.Name, $"{label} {evt.Tag} -> UNAVAILABLE ({ex.Message})", evt.ClientId);
                return DeliveryResult.Create(subscriber.Name, DeliveryStatus.Unavailable, ex.Message, startedAt, attempts);
            }
            catch (Exception ex)
            {
                Logger.Log(subscriber.Name, $"{label} {evt.Tag} -> FAIL ({ex.Message})", evt.ClientId);
                return DeliveryResult.Create(subscriber.Name, DeliveryStatus.Failed, ex.Message, startedAt, attempts);
            }
        }

        private void Enqueue(Event evt, ISubscriber subscriber, string error)
        {
            PendingDelivery item = new(evt, subscriber, error, DateTime.Now, Name);

            if (!TryAddToQueue(item))
                return;

            Logger.Log(Name, $"QUEUE <- {subscriber.Name} {evt.Tag} (в очереди: {QueueCount})", evt.ClientId);

            ReplicateToPeers(item);
        }

        private bool TryAddToQueue(PendingDelivery item)
        {
            lock (_queueLock)
            {
                if (_queue.Any(p => p.Event.Id == item.Event.Id && p.Subscriber.Name == item.Subscriber.Name))
                    return false;

                _queue.Add(item);
                return true;
            }
        }

        private void ReplicateToPeers(PendingDelivery item)
        {
            foreach (IBroker peer in Peers())
            {
                if (!peer.IsAvailable)
                {
                    Logger.Log(Name, $"REPLICATE SKIP -> {peer.Name} недоступен, копия не создана", item.Event.ClientId);
                    continue;
                }

                try
                {
                    peer.AcceptReplica(item);
                    Logger.Log(Name, $"REPLICATE -> {peer.Name} {item.Subscriber.Name} {item.Event.Tag}", item.Event.ClientId);
                }
                catch (Exception ex)
                {
                    Logger.Log(Name, $"REPLICATE FAIL -> {peer.Name} ({ex.Message})", item.Event.ClientId);
                }
            }
        }

        private void RemoveFromPeers(PendingDelivery item)
        {
            foreach (IBroker peer in Peers())
            {
                if (!peer.IsAvailable)
                    continue;

                try
                {
                    peer.RemoveReplica(item.Event.Id, item.Subscriber.Name);
                }
                catch (Exception ex)
                {
                    Logger.Log(Name, $"REPLICA REMOVE FAIL -> {peer.Name} ({ex.Message})");
                }
            }
        }

        private bool IsOwnerAvailable(string owner)
        {
            if (owner == Name)
                return _isAvailable;

            IBroker? peer = Peers().FirstOrDefault(p => p.Name == owner);
            return peer?.IsAvailable ?? false;
        }

        private List<IBroker> Peers()
        {
            lock (_peersLock)
                return _peers.ToList();
        }
    }
}