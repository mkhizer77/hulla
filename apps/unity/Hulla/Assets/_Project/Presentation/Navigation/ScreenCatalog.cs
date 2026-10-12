using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>The screen documents, authored once and assigned in the Main scene.</summary>
    [CreateAssetMenu(menuName = "Hulla/Screen Catalog")]
    public sealed class ScreenCatalog : ScriptableObject
    {
        public VisualTreeAsset? Home;
        public VisualTreeAsset? Lobby;
        public VisualTreeAsset? Table;
        public VisualTreeAsset? QuickPlay;
        public VisualTreeAsset? Settings;
    }
}
