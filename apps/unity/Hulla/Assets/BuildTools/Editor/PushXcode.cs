#if UNITY_IOS
using System.IO;
using System.Text;
using Hulla.Presentation.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Push on iOS (GDD 10.10): the Push Notifications capability (aps-environment), the remote
    /// notification background mode Firebase Messaging expects, and Localizable.strings with the keys
    /// the server's title-loc-key and loc-key name. A personal team cannot sign the capability, so a
    /// build signed by one needs the switch off.
    /// </summary>
    public sealed class PushXcode : IPostprocessBuildWithReport
    {
        /// <summary>On since the paid account signs the app (docs/runbooks/push-and-purchases-setup.md).</summary>
        public static readonly bool Push = true;

        private const string StringsFile = "Localizable.strings";

        public int callbackOrder => 7;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS || !Push)
            {
                return;
            }

            var path = report.summary.outputPath;
            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var target = project.GetUnityMainTargetGuid();

            // Development builds are signed for APNs' sandbox; the App Store and TestFlight use production.
            var development = (report.summary.options & BuildOptions.Development) != 0;
            var capabilities = new ProjectCapabilityManager(projectPath, "Unity-iPhone.entitlements", null, target);
            capabilities.AddPushNotifications(development);
            capabilities.AddBackgroundModes(BackgroundModesOptions.RemoteNotifications);
            capabilities.WriteToFile();

            // One table at the bundle's root serves every device language until the translations land (#100).
            File.WriteAllText(Path.Combine(path, StringsFile), Strings());
            project.ReadFromFile(projectPath);
            project.AddFileToBuild(target, project.AddFile(StringsFile, StringsFile));
            project.WriteToFile(projectPath);
        }

        private static string Strings()
        {
            var text = new StringBuilder();
            foreach (var pair in PushText.Keyed)
            {
                text.Append('"').Append(pair.Key).Append("\" = \"").Append(PushText.ToApple(pair.Value)).Append("\";\n");
            }

            return text.ToString();
        }
    }
}
#endif
