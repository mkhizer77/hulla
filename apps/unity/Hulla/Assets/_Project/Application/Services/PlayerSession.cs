namespace Hulla.Application.Services
{
    /// <summary>The signed-in player as the server knows them.</summary>
    public sealed class PlayerSession
    {
        public PlayerSession(string userId, string displayName)
        {
            UserId = userId;
            DisplayName = displayName;
        }

        public string UserId { get; }

        public string DisplayName { get; }
    }
}
