using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MuProto
{
    // Open-world boss on a fixed clock (every 15 minutes, by real UTC time, so every player sees
    // the same schedule). Everyone fights the same HP: each game sends its own running damage total
    // and applies everyone else's. Whoever dealt the most damage gets the best drop.
    public class WorldBoss : MonoBehaviour
    {
        public static WorldBoss Instance { get; private set; }
        public static readonly Vector3 Lair = new Vector3(-40f, 0, -8f);

        const long CycleSeconds = 900;  // a boss every 15 minutes
        const long AliveSeconds = 300;  // it leaves after 5 minutes if nobody kills it
        const float SendEvery = 0.3f;

        struct BossDef
        {
            public string Name, Model;
            public int Level;
            public float Scale, HpMul;
            public Color Tint;
        }

        static readonly BossDef SkeletonKing = new BossDef
            { Name = "Skeleton King", Model = "Golem", Level = 45, Scale = 3.4f, HpMul = 30f, Tint = new Color(0.85f, 0.85f, 0.75f) };
        static readonly BossDef Gorgon = new BossDef
            { Name = "Gorgon", Model = "Orc", Level = 75, Scale = 3.6f, HpMul = 30f, Tint = new Color(0.55f, 1f, 0.55f) };

        static BossDef DefFor(long cycle) => cycle % 2 == 0 ? SkeletonKing : Gorgon;
        static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        MonsterSpawner spawner;
        Monster boss;
        long bossCycle = -1, doneCycle = -1;
        int myDamage;
        bool sendPending, joined;
        float sendTimer;
        readonly Dictionary<string, int> others = new Dictionary<string, int>();
        WorldLabel label;

        public Monster Boss => boss != null && !boss.IsDead ? boss : null;
        public MonsterSpawner Zone => spawner;

        public static void Create()
        {
            if (Instance == null) new GameObject("World Boss").AddComponent<WorldBoss>();
        }

        void Awake()
        {
            Instance = this;
            var lair = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(lair.GetComponent<Collider>());
            lair.name = "World Boss Lair";
            lair.transform.position = new Vector3(Lair.x, 0.012f, Lair.z);
            lair.transform.localScale = new Vector3(14f, 0.01f, 14f);
            Paint.Color(lair, new Color(0.22f, 0.15f, 0.25f));
            label = WorldLabel.Create(Lair, "World Boss Lair", new Color(0.9f, 0.6f, 1f), 1.5f, 28);
        }

        public string Status
        {
            get
            {
                if (Boss != null)
                    return "World Boss " + DefFor(bossCycle).Name + " is here!  HP " + Mathf.CeilToInt(Boss.HpRatio * 100f) + "%";
                long now = Now;
                long cycle = now / CycleSeconds;
                long next = (cycle + 1) * CycleSeconds - now;
                if (now % CycleSeconds < AliveSeconds && doneCycle != cycle) next = 0;
                var def = DefFor(cycle + (next > 0 ? 1 : 0));
                return next > 0 ? "World Boss " + def.Name + " in " + next / 60 + ":" + (next % 60).ToString("00") : "World Boss " + def.Name + " appearing...";
            }
        }

        void Update()
        {
            long now = Now;
            long cycle = now / CycleSeconds;
            long t = now % CycleSeconds;

            if (boss == null && bossCycle != cycle && doneCycle != cycle && t < AliveSeconds) Spawn(cycle);
            if (Boss != null && (cycle != bossCycle || t >= AliveSeconds)) Despawn();

            sendTimer -= Time.deltaTime;
            if (sendPending && sendTimer <= 0)
            {
                sendPending = false;
                sendTimer = SendEvery;
                if (WorldSync.Instance != null) WorldSync.Instance.SendBossDamage(bossCycle, myDamage);
            }
            label.SetText(Boss != null ? DefFor(bossCycle).Name + "  (World Boss)" : "World Boss Lair");
        }

        void Spawn(long cycle)
        {
            var def = DefFor(cycle);
            bossCycle = cycle;
            myDamage = 0;
            joined = false;
            others.Clear();
            spawner = MonsterSpawner.Create(def.Name, def.Level, Lair, 6f, 0, new Color(0.4f, 0.3f, 0.4f), def.Scale, false, def.Model, def.Tint);
            boss = spawner.Spawn();
            boss.MakeWorldBoss(def.HpMul);
            boss.HpLost += OnLocalHit;
            spawner.Died += _ => OnBossDied();
            HUD.Message("World Boss " + def.Name + " has appeared at the lair (west)!", 4f);
        }

        void Despawn()
        {
            doneCycle = bossCycle;
            if (boss != null) Destroy(boss.gameObject);
            if (spawner != null) Destroy(spawner.gameObject);
            boss = null;
            HUD.Message("The World Boss has left...", 3f);
        }

        void OnLocalHit(int amount, Combatant source)
        {
            if (source == null || source != Player.Instance) return;
            myDamage += amount;
            sendPending = true;
            if (!joined)
            {
                joined = true;
                Quests.OnEvent(Player.Instance, QuestKind.WorldBoss);
            }
        }

        // Another player's running damage total for this boss.
        public void OnRemoteDamage(string session, string name, long cycle, int total)
        {
            if (cycle != bossCycle || Boss == null) return;
            others.TryGetValue(session, out int before);
            if (total <= before) return;
            others[session] = total;
            boss.ApplyExternalDamage(total - before);
        }

        void OnBossDied()
        {
            doneCycle = bossCycle;
            var def = DefFor(bossCycle);
            var p = Player.Instance;
            int maxHp = boss != null ? boss.MaxHp : 1;
            boss = null;
            if (spawner != null) Destroy(spawner.gameObject, 2f);
            if (p == null) return;
            if (myDamage >= maxHp * 0.02f) p.Data.bossKills++;
            if (myDamage < maxHp * 0.02f)
            {
                HUD.Message(def.Name + " was defeated by other players", 3f);
                return;
            }
            int exp = GameData.MonsterExp(def.Level) * 20;
            p.AddMaterials(3000 + def.Level * 50, exp, 3, 2, 3, 1);
            int best = others.Count > 0 ? others.Values.Max() : 0;
            string msg = def.Name + " defeated!  +" + exp + " EXP  +3 Bless  +2 Soul  +1 Condor Flame";
            if (myDamage >= best)
            {
                p.AddMaterials(0, 0, 0, 3, 0, 2);
                var drop = GameData.MakeItem((Slot)Random.Range(0, 4), Rarity.Epic, def.Level, 1 + Mathf.Min(p.Data.rebirth, GameData.MaxTier - 1), p.Data.classId);
                p.GiveItem(drop, p.transform.position + Vector3.up * 3f);
                msg += "\nTop damage! +3 Soul  +2 Flame  " + drop.name;
            }
            HUD.Message(msg, 6f);
        }
    }
}
