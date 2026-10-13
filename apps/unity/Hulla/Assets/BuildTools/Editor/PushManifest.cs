using System.IO;
using System.Text;
using System.Xml;
using Hulla.Presentation.Text;
using UnityEditor.Android;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Push on Android (GDD 10.10): the POST_NOTIFICATIONS permission for Android 13 and later, and the
    /// string resources the server's title_loc_key and body_loc_key name.
    /// </summary>
    public sealed class PushManifest : IPostGenerateGradleAndroidProject
    {
        private const string Android = "http://schemas.android.com/apk/res/android";
        private const string Permission = "android.permission.POST_NOTIFICATIONS";

        public int callbackOrder => 2;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            AddPermission(Path.Combine(path, "src", "main", "AndroidManifest.xml"));
            WriteStrings(Path.Combine(path, "src", "main", "res", "values", "hulla_push_strings.xml"));
        }

        private static void AddPermission(string manifestPath)
        {
            var document = new XmlDocument();
            document.Load(manifestPath);
            foreach (XmlNode node in document.SelectNodes("/manifest/uses-permission")!)
            {
                if (node.Attributes?["name", Android]?.Value == Permission)
                {
                    return;
                }
            }

            var permission = document.CreateElement("uses-permission");
            permission.SetAttribute("name", Android, Permission);
            document.DocumentElement!.PrependChild(permission);
            document.Save(manifestPath);
        }

        private static void WriteStrings(string path)
        {
            var text = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources>\n");
            foreach (var pair in PushText.Strings)
            {
                text.Append("    <string name=\"").Append(pair.Key).Append("\">")
                    .Append(PushText.ToAndroid(pair.Value)).Append("</string>\n");
            }

            text.Append("</resources>\n");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.ToString());
        }
    }
}
