#if UNITY_EDITOR
using System.Collections;
using System.IO;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Ranked;
using Hulla.Presentation.Share;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Draws the Ranked screen, placed and in placement, to PNGs for eyeballing; explicit:
    /// <c>-testFilter Hulla.Screens.Tests.RankedSnapshot</c>, files in HULLA_SNAPSHOT_DIR.
    /// </summary>
    public sealed class RankedSnapshot
    {
        private static string Dir => System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";

        [UnityTest, Explicit]
        public IEnumerator Placed() => Snap("ranked", true);

        [UnityTest, Explicit]
        public IEnumerator InPlacement() => Snap("ranked-placement", false);

        private static IEnumerator Snap(string name, bool placed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var model = new RankedViewModel(new Fakes.Ranked { Placed = placed });
            var task = CardRenderer.RenderAsync(settings, CardRatio.Story, root =>
            {
                var instance = catalog.Ranked!.Instantiate();
                instance.style.flexGrow = 1;
                root.Add(instance);
                new RankedView(instance, model, () => 1_000_000_000_000, "me", () => { }, _ => { });
                _ = model.RefreshAsync(default);
            });
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), awaiter.GetResult());
        }
    }
}
#endif
