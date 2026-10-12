using Hulla.Presentation.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hulla.Composition
{
    /// <summary>The only object in the Boot scene: builds the app scope, opens Main and starts the flow there.</summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        private AppScope? _app;
        private AppFlow? _flow;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            UnityEngine.Application.targetFrameRate = 60;

            // The socket adapter delivers events from Update; a paused player would never see them.
            UnityEngine.Application.runInBackground = true;
            _app = new AppScope(AppConfig.FromEnvironment());
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene("Main");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Main" || _app == null)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            var shell = FindAnyObjectByType<ShellView>();
            if (shell == null)
            {
                Debug.LogError("Main has no ShellView");
                return;
            }

            _flow = new AppFlow(_app, shell);
            _flow.Start();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _flow?.Dispose();
            _app?.Dispose();
        }
    }
}
