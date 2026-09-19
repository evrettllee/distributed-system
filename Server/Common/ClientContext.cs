namespace Server.Common
{
    public static class ClientContext
    {
        private static readonly AsyncLocal<int> _clientId = new();

        public static int ClientId
        {
            get => _clientId.Value;
            set => _clientId.Value = value;
        }
    }
}