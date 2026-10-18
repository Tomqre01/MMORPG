using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MuProto.EditorTools
{
    // Tools > UI Shots: while playing, captures the HUD and each main window to Assets/../UIShots,
    // so the look can be checked (and shared) without clicking through every screen by hand.
    public static class UIShots
    {
        [MenuItem("Tools/UI Shots (Play mode)")]
        static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[UIShots] Enter Play mode first.");
                return;
            }
            var login = Object.FindAnyObjectByType<LoginScreen>();
            if (login != null) login.StartCoroutine(Capture());
            else if (HUD.Instance != null) HUD.Instance.StartCoroutine(Capture());
        }

        static IEnumerator Capture()
        {
            string dir = Path.Combine(Application.dataPath, "UIShots");
            Directory.CreateDirectory(dir);
            var login = Object.FindAnyObjectByType<LoginScreen>();
            if (login != null)
            {
                var host = new GameObject("UIShots Runner").AddComponent<Runner>();
                Object.DontDestroyOnLoad(host);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "Login.png"));
                yield return new WaitForSecondsRealtime(0.6f);
                foreach (var b in Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
                    if (b.name == "Play Offline Button") { b.onClick.Invoke(); break; }
                host.StartCoroutine(Rest(dir));
                yield break;
            }
            yield return Rest(dir);
        }

        class Runner : MonoBehaviour { }

        // Tools > Gear Shots: puts on a full set of every tier in turn and photographs the Backpack,
        // then gives the player back what they had on.
        [MenuItem("Tools/Gear Shots (Play mode)")]
        static void RunGear()
        {
            if (!Application.isPlaying || HUD.Instance == null || Player.Instance == null)
            {
                Debug.LogWarning("[UIShots] Enter Play mode (and log in) first.");
                return;
            }
            HUD.Instance.StartCoroutine(Gear());
        }

        static IEnumerator Gear()
        {
            string dir = Path.Combine(Application.dataPath, "UIShots");
            Directory.CreateDirectory(dir);
            var p = Player.Instance;
            p.AutoBattle = false;
            var before = new System.Collections.Generic.List<Item>(p.Data.equipped);
            for (int tier = 1; tier <= GameData.MaxTier; tier++)
            {
                foreach (var slot in GearVisual.Shown)
                {
                    var it = GameData.MakeItem(slot, tier >= 5 ? Rarity.Epic : tier >= 3 ? Rarity.Rare : Rarity.Common, 10 * tier, tier, p.Data.classId);
                    it.plus = tier >= 4 ? 9 : 0;
                    p.Data.equipped[(int)slot] = it;
                }
                p.RefreshLook();
                p.Commit();
                HUD.Instance.CloseAll();
                HUD.Instance.Open("Bag");
                yield return new WaitForSecondsRealtime(0.8f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "Gear_T" + tier + ".png"));
                yield return new WaitForSecondsRealtime(0.4f);
                HUD.Instance.CloseAll();
                yield return new WaitForSecondsRealtime(0.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "GearWorld_T" + tier + ".png"));
                yield return new WaitForSecondsRealtime(0.4f);
            }
            for (int i = 0; i < before.Count; i++) p.Data.equipped[i] = before[i];
            p.RefreshLook();
            p.Commit();
            Debug.Log("[UIShots] Gear shots saved to " + dir);
        }

        static IEnumerator Rest(string dir)
        {
            float t = 0;
            while (HUD.Instance == null && t < 10) { t += 0.2f; yield return new WaitForSecondsRealtime(0.2f); }
            yield return new WaitForSecondsRealtime(1.5f);
            string[] windows = { null, "Menu", "Bag", "Character", "Events", "Map", "Social", "Shop" };
            foreach (var w in windows)
            {
                if (w == "Menu") HUD.Instance.ShowMenu(true);
                else if (w != null) HUD.Instance.Open(w);
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, (w ?? "HUD") + ".png"));
                yield return new WaitForSecondsRealtime(0.6f);
                HUD.Instance.CloseAll();
            }
            // Let auto-battle find a monster, so the target frame and a fight are in the last shot.
            Player.Instance.AutoBattle = true;
            yield return new WaitForSecondsRealtime(9f);
            HUD.Instance.CloseAll(); // walking past the merchant opens the shop
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "Fight.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            // A close look at every model in Aurelion: stand next to each zone and take a picture.
            var p = Player.Instance;
            p.AutoBattle = false;
            int before = Maps.Current;
            Maps.Enter(p, 0, true);
            yield return new WaitForSecondsRealtime(1f);
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var z in Maps.ZonesOf(0))
            {
                if (!seen.Add(z.ModelName)) continue;
                p.Teleport(z.Center + new Vector3(0, 0, -z.Radius - 3f), Maps.All[0].Bounds);
                yield return new WaitForSecondsRealtime(2f);
                HUD.Instance.CloseAll();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "Zone_" + z.ModelName + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Maps.Enter(p, before, true);
            Debug.Log("[UIShots] Saved to " + dir);
        }
    }
}
