using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MuProto
{
    public class HUD : MonoBehaviour
    {
        public static HUD Instance { get; private set; }
        public RectTransform FxLayer { get; private set; }

        Text levelText, hpText, goldText, centerText;
        RectTransform hpFill, expFill;
        Image[] cooldowns = new Image[3];
        Button autoButton;
        InventoryUI inventory;

        public static void Create()
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            var go = new GameObject("HUD", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<HUD>().Build();
        }

        void Build()
        {
            Instance = this;
            var root = transform;
            var tl = new Vector2(0, 1);
            var br = new Vector2(1, 0);
            var center = new Vector2(0.5f, 0.5f);

            // Floating combat text sits behind every control.
            FxLayer = UIB.Stretch(UIB.Rect("FX", root, Vector2.zero, Vector2.zero, Vector2.zero));

            // Character panel, top left.
            var panel = UIB.Rect("Status", root, tl, new Vector2(20, -20), new Vector2(520, 200));
            UIB.Image(panel, new Color(0, 0, 0, 0.45f)).raycastTarget = false;
            levelText = UIB.Label(panel, "", 34, tl, new Vector2(20, -12), new Vector2(480, 44));
            hpFill = UIB.Bar(panel, tl, new Vector2(20, -64), new Vector2(480, 34), new Color(0.85f, 0.15f, 0.15f));
            hpText = UIB.Label(hpFill.parent, "", 24, center, Vector2.zero, new Vector2(480, 34), TextAnchor.MiddleCenter);
            expFill = UIB.Bar(panel, tl, new Vector2(20, -108), new Vector2(480, 16), new Color(0.95f, 0.8f, 0.2f));
            goldText = UIB.Label(panel, "", 28, tl, new Vector2(20, -136), new Vector2(480, 44));

            centerText = UIB.Label(root, "", 48, new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(900, 80), TextAnchor.MiddleCenter);

            // Joystick, bottom left.
            var stickBg = UIB.Rect("Joystick", root, Vector2.zero, new Vector2(240, 240), new Vector2(300, 300), center);
            UIB.Image(stickBg, new Color(1, 1, 1, 0.15f), UIB.Circle);
            var knob = UIB.Rect("Knob", stickBg, center, Vector2.zero, new Vector2(130, 130));
            UIB.Image(knob, new Color(1, 1, 1, 0.5f), UIB.Circle).raycastTarget = false;
            var stick = stickBg.gameObject.AddComponent<Joystick>();
            stick.Bg = stickBg;
            stick.Knob = knob;

            // Combat buttons, bottom right.
            UIB.Button(root, "Attack", br, new Vector2(-190, 190), new Vector2(220, 220), new Color(0.8f, 0.25f, 0.2f, 0.85f),
                () => Player.Instance.AttackPressed(), UIB.Circle, 40, center);
            Vector2[] skillPos = { new Vector2(-430, 130), new Vector2(-400, 350), new Vector2(-190, 440) };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = UIB.Button(root, Player.SkillNames[i], br, skillPos[i], new Vector2(150, 150), new Color(0.25f, 0.35f, 0.7f, 0.85f),
                    () => Player.Instance.UseSkill(idx), UIB.Circle, 28, center);
                var cd = UIB.Image(UIB.Stretch(UIB.Rect("Cooldown", b.transform, Vector2.zero, Vector2.zero, Vector2.zero)), new Color(0, 0, 0, 0.6f), UIB.Circle);
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Radial360;
                cd.fillOrigin = (int)Image.Origin360.Top;
                cd.fillClockwise = false;
                cd.raycastTarget = false;
                cooldowns[i] = cd;
            }
            autoButton = UIB.Button(root, "AUTO", br, new Vector2(-560, 470), new Vector2(170, 90), Color.gray,
                () => Player.Instance.AutoBattle = !Player.Instance.AutoBattle, null, 32, center);

            UIB.Button(root, "Bag", new Vector2(1, 1), new Vector2(-20, -20), new Vector2(180, 90), new Color(0.3f, 0.3f, 0.35f, 0.9f),
                () => inventory.Toggle());

            inventory = new InventoryUI(root);
        }

        void Update()
        {
            var p = Player.Instance;
            if (p == null) return;
            var d = p.Data;
            int need = GameData.ExpToNext(d.level);
            levelText.text = "Lv " + d.level + "      CP " + p.CombatPower;
            UIB.SetBar(hpFill, (float)p.Hp / p.MaxHp);
            hpText.text = p.Hp + " / " + p.MaxHp;
            UIB.SetBar(expFill, (float)d.exp / need);
            goldText.text = "Gold " + d.gold + "     EXP " + d.exp + "/" + need;
            for (int i = 0; i < 3; i++) cooldowns[i].fillAmount = p.SkillCooldownRatio(i);
            autoButton.image.color = p.AutoBattle ? new Color(0.2f, 0.7f, 0.3f, 0.95f) : new Color(0.4f, 0.4f, 0.4f, 0.85f);
            centerText.text = p.IsDead ? "Defeated. Respawn in " + Mathf.CeilToInt(p.RespawnIn) : "";
        }
    }

    public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Bg, Knob;

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Bg, e.position, e.pressEventCamera, out Vector2 local);
            float radius = Bg.sizeDelta.x * 0.5f;
            Vector2 v = Vector2.ClampMagnitude(local / radius, 1f);
            Knob.anchoredPosition = v * radius;
            if (Player.Instance != null) Player.Instance.MoveInput = v;
        }

        public void OnPointerUp(PointerEventData e)
        {
            Knob.anchoredPosition = Vector2.zero;
            if (Player.Instance != null) Player.Instance.MoveInput = Vector2.zero;
        }
    }

    public class FloatingText : MonoBehaviour
    {
        Vector3 world;
        Text text;
        Color color;
        float t;

        public static void Spawn(Vector3 worldPos, string s, Color color, int size = 34)
        {
            if (HUD.Instance == null) return;
            var rt = UIB.Rect("FloatText", HUD.Instance.FxLayer, Vector2.zero, Vector2.zero, new Vector2(600, 70), new Vector2(0.5f, 0.5f));
            var ft = rt.gameObject.AddComponent<FloatingText>();
            ft.text = UIB.Text(rt, s, size, color, TextAnchor.MiddleCenter);
            ft.world = worldPos + new Vector3(Random.Range(-0.4f, 0.4f), 0, 0);
            ft.color = color;
            ft.Place();
        }

        void LateUpdate()
        {
            t += Time.deltaTime;
            if (t >= 1f) { Destroy(gameObject); return; }
            color.a = 1f - t * t;
            text.color = color;
            Place();
        }

        void Place()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(world + Vector3.up * t * 1.5f);
            text.enabled = sp.z > 0;
            transform.position = new Vector3(sp.x, sp.y, 0);
        }
    }

    public class InventoryUI
    {
        readonly RectTransform panel, equipRoot, bagRoot;
        readonly Text info;

        public InventoryUI(Transform canvas)
        {
            var center = new Vector2(0.5f, 0.5f);
            var tl = new Vector2(0, 1);
            panel = UIB.Rect("Inventory", canvas, center, Vector2.zero, new Vector2(1360, 820));
            UIB.Image(panel, new Color(0.08f, 0.08f, 0.12f, 0.95f));
            UIB.Label(panel, "Equipment", 36, tl, new Vector2(30, -20), new Vector2(560, 50));
            UIB.Label(panel, "Bag (tap to equip)", 36, tl, new Vector2(640, -20), new Vector2(680, 50));
            equipRoot = UIB.Rect("Equipped", panel, tl, new Vector2(30, -80), new Vector2(580, 520));
            bagRoot = UIB.Rect("Bag", panel, tl, new Vector2(640, -80), new Vector2(690, 600));
            info = UIB.Label(panel, "", 28, tl, new Vector2(30, -620), new Vector2(580, 60));

            var bl = new Vector2(0, 0);
            UIB.Button(panel, "Sell Commons", bl, new Vector2(30, 30), new Vector2(260, 80), new Color(0.6f, 0.45f, 0.15f),
                () => info.text = "Sold " + Player.Instance.SellCommons() + " items");
            UIB.Button(panel, "Reset Save", bl, new Vector2(310, 30), new Vector2(220, 80), new Color(0.45f, 0.15f, 0.15f),
                () => { Player.Instance.ResetSave(); info.text = "Save reset"; }, null, 26);
            UIB.Button(panel, "Close", new Vector2(1, 0), new Vector2(-30, 30), new Vector2(200, 80), new Color(0.3f, 0.3f, 0.35f), Toggle);

            Player.Instance.Changed += () => { if (panel.gameObject.activeSelf) Refresh(); };
            panel.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            bool show = !panel.gameObject.activeSelf;
            panel.gameObject.SetActive(show);
            if (show) { info.text = ""; Refresh(); }
        }

        void Refresh()
        {
            Clear(equipRoot);
            Clear(bagRoot);
            var p = Player.Instance;
            var tl = new Vector2(0, 1);

            for (int s = 0; s < 4; s++)
            {
                var slot = (Slot)s;
                var it = p.Data.equipped[s];
                var row = UIB.Rect("Slot", equipRoot, tl, new Vector2(0, -s * 128), new Vector2(580, 116));
                UIB.Image(row, new Color(1, 1, 1, 0.06f));
                string desc = it.IsEmpty ? slot + ": empty" : it.Label + "\n" + it.StatName + " " + it.Stat;
                var label = UIB.Label(row, desc, 26, tl, new Vector2(16, -8), new Vector2(330, 100));
                if (!it.IsEmpty) label.color = GameData.RarityColor(it.rarity);
                if (it.CanEnhance)
                    UIB.Button(row, "Enhance\n" + it.EnhanceCost + "g  " + Mathf.RoundToInt(it.EnhanceChance * 100) + "%",
                        new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(210, 96), new Color(0.2f, 0.45f, 0.25f),
                        () => Enhance(slot), null, 22);
            }

            const int cols = 6;
            const float cell = 106, gap = 8;
            for (int i = 0; i < p.Data.bag.Count; i++)
            {
                int idx = i;
                var it = p.Data.bag[i];
                var current = p.Data.equipped[(int)it.slot];
                bool better = current.IsEmpty || it.Stat > current.Stat;
                var b = UIB.Button(bagRoot, it.name.Replace(" Lv", "\nLv") + "\n" + it.StatName + " " + it.Stat,
                    tl, new Vector2((i % cols) * (cell + gap), -(i / cols) * (cell + gap)), new Vector2(cell, cell),
                    better ? new Color(0.15f, 0.4f, 0.2f) : new Color(0.22f, 0.22f, 0.28f), () => p.Equip(idx), null, 17);
                b.GetComponentInChildren<Text>().color = GameData.RarityColor(it.rarity);
            }
        }

        void Enhance(Slot slot)
        {
            bool? result = Player.Instance.Enhance(slot);
            info.text = result == null ? "Not enough gold" : result.Value ? "Enhance succeeded!" : "Enhance failed";
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
