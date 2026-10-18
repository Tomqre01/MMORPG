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

    public class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        public Vector3 Offset = new Vector3(0, 13f, -10f);

        void LateUpdate()
        {
            if (Target == null) return;
            transform.position = Vector3.Lerp(transform.position, Target.position + Offset, 8f * Time.deltaTime);
            transform.rotation = Quaternion.LookRotation(-Offset);
        }
    }

    // Expanding disc used as a skill effect.
    public class Effect : MonoBehaviour
    {
        float size, t;

        public static void Ring(Vector3 position, float radius, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = new Vector3(position.x, 0.05f, position.z);
            go.transform.localScale = new Vector3(0.1f, 0.02f, 0.1f);
            Paint.Color(go, color);
            go.AddComponent<Effect>().size = radius * 2f;
        }

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

            Maps.BuildAll();
            var player = Player.Create(Vector3.zero);

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.gameObject.AddComponent<CameraFollow>().Target = player.transform;

            HUD.Create();
            int map = Mathf.Clamp(player.Data.mapId, 0, Maps.All.Length - 1);
            if (!Maps.CanEnter(player.Data, Maps.All[map], out _)) map = 0;
            Maps.Enter(player, map, true);
            cam.transform.position = player.transform.position + new Vector3(0, 13f, -10f);

            WorldSync.Create();
            WorldBoss.Create();
            Activities.Create();
        }
    }
}
