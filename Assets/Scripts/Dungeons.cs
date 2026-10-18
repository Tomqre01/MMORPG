using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // Shared parts of the two daily dungeons: one run at a time, its own arena far from the field.
    public abstract class DungeonRun : MonoBehaviour
    {
        public static DungeonRun Active { get; protected set; }

        public float TimeLeft { get; protected set; }
        public abstract string Status { get; }
        public virtual Vector3? AutoGoal => null; // where AUTO should walk instead of fighting
        protected bool finished;
        protected int level;
        protected readonly List<MonsterSpawner> spawners = new List<MonsterSpawner>();
        protected readonly List<GameObject> props = new List<GameObject>();

        public void Leave()
        {
            if (!finished) Finish(false);
        }

        protected abstract void Finish(bool won);

        // Monsters like the strongest field zone the player can handle, at the player's level.
        protected MonsterSpawner MakeSpawner(int lv, Vector3 center, float radius, int count, Rect bounds, float scaleMul = 1f)
        {
            var like = MonsterSpawner.BestFor(lv);
            var s = MonsterSpawner.Create(like.MonsterName, lv, center, radius, count, like.Color, like.Scale * scaleMul, false, like.ModelName, like.Tint);
            s.Bounds = bounds;
            spawners.Add(s);
            return s;
        }

        protected void Cleanup()
        {
            Monster.AnyHpLost -= OnAnyHit;
            foreach (var m in new List<Monster>(Monster.All))
                if (spawners.Contains(m.Spawner)) Destroy(m.gameObject);
            foreach (var s in spawners) if (s != null) Destroy(s.gameObject);
            foreach (var g in props) if (g != null) Destroy(g);
            var p = Player.Instance;
            if (p != null && !p.IsDead) p.Teleport(Vector3.zero, Combatant.FieldBounds);
            Active = null;
            Destroy(gameObject);
        }

        protected virtual void OnAnyHit(Monster m, int amount, Combatant source) { }

        protected static GameObject Block(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = scale;
            Paint.Color(go, color);
            return go;
        }

        protected static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }

    // Survival arena: waves keep coming for the whole time. The more damage you deal, the bigger the payout.
    public class DevilSquare : DungeonRun
    {
        public const int Waves = 5;
        const float TimeLimit = 150f, WaveEvery = 30f;
        static readonly Vector3 Center = new Vector3(300f, 0, 0);
        static readonly Rect ArenaBounds = new Rect(286f, -14f, 28f, 28f);
        static GameObject arena;

        MonsterSpawner spawner;
        int wave, alive;
        float waveTimer;
        long score;

        public override string Status => "Devil Square   Wave " + wave + "/" + Waves + "   " + Clock(TimeLeft) + "   Score " + score;

        public static string Enter()
        {
            var p = Player.Instance;
            if (Active != null || p == null || p.IsDead || !p.HasClass) return "Can't enter right now";
            if (!p.UseRun(false)) return "No Devil Square entries left today";
            BuildArena();
            var run = new GameObject("Devil Square").AddComponent<DevilSquare>();
            Active = run;
            run.Begin(p);
            return null;
        }

        static void BuildArena()
        {
            if (arena != null) return;
            arena = new GameObject("Devil Square Arena");
            Block(arena.transform, PrimitiveType.Plane, Center, new Vector3(3.2f, 1, 3.2f), new Color(0.25f, 0.12f, 0.12f));
            Block(arena.transform, PrimitiveType.Cylinder, Center + Vector3.up * 0.02f, new Vector3(18f, 0.01f, 18f), new Color(0.45f, 0.08f, 0.1f));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Block(arena.transform, PrimitiveType.Cube, Center + new Vector3(x * 15f, 2f, z * 15f), new Vector3(1.5f, 4f, 1.5f), new Color(0.35f, 0.2f, 0.25f));
                    Block(arena.transform, PrimitiveType.Sphere, Center + new Vector3(x * 15f, 4.4f, z * 15f), Vector3.one * 0.9f, new Color(1f, 0.4f, 0.1f));
                }
        }

        void Begin(Player p)
        {
            level = Mathf.Max(1, p.Data.level - 2);
            spawner = MakeSpawner(level, Center, 10f, 0, ArenaBounds);
            spawner.Died += _ => alive--;
            Monster.AnyHpLost += OnAnyHit;
            p.Teleport(Center + new Vector3(0, 0, -10f), ArenaBounds);
            p.Refill();
            TimeLeft = TimeLimit;
            NextWave();
        }

        void NextWave()
        {
            wave++;
            waveTimer = WaveEvery;
            spawner.Level = level + wave - 1;
            int n = 4 + wave * 2;
            for (int i = 0; i < n; i++) spawner.Spawn();
            alive += n;
            if (wave == Waves)
            {
                spawner.Spawn().MakeBoss(6f);
                alive++;
                HUD.Message("Final wave: the Devil Square master appears!", 2.5f);
            }
            else HUD.Message("Wave " + wave + " / " + Waves, 2f);
        }

        protected override void OnAnyHit(Monster m, int amount, Combatant source)
        {
            if (finished || m.Spawner != spawner || source == null || source != Player.Instance) return;
            score += amount;
        }

        void Update()
        {
            if (finished) return;
            float dt = Time.deltaTime;
            TimeLeft -= dt;
            waveTimer -= dt;
            var p = Player.Instance;
            if (p == null || p.IsDead) { Finish(false); return; }
            if (TimeLeft <= 0) { Finish(true); return; }
            if (wave < Waves && (waveTimer <= 0 || alive <= 0)) NextWave();
        }

        protected override void Finish(bool won)
        {
            finished = true;
            var p = Player.Instance;
            if (p != null && score > 0)
            {
                float f = won ? 1f : 0.5f;
                float monstersWorth = (float)score / GameData.MonsterHp(level);
                int exp = Mathf.RoundToInt(monstersWorth * GameData.MonsterExp(level) * 0.5f * f);
                int gold = Mathf.RoundToInt(score * 0.1f * f);
                int bless = Mathf.Clamp(Mathf.RoundToInt(monstersWorth / 8f * f), 1, 8);
                int soul = monstersWorth > 25 ? (won ? 2 : 1) : 0;
                p.AddMaterials(gold, exp, bless, soul, 0, 0);
                HUD.Message((won ? "Devil Square survived! " : "Devil Square over. ") + "Score " + score +
                            ":  +" + exp + " EXP  +" + gold + "g  +" + bless + " Bless" + (soul > 0 ? "  +" + soul + " Soul" : ""), 5f);
            }
            else HUD.Message("Devil Square failed", 3f);
            if (won) Quests.OnEvent(p, QuestKind.DevilSquare);
            Cleanup();
        }
    }

    // Timed castle run: clear the bridge, break the gate, destroy the Statue of Saint for the
    // Archangel's Weapon, then bring it back to the Archangel at the start of the bridge.
    public class BloodCastle : DungeonRun
    {
        enum Phase { Bridge, Gate, Statue, Return }

        const float TimeLimit = 300f;
        const int BridgeKills = 25;
        static readonly Vector3 Center = new Vector3(600f, 0, 0);
        static readonly Vector3 AngelPos = new Vector3(600f, 0, -33f);
        static readonly Rect BridgeBounds = new Rect(596f, -36f, 8f, 35f);
        static readonly Rect CastleBounds = new Rect(586f, -36f, 28f, 62f);
        static GameObject castle;

        Phase phase;
        int kills;
        MonsterSpawner bridge, court;

        public override Vector3? AutoGoal => phase == Phase.Return ? AngelPos : (Vector3?)null;

        public override string Status
        {
            get
            {
                string goal = phase == Phase.Bridge ? "Clear the bridge " + kills + "/" + BridgeKills
                            : phase == Phase.Gate ? "Break the Castle Gate"
                            : phase == Phase.Statue ? "Destroy the Statue of Saint"
                            : "Bring the Archangel's Weapon back to the Archangel";
                return "Blood Castle   " + Clock(TimeLeft) + "\n" + goal;
            }
        }

        public static string Enter()
        {
            var p = Player.Instance;
            if (Active != null || p == null || p.IsDead || !p.HasClass) return "Can't enter right now";
            if (!p.UseRun(true)) return "No Blood Castle entries left today";
            BuildCastle();
            var run = new GameObject("Blood Castle").AddComponent<BloodCastle>();
            Active = run;
            run.Begin(p);
            return null;
        }

        static void BuildCastle()
        {
            if (castle != null) return;
            castle = new GameObject("Blood Castle Map");
            var t = castle.transform;
            Block(t, PrimitiveType.Plane, Center + new Vector3(0, -0.3f, -5f), new Vector3(6f, 1, 8f), new Color(0.35f, 0.05f, 0.02f)); // lava below
            Block(t, PrimitiveType.Cube, new Vector3(600f, -0.05f, -18f), new Vector3(8f, 0.1f, 36f), new Color(0.4f, 0.35f, 0.32f));  // bridge
            Block(t, PrimitiveType.Cube, new Vector3(600f, -0.05f, 12f), new Vector3(28f, 0.1f, 28f), new Color(0.32f, 0.28f, 0.3f));  // courtyard
            for (int i = 0; i < 6; i++)
                for (int side = -1; side <= 1; side += 2)
                    Block(t, PrimitiveType.Cube, new Vector3(600f + side * 4.2f, 0.5f, -33f + i * 6f), new Vector3(0.5f, 1f, 0.5f), new Color(0.5f, 0.15f, 0.12f));
            for (int side = -1; side <= 1; side += 2)
            {
                Block(t, PrimitiveType.Cube, new Vector3(600f + side * 9f, 1f, -1f), new Vector3(10f, 2f, 1.5f), new Color(0.38f, 0.32f, 0.36f));  // walls by the gate
                Block(t, PrimitiveType.Cube, new Vector3(600f + side * 14.5f, 0.75f, 12f), new Vector3(1f, 1.5f, 28f), new Color(0.38f, 0.32f, 0.36f));
            }
            Block(t, PrimitiveType.Cube, new Vector3(600f, 1.5f, 26.5f), new Vector3(30f, 3f, 1f), new Color(0.38f, 0.32f, 0.36f));
        }

        void Begin(Player p)
        {
            level = Mathf.Max(1, p.Data.level);
            bridge = MakeSpawner(level, new Vector3(600f, 0, -16f), 3f, 7, BridgeBounds);
            bridge.Died += _ => OnBridgeKill();

            // The Archangel waits at the start of the bridge.
            var angel = new GameObject("Archangel");
            angel.transform.position = AngelPos;
            Block(angel.transform, PrimitiveType.Capsule, AngelPos + Vector3.up, new Vector3(0.9f, 1f, 0.9f), new Color(0.95f, 0.95f, 1f));
            Player.MakeWings(angel.transform, 4, 5);
            props.Add(angel);
            props.Add(WorldLabel.Create(AngelPos, "Archangel", new Color(1f, 0.9f, 0.5f), 2.8f).gameObject);

            p.Teleport(AngelPos + new Vector3(0, 0, 2f), BridgeBounds);
            p.Refill();
            TimeLeft = TimeLimit;
            phase = Phase.Bridge;
            HUD.Message("Blood Castle: clear the bridge!", 2.5f);
        }

        void OnBridgeKill()
        {
            if (phase != Phase.Bridge || finished) return;
            kills++;
            if (kills < BridgeKills) return;
            phase = Phase.Gate;
            bridge.Count = 0;
            var gateSpawner = MakeSpawner(level, new Vector3(600f, 0, -1f), 0.1f, 0, BridgeBounds);
            var gate = Monster.CreateStructure(gateSpawner, new Vector3(600f, 0, -1f), "Castle Gate", GameData.MonsterHp(level) * 15, root =>
            {
                Block(root, PrimitiveType.Cube, root.position + Vector3.up * 2.5f, new Vector3(8f, 5f, 1.2f), new Color(0.45f, 0.28f, 0.15f));
                Block(root, PrimitiveType.Cube, root.position + Vector3.up * 5.2f, new Vector3(8.6f, 0.5f, 1.4f), new Color(0.3f, 0.3f, 0.32f));
                return 5.5f;
            });
            gate.Bounds = BridgeBounds;
            gateSpawner.Died += _ => OnGateDown();
            HUD.Message("The bridge is clear! Break the Castle Gate", 2.5f);
        }

        void OnGateDown()
        {
            if (finished) return;
            phase = Phase.Statue;
            var p = Player.Instance;
            if (p != null) p.Bounds = CastleBounds;
            court = MakeSpawner(level + 2, new Vector3(600f, 0, 12f), 8f, 10, CastleBounds);
            var statueSpawner = MakeSpawner(level + 2, new Vector3(600f, 0, 21f), 0.1f, 0, CastleBounds);
            var statue = Monster.CreateStructure(statueSpawner, new Vector3(600f, 0, 21f), "Statue of Saint", GameData.MonsterHp(level) * 20, root =>
            {
                Block(root, PrimitiveType.Cylinder, root.position + Vector3.up * 0.5f, new Vector3(2.4f, 0.5f, 2.4f), new Color(0.55f, 0.55f, 0.6f));
                Block(root, PrimitiveType.Capsule, root.position + Vector3.up * 2.2f, new Vector3(1.1f, 1.3f, 1.1f), new Color(0.85f, 0.85f, 0.9f));
                Block(root, PrimitiveType.Sphere, root.position + Vector3.up * 3.9f, Vector3.one * 0.7f, new Color(1f, 0.9f, 0.5f));
                return 4.3f;
            });
            statue.Bounds = CastleBounds;
            statueSpawner.Died += _ => OnStatueDown();
            HUD.Message("The gate is down! Destroy the Statue of Saint", 2.5f);
        }

        void OnStatueDown()
        {
            if (finished) return;
            phase = Phase.Return;
            if (court != null) court.Count = 0;
            HUD.Message("You got the Archangel's Weapon! Bring it back to the Archangel", 3.5f);
        }

        void Update()
        {
            if (finished) return;
            TimeLeft -= Time.deltaTime;
            var p = Player.Instance;
            if (p == null || p.IsDead || TimeLeft <= 0) { Finish(false); return; }
            if (phase == Phase.Return)
            {
                Vector3 d = p.transform.position - AngelPos;
                d.y = 0;
                if (d.magnitude < 3.5f) Finish(true);
            }
        }

        protected override void Finish(bool won)
        {
            finished = true;
            var p = Player.Instance;
            if (won && p != null)
            {
                int exp = GameData.MonsterExp(level) * 15;
                int gold = 1500 + level * 80;
                int soul = 1 + (Random.value < 0.5f ? 1 : 0);
                int flames = Random.value < 0.3f ? 1 : 0;
                p.AddMaterials(gold, exp, 3, soul, 3, flames);
                HUD.Message("Blood Castle cleared!  +" + exp + " EXP  +" + gold + "g  +3 Bless  +" + soul + " Soul  +3 Feathers" +
                            (flames > 0 ? "  +1 Condor Flame" : ""), 5f);
                Quests.OnEvent(p, QuestKind.BloodCastle);
            }
            else HUD.Message(TimeLeft <= 0 ? "Time's up! Blood Castle failed" : "Blood Castle failed", 3f);
            Cleanup();
        }
    }
}
