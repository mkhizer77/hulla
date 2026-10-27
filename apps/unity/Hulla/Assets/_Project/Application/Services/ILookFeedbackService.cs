using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>One player's answer about a test look: 1 to 5, whether they keep it, an optional line.</summary>
    public sealed class LookAnswer
    {
        public LookAnswer(string look, int rating, bool keep, string comment, int rounds)
        {
            Look = look;
            Rating = rating;
            Keep = keep;
            Comment = comment;
            Rounds = rounds;
        }

        public string Look { get; }

        public int Rating { get; }

        public bool Keep { get; }

        public string Comment { get; }

        public int Rounds { get; }
    }

    /// <summary>look_feedback: the server keeps the latest answer per player and look.</summary>
    public interface ILookFeedbackService
    {
        Awaitable<Result<bool>> SendAsync(LookAnswer answer, CancellationToken cancellation);
    }
}
