namespace Hulla.Application.Social
{
    /// <summary>The parts of a profile the screens show, so views never touch wire types.</summary>
    public sealed class ProfileCard
    {
        public ProfileCard(string displayName, string handle, string bio, string friendCode, long rounds, long escapes, long bhabhis, long thullasGiven, string? avatar = null, string? title = null)
        {
            DisplayName = displayName;
            Handle = handle;
            Bio = bio;
            FriendCode = friendCode;
            Rounds = rounds;
            Escapes = escapes;
            Bhabhis = bhabhis;
            ThullasGiven = thullasGiven;
            Avatar = avatar;
            Title = title;
        }

        public string? Avatar { get; }

        public string? Title { get; }

        public string DisplayName { get; }

        public string Handle { get; }

        public string Bio { get; }

        public string FriendCode { get; }

        public long Rounds { get; }

        public long Escapes { get; }

        public long Bhabhis { get; }

        public long ThullasGiven { get; }
    }
}
