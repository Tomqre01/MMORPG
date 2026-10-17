using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public class ZoneDef
    {
        public string Name, Model;
        public int Level, Count;
        public Vector3 Offset;     // from the map centre
        public float Radius, Scale;
        public Color Tint = Color.white;
    }

    public class MapDef
    {
        public int Id;
        public string Name;
        public int MinLevel, MinRebirth, Cost;
        public Vector3 Center;
        public Color Ground, Town, Tree, Trunk;
        public ZoneDef[] Zones;
        public Rect Bounds => new Rect(Center.x - 48f, Center.z - 48f, 96f, 96f);
    }

    // The MU Origin maps. They all live in the one scene, 1000 units apart; only the map you are on
    // is shown and has monsters, the rest sleep.
    public static class Maps
    {
        static ZoneDef Z(string name, string model, int lv, float x, float z, int count, float scale, Color? tint = null, float radius = 7f) =>
            new ZoneDef { Name = name, Model = model, Level = lv, Offset = new Vector3(x, 0, z), Count = count, Scale = scale, Radius = radius, Tint = tint ?? Color.white };

        public static readonly MapDef[] All =
        {
            new MapDef
            {
                Id = 0, Name = "Lorencia", MinLevel = 1, Cost = 0, Center = Vector3.zero,
                Ground = new Color(0.32f, 0.5f, 0.27f), Town = new Color(0.62f, 0.58f, 0.5f), Tree = new Color(0.18f, 0.42f, 0.2f), Trunk = new Color(0.4f, 0.28f, 0.15f),
                Zones = new[]
                {
                    Z("Slime", "Slime", 1, 0, 16, 8, 0.8f),
                    Z("Turtle", "Turtle", 3, -24, 2, 6, 0.8f),
                    Z("Goblin", "Goblin", 5, -24, 26, 8, 1f, null, 8f),
                    Z("Orc", "Orc", 10, 24, 28, 6, 1.3f, null, 8f),
                    Z("Golem", "Golem", 20, 0, -30, 3, 2f),
                },
            },
            new MapDef
            {
                Id = 1, Name = "Noria", MinLevel = 15, Cost = 500, Center = new Vector3(0, 0, 1000f),
                Ground = new Color(0.2f, 0.42f, 0.22f), Town = new Color(0.55f, 0.5f, 0.38f), Tree = new Color(0.1f, 0.32f, 0.12f), Trunk = new Color(0.32f, 0.22f, 0.12f),
                Zones = new[]
                {
                    Z("Elite Goblin", "Goblin", 15, -22, 18, 8, 1.2f, new Color(0.6f, 1f, 0.6f)),
                    Z("Forest Orc", "Orc", 22, 24, 20, 7, 1.4f, new Color(0.6f, 0.9f, 0.5f)),
                    Z("Moss Golem", "Golem", 28, 0, -28, 4, 2.1f, new Color(0.55f, 0.85f, 0.5f)),
                },
            },
            new MapDef
            {
                Id = 2, Name = "Devias", MinLevel = 30, Cost = 1500, Center = new Vector3(0, 0, 2000f),
                Ground = new Color(0.85f, 0.88f, 0.92f), Town = new Color(0.7f, 0.72f, 0.78f), Tree = new Color(0.75f, 0.85f, 0.85f), Trunk = new Color(0.35f, 0.3f, 0.28f),
                Zones = new[]
                {
                    Z("Ice Turtle", "Turtle", 32, -24, 16, 7, 1f, new Color(0.6f, 0.85f, 1f)),
                    Z("Yeti", "Golem", 38, 24, 18, 5, 2f, new Color(1.2f, 1.2f, 1.25f)),
                    Z("Orc Warlord", "Orc", 45, 0, -28, 6, 1.6f, new Color(1f, 0.55f, 0.55f)),
                },
            },
            new MapDef
            {
                Id = 3, Name = "Lost Tower", MinLevel = 50, Cost = 3000, Center = new Vector3(0, 0, 3000f),
                Ground = new Color(0.22f, 0.18f, 0.26f), Town = new Color(0.38f, 0.32f, 0.42f), Tree = new Color(0.32f, 0.2f, 0.38f), Trunk = new Color(0.2f, 0.15f, 0.2f),
                Zones = new[]
                {
                    Z("Shadow Goblin", "Goblin", 50, -24, 16, 8, 1.3f, new Color(0.55f, 0.45f, 0.75f)),
                    Z("Stone Golem", "Golem", 55, 24, 16, 4, 2.3f, new Color(0.6f, 0.6f, 0.75f)),
                    Z("Death Orc", "Orc", 62, 0, -28, 6, 1.7f, new Color(0.5f, 0.4f, 0.5f)),
                },
            },
            new MapDef
            {
                Id = 4, Name = "Atlans", MinLevel = 65, Cost = 5000, Center = new Vector3(0, 0, 4000f),
                Ground = new Color(0.15f, 0.35f, 0.5f), Town = new Color(0.55f, 0.65f, 0.7f), Tree = new Color(0.2f, 0.55f, 0.55f), Trunk = new Color(0.3f, 0.35f, 0.4f),
                Zones = new[]
                {
                    Z("Sea Slime", "Slime", 65, -24, 16, 8, 1.4f, new Color(0.5f, 0.8f, 1.2f)),
                    Z("Shell Lord", "Turtle", 72, 24, 16, 6, 1.8f, new Color(0.7f, 0.5f, 1f)),
                    Z("Coral Golem", "Golem", 78, 0, -28, 4, 2.3f, new Color(1f, 0.6f, 0.6f)),
                },
            },
            new MapDef
            {
                Id = 5, Name = "Tarkan", MinLevel = 80, Cost = 8000, Center = new Vector3(0, 0, 5000f),
                Ground = new Color(0.78f, 0.66f, 0.42f), Town = new Color(0.7f, 0.6f, 0.45f), Tree = new Color(0.45f, 0.55f, 0.25f), Trunk = new Color(0.45f, 0.35f, 0.2f),
                Zones = new[]
                {
                    Z("Sand Orc", "Orc", 82, -24, 16, 7, 1.7f, new Color(1f, 0.85f, 0.55f)),
                    Z("Desert Golem", "Golem", 88, 24, 16, 4, 2.4f, new Color(1f, 0.8f, 0.5f)),
                    Z("Tarkan Turtle", "Turtle", 95, 0, -28, 6, 1.9f, new Color(0.9f, 0.7f, 0.4f)),
                },
            },
            new MapDef
            {
                Id = 6, Name = "Icarus", MinLevel = 1, MinRebirth = 1, Cost = 12000, Center = new Vector3(0, 0, 6000f),
                Ground = new Color(0.62f, 0.75f, 0.9f), Town = new Color(0.9f, 0.9f, 0.95f), Tree = new Color(0.95f, 0.95f, 1f), Trunk = new Color(0.7f, 0.7f, 0.8f),
                Zones = new[]
                {
                    Z("Sky Slime", "Slime", 100, -24, 16, 8, 1.5f, new Color(1.2f, 1.2f, 1.3f)),
                    Z("Phoenix Orc", "Orc", 105, 24, 16, 6, 1.8f, new Color(1.3f, 0.6f, 0.3f)),
                    Z("Sky Golem", "Golem", 110, 0, -28, 4, 2.5f, new Color(0.9f, 0.95f, 1.3f)),
                },
            },
        };

        static readonly Dictionary<int, GameObject> roots = new Dictionary<int, GameObject>();
        static readonly Dictionary<int, List<MonsterSpawner>> zones = new Dictionary<int, List<MonsterSpawner>>();
        static readonly List<WorldLabel> labels = new List<WorldLabel>();

        public static int Current { get; private set; }
        public static MapDef CurrentDef => All[Current];
        public static IEnumerable<MonsterSpawner> ZonesOf(int map) => zones.TryGetValue(map, out var l) ? l : new List<MonsterSpawner>();

        public static int MapAt(Vector3 pos)
        {
            int best = 0;
            float bestD = float.MaxValue;
            foreach (var m in All)
            {
                float d = Mathf.Abs(pos.z - m.Center.z) + Mathf.Abs(pos.x - m.Center.x);
                if (d < bestD) { bestD = d; best = m.Id; }
            }
            return best;
        }

        public static bool InTown(Vector3 pos)
        {
            var c = All[MapAt(pos)].Center;
            Vector3 d = pos - c;
            d.y = 0;
            return d.magnitude < GameData.TownRadius;
        }

        public static bool CanEnter(PlayerSave d, MapDef m, out string why)
        {
            why = null;
            if (d.rebirth < m.MinRebirth) why = "Needs " + m.MinRebirth + " Rebirth";
            else if (d.rebirth == 0 && d.level < m.MinLevel) why = "Needs Lv " + m.MinLevel;
            return why == null;
        }

        // Builds every map (ground, town, trees, zones). Monsters only appear on the map you enter.
        public static void BuildAll()
        {
            MonsterSpawner.Field.Clear();
            foreach (var m in All)
            {
                var root = new GameObject("Map " + m.Name);
                roots[m.Id] = root;
                var t = root.transform;

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.SetParent(t, false);
                ground.transform.position = m.Center;
                ground.transform.localScale = new Vector3(10, 1, 10);
                Paint.Color(ground, m.Ground);

                BuildTown(m, t);

                var list = new List<MonsterSpawner>();
                foreach (var z in m.Zones)
                {
                    var s = MonsterSpawner.Create(z.Name, z.Level, m.Center + z.Offset, z.Radius, z.Count,
                        Color.Lerp(m.Ground, Color.black, 0.25f), z.Scale, true, z.Model, z.Tint);
                    s.Map = m.Id;
                    s.Bounds = m.Bounds;
                    s.transform.SetParent(t, true);
                    s.SetActive(false);
                    list.Add(s);
                    MonsterSpawner.Field.Add(s);
                }
                zones[m.Id] = list;

                var keepClear = new List<(Vector3, float)> { (m.Center, GameData.TownRadius + 3f) };
                if (m.Id == 0) keepClear.Add((WorldBoss.Lair, 8f));
                foreach (var s in list) keepClear.Add((s.Center, s.Radius * 1.2f + 2.5f));
                PlantTrees(m, t, keepClear);
                root.SetActive(false);
            }
        }

        static void BuildTown(MapDef m, Transform parent)
        {
            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(plaza.GetComponent<Collider>());
            plaza.name = "Town";
            plaza.transform.SetParent(parent, false);
            plaza.transform.position = m.Center + new Vector3(0, 0.015f, 0);
            plaza.transform.localScale = new Vector3(GameData.TownRadius * 2f, 0.01f, GameData.TownRadius * 2f);
            Paint.Color(plaza, m.Town);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(post.GetComponent<Collider>());
                post.transform.SetParent(parent, false);
                post.transform.position = m.Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * GameData.TownRadius + Vector3.up * 0.6f;
                post.transform.localScale = new Vector3(0.35f, 0.6f, 0.35f);
                Paint.Color(post, Color.Lerp(m.Town, Color.white, 0.4f));
            }
            // Merchant NPC: walk up to it to open the shop.
            var npc = new GameObject("Merchant");
            npc.transform.SetParent(parent, false);
            npc.transform.position = m.Center + new Vector3(3.5f, 0, 2.5f);
            var bodyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(bodyGo.GetComponent<Collider>());
            bodyGo.transform.SetParent(npc.transform, false);
            bodyGo.transform.localPosition = Vector3.up;
            bodyGo.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            Paint.Color(bodyGo, new Color(0.85f, 0.55f, 0.3f));
            npc.AddComponent<MerchantNpc>();
            labels.Add(WorldLabel.Create(npc.transform.position, "Merchant", new Color(1f, 0.85f, 0.4f), 2.6f, 26));
            labels.Add(WorldLabel.Create(m.Center + new Vector3(0, 0, GameData.TownRadius - 1f), m.Name + " (safe zone)", new Color(1f, 0.95f, 0.7f), 3f));
        }

        static void PlantTrees(MapDef m, Transform parent, List<(Vector3 center, float radius)> keepClear)
        {
            var state = Random.state;
            Random.InitState(42 + m.Id);
            for (int i = 0; i < 60; i++)
            {
                var p = m.Center + new Vector3(Random.Range(-47f, 47f), 0, Random.Range(-47f, 47f));
                bool blocked = false;
                foreach (var (c, r) in keepClear)
                {
                    Vector3 d = p - c;
                    d.y = 0;
                    if (d.magnitude < r) { blocked = true; break; }
                }
                if (blocked) continue;
                if (m.Id == 0 && ModelLibrary.Find("Tree") != null)
                {
                    var holder = new GameObject("Tree").transform;
                    holder.SetParent(parent, false);
                    holder.position = p;
                    holder.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    ModelLibrary.Spawn("Tree", holder, Random.Range(3.5f, 5.5f), out _, procedural: false);
                    continue;
                }
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(trunk.GetComponent<Collider>());
                trunk.transform.SetParent(parent, false);
                trunk.transform.position = p + Vector3.up;
                trunk.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
                Paint.Color(trunk, m.Trunk);
                var top = GameObject.CreatePrimitive(m.Id == 2 ? PrimitiveType.Capsule : PrimitiveType.Sphere);
                Object.Destroy(top.GetComponent<Collider>());
                top.transform.SetParent(parent, false);
                top.transform.position = p + Vector3.up * 2.6f;
                top.transform.localScale = Vector3.one * Random.Range(1.6f, 2.4f);
                Paint.Color(top, m.Tree);
            }
            Random.state = state;
        }

        // Shows map id, wakes its monsters, and puts the player in its town.
        public static void Show(int id)
        {
            foreach (var kv in roots) kv.Value.SetActive(kv.Key == id);
            foreach (var kv in zones)
                foreach (var s in kv.Value) s.SetActive(kv.Key == id);
            Current = id;
        }

        public static string Enter(Player p, int id, bool free)
        {
            if (DungeonRun.Active != null) return "Leave the dungeon first";
            var m = All[id];
            if (!CanEnter(p.Data, m, out string why)) return why;
            if (!free && id != Current)
            {
                if (p.Data.gold < m.Cost) return "Need " + m.Cost + " Zen";
                p.Data.gold -= m.Cost;
            }
            Show(id);
            p.Data.mapId = id;
            p.Teleport(m.Center + new Vector3(0, 0, -2f), m.Bounds);
            p.Commit();
            return "Welcome to " + m.Name;
        }

        public static void ReturnToTown(Player p)
        {
            var m = CurrentDef;
            if (!roots[m.Id].activeSelf) Show(m.Id);
            p.Teleport(m.Center + new Vector3(0, 0, -2f), m.Bounds);
        }

        // Strongest open-world zone the player can handle, on a map they may enter.
        public static MonsterSpawner BestFor(PlayerSave d)
        {
            MonsterSpawner best = null;
            int effective = d.rebirth > 0 ? GameData.MaxLevel + d.level : d.level;
            foreach (var m in All)
            {
                if (!CanEnter(d, m, out _)) continue;
                foreach (var z in ZonesOf(m.Id))
                    if (z.Level <= effective + 2 && (best == null || z.Level > best.Level)) best = z;
            }
            return best ?? MonsterSpawner.Field[0];
        }
    }

    // Walk up to the town merchant to open the shop.
    public class MerchantNpc : MonoBehaviour
    {
        bool near;

        void Update()
        {
            var p = Player.Instance;
            if (p == null || HUD.Instance == null) return;
            Vector3 d = p.transform.position - transform.position;
            d.y = 0;
            bool now = d.magnitude < 2.5f;
            if (now && !near) HUD.Instance.Open("Shop");
            near = now;
        }
    }
}
