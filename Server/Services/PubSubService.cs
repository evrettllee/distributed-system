using Server.PubSub;
using Server.Subscribers;
using Shared;
using System.Text.Json;

namespace Server.Services
{
    public class PubSubService
    {
        private readonly IReadOnlyList<IBroker> _brokers;
        private readonly IPublisher _publisher;
        private readonly IReadOnlyDictionary<string, Func<ISubscriber>> _catalog;
        private readonly Lock _registerLock = new();

        public PubSubService(IReadOnlyList<IBroker> brokers, IPublisher publisher, IReadOnlyDictionary<string, Func<ISubscriber>> catalog)
        {
            _brokers = brokers;
            _publisher = publisher;
            _catalog = catalog;
        }

        private static string StateText(IBroker broker) => broker.IsAvailable ? "AVAILABLE" : "UNAVAILABLE";

        private IReadOnlyList<ISubscriber> AllSubscribers() =>
            _brokers.SelectMany(b => b.GetAllSubscribers()).DistinctBy(s => s.Name).ToList();

        private ISubscriber? FindSubscriber(string name) =>
            AllSubscribers().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private string BrokersOf(ISubscriber subscriber) =>
            string.Join(", ", _brokers.Where(b => b.FindSubscriber(subscriber.Name) != null).Select(b => b.Name));

        public Response GetSubscribers()
        {
            IReadOnlyList<ISubscriber> subscribers = AllSubscribers();

            if (subscribers.Count == 0)
                return Response.Ok(JsonSerializer.Serialize(Array.Empty<string>()), "Нет зарегистрированных подписчиков");

            IEnumerable<string> lines = subscribers.Select((s, i) =>
                $"-- {i + 1}. {s.Name,-20} {s.State.ToString().ToUpper(),-12} задержка {s.ProcessingDelay.TotalSeconds:F1} сек | подписки: {string.Join(", ", s.Subscriptions)} | Broker: {BrokersOf(s)}");

            string names = JsonSerializer.Serialize(subscribers.Select(s => s.Name).ToArray());

            return Response.Ok(names, $"Подписчиков: {subscribers.Count}\n" + string.Join("\n", lines));
        }

        public Response SetSubscriberState(string name, string state)
        {
            if (!Enum.TryParse(state, true, out SubscriberState newState) || !Enum.IsDefined(newState))
                return Response.Error($"Неизвестное состояние подписчика: {state}");

            ISubscriber? subscriber = FindSubscriber(name);
            if (subscriber == null)
                return Response.Error($"Subscriber '{name}' не зарегистрирован");

            subscriber.SetState(newState);

            return Response.Ok(message: $"{subscriber.Name} -> {newState.ToString().ToUpper()}");
        }

        public Response GetSubscriberCatalog()
        {
            string[] available = _catalog.Keys
                .Where(name => FindSubscriber(name) == null)
                .ToArray();

            if (available.Length == 0)
                return Response.Ok(JsonSerializer.Serialize(available), "Все подписчики из каталога уже зарегистрированы");

            IEnumerable<string> lines = available.Select((name, i) =>
            {
                ISubscriber preview = _catalog[name]();
                return $"-- {i + 1}. {name,-20} задержка {preview.ProcessingDelay.TotalSeconds:F1} сек | подписки: {string.Join(", ", preview.Subscriptions)}";
            });

            return Response.Ok(JsonSerializer.Serialize(available), $"Доступно для регистрации: {available.Length}\n" + string.Join("\n", lines));
        }

        public Response RegisterSubscriber(string name)
        {
            if (!_catalog.TryGetValue(name, out Func<ISubscriber>? factory))
                return Response.Error($"Неизвестный тип подписчика: {name}");

            List<IBroker> availableBrokers = _brokers.Where(b => b.IsAvailable).ToList();
            if (availableBrokers.Count == 0)
                return Response.Error("Регистрация невозможна: все Broker недоступны");

            lock (_registerLock)
            {
                if (FindSubscriber(name) != null)
                    return Response.Error($"Subscriber '{name}' уже зарегистрирован");

                ISubscriber subscriber = factory();

                List<string> connected = new();
                foreach (IBroker broker in availableBrokers)
                {
                    if (subscriber.ConnectTo(broker) > 0)
                        connected.Add(broker.Name);
                }

                if (connected.Count == 0)
                    return Response.Error($"{name}: не удалось оформить ни одной подписки");

                string message =
                    $"{subscriber.Name} зарегистрирован\n" +
                    $"-- Broker: {string.Join(", ", connected)}\n" +
                    $"-- Подписки: {string.Join(", ", subscriber.Subscriptions)}\n" +
                    $"-- Задержка обработки: {subscriber.ProcessingDelay.TotalSeconds:F1} сек";

                string skipped = string.Join(", ", _brokers.Where(b => !b.IsAvailable).Select(b => b.Name));
                if (skipped.Length > 0)
                    message += $"\n-- ВНИМАНИЕ: {skipped} недоступен, подписка будет оформлена при его восстановлении";

                return Response.Ok(message: message);
            }
        }

        public Response GetBrokers()
        {
            List<string> lines = new();

            for (int i = 0; i < _brokers.Count; i++)
            {
                IBroker broker = _brokers[i];

                lines.Add($"-- {i + 1}. {broker.Name,-10} {StateText(broker),-12} подписчиков {broker.GetAllSubscribers().Count} | " +
                          $"получено {broker.ReceivedCount}, доставлено {broker.DeliveredCount}, ошибок {broker.FailedCount} | в очереди {broker.QueueCount}");

                foreach (string item in broker.GetQueue())
                    lines.Add($"--      очередь: {item}");
            }

            string names = JsonSerializer.Serialize(_brokers.Select(b => b.Name).ToArray());

            return Response.Ok(names, $"Broker: {_brokers.Count}\n" + string.Join("\n", lines));
        }

        public Response SetBrokerState(string name, bool available)
        {
            IBroker? broker = _brokers.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (broker == null)
                return Response.Error($"Broker '{name}' не найден");

            broker.SetAvailable(available);

            string message = $"{broker.Name} -> {StateText(broker)}";

            if (available)
            {
                int synced = SyncSubscriptions(broker);
                if (synced > 0)
                    message += $"\n-- Синхронизировано подписок: {synced}";
            }

            return Response.Ok(message: message);
        }

        private int SyncSubscriptions(IBroker target)
        {
            lock (_registerLock)
            {
                int added = 0;

                foreach (ISubscriber subscriber in AllSubscribers())
                {
                    foreach (string eventType in subscriber.Subscriptions)
                    {
                        if (!target.GetSubscribers(eventType).Contains(subscriber) && target.Subscribe(eventType, subscriber))
                            added++;
                    }
                }

                return added;
            }
        }

        public async Task<Response> ProcessQueueAsync()
        {
            List<string> lines = new();
            int total = 0;

            foreach (IBroker broker in _brokers)
            {
                if (!broker.IsAvailable)
                {
                    lines.Add($"-- {broker.Name}: UNAVAILABLE, очередь ({broker.QueueCount}) не обработана");
                    continue;
                }

                int before = broker.QueueCount;
                int delivered = await broker.ProcessQueueAsync();
                total += delivered;

                lines.Add($"-- {broker.Name}: было в очереди {before}, доставлено {delivered}, осталось {broker.QueueCount}");
            }

            return Response.Ok(message: $"Повторная доставка из очереди: доставлено {total}\n" + string.Join("\n", lines));
        }

        public Response GetPublisherErrors()
        {
            IReadOnlyList<string> errors = _publisher.Errors;

            if (errors.Count == 0)
                return Response.Ok(message: "Ошибок публикации нет");

            return Response.Ok(message:
                $"Ошибок публикации: {errors.Count}\n" +
                string.Join("\n", errors.Select(e => $"-- {e}")));
        }

        public Response GetEventStats()
        {
            AnalyticsService? analytics = AllSubscribers().OfType<AnalyticsService>().FirstOrDefault();

            if (analytics == null)
                return Response.Error("AnalyticsService не зарегистрирован (пункт 13)");

            return Response.Ok(message: analytics.GetReport());
        }
    }
}