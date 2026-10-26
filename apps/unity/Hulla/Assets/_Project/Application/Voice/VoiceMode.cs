namespace Hulla.Application.Voice
{
    /// <summary>How a table talks (GDD 11.4): open mic with voice activity, push-to-talk, or no voice.</summary>
    public enum VoiceMode
    {
        Off,
        OpenMic,
        PushToTalk,
    }

    /// <summary>What the mic button and the voice pill show.</summary>
    public enum VoiceState
    {
        /// <summary>No voice here: no SDK on this build, the server switch is off, or the table says off.</summary>
        Hidden,

        /// <summary>Not in the channel, and nothing asks to be.</summary>
        Idle,

        /// <summary>My mic is on; the channel opens once a second mic is on (GDD 20.14).</summary>
        Waiting,

        Joining,

        Live,

        /// <summary>Left after two minutes of silence; the next mic change brings voice back.</summary>
        Paused,

        /// <summary>The channel failed or dropped (E25); play goes on and a tap retries.</summary>
        Failed,

        /// <summary>Moderation has suspended this player's voice (GDD 24.3).</summary>
        Suspended,

        /// <summary>The player refused the microphone; they can still listen.</summary>
        NoPermission,
    }
}
