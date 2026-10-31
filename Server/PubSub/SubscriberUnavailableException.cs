namespace Server.PubSub
{
    public class SubscriberUnavailableException(string subscriberName)
        : Exception($"{subscriberName} недоступен");
}