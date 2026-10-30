using System;
using System.IO;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Development builds only: with <c>-hulla-shots &lt;dir&gt;</c> on the command line, F9 saves the window
    /// as a numbered PNG in that folder, so a review of the Mac windows needs no screen-recording rights.
    /// </summary>
    public sealed class DevScreenshots : MonoBehaviour
    {
        private string? _dir;
        private int _count;

        public static void Attach(GameObject host)
        {
            if (!Debug.isDebugBuild)
            {
                return;
            }

            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-shots" && args[i + 1].Trim().Length > 0)
                {
                    var shots = host.AddComponent<DevScreenshots>();
                    shots._dir = args[i + 1].Trim();
                    Directory.CreateDirectory(shots._dir);
                    return;
                }
            }
        }

        private void Update()
        {
            if (_dir == null || !Input.GetKeyDown(KeyCode.F9))
            {
                return;
            }

            _count++;
            var path = Path.Combine(_dir, $"{DateTime.Now:HHmmss}-{_count:000}.png");
            ScreenCapture.CaptureScreenshot(path);
        }
    }
}
