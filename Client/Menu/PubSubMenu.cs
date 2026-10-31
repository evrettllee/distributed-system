using Client.Network;
using Client.UI;
using Shared;

namespace Client.Menu
{
    public static class PubSubMenu
    {
        public static Response GetSubscribers(ServerClient client) => client.Send("GetSubscribers");

        public static Response GetPublisherErrors(ServerClient client) => client.Send("GetPublisherErrors");

        public static Response GetBrokers(ServerClient client) => client.Send("GetBrokers");

        public static Response ProcessQueue(ServerClient client) => client.Send("ProcessQueue");

        public static Response GetEventStats(ServerClient client) => client.Send("GetEventStats");

        public static Response SetSubscriberState(ServerClient client)
        {
            var (name, error) = ConsoleInput.SelectByNumber(client, "GetSubscribers", "Номер подписчика", "Нет зарегистрированных подписчиков");
            if (error != null)
                return error;

            string[] states = ["Available", "Unavailable", "Failing", "Unstable"];
            if (!ConsoleInput.TryChoose("Состояние: 1 - Available, 2 - Unavailable, 3 - Failing, 4 - Unstable (2 сбоя, затем успех)", states, out string state))
                return ConsoleInput.InvalidChoice(states.Length);

            return client.Send("SetSubscriberState", new { Name = name, State = state });
        }

        public static Response RegisterSubscriber(ServerClient client)
        {
            var (name, error) = ConsoleInput.SelectByNumber(client, "GetSubscriberCatalog", "Номер подписчика для регистрации", "Нет доступных для регистрации подписчиков");
            if (error != null)
                return error;

            return client.Send("RegisterSubscriber", new { Name = name });
        }

        public static Response SetBrokerState(ServerClient client)
        {
            var (name, error) = ConsoleInput.SelectByNumber(client, "GetBrokers", "Номер Broker", "Нет Broker");
            if (error != null)
                return error;

            bool[] values = [true, false];
            if (!ConsoleInput.TryChoose("Состояние Broker: 1 - Available, 2 - Unavailable", values, out bool available))
                return ConsoleInput.InvalidChoice(values.Length);

            return client.Send("SetBrokerState", new { Name = name, Available = available });
        }
    }
}
