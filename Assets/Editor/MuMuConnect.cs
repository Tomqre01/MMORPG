using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace MuProto.EditorTools
{
    // Emulators such as MuMu Player are reached over adb TCP, which Unity does not do on its own,
    // so "Build And Run" says "No Android devices connected". This connects Unity's own adb to the
    // emulator when the project opens on the Android target, and from Tools > Android.
    [InitializeOnLoad]
    static class MuMuConnect
    {
        // MuMu Player 12: 16384 for the first instance, +32 for each extra one. 7555: older MuMu. 5555: other emulators.
        static readonly int[] Ports = { 16384, 16416, 16448, 7555, 5555 };
        static bool busy;

        static MuMuConnect()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android) Connect(false);
            };
        }

        [MenuItem("Tools/Android/Connect MuMu Emulator")]
        static void ConnectMenu() => Connect(true);

        [MenuItem("Tools/Android/List Connected Devices")]
        static void ListMenu()
        {
            string adb = FindAdb();
            if (adb == null) { Debug.LogWarning("[MuMu] adb.exe not found. Install Android Build Support in Unity Hub."); return; }
            Task.Run(() => Debug.Log("[MuMu] adb devices\n" + Run(adb, "devices", 10000)));
        }

        static void Connect(bool verbose)
        {
            if (busy) return;
            string adb = FindAdb();
            if (adb == null)
            {
                Debug.LogWarning("[MuMu] adb.exe not found. Install Android Build Support (with Android SDK) in Unity Hub.");
                return;
            }
            busy = true;
            // adb connect can wait a few seconds per closed port, so keep it off the editor's main thread.
            Task.Run(() =>
            {
                try
                {
                    var log = new StringBuilder();
                    bool any = false;
                    foreach (int port in Ports)
                    {
                        string o = Run(adb, "connect 127.0.0.1:" + port, 6000).Trim();
                        bool ok = o.Contains("connected to") && !o.Contains("cannot");
                        any |= ok;
                        if (ok || verbose) log.AppendLine(o);
                        if (ok) break; // one port per emulator, or it shows up several times under Run Device
                    }
                    string devices = Run(adb, "devices", 10000).Trim();
                    if (any)
                        Debug.Log("[MuMu] Emulator connected. Pick it under Run Device (press Refresh), then Build And Run.\n" + log + devices);
                    else if (verbose)
                        Debug.LogWarning("[MuMu] Could not reach an emulator. Start MuMu Player first, and check its ADB port " +
                                         "in the MuMu Multi-Instance window.\n" + log + devices);
                }
                finally { busy = false; }
            });
        }

        static string FindAdb()
        {
            string exe = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
            string[] roots =
            {
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK"),
                EditorPrefs.GetString("AndroidSdkRoot"),
            };
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string path = Path.Combine(root, "platform-tools", exe);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        static string Run(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return "adb " + args + ": timed out";
                    }
                    return outTask.Result + errTask.Result;
                }
            }
            catch (System.Exception e)
            {
                return "adb " + args + ": " + e.Message;
            }
        }
    }
}
