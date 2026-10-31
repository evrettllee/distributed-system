using Server.Common;
using Server.PubSub;
using Server.Subscribers;

namespace Server.Startup
{
    public class PubSubSetup
    {
        public IReadOnlyList<Broker> Brokers { get; }
        public Publisher Publisher { get; }
        public IReadOnlyDictionary<string, Func<ISubscriber>> Catalog { get; }

        private PubSubSetup(IReadOnlyList<Broker> brokers, Publisher publisher, IReadOnlyDictionary<string, Func<ISubscriber>> catalog)
        {
            Brokers = brokers;
            Publisher = publisher;
            Catalog = catalog;
        }

        public static PubSubSetup Create(TimeSpan queueInterval)
        {
            Broker broker1 = new("Broker-1");
            Broker broker2 = new("Broker-2");
            Broker[] brokers = [broker1, broker2];

            broker1.AddPeer(broker2);
            broker2.AddPeer(broker1);

            ISubscriber[] subscribers = [new LoggerSubscriber(), new CourseService(), new NotificationService()];

            foreach (Broker broker in brokers)
                foreach (ISubscriber subscriber in subscribers)
                    subscriber.ConnectTo(broker);

            Dictionary<string, Func<ISubscriber>> catalog = new()
            {
                ["CertificateService"] = () => new CertificateService(),
                ["AnalyticsService"] = () => new AnalyticsService()
            };

            foreach (Broker broker in brokers)
                broker.StartQueueProcessing(queueInterval);

            Logger.Log($"Pub/Sub: Publisher -> {string.Join(" -> ", brokers.Select(b => b.Name))}, " +
                       $"репликация очереди {string.Join(" <-> ", brokers.Select(b => b.Name))}, " +
                       $"каталог (не подключены): {string.Join(", ", catalog.Keys)}");

            return new PubSubSetup(brokers, new Publisher(broker1, broker2), catalog);
        }
    }
}
