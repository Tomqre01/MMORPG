using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // Shows the gear a hero is wearing on its model.
    // The hero FBX (Tools/Blender/build_heroes.py) carries every piece as its own skinned mesh:
    //   G_Body, G_Hair, G_<Slot><Stage> for Armor/Pants/Gloves/Boots/Helm (stage 0..2 = tiers 1-2, 3-4, 5-6)
    //   and G_Weapon1..6. This turns on the pieces that match what is equipped and paints each
    //   armour piece with its tier's colours (materials named *_arm, *_arm2, *_trim, *_glow).
    public class GearVisual : MonoBehaviour
    {
        // Gear slots with a look, in the order of the network code (Player.Data.equipped index = Slot).
        public static readonly Slot[] Shown = { Slot.Weapon, Slot.Armor, Slot.Helm, Slot.Boots, Slot.Gloves, Slot.Pants };
        static readonly Slot[] Armour = { Slot.Armor, Slot.Helm, Slot.Boots, Slot.Gloves, Slot.Pants };
        const int Stages = 3, Tiers = 6;

        // Per class, per tier: arm, arm2, trim, glow. Same values as TIER_COLORS in build_heroes.py.
        static readonly Dictionary<string, Color32[,]> Palettes = new Dictionary<string, Color32[,]>
        {
            ["DarkKnight"] = new Color32[,]
            {
                { C(150, 98, 56), C(70, 46, 30), C(196, 150, 96), C(255, 170, 90) },
                { C(186, 192, 204), C(60, 64, 74), C(110, 150, 200), C(150, 200, 255) },
                { C(70, 74, 88), C(22, 20, 26), C(212, 168, 64), C(255, 200, 90) },
                { C(34, 32, 40), C(12, 10, 14), C(170, 32, 40), C(255, 60, 50) },
                { C(214, 186, 110), C(90, 60, 30), C(245, 245, 250), C(255, 240, 170) },
                { C(110, 22, 30), C(30, 8, 10), C(255, 150, 40), C(255, 120, 30) },
            },
            ["DarkWizard"] = new Color32[,]
            {
                { C(96, 70, 46), C(60, 44, 30), C(190, 150, 96), C(255, 180, 90) },
                { C(170, 176, 190), C(70, 80, 100), C(120, 160, 210), C(150, 200, 255) },
                { C(40, 46, 110), C(26, 28, 70), C(212, 168, 64), C(90, 180, 255) },
                { C(30, 26, 40), C(70, 20, 80), C(170, 40, 200), C(200, 90, 255) },
                { C(230, 226, 210), C(180, 140, 60), C(240, 200, 90), C(255, 240, 170) },
                { C(120, 20, 34), C(40, 10, 16), C(255, 150, 40), C(255, 120, 30) },
            },
            ["Elf"] = new Color32[,]
            {
                { C(120, 92, 60), C(210, 196, 170), C(180, 140, 90), C(180, 255, 150) },
                { C(170, 180, 190), C(236, 236, 240), C(130, 170, 210), C(150, 220, 255) },
                { C(60, 132, 72), C(236, 236, 228), C(222, 180, 70), C(60, 220, 120) },
                { C(40, 60, 50), C(60, 70, 64), C(200, 200, 210), C(120, 255, 200) },
                { C(230, 210, 140), C(250, 248, 240), C(240, 200, 90), C(255, 240, 170) },
                { C(140, 30, 50), C(250, 220, 200), C(255, 160, 60), C(255, 120, 30) },
            },
        };
        static readonly string[] Keys = { "arm", "arm2", "trim", "glow" };

        // The colour of a tier for an item icon: the class's armour colour, or gold for jewellery.
        public static Color TierSwatch(string classId, int tier)
        {
            tier = Mathf.Clamp(tier, 1, Tiers);
            if (string.IsNullOrEmpty(classId) || !Palettes.TryGetValue(classId, out var pal))
                return Color.Lerp(new Color(0.85f, 0.65f, 0.3f), new Color(1f, 0.95f, 0.7f), (tier - 1) / 5f);
            return Color.Lerp(pal[tier - 1, 0], pal[tier - 1, 2], 0.35f);
        }

        static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        string classId;
        readonly Dictionary<string, Renderer> parts = new Dictionary<string, Renderer>();
        int[] shown;

        public static bool HasGear(GameObject model) => FindPart(model.transform, "G_Body") != null;

        static Transform FindPart(Transform t, string name)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                if (r.name == name) return r.transform;
            return null;
        }

        // Called by ModelLibrary.Spawn on hero models: starts in plain clothes with nothing equipped.
        public static GearVisual Attach(GameObject model, string classId)
        {
            if (!HasGear(model)) return null;
            var g = model.GetComponent<GearVisual>() ?? model.AddComponent<GearVisual>();
            g.classId = classId;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("G_")) g.parts[r.name] = r;
            g.Apply(new int[Shown.Length]);
            return g;
        }

        // looks[i] for the item in Shown[i]: tier (1..6) + 10 * rarity (0 Normal, 1 Excellent, 2 Ancient); 0 = nothing there.
        // Excellent pieces get glowing trim, Ancient ones a stronger glow.
        public void Apply(int[] tiers)
        {
            if (tiers == null || tiers.Length < Shown.Length) tiers = new int[Shown.Length];
            if (shown != null && System.Linq.Enumerable.SequenceEqual(shown, tiers)) return;
            shown = (int[])tiers.Clone();

            int weapon = tiers[0] % 10;
            for (int t = 1; t <= Tiers; t++) Show("G_Weapon" + t, t == weapon);

            var aura = GetComponentInParent<Aura>();
            for (int i = 1; i < Shown.Length; i++)
            {
                var slot = Shown[i];
                int tier = Mathf.Clamp(tiers[i] % 10, 0, Tiers), rarity = tiers[i] / 10;
                int stage = tier > 0 ? (tier - 1) / 2 : -1;
                for (int s = 0; s < Stages; s++)
                {
                    bool on = s == stage;
                    var r = Show("G_" + slot + s, on);
                    if (on && r != null) Paint(r, tier, rarity, aura);
                }
                if (slot == Slot.Helm) Show("G_Hair", tier == 0);
            }
        }

        Renderer Show(string name, bool on)
        {
            if (!parts.TryGetValue(name, out var r)) return null;
            r.enabled = on;
            return r;
        }

        // Only one piece showing (for the Backpack's item icons). Returns it, or null.
        public Renderer ShowOnly(Slot slot, int tier, int rarity)
        {
            shown = null;
            foreach (var p in parts.Values) p.enabled = false;
            tier = Mathf.Clamp(tier, 1, Tiers);
            string name = slot == Slot.Weapon ? "G_Weapon" + tier : "G_" + slot + (tier - 1) / 2;
            if (!parts.TryGetValue(name, out var r)) return null;
            r.enabled = true;
            if (slot != Slot.Weapon) Paint(r, tier, rarity, null);
            return r;
        }

        void Paint(Renderer r, int tier, int rarity, Aura aura)
        {
            if (!Palettes.TryGetValue(classId ?? "", out var pal)) return;
            foreach (var m in r.materials) // per-object copies, so every piece and every player keeps its own colours
            {
                if (m == null) continue;
                string n = m.name.Replace(" (Instance)", "");
                int k = System.Array.IndexOf(Keys, n.Substring(n.LastIndexOf('_') + 1));
                if (k < 0) continue;
                Color c = pal[tier - 1, k];
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                Color e = Color.black;
                if (Keys[k] == "glow") e = c * (1.2f + 0.3f * tier);
                else if (Keys[k] == "trim" && rarity > 0) e = (Color)pal[tier - 1, 3] * (rarity >= 2 ? 0.8f : 0.35f);
                else if (Keys[k] != "trim") continue;
                if (e.maxColorComponent > 0f) m.EnableKeyword("_EMISSION");
                else m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", e);
                if (aura != null) aura.SetBaseEmission(m, e);
            }
        }

        // ---- what to show for a player ----

        public static int[] TiersOf(PlayerSave d)
        {
            var t = new int[Shown.Length];
            for (int i = 0; i < Shown.Length; i++)
            {
                var it = d.equipped[(int)Shown[i]];
                if (it.IsEmpty || (Shown[i] == Slot.Helm && d.hideHelm)) continue;
                t[i] = Mathf.Clamp(it.tier, 1, Tiers) + 10 * (int)it.rarity;
            }
            return t;
        }

        // One character per Shown slot ('0' + look, so "3;1000"), sent to other players in PlayerState.eq.
        public static string Encode(int[] tiers)
        {
            var sb = new System.Text.StringBuilder(tiers.Length);
            foreach (int t in tiers) sb.Append((char)('0' + Mathf.Clamp(t, 0, 29)));
            return sb.ToString();
        }

        public static int[] Decode(string code)
        {
            var t = new int[Shown.Length];
            if (string.IsNullOrEmpty(code)) return t;
            for (int i = 0; i < t.Length && i < code.Length; i++)
            {
                int v = Mathf.Clamp(code[i] - '0', 0, 29);
                t[i] = Mathf.Min(v % 10, Tiers) + 10 * Mathf.Min(v / 10, 2);
            }
            return t;
        }
    }
}
