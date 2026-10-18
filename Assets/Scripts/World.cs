using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // Keeps a zone filled with monsters of one level.
    public class MonsterSpawner : MonoBehaviour
    {
        public static readonly List<MonsterSpawner> Field = new List<MonsterSpawner>(); // open-world zones, by level

        public int Map = -1;         // map id for open-world zones (woken by Maps.Show); -1 = dungeon / event
        public bool Elite;           // a single named elite: boss-strength, respawns after a minute
        public string MonsterName;   // shown and used by quests
        public string ModelName;     // Resources/Models/<ModelName>
        public Color Tint = Color.white;
        public int Level;
        public int Count;
        public float Radius;
        public float Scale;
        public Color Color;

        public Rect Bounds = Combatant.FieldBounds;
        public event System.Action<Monster> Died;

        int alive;
        float timer;

        public static MonsterSpawner Create(string monsterName, int level, Vector3 center, float radius, int count, Color color, float scale,
            bool showZone = true, string model = null, Color? tint = null)
        {
            GameObject zone;
            if (showZone)
            {
                zone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(zone.GetComponent<Collider>());
                zone.transform.position = new Vector3(center.x, 0.01f, center.z);
                zone.transform.localScale = new Vector3(radius * 2.4f, 0.01f, radius * 2.4f);
                Paint.Color(zone, Color.Lerp(color, new Color(0.2f, 0.25f, 0.15f), 0.7f));
            }
            else
            {
                zone = new GameObject();
                zone.transform.position = center;
            }
            zone.name = monsterName + " Zone";

            var s = zone.AddComponent<MonsterSpawner>();
            s.MonsterName = monsterName;
            s.ModelName = model ?? monsterName;
            s.Tint = tint ?? Color.white;
            s.Level = level;
            s.Count = count;
            s.Radius = radius;
            s.Scale = scale;
            s.Color = color;
            return s;
        }

        bool active = true;

        void Start()
        {
            if (Map < 0) for (int i = 0; i < Count; i++) Spawn();
        }

        // Map zones sleep while you are on another map: no monsters, no respawns.
        public void SetActive(bool on)
        {
            if (on == active) return;
            active = on;
            if (!on)
            {
                foreach (var m in new List<Monster>(Monster.All))
                    if (m.Spawner == this) Destroy(m.gameObject);
                alive = 0;
            }
            else for (int i = 0; i < Count; i++) Spawn();
        }

        void Update()
        {
            if (!active || alive >= Count) return;
            timer -= Time.deltaTime;
            if (timer > 0) return;
            timer = Elite ? 60f : 2f;
            Spawn();
        }

        public Monster Spawn()
        {
            Vector2 r = Random.insideUnitCircle * Radius;
            var m = Monster.Create(this, new Vector3(transform.position.x + r.x, 0, transform.position.z + r.y));
            m.Bounds = Bounds;
            alive++;
            return m;
        }

        public void Track() => alive++; // a monster made outside Spawn (structures)

        public Vector3 Center => transform.position;

        // Highest zone at or below this level (dungeons copy its look).
        public static MonsterSpawner BestFor(int level)
        {
            MonsterSpawner best = null;
            foreach (var z in Field)
                if (!z.Elite && z.Level <= level + 2 && (best == null || z.Level > best.Level)) best = z;
            return best ?? (Field.Count > 0 ? Field[0] : null);
        }

        public static MonsterSpawner Named(string name)
        {
            foreach (var z in Field) if (z.MonsterName == name) return z;
            return null;
        }

        public void NotifyDeath(Monster m)
        {
            alive--;
            if (Elite) timer = 60f;
            Died?.Invoke(m);
        }
    }

    // Third-person follow camera, close behind the player and looking down at a slant, like most
    // mobile MMOs. Zoom with the mouse wheel or a two-finger pinch (outside the HUD): closer also
    // means a lower angle, further a more top-down one.
    public class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        public float Zoom = 0.35f;                 // 0 = closest, 1 = furthest
        const float NearDist = 6f, FarDist = 17f;  // distance from the look point
        const float NearPitch = 30f, FarPitch = 55f;
        const float LookHeight = 1.3f;             // look at the chest, not the feet
        float pinchStart = -1f, zoomAtPinch;
        static float shakeAmp, shakeLen, shakeLeft;

        // A short rattle for heavy blows.
        public static void Shake(float amplitude, float seconds)
        {
            if (shakeLeft > 0f && shakeAmp * shakeLeft / shakeLen > amplitude) return; // a bigger one is still going
            shakeAmp = amplitude;
            shakeLen = shakeLeft = seconds;
        }

        public Vector3 Offset
        {
            get
            {
                float pitch = Mathf.Lerp(NearPitch, FarPitch, Zoom) * Mathf.Deg2Rad;
                float dist = Mathf.Lerp(NearDist, FarDist, Zoom);
                return new Vector3(0, Mathf.Sin(pitch) * dist + LookHeight, -Mathf.Cos(pitch) * dist);
            }
        }

        public void SnapToTarget()
        {
            if (Target == null) return;
            transform.position = Target.position + Offset;
            transform.rotation = Quaternion.LookRotation(Target.position + Vector3.up * LookHeight - transform.position);
        }

        void LateUpdate()
        {
            if (Target == null) return;
            ReadZoom();
            Vector3 want = Target.position + Offset;
            // Teleports and map changes jump straight there instead of flying across the world.
            if ((want - transform.position).sqrMagnitude > 30f * 30f) transform.position = want;
            else transform.position = Vector3.Lerp(transform.position, want, 10f * Time.deltaTime);
            Vector3 look = Target.position + Vector3.up * LookHeight - transform.position;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 12f * Time.deltaTime);
            if (shakeLeft > 0f)
            {
                shakeLeft -= Time.unscaledDeltaTime;
                float k = Mathf.Max(0f, shakeLeft / shakeLen);
                transform.position += Random.insideUnitSphere * shakeAmp * k;
            }
        }

        void ReadZoom()
        {
            float wheel = 0f;
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int touches = 0;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null) wheel = mouse.scroll.ReadValue().y / 120f;
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (touches == 0) a = t.position.ReadValue(); else if (touches == 1) b = t.position.ReadValue();
                    touches++;
                }
            }
#else
            wheel = Input.mouseScrollDelta.y;
            touches = Input.touchCount;
            if (touches >= 2) { a = Input.GetTouch(0).position; b = Input.GetTouch(1).position; }
#endif
            if (Mathf.Abs(wheel) > 0.01f && !OverUI(Pointer())) Zoom = Mathf.Clamp01(Zoom - wheel * 0.08f);

            if (touches == 2)
            {
                float d = (a - b).magnitude;
                if (pinchStart < 0f)
                {
                    // Only a pinch on the world: not the joystick plus a skill button.
                    if (OverUI(a) || OverUI(b)) return;
                    pinchStart = d;
                    zoomAtPinch = Zoom;
                }
                else Zoom = Mathf.Clamp01(zoomAtPinch + (pinchStart - d) / Mathf.Max(200f, Screen.height * 0.6f));
            }
            else pinchStart = -1f;
        }

        static Vector2 Pointer()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();

        static bool OverUI(Vector2 screen)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            hits.Clear();
            es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = screen }, hits);
            return hits.Count > 0;
        }
    }

    // Expanding disc used as a skill effect.
    public class Effect : MonoBehaviour
    {
        float size, t;

        // Now drawn as a glowing additive shockwave (Fx); the old solid disc covered the fight.
        public static void Ring(Vector3 position, float radius, Color color) => Fx.Shockwave(position, radius, color);

        void Update()
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(0.1f, size, t / 0.25f);
            transform.localScale = new Vector3(s, 0.02f, s);
            if (t > 0.35f) Destroy(gameObject);
        }
    }

    public static class GameBootstrap
    {
        // Runs automatically when Play starts, in any scene, so no scene setup is needed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Application.targetFrameRate = 60;
            LoginScreen.Show(BuildWorld);
        }

        // Runs after login (or "Play Offline"), so the player loads the save the server sent.
        static void BuildWorld()
        {
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            // A soft haze hides the edge of the map now that the camera looks out at a slant.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.86f);
            RenderSettings.fogStartDistance = 32f;
            RenderSettings.fogEndDistance = 75f;

            Maps.BuildAll();
            var player = Player.Create(Vector3.zero);

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.fieldOfView = 50f;
            cam.farClipPlane = 120f;
            var follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.Target = player.transform;

            HUD.Create();
            int map = Mathf.Clamp(player.Data.mapId, 0, Maps.All.Length - 1);
            if (!Maps.CanEnter(player.Data, Maps.All[map], out _)) map = 0;
            Maps.Enter(player, map, true);
            follow.SnapToTarget();

            WorldSync.Create();
            WorldBoss.Create();
            Activities.Create();
        }
    }
}
