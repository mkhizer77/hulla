using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hulla.Composition
{
    /// <summary>The only object in the Boot scene: builds the app scope and opens the Main scene.</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        private AppScope? _app;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _app = new AppScope(AppConfig.Local());
            SceneManager.LoadScene("Main");
        }

        private void OnDestroy()
        {
            _app?.Dispose();
            _app = null;
        }
    }
}
