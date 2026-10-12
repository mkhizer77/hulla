using Nakama;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>The client, session and socket one signed-in player shares across services.</summary>
    public sealed class NakamaConnection
    {
        public NakamaConnection(IClient client)
        {
            Client = client;
        }

        public IClient Client { get; }

        /// <summary>A client over UnityWebRequest so it works on every Unity target.</summary>
        public static NakamaConnection Create(string scheme, string host, int port, string serverKey) =>
            new NakamaConnection(new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance));

        public ISession? Session { get; set; }

        public ISocket? Socket { get; set; }
    }
}
