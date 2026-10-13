using System.Collections.Generic;
using Hulla.Protocol;

namespace Hulla.Application.Social
{
    /// <summary>
    /// A tapped notification (GDD 10.10): the server's data carries <c>route</c>, and a table invite
    /// also carries the room <c>code</c>.
    /// </summary>
    public readonly struct PushTap
    {
        public PushTap(PushRoute route, string? code)
        {
            Route = route;
            Code = code;
        }

        public PushRoute Route { get; }

        /// <summary>The room code of an invite; null for other routes.</summary>
        public string? Code { get; }

        /// <returns>Null when the data is not one of ours or an invite has no valid code.</returns>
        public static PushTap? From(IReadOnlyDictionary<string, string>? data)
        {
            if (data == null || !data.TryGetValue("route", out var route))
            {
                return null;
            }

            switch (route)
            {
                case "lobby":
                    data.TryGetValue("code", out var code);
                    var valid = InviteLink.CodeFrom(InviteLink.For(code ?? ""));
                    return valid == null ? (PushTap?)null : new PushTap(PushRoute.Lobby, valid);
                case "friends":
                    return new PushTap(PushRoute.Friends, null);
                case "home":
                    return new PushTap(PushRoute.Home, null);
                case "pass":
                    return new PushTap(PushRoute.Pass, null);
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// The phone's side of push: the permission and the notification the app was opened from.
    /// Tokens for push_register come with the Firebase SDK (docs/runbooks/push-and-purchases-setup.md).
    /// </summary>
    public interface IPushDevice
    {
        /// <summary>True when the platform needs a runtime permission that has not been granted.</summary>
        bool CanAsk { get; }

        /// <summary>Shows the system permission dialog; the callback gets the answer.</summary>
        void Ask(System.Action<bool> answered);

        /// <summary>The data of the notification that opened the app, once; null if none.</summary>
        IReadOnlyDictionary<string, string>? TakeOpenedData();
    }
}
