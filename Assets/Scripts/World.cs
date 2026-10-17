using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // Keeps a zone filled with monsters of one level.
    public class MonsterSpawner : MonoBehaviour
    {
        public string MonsterName;
        public int Level;
        public int Count;
        public float Radius;
        public float Scale;
        public Color Color;

        public Rect Bounds = Combatant.FieldBounds;
        public event System.Action<Monster> Died;

        int alive;
        float timer;

        public static MonsterSpawner Create(string monsterName, int level, Vector3 center, float radius, int count, Color color, float scale, bool showZone = true)
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
            s.Level = level;
            s.Count = count;
            s.Radius = radius;
            s.Scale = scale;
            s.Color = color;
            return s;
        }

        void Start()
        {
            for (int i = 0; i < Count; i++) Spawn();
        }

        void Update()
        {
            if (alive >= Count) return;
            timer -= Time.deltaTime;
            if (timer > 0) return;
            timer = 2f;
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

        public void NotifyDeath(Monster m)
        {
            alive--;
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

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10, 1, 10);
            Paint.Color(ground, new Color(0.32f, 0.5f, 0.27f));

            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            var player = Player.Create(Vector3.zero);

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.transform.position = player.transform.position + new Vector3(0, 13f, -10f);
            cam.gameObject.AddComponent<CameraFollow>().Target = player.transform;

            var zones = new List<MonsterSpawner>
            {
                MonsterSpawner.Create("Slime", 1, new Vector3(0, 0, 14), 7, 8, new Color(0.4f, 0.9f, 0.4f), 0.8f),
                MonsterSpawner.Create("Turtle", 3, new Vector3(-26, 0, 0), 7, 6, new Color(0.3f, 0.7f, 0.75f), 0.8f),
                MonsterSpawner.Create("Goblin", 5, new Vector3(-24, 0, 26), 8, 8, new Color(0.65f, 0.45f, 0.25f), 1f),
                MonsterSpawner.Create("Orc", 10, new Vector3(24, 0, 28), 8, 6, new Color(0.7f, 0.2f, 0.2f), 1.3f),
                MonsterSpawner.Create("Golem", 20, new Vector3(0, 0, -30), 7, 3, new Color(0.55f, 0.55f, 0.6f), 2f),
            };
            PlantTrees(zones);

            HUD.Create();
        }

        static void PlantTrees(List<MonsterSpawner> zones)
        {
            var state = Random.state;
            Random.InitState(42);
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector3(Random.Range(-47f, 47f), 0, Random.Range(-47f, 47f));
                if (p.magnitude < 6f || InsideZone(p, zones)) continue;
                if (ModelLibrary.Find("Tree") != null)
                {
                    var holder = new GameObject("Tree").transform;
                    holder.position = p;
                    holder.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    ModelLibrary.Spawn("Tree", holder, Random.Range(3.5f, 5.5f), out _, procedural: false);
                    continue;
                }
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(trunk.GetComponent<Collider>());
                trunk.transform.position = p + Vector3.up;
                trunk.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
                Paint.Color(trunk, new Color(0.4f, 0.28f, 0.15f));
                var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(top.GetComponent<Collider>());
                top.transform.position = p + Vector3.up * 2.6f;
                top.transform.localScale = Vector3.one * Random.Range(1.6f, 2.4f);
                Paint.Color(top, new Color(0.18f, 0.42f, 0.2f));
            }
            Random.state = state;
        }

        // Keeps trees off the zone discs (plus a margin for the tree crown) so they never hide monsters.
        static bool InsideZone(Vector3 p, List<MonsterSpawner> zones)
        {
            foreach (var z in zones)
            {
                Vector3 d = p - z.transform.position;
                d.y = 0;
                if (d.magnitude < z.Radius * 1.2f + 2.5f) return true;
            }
            return false;
        }
    }
}
