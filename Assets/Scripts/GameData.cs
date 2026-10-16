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

        public void EnsureSlots()
        {
            while (equipped.Count < 4) equipped.Add(new Item());
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

        public static void Save(PlayerSave save)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
        }

        public static void Delete() => PlayerPrefs.DeleteKey(Key);
    }
}
