using System.IO;
using System.Xml;
using Hulla.Application.Social;
using UnityEditor.Android;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Lets invite and referral links open the game activity (GDD 10.5, 10.9): <c>hulla://j/CODE</c>,
    /// <c>hulla://r/FRIENDCODE</c> and the same paths on every <see cref="InviteLink.Hosts"/> as App Links,
    /// which Android verifies against each host's /.well-known/assetlinks.json (infra/cloudflare/links).
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

            RemoveRetiredHosts(activity);
            // j: table invites; r: referral links from share cards (GDD 10.9).
            foreach (var kind in new[] { "j", "r" })
            {
                AddOnce(document, activity, autoVerify: false, ("scheme", "hulla"), ("host", kind));
                foreach (var host in InviteLink.Hosts)
                {
                    AddOnce(document, activity, autoVerify: true, ("scheme", "https"), ("host", host), ("pathPrefix", "/" + kind + "/"));
                }
            }

            document.Save(manifestPath);
        }

        /// <summary>
        /// Gradle reuses the exported project, so filters for hosts since dropped from <see cref="InviteLink.Hosts"/>
        /// would stay; Android before 12 then verifies none of the hosts.
        /// </summary>
        private static void RemoveRetiredHosts(XmlElement activity)
        {
            foreach (XmlNode data in activity.SelectNodes("intent-filter/data")!)
            {
                var scheme = data.Attributes?["scheme", Android]?.Value;
                var host = data.Attributes?["host", Android]?.Value;
                if (scheme == "https" && host != null && System.Array.IndexOf(InviteLink.Hosts, host) < 0)
                {
                    activity.RemoveChild(data.ParentNode!);
                }
            }
        }

        /// <summary>Gradle reuses the exported project, so a filter may already be there.</summary>
        private static void AddOnce(XmlDocument document, XmlElement activity, bool autoVerify, params (string Name, string Value)[] data)
        {
            foreach (XmlNode existing in activity.SelectNodes("intent-filter/data")!)
            {
                var same = true;
                foreach (var (name, value) in data)
                {
                    same &= existing.Attributes?[name, Android]?.Value == value;
                }

                if (same)
                {
                    return;
                }
            }

            activity.AppendChild(Filter(document, autoVerify, data));
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
