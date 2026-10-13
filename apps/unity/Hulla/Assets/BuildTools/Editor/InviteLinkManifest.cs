using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Lets invite links open the game activity (GDD 10.5): <c>hulla://j/CODE</c> now, and
    /// <c>https://hulla.gg/j/CODE</c> as an App Link once the domain serves its assetlinks.json (#130).
    /// </summary>
    public sealed class InviteLinkManifest : IPostGenerateGradleAndroidProject
    {
        private const string Android = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            var activity = LauncherActivity(document);
            if (activity == null)
            {
                UnityEngine.Debug.LogWarning("InviteLinkManifest: no launcher activity; invite links will not open the app");
                return;
            }

            // Gradle reuses the exported project, so the filters may already be there.
            foreach (XmlNode data in activity.SelectNodes("intent-filter/data")!)
            {
                if (data.Attributes?["scheme", Android]?.Value == "hulla")
                {
                    return;
                }
            }

            activity.AppendChild(Filter(document, autoVerify: false, ("scheme", "hulla"), ("host", "j")));
            activity.AppendChild(Filter(document, autoVerify: true, ("scheme", "https"), ("host", "hulla.gg"), ("pathPrefix", "/j/")));
            document.Save(manifestPath);
        }

        private static XmlElement LauncherActivity(XmlDocument document)
        {
            foreach (XmlNode activity in document.SelectNodes("/manifest/application/activity")!)
            {
                foreach (XmlNode filter in activity.ChildNodes)
                {
                    if (filter.Name != "intent-filter")
                    {
                        continue;
                    }

                    foreach (XmlNode child in filter.ChildNodes)
                    {
                        if (child.Name == "category" && child.Attributes?["name", Android]?.Value == "android.intent.category.LAUNCHER")
                        {
                            return (XmlElement)activity;
                        }
                    }
                }
            }

            return null;
        }

        private static XmlElement Filter(XmlDocument document, bool autoVerify, params (string Name, string Value)[] data)
        {
            var filter = document.CreateElement("intent-filter");
            if (autoVerify)
            {
                filter.SetAttribute("autoVerify", Android, "true");
            }

            Append(document, filter, "action", "android.intent.action.VIEW");
            Append(document, filter, "category", "android.intent.category.DEFAULT");
            Append(document, filter, "category", "android.intent.category.BROWSABLE");
            var element = document.CreateElement("data");
            foreach (var (name, value) in data)
            {
                element.SetAttribute(name, Android, value);
            }

            filter.AppendChild(element);
            return filter;
        }

        private static void Append(XmlDocument document, XmlElement parent, string name, string value)
        {
            var element = document.CreateElement(name);
            element.SetAttribute("name", Android, value);
            parent.AppendChild(element);
        }
    }
}
