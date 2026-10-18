using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // Equipped list index = (int)Slot, plus GameData.Ring2Index for the second ring.
    public enum Slot { Weapon, Armor, Helm, Boots, Gloves, Pants, Necklace, Ring }

    // Excellent options (green items roll 1-2 of these).
    public enum ExOpt { None, ExcDamage, AtkPct, DefPct, HpPct, CritRate, ZenDrop, LifeOnKill, DmgReduce, AtkSpeed }

    public enum GemType { None, Ruby, Sapphire, Emerald, Topaz } // ATK, DEF, HP, crit
    public enum Rarity { Common, Rare, Epic } // shown as Normal, Excellent, Ancient

    [System.Serializable]
    public class Item
    {
        public string name;
        public Slot slot;
        public Rarity rarity;
        public int level;
        public int tier = 1;     // 1..GameData.MaxTier; tier N needs (N-1) rebirths
        public string classId;   // who can wear it
        public int baseStat;     // ATK for weapons, DEF for everything else
        public int plus;         // enhance level (+0 .. +15)
        public int life;         // Jewel of Life additional option, +4 ATK/DEF per level (0..7)
        public int[] opts;       // ExOpt values (Excellent items)
        public string set;       // Ancient set name
        public int[] sockets;    // per socket: 0 empty, else gemType * 10 + gem level

        public bool IsEmpty => string.IsNullOrEmpty(name);
        public int Stat => Mathf.RoundToInt(baseStat * (1f + 0.1f * plus + 0.06f * Mathf.Max(0, plus - 6) * Mathf.Max(0, plus - 6) / 3f)) + life * 4;
        public string StatName => slot == Slot.Weapon || slot == Slot.Necklace ? "ATK" : slot == Slot.Ring ? "HP" : "DEF";
        public bool IsArmour => slot == Slot.Armor || slot == Slot.Helm || slot == Slot.Boots || slot == Slot.Gloves || slot == Slot.Pants;
        public bool HasOpt(ExOpt o) { if (opts != null) foreach (int x in opts) if (x == (int)o) return true; return false; }
        public int SocketCount => sockets == null ? 0 : sockets.Length;
        public string Label => (plus > 0 ? "+" + plus + " " : "") + name;
        public int SellPrice => baseStat * 2 + plus * 30 + (tier - 1) * 200 + (rarity == Rarity.Common ? 0 : 100);
        public bool CanEnhance => !IsEmpty && plus < GameData.MaxPlus;

        // The class's main stat (STR, AGI or ENE) needed to wear it.
        public int ReqStat => Mathf.RoundToInt((slot == Slot.Weapon ? 1f : 0.8f) * (15 + level * 1.2f + (tier - 1) * 60));
        public int ReqRebirth => tier - 1;

        // +1..+6 use a Jewel of Bless and always work. +7 and up use a Jewel of Soul and can fail.
        public bool UsesSoul => plus >= 6;
        public float EnhanceChance => plus < 6 ? 1f : Mathf.Max(0.15f, 0.6f - (plus - 6) * 0.05f);
        public int EnhanceGold => 20 * (plus + 1) * (plus + 1);
    }

    [System.Serializable]
    public class PlayerSave
    {
        public int version;           // GameData.SaveVersion; older saves start over
        public int level = 1;
        public int rebirth;           // 0R, 1R, 2R ...
        public int exp;
        public int gold;
        public string classId = "";   // empty until the player picks a class
        public int str, agi, vit, ene; // allocated, including the class's starting values
        public int freePoints;
        public int[] skillLevels = { 1, 1, 1 };
        public List<Item> bag = new List<Item>();
        public List<Item> equipped = new List<Item>();

        public int jewelBless, jewelSoul, jewelChaos, jewelLife;
        public int diamonds;          // premium currency: sign-in, achievements, arena, events
        public int[] gemBag = new int[20]; // count per (gemType-1) * 5 + (level-1)
        public int mapId;             // current map (Maps.All)
        public string charName = "";  // name shown to others (online)

        // growth
        public int[] talents = new int[6];
        public int mountsOwned;       // bit per Growth.Mounts index
        public int mountId = -1;      // mount being ridden, -1 = on foot
        public int titleId = -1;

        // activities
        public string signDate = "", onlineDate = "", ccDate = "", arenaDate = "";
        public int signDays, onlineClaimed, ccRuns, arenaRuns, arenaPoints;
        public float onlineSeconds;
        public int kills, bcClears, dsClears, ccClears, bossKills, pkKills, arenaWins;
        public int achievementsClaimed; // bit per Activities.Achievements index
        public int feathers;          // Condor Feather: wing levels
        public int flames;            // Condor Flame: wing tiers
        public int petFood;
        public int wingTier;          // 0 = no wings
        public int wingLevel;         // 0..Upgrades.WingLevelsPerTier inside the tier
        public int petLevel;          // 0 = no pet
        public int potions = 10;
        public bool autoPotion = true;

        public string bcDate = "", dsDate = "";
        public int bcRuns, dsRuns;    // Blood Castle / Devil Square runs used on that date

        public int questIndex, questProgress, huntsDone;
        public int rebirthKills;      // Lv50+ monsters killed at Lv100 (rebirth quest)
        public int pkPoints;          // above 0 = red name
        public float pkClock;         // seconds until one PK point wears off

        public void EnsureSlots()
        {
            while (equipped.Count < GameData.EquipSlots) equipped.Add(new Item());
            if (skillLevels == null || skillLevels.Length != 3) skillLevels = new[] { 1, 1, 1 };
            if (gemBag == null || gemBag.Length != 20) gemBag = new int[20];
            if (talents == null || talents.Length != 6) talents = new int[6];
        }
    }

    public enum SkillKind { Single, SelfArea, TargetArea, Multi, Dash, Buff }

    public class SkillDef
    {
        public string Name;
        public float Cooldown;
        public SkillKind Kind;
        public float Multiplier;
        public float Radius;   // area size, or max targets for Multi
        public float Range;    // how far the target may be
        public Color Color;
        public float Duration, BuffAtk, BuffDef, Heal; // Buff skills
    }

    public class ClassDef
    {
        public string Id, Name, Description;
        public int BaseStr, BaseAgi, BaseVit, BaseEne;
        public float StrAtk, AgiAtk, EneAtk;   // ATK per point
        public float BaseHp, HpPerVit, HpPerLevel, DefMul;
        public float AttackRange, AttackInterval, MoveBonus;
        public bool Ranged;
        public string MainStat;                 // STR, AGI or ENE: what gear asks for
        public string Weapon, Armor, Helm;      // item names
        public string[] Wings;                  // wing name per tier
        public Color BodyColor;
        public SkillDef[] Skills;
    }

    public static class Classes
    {
        public static readonly ClassDef[] All =
        {
            new ClassDef
            {
                Id = "DarkKnight", Name = "Dark Knight", Description = "Melee, lots of HP\nCharges in and spins\nits blade through crowds",
                BaseStr = 28, BaseAgi = 20, BaseVit = 25, BaseEne = 10,
                StrAtk = 1.5f, AgiAtk = 0.25f, EneAtk = 0f, BaseHp = 120, HpPerVit = 10, HpPerLevel = 8, DefMul = 1.2f,
                AttackRange = 2.2f, AttackInterval = 0.6f, MainStat = "STR",
                Weapon = "Sword", Armor = "Armor", Helm = "Helm",
                Wings = new[] { "Wings of Satan", "Wings of Dragon", "Wings of Doom", "Wings of Conqueror" },
                BodyColor = new Color(0.25f, 0.45f, 0.95f),
                Skills = new[]
                {
                    new SkillDef { Name = "Lunge", Cooldown = 3, Kind = SkillKind.Dash, Multiplier = 2.4f, Range = 8f, Radius = 1.6f, Color = new Color(1f, 0.6f, 0.2f) },
                    new SkillDef { Name = "Cyclone", Cooldown = 5, Kind = SkillKind.SelfArea, Multiplier = 1.8f, Radius = 3.5f, Color = new Color(0.3f, 0.9f, 1f) },
                    new SkillDef { Name = "Twisting\nSlash", Cooldown = 10, Kind = SkillKind.SelfArea, Multiplier = 3f, Radius = 6f, Color = new Color(1f, 0.3f, 0.2f) },
                },
            },
            new ClassDef
            {
                Id = "DarkWizard", Name = "Dark Wizard", Description = "Ranged magic, fragile\nClears whole packs\nwith Hellfire",
                BaseStr = 18, BaseAgi = 18, BaseVit = 15, BaseEne = 30,
                StrAtk = 0.2f, AgiAtk = 0.1f, EneAtk = 1.7f, BaseHp = 90, HpPerVit = 6, HpPerLevel = 5, DefMul = 0.8f,
                AttackRange = 9f, AttackInterval = 0.9f, Ranged = true, MainStat = "ENE",
                Weapon = "Staff", Armor = "Robe", Helm = "Hood",
                Wings = new[] { "Wings of Heaven", "Wings of Soul", "Wings of Despair", "Wings of Eternal" },
                BodyColor = new Color(0.6f, 0.3f, 0.9f),
                Skills = new[]
                {
                    new SkillDef { Name = "Energy\nBall", Cooldown = 2, Kind = SkillKind.Single, Multiplier = 2.2f, Range = 10f, Color = new Color(0.4f, 0.8f, 1f) },
                    new SkillDef { Name = "Evil\nSpirits", Cooldown = 6, Kind = SkillKind.Multi, Multiplier = 1.7f, Radius = 8, Range = 9f, Color = new Color(0.75f, 0.4f, 1f) },
                    new SkillDef { Name = "Hellfire", Cooldown = 11, Kind = SkillKind.SelfArea, Multiplier = 3.2f, Radius = 6.5f, Color = new Color(1f, 0.35f, 0.1f) },
                },
            },
            new ClassDef
            {
                Id = "Elf", Name = "Elf", Description = "Fast ranged bow\nMultiple Shot and a\nself buff, good at bosses",
                BaseStr = 22, BaseAgi = 25, BaseVit = 20, BaseEne = 15,
                StrAtk = 0.5f, AgiAtk = 1.1f, EneAtk = 0f, BaseHp = 100, HpPerVit = 8, HpPerLevel = 6, DefMul = 1f,
                AttackRange = 8f, AttackInterval = 0.45f, Ranged = true, MoveBonus = 0.5f, MainStat = "AGI",
                Weapon = "Bow", Armor = "Vest", Helm = "Circlet",
                Wings = new[] { "Wings of Fairy", "Wings of Spirit", "Wings of Illusion", "Wings of Mystery" },
                BodyColor = new Color(0.25f, 0.7f, 0.35f),
                Skills = new[]
                {
                    new SkillDef { Name = "Power\nShot", Cooldown = 2.5f, Kind = SkillKind.Single, Multiplier = 2.4f, Range = 9f, Color = new Color(1f, 0.9f, 0.3f) },
                    new SkillDef { Name = "Multiple\nShot", Cooldown = 5, Kind = SkillKind.Multi, Multiplier = 1.6f, Radius = 6, Range = 9f, Color = new Color(0.6f, 1f, 0.5f) },
                    new SkillDef { Name = "Greater\nDamage", Cooldown = 35, Kind = SkillKind.Buff, Duration = 20, BuffAtk = 0.3f, BuffDef = 0.2f, Heal = 0.2f, Color = new Color(1f, 0.85f, 0.4f) },
                },
            },
        };

        public static ClassDef Get(string id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return All[0];
        }

        public static int SkillUpgradeGold(int lv) => 200 * lv * lv;
        public const int MaxSkillLevel = 20;
    }

    public static class Upgrades
    {
        public const int MaxWingTier = 4;
        public const int WingLevelsPerTier = 10;
        public const int MaxPet = 50;
        public const int PetAdoptCost = 200;
        public const int BloodCastleRuns = 2;
        public const int DevilSquareRuns = 2;
        public const float PotionHeal = 0.35f;

        public static int PetFood(int lv) => 2 + lv;
        public static int PetGold(int lv) => 80 * (lv + 1);
        public static int PotionPrice(int level) => 10 + level * 2;

        // Wings: craft, then +1 level at a time with Condor Feathers; at the top of a tier,
        // Condor Flames move it to the next tier.
        public static int WingPower(int tier, int level) => tier <= 0 ? 0 : (tier - 1) * 15 + 10 + level;
        public static float WingSpeed(int tier, int level) => tier <= 0 ? 0 : tier * 0.35f + level * 0.03f;

        public enum WingStep { Craft, Level, Tier, Max }

        public static WingStep NextWingStep(PlayerSave d) =>
            d.wingTier == 0 ? WingStep.Craft :
            d.wingLevel < WingLevelsPerTier ? WingStep.Level :
            d.wingTier < MaxWingTier ? WingStep.Tier : WingStep.Max;

        // feathers, flames, gold for the next step
        public static void WingCost(PlayerSave d, out int feathers, out int flames, out int gold)
        {
            switch (NextWingStep(d))
            {
                case WingStep.Craft: feathers = 10; flames = 1; gold = 2000; break;
                case WingStep.Level: feathers = 1 + d.wingLevel / 2 + d.wingTier; flames = 0; gold = 300 * d.wingTier * (d.wingLevel + 1); break;
                case WingStep.Tier: feathers = 5 * d.wingTier; flames = 2 * d.wingTier; gold = 5000 * d.wingTier; break;
                default: feathers = flames = gold = 0; break;
            }
        }

        public static Color WingColor(int tier)
        {
            Color[] tiers = { Color.white, new Color(0.4f, 0.7f, 1f), new Color(0.75f, 0.45f, 1f), new Color(1f, 0.8f, 0.2f), new Color(1f, 0.3f, 0.25f) };
            return tiers[Mathf.Clamp(tier, 0, tiers.Length - 1)];
        }
    }

    public static class GameData
    {
        public const int SaveVersion = 2;
        public const int EquipSlots = 9;
        public const int Ring2Index = 8;
        public static Slot SlotAt(int index) => index == Ring2Index ? Slot.Ring : (Slot)index;
        public const int MaxPlus = 15;
        public const int BagSize = 30;
        public const int MaxLevel = 100;
        public const int MaxRebirth = 10;
        public const int MaxTier = 6;
        public const int PointsPerLevel = 5;
        public const int RebirthBonusPoints = 50;
        public const int RebirthKillsNeeded = 100;
        public const int RebirthKillMinLevel = 50;
        public const float TownRadius = 6f;

        static readonly string[] TierNames = { "Bronze", "Silver", "Dragon", "Black Dragon", "Great Dragon", "Dark Phoenix" };

        public static int RebirthGold(int rebirth) => 20000 * (rebirth + 1);
        public static int ExpToNext(int level, int rebirth) => Mathf.RoundToInt((20 + level * level * 8) * (1f + 0.25f * rebirth));

        // Monster numbers by level (field, dungeons and bosses all use these).
        public static int MonsterHp(int lv) => Mathf.RoundToInt(40 + lv * 30 + lv * lv * 0.8f);
        public static int MonsterAtk(int lv) => Mathf.RoundToInt(6 + lv * 4 + lv * lv * 0.02f);
        public static int MonsterDef(int lv) => lv * 2;
        public static int MonsterExp(int lv) => Mathf.RoundToInt(10 + lv * 6 + lv * lv * 0.6f);

        public static string RarityName(Rarity r) => r == Rarity.Epic ? "Ancient" : r == Rarity.Rare ? "Excellent" : "Normal";

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return new Color(0.35f, 1f, 0.45f);  // MU excellent green
                case Rarity.Epic: return new Color(1f, 0.75f, 0.25f);  // ancient gold
                default: return Color.white;
            }
        }

        public static string TierName(int tier) => TierNames[Mathf.Clamp(tier - 1, 0, TierNames.Length - 1)];

        public static Item MakeItem(Slot slot, Rarity rarity, int level, int tier, string classId)
        {
            var cls = Classes.Get(classId);
            float mult = (rarity == Rarity.Epic ? 2.5f : rarity == Rarity.Rare ? 1.6f : 1f) * (1f + 0.5f * (tier - 1));
            float baseValue;
            switch (slot)
            {
                case Slot.Weapon: baseValue = 8 + level * 3f; break;
                case Slot.Necklace: baseValue = 4 + level * 1.2f; break;
                case Slot.Ring: baseValue = 20 + level * 8f; break;
                default: baseValue = 2 + level * 1.5f; break;
            }
            string piece = slot == Slot.Weapon ? cls.Weapon : slot == Slot.Armor ? cls.Armor : slot == Slot.Helm ? cls.Helm : slot.ToString();
            bool jewelry = slot == Slot.Necklace || slot == Slot.Ring;
            string prefix = rarity == Rarity.Epic ? "Ancient " : rarity == Rarity.Rare ? "Excellent " : "";
            var item = new Item
            {
                name = prefix + (jewelry ? JewelryName(slot, tier) : TierName(tier) + " " + piece),
                slot = slot,
                rarity = rarity,
                level = level,
                tier = Mathf.Clamp(tier, 1, MaxTier),
                classId = jewelry ? "" : cls.Id, // jewellery fits every class
                baseStat = Mathf.RoundToInt(baseValue * mult),
            };
            if (rarity == Rarity.Rare)
            {
                int n = Random.value < 0.3f ? 2 : 1;
                var opts = new List<int>();
                while (opts.Count < n)
                {
                    int o = Random.Range(1, 10);
                    if (!opts.Contains(o)) opts.Add(o);
                }
                item.opts = opts.ToArray();
                item.sockets = new int[Random.value < 0.5f ? 1 : 0];
            }
            else if (rarity == Rarity.Epic)
            {
                item.set = jewelry ? null : SetName(cls.Id);
                item.opts = new[] { Random.Range(1, 10) };
                item.sockets = new int[2];
            }
            return item;
        }

        static string JewelryName(Slot slot, int tier)
        {
            string[] neck = { "Pendant of Lightning", "Pendant of Fire", "Pendant of Ice", "Pendant of Wind", "Pendant of Water", "Pendant of Ability" };
            string[] ring = { "Ring of Ice", "Ring of Poison", "Ring of Fire", "Ring of Earth", "Ring of Wind", "Ring of Magic" };
            int i = Mathf.Clamp(tier - 1, 0, 5);
            return slot == Slot.Necklace ? neck[i] : ring[i];
        }

        // One Ancient set per class: more pieces worn, bigger bonus.
        public static string SetName(string classId) => classId == "DarkWizard" ? "Apollo" : classId == "Elf" ? "Gywen" : "Hyon";

        public static string OptName(ExOpt o)
        {
            switch (o)
            {
                case ExOpt.ExcDamage: return "Excellent damage +10%";
                case ExOpt.AtkPct: return "ATK +5%";
                case ExOpt.DefPct: return "DEF +5%";
                case ExOpt.HpPct: return "HP +4%";
                case ExOpt.CritRate: return "Critical rate +5%";
                case ExOpt.ZenDrop: return "Zen drop +30%";
                case ExOpt.LifeOnKill: return "HP +1/8 on kill";
                case ExOpt.DmgReduce: return "Damage taken -4%";
                case ExOpt.AtkSpeed: return "Attack speed +7%";
                default: return "";
            }
        }

        public static string GemName(int code)
        {
            if (code <= 0) return "empty";
            return ((GemType)(code / 10)) + " Lv" + (code % 10);
        }

        public static int GemIndex(GemType t, int lv) => ((int)t - 1) * 5 + (lv - 1);

        // Mostly gear for the killer's own class, at the tier their rebirth allows.
        public static Item RollDrop(int monsterLevel, PlayerSave d, float epicBoost = 1f)
        {
            float s = Random.value;
            var slot = s < 0.12f ? Slot.Necklace : s < 0.24f ? Slot.Ring : (Slot)Random.Range(0, 6);
            float r = Random.value;
            var rarity = r < 0.04f * epicBoost ? Rarity.Epic : r < 0.25f ? Rarity.Rare : Rarity.Common;
            string cls = Random.value < 0.85f ? d.classId : Classes.All[Random.Range(0, Classes.All.Length)].Id;
            return MakeItem(slot, rarity, monsterLevel, 1 + Mathf.Min(d.rebirth, MaxTier - 1), cls);
        }

        public static PlayerSave NewSave()
        {
            var save = new PlayerSave { version = SaveVersion };
            save.EnsureSlots();
            return save;
        }

        // Called when the class is picked: starting stats and a starter weapon.
        public static void StartClass(PlayerSave s, ClassDef c)
        {
            s.classId = c.Id;
            s.str = c.BaseStr; s.agi = c.BaseAgi; s.vit = c.BaseVit; s.ene = c.BaseEne;
            s.freePoints = (s.level - 1) * PointsPerLevel + s.rebirth * RebirthBonusPoints;
            s.equipped[(int)Slot.Weapon] = MakeItem(Slot.Weapon, Rarity.Common, 1, 1, c.Id);
        }
    }

    public static class SaveSystem
    {
        const string Key = "muproto_save_v2";

        public static PlayerSave Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return GameData.NewSave();
            PlayerSave save = null;
            try { save = JsonUtility.FromJson<PlayerSave>(json); } catch { }
            // Saves from before the MU classic rework (no version) start over, as agreed.
            if (save == null || save.version != GameData.SaveVersion) return GameData.NewSave();
            save.EnsureSlots();
            return save;
        }

        // Raised after every save with the saved JSON; Online copies it to the server.
        public static event System.Action<string> Saved;

        public static void Save(PlayerSave save)
        {
            string json = JsonUtility.ToJson(save);
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
            Saved?.Invoke(json);
        }

        // Replaces the device save with one from the server (before the player is created).
        public static void SetJson(string json)
        {
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
        }

        public static void Delete() => PlayerPrefs.DeleteKey(Key);
    }
}
