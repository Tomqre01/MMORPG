using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MuProto
{
    public partial class HUD : MonoBehaviour
    {
        public static HUD Instance { get; private set; }
        public RectTransform FxLayer { get; private set; }

        // Portrait block (top left)
        Text nameText, cpText, levelBadge, hpText, portraitGlyph, stateText;
        Image portraitOrb, portraitGlow;
        RectTransform hpFill;
        // Currencies
        Text zenText, diamondText, blessText, soulText;
        // Target frame (top centre)
        GameObject targetFrame;
        Text targetName, targetHp;
        RectTransform targetFill;
        // Quest tracker (left)
        Text questTitle, questBody, questHint, bossText;
        Image questTabImg, teamTabImg;
        int trackerTab;
        // Bottom
        RectTransform expFill;
        Text expText, centerText, dungeonText, potionText, cpToast;
        float cpToastUntil;
        int lastCp = -1;
        // Combat cluster
        readonly Image[] cooldowns = new Image[3];
        readonly Image[] skillOrbs = new Image[3];
        readonly Text[] skillLabels = new Text[3];
        Button autoButton, pkButton, autoPotButton, rideButton;
        Text autoText;
        // Activity icons and the expanding menu
        GameObject activityRow, menuGrid;
        GameObject eventsBadge, achieveBadge, menuBadge, charBadge, talentBadge, bagBadge;
        Text dungeonGlyph, foldText;

        InventoryUI inventory;
        MenuPanel character, wingsPanel, petPanel, dungeonPanel;
        GameObject classSelect;
        string message;
        float messageUntil;

        public static void Message(string text, float seconds)
        {
            if (Instance == null) return;
            Instance.message = text;
            Instance.messageUntil = Time.time + seconds;
        }

        public static void Create()
        {
            NewCanvas("HUD").AddComponent<HUD>().Build();
        }

        // A full-screen overlay canvas at 1920x1080 reference size, plus an EventSystem if missing.
        public static GameObject NewCanvas(string name)
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

            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f; // landscape phones: keep the height, let wide screens get more width
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        // Opens a window by name: Bag, Character, Wings, Pet, Dungeon, or any of the extra windows.
        public void Open(string name)
        {
            menuGrid.SetActive(false);
            switch (name)
            {
                case "Bag": inventory.Show(); break;
                case "Character": case "Skills": character.Show(); break;
                case "Wings": wingsPanel.Show(); break;
                case "Pet": petPanel.Show(); break;
                case "Dungeon":
                    if (DungeonRun.Active != null) DungeonRun.Active.Leave();
                    else dungeonPanel.Show();
                    break;
                case "Guild": OpenExtra("Social"); break;
                default: OpenExtra(name); break;
            }
        }

        static readonly Vector2 TL = new Vector2(0, 1), TR = new Vector2(1, 1), BL = Vector2.zero, BR = new Vector2(1, 0),
                                TC = new Vector2(0.5f, 1), BC = new Vector2(0.5f, 0), C = new Vector2(0.5f, 0.5f);

        public void ShowMenu(bool on) => menuGrid.SetActive(on);

        public void CloseAll()
        {
            menuGrid.SetActive(false);
            foreach (var w in Skin.Windows) if (w != null) w.SetActive(false);
        }

        void Build()
        {
            Instance = this;
            Skin.Windows.Clear();
            var root = transform;

            // Floating combat text sits behind every control.
            FxLayer = UIB.Stretch(UIB.Rect("FX", root, Vector2.zero, Vector2.zero, Vector2.zero));

            BuildPortrait(root);
            BuildCurrencies(root);
            BuildTarget(root);
            BuildMinimap(root);
            BuildActivities(root);
            BuildTracker(root);
            BuildJoystick(root);
            BuildCombat(root);
            BuildExpBar(root);

            centerText = UIB.Label(root, "", 44, TC, new Vector2(0, -330), new Vector2(1300, 140), TextAnchor.MiddleCenter);
            centerText.rectTransform.pivot = new Vector2(0.5f, 1);
            centerText.color = Skin.Cream;
            dungeonText = UIB.Label(root, "", 32, TC, new Vector2(0, -236), new Vector2(1000, 90), TextAnchor.MiddleCenter);
            dungeonText.rectTransform.pivot = new Vector2(0.5f, 1);
            dungeonText.color = Skin.Gold;
            cpToast = UIB.Label(root, "", 46, C, new Vector2(0, -160), new Vector2(700, 80), TextAnchor.MiddleCenter);
            cpToast.rectTransform.pivot = C;
            cpToast.fontStyle = FontStyle.Bold;

            BuildMenuGrid(root);

            inventory = new InventoryUI(root);
            BuildCharacter(root);
            BuildWings(root);
            BuildPet(root);
            BuildDungeons(root);
            BuildExtra(root);
            BuildClassSelect(root);
        }

        // ---- top left: portrait, name, CP, HP and buffs ----

        void BuildPortrait(Transform root)
        {
            var block = UIB.Rect("Status", root, TL, new Vector2(14, -10), new Vector2(640, 200));
            var plate = Skin.Frame(block, "Plate", TL, new Vector2(100, -22), new Vector2(520, 128));
            plate.GetComponent<Image>().raycastTarget = false;
            nameText = UIB.Label(plate, "", 26, TL, new Vector2(88, -10), new Vector2(420, 34));
            nameText.color = Skin.Cream;
            cpText = UIB.Label(plate, "", 30, TL, new Vector2(88, -42), new Vector2(420, 36));
            cpText.color = new Color(1f, 0.72f, 0.25f);
            cpText.fontStyle = FontStyle.Bold;
            hpFill = UIB.Bar(plate, TL, new Vector2(80, -82), new Vector2(424, 30), new Color(0.9f, 0.16f, 0.12f));
            hpText = UIB.Label(hpFill.parent, "", 21, C, Vector2.zero, new Vector2(424, 30), TextAnchor.MiddleCenter);
            hpText.rectTransform.pivot = C;

            var portrait = UIB.Rect("Portrait", block, TL, new Vector2(0, 0), new Vector2(176, 176));
            portraitGlow = UIB.Image(UIB.Rect("Glow", portrait, C, Vector2.zero, new Vector2(230, 230), C), new Color(1, 1, 1, 0.35f), Skin.Soft);
            portraitGlow.raycastTarget = false;
            var orbRt = UIB.Rect("Orb", portrait, C, Vector2.zero, new Vector2(160, 160), C);
            portraitOrb = UIB.Image(orbRt, Color.gray, Skin.Orb);
            Skin.Child(orbRt, "Ring", Skin.Ring, Color.white);
            orbRt.gameObject.AddComponent<Button>().onClick.AddListener(() => Open("Character"));
            portraitGlyph = UIB.Label(orbRt, "", 52, C, Vector2.zero, new Vector2(160, 80), TextAnchor.MiddleCenter);
            portraitGlyph.rectTransform.pivot = C;
            portraitGlyph.fontStyle = FontStyle.Bold;
            portraitGlyph.color = Skin.Cream;

            var badge = UIB.Rect("Level", portrait, BC, new Vector2(0, -2), new Vector2(84, 50), new Vector2(0.5f, 0.5f));
            Skin.Sliced(badge, Skin.Button, new Color(0.25f, 0.08f, 0.05f)).raycastTarget = false;
            Skin.Child(badge, "Edge", Skin.ButtonEdge, Color.white, true);
            levelBadge = UIB.Label(badge, "", 24, C, Vector2.zero, new Vector2(84, 50), TextAnchor.MiddleCenter);
            levelBadge.rectTransform.pivot = C;
            levelBadge.color = Skin.Gold;
            levelBadge.fontStyle = FontStyle.Bold;
            var cb = UIB.Rect("CharBadge", portrait, TR, new Vector2(-14, -14), new Vector2(34, 34), C);
            UIB.Image(cb, new Color(1f, 0.15f, 0.1f), UIB.Circle).raycastTarget = false;
            charBadge = cb.gameObject;

            stateText = UIB.Label(block, "", 22, TL, new Vector2(186, -156), new Vector2(460, 40), TextAnchor.MiddleLeft);
        }

        // ---- currencies ----

        void BuildCurrencies(Transform root)
        {
            zenText = Skin.Pill(root, UIB.Circle, new Color(1f, 0.8f, 0.25f), TL, new Vector2(680, -22), 200);
            diamondText = Skin.Pill(root, Skin.Diamond, new Color(0.45f, 0.8f, 1f), TL, new Vector2(900, -22), 170);
            blessText = Skin.Pill(root, Skin.Diamond, new Color(0.7f, 0.55f, 1f), TL, new Vector2(680, -76), 200);
            soulText = Skin.Pill(root, Skin.Diamond, new Color(1f, 0.55f, 0.75f), TL, new Vector2(900, -76), 170);
        }

        // ---- target frame ----

        void BuildTarget(Transform root)
        {
            var rt = UIB.Rect("Target", root, TC, new Vector2(0, -136), new Vector2(620, 92), new Vector2(0.5f, 1));
            UIB.Image(rt, Color.white, Skin.Banner).raycastTarget = false;
            targetName = UIB.Label(rt, "", 26, TC, new Vector2(0, -6), new Vector2(600, 36), TextAnchor.MiddleCenter);
            targetName.rectTransform.pivot = new Vector2(0.5f, 1);
            targetFill = UIB.Bar(rt, TC, new Vector2(0, -44), new Vector2(520, 26), new Color(0.85f, 0.15f, 0.1f), new Vector2(0.5f, 1));
            targetHp = UIB.Label(targetFill.parent, "", 19, C, Vector2.zero, new Vector2(520, 26), TextAnchor.MiddleCenter);
            targetHp.rectTransform.pivot = C;
            targetFrame = rt.gameObject;
        }

        // ---- left: quest / team tracker ----

        void BuildTracker(Transform root)
        {
            var questTab = UIB.Button(root, "Quest", TL, new Vector2(20, -246), new Vector2(150, 48), Color.white, () => trackerTab = 0, Skin.Tab, 24);
            questTabImg = questTab.image;
            questTabImg.type = Image.Type.Sliced;
            var teamTab = UIB.Button(root, "Team", TL, new Vector2(176, -246), new Vector2(150, 48), Color.white, () => trackerTab = 1, Skin.Tab, 24);
            teamTabImg = teamTab.image;
            teamTabImg.type = Image.Type.Sliced;

            var panel = UIB.Rect("Tracker", root, TL, new Vector2(16, -292), new Vector2(480, 190));
            Skin.Sliced(panel, Skin.Inset, Color.white);
            panel.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (trackerTab == 1) { Open("Social"); return; }
                string m = Quests.AutoRun(Player.Instance);
                if (m != null) Message(m, 2.5f);
            });
            questTitle = UIB.Label(panel, "", 25, TL, new Vector2(18, -12), new Vector2(450, 34));
            questTitle.color = Skin.Gold;
            questTitle.fontStyle = FontStyle.Bold;
            questBody = UIB.Label(panel, "", 22, TL, new Vector2(18, -50), new Vector2(450, 96), TextAnchor.UpperLeft);
            questBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            questBody.color = Skin.Cream;
            questHint = UIB.Label(panel, "", 20, BL, new Vector2(18, 10), new Vector2(450, 30));
            questHint.color = new Color(0.55f, 1f, 0.5f);

            bossText = UIB.Label(root, "", 23, TL, new Vector2(22, -494), new Vector2(560, 34));
            bossText.color = new Color(0.9f, 0.65f, 1f);
        }

        // ---- joystick ----

        void BuildJoystick(Transform root)
        {
            var stickBg = UIB.Rect("Joystick", root, BL, new Vector2(240, 230), new Vector2(300, 300), C);
            UIB.Image(stickBg, new Color(0.35f, 0.32f, 0.3f, 0.35f), Skin.Orb);
            Skin.Child(stickBg, "Ring", Skin.Ring, new Color(1, 1, 1, 0.55f));
            var knob = UIB.Rect("Knob", stickBg, C, Vector2.zero, new Vector2(130, 130));
            UIB.Image(knob, new Color(0.95f, 0.85f, 0.6f, 0.8f), Skin.Orb).raycastTarget = false;
            Skin.Child(knob, "Ring", Skin.Ring, new Color(1, 1, 1, 0.8f));
            var stick = stickBg.gameObject.AddComponent<Joystick>();
            stick.Bg = stickBg;
            stick.Knob = knob;
        }

        // ---- bottom right: attack, skill arc, potion, toggles ----

        void BuildCombat(Transform root)
        {
            var atkPos = new Vector2(-190, 200);
            var atk = UIB.Button(root, "Attack", BR, atkPos, new Vector2(240, 240), new Color(0.72f, 0.2f, 0.12f), () => Player.Instance.AttackPressed(), UIB.Circle, 40, C);
            atk.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;

            float[] angles = { 170, 130, 90 };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                float a = angles[i] * Mathf.Deg2Rad;
                var pos = atkPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 255f;
                var b = UIB.Button(root, "Skill", BR, pos, new Vector2(140, 140), Skin.Blue, () => Player.Instance.UseSkill(idx), UIB.Circle, 22, C);
                skillOrbs[i] = b.image;
                skillLabels[i] = b.GetComponentInChildren<Text>();
                skillLabels[i].fontStyle = FontStyle.Bold;
                var cd = UIB.Image(UIB.Stretch(UIB.Rect("Cooldown", b.transform, Vector2.zero, Vector2.zero, Vector2.zero)), new Color(0, 0, 0, 0.65f), UIB.Circle);
                cd.rectTransform.offsetMin = new Vector2(7, 7);
                cd.rectTransform.offsetMax = new Vector2(-7, -7);
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Radial360;
                cd.fillOrigin = (int)Image.Origin360.Top;
                cd.fillClockwise = false;
                cd.raycastTarget = false;
                cd.transform.SetSiblingIndex(1);
                cooldowns[i] = cd;
            }

            float pa = 205 * Mathf.Deg2Rad;
            var potion = UIB.Button(root, "Potion", BR, atkPos + new Vector2(Mathf.Cos(pa), Mathf.Sin(pa)) * 250f, new Vector2(108, 108), new Color(0.7f, 0.12f, 0.18f),
                () => { if (!Player.Instance.UsePotion() && Player.Instance.Data.potions <= 0) Message("No potions. Buy them at the Merchant", 2f); }, UIB.Circle, 20, C);
            potionText = potion.GetComponentInChildren<Text>();

            autoButton = UIB.Button(root, "AUTO", BR, new Vector2(-610, 110), new Vector2(128, 128), Skin.Slate,
                () => { var p = Player.Instance; p.AutoBattle = !p.AutoBattle; if (!p.AutoBattle) p.StartQuestHunt(null); }, UIB.Circle, 30, C);
            autoText = autoButton.GetComponentInChildren<Text>();
            autoText.fontStyle = FontStyle.Bold;
            autoPotButton = UIB.Button(root, "Auto Potion", BR, new Vector2(-610, 230), new Vector2(170, 50), Skin.Slate, () =>
            {
                var d = Player.Instance.Data;
                d.autoPotion = !d.autoPotion;
                Player.Instance.Commit();
            }, null, 21, C);
            pkButton = UIB.Button(root, "Peace", BR, new Vector2(-610, 290), new Vector2(170, 50), Skin.Slate, () =>
            {
                var p = Player.Instance;
                p.PkMode = !p.PkMode;
                Message(p.PkMode ? "PK mode ON: you can attack players outside town. Killing one turns your name red!" : "Peace mode", 3f);
            }, null, 22, C);
            rideButton = UIB.Button(root, "Ride", BR, new Vector2(-610, 350), new Vector2(170, 50), Skin.Brown, () => Player.Instance.ToggleRide(), null, 22, C);

            var bag = Skin.Icon(root, "BAG", "", Skin.Brown, BR, new Vector2(-200, 620), 96, () => inventory.Toggle());
            bagBadge = bag.badge;
            var menu = Skin.Icon(root, "MENU", "", Skin.Red, BR, new Vector2(-76, 620), 96, () => menuGrid.SetActive(!menuGrid.activeSelf));
            menu.button.GetComponentInChildren<Text>().fontSize = 24;
            menuBadge = menu.badge;
        }

        // ---- bottom: experience bar across the whole screen ----

        void BuildExpBar(Transform root)
        {
            expFill = UIB.Bar(root, BC, new Vector2(0, 2), new Vector2(1900, 22), new Color(0.95f, 0.78f, 0.25f), BC);
            var bar = (RectTransform)expFill.parent;
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.offsetMin = new Vector2(10, 2);
            bar.offsetMax = new Vector2(-10, 24);
            expText = UIB.Label(bar, "", 18, C, Vector2.zero, new Vector2(900, 24), TextAnchor.MiddleCenter);
            expText.rectTransform.pivot = C;
        }

        // ---- top right: activity icons ----

        void BuildActivities(Transform root)
        {
            var row = UIB.Stretch(UIB.Rect("Activities", root, Vector2.zero, Vector2.zero, Vector2.zero));
            activityRow = row.gameObject;
            float x0 = -330, step = 108, size = 86;
            var top = new (string glyph, string cap, Color col, string open)[]
            {
                ("EV", "Events", new Color(0.7f, 0.5f, 0.15f), "Events"),
                ("DG", "Dungeon", Skin.Purple, "Dungeon"),
                ("SH", "Shop", Skin.Green, "Shop"),
                ("RK", "Ranking", Skin.Blue, "Ranking"),
                ("AC", "Achieve", Skin.Brown, "Achievements"),
            };
            for (int i = 0; i < top.Length; i++)
            {
                var e = top[i];
                var ic = Skin.Icon(row, e.glyph, e.cap, e.col, TR, new Vector2(x0 - i * step, -64), size, () => Open(e.open));
                if (e.open == "Events") eventsBadge = ic.badge;
                if (e.open == "Achievements") achieveBadge = ic.badge;
                if (e.open == "Dungeon") dungeonGlyph = ic.button.GetComponentInChildren<Text>();
            }
            var second = new (string glyph, string cap, Color col, string open)[]
            {
                ("MP", "Teleport", Skin.Blue, "Map"),
                ("SO", "Social", Skin.Green, "Social"),
                ("WG", "Wings", Skin.Purple, "Wings"),
                ("MT", "Mount", Skin.Brown, "Mount"),
            };
            for (int i = 0; i < second.Length; i++)
            {
                var e = second[i];
                Skin.Icon(row, e.glyph, e.cap, e.col, TR, new Vector2(x0 - (i + 1) * step, -180), size, () => Open(e.open));
            }
            var fold = UIB.Button(root, "<", TR, new Vector2(x0, -180), new Vector2(64, 64), Skin.Slate, () =>
            {
                activityRow.SetActive(!activityRow.activeSelf);
                foldText.text = activityRow.activeSelf ? ">" : "<";
            }, UIB.Circle, 30, C);
            foldText = fold.GetComponentInChildren<Text>();
            foldText.text = ">";
        }

        // ---- the expanding main menu (every window) ----

        void BuildMenuGrid(Transform root)
        {
            var entries = new (string glyph, string cap, Color col, string open)[]
            {
                ("CH", "Character", Skin.Red, "Character"), ("SK", "Skills", Skin.Blue, "Skills"), ("BG", "Bag", Skin.Brown, "Bag"),
                ("WG", "Wings", Skin.Purple, "Wings"), ("PT", "Pet", Skin.Green, "Pet"), ("MT", "Mount", Skin.Brown, "Mount"),
                ("TL", "Talent", Skin.Purple, "Talent"), ("TI", "Title", new Color(0.7f, 0.5f, 0.15f), "Title"), ("GM", "Gems", Skin.Blue, "Gems"),
                ("CM", "Chaos", Skin.Purple, "Chaos"), ("GD", "Guild", Skin.Green, "Guild"), ("SO", "Social", Skin.Green, "Social"),
                ("MP", "Teleport", Skin.Blue, "Map"), ("SH", "Shop", Skin.Green, "Shop"), ("DG", "Dungeon", Skin.Purple, "Dungeon"),
                ("EV", "Events", new Color(0.7f, 0.5f, 0.15f), "Events"), ("RK", "Ranking", Skin.Blue, "Ranking"), ("AC", "Achieve", Skin.Brown, "Achievements"),
            };
            const int cols = 6;
            const float cell = 124;
            int rows = (entries.Length + cols - 1) / cols;
            var size = new Vector2(cols * cell + 40, rows * (cell + 14) + 50);
            var grid = Skin.Frame(root, "Menu", BR, new Vector2(-20, 96), size, new Vector2(1, 0));
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                var pos = new Vector2(20 + cell * (i % cols) + cell / 2, -30 - (cell + 14) * (i / cols) - cell / 2 + 8);
                var ic = Skin.Icon(grid, e.glyph, e.cap, e.col, TL, pos, 88, () => Open(e.open));
                if (e.open == "Character") menuCharBadge = ic.badge;
                if (e.open == "Talent") talentBadge = ic.badge;
            }
            menuGrid = grid.gameObject;
            menuGrid.SetActive(false);
        }

        GameObject menuCharBadge;

        // ---- class select ----

        void BuildClassSelect(Transform root)
        {
            var panel = UIB.Stretch(UIB.Rect("Class Select", root, Vector2.zero, Vector2.zero, Vector2.zero));
            UIB.Image(panel, new Color(0.03f, 0.03f, 0.05f, 0.97f));
            var glow = UIB.Rect("Glow", panel, C, new Vector2(0, 60), new Vector2(2200, 1400), C);
            UIB.Image(glow, new Color(0.6f, 0.35f, 0.15f, 0.25f), Skin.Soft).raycastTarget = false;
            var head = UIB.Rect("Header", panel, TC, new Vector2(0, -50), new Vector2(900, 96), new Vector2(0.5f, 1));
            UIB.Image(head, Color.white, Skin.Banner).raycastTarget = false;
            var title = Skin.Title(head, "Choose your class", 52, C, Vector2.zero, new Vector2(900, 96));
            title.rectTransform.pivot = C;
            for (int i = 0; i < Classes.All.Length; i++)
            {
                var c = Classes.All[i];
                var card = Skin.Frame(panel, c.Name, C, new Vector2((i - 1) * 500, -60), new Vector2(460, 700), C);
                card.gameObject.AddComponent<Button>().onClick.AddListener(() => Player.Instance.ChooseClass(c.Id));
                var halo = UIB.Rect("Halo", card, TC, new Vector2(0, -40), new Vector2(300, 300), new Vector2(0.5f, 1));
                UIB.Image(halo, new Color(c.BodyColor.r, c.BodyColor.g, c.BodyColor.b, 0.5f), Skin.Soft).raycastTarget = false;
                var orb = UIB.Rect("Orb", card, TC, new Vector2(0, -80), new Vector2(220, 220), new Vector2(0.5f, 1));
                UIB.Image(orb, c.BodyColor, Skin.Orb).raycastTarget = false;
                Skin.Child(orb, "Ring", Skin.Ring, Color.white);
                var g = UIB.Label(orb, Initials(c.Name), 72, C, Vector2.zero, new Vector2(220, 100), TextAnchor.MiddleCenter);
                g.rectTransform.pivot = C;
                g.fontStyle = FontStyle.Bold;
                var n = Skin.Title(card, c.Name, 40, TC, new Vector2(0, -320), new Vector2(420, 56));
                n.rectTransform.pivot = new Vector2(0.5f, 1);
                var desc = UIB.Label(card, c.Description, 25, TC, new Vector2(0, -385), new Vector2(400, 160), TextAnchor.UpperCenter);
                desc.rectTransform.pivot = new Vector2(0.5f, 1);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                desc.color = Skin.Cream;
                var st = UIB.Label(card, "STR " + c.BaseStr + "   AGI " + c.BaseAgi + "\nVIT " + c.BaseVit + "   ENE " + c.BaseEne, 24, TC, new Vector2(0, -540), new Vector2(400, 70), TextAnchor.MiddleCenter);
                st.rectTransform.pivot = new Vector2(0.5f, 1);
                st.color = Skin.Muted;
                UIB.Button(card, "Choose", BC, new Vector2(0, 30), new Vector2(260, 70), Skin.Red, () => Player.Instance.ChooseClass(c.Id), null, 30, new Vector2(0.5f, 0));
            }
            classSelect = panel.gameObject;
        }

        static string Initials(string name)
        {
            var parts = name.Split(' ');
            if (parts.Length == 1) return name.Substring(0, Mathf.Min(3, name.Length)).ToUpper();
            string s = "";
            foreach (var p in parts) if (p.Length > 0) s += char.ToUpper(p[0]);
            return s;
        }

        void BuildCharacter(Transform root)
        {
            character = new MenuPanel(root, "Character", new Vector2(1320, 940), 400, 5, () =>
            {
                var p = Player.Instance;
                var d = p.Data;
                var c = p.Class;
                string main = c.MainStat;
                return c.Name + "    " + (d.rebirth > 0 ? d.rebirth + " Rebirth  " : "") + "Lv " + d.level + "/" + GameData.MaxLevel + "    CP " + p.CombatPower + "\n" +
                       "Free points: " + d.freePoints + "   (main stat: " + main + ")\n\n" +
                       "STR " + d.str + "      AGI " + d.agi + "      VIT " + d.vit + "      ENE " + d.ene + "\n" +
                       "ATK " + p.Atk + "    DEF " + p.Def + "    HP " + p.MaxHp + "    Attack every " + p.AttackInterval.ToString("0.00") + "s" +
                       "    Speed " + p.MoveSpeed.ToString("0.0") + "\n\n" +
                       "Skills: " + SkillLine(p, 0) + "   " + SkillLine(p, 1) + "   " + SkillLine(p, 2) + "\n\n" +
                       "Rebirth: " + (p.RebirthBlocker() ?? "Ready! Costs " + GameData.RebirthGold(d.rebirth) + "g") + "\n" +
                       "Each rebirth: back to Lv 1, keep stats, +" + GameData.RebirthBonusPoints + " points, next gear Tier.";
            });
            foreach (var stat in new[] { "STR", "AGI", "VIT", "ENE" })
                character.AddButton(() => stat + " +1", () => { Player.Instance.AddStat(stat, 1); return null; }, new Color(0.25f, 0.35f, 0.55f));
            character.AddButton(() => "Auto Add", () => { Player.Instance.AutoAddStats(); return "Points spent"; }, new Color(0.2f, 0.45f, 0.3f));
            foreach (var stat in new[] { "STR", "AGI", "VIT", "ENE" })
                character.AddButton(() => stat + " +10", () => { Player.Instance.AddStat(stat, 10); return null; }, new Color(0.25f, 0.35f, 0.55f));
            character.AddButton(() => "Rebirth", () => Player.Instance.Rebirth(), new Color(0.6f, 0.45f, 0.1f));
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                character.AddButton(() =>
                {
                    var p = Player.Instance;
                    int lv = p.Data.skillLevels[idx];
                    return p.Class.Skills[idx].Name.Replace("\n", " ") + " Lv" + lv + (lv < Classes.MaxSkillLevel ? "\n" + Classes.SkillUpgradeGold(lv) + "g" : "\nMAX");
                }, () => Player.Instance.UpgradeSkill(idx), new Color(0.35f, 0.25f, 0.55f), 22);
            }
        }

        static string SkillLine(Player p, int i) => p.Class.Skills[i].Name.Replace("\n", " ") + " Lv" + p.Data.skillLevels[i];

        void BuildWings(Transform root)
        {
            wingsPanel = new MenuPanel(root, "Wings", new Vector2(900, 720), 400, 2, () =>
            {
                var p = Player.Instance;
                var d = p.Data;
                string now = d.wingTier == 0 ? "No wings yet"
                    : p.Class.Wings[d.wingTier - 1] + " (Tier " + d.wingTier + ")  +" + d.wingLevel + "\n" +
                      "ATK +" + p.WingPower * 5 + "   DEF +" + p.WingPower * 3 + "   HP +" + p.WingPower * 40 +
                      "   Speed +" + Upgrades.WingSpeed(d.wingTier, d.wingLevel).ToString("0.00");
                var step = Upgrades.NextWingStep(d);
                Upgrades.WingCost(d, out int f, out int fl, out int g);
                string next = step == Upgrades.WingStep.Max ? "Max tier reached" :
                    (step == Upgrades.WingStep.Craft ? "Craft " + p.Class.Wings[0] : step == Upgrades.WingStep.Tier ? "Tier up to " + p.Class.Wings[d.wingTier] : "Level up to +" + (d.wingLevel + 1)) +
                    ":\n" + f + " Condor Feather" + (fl > 0 ? " + " + fl + " Condor Flame" : "") + " + " + g + "g";
                return now + "\n\n" + next + "\n\nYou have " + d.feathers + " Feathers, " + d.flames + " Flames, " + d.gold + "g\n" +
                       "Feathers drop from Lv5+ monsters, Flames from Lv20+ monsters,\nBlood Castle and World Bosses.";
            });
            wingsPanel.AddButton(() =>
            {
                var step = Upgrades.NextWingStep(Player.Instance.Data);
                return step == Upgrades.WingStep.Craft ? "Craft" : step == Upgrades.WingStep.Tier ? "Tier Up" : step == Upgrades.WingStep.Max ? "Max" : "Upgrade";
            }, () => Player.Instance.UpgradeWings(), new Color(0.2f, 0.45f, 0.25f));
        }

        void BuildPet(Transform root)
        {
            petPanel = new MenuPanel(root, "Pet", new Vector2(820, 640), 330, 2, () =>
            {
                var d = Player.Instance.Data;
                int lv = d.petLevel;
                if (lv == 0) return "No pet yet\n\nAdopt one for " + Upgrades.PetAdoptCost + "g.\nIt follows you and attacks your target.\n\nYou have " + d.gold + "g";
                string cost = lv >= Upgrades.MaxPet ? "Max level" :
                    "Next: " + Upgrades.PetFood(lv) + " Pet Food + " + Upgrades.PetGold(lv) + "g\nYou have " + d.petFood + " Pet Food, " + d.gold + "g";
                return "Pet Lv " + lv + "\nDamage about " + Pet.Instance.Damage + " every 1.5s\n\n" + cost + "\n\nPet Food drops from monsters.";
            });
            petPanel.AddButton(() => Player.Instance.Data.petLevel == 0 ? "Adopt" : "Feed",
                () => Player.Instance.UpgradePet() ? "Success!" : "Not enough materials", new Color(0.2f, 0.45f, 0.25f));
        }

        void BuildDungeons(Transform root)
        {
            dungeonPanel = new MenuPanel(root, "Daily Dungeons", new Vector2(1100, 700), 380, 2, () =>
            {
                var p = Player.Instance;
                return "Blood Castle   (" + p.BloodCastleLeft + "/" + Upgrades.BloodCastleRuns + " left today)\n" +
                       "5 minutes: clear the bridge, break the Castle Gate, destroy the Statue of Saint,\n" +
                       "then bring the Archangel's Weapon back to the Archangel. Lots of EXP and Jewels.\n\n" +
                       "Devil Square   (" + p.DevilSquareLeft + "/" + Upgrades.DevilSquareRuns + " left today)\n" +
                       "Survive " + DevilSquare.Waves + " waves for 2:30. The more damage you deal, the more EXP and Zen.\n\n" +
                       "Monsters match your level.";
            });
            dungeonPanel.AddButton(() => "Blood Castle", () =>
            {
                string err = BloodCastle.Enter();
                if (err == null) dungeonPanel.Hide();
                return err;
            }, new Color(0.55f, 0.15f, 0.15f));
            dungeonPanel.AddButton(() => "Devil Square", () =>
            {
                string err = DevilSquare.Enter();
                if (err == null) dungeonPanel.Hide();
                return err;
            }, new Color(0.4f, 0.15f, 0.45f));
        }

        void Update()
        {
            var p = Player.Instance;
            if (p == null) return;
            var d = p.Data;
            classSelect.SetActive(!p.HasClass);
            var cls = p.Class;

            // portrait block
            int need = GameData.ExpToNext(d.level, d.rebirth);
            bool max = d.level >= GameData.MaxLevel;
            string name = string.IsNullOrEmpty(d.charName) ? "Hero" : d.charName;
            nameText.text = name + "   <color=#c9b48a>" + cls.Name + (d.rebirth > 0 ? "  ·  " + d.rebirth + " Rebirth" : "") + "</color>";
            cpText.text = "CP  " + p.CombatPower.ToString("N0");
            levelBadge.text = d.level.ToString();
            portraitGlyph.text = Initials(cls.Name);
            portraitOrb.color = cls.BodyColor;
            portraitGlow.color = new Color(cls.BodyColor.r, cls.BodyColor.g, cls.BodyColor.b, 0.35f);
            UIB.SetBar(hpFill, (float)p.Hp / p.MaxHp);
            hpText.text = p.Hp + " / " + p.MaxHp;
            charBadge.SetActive(d.freePoints > 0);
            string state = "";
            if (p.BuffActive) state += "<color=#ffd46b>Greater Damage " + Mathf.CeilToInt(p.BuffLeft) + "s</color>   ";
            if (p.InTown) state += "<color=#9dff8a>Safe zone</color>   ";
            if (p.Riding) state += "<color=#e8c27a>Riding</color>   ";
            if (d.freePoints > 0) state += "<color=#ff8a6b>+" + d.freePoints + " stat points</color>   ";
            if (p.IsRed) state += "<color=#ff5544>Red name (PK " + d.pkPoints + ") " + Mathf.CeilToInt(d.pkClock / 60f) + " min</color>";
            stateText.text = state;

            // currencies
            zenText.text = d.gold.ToString("N0");
            diamondText.text = d.diamonds.ToString("N0");
            blessText.text = "Bless " + d.jewelBless;
            soulText.text = "Soul " + d.jewelSoul;

            // CP change toast
            int cp = p.CombatPower;
            if (lastCp >= 0 && cp != lastCp && p.HasClass)
            {
                int diff = cp - lastCp;
                cpToast.text = "CP " + cp.ToString("N0") + (diff > 0 ? "  <color=#7dff6b>+" + diff + "</color>" : "  <color=#ff6b5b>" + diff + "</color>");
                cpToastUntil = Time.time + 2.2f;
            }
            lastCp = cp;
            float left = cpToastUntil - Time.time;
            cpToast.gameObject.SetActive(left > 0);
            if (left > 0)
            {
                cpToast.color = new Color(1f, 0.78f, 0.3f, Mathf.Clamp01(left * 2f));
                cpToast.rectTransform.anchoredPosition = new Vector2(0, -160 + (2.2f - left) * 30f);
            }

            // target
            var tgt = p.Target;
            var pvp = p.PvpTarget;
            if (tgt != null && !tgt.IsDead)
            {
                targetFrame.SetActive(true);
                targetName.text = (tgt.IsBoss ? "<color=#e59bff>BOSS</color>  " : "") + "Lv " + tgt.Level + "  " + tgt.DisplayName;
                UIB.SetBar(targetFill, (float)tgt.Hp / tgt.MaxHp);
                targetHp.text = tgt.Hp.ToString("N0") + " / " + tgt.MaxHp.ToString("N0");
            }
            else if (pvp != null && !pvp.IsDead)
            {
                targetFrame.SetActive(true);
                targetName.text = "<color=#ff8a6b>PK</color>  " + pvp.Name;
                UIB.SetBar(targetFill, pvp.HpPercent / 100f);
                targetHp.text = pvp.HpPercent + "%";
            }
            else targetFrame.SetActive(false);

            // tracker
            questTabImg.color = trackerTab == 0 ? new Color(0.75f, 0.45f, 0.18f) : new Color(0.3f, 0.28f, 0.27f);
            teamTabImg.color = trackerTab == 1 ? new Color(0.75f, 0.45f, 0.18f) : new Color(0.3f, 0.28f, 0.27f);
            if (trackerTab == 0)
            {
                string q = Quests.Describe(p);
                questTitle.text = q.StartsWith("Hunt") ? "[Daily Hunt]" : "[Main Quest]";
                questBody.text = q;
                questHint.text = p.QuestZone != null ? "Auto quest running..." : "Tap to auto quest";
            }
            else
            {
                var s = Social.Instance;
                questTitle.text = "Team";
                questBody.text = s != null ? s.PartyText() : "Log in online to make a party.";
                questHint.text = "Tap to open Social";
            }
            bossText.text = WorldBoss.Instance != null ? WorldBoss.Instance.Status : "";

            // combat cluster
            for (int i = 0; i < 3; i++)
            {
                var sk = cls.Skills[i];
                float r = p.SkillCooldownRatio(i);
                cooldowns[i].fillAmount = r;
                skillOrbs[i].color = Color.Lerp(sk.Color, Color.black, 0.35f);
                skillLabels[i].text = r > 0 ? Mathf.CeilToInt(r * sk.Cooldown).ToString() : sk.Name;
                skillLabels[i].fontSize = r > 0 ? 44 : 22;
            }
            potionText.text = "Potion\n" + d.potions;
            Tint(autoPotButton, d.autoPotion, Skin.Green);
            Tint(autoButton, p.AutoBattle, new Color(0.85f, 0.6f, 0.15f));
            autoText.text = p.AutoBattle ? "AUTO\n<size=18>ON</size>" : "AUTO";
            Tint(pkButton, p.PkMode, new Color(0.85f, 0.2f, 0.12f));
            pkButton.GetComponentInChildren<Text>().text = p.PkMode ? "PK Mode" : "Peace";
            rideButton.GetComponentInChildren<Text>().text = p.Riding ? "Dismount" : "Ride";
            bagBadge.SetActive(d.bag.Count >= 40);

            // activities and menu badges
            bool events = Activities.CanSignIn(d);
            bool ach = Activities.ReadyCount(d) > 0;
            bool talents = Growth.TalentPoints(d) - Growth.TalentSpent(d) > 0;
            eventsBadge.SetActive(events);
            achieveBadge.SetActive(ach);
            menuCharBadge.SetActive(d.freePoints > 0);
            talentBadge.SetActive(talents);
            menuBadge.SetActive(d.freePoints > 0 || talents);

            var run = DungeonRun.Active;
            dungeonGlyph.text = run != null ? "OUT" : "DG";
            dungeonText.text = run != null ? run.Status : "";

            // exp
            float ratio = max ? 1f : (float)d.exp / need;
            UIB.SetBar(expFill, ratio);
            expText.text = "Lv " + d.level + "    EXP " + (max ? "MAX" : (ratio * 100f).ToString("0.00") + "%");

            centerText.text = p.IsDead ? "Defeated. Respawn in " + Mathf.CeilToInt(p.RespawnIn)
                : Time.time < messageUntil ? message : "";
            UpdateMinimap(p);
            UpdateExtra(p);
        }

        static void Tint(Button b, bool on, Color onColor)
        {
            b.image.color = on ? onColor : Skin.Slate;
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

    // Centred MU-style window: gold-trimmed frame, title plate, red X, a description on top,
    // a grid of buttons under it and a result line at the bottom.
    public class MenuPanel
    {
        readonly RectTransform panel;
        readonly GameObject root;
        readonly Text info, result;
        readonly System.Func<string> describe;
        readonly List<(Text text, System.Func<string> label)> buttons = new List<(Text, System.Func<string>)>();
        readonly float infoHeight, width;
        readonly int columns;

        public MenuPanel(Transform canvas, string title, Vector2 size, float infoHeight, int columns, System.Func<string> describe)
        {
            this.describe = describe;
            this.infoHeight = infoHeight;
            this.columns = columns;
            width = size.x;
            var tl = new Vector2(0, 1);
            (root, panel) = Skin.Window(canvas, title, title, size, Hide);
            var box = UIB.Rect("Info", panel, tl, new Vector2(24, -86), new Vector2(size.x - 48, infoHeight + 18));
            Skin.Sliced(box, Skin.Inset, Color.white).raycastTarget = false;
            info = UIB.Label(panel, "", 26, tl, new Vector2(40, -96), new Vector2(size.x - 80, infoHeight), TextAnchor.UpperLeft);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.color = Skin.Cream;
            result = UIB.Label(panel, "", 27, new Vector2(0, 0), new Vector2(40, 24), new Vector2(size.x - 80, 80), TextAnchor.MiddleCenter);
            result.horizontalOverflow = HorizontalWrapMode.Wrap;
            result.color = Skin.Gold;
            root.SetActive(false);
            if (Player.Instance != null) Player.Instance.Changed += () => { if (root.activeSelf) Refresh(); };
        }

        public void AddButton(System.Func<string> label, System.Func<string> action, Color color, int fontSize = 26)
        {
            int i = buttons.Count;
            const float gap = 14, h = 84;
            float w = (width - 60 - gap * (columns - 1)) / columns;
            var pos = new Vector2(30 + (i % columns) * (w + gap), -(122 + infoHeight) - (i / columns) * (h + gap));
            var b = UIB.Button(panel, "", new Vector2(0, 1), pos, new Vector2(w, h), color, () =>
            {
                string r = action();
                if (r != null) result.text = r;
                Refresh();
            }, null, fontSize);
            buttons.Add((b.GetComponentInChildren<Text>(), label));
        }

        public bool IsOpen => root.activeSelf;

        public void Show()
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            result.text = "";
            Refresh();
        }

        public void Hide() => root.SetActive(false);

        public void Toggle()
        {
            if (IsOpen) Hide(); else Show();
        }

        void Refresh()
        {
            info.text = describe();
            foreach (var (text, label) in buttons) text.text = label();
        }
    }

    public class InventoryUI
    {
        readonly RectTransform panel, equipRoot, bagRoot;
        readonly GameObject root;
        readonly Text info, bagTitle;

        public InventoryUI(Transform canvas)
        {
            var tl = new Vector2(0, 1);
            (root, panel) = Skin.Window(canvas, "Inventory", "Inventory", new Vector2(1560, 960), () => root.SetActive(false));
            Skin.Title(panel, "Equipment", 30, tl, new Vector2(34, -86), new Vector2(640, 40), TextAnchor.MiddleLeft);
            bagTitle = Skin.Title(panel, "Bag", 30, tl, new Vector2(740, -86), new Vector2(780, 40), TextAnchor.MiddleLeft);
            Skin.Sliced(UIB.Rect("EquipBox", panel, tl, new Vector2(24, -130), new Vector2(690, 676)), Skin.Inset, Color.white).raycastTarget = false;
            Skin.Sliced(UIB.Rect("BagBox", panel, tl, new Vector2(728, -130), new Vector2(808, 676)), Skin.Inset, Color.white).raycastTarget = false;
            equipRoot = UIB.Rect("Equipped", panel, tl, new Vector2(36, -142), new Vector2(666, 650));
            bagRoot = UIB.Rect("Bag", panel, tl, new Vector2(742, -142), new Vector2(780, 650));
            info = UIB.Label(panel, "", 23, tl, new Vector2(34, -814), new Vector2(1490, 34), TextAnchor.MiddleLeft);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            info.color = Skin.Gold;

            var bl = new Vector2(0, 0);
            UIB.Button(panel, "Sell Junk", bl, new Vector2(30, 30), new Vector2(220, 80), new Color(0.6f, 0.45f, 0.15f),
                () => info.text = "Sold " + Player.Instance.SellCommons() + " items (kept Excellent/Ancient gear you can wear)", null, 26);
            UIB.Button(panel, "Gems", bl, new Vector2(270, 30), new Vector2(200, 80), new Color(0.2f, 0.45f, 0.6f),
                () => HUD.Instance.Open("Gems"), null, 26);
            UIB.Button(panel, "Chaos Machine", bl, new Vector2(490, 30), new Vector2(220, 80), new Color(0.5f, 0.2f, 0.5f),
                () => HUD.Instance.Open("Chaos"), null, 24);
            UIB.Button(panel, "Reset Save", bl, new Vector2(730, 30), new Vector2(220, 80), new Color(0.45f, 0.15f, 0.15f),
                () => { Player.Instance.ResetSave(); info.text = "Save reset"; }, null, 26);

            Player.Instance.Changed += () => { if (root.activeSelf) Refresh(); };
            root.SetActive(false);
        }

        public void Show()
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            info.text = "Bless: +1..+6 always works.  Soul: +7 and up, can fail and drop a level.  The set glows from +7.";
            Refresh();
        }

        public void Toggle()
        {
            if (root.activeSelf) root.SetActive(false);
            else Show();
        }

        void Refresh()
        {
            bagTitle.text = "Bag  <size=22><color=#c9b48a>" + Player.Instance.Data.bag.Count + " items · tap to equip</color></size>";
            Clear(equipRoot);
            Clear(bagRoot);
            var p = Player.Instance;
            var tl = new Vector2(0, 1);

            for (int s = 0; s < GameData.EquipSlots; s++)
            {
                int index = s;
                var slot = GameData.SlotAt(s);
                var it = p.Data.equipped[s];
                var row = UIB.Rect("Slot", equipRoot, tl, new Vector2(0, -s * 72), new Vector2(666, 66));
                Skin.Sliced(row, Skin.Inset, it.IsEmpty ? new Color(1, 1, 1, 0.5f) : Color.white);
                string desc = it.IsEmpty ? slot + ": empty" : it.Label + "   " + it.StatName + " " + it.Stat + "   T" + it.tier + "\n<size=17>" + Extras(it) + "</size>";
                var label = UIB.Label(row, desc, 21, tl, new Vector2(12, -4), new Vector2(470, 60), TextAnchor.UpperLeft);
                if (!it.IsEmpty) label.color = GameData.RarityColor(it.rarity);
                if (it.CanEnhance)
                {
                    string jewel = it.UsesSoul ? "Soul " + Mathf.RoundToInt(it.EnhanceChance * 100) + "%" : "Bless";
                    UIB.Button(row, "+" + (it.plus + 1) + " " + jewel + "\n" + it.EnhanceGold + "g",
                        new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(170, 60), it.UsesSoul ? new Color(0.55f, 0.2f, 0.4f) : new Color(0.35f, 0.25f, 0.55f),
                        () => info.text = p.Enhance(index), null, 19);
                }
            }

            const int cols = 6;
            const float cell = 116, gap = 8;
            for (int i = 0; i < p.Data.bag.Count; i++)
            {
                int idx = i;
                var it = p.Data.bag[i];
                var current = p.Data.equipped[(int)it.slot];
                if (it.slot == Slot.Ring && !p.Data.equipped[GameData.Ring2Index].IsEmpty && p.Data.equipped[GameData.Ring2Index].Stat < current.Stat)
                    current = p.Data.equipped[GameData.Ring2Index];
                bool usable = p.CanEquip(it, out string why);
                bool better = usable && (current.IsEmpty || it.Stat > current.Stat);
                string text = (it.plus > 0 ? "+" + it.plus + " " : "") + it.name.Replace(" ", "\n") + "\nT" + it.tier + " " + it.StatName + " " + it.Stat;
                var b = UIB.Button(bagRoot, text, tl, new Vector2((i % cols) * (cell + gap), -(i / cols) * (cell + gap)), new Vector2(cell, cell),
                    better ? new Color(0.12f, 0.3f, 0.14f) : usable ? new Color(0.14f, 0.13f, 0.15f) : new Color(0.3f, 0.09f, 0.08f),
                    () => info.text = p.Equip(idx), null, 15);
                b.GetComponentInChildren<Text>().color = GameData.RarityColor(it.rarity);
                var edge = b.transform.Find("Edge");
                if (edge != null) edge.GetComponent<Image>().color = Color.Lerp(GameData.RarityColor(it.rarity), Color.white, 0.15f);
                if (better)
                {
                    var up = UIB.Label(b.transform, "UP", 16, new Vector2(1, 1), new Vector2(-6, -4), new Vector2(40, 20), TextAnchor.UpperRight);
                    up.rectTransform.pivot = new Vector2(1, 1);
                    up.color = new Color(0.5f, 1f, 0.45f);
                }
            }
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
}
