using System;

namespace Hulla.Games.Shared
{
    /// <summary>What the shell tells a game's result once the round is paid; every table view has it.</summary>
    public interface IRoundResults
    {
        void ShowReward(string? text);

        void ShowLevel(long level, float fraction);

        void OfferDouble(string? text, Action? onTap);

        void SetFriendActions(Func<string, bool> canAdd, Action<string> addFriend);
    }
}
