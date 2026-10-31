namespace Server.PubSub
{
    public class BrokerUnavailableException(string brokerName)
        : Exception($"{brokerName} недоступен");
}