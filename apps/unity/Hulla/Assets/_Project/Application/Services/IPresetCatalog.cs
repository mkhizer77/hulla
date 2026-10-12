using System.Threading;
using Hulla.Protocol;
using TableSettings = Hulla.Protocol.Settings;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>The Thulla presets from content/presets (GDD 6.13): the toggles and table settings each one sets.</summary>
    public interface IPresetCatalog
    {
        /// <summary>The preset's toggles over the defaults and its table settings, or null when unknown.</summary>
        (ThullaRules Rules, TableSettings Settings)? Find(string presetId);

        Awaitable LoadAsync(CancellationToken cancellation);
    }
}
