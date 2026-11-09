using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>The birth year on the server (GDD 24.6, #87): stored once, server-only, and what it allows.</summary>
    public interface IAgeService
    {
        /// <summary>Sends the year given at first launch; the answer is what the age allows.</summary>
        Awaitable<Result<AgeLimits>> SetAsync(int birthYear, CancellationToken cancellation);
    }

    /// <summary>What the player's age allows, as the server decides it.</summary>
    public readonly struct AgeLimits
    {
        public AgeLimits(bool strangersLocked, bool personalisedAds)
        {
            StrangersLocked = strangersLocked;
            PersonalisedAds = personalisedAds;
        }

        /// <summary>Under 16: no voice or free text with players who aren't friends.</summary>
        public bool StrangersLocked { get; }

        /// <summary>18 and over: ads may be personalised with consent.</summary>
        public bool PersonalisedAds { get; }
    }
}
