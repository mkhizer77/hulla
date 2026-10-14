#if UNITY_EDITOR
using System.Collections;
using System.IO;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Share;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Every main screen in English and the default theme, with the bottom navigation where the app shows
    /// it, as PNGs for a design review; explicit: <c>-testFilter Hulla.Screens.Tests.AuditSnapshots</c>.
    /// </summary>
    public sealed class AuditSnapshots
    {
        private static readonly string[] WithNav = { "home", "friends", "profile", "shop", "pass", "leaderboard", "ranked", "missions", "achievements" };

        [UnityTest, Explicit]
        public IEnumerator EveryScreen()
        {
            Strings.Use(Strings.Fallback);
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";
            Directory.CreateDirectory(dir);
            foreach (var screen in ScreenCases.All())
            {
                var task = CardRenderer.RenderAsync(settings, CardRatio.Story, root =>
                {
                    ScreenCases.Skin(root, Hulla.Application.Settings.TableSkin.Club);
                    var instance = screen.Screen!.Instantiate();
                    instance.style.flexGrow = 1;
                    root.Add(instance);
                    screen.Bind(instance);
                    var face = instance.Q(className: "screen");
                    if (face != null)
                    {
                        Hulla.Presentation.Theme.AmbientLayer.Decorate(face, Hulla.Application.Settings.TableSkin.Club, false);
                    }
                    if (System.Array.IndexOf(WithNav, screen.Name) >= 0)
                    {
                        var bar = new NavBar(UiText.NavLabel);
                        bar.Select(NavTab.Home);
                        root.Add(bar);
                    }
                });
                var awaiter = task.GetAwaiter();
                while (!awaiter.IsCompleted)
                {
                    yield return null;
                }

                File.WriteAllBytes(Path.Combine(dir, "audit-" + screen.Name + ".png"), awaiter.GetResult());
            }
        }
    }
}
#endif
