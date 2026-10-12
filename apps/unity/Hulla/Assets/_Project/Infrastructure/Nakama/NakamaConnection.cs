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

        public IClient Client { get; private set; }

        /// <summary>A client over UnityWebRequest so it works on every Unity target.</summary>
        public static NakamaConnection Create(string scheme, string host, int port, string serverKey) =>
            new NakamaConnection(new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance));

        public ISession? Session { get; set; }

        public ISocket? Socket { get; set; }

        /// <summary>Points the connection at another server; the next sign-in starts a fresh session.</summary>
        public void Retarget(string scheme, string host, int port, string serverKey)
        {
            Socket?.CloseAsync();
            Socket = null;
            Session = null;
            Client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);
        }
    }
}
