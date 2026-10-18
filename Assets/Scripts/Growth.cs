using UnityEngine;

namespace MuProto
{
    // Character growth beyond levels and gear: talents (after rebirth), mounts, titles and the shops.
    public static class Growth
    {
        // ---- Talents ----

        public static readonly string[] TalentNames = { "Strength Mastery", "Guard Mastery", "Vitality Mastery", "Critical Mastery", "Skill Mastery", "Swiftness" };
        public static readonly string[] TalentEffects = { "ATK +2%", "DEF +2%", "HP +2%", "Critical +1%", "Skill damage +3%", "Attack speed +1%" };
        public const int MaxTalent = 10;
        public const int TalentResetZen = 5000;

        // 10 points per rebirth, plus 1 per 10 levels once reborn.
        public static int TalentPoints(PlayerSave d) => d.rebirth * 10 + (d.rebirth > 0 ? d.level / 10 : 0);

        public static int TalentSpent(PlayerSave d)
        {
            int n = 0;
            foreach (int t in d.talents) n += t;
            return n;
        }

        // ---- Mounts ----

        public class MountDef
        {
            public string Name;
            public int MinLevel, MinRebirth, Zen, Diamonds;
            public float Speed, AtkPct, DefPct;
            public Color Color;
        }

        public static readonly MountDef[] Mounts =
        {
            new MountDef { Name = "Uniria", MinLevel = 20, Zen = 20000, Speed = 1.5f, Color = new Color(0.95f, 0.95f, 0.9f) },
            new MountDef { Name = "Dinorant", MinLevel = 50, Diamonds = 100, Speed = 2.5f, AtkPct = 0.03f, Color = new Color(0.45f, 0.6f, 0.35f) },
            new MountDef { Name = "Dark Horse", MinRebirth = 1, Diamonds = 300, Speed = 3.5f, DefPct = 0.05f, Color = new Color(0.15f, 0.12f, 0.15f) },
            new MountDef { Name = "Fenrir", MinRebirth = 2, Diamonds = 800, Speed = 4.5f, AtkPct = 0.08f, Color = new Color(0.85f, 0.25f, 0.2f) },
        };

        // A simple four-legged mount out of boxes (to be replaced by a real model later).
        public static Transform MakeMount(Transform parent, int id)
        {
            var m = Mounts[Mathf.Clamp(id, 0, Mounts.Length - 1)];
            var root = new GameObject("Mount").transform;
            root.SetParent(parent, false);
            Box(root, new Vector3(0, 0.75f, 0), new Vector3(0.7f, 0.55f, 1.5f), m.Color);
            Box(root, new Vector3(0, 1.2f, 0.85f), new Vector3(0.4f, 0.45f, 0.55f), m.Color);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box(root, new Vector3(x * 0.25f, 0.25f, z * 0.55f), new Vector3(0.16f, 0.5f, 0.16f), Color.Lerp(m.Color, Color.black, 0.3f));
            if (id == 0) Box(root, new Vector3(0, 1.55f, 1.05f), new Vector3(0.06f, 0.35f, 0.06f), new Color(1f, 0.9f, 0.5f)); // unicorn horn
            if (id == 3) Aura.Attach(root, m.Color).SetLevel(9);
            return root;
        }

        static void Box(Transform parent, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Paint.Color(go, c);
        }

        // ---- Titles ----

        public class TitleDef
        {
            public string Name, Requirement;
            public System.Func<PlayerSave, bool> Unlocked;
            public float Atk, Def, Hp;
            public Color Color;
        }

        public static readonly TitleDef[] Titles =
        {
            new TitleDef { Name = "Novice Hunter", Requirement = "Defeat 100 monsters", Unlocked = d => d.kills >= 100, Atk = 0.02f, Color = new Color(0.8f, 0.9f, 0.8f) },
            new TitleDef { Name = "Monster Slayer", Requirement = "Defeat 2000 monsters", Unlocked = d => d.kills >= 2000, Atk = 0.05f, Color = new Color(0.5f, 1f, 0.5f) },
            new TitleDef { Name = "Castle Hero", Requirement = "Clear Blood Castle 5 times", Unlocked = d => d.bcClears >= 5, Def = 0.05f, Color = new Color(1f, 0.5f, 0.4f) },
            new TitleDef { Name = "Boss Breaker", Requirement = "Defeat a World Boss", Unlocked = d => d.bossKills >= 1, Hp = 0.05f, Color = new Color(0.85f, 0.55f, 1f) },
            new TitleDef { Name = "Reborn", Requirement = "Rebirth once", Unlocked = d => d.rebirth >= 1, Atk = 0.03f, Def = 0.03f, Hp = 0.03f, Color = new Color(1f, 0.85f, 0.3f) },
            new TitleDef { Name = "Outlaw", Requirement = "Win 5 PK fights", Unlocked = d => d.pkKills >= 5, Atk = 0.04f, Color = new Color(1f, 0.3f, 0.25f) },
            new TitleDef { Name = "Craftsman", Requirement = "Enhance an item to +9", Unlocked = d => MaxPlus(d) >= 9, Def = 0.04f, Color = new Color(0.4f, 0.8f, 1f) },
            new TitleDef { Name = "Arena Champion", Requirement = "Win 20 arena fights", Unlocked = d => d.arenaWins >= 20, Atk = 0.04f, Hp = 0.04f, Color = new Color(1f, 0.65f, 0.2f) },
        };

        public static int MaxPlus(PlayerSave d)
        {
            int best = 0;
            foreach (var it in d.equipped) if (!it.IsEmpty) best = Mathf.Max(best, it.plus);
            foreach (var it in d.bag) best = Mathf.Max(best, it.plus);
            return best;
        }

        // ---- Shops ----

        public class ShopItem
        {
            public string Name;
            public int Zen, Diamonds;
            public System.Action<Player> Give;
        }

        // The town Merchant (Zen) and the Diamond shop.
        public static readonly ShopItem[] Shop =
        {
            new ShopItem { Name = "10 HP Potions", Zen = 300, Give = p => p.Data.potions += 10 },
            new ShopItem { Name = "50 HP Potions", Zen = 1400, Give = p => p.Data.potions += 50 },
            new ShopItem { Name = "Jewel of Bless", Zen = 3000, Give = p => p.Data.jewelBless++ },
            new ShopItem { Name = "Condor Feather", Zen = 1500, Give = p => p.Data.feathers++ },
            new ShopItem { Name = "5 Pet Food", Zen = 800, Give = p => p.Data.petFood += 5 },
            new ShopItem { Name = "Jewel of Soul", Diamonds = 30, Give = p => p.Data.jewelSoul++ },
            new ShopItem { Name = "Condor Flame", Diamonds = 60, Give = p => p.Data.flames++ },
            new ShopItem { Name = "5 Jewels of Bless", Diamonds = 40, Give = p => p.Data.jewelBless += 5 },
            new ShopItem { Name = "Gem Box (Lv2 gem)", Diamonds = 50, Give = p =>
                {
                    var t = (GemType)Random.Range(1, 5);
                    p.Data.gemBag[GameData.GemIndex(t, 2)]++;
                    HUD.Message("Gem Box: " + t + " Lv2", 2.5f);
                } },
            new ShopItem { Name = "Ancient Box", Diamonds = 150, Give = p =>
                {
                    var it = GameData.MakeItem((Slot)Random.Range(0, 6), Rarity.Epic, Mathf.Max(1, p.Data.level), 1 + Mathf.Min(p.Data.rebirth, GameData.MaxTier - 1), p.Data.classId);
                    p.GiveItem(it, p.transform.position + Vector3.up * 3f);
                } },
        };
    }

    // The Chaos Goblin's machine: Jewel of Life options, Excellent rerolls, Condor Flames and Feathers.
    public static class ChaosMachine
    {
        public const int MaxLife = 7;

        public static string AddLife(Player p, int index)
        {
            var d = p.Data;
            var it = d.equipped[index];
            if (it.IsEmpty) return "Nothing equipped there";
            if (it.life >= MaxLife) return "Option already +" + MaxLife * 4;
            if (d.jewelLife < 1) return "Need a Jewel of Life";
            d.jewelLife--;
            if (Random.value < 0.5f)
            {
                it.life++;
                p.Commit();
                return "Success! Additional option +" + it.life * 4;
            }
            p.Commit();
            return "The Jewel of Life failed";
        }

        public static string Reroll(Player p, int index)
        {
            var d = p.Data;
            var it = d.equipped[index];
            if (it.IsEmpty || it.rarity == Rarity.Common) return "Only Excellent or Ancient items have options";
            if (d.jewelChaos < 2) return "Need 2 Jewels of Chaos";
            d.jewelChaos -= 2;
            int n = it.rarity == Rarity.Rare && Random.value < 0.35f ? 2 : 1;
            var opts = new System.Collections.Generic.List<int>();
            while (opts.Count < n)
            {
                int o = Random.Range(1, 10);
                if (!opts.Contains(o)) opts.Add(o);
            }
            it.opts = opts.ToArray();
            p.Commit();
            return "New options: " + HUDText(it);
        }

        public static string MakeFlame(Player p)
        {
            var d = p.Data;
            if (d.jewelChaos < 3 || d.jewelSoul < 1 || d.jewelBless < 1 || d.gold < 5000) return "Need 3 Chaos + 1 Soul + 1 Bless + 5000 Zen";
            d.jewelChaos -= 3; d.jewelSoul--; d.jewelBless--; d.gold -= 5000;
            bool ok = Random.value < 0.5f;
            if (ok) d.flames++;
            p.Commit();
            return ok ? "Success! +1 Condor Flame" : "The combination failed";
        }

        // Three Normal items from the bag + a Jewel of Chaos become 5 Condor Feathers.
        public static string MakeFeathers(Player p)
        {
            var d = p.Data;
            if (d.jewelChaos < 1) return "Need a Jewel of Chaos";
            var junk = new System.Collections.Generic.List<Item>();
            foreach (var it in d.bag) if (it.rarity == Rarity.Common && it.plus == 0 && junk.Count < 3) junk.Add(it);
            if (junk.Count < 3) return "Need 3 Normal items (+0) in the bag";
            foreach (var it in junk) d.bag.Remove(it);
            d.jewelChaos--;
            bool ok = Random.value < 0.75f;
            if (ok) d.feathers += 5;
            p.Commit();
            return ok ? "Success! +5 Condor Feathers" : "The combination failed (items lost)";
        }

        static string HUDText(Item it) => InventoryUI.Extras(it);

    }
}
