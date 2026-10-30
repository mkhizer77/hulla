#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Table voice on iOS (ADR-0024): LiveKit's static library carries WebRTC's NSString category, which the
    /// linker drops unless UnityFramework links with -ObjC; without it the app aborts at launch with an
    /// unrecognized selector (stringForStdString:).
    /// </summary>
    public static class VoiceXcodeProject
    {
        [PostProcessBuild(3)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddBuildProperty(project.GetUnityFrameworkTargetGuid(), "OTHER_LDFLAGS", "-ObjC");
            project.WriteToFile(projectPath);
        }
    }
}
#endif
