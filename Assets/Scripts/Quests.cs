using UnityEngine;

namespace MuProto
{
    public enum QuestKind { Kill, Level, Pet, Enhance, Wings, BloodCastle, DevilSquare, WorldBoss, Rebirth, Hunt }

    public class QuestDef
    {
        public QuestKind Kind;
        public string Target;   // monster name for Kill
        public int Count;       // kills, level, +N, rebirths...
        public int Gold, Bless, Soul, Feathers;
    }

    // The main quest line. Tapping the tracker runs it for you: walks to the right zone and fights,
    // or opens the window you need (pet, wings, bag, dungeons).
    public static class Quests
    {
        static QuestDef Q(QuestKind k, string target, int count, int gold, int bless = 0, int soul = 0, int feathers = 0) =>
            new QuestDef { Kind = k, Target = target, Count = count, Gold = gold, Bless = bless, Soul = soul, Feathers = feathers };

        static readonly QuestDef[] Chain =
        {
            Q(QuestKind.Kill, "แมงมุม", 10, 200, 1),          // Lorencia spiders
            Q(QuestKind.Level, null, 5, 300, 1),
            Q(QuestKind.Kill, "มังกรเด็ก", 10, 400, 1),
            Q(QuestKind.Pet, null, 1, 300),
            Q(QuestKind.Kill, "ปีศาจวัว", 15, 600, 2),
            Q(QuestKind.Enhance, null, 3, 800, 2),
            Q(QuestKind.Level, null, 15, 1000, 2, 1),
            Q(QuestKind.Kill, "ทหารกระดูก", 20, 1500, 2, 0, 5),      // Lorencia skeletons
            Q(QuestKind.DevilSquare, null, 1, 2000, 2, 1),
            Q(QuestKind.Wings, null, 1, 2000, 2, 1, 3),
            Q(QuestKind.Kill, "ก็อบลินเด็ก", 20, 3000, 3, 1, 3),   // Noria
            Q(QuestKind.BloodCastle, null, 1, 4000, 3, 1),
            Q(QuestKind.Level, null, 35, 5000, 3, 2),
            Q(QuestKind.Kill, "หนอนหิมะ", 25, 6000, 3, 2, 5),         // Devias
            Q(QuestKind.WorldBoss, null, 1, 6000, 3, 2),
            Q(QuestKind.Enhance, null, 7, 8000, 2, 2),
            Q(QuestKind.Level, null, 55, 10000, 4, 2),
            Q(QuestKind.Kill, "หนอนพิษ", 30, 12000, 4, 3, 5),         // Dungeon
            Q(QuestKind.Level, null, 80, 15000, 5, 3),
            Q(QuestKind.Kill, "พ่อมดสาปแช่ง", 30, 20000, 5, 3, 8),    // Lost Tower
            Q(QuestKind.Level, null, 100, 25000, 5, 4),
            Q(QuestKind.Rebirth, null, 1, 30000, 5, 5, 10),
        };

        // After the main line: endless hunts of 40 monsters near your level.
        static readonly QuestDef Hunt = Q(QuestKind.Hunt, null, 40, 0, 2, 1);

        public static QuestDef Current(PlayerSave d) => d.questIndex < Chain.Length ? Chain[d.questIndex] : Hunt;

        public static string Describe(Player p)
        {
            var d = p.Data;
            var q = Current(d);
            switch (q.Kind)
            {
                case QuestKind.Kill: return "Defeat " + q.Target + "  " + d.questProgress + "/" + q.Count;
                case QuestKind.Level: return "Reach Lv " + q.Count + "  (now " + d.level + ")";
                case QuestKind.Pet: return "Adopt a pet";
                case QuestKind.Enhance: return "Enhance any gear to +" + q.Count + "  (best +" + BestPlus(d) + ")";
                case QuestKind.Wings: return "Craft your first Wings";
                case QuestKind.BloodCastle: return "Clear Blood Castle";
                case QuestKind.DevilSquare: return "Survive Devil Square";
                case QuestKind.WorldBoss: return "Fight a World Boss";
                case QuestKind.Rebirth:
                    return d.level < GameData.MaxLevel ? "Rebirth: reach Lv " + GameData.MaxLevel
                         : d.rebirthKills < GameData.RebirthKillsNeeded ? "Rebirth: defeat Lv" + GameData.RebirthKillMinLevel + "+ monsters " + d.rebirthKills + "/" + GameData.RebirthKillsNeeded
                         : "Rebirth: talk to the Character window";
                default: return "Hunt #" + (d.huntsDone + 1) + ": defeat monsters near your level  " + d.questProgress + "/" + q.Count;
            }
        }

        static int BestPlus(PlayerSave d)
        {
            int best = 0;
            foreach (var it in d.equipped) if (!it.IsEmpty) best = Mathf.Max(best, it.plus);
            return best;
        }

        static bool IsDone(PlayerSave d, QuestDef q)
        {
            switch (q.Kind)
            {
                case QuestKind.Kill:
                case QuestKind.Hunt: return d.questProgress >= q.Count;
                case QuestKind.Level: return d.level >= q.Count || d.rebirth > 0;
                case QuestKind.Pet: return d.petLevel >= 1;
                case QuestKind.Enhance: return BestPlus(d) >= q.Count;
                case QuestKind.Wings: return d.wingTier >= 1;
                case QuestKind.Rebirth: return d.rebirth >= q.Count;
                default: return d.questProgress >= 1; // dungeons, world boss
            }
        }

        // Only this monster counts (and auto-hunt only fights it).
        public static string MonsterFilter(PlayerSave d)
        {
            var q = Current(d);
            return q.Kind == QuestKind.Kill ? q.Target : null;
        }

        public static void OnKill(Player p, Monster m)
        {
            var q = Current(p.Data);
            if ((q.Kind == QuestKind.Kill && m.DisplayName == q.Target) ||
                (q.Kind == QuestKind.Hunt && m.Level >= GameData.Power(p.Data) - 15))
                p.Data.questProgress++;
            Check(p);
        }

        public static void OnEvent(Player p, QuestKind kind)
        {
            if (p == null) return;
            if (Current(p.Data).Kind == kind) p.Data.questProgress++;
            Check(p);
        }

        // Completes the current quest (and any next ones already satisfied), paying the rewards.
        public static void Check(Player p)
        {
            var d = p.Data;
            for (int guard = 0; guard < 5; guard++)
            {
                var q = Current(d);
                if (!IsDone(d, q)) return;
                int exp = GameData.MonsterExp(Mathf.Max(1, d.level)) * 5;
                int gold = q.Kind == QuestKind.Hunt ? 500 + d.level * 60 : q.Gold;
                d.questProgress = 0;
                if (d.questIndex < Chain.Length) d.questIndex++;
                else d.huntsDone++;
                p.AddMaterials(gold, exp, q.Bless, q.Soul, q.Feathers, 0);
                HUD.Message("Quest complete!  +" + gold + "g  +" + exp + " EXP" + (q.Bless > 0 ? "  +" + q.Bless + " Bless" : "") +
                            (q.Soul > 0 ? "  +" + q.Soul + " Soul" : "") + (q.Feathers > 0 ? "  +" + q.Feathers + " Feathers" : ""), 3.5f);
                // Auto-quest keeps going into the next hunt; anything that needs a window stops here.
                if (p.QuestZone != null)
                {
                    var next = Current(d).Kind;
                    if (next == QuestKind.Kill || next == QuestKind.Level || next == QuestKind.Hunt || next == QuestKind.Rebirth) AutoRun(p);
                    else p.StartQuestHunt(null);
                }
            }
        }

        // One tap: do whatever the current quest needs. Returns a message for the screen, or null.
        public static string AutoRun(Player p)
        {
            var d = p.Data;
            var q = Current(d);
            switch (q.Kind)
            {
                case QuestKind.Kill:
                {
                    var zone = MonsterSpawner.Named(q.Target);
                    if (zone == null) return "Can't find " + q.Target;
                    return Go(p, zone);
                }
                case QuestKind.Level:
                case QuestKind.Hunt:
                    return Go(p, Maps.BestFor(d));
                case QuestKind.Rebirth:
                    if (d.level >= GameData.MaxLevel && d.rebirthKills >= GameData.RebirthKillsNeeded) { HUD.Instance.Open("Character"); return null; }
                    return Go(p, Maps.BestFor(d));
                case QuestKind.Pet: HUD.Instance.Open("Pet"); return null;
                case QuestKind.Enhance: HUD.Instance.Open("Bag"); return "Enhance an equipped item with Jewels";
                case QuestKind.Wings: HUD.Instance.Open("Wings"); return null;
                case QuestKind.BloodCastle:
                case QuestKind.DevilSquare: HUD.Instance.Open("Dungeon"); return null;
                case QuestKind.WorldBoss:
                    if (WorldBoss.Instance != null && WorldBoss.Instance.Boss != null) return Go(p, WorldBoss.Instance.Zone);
                    return WorldBoss.Instance != null ? WorldBoss.Instance.Status : null;
            }
            return null;
        }

        static string Go(Player p, MonsterSpawner zone)
        {
            if (DungeonRun.Active != null) return "Finish the dungeon first";
            if (zone.Map >= 0 && zone.Map != Maps.Current)
            {
                if (!Maps.CanEnter(p.Data, Maps.All[zone.Map], out string why)) return Maps.All[zone.Map].Name + ": " + why;
                Maps.Enter(p, zone.Map, true); // quest travel is free
            }
            p.StartQuestHunt(zone);
            return "Auto quest: heading to " + zone.MonsterName;
        }
    }
}
