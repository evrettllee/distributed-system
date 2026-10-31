using Client.Network;
using Shared;

namespace Client.Menu
{
    public static class ReplicationMenu
    {
        public static Response GetStatus(ServerClient client) => client.Send("GetStatus");

        public static Response CompareReplicas(ServerClient client) => client.Send("CompareReplicas");

        public static Response RecoverPrimary(ServerClient client) => client.Send("RecoverPrimary");
    }
}
