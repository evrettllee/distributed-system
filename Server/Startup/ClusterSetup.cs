using Server.Common;
using Server.Consensus;

namespace Server.Startup
{
    public static class ClusterSetup
    {
        public static Cluster Create(int nodeCount, TimeSpan heartbeatInterval)
        {
            Cluster cluster = new(nodeCount);
            cluster.RunElection();
            cluster.StartHeartbeatMonitor(heartbeatInterval);

            Logger.Log($"Узлов: {cluster.Nodes.Count}, Majority = {cluster.Majority}, heartbeat каждые {heartbeatInterval.TotalSeconds:0} сек");

            return cluster;
        }
    }
}
