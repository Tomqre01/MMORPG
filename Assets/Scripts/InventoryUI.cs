using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MuProto
{
    // The Backpack: equipment slots on the left, the hero in 3D in the middle wearing exactly what
    // is equipped (drag to turn), the bag on the right. Item squares show a picture of the piece
    // itself. Tap an item for its card (stats, requirements, compare) with Equip / Take off / Enhance;
    // tap it again to wear it. Changing gear flashes the hero and shows the Power change.
    public class InventoryUI
    {
        const float Tile = 112, Gap = 10, Cell = 104, CellGap = 8;
        const int Cols = 5;
        static readonly Vector2 TL = new Vector2(0, 1);

        // Left column, then right column.
        static readonly int[] SlotOrder =
        {
            (int)Slot.Weapon, (int)Slot.Helm, (int)Slot.Armor, (int)Slot.Pants, (int)Slot.Necklace,
            (int)Slot.Gloves, (int)Slot.Boots, (int)Slot.Ring, GameData.Ring2Index, -1, // -1: wings
        };

        readonly RectTransform panel, stage, equipRoot, bagRoot, card, banner;
        readonly GameObject root;
        readonly Text info, bagTitle, powerText, nameText, zen, diamonds, bless, soul, bannerText, helmText;
        readonly RawImage view;
        readonly HashSet<Item> seen = new HashSet<Item>();
        int pickedEquip = -1, pickedBag = -1;
        bool firstOpen = true;
        Coroutine bannerRun;
        // Selling: one item from its card (a second tap confirms good gear), or a whole grade at once.
        int confirmSell = -1;
        readonly bool[] sellGrades = { true, false, false };
        RectTransform sellBox;
        bool sellOpen;

        public InventoryUI(Transform canvas)
        {
            (root, panel) = Skin.Window(canvas, "Inventory", "Backpack", new Vector2(1560, 960), Close);

            zen = Skin.Pill(panel, UIB.Circle, new Color(1f, 0.8f, 0.25f), TL, new Vector2(48, -92), 210);
            diamonds = Skin.Pill(panel, Skin.Diamond, new Color(0.45f, 0.8f, 1f), TL, new Vector2(282, -92), 170);
            bless = Skin.Pill(panel, Skin.Diamond, new Color(0.7f, 0.55f, 1f), TL, new Vector2(1080, -92), 200);
            soul = Skin.Pill(panel, Skin.Diamond, new Color(1f, 0.55f, 0.75f), TL, new Vector2(1310, -92), 200);

            // equipment slots
            Skin.Sliced(UIB.Rect("EquipBox", panel, TL, new Vector2(22, -146), new Vector2(256, 694)), Skin.Inset, Color.white).raycastTarget = false;
            equipRoot = UIB.Rect("Equipped", panel, TL, new Vector2(34, -168), new Vector2(234, 640));

            // the hero
            stage = UIB.Rect("Stage", panel, TL, new Vector2(290, -146), new Vector2(640, 694));
            Skin.Sliced(stage, Skin.Inset, Color.white).raycastTarget = false;
            var glow = UIB.Rect("Glow", stage, new Vector2(0.5f, 0), new Vector2(0, 60), new Vector2(520, 150), new Vector2(0.5f, 0.5f));
            UIB.Image(glow, new Color(1f, 0.85f, 0.45f, 0.35f), Skin.Soft).raycastTarget = false;
            var viewRt = UIB.Stretch(UIB.Rect("View", stage, Vector2.zero, Vector2.zero, Vector2.zero));
            viewRt.offsetMin = new Vector2(6, 6);
            viewRt.offsetMax = new Vector2(-6, -6);
            view = viewRt.gameObject.AddComponent<RawImage>();
            view.gameObject.AddComponent<PreviewDrag>();
            var tapHero = view.gameObject.AddComponent<Button>();
            tapHero.transition = Selectable.Transition.None;
            tapHero.onClick.AddListener(() => { pickedBag = pickedEquip = -1; Refresh(); }); // tap the hero: close the card
            nameText = UIB.Label(stage, "", 26, TL, new Vector2(16, -12), new Vector2(608, 36), TextAnchor.MiddleCenter);
            nameText.color = Skin.Cream;
            nameText.raycastTarget = false;
            var pw = UIB.Rect("Power", stage, new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(360, 56), new Vector2(0.5f, 0));
            UIB.Image(pw, Color.white, Skin.Banner).raycastTarget = false;
            powerText = Skin.Title(pw, "", 30, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 56));
            powerText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            powerText.raycastTarget = false;
            var helm = UIB.Button(stage, "", new Vector2(1, 1), new Vector2(-12, -54), new Vector2(150, 46), new Color(0.15f, 0.14f, 0.16f),
                () => { var d = Player.Instance.Data; d.hideHelm = !d.hideHelm; Player.Instance.RefreshLook(); Player.Instance.Commit(); }, null, 18, new Vector2(1, 1));
            helmText = helm.GetComponentInChildren<Text>();

            // the Power change banner across the hero
            banner = UIB.Rect("PowerChange", stage, new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(620, 70), new Vector2(0.5f, 0.5f));
            UIB.Image(banner, new Color(0.15f, 0.35f, 0.85f, 0.85f), Skin.Banner).raycastTarget = false;
            bannerText = UIB.Label(banner, "", 34, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 70), TextAnchor.MiddleCenter);
            bannerText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            bannerText.fontStyle = FontStyle.BoldAndItalic;
            bannerText.raycastTarget = false;
            banner.gameObject.SetActive(false);

            // item card (over the left of the hero)
            card = UIB.Rect("ItemCard", stage, TL, new Vector2(10, -56), new Vector2(380, 590));
            Skin.Sliced(card, Skin.Panel, Color.white).raycastTarget = true; // blocks taps from reaching the hero behind
            card.gameObject.SetActive(false);

            // bag
            bagTitle = Skin.Title(panel, "", 26, TL, new Vector2(946, -92), new Vector2(130, 44), TextAnchor.MiddleLeft);
            Skin.Sliced(UIB.Rect("BagBox", panel, TL, new Vector2(942, -146), new Vector2(596, 694)), Skin.Inset, Color.white).raycastTarget = false;
            bagRoot = UIB.Rect("Bag", panel, TL, new Vector2(962, -160), new Vector2(Cols * (Cell + CellGap), 680));

            info = UIB.Label(panel, "", 21, TL, new Vector2(34, -846), new Vector2(1490, 30), TextAnchor.MiddleLeft);
            info.color = Skin.Gold;

            var bl = new Vector2(0, 0);
            UIB.Button(panel, "Sell", bl, new Vector2(30, 14), new Vector2(220, 60), new Color(0.6f, 0.45f, 0.15f),
                () => { sellOpen = !sellOpen; pickedBag = pickedEquip = -1; Refresh(); }, null, 26);
            sellBox = UIB.Rect("SellBox", panel, TL, new Vector2(310, -616), new Vector2(600, 210));
            Skin.Sliced(sellBox, Skin.Panel, Color.white).raycastTarget = true;
            sellBox.gameObject.SetActive(false);
            UIB.Button(panel, "Gems", bl, new Vector2(266, 14), new Vector2(200, 60), new Color(0.2f, 0.45f, 0.6f),
                () => HUD.Instance.Open("Gems"), null, 24);
            UIB.Button(panel, "Chaos Machine", bl, new Vector2(482, 14), new Vector2(230, 60), new Color(0.5f, 0.2f, 0.5f),
                () => HUD.Instance.Open("Chaos"), null, 22);
            UIB.Button(panel, "Wings", bl, new Vector2(728, 14), new Vector2(200, 60), new Color(0.25f, 0.4f, 0.3f),
                () => HUD.Instance.Open("Wings"), null, 24);
            UIB.Button(panel, "Reset Save", new Vector2(1, 0), new Vector2(-30, 14), new Vector2(220, 60), new Color(0.45f, 0.15f, 0.15f),
                () => { Player.Instance.ResetSave(); info.text = "Save reset"; }, null, 24, new Vector2(1, 0));

            Player.Instance.Changed += () => { if (root.activeSelf) Refresh(); };
            root.SetActive(false);
        }

        void Close()
        {
            MarkSeen();
            root.SetActive(false);
        }

        void MarkSeen()
        {
            foreach (var it in Player.Instance.Data.bag) seen.Add(it);
        }

        public void Show()
        {
            if (firstOpen) { MarkSeen(); firstOpen = false; } // what you start with is not news
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            pickedEquip = pickedBag = -1;
            banner.gameObject.SetActive(false);
            info.text = "Tap an item to see it, tap again to wear it. Drag the hero to turn.";
            var preview = GearPreview.Get();
            view.texture = preview.Texture;
            preview.Show(view);
            Refresh();
        }

        public void Toggle()
        {
            if (root.activeSelf) Close();
            else Show();
        }

        void Refresh()
        {
            var p = Player.Instance;
            var d = p.Data;
            zen.text = d.gold.ToString("N0");
            diamonds.text = d.diamonds.ToString("N0");
            bless.text = "Bless " + d.jewelBless;
            soul.text = "Soul " + d.jewelSoul;
            powerText.text = "Power " + p.CombatPower.ToString("N0");
            nameText.text = (string.IsNullOrEmpty(d.charName) ? p.Class.Name : d.charName) + "  <color=#c9b48a>" + GameData.LevelLabel(GameData.Power(d)) + "</color>";
            helmText.text = d.hideHelm ? "Show Helm" : "Hide Helm";
            bagTitle.text = "Bag  <size=22><color=#c9b48a>" + d.bag.Count + "/" + GameData.BagSize + "</color></size>";
            if (pickedBag >= d.bag.Count) pickedBag = -1;
            GearPreview.Get().Refresh();

            Clear(equipRoot);
            for (int i = 0; i < SlotOrder.Length; i++)
            {
                int index = SlotOrder[i];
                var pos = new Vector2((i / 5) * (Tile + Gap), -(i % 5) * (Tile + 16));
                if (index < 0) { WingsTile(pos); continue; }
                var it = d.equipped[index];
                string empty = index == GameData.Ring2Index ? "Ring" : GameData.SlotAt(index).ToString();
                var tile = MakeTile(equipRoot, pos, Tile, it, empty, pickedEquip == index, false, false, () => PickEquip(index));
                if (!it.IsEmpty && !p.CanEquip(it, out _)) Tint(tile, new Color(0.6f, 0.1f, 0.1f, 0.5f));
            }

            Clear(bagRoot);
            for (int i = 0; i < GameData.BagSize; i++)
            {
                int idx = i;
                var pos = new Vector2((i % Cols) * (Cell + CellGap), -(i / Cols) * (Cell + CellGap));
                if (i >= d.bag.Count)
                {
                    var slot = UIB.Rect("Empty", bagRoot, TL, pos, new Vector2(Cell, Cell));
                    UIB.Image(slot, new Color(0, 0, 0, 0.28f), Skin.Inset).type = Image.Type.Sliced;
                    continue;
                }
                var it = d.bag[i];
                bool usable = p.CanEquip(it, out _);
                var current = Current(it);
                bool better = usable && (current.IsEmpty || it.Stat > current.Stat);
                var tile = MakeTile(bagRoot, pos, Cell, it, "", pickedBag == i, !seen.Contains(it), better, () => PickBag(idx));
                if (!usable) Tint(tile, new Color(0.6f, 0.1f, 0.1f, 0.45f));
                if (sellOpen && sellGrades[(int)it.rarity]) SellMark(tile);
            }
            ShowSellBox();
            ShowCard();
        }

        Item Current(Item it)
        {
            var d = Player.Instance.Data;
            var current = d.equipped[(int)it.slot];
            if (it.slot == Slot.Ring && !d.equipped[GameData.Ring2Index].IsEmpty && d.equipped[GameData.Ring2Index].Stat < current.Stat)
                current = d.equipped[GameData.Ring2Index];
            return current;
        }

        void PickEquip(int index)
        {
            var p = Player.Instance;
            if (pickedEquip == index && !p.Data.equipped[index].IsEmpty) { TakeOff(index); return; }
            pickedEquip = index;
            pickedBag = -1;
            Refresh();
        }

        void PickBag(int index)
        {
            var p = Player.Instance;
            seen.Add(p.Data.bag[index]);
            if (pickedBag == index) { Wear(index); return; }
            pickedBag = index;
            confirmSell = -1;
            pickedEquip = -1;
            Refresh();
        }

        // ---- changing gear, with the Power change shown over the hero ----

        void Wear(int bagIndex)
        {
            var p = Player.Instance;
            var it = p.Data.bag[bagIndex];
            seen.Add(Current(it));
            int before = p.CombatPower;
            string msg = p.Equip(bagIndex);
            info.text = msg;
            pickedBag = -1;
            if (p.Data.bag.Contains(it)) { Refresh(); return; } // could not wear it
            Changed(before, it);
        }

        void TakeOff(int index)
        {
            var p = Player.Instance;
            seen.Add(p.Data.equipped[index]);
            int before = p.CombatPower;
            info.text = p.Unequip(index);
            pickedEquip = -1;
            if (p.Data.equipped[index].IsEmpty) Changed(before, null);
            else Refresh(); // bag full: nothing came off
        }

        void Enhance(int index)
        {
            var p = Player.Instance;
            int before = p.CombatPower;
            info.text = p.Enhance(index);
            if (p.CombatPower != before) Changed(before, p.Data.equipped[index]);
        }

        void Changed(int before, Item worn)
        {
            Refresh();
            int after = Player.Instance.CombatPower;
            GearPreview.Get().Flash(worn != null ? GearVisual.TierSwatch(worn.classId, worn.tier) : Skin.Muted);
            if (after == before) return;
            if (bannerRun != null) HUD.Instance.StopCoroutine(bannerRun);
            bannerRun = HUD.Instance.StartCoroutine(Banner(after, after - before));
        }

        IEnumerator Banner(int power, int diff)
        {
            bool up = diff > 0;
            bannerText.text = "Power " + power.ToString("N0") + "   <color=" + (up ? "#9CFF7A" : "#FF7A6B") + ">" + (up ? "▲ " : "▼ ") + Mathf.Abs(diff).ToString("N0") + "</color>";
            var img = banner.GetComponent<Image>();
            var baseCol = up ? new Color(0.35f, 0.6f, 1f, 0.9f) : new Color(1f, 0.4f, 0.35f, 0.9f);
            banner.gameObject.SetActive(true);
            for (float t = 0; t < 2.8f; t += Time.unscaledDeltaTime)
            {
                float slide = Mathf.Clamp01(t / 0.18f);
                float fade = t < 2.2f ? 1f : 1f - (t - 2.2f) / 0.6f;
                banner.anchoredPosition = new Vector2(Mathf.Lerp(-420f, 0f, 1f - (1f - slide) * (1f - slide)), 150f);
                banner.localScale = Vector3.one * (t < 0.3f ? Mathf.Lerp(1.25f, 1f, t / 0.3f) : 1f);
                img.color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * fade);
                var c = bannerText.color;
                bannerText.color = new Color(c.r, c.g, c.b, fade);
                yield return null;
            }
            banner.gameObject.SetActive(false);
            bannerRun = null;
        }

        // ---- the item card ----

        void ShowCard()
        {
            Clear(card);
            var p = Player.Instance;
            Item it = null;
            int bagIdx = -1, equipIdx = -1;
            if (pickedBag >= 0) { bagIdx = pickedBag; it = p.Data.bag[bagIdx]; }
            else if (pickedEquip >= 0 && !p.Data.equipped[pickedEquip].IsEmpty) { equipIdx = pickedEquip; it = p.Data.equipped[equipIdx]; }
            card.gameObject.SetActive(it != null);
            if (it == null) return;

            var rc = GameData.RarityColor(it.rarity);
            var iconBox = UIB.Rect("Icon", card, TL, new Vector2(22, -22), new Vector2(104, 104));
            UIB.Image(iconBox, Color.Lerp(rc, Color.black, 0.7f), Skin.Inset).type = Image.Type.Sliced;
            var icon = UIB.Stretch(UIB.Rect("Pic", iconBox, Vector2.zero, Vector2.zero, Vector2.zero)).gameObject.AddComponent<RawImage>();
            icon.texture = GearIcons.Get(it);
            icon.raycastTarget = false;
            var title = UIB.Label(card, "<b>" + it.Label + "</b>", 25, TL, new Vector2(140, -22), new Vector2(220, 60), TextAnchor.UpperLeft);
            title.color = rc;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            var pw = UIB.Label(card, ItemPower(it).ToString("N0") + " <size=18>Power</size>", 30, TL, new Vector2(140, -84), new Vector2(220, 40), TextAnchor.MiddleLeft);
            pw.color = Skin.Gold;
            pw.fontStyle = FontStyle.Bold;
            UIB.Label(card, "T" + it.tier + " " + GameData.TierName(it.tier) + " · " + GameData.RarityName(it.rarity) + " · " + it.slot, 18,
                TL, new Vector2(22, -134), new Vector2(340, 26), TextAnchor.MiddleLeft).color = Skin.Muted;

            var lines = new List<string>();
            bool statOk = p.MainStatValue >= it.ReqStat, rbOk = p.Data.rebirth >= it.ReqRebirth;
            bool classOk = string.IsNullOrEmpty(it.classId) || it.classId == p.Data.classId;
            lines.Add("<color=#c9b48a>Requirements</color>");
            if (!string.IsNullOrEmpty(it.classId)) lines.Add(Ok(classOk) + Classes.Get(it.classId).Name + " only</color>");
            lines.Add(Ok(statOk) + p.Class.MainStat + " " + it.ReqStat + "</color>" + (it.ReqRebirth > 0 ? "   " + Ok(rbOk) + it.ReqRebirth + " Rebirth</color>" : ""));
            lines.Add("<color=#c9b48a>Base</color>");
            lines.Add(it.StatName + " " + it.Stat + (it.plus > 0 ? "  <color=#ffd860>(+" + it.plus + ")</color>" : "") + (it.life > 0 ? "   Option +" + it.life * 4 : ""));
            string extras = Extras(it);
            if (extras.Length > 0)
            {
                lines.Add("<color=#7CFF6B>Excellent / extras</color>");
                foreach (var e in extras.Split('|')) lines.Add("<color=#9be88f>" + e.Trim() + "</color>");
            }
            if (bagIdx >= 0)
            {
                var cur = Current(it);
                int diff = it.Stat - (cur.IsEmpty ? 0 : cur.Stat);
                int pdiff = ItemPower(it) - (cur.IsEmpty ? 0 : ItemPower(cur));
                lines.Add("");
                lines.Add(cur.IsEmpty ? "<color=#9CFF7A>Nothing worn there</color>"
                    : "vs worn: <color=" + (diff >= 0 ? "#9CFF7A>+" : "#FF7A6B>") + diff + " " + it.StatName + "</color>   <color=" + (pdiff >= 0 ? "#9CFF7A>▲" : "#FF7A6B>▼") + Mathf.Abs(pdiff) + " Power</color>");
            }
            var body = UIB.Label(card, string.Join("\n", lines), 20, TL, new Vector2(22, -166), new Vector2(340, 330), TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.lineSpacing = 1.05f;

            var bc = new Vector2(0.5f, 0);
            if (bagIdx >= 0)
            {
                bool usable = p.CanEquip(it, out _);
                var b = UIB.Button(card, "Equip", bc, new Vector2(-84, 22), new Vector2(170, 66), usable ? new Color(0.62f, 0.14f, 0.18f) : new Color(0.25f, 0.25f, 0.27f),
                    () => Wear(bagIdx), null, 28, new Vector2(0.5f, 0));
                b.interactable = usable;
                bool precious = it.rarity != Rarity.Common || it.plus >= 7;
                bool asking = confirmSell == bagIdx;
                UIB.Button(card, asking ? "Sure? Sell" : "Sell\n" + it.SellPrice.ToString("N0") + "g", bc, new Vector2(94, 22), new Vector2(170, 66),
                    asking ? new Color(0.75f, 0.2f, 0.12f) : new Color(0.55f, 0.42f, 0.14f),
                    () =>
                    {
                        if (precious && confirmSell != bagIdx) { confirmSell = bagIdx; Refresh(); return; }
                        confirmSell = -1;
                        pickedBag = -1;
                        info.text = Player.Instance.SellItem(bagIdx);
                    }, null, asking ? 24 : 21, new Vector2(0.5f, 0));
            }
            else
            {
                UIB.Button(card, "Take off", bc, new Vector2(it.CanEnhance ? -92 : 0, 22), new Vector2(160, 66), new Color(0.4f, 0.32f, 0.2f),
                    () => TakeOff(equipIdx), null, 24, new Vector2(0.5f, 0));
                if (it.CanEnhance)
                {
                    string jewel = it.UsesSoul ? "Soul " + Mathf.RoundToInt(it.EnhanceChance * 100) + "%" : "Bless";
                    UIB.Button(card, "+" + (it.plus + 1) + " " + jewel + "\n" + it.EnhanceGold.ToString("N0") + "g", bc, new Vector2(88, 22), new Vector2(170, 66),
                        it.UsesSoul ? new Color(0.55f, 0.2f, 0.4f) : new Color(0.35f, 0.25f, 0.55f), () => Enhance(equipIdx), null, 19, new Vector2(0.5f, 0));
                }
            }
            var x = UIB.Button(card, "X", new Vector2(1, 1), new Vector2(-8, -8), new Vector2(44, 44), new Color(0.3f, 0.12f, 0.1f),
                () => { pickedBag = pickedEquip = -1; Refresh(); }, UIB.Circle, 22, new Vector2(1, 1));
        }

        // ---- selling a whole grade ----

        static readonly string[] GradeNames = { "Normal", "Excellent", "Ancient" };

        void ShowSellBox()
        {
            sellBox.gameObject.SetActive(sellOpen);
            if (!sellOpen) return;
            Clear(sellBox);
            var p = Player.Instance;
            int n = 0, gold = 0;
            foreach (var it in p.Data.bag)
                if (sellGrades[(int)it.rarity]) { n++; gold += it.SellPrice; }
            UIB.Label(sellBox, "Sell every item of the ticked grades (any tier, any +level)", 19, TL, new Vector2(20, -14), new Vector2(560, 28), TextAnchor.MiddleLeft).color = Skin.Cream;
            for (int g = 0; g < 3; g++)
            {
                int grade = g;
                var rc = GameData.RarityColor((Rarity)g);
                var t = UIB.Button(sellBox, (sellGrades[g] ? "● " : "○ ") + GradeNames[g], TL, new Vector2(20 + g * 188, -52), new Vector2(176, 56),
                    sellGrades[g] ? Color.Lerp(rc, Color.black, 0.45f) : new Color(0.14f, 0.13f, 0.15f), () => { sellGrades[grade] = !sellGrades[grade]; Refresh(); }, null, 22);
                t.GetComponentInChildren<Text>().color = rc;
            }
            UIB.Label(sellBox, n + " items  ·  +" + gold.ToString("N0") + "g", 24, TL, new Vector2(20, -122), new Vector2(330, 40), TextAnchor.MiddleLeft).color = Skin.Gold;
            UIB.Button(sellBox, "Sell " + n, TL, new Vector2(350, -116), new Vector2(150, 56), n > 0 ? new Color(0.62f, 0.2f, 0.12f) : new Color(0.25f, 0.25f, 0.27f),
                () =>
                {
                    int sold = Player.Instance.SellBag(sellGrades, out int g2);
                    sellOpen = false;
                    info.text = "Sold " + sold + " items for " + g2.ToString("N0") + "g";
                }, null, 24).interactable = n > 0;
            UIB.Button(sellBox, "X", TL, new Vector2(516, -120), new Vector2(50, 50), new Color(0.3f, 0.12f, 0.1f),
                () => { sellOpen = false; Refresh(); }, UIB.Circle, 22);
        }

        static void SellMark(Button b)
        {
            var o = UIB.Stretch(UIB.Rect("SellMark", b.transform, Vector2.zero, Vector2.zero, Vector2.zero));
            UIB.Image(o, new Color(0.9f, 0.6f, 0.1f, 0.28f)).raycastTarget = false;
            var t = UIB.Label(b.transform, "SELL", 15, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 22), TextAnchor.MiddleCenter);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.color = new Color(1f, 0.85f, 0.4f);
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
        }

        static string Ok(bool ok) => ok ? "<color=#e8e2d0>" : "<color=#FF6B5A>";

        // Roughly what the item adds to Combat Power (same weights as Player.CombatPower).
        static int ItemPower(Item it)
        {
            int stat = it.StatName == "ATK" ? it.Stat * 4 : it.StatName == "DEF" ? it.Stat * 3 : it.Stat / 2;
            return stat + it.plus * it.plus * 8 + (it.rarity == Rarity.Common ? 0 : 40);
        }

        void WingsTile(Vector2 pos)
        {
            var d = Player.Instance.Data;
            var b = UIB.Button(equipRoot, d.wingTier > 0 ? "Wings\nT" + d.wingTier + " +" + d.wingLevel : "Wings", TL, pos, new Vector2(Tile, Tile),
                d.wingTier > 0 ? Color.Lerp(Upgrades.WingColor(d.wingTier), Color.black, 0.55f) : new Color(0.1f, 0.1f, 0.12f), () => HUD.Instance.Open("Wings"), null, 19);
            b.GetComponentInChildren<Text>().color = d.wingTier > 0 ? Skin.Cream : Skin.Muted;
        }

        // An item square: rarity background, a picture of the piece, tier, +level, and NEW / ▲ marks.
        static Button MakeTile(Transform parent, Vector2 pos, float size, Item it, string empty, bool picked, bool isNew, bool better, UnityEngine.Events.UnityAction onClick)
        {
            if (it.IsEmpty)
            {
                var e = UIB.Button(parent, empty, TL, pos, new Vector2(size, size), new Color(0.1f, 0.1f, 0.12f), onClick, null, 18);
                e.GetComponentInChildren<Text>().color = Skin.Muted;
                if (picked) Outline(e.transform);
                return e;
            }
            var rc = GameData.RarityColor(it.rarity);
            var b = UIB.Button(parent, "", TL, pos, new Vector2(size, size), Color.Lerp(rc, Color.black, 0.62f), onClick, null, 16);
            var edge = b.transform.Find("Edge");
            if (edge != null) edge.GetComponent<Image>().color = Color.Lerp(rc, Color.white, 0.15f);

            var pic = UIB.Stretch(UIB.Rect("Pic", b.transform, Vector2.zero, Vector2.zero, Vector2.zero));
            pic.offsetMin = new Vector2(6, 6);
            pic.offsetMax = new Vector2(-6, -6);
            var raw = pic.gameObject.AddComponent<RawImage>();
            raw.texture = GearIcons.Get(it);
            raw.raycastTarget = false;
            if (raw.texture == null)
            {
                Object.Destroy(raw);
                UIB.Image(UIB.Rect("Tier", b.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.46f, size * 0.46f), new Vector2(0.5f, 0.5f)),
                    GearVisual.TierSwatch(it.classId, it.tier), Skin.Diamond).raycastTarget = false;
            }
            var tier = UIB.Label(b.transform, "T" + it.tier, 16, new Vector2(0, 0), new Vector2(6, 4), new Vector2(40, 20), TextAnchor.LowerLeft);
            tier.rectTransform.pivot = Vector2.zero;
            tier.color = Skin.Cream;
            tier.raycastTarget = false;
            if (it.plus > 0)
            {
                var plus = UIB.Label(b.transform, "+" + it.plus, 18, new Vector2(1, 1), new Vector2(-6, -4), new Vector2(50, 22), TextAnchor.UpperRight);
                plus.rectTransform.pivot = new Vector2(1, 1);
                plus.color = it.plus >= 7 ? new Color(1f, 0.85f, 0.35f) : Skin.Cream;
                plus.fontStyle = FontStyle.Bold;
                plus.raycastTarget = false;
            }
            if (better)
            {
                var up = UIB.Label(b.transform, "▲", 22, new Vector2(1, 0), new Vector2(-6, 4), new Vector2(30, 24), TextAnchor.LowerRight);
                up.rectTransform.pivot = new Vector2(1, 0);
                up.color = new Color(0.45f, 1f, 0.4f);
                up.raycastTarget = false;
            }
            if (isNew)
            {
                var tag = UIB.Rect("New", b.transform, TL, new Vector2(-6, 8), new Vector2(54, 24));
                UIB.Image(tag, new Color(0.85f, 0.12f, 0.1f), Skin.Inset).type = Image.Type.Sliced;
                var t = UIB.Label(tag, "NEW", 15, Vector2.zero, Vector2.zero, new Vector2(54, 24), TextAnchor.MiddleCenter);
                t.rectTransform.pivot = Vector2.zero;
                t.fontStyle = FontStyle.Bold;
                t.raycastTarget = false;
                tag.GetComponent<Image>().raycastTarget = false;
            }
            if (picked) Outline(b.transform);
            return b;
        }

        static void Outline(Transform t)
        {
            var o = UIB.Stretch(UIB.Rect("Picked", t, Vector2.zero, Vector2.zero, Vector2.zero));
            o.offsetMin = new Vector2(-5, -5);
            o.offsetMax = new Vector2(5, 5);
            var img = UIB.Image(o, new Color(1f, 0.85f, 0.35f), Skin.ButtonEdge);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
        }

        static void Tint(Button b, Color c)
        {
            var o = UIB.Stretch(UIB.Rect("Locked", b.transform, Vector2.zero, Vector2.zero, Vector2.zero));
            UIB.Image(o, c).raycastTarget = false;
        }

        // Excellent options, set name and sockets in one short line.
        public static string Extras(Item it)
        {
            var parts = new List<string>();
            if (it.opts != null) foreach (int o in it.opts) parts.Add(GameData.OptName((ExOpt)o));
            if (!string.IsNullOrEmpty(it.set)) parts.Add("Set " + it.set);
            if (it.SocketCount > 0)
            {
                var g = new List<string>();
                foreach (int c in it.sockets) g.Add(GameData.GemName(c));
                parts.Add("Sockets: " + string.Join(", ", g));
            }
            return string.Join(" | ", parts);
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    // Drag on the hero picture to turn the hero.
    public class PreviewDrag : MonoBehaviour, IDragHandler
    {
        public void OnDrag(PointerEventData e) => GearPreview.Get().Turn(-e.delta.x * 0.4f);
    }

    // A copy of the player's hero standing far above the world, filmed by its own camera into a
    // texture for the Backpack. It wears what the player wears and is rebuilt when that changes.
    public class GearPreview : MonoBehaviour
    {
        const int Layer = 30; // only the preview camera sees this layer
        static GearPreview instance;

        public RenderTexture Texture { get; private set; }
        Camera cam;
        Transform turntable, body, wings;
        GearVisual gear;
        Aura aura;
        RawImage target;
        string cls;
        int wingCode = -1;
        float yaw = 20f, pop;

        public static GearPreview Get()
        {
            if (instance == null) instance = Create();
            return instance;
        }

        static GearPreview Create()
        {
            var go = new GameObject("Gear Preview Stage");
            go.transform.position = new Vector3(0, 400f, 0);
            var gp = go.AddComponent<GearPreview>();
            gp.Texture = new RenderTexture(640, 694, 24, RenderTextureFormat.ARGB32) { name = "Gear Preview", antiAliasing = 4 };
            gp.turntable = new GameObject("Turntable").transform;
            gp.turntable.SetParent(go.transform, false);

            var camGo = new GameObject("Preview Camera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.25f, 5.6f);
            camGo.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 1.02f, 0) - camGo.transform.localPosition);
            gp.cam = camGo.AddComponent<Camera>();
            gp.cam.targetTexture = gp.Texture;
            gp.cam.cullingMask = 1 << Layer;
            gp.cam.clearFlags = CameraClearFlags.SolidColor;
            gp.cam.backgroundColor = new Color(0.07f, 0.06f, 0.05f, 0f);
            gp.cam.fieldOfView = 26f;
            gp.cam.nearClipPlane = 0.5f;
            gp.cam.farClipPlane = 20f;
            gp.cam.enabled = false;

            // a warm key light from the front
            var key = new GameObject("Key Light").AddComponent<Light>();
            key.transform.SetParent(go.transform, false);
            key.transform.localPosition = new Vector3(1.5f, 2.6f, 3f);
            key.type = LightType.Point;
            key.range = 9f;
            key.intensity = 2.2f;
            key.color = new Color(1f, 0.93f, 0.82f);
            key.cullingMask = 1 << Layer;
            key.shadows = LightShadows.None;

            foreach (var c in Camera.allCameras) if (c != gp.cam) c.cullingMask &= ~(1 << Layer);
            return gp;
        }

        public void Show(RawImage img)
        {
            target = img;
            Refresh();
        }

        public void Turn(float deg) => yaw += deg;

        // Gear changed: a ring and a flash at the hero's feet, and a little bounce.
        public void Flash(Color color)
        {
            Vector3 feet = turntable.position;
            Fx.Shockwave(feet, 1.6f, color, Layer, cam);
            Fx.Spark(feet + Vector3.up * 1.1f, color, 2.2f, Layer, cam);
            pop = 1f;
        }

        public void Refresh()
        {
            var p = Player.Instance;
            if (p == null) return;
            if (body == null || cls != p.Class.Id)
            {
                if (body != null) Destroy(body.gameObject);
                cls = p.Class.Id;
                body = Player.MakeBody(turntable, p.Class, out _);
                aura = Aura.Attach(body, p.Class.BodyColor);
                gear = body.GetComponentInChildren<GearVisual>();
                wingCode = -1;
            }
            if (gear != null) gear.Apply(GearVisual.TiersOf(p.Data));
            aura.SetLevel(p.GlowPlus);
            int wc = Player.WingCode(p.Data.wingTier, p.Data.wingLevel);
            if (wc != wingCode)
            {
                wingCode = wc;
                if (wings != null) Destroy(wings.gameObject);
                wings = Player.MakeWings(turntable, wc);
            }
            SetLayer(turntable);
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            foreach (Transform c in t) SetLayer(c);
        }

        void LateUpdate()
        {
            bool on = target != null && target.isActiveAndEnabled;
            cam.enabled = on;
            if (!on) return;
            turntable.localRotation = Quaternion.Euler(0, yaw, 0);
            pop = Mathf.Max(0f, pop - Time.unscaledDeltaTime * 4f);
            turntable.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(pop * Mathf.PI));
            foreach (var l in GetComponentsInChildren<Light>()) l.cullingMask = 1 << Layer; // the aura's glow light too
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
