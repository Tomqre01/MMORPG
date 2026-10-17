using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MuProto
{
    // Daily and timed activities: sign-in, online rewards, achievements, ranking, Golden Invasion,
    // plus the Chaos Castle and Arena runs (below).
    public class Activities : MonoBehaviour
    {
        public static Activities Instance { get; private set; }

        // ---- Achievements ----

        public class Achievement
        {
            public string Name;
            public System.Func<PlayerSave, int> Progress;
            public int Target, Diamonds;
        }

        static int Lv(PlayerSave d) => d.rebirth > 0 ? GameData.MaxLevel : d.level;
        static int Bits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }

        public static readonly Achievement[] Achievements =
        {
            new Achievement { Name = "Hunter I: 100 monsters", Progress = d => d.kills, Target = 100, Diamonds = 20 },
            new Achievement { Name = "Hunter II: 1000 monsters", Progress = d => d.kills, Target = 1000, Diamonds = 50 },
            new Achievement { Name = "Hunter III: 5000 monsters", Progress = d => d.kills, Target = 5000, Diamonds = 120 },
            new Achievement { Name = "Reach Lv 50", Progress = Lv, Target = 50, Diamonds = 50 },
            new Achievement { Name = "Reach Lv 100", Progress = Lv, Target = 100, Diamonds = 100 },
            new Achievement { Name = "First Rebirth", Progress = d => d.rebirth, Target = 1, Diamonds = 200 },
            new Achievement { Name = "Enhance to +7", Progress = Growth.MaxPlus, Target = 7, Diamonds = 50 },
            new Achievement { Name = "Enhance to +11", Progress = Growth.MaxPlus, Target = 11, Diamonds = 150 },
            new Achievement { Name = "Wings Tier 2", Progress = d => d.wingTier, Target = 2, Diamonds = 80 },
            new Achievement { Name = "Pet Lv 20", Progress = d => d.petLevel, Target = 20, Diamonds = 60 },
            new Achievement { Name = "Blood Castle x5", Progress = d => d.bcClears, Target = 5, Diamonds = 60 },
            new Achievement { Name = "Devil Square x5", Progress = d => d.dsClears, Target = 5, Diamonds = 60 },
            new Achievement { Name = "Chaos Castle x3", Progress = d => d.ccClears, Target = 3, Diamonds = 60 },
            new Achievement { Name = "World Boss slayer", Progress = d => d.bossKills, Target = 1, Diamonds = 80 },
            new Achievement { Name = "Arena: 10 wins", Progress = d => d.arenaWins, Target = 10, Diamonds = 80 },
            new Achievement { Name = "PK: 3 wins", Progress = d => d.pkKills, Target = 3, Diamonds = 30 },
            new Achievement { Name = "Own a mount", Progress = d => Bits(d.mountsOwned), Target = 1, Diamonds = 40 },
        };

        public static bool Claimed(PlayerSave d, int i) => (d.achievementsClaimed & (1 << i)) != 0;
        public static bool Ready(PlayerSave d, int i) => !Claimed(d, i) && Achievements[i].Progress(d) >= Achievements[i].Target;
        public static int ReadyCount(PlayerSave d) => Enumerable.Range(0, Achievements.Length).Count(i => Ready(d, i));

        public static string ClaimAchievement(Player p, int i)
        {
            var a = Achievements[i];
            if (Claimed(p.Data, i)) return "Already claimed";
            if (!Ready(p.Data, i)) return a.Name + ": " + Mathf.Min(a.Progress(p.Data), a.Target) + "/" + a.Target;
            p.Data.achievementsClaimed |= 1 << i;
            p.Data.diamonds += a.Diamonds;
            p.Commit();
            return "Achievement: " + a.Name + "  +" + a.Diamonds + " Diamonds";
        }

        // ---- Daily sign-in (7-day cycle) and online time rewards ----

        static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

        static readonly string[] SignRewards = { "5000 Zen", "2 Jewels of Bless", "30 HP Potions", "1 Jewel of Soul", "30 Diamonds", "5 Condor Feathers", "100 Diamonds + 1 Condor Flame" };

        public static bool CanSignIn(PlayerSave d) => d.signDate != Today;
        public static string NextSignReward(PlayerSave d) => "Day " + (d.signDays % 7 + 1) + ": " + SignRewards[d.signDays % 7];

        public static string SignIn(Player p)
        {
            var d = p.Data;
            if (!CanSignIn(d)) return "Already signed in today. Come back tomorrow!";
            int day = d.signDays % 7;
            switch (day)
            {
                case 0: d.gold += 5000; break;
                case 1: d.jewelBless += 2; break;
                case 2: d.potions += 30; break;
                case 3: d.jewelSoul += 1; break;
                case 4: d.diamonds += 30; break;
                case 5: d.feathers += 5; break;
                default: d.diamonds += 100; d.flames += 1; break;
            }
            d.signDate = Today;
            d.signDays++;
            p.Commit();
            return "Signed in, day " + (day + 1) + ": " + SignRewards[day];
        }

        public static readonly int[] OnlineMinutes = { 10, 30, 60, 120 };
        static readonly string[] OnlineRewards = { "2000 Zen + 10 Potions", "1 Bless + 10 Diamonds", "1 Soul + 20 Diamonds", "1 Condor Flame + 40 Diamonds" };

        public static string OnlineStatus(PlayerSave d)
        {
            int minutes = Mathf.FloorToInt(d.onlineSeconds / 60f);
            for (int i = 0; i < OnlineMinutes.Length; i++)
                if ((d.onlineClaimed & (1 << i)) == 0)
                    return "Online today " + minutes + " min. Next: " + OnlineMinutes[i] + " min = " + OnlineRewards[i];
            return "Online today " + minutes + " min. All online rewards claimed";
        }

        public static string ClaimOnline(Player p)
        {
            var d = p.Data;
            int minutes = Mathf.FloorToInt(d.onlineSeconds / 60f);
            for (int i = 0; i < OnlineMinutes.Length; i++)
            {
                if ((d.onlineClaimed & (1 << i)) != 0) continue;
                if (minutes < OnlineMinutes[i]) return "Play " + (OnlineMinutes[i] - minutes) + " more minutes";
                switch (i)
                {
                    case 0: d.gold += 2000; d.potions += 10; break;
                    case 1: d.jewelBless += 1; d.diamonds += 10; break;
                    case 2: d.jewelSoul += 1; d.diamonds += 20; break;
                    default: d.flames += 1; d.diamonds += 40; break;
                }
                d.onlineClaimed |= 1 << i;
                p.Commit();
                return "Online reward: " + OnlineRewards[i];
            }
            return "All online rewards claimed today";
        }

        // ---- Ranking (public storage objects on the server) ----

        public List<Online.RankEntry> Ranking { get; private set; } = new List<Online.RankEntry>();
        public string RankingStatus { get; private set; } = "Tap Refresh";
        float rankPushTimer = 5f;

        public static Online.RankEntry MyEntry(Player p) => new Online.RankEntry
        {
            n = string.IsNullOrEmpty(p.Data.charName) ? (Online.Session != null ? Online.Session.Username : "You") : p.Data.charName,
            c = p.Data.classId, lv = p.Data.level, rb = p.Data.rebirth, cp = p.CombatPower,
            atk = p.Atk, def = p.Def, hp = p.MaxHp, ap = p.Data.arenaPoints,
        };

        public async void RefreshRanking(System.Action done = null)
        {
            if (!Online.IsOnline) { RankingStatus = "Ranking needs the server (log in online)"; done?.Invoke(); return; }
            RankingStatus = "Loading...";
            try
            {
                Ranking = await Online.ListRanks();
                RankingStatus = Ranking.Count + " players";
            }
            catch (System.Exception e) { RankingStatus = "Could not load: " + e.Message; }
            done?.Invoke();
        }

        public string RankingText(Player p)
        {
            if (Ranking.Count == 0) return RankingStatus;
            var lines = new List<string> { RankingStatus };
            string me = MyEntry(p).n;
            for (int i = 0; i < Ranking.Count && i < 15; i++)
            {
                var r = Ranking[i];
                string line = (i + 1) + ". " + r.n + "   " + Classes.Get(r.c).Name + "   " + (r.rb > 0 ? r.rb + "R " : "") + "Lv " + r.lv + "   CP " + r.cp + "   Arena " + r.ap;
                lines.Add(r.n == me ? "<color=#ffd24d>" + line + "</color>" : line);
            }
            return string.Join("\n", lines);
        }

        // ---- Golden Invasion: every 30 minutes, golden monsters raid your map for 5 minutes ----

        const long InvasionCycle = 1800, InvasionStart = 600, InvasionLength = 300;
        long invasionCycle = -1;
        readonly List<MonsterSpawner> golden = new List<MonsterSpawner>();

        static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public string InvasionStatus
        {
            get
            {
                long t = Now % InvasionCycle;
                if (t >= InvasionStart && t < InvasionStart + InvasionLength)
                    return "Golden Invasion! " + GoldenLeft + " golden monsters on this map";
                long wait = t < InvasionStart ? InvasionStart - t : InvasionCycle - t + InvasionStart;
                return "Golden Invasion in " + wait / 60 + ":" + (wait % 60).ToString("00");
            }
        }

        int GoldenLeft => Monster.All.Count(m => !m.IsDead && golden.Contains(m.Spawner));

        void StartInvasion()
        {
            var map = Maps.CurrentDef;
            foreach (var z in Maps.ZonesOf(map.Id).Take(3))
            {
                var s = MonsterSpawner.Create("Golden " + z.MonsterName, z.Level + 5, z.Center, z.Radius, 0, new Color(1f, 0.8f, 0.2f),
                    z.Scale * 1.2f, false, z.ModelName, new Color(1.6f, 1.3f, 0.35f));
                s.Bounds = map.Bounds;
                s.Died += m =>
                {
                    var p = Player.Instance;
                    if (p == null) return;
                    p.Data.jewelBless++;
                    p.Data.diamonds += 5;
                    if (Random.value < 0.4f) p.Data.jewelSoul++;
                    FloatingText.Spawn(m.transform.position + Vector3.up * 4f, "Golden loot! +Bless +5 Diamonds", new Color(1f, 0.85f, 0.2f), 30);
                    p.Commit();
                };
                for (int i = 0; i < 2; i++) s.Spawn().MakeBoss(5f);
                golden.Add(s);
            }
            HUD.Message("Golden Invasion! Golden monsters appeared in " + map.Name, 4f);
        }

        void EndInvasion()
        {
            foreach (var m in new List<Monster>(Monster.All)) if (golden.Contains(m.Spawner)) Destroy(m.gameObject);
            foreach (var s in golden) if (s != null) Destroy(s.gameObject);
            golden.Clear();
        }

        public static void Create()
        {
            if (Instance == null) new GameObject("Activities").AddComponent<Activities>();
        }

        void Awake() => Instance = this;

        void Update()
        {
            var p = Player.Instance;
            if (p == null || !p.HasClass) return;
            var d = p.Data;
            if (d.onlineDate != Today) { d.onlineDate = Today; d.onlineSeconds = 0; d.onlineClaimed = 0; }
            d.onlineSeconds += Time.unscaledDeltaTime;

            long now = Now;
            long cycle = now / InvasionCycle;
            long t = now % InvasionCycle;
            bool active = t >= InvasionStart && t < InvasionStart + InvasionLength;
            if (active && invasionCycle != cycle) { invasionCycle = cycle; StartInvasion(); }
            if (!active && golden.Count > 0) EndInvasion();

            rankPushTimer -= Time.unscaledDeltaTime;
            if (rankPushTimer <= 0)
            {
                rankPushTimer = 60f;
                Online.PushRank(MyEntry(p));
            }
        }

        // ---- Arena opponents: real players' snapshots near your CP, or practice bots offline ----

        public Online.RankEntry PickOpponent(Player p)
        {
            var me = MyEntry(p);
            var others = Ranking.Where(r => r.n != me.n && r.hp > 0).ToList();
            if (others.Count > 0)
                return others.OrderBy(r => Mathf.Abs(r.cp - me.cp) + Random.Range(0, me.cp / 5 + 1)).First();
            float k = Random.Range(0.8f, 1.1f);
            var cls = Classes.All[Random.Range(0, Classes.All.Length)];
            return new Online.RankEntry
            {
                n = "Shadow " + cls.Name, c = cls.Id, lv = me.lv, rb = me.rb, cp = Mathf.RoundToInt(me.cp * k),
                atk = Mathf.RoundToInt(me.atk * k), def = Mathf.RoundToInt(me.def * k), hp = Mathf.RoundToInt(me.hp * k), ap = me.ap,
            };
        }

        public const int ArenaRunsPerDay = 5;
        public const int ChaosRunsPerDay = 1;

        public static int ArenaLeft(PlayerSave d) => ArenaRunsPerDay - (d.arenaDate == Today ? d.arenaRuns : 0);
        public static int ChaosLeft(PlayerSave d) => ChaosRunsPerDay - (d.ccDate == Today ? d.ccRuns : 0);

        public static bool UseArena(PlayerSave d)
        {
            if (ArenaLeft(d) <= 0) return false;
            if (d.arenaDate != Today) { d.arenaDate = Today; d.arenaRuns = 0; }
            d.arenaRuns++;
            return true;
        }

        public static bool UseChaos(PlayerSave d)
        {
            if (ChaosLeft(d) <= 0) return false;
            if (d.ccDate != Today) { d.ccDate = Today; d.ccRuns = 0; }
            d.ccRuns++;
            return true;
        }
    }

    // Chaos Castle: a crowd of monsters on a floor that crumbles inward every 30 seconds.
    // Defeat them all before time runs out.
    public class ChaosCastle : DungeonRun
    {
        const float TimeLimit = 180f, ShrinkEvery = 30f;
        const int Monsters = 22;
        static readonly Vector3 Center = new Vector3(900f, 0, 0);

        float half = 16f, shrinkTimer = ShrinkEvery;
        int alive;
        Transform floor;
        MonsterSpawner spawner;

        Rect Area => new Rect(Center.x - half, Center.z - half, half * 2, half * 2);
        public override string Status => "Chaos Castle   " + Clock(TimeLeft) + "   Monsters left " + alive + "\nThe floor crumbles every 30s";

        public static string Enter()
        {
            var p = Player.Instance;
            if (Active != null || p == null || p.IsDead || !p.HasClass) return "Can't enter right now";
            if (!Activities.UseChaos(p.Data)) return "Chaos Castle is once per day";
            var run = new GameObject("Chaos Castle").AddComponent<ChaosCastle>();
            Active = run;
            run.Begin(p);
            return null;
        }

        void Begin(Player p)
        {
            level = Mathf.Max(1, p.Data.level);
            var lava = Block(transform, PrimitiveType.Plane, Center + Vector3.down * 0.3f, new Vector3(5f, 1, 5f), new Color(0.4f, 0.08f, 0.02f));
            props.Add(lava);
            floor = Block(transform, PrimitiveType.Cube, Center + Vector3.down * 0.05f, new Vector3(half * 2, 0.1f, half * 2), new Color(0.35f, 0.32f, 0.38f)).transform;
            spawner = MakeSpawner(level, Center, half - 3f, 0, Area);
            spawner.Died += _ => alive--;
            for (int i = 0; i < Monsters; i++) spawner.Spawn();
            alive = Monsters;
            p.Teleport(Center, Area);
            p.Refill();
            TimeLeft = TimeLimit;
            HUD.Message("Chaos Castle: defeat every monster!", 2.5f);
        }

        void Update()
        {
            if (finished) return;
            float dt = Time.deltaTime;
            TimeLeft -= dt;
            var p = Player.Instance;
            if (p == null || p.IsDead || TimeLeft <= 0) { Finish(false); return; }
            if (alive <= 0) { Finish(true); return; }
            shrinkTimer -= dt;
            if (shrinkTimer <= 0 && half > 7f)
            {
                shrinkTimer = ShrinkEvery;
                half -= 3f;
                floor.localScale = new Vector3(half * 2, 0.1f, half * 2);
                var area = Area;
                p.Bounds = area;
                p.transform.position = Clamp(p.transform.position, area);
                foreach (var m in Monster.All)
                    if (m.Spawner == spawner) { m.Bounds = area; m.transform.position = Clamp(m.transform.position, area); }
                Effect.Ring(Center, half, new Color(1f, 0.3f, 0.1f));
                HUD.Message("The floor crumbles!", 1.5f);
            }
        }

        static Vector3 Clamp(Vector3 v, Rect r) => new Vector3(Mathf.Clamp(v.x, r.xMin, r.xMax), v.y, Mathf.Clamp(v.z, r.yMin, r.yMax));

        protected override void Finish(bool won)
        {
            finished = true;
            var p = Player.Instance;
            if (won && p != null)
            {
                int gold = 3000 + level * 60;
                int flames = Random.value < 0.4f ? 1 : 0;
                p.Data.diamonds += 20;
                p.Data.ccClears++;
                p.AddMaterials(gold, GameData.MonsterExp(level) * 10, 4, 2, 0, flames);
                HUD.Message("Chaos Castle cleared!  +" + gold + "g  +4 Bless  +2 Soul  +20 Diamonds" + (flames > 0 ? "  +1 Flame" : ""), 5f);
            }
            else HUD.Message("Chaos Castle failed", 3f);
            Cleanup();
        }
    }

    // Arena: fight a copy of another player (their saved ATK / DEF / HP) for arena points and diamonds.
    public class ArenaFight : DungeonRun
    {
        const float TimeLimit = 90f;
        static readonly Vector3 Center = new Vector3(1200f, 0, 0);
        static readonly Rect ArenaBounds = new Rect(1188f, -12f, 24f, 24f);

        Online.RankEntry foe;
        Monster shadow;

        public override string Status => "Arena vs " + foe.n + " (CP " + foe.cp + ")   " + Clock(TimeLeft) +
                                         (shadow != null ? "   HP " + Mathf.CeilToInt(shadow.HpRatio * 100) + "%" : "");

        public static string Enter()
        {
            var p = Player.Instance;
            var a = Activities.Instance;
            if (Active != null || p == null || p.IsDead || !p.HasClass || a == null) return "Can't enter right now";
            if (!Activities.UseArena(p.Data)) return "No arena fights left today (" + Activities.ArenaRunsPerDay + " per day)";
            var run = new GameObject("Arena").AddComponent<ArenaFight>();
            Active = run;
            run.Begin(p, a.PickOpponent(p));
            return null;
        }

        void Begin(Player p, Online.RankEntry opponent)
        {
            foe = opponent;
            level = Mathf.Max(1, foe.lv);
            props.Add(Block(transform, PrimitiveType.Cylinder, Center + Vector3.down * 0.02f, new Vector3(24f, 0.02f, 24f), new Color(0.55f, 0.45f, 0.3f)));
            var s = MonsterSpawner.Create(foe.n, level, Center + new Vector3(0, 0, 5f), 0.3f, 0, Classes.Get(foe.c).BodyColor, 1.4f, false, "Player",
                Color.Lerp(Classes.Get(foe.c).BodyColor, Color.white, 0.4f));
            s.Bounds = ArenaBounds;
            spawners.Add(s);
            shadow = s.Spawn();
            shadow.SetStats(Mathf.Max(100, foe.hp * 3), Mathf.Max(10, Mathf.RoundToInt(foe.atk * 0.7f)), foe.def);
            shadow.NoRewards = true;
            s.Died += _ => { if (!finished) Finish(true); };
            p.Teleport(Center + new Vector3(0, 0, -6f), ArenaBounds);
            p.Refill();
            TimeLeft = TimeLimit;
            HUD.Message("Arena: defeat " + foe.n + "!", 2.5f);
        }

        void Update()
        {
            if (finished) return;
            TimeLeft -= Time.deltaTime;
            var p = Player.Instance;
            if (p == null || p.IsDead || TimeLeft <= 0) Finish(false);
        }

        protected override void Finish(bool won)
        {
            finished = true;
            var p = Player.Instance;
            if (p != null)
            {
                if (won)
                {
                    p.Data.arenaPoints += 15;
                    p.Data.arenaWins++;
                    p.Data.diamonds += 15;
                    p.AddMaterials(1000 + level * 30, 0, 0, 0, 0, 0);
                    HUD.Message("Arena victory!  +15 points  +15 Diamonds", 3.5f);
                }
                else
                {
                    p.Data.arenaPoints = Mathf.Max(0, p.Data.arenaPoints - 5);
                    p.Commit();
                    HUD.Message("Arena defeat  -5 points", 3f);
                }
            }
            Cleanup();
        }
    }
}
