#if UNITY_IOS
using System.Linq;
using Hulla.Application.Social;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Universal Links for invite and referral links (GDD 10.5, 10.9): the Associated Domains capability for
    /// every <see cref="InviteLink.Hosts"/>, checked by iOS against /.well-known/apple-app-site-association.
    /// A personal team cannot sign the capability, so it waits for the paid Apple Developer account; until
    /// then the link page opens the app through hulla://.
    /// </summary>
    public static class InviteLinkXcode
    {
        /// <summary>Switch on once the paid account signs the app (docs/runbooks/links-setup.md).</summary>
        public static readonly bool UniversalLinks = false;

        [PostProcessBuild(5)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS || !UniversalLinks)
            {
                return;
            }

            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var capabilities = new ProjectCapabilityManager(projectPath, "Unity-iPhone.entitlements", null, project.GetUnityMainTargetGuid());
            capabilities.AddAssociatedDomains(InviteLink.Hosts.Select(host => "applinks:" + host).ToArray());
            capabilities.WriteToFile();
        }
    }
}
#endif
