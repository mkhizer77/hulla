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
            Hulla.Presentation.Text.LocaleChoice.Apply();
            DevScreenshots.Attach(gameObject);
            _app = new AppScope(AppConfig.FromEnvironment());
            _app.Links.Receive(UnityEngine.Application.absoluteURL, Time.realtimeSinceStartup);
            UnityEngine.Application.deepLinkActivated += OnDeepLink;
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

        private void OnDeepLink(string url) => _app?.Links.Receive(url, Time.realtimeSinceStartup);

        private void OnApplicationPause(bool paused) => _flow?.SetAway(paused);

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.Application.deepLinkActivated -= OnDeepLink;
            _flow?.Dispose();
            _app?.Dispose();
        }
    }
}
