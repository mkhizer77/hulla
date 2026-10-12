namespace Hulla.Application.Social
{
    /// <summary>A friend's presence (GDD 10.4), as the server's table_list_friends reports it.</summary>
    public enum PresenceState
    {
        Lobby,
        Round,
        Online,
        Away,
        Offline,
        Hidden,
    }
}
