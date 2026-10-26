using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Table voice on Android (ADR-0024): the microphone permission, asked at runtime the first time a seat
    /// turns its mic on, and the audio-settings permission WebRTC uses to switch the phone to call audio.
    /// </summary>
    public sealed class VoiceManifest : IPostGenerateGradleAndroidProject
    {
        private const string Android = "http://schemas.android.com/apk/res/android";

        private static readonly string[] Permissions =
        {
            "android.permission.RECORD_AUDIO",
            "android.permission.MODIFY_AUDIO_SETTINGS",
        };

        public int callbackOrder => 3;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            foreach (var name in Permissions)
            {
                if (!Declared(document, name))
                {
                    var permission = document.CreateElement("uses-permission");
                    permission.SetAttribute("name", Android, name);
                    document.DocumentElement!.PrependChild(permission);
                }
            }

            document.Save(manifestPath);
        }

        private static bool Declared(XmlDocument document, string name)
        {
            foreach (XmlNode node in document.SelectNodes("/manifest/uses-permission")!)
            {
                if (node.Attributes?["name", Android]?.Value == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
