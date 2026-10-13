using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Registers the share card's content provider in the generated library manifest, so the main
    /// manifest stays Unity's own.
    /// </summary>
    public sealed class ShareProviderManifest : IPostGenerateGradleAndroidProject
    {
        private const string Android = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            var application = document.SelectSingleNode("/manifest/application");
            if (application == null)
            {
                application = document.CreateElement("application");
                document.DocumentElement!.AppendChild(application);
            }

            foreach (XmlNode node in application.ChildNodes)
            {
                if (node.Name == "provider" && node.Attributes?["name", Android]?.Value == "com.hulla.share.HullaShareProvider")
                {
                    return;
                }
            }

            var provider = document.CreateElement("provider");
            provider.SetAttribute("name", Android, "com.hulla.share.HullaShareProvider");
            provider.SetAttribute("authorities", Android, "${applicationId}.share");
            provider.SetAttribute("exported", Android, "false");
            provider.SetAttribute("grantUriPermissions", Android, "true");
            application.AppendChild(provider);
            document.Save(manifestPath);
        }
    }
}
