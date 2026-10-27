using System;
using System.Threading;
using Hulla.Application.Services;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// The live switches from the server (GDD 22.5, 20.15). Until the first answer, and whenever the
    /// server cannot be reached, the client runs on its built-in defaults: everything on, nothing
    /// to update.
    /// </summary>
    public sealed class RemoteConfigModel
    {
        private readonly IRemoteConfigService _service;

        public RemoteConfigModel(IRemoteConfigService service)
        {
            _service = service;
        }

        public event Action? Changed;

        public RemoteConfigSnapshot Current { get; private set; } = RemoteConfigSnapshot.Defaults;

        /// <summary>The server is down for maintenance: no new searches or tables (GDD 20.15).</summary>
        public bool Maintenance => Current.Bool("maintenance", false);

        public bool QuickPlay => Current.Bool("quick_play", true) && !Maintenance;

        public bool Voice => Current.Bool("voice", true);

        /// <summary>The look playtest: the Home card and the question after a round in a new look.</summary>
        public bool LooksTest => Current.Bool("looks_test", true);

        public bool UpdateRequired => Current.UpdateRequired;

        public async Awaitable RefreshAsync(string clientVersion, string cohort, CancellationToken cancellation)
        {
            var fetched = await _service.FetchAsync(clientVersion, cohort, cancellation);
            if (!fetched.IsOk)
            {
                return;
            }

            Current = fetched.Value;
            Changed?.Invoke();
        }
    }
}
