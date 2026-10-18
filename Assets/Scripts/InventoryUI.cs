using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MuProto
{
    // The Backpack: equipment slots on the left, the hero in 3D in the middle (wearing what is
    // equipped, drag to turn), the bag on the right, and the picked item's details underneath.
    // Tap an item to look at it, tap it again (or the button) to put it on / take it off.
    public class InventoryUI
    {
        const float Tile = 112, Gap = 10, Cell = 112, CellGap = 8;
        const int Cols = 6;
        static readonly Vector2 TL = new Vector2(0, 1);

        // Left column, then right column.
        static readonly int[] SlotOrder =
        {
            (int)Slot.Weapon, (int)Slot.Helm, (int)Slot.Armor, (int)Slot.Pants, (int)Slot.Necklace,
            (int)Slot.Gloves, (int)Slot.Boots, (int)Slot.Ring, GameData.Ring2Index, -1, // -1: wings
        };

        readonly RectTransform panel, equipRoot, bagRoot, actions;
        readonly GameObject root;
        readonly Text detail, info, bagTitle, powerText, nameText, zen, diamonds, bless, soul;
        readonly RawImage view;
        readonly HashSet<Item> seen = new HashSet<Item>();
        int pickedEquip = -1, pickedBag = -1;
        bool firstOpen = true;

        public InventoryUI(Transform canvas)
        {
            (root, panel) = Skin.Window(canvas, "Inventory", "Backpack", new Vector2(1560, 960), Close);

            zen = Skin.Pill(panel, UIB.Circle, new Color(1f, 0.8f, 0.25f), TL, new Vector2(48, -92), 210);
            diamonds = Skin.Pill(panel, Skin.Diamond, new Color(0.45f, 0.8f, 1f), TL, new Vector2(282, -92), 170);
            bless = Skin.Pill(panel, Skin.Diamond, new Color(0.7f, 0.55f, 1f), TL, new Vector2(1060, -92), 200);
            soul = Skin.Pill(panel, Skin.Diamond, new Color(1f, 0.55f, 0.75f), TL, new Vector2(1290, -92), 200);

            // equipment slots
            Skin.Sliced(UIB.Rect("EquipBox", panel, TL, new Vector2(22, -146), new Vector2(256, 622)), Skin.Inset, Color.white).raycastTarget = false;
            equipRoot = UIB.Rect("Equipped", panel, TL, new Vector2(34, -158), new Vector2(234, 600));

            // the hero
            var stage = UIB.Rect("Stage", panel, TL, new Vector2(290, -146), new Vector2(440, 622));
            Skin.Sliced(stage, Skin.Inset, Color.white).raycastTarget = false;
            var glow = UIB.Rect("Glow", stage, new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(380, 120), new Vector2(0.5f, 0.5f));
            UIB.Image(glow, new Color(1f, 0.85f, 0.45f, 0.35f), Skin.Soft).raycastTarget = false;
            var viewRt = UIB.Stretch(UIB.Rect("View", stage, Vector2.zero, Vector2.zero, Vector2.zero));
            viewRt.offsetMin = new Vector2(6, 6);
            viewRt.offsetMax = new Vector2(-6, -6);
            view = viewRt.gameObject.AddComponent<RawImage>();
            view.gameObject.AddComponent<PreviewDrag>();
            nameText = UIB.Label(stage, "", 24, TL, new Vector2(16, -10), new Vector2(408, 34), TextAnchor.MiddleCenter);
            nameText.color = Skin.Cream;
            nameText.raycastTarget = false;
            var pw = UIB.Rect("Power", stage, new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(320, 52), new Vector2(0.5f, 0));
            UIB.Image(pw, Color.white, Skin.Banner).raycastTarget = false;
            powerText = Skin.Title(pw, "", 28, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 52));
            powerText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            powerText.raycastTarget = false;

            // bag
            bagTitle = Skin.Title(panel, "", 26, TL, new Vector2(746, -92), new Vector2(300, 44), TextAnchor.MiddleLeft);
            Skin.Sliced(UIB.Rect("BagBox", panel, TL, new Vector2(742, -146), new Vector2(796, 622)), Skin.Inset, Color.white).raycastTarget = false;
            bagRoot = UIB.Rect("Bag", panel, TL, new Vector2(778, -158), new Vector2(Cols * (Cell + CellGap), 600));

            // details of the picked item and what you can do with it
            Skin.Sliced(UIB.Rect("DetailBox", panel, TL, new Vector2(22, -778), new Vector2(1516, 92)), Skin.Inset, Color.white).raycastTarget = false;
            detail = UIB.Label(panel, "", 21, TL, new Vector2(40, -784), new Vector2(1020, 56), TextAnchor.UpperLeft);
            detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            info = UIB.Label(panel, "", 20, TL, new Vector2(40, -838), new Vector2(1020, 28), TextAnchor.MiddleLeft);
            info.color = Skin.Gold;
            actions = UIB.Rect("Actions", panel, TL, new Vector2(1070, -784), new Vector2(460, 80));

            var bl = new Vector2(0, 0);
            UIB.Button(panel, "Sell Junk", bl, new Vector2(30, 18), new Vector2(220, 62), new Color(0.6f, 0.45f, 0.15f),
                () => info.text = "Sold " + Player.Instance.SellCommons() + " items (kept Excellent/Ancient gear you can wear)", null, 24);
            UIB.Button(panel, "Gems", bl, new Vector2(266, 18), new Vector2(200, 62), new Color(0.2f, 0.45f, 0.6f),
                () => HUD.Instance.Open("Gems"), null, 24);
            UIB.Button(panel, "Chaos Machine", bl, new Vector2(482, 18), new Vector2(230, 62), new Color(0.5f, 0.2f, 0.5f),
                () => HUD.Instance.Open("Chaos"), null, 22);
            UIB.Button(panel, "Wings", bl, new Vector2(728, 18), new Vector2(200, 62), new Color(0.25f, 0.4f, 0.3f),
                () => HUD.Instance.Open("Wings"), null, 24);
            UIB.Button(panel, "Reset Save", new Vector2(1, 0), new Vector2(-30, 18), new Vector2(220, 62), new Color(0.45f, 0.15f, 0.15f),
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
            info.text = "Tap an item to look at it, tap again to wear it. Drag the hero to turn.";
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
            bagTitle.text = "Bag  <size=22><color=#c9b48a>" + d.bag.Count + " / " + GameData.BagSize + "</color></size>";
            if (pickedBag >= d.bag.Count) pickedBag = -1;
            GearPreview.Get().Refresh();

            Clear(equipRoot);
            for (int i = 0; i < SlotOrder.Length; i++)
            {
                int index = SlotOrder[i];
                var pos = new Vector2((i / 5) * (Tile + Gap), -(i % 5) * (Tile + Gap));
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
            }
            ShowDetail();
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
            if (pickedEquip == index && !p.Data.equipped[index].IsEmpty) { seen.Add(p.Data.equipped[index]); info.text = p.Unequip(index); pickedEquip = -1; return; }
            pickedEquip = index;
            pickedBag = -1;
            Refresh();
        }

        void PickBag(int index)
        {
            var p = Player.Instance;
            seen.Add(p.Data.bag[index]);
            if (pickedBag == index) { seen.Add(Current(p.Data.bag[index])); info.text = p.Equip(index); pickedBag = -1; Refresh(); return; }
            pickedBag = index;
            pickedEquip = -1;
            Refresh();
        }

        void ShowDetail()
        {
            Clear(actions);
            var p = Player.Instance;
            var right = new Vector2(1, 0.5f);
            if (pickedBag >= 0)
            {
                int idx = pickedBag;
                var it = p.Data.bag[idx];
                var cur = Current(it);
                int diff = it.Stat - (cur.IsEmpty ? 0 : cur.Stat);
                bool usable = p.CanEquip(it, out string why);
                detail.text = Describe(it) + "   <color=" + (diff >= 0 ? "#7CFF6B" : "#FF6B5A") + ">" + (diff >= 0 ? "+" : "") + diff + " " + it.StatName + " vs worn</color>"
                              + (usable ? "" : "   <color=#FF6B5A>" + why + "</color>");
                if (usable)
                    UIB.Button(actions, "Equip", right, new Vector2(0, 0), new Vector2(200, 72), new Color(0.2f, 0.5f, 0.22f),
                        () => { seen.Add(cur); info.text = p.Equip(idx); pickedBag = -1; Refresh(); }, null, 28, new Vector2(1, 0.5f));
            }
            else if (pickedEquip >= 0 && !p.Data.equipped[pickedEquip].IsEmpty)
            {
                int index = pickedEquip;
                var it = p.Data.equipped[index];
                detail.text = Describe(it);
                UIB.Button(actions, "Take off", right, new Vector2(0, 0), new Vector2(180, 72), new Color(0.4f, 0.32f, 0.2f),
                    () => { seen.Add(it); info.text = p.Unequip(index); pickedEquip = -1; Refresh(); }, null, 26, new Vector2(1, 0.5f));
                if (it.CanEnhance)
                {
                    string jewel = it.UsesSoul ? "Soul " + Mathf.RoundToInt(it.EnhanceChance * 100) + "%" : "Bless";
                    UIB.Button(actions, "+" + (it.plus + 1) + " " + jewel + "\n" + it.EnhanceGold.ToString("N0") + "g", right, new Vector2(-196, 0), new Vector2(240, 72),
                        it.UsesSoul ? new Color(0.55f, 0.2f, 0.4f) : new Color(0.35f, 0.25f, 0.55f),
                        () => info.text = p.Enhance(index), null, 21, new Vector2(1, 0.5f));
                }
            }
            else detail.text = pickedEquip >= 0 ? "Nothing equipped there." : "<color=#c9b48a>Equipped gear shows on your hero. The set glows from +7.</color>";
        }

        static string Describe(Item it)
        {
            string hex = ColorUtility.ToHtmlStringRGB(GameData.RarityColor(it.rarity));
            string extras = InventoryUI.Extras(it);
            return "<color=#" + hex + "><b>" + it.Label + "</b></color>  " + GameData.RarityName(it.rarity) + " · " + GameData.TierName(it.tier) +
                   "   " + it.StatName + " " + it.Stat + "   Req " + it.ReqStat + (it.ReqRebirth > 0 ? " · " + it.ReqRebirth + "R" : "") +
                   (extras.Length > 0 ? "\n<size=18>" + extras + "</size>" : "");
        }

        void WingsTile(Vector2 pos)
        {
            var d = Player.Instance.Data;
            var b = UIB.Button(equipRoot, d.wingTier > 0 ? "Wings\nT" + d.wingTier + " +" + d.wingLevel : "Wings", TL, pos, new Vector2(Tile, Tile),
                d.wingTier > 0 ? Color.Lerp(Upgrades.WingColor(d.wingTier), Color.black, 0.55f) : new Color(0.1f, 0.1f, 0.12f), () => HUD.Instance.Open("Wings"), null, 19);
            b.GetComponentInChildren<Text>().color = d.wingTier > 0 ? Skin.Cream : Skin.Muted;
        }

        // An item square: rarity colour, a tier swatch, +level, short name, and NEW / UP marks.
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
            var b = UIB.Button(parent, "", TL, pos, new Vector2(size, size), Color.Lerp(rc, Color.black, 0.72f), onClick, null, 16);
            var edge = b.transform.Find("Edge");
            if (edge != null) edge.GetComponent<Image>().color = Color.Lerp(rc, Color.white, 0.15f);

            var sw = UIB.Rect("Tier", b.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(size * 0.46f, size * 0.46f), new Vector2(0.5f, 0.5f));
            UIB.Image(sw, GearVisual.TierSwatch(it.classId, it.tier), Skin.Diamond).raycastTarget = false;
            var kind = UIB.Label(b.transform, ShortName(it), 16, new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(size - 8, 22), TextAnchor.MiddleCenter);
            kind.rectTransform.pivot = new Vector2(0.5f, 0);
            kind.color = rc;
            kind.raycastTarget = false;
            var tier = UIB.Label(b.transform, "T" + it.tier, 16, TL, new Vector2(6, -4), new Vector2(40, 20), TextAnchor.UpperLeft);
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
                var up = UIB.Label(b.transform, "▲", 20, new Vector2(1, 0), new Vector2(-6, 22), new Vector2(30, 24), TextAnchor.LowerRight);
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

        static string ShortName(Item it)
        {
            string n = it.name;
            int sp = n.LastIndexOf(' ');
            return sp > 0 && n.Length > 12 ? n.Substring(sp + 1) : n;
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
        float yaw = 20f;

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
            gp.Texture = new RenderTexture(440, 620, 24, RenderTextureFormat.ARGB32) { name = "Gear Preview", antiAliasing = 4 };
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
            foreach (var l in GetComponentsInChildren<Light>()) l.cullingMask = 1 << Layer; // the aura's glow light too
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
