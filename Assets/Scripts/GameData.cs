using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public enum Slot { Weapon, Armor, Helm, Boots }
    public enum Rarity { Common, Rare, Epic }

    [System.Serializable]
    public class Item
    {
        public string name;
        public Slot slot;
        public Rarity rarity;
        public int level;
        public int baseStat; // ATK for weapons, DEF for everything else
        public int plus;     // enhance level (+0 .. +15)

        public bool IsEmpty => string.IsNullOrEmpty(name);
        public int Stat => Mathf.RoundToInt(baseStat * (1f + 0.15f * plus));
        public string StatName => slot == Slot.Weapon ? "ATK" : "DEF";
        public string Label => (plus > 0 ? "+" + plus + " " : "") + name;
        public int SellPrice => baseStat * 2 + plus * 20;
        public int EnhanceCost => 50 * (plus + 1) * (plus + 1);
        public float EnhanceChance => Mathf.Max(0.2f, 1f - plus * 0.08f);
        public bool CanEnhance => !IsEmpty && plus < GameData.MaxPlus;
    }

    [System.Serializable]
    public class PlayerSave
    {
        public int level = 1;
        public int exp;
        public int gold;
        public List<Item> bag = new List<Item>();
        public List<Item> equipped = new List<Item>();
        public string classId = "";   // empty until the player picks a class
        public int feathers;          // wing upgrade material
        public int petFood;           // pet upgrade material
        public int wingsLevel;        // 0 = no wings
        public int petLevel;          // 0 = no pet
        public string dungeonDate = "";
        public int dungeonRuns;       // runs used on dungeonDate

        public void EnsureSlots()
        {
            while (equipped.Count < 4) equipped.Add(new Item());
        }
    }

    public enum SkillKind { Single, SelfArea, TargetArea, Multi }

    public class SkillDef
    {
        public string Name;
        public float Cooldown;
        public SkillKind Kind;
        public float Multiplier;
        public float Radius;   // area size, or max targets for Multi
        public float Range;    // how far the target may be
        public Color Color;
    }

    public class ClassDef
    {
        public string Id, Name, Description;
        public float AtkMul, DefMul, HpMul;
        public float AttackRange, AttackInterval;
        public bool Ranged;
        public Color BodyColor;
        public SkillDef[] Skills;
    }

    public static class Classes
    {
        public static readonly ClassDef[] All =
        {
            new ClassDef
            {
                Id = "Knight", Name = "Knight", Description = "Tough melee fighter\nHigh HP and DEF",
                AtkMul = 1f, DefMul = 1.3f, HpMul = 1.3f, AttackRange = 2.2f, AttackInterval = 0.6f,
                BodyColor = new Color(0.25f, 0.45f, 0.95f),
                Skills = new[]
                {
                    new SkillDef { Name = "Slash", Cooldown = 3, Kind = SkillKind.Single, Multiplier = 2.5f, Range = 3.2f, Color = new Color(1f, 0.6f, 0.2f) },
                    new SkillDef { Name = "Whirl", Cooldown = 6, Kind = SkillKind.SelfArea, Multiplier = 1.6f, Radius = 3.5f, Color = new Color(0.3f, 0.9f, 1f) },
                    new SkillDef { Name = "Fury", Cooldown = 12, Kind = SkillKind.SelfArea, Multiplier = 3f, Radius = 6f, Color = new Color(1f, 0.3f, 0.2f) },
                },
            },
            new ClassDef
            {
                Id = "Mage", Name = "Mage", Description = "Ranged magic\nHuge area damage, fragile",
                AtkMul = 1.3f, DefMul = 0.8f, HpMul = 0.85f, AttackRange = 9f, AttackInterval = 0.9f, Ranged = true,
                BodyColor = new Color(0.6f, 0.3f, 0.9f),
                Skills = new[]
                {
                    new SkillDef { Name = "Fireball", Cooldown = 3, Kind = SkillKind.Single, Multiplier = 2.5f, Range = 10f, Color = new Color(1f, 0.45f, 0.1f) },
                    new SkillDef { Name = "Frost", Cooldown = 6, Kind = SkillKind.SelfArea, Multiplier = 1.6f, Radius = 4f, Color = new Color(0.5f, 0.85f, 1f) },
                    new SkillDef { Name = "Meteor", Cooldown = 12, Kind = SkillKind.TargetArea, Multiplier = 3f, Radius = 5f, Range = 10f, Color = new Color(0.9f, 0.2f, 0.1f) },
                },
            },
            new ClassDef
            {
                Id = "Archer", Name = "Archer", Description = "Fast ranged attacks\nHits many targets",
                AtkMul = 1.1f, DefMul = 1f, HpMul = 1f, AttackRange = 8f, AttackInterval = 0.45f, Ranged = true,
                BodyColor = new Color(0.25f, 0.7f, 0.35f),
                Skills = new[]
                {
                    new SkillDef { Name = "Power Shot", Cooldown = 3, Kind = SkillKind.Single, Multiplier = 2.5f, Range = 9f, Color = new Color(1f, 0.9f, 0.3f) },
                    new SkillDef { Name = "Multi Shot", Cooldown = 6, Kind = SkillKind.Multi, Multiplier = 1.4f, Radius = 5, Range = 9f, Color = new Color(0.6f, 1f, 0.5f) },
                    new SkillDef { Name = "Arrow Rain", Cooldown = 12, Kind = SkillKind.TargetArea, Multiplier = 2.6f, Radius = 4f, Range = 9f, Color = new Color(0.9f, 0.9f, 0.5f) },
                },
            },
        };

        public static ClassDef Get(string id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return All[0];
        }
    }

    public static class Upgrades
    {
        public const int MaxWings = 50;
        public const int MaxPet = 50;
        public const int DungeonRunsPerDay = 3;
        public const int PetAdoptCost = 200;

        public static int WingFeathers(int lv) => 2 + lv;
        public static int WingGold(int lv) => 100 * (lv + 1);
        public static int PetFood(int lv) => 2 + lv;
        public static int PetGold(int lv) => 80 * (lv + 1);

        public static Color WingColor(int lv)
        {
            Color[] tiers = { Color.white, new Color(0.4f, 0.7f, 1f), new Color(0.75f, 0.45f, 1f), new Color(1f, 0.8f, 0.2f), new Color(1f, 0.3f, 0.25f) };
            return tiers[Mathf.Clamp((lv - 1) / 10, 0, tiers.Length - 1)];
        }
    }

    public static class GameData
    {
        public const int MaxPlus = 15;
        public const int BagSize = 30;

        static readonly string[] SlotNames = { "Sword", "Armor", "Helm", "Boots" };

        public static int ExpToNext(int level) => 20 + level * level * 10;

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return new Color(0.35f, 0.65f, 1f);
                case Rarity.Epic: return new Color(0.85f, 0.45f, 1f);
                default: return Color.white;
            }
        }

        public static Item MakeItem(Slot slot, Rarity rarity, int level)
        {
            float mult = rarity == Rarity.Epic ? 2.5f : rarity == Rarity.Rare ? 1.6f : 1f;
            float baseValue = slot == Slot.Weapon ? 5 + level * 3f : 2 + level * 1.5f;
            string prefix = rarity == Rarity.Epic ? "Legend " : rarity == Rarity.Rare ? "Fine " : "";
            return new Item
            {
                name = prefix + SlotNames[(int)slot] + " Lv" + level,
                slot = slot,
                rarity = rarity,
                level = level,
                baseStat = Mathf.RoundToInt(baseValue * mult),
            };
        }

        public static Item RollDrop(int monsterLevel)
        {
            var slot = (Slot)Random.Range(0, 4);
            float r = Random.value;
            var rarity = r < 0.05f ? Rarity.Epic : r < 0.30f ? Rarity.Rare : Rarity.Common;
            return MakeItem(slot, rarity, monsterLevel);
        }

        public static PlayerSave NewSave()
        {
            var save = new PlayerSave();
            save.EnsureSlots();
            save.equipped[(int)Slot.Weapon] = MakeItem(Slot.Weapon, Rarity.Common, 1);
            return save;
        }
    }

    public static class SaveSystem
    {
        const string Key = "muproto_save_v1";

        public static PlayerSave Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return GameData.NewSave();
            var save = JsonUtility.FromJson<PlayerSave>(json) ?? GameData.NewSave();
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
