#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Hulla.BuildTools
{
    /// <summary>
    /// The app uses only the encryption built into iOS (HTTPS, TLS for voice), which is exempt from export
    /// rules, so Info.plist says so and App Store Connect stops asking on every TestFlight upload.
    /// </summary>
    public static class ExportCompliance
    {
        [PostProcessBuild(6)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);
        }
    }
}
#endif
