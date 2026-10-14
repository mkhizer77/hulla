using System;

namespace Hulla.Infrastructure.Settings
{
    /// <summary>
    /// Several copies of the app on one computer share one PlayerPrefs store (macOS keeps it per app),
    /// so a theme picked in one window changed every window. With <c>-hulla-device &lt;id&gt;</c> each copy is
    /// a separate player, and its preferences live under that id too. Phones have one player and no prefix.
    /// </summary>
    public static class DeviceProfile
    {
        private static readonly string Prefix = PrefixFrom(Environment.GetCommandLineArgs());

        /// <summary>The PlayerPrefs key for this copy of the app.</summary>
        public static string Key(string key) => Prefix + key;

        public static string PrefixFrom(string[] args)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-device" && args[i + 1].Trim().Length > 0)
                {
                    return "profile." + args[i + 1].Trim() + ".";
                }
            }

            return "";
        }
    }
}
