using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public class ZoneDef
    {
        public string Name, Model;
        public int Level, Count;   // Level is GameData power: rebirth * 100 + level
        public bool Elite;         // one tough named monster (ชั้นยอด), respawns slowly
        public Vector3 Offset;     // from the map centre
        public float Radius, Scale;
        public Color Tint = Color.white;
    }

    public class MapDef
    {
        public int Id, ServerCode;
        public string Name;
        public int MinLevel, MinRebirth, Cost;
        public int MinPower => MinRebirth * 100 + MinLevel;
        public string Requirement => MinRebirth > 0 ? MinRebirth + "R Lv" + MinLevel : "Lv " + MinLevel;
        public Vector3 Center;
        public Color Ground, Town, Tree, Trunk, MonsterTint = Color.white;
        public ZoneDef[] Zones;
        public Rect Bounds => new Rect(Center.x - 48f, Center.z - 48f, 96f, 96f);
    }

    // The MU Origin maps. They all live in the one scene, 1000 units apart; only the map you are on
    // is shown and has monsters, the rest sleep.
    public static partial class Maps
    {
        static ZoneDef Z(string name, string model, int lv, float x, float z, int count, float scale, Color? tint = null, float radius = 6f, bool elite = false) =>
            new ZoneDef { Name = name, Model = model, Level = lv, Offset = new Vector3(x, 0, z), Count = count, Scale = scale,
                          Radius = elite ? 2.5f : radius, Tint = tint ?? Color.white, Elite = elite };

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
            why = GameData.Power(d) < m.MinPower ? "Needs " + m.Requirement : null;
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
                        Color.Lerp(m.Ground, Color.black, 0.25f), z.Scale, !z.Elite, z.Model, z.Tint * m.MonsterTint);
                    s.Elite = z.Elite;
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
            int effective = GameData.Power(d);
            foreach (var m in All)
            {
                if (!CanEnter(d, m, out _)) continue;
                foreach (var z in ZonesOf(m.Id))
                    if (!z.Elite && z.Level <= effective + 2 && (best == null || z.Level > best.Level)) best = z;
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
            // Only when you walk up yourself: auto-battle and auto-quest often pass through town.
            if (now && !near && !p.AutoBattle && p.QuestZone == null) HUD.Instance.Open("Shop");
            near = now;
        }
    }
}
