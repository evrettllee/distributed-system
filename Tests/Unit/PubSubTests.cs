using Server.Events;
using Server.PubSub;
using Server.Subscribers;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class PubSubTests
    {
        private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(10);

        private static (Broker Broker, Publisher Publisher) CreatePubSub()
        {
            Broker broker = new("TestBroker", FastRetry);
            return (broker, new Publisher(broker));
        }

        private static Event CreateEvent(string eventType) =>
            Event.Create(eventType, 1, new()
            {
                ["StudentId"] = "1",
                ["StudentName"] = "Иванов Иван",
                ["CourseId"] = "1",
                ["CourseTitle"] = "Основы программирования"
            });

        // Тест 30 — корректное создание события
        [Fact]
        public void PubSub_Event_CreatedCorrectly()
        {
            Event evt = CreateEvent(EventTypes.StudentEnrolled);

            Assert.NotEqual(Guid.Empty, evt.Id);
            Assert.Equal(EventTypes.StudentEnrolled, evt.EventType);
            Assert.Equal(1, evt.EntityId);
            Assert.NotEqual(default, evt.Timestamp);
            Assert.Null(evt.Validate());
        }

        // Тест 31 — корректная публикация события
        [Fact]
        public void PubSub_Publish_ReturnsSuccess()
        {
            var (broker, publisher) = CreatePubSub();
            new TestSubscriber("A", EventTypes.StudentEnrolled).ConnectTo(broker);

            PublishResult result = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));

            Assert.True(result.Success);
            Assert.Equal("TestBroker", result.BrokerName);
            Assert.Equal(1, result.SubscriberCount);
        }

        // Тест 32 — регистрация Subscriber
        [Fact]
        public void PubSub_Subscribe_RegistersSubscriber()
        {
            var (broker, _) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);

            subscriber.ConnectTo(broker);

            Assert.Contains(subscriber, broker.GetSubscribers(EventTypes.StudentEnrolled));
        }

        // Тест 33 — получение события подписанным Subscriber
        [Fact]
        public async Task PubSub_SubscribedSubscriber_ReceivesEvent()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Single(subscriber.Received);
        }

        // Тест 34 — отсутствие доставки неподписанному Subscriber
        [Fact]
        public async Task PubSub_UnsubscribedSubscriber_DoesNotReceiveEvent()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.CourseCompleted);
            subscriber.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Empty(subscriber.Received);
        }

        // Тест 35 — доставка одного события нескольким Subscriber
        [Fact]
        public async Task PubSub_OneEvent_DeliveredToSeveralSubscribers()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber a = new("A", EventTypes.StudentEnrolled);
            TestSubscriber b = new("B", EventTypes.StudentEnrolled);
            TestSubscriber c = new("C", EventTypes.StudentEnrolled);
            a.ConnectTo(broker);
            b.ConnectTo(broker);
            c.ConnectTo(broker);

            IReadOnlyList<DeliveryResult> results = await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Equal(3, results.Count(r => r.Success));
            Assert.Single(a.Received);
            Assert.Single(b.Received);
            Assert.Single(c.Received);
        }

        // Тест 36 — обработка нескольких типов событий
        [Fact]
        public async Task PubSub_SeveralEventTypes_RoutedCorrectly()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber logger = new("Logger", EventTypes.StudentEnrolled, EventTypes.AssignmentSubmitted, EventTypes.CourseCompleted);
            TestSubscriber course = new("CourseService", EventTypes.StudentEnrolled, EventTypes.CourseCompleted);
            logger.ConnectTo(broker);
            course.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.AssignmentSubmitted)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.CourseCompleted)).Delivery;

            Assert.Equal(3, logger.Received.Count);
            Assert.Equal(2, course.Received.Count);
            Assert.DoesNotContain(course.Received, e => e.EventType == EventTypes.AssignmentSubmitted);
        }

        // Тест 37 — корректная обработка ошибки Subscriber
        [Fact]
        public async Task PubSub_SubscriberError_HandledCorrectly()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            subscriber.SetState(SubscriberState.Failing);

            PublishResult result = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));
            DeliveryResult delivery = Assert.Single(await result.Delivery);

            Assert.True(result.Success);
            Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        }

        // Тест 38 — продолжение работы Broker при отказе Subscriber
        [Fact]
        public async Task PubSub_Broker_ContinuesWork_WhenSubscriberUnavailable()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber working = new("Working", EventTypes.StudentEnrolled);
            TestSubscriber down = new("Down", EventTypes.StudentEnrolled);
            working.ConnectTo(broker);
            down.ConnectTo(broker);
            down.SetState(SubscriberState.Unavailable);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Equal(2, working.Received.Count);
            Assert.Empty(down.Received);
        }

        // Тест 39 — добавление нового Subscriber
        [Fact]
        public void PubSub_NewSubscriber_IsAdded()
        {
            var (broker, _) = CreatePubSub();
            new TestSubscriber("A", EventTypes.CourseCompleted).ConnectTo(broker);

            new CertificateService().ConnectTo(broker);

            Assert.Equal(2, broker.GetSubscribers(EventTypes.CourseCompleted).Count);
            Assert.NotNull(broker.FindSubscriber("CertificateService"));
        }

        // Тест 40 — получение события новым Subscriber
        [Fact]
        public async Task PubSub_NewSubscriber_ReceivesEvent()
        {
            var (broker, publisher) = CreatePubSub();
            CertificateService certificate = new();
            certificate.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.CourseCompleted)).Delivery;

            Assert.Single(certificate.Certificates);
        }

        // Тест 41 — Publisher не изменяется при добавлении Subscriber
        [Fact]
        public async Task PubSub_NewSubscriber_PublisherNotChanged()
        {
            var (broker, publisher) = CreatePubSub();
            new TestSubscriber("A", EventTypes.CourseCompleted).ConnectTo(broker);

            PublishResult before = publisher.Publish(CreateEvent(EventTypes.CourseCompleted));
            await before.Delivery;

            TestSubscriber added = new("Added", EventTypes.CourseCompleted);
            added.ConnectTo(broker);

            PublishResult after = publisher.Publish(CreateEvent(EventTypes.CourseCompleted));
            await after.Delivery;

            Assert.Equal(1, before.SubscriberCount);
            Assert.Equal(2, after.SubscriberCount);
            Assert.Single(added.Received);
        }

        // Тест 42 — несколько Broker: переключение на Broker-2 и ошибка при отказе всех
        [Fact]
        public async Task PubSub_SeveralBrokers_FailoverToSecondBroker()
        {
            Broker broker1 = new("Broker-1", FastRetry);
            Broker broker2 = new("Broker-2", FastRetry);
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker1);
            subscriber.ConnectTo(broker2);
            Publisher publisher = new(broker1, broker2);

            broker1.SetAvailable(false);
            PublishResult result = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));
            await result.Delivery;

            Assert.True(result.Success);
            Assert.Equal("Broker-2", result.BrokerName);
            Assert.Single(subscriber.Received);

            broker2.SetAvailable(false);
            PublishResult failed = publisher.Publish(CreateEvent(EventTypes.StudentEnrolled));

            Assert.False(failed.Success);
            Assert.Contains("все Broker недоступны", failed.Message);
            Assert.Single(publisher.Errors);
        }

        // Тест 43 — повторная доставка: два сбоя, успех с третьей попытки
        [Fact]
        public async Task PubSub_Retry_SucceedsOnThirdAttempt()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            subscriber.SetState(SubscriberState.Unstable);

            DeliveryResult delivery = Assert.Single(await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery);

            Assert.True(delivery.Success);
            Assert.Equal(3, delivery.Attempts);
            Assert.Single(subscriber.Received);
            Assert.Equal(0, broker.QueueCount);
        }

        // Тест 44 — очередь: событие не теряется и доставляется после восстановления Subscriber
        [Fact]
        public async Task PubSub_Queue_EventDeliveredAfterSubscriberRecovery()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            subscriber.SetState(SubscriberState.Unavailable);

            DeliveryResult delivery = Assert.Single(await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery);

            Assert.Equal(DeliveryStatus.Unavailable, delivery.Status);
            Assert.Equal(1, broker.QueueCount);
            Assert.Empty(subscriber.Received);

            subscriber.SetState(SubscriberState.Available);
            int delivered = await broker.ProcessQueueAsync();

            Assert.Equal(1, delivered);
            Assert.Equal(0, broker.QueueCount);
            Assert.Single(subscriber.Received);
        }

        // Тест 45 — AnalyticsService собирает статистику по событиям
        [Fact]
        public async Task PubSub_Analytics_CollectsStatistics()
        {
            var (broker, publisher) = CreatePubSub();
            AnalyticsService analytics = new();
            analytics.ConnectTo(broker);

            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.AssignmentSubmitted)).Delivery;
            await publisher.Publish(CreateEvent(EventTypes.CourseCompleted)).Delivery;

            string report = analytics.GetReport();

            Assert.Contains("всего событий 3", report);
            Assert.Contains("Основы программирования", report);
            Assert.Contains("Уникальных студентов: 1", report);
        }

        // Тест 46 — репликация очереди: копия у Broker-2, доставка при отказе Broker-1, без дубля после его возврата
        [Fact]
        public async Task PubSub_QueueReplication_TakeoverWhenOwnerDown()
        {
            Broker broker1 = new("Broker-1", FastRetry);
            Broker broker2 = new("Broker-2", FastRetry);
            broker1.AddPeer(broker2);
            broker2.AddPeer(broker1);

            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker1);
            subscriber.ConnectTo(broker2);
            subscriber.SetState(SubscriberState.Unavailable);

            Publisher publisher = new(broker1, broker2);
            await publisher.Publish(CreateEvent(EventTypes.StudentEnrolled)).Delivery;

            Assert.Equal(1, broker1.QueueCount);
            Assert.Equal(1, broker2.QueueCount);

            broker1.SetAvailable(false);
            subscriber.SetState(SubscriberState.Available);
            int delivered = await broker2.ProcessQueueAsync();

            Assert.Equal(1, delivered);
            Assert.Equal(0, broker2.QueueCount);
            Assert.Single(subscriber.Received);

            broker1.SetAvailable(true);
            await broker1.ProcessQueueAsync();

            Assert.Equal(0, broker1.QueueCount);
            Assert.Single(subscriber.Received);
        }

        // Тест 47 — защита от дублей: повторно доставленное событие не обрабатывается второй раз
        [Fact]
        public async Task PubSub_DuplicateEvent_ProcessedOnlyOnce()
        {
            var (broker, publisher) = CreatePubSub();
            TestSubscriber subscriber = new("A", EventTypes.StudentEnrolled);
            subscriber.ConnectTo(broker);
            Event evt = CreateEvent(EventTypes.StudentEnrolled);

            await publisher.Publish(evt).Delivery;
            DeliveryResult second = Assert.Single(await publisher.Publish(evt).Delivery);

            Assert.True(second.Success);
            Assert.Contains("уже обработано", second.Message);
            Assert.Single(subscriber.Received);
        }
    }
}
