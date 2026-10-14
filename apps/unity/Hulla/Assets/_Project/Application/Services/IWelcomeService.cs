using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>The first session's one-time rewards (GDD 17.2): the server pays each once per account.</summary>
    public interface IWelcomeService
    {
        /// <summary>Reports the finished tutorial; the coins paid now, 0 when it was paid before.</summary>
        Awaitable<Result<long>> TutorialCompleteAsync(CancellationToken cancellation);
    }
}
