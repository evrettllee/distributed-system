using Server.Common;
using Server.Events;

namespace Server.PubSub
{
    public class Publisher : IPublisher
    {
        private readonly IReadOnlyList<IBroker> _brokers;
        private readonly List<string> _errors = new();
        private readonly Lock _lock = new();

        public Publisher(params IBroker[] brokers)
        {
            if (brokers.Length == 0)
                throw new ArgumentException("Publisher должен знать хотя бы один Broker");

            _brokers = brokers;
        }

        public IReadOnlyList<string> Errors
        {
            get { lock (_lock) return _errors.ToList(); }
        }

        public PublishResult Publish(Event evt)
        {
            List<string> failures = new();

            foreach (IBroker broker in _brokers)
            {
                PublishResult result;
                try
                {
                    result = broker.Publish(evt);
                }
                catch (BrokerUnavailableException ex)
                {
                    failures.Add(ex.Message);
                    Logger.Log("Publisher", $"{broker.Name} -> UNAVAILABLE, переключение на следующий Broker", evt.ClientId);
                    continue;
                }
                catch (Exception ex)
                {
                    failures.Add($"{broker.Name}: {ex.Message}");
                    Logger.Log("Publisher", $"{broker.Name} -> ERROR ({ex.Message}), переключение на следующий Broker", evt.ClientId);
                    continue;
                }

                if (!result.Success)
                {
                    RegisterError(evt, result.Message);
                    return result;
                }

                string failover = failures.Count > 0 ? " (FAILOVER)" : "";
                Logger.Log("Publisher", $"PUBLISH {evt} -> {result.Message}{failover}", evt.ClientId);
                return result;
            }

            PublishResult fail = PublishResult.Fail($"все Broker недоступны ({string.Join("; ", failures)})");
            RegisterError(evt, fail.Message);
            return fail;
        }

        private void RegisterError(Event evt, string message)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {evt}: {message}";

            lock (_lock)
                _errors.Add(entry);

            Logger.Log("Publisher", $"PUBLISH {evt} -> FAIL ({message})", evt.ClientId);
        }
    }
}