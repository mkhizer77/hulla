#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Share;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Home with every button as a pointer leaves it and as it hovers, for eyeballing the desktop hover;
    /// explicit: <c>-testFilter Hulla.Screens.Tests.HoverSnapshot</c>, files in HULLA_SNAPSHOT_DIR.
    /// </summary>
    public sealed class HoverSnapshot
    {
        [UnityTest, Explicit]
        public IEnumerator Plain() => Snap("home-plain", false);

        [UnityTest, Explicit]
        public IEnumerator Hovered() => Snap("home-hover", true);

        private static IEnumerator Snap(string name, bool hover)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var task = CardRenderer.RenderAsync(settings, CardRatio.Story, root =>
            {
                ScreenCases.Skin(root);
                var instance = catalog.Home!.Instantiate();
                instance.style.flexGrow = 1;
                root.Add(instance);
                new HomeView(instance, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { });
                if (hover)
                {
                    var states = typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var hovered = System.Enum.Parse(states.PropertyType, "Hover");
                    foreach (var button in instance.Query<Button>().ToList())
                    {
                        states.SetValue(button, hovered);
                    }
                }
            });
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), awaiter.GetResult());
        }
    }
}
#endif
