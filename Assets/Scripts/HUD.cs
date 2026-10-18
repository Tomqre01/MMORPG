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

        Text levelText, hpText, goldText, centerText, dungeonText, dungeonButtonText, questText, bossText, stateText, potionText;
        RectTransform hpFill, expFill;
        readonly Image[] cooldowns = new Image[3];
        readonly Text[] skillLabels = new Text[3];
        Button autoButton, pkButton, autoPotButton;
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
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        // Opens a window by name: Bag, Character, Wings, Pet, Dungeon.
        public void Open(string name)
        {
            switch (name)
            {
                case "Bag": inventory.Show(); break;
                case "Character": character.Show(); break;
                case "Wings": wingsPanel.Show(); break;
                case "Pet": petPanel.Show(); break;
                case "Dungeon": dungeonPanel.Show(); break;
                default: OpenExtra(name); break;
            }
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
            var panel = UIB.Rect("Status", root, tl, new Vector2(20, -20), new Vector2(560, 210));
            UIB.Image(panel, new Color(0, 0, 0, 0.45f)).raycastTarget = false;
            levelText = UIB.Label(panel, "", 32, tl, new Vector2(20, -10), new Vector2(520, 44));
            hpFill = UIB.Bar(panel, tl, new Vector2(20, -60), new Vector2(520, 34), new Color(0.85f, 0.15f, 0.15f));
            hpText = UIB.Label(hpFill.parent, "", 24, center, Vector2.zero, new Vector2(520, 34), TextAnchor.MiddleCenter);
            expFill = UIB.Bar(panel, tl, new Vector2(20, -104), new Vector2(520, 16), new Color(0.95f, 0.8f, 0.2f));
            goldText = UIB.Label(panel, "", 24, tl, new Vector2(20, -126), new Vector2(520, 80), TextAnchor.UpperLeft);

            // Quest tracker: tap to let the game run the quest.
            var questButton = UIB.Button(root, "", tl, new Vector2(20, -240), new Vector2(560, 96), new Color(0.15f, 0.12f, 0.05f, 0.75f), () =>
            {
                string m = Quests.AutoRun(Player.Instance);
                if (m != null) Message(m, 2.5f);
            });
            questText = questButton.GetComponentInChildren<Text>();
            questText.fontSize = 24;
            questText.alignment = TextAnchor.MiddleLeft;
            questText.rectTransform.offsetMin = new Vector2(16, 0);
            bossText = UIB.Label(root, "", 24, tl, new Vector2(24, -342), new Vector2(560, 34));
            bossText.color = new Color(0.9f, 0.65f, 1f);
            stateText = UIB.Label(root, "", 24, tl, new Vector2(24, -378), new Vector2(560, 70), TextAnchor.UpperLeft);

            centerText = UIB.Label(root, "", 44, new Vector2(0.5f, 1), new Vector2(0, -330), new Vector2(1300, 140), TextAnchor.MiddleCenter);

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
                var b = UIB.Button(root, "Skill", br, skillPos[i], new Vector2(150, 150), new Color(0.25f, 0.35f, 0.7f, 0.85f),
                    () => Player.Instance.UseSkill(idx), UIB.Circle, 22, center);
                skillLabels[i] = b.GetComponentInChildren<Text>();
                var cd = UIB.Image(UIB.Stretch(UIB.Rect("Cooldown", b.transform, Vector2.zero, Vector2.zero, Vector2.zero)), new Color(0, 0, 0, 0.6f), UIB.Circle);
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Radial360;
                cd.fillOrigin = (int)Image.Origin360.Top;
                cd.fillClockwise = false;
                cd.raycastTarget = false;
                cooldowns[i] = cd;
            }
            var potion = UIB.Button(root, "Potion", br, new Vector2(-640, 130), new Vector2(130, 130), new Color(0.65f, 0.15f, 0.2f, 0.85f),
                () => { if (!Player.Instance.UsePotion() && Player.Instance.Data.potions <= 0) Message("No potions. Buy them in the Bag", 2f); }, UIB.Circle, 22, center);
            potionText = potion.GetComponentInChildren<Text>();
            autoPotButton = UIB.Button(root, "Auto Pot", br, new Vector2(-640, 255), new Vector2(150, 60), Color.gray, () =>
            {
                var d = Player.Instance.Data;
                d.autoPotion = !d.autoPotion;
                Player.Instance.Commit();
            }, null, 22, center);
            autoButton = UIB.Button(root, "AUTO", br, new Vector2(-560, 470), new Vector2(170, 90), Color.gray,
                () => { var p = Player.Instance; p.AutoBattle = !p.AutoBattle; if (!p.AutoBattle) p.StartQuestHunt(null); }, null, 32, center);
            pkButton = UIB.Button(root, "Peace", br, new Vector2(-750, 470), new Vector2(170, 90), Color.gray, () =>
            {
                var p = Player.Instance;
                p.PkMode = !p.PkMode;
                Message(p.PkMode ? "PK mode ON: you can attack players outside town. Killing one turns your name red!" : "Peace mode", 3f);
            }, null, 28, center);

            var tr = new Vector2(1, 1);
            var menuColor = new Color(0.3f, 0.3f, 0.35f, 0.9f);
            UIB.Button(root, "Bag", tr, new Vector2(-20, -20), new Vector2(160, 90), menuColor, () => inventory.Toggle());
            UIB.Button(root, "Char", tr, new Vector2(-190, -20), new Vector2(160, 90), menuColor, () => character.Toggle());
            UIB.Button(root, "Wings", tr, new Vector2(-360, -20), new Vector2(160, 90), menuColor, () => wingsPanel.Toggle());
            UIB.Button(root, "Pet", tr, new Vector2(-530, -20), new Vector2(160, 90), menuColor, () => petPanel.Toggle());
            var dungeonButton = UIB.Button(root, "Dungeon", tr, new Vector2(-700, -20), new Vector2(190, 90), new Color(0.45f, 0.25f, 0.5f, 0.9f), () =>
            {
                if (DungeonRun.Active != null) DungeonRun.Active.Leave();
                else dungeonPanel.Toggle();
            }, null, 26);
            dungeonButtonText = dungeonButton.GetComponentInChildren<Text>();
            dungeonText = UIB.Label(root, "", 34, new Vector2(0.5f, 1), new Vector2(0, -225), new Vector2(1000, 90), TextAnchor.MiddleCenter);

            inventory = new InventoryUI(root);
            BuildCharacter(root);
            BuildWings(root);
            BuildPet(root);
            BuildDungeons(root);
            BuildExtra(root);
            BuildClassSelect(root);
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

        // Full-screen class picker, shown until the save has a class.
        void BuildClassSelect(Transform root)
        {
            var center = new Vector2(0.5f, 0.5f);
            var panel = UIB.Stretch(UIB.Rect("Class Select", root, Vector2.zero, Vector2.zero, Vector2.zero));
            UIB.Image(panel, new Color(0.05f, 0.05f, 0.1f, 0.95f));
            UIB.Label(panel, "Choose your class", 56, new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(900, 90), TextAnchor.MiddleCenter);
            for (int i = 0; i < Classes.All.Length; i++)
            {
                var c = Classes.All[i];
                string stats = "\n\nSTR " + c.BaseStr + "  AGI " + c.BaseAgi + "\nVIT " + c.BaseVit + "  ENE " + c.BaseEne;
                var b = UIB.Button(panel, c.Name + "\n\n" + c.Description + stats, center, new Vector2((i - 1) * 480, -40), new Vector2(440, 520),
                    Color.Lerp(c.BodyColor, Color.black, 0.45f), () => Player.Instance.ChooseClass(c.Id), null, 32, center);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            }
            classSelect = panel.gameObject;
        }

        void Update()
        {
            var p = Player.Instance;
            if (p == null) return;
            var d = p.Data;
            int need = GameData.ExpToNext(d.level, d.rebirth);
            bool max = d.level >= GameData.MaxLevel;
            levelText.text = (d.rebirth > 0 ? d.rebirth + "R  " : "") + "Lv " + d.level + "   CP " + p.CombatPower + (d.freePoints > 0 ? "   (+" + d.freePoints + " pts)" : "");
            UIB.SetBar(hpFill, (float)p.Hp / p.MaxHp);
            hpText.text = p.Hp + " / " + p.MaxHp;
            UIB.SetBar(expFill, max ? 1f : (float)d.exp / need);
            goldText.text = "Zen " + d.gold + "   Diamond " + d.diamonds + "   EXP " + (max ? "MAX" : d.exp + "/" + need) + "\n" +
                            "Bless " + d.jewelBless + "   Soul " + d.jewelSoul + "   Feather " + d.feathers + "   Flame " + d.flames;
            classSelect.SetActive(!p.HasClass);
            for (int i = 0; i < 3; i++)
            {
                cooldowns[i].fillAmount = p.SkillCooldownRatio(i);
                skillLabels[i].text = p.Class.Skills[i].Name;
            }
            questText.text = "QUEST  " + Quests.Describe(p) + "\n<size=20>" + (p.QuestZone != null ? "Auto quest running..." : "Tap to auto quest") + "</size>";
            bossText.text = WorldBoss.Instance != null ? WorldBoss.Instance.Status : "";
            string state = "";
            if (p.BuffActive) state += "Greater Damage " + Mathf.CeilToInt(p.BuffLeft) + "s   ";
            if (p.InTown) state += "Safe zone   ";
            if (p.IsRed) state += "<color=#ff5544>Red name (PK " + d.pkPoints + ") " + Mathf.CeilToInt(d.pkClock / 60f) + " min</color>";
            stateText.text = state;
            potionText.text = "Potion\n" + d.potions;
            autoPotButton.image.color = d.autoPotion ? new Color(0.2f, 0.6f, 0.3f, 0.95f) : new Color(0.4f, 0.4f, 0.4f, 0.85f);
            var run = DungeonRun.Active;
            dungeonButtonText.text = run != null ? "Leave" : "Dungeon";
            dungeonText.text = run != null ? run.Status : "";
            autoButton.image.color = p.AutoBattle ? new Color(0.2f, 0.7f, 0.3f, 0.95f) : new Color(0.4f, 0.4f, 0.4f, 0.85f);
            pkButton.image.color = p.PkMode ? new Color(0.85f, 0.25f, 0.15f, 0.95f) : new Color(0.4f, 0.4f, 0.4f, 0.85f);
            pkButton.GetComponentInChildren<Text>().text = p.PkMode ? "PK" : "Peace";
            centerText.text = p.IsDead ? "Defeated. Respawn in " + Mathf.CeilToInt(p.RespawnIn)
                : Time.time < messageUntil ? message : "";
            UpdateExtra(p);
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

    // Centred window: a description on top, a grid of buttons under it, a result line and Close.
    public class MenuPanel
    {
        readonly RectTransform panel;
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
            var center = new Vector2(0.5f, 0.5f);
            var tl = new Vector2(0, 1);
            panel = UIB.Rect(title, canvas, center, Vector2.zero, size);
            UIB.Image(panel, new Color(0.08f, 0.08f, 0.12f, 0.96f));
            UIB.Label(panel, title, 44, tl, new Vector2(30, -20), new Vector2(size.x - 60, 60));
            info = UIB.Label(panel, "", 27, tl, new Vector2(30, -95), new Vector2(size.x - 60, infoHeight), TextAnchor.UpperLeft);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;
            result = UIB.Label(panel, "", 28, new Vector2(0, 0), new Vector2(30, 30), new Vector2(size.x - 280, 90));
            result.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIB.Button(panel, "Close", new Vector2(1, 0), new Vector2(-30, 30), new Vector2(200, 90), new Color(0.3f, 0.3f, 0.35f), Toggle);
            panel.gameObject.SetActive(false);
            if (Player.Instance != null) Player.Instance.Changed += () => { if (panel.gameObject.activeSelf) Refresh(); };
        }

        public void AddButton(System.Func<string> label, System.Func<string> action, Color color, int fontSize = 26)
        {
            int i = buttons.Count;
            const float gap = 14, h = 84;
            float w = (width - 60 - gap * (columns - 1)) / columns;
            var pos = new Vector2(30 + (i % columns) * (w + gap), -(105 + infoHeight) - (i / columns) * (h + gap));
            var b = UIB.Button(panel, "", new Vector2(0, 1), pos, new Vector2(w, h), color, () =>
            {
                string r = action();
                if (r != null) result.text = r;
                Refresh();
            }, null, fontSize);
            buttons.Add((b.GetComponentInChildren<Text>(), label));
        }

        public bool IsOpen => panel.gameObject.activeSelf;

        public void Show()
        {
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
            result.text = "";
            Refresh();
        }

        public void Hide() => panel.gameObject.SetActive(false);

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
        readonly Text info;

        public InventoryUI(Transform canvas)
        {
            var center = new Vector2(0.5f, 0.5f);
            var tl = new Vector2(0, 1);
            panel = UIB.Rect("Inventory", canvas, center, Vector2.zero, new Vector2(1500, 900));
            UIB.Image(panel, new Color(0.08f, 0.08f, 0.12f, 0.96f));
            UIB.Label(panel, "Equipment", 36, tl, new Vector2(30, -20), new Vector2(640, 50));
            UIB.Label(panel, "Bag (tap to equip)", 36, tl, new Vector2(720, -20), new Vector2(740, 50));
            equipRoot = UIB.Rect("Equipped", panel, tl, new Vector2(30, -80), new Vector2(660, 630));
            bagRoot = UIB.Rect("Bag", panel, tl, new Vector2(720, -80), new Vector2(750, 640));
            info = UIB.Label(panel, "", 24, tl, new Vector2(30, -715), new Vector2(660, 60), TextAnchor.UpperLeft);
            info.horizontalOverflow = HorizontalWrapMode.Wrap;

            var bl = new Vector2(0, 0);
            UIB.Button(panel, "Sell Junk", bl, new Vector2(30, 30), new Vector2(220, 80), new Color(0.6f, 0.45f, 0.15f),
                () => info.text = "Sold " + Player.Instance.SellCommons() + " items (kept Excellent/Ancient gear you can wear)", null, 26);
            UIB.Button(panel, "Gems", bl, new Vector2(270, 30), new Vector2(200, 80), new Color(0.2f, 0.45f, 0.6f),
                () => HUD.Instance.Open("Gems"), null, 26);
            UIB.Button(panel, "Chaos Machine", bl, new Vector2(490, 30), new Vector2(220, 80), new Color(0.5f, 0.2f, 0.5f),
                () => HUD.Instance.Open("Chaos"), null, 24);
            UIB.Button(panel, "Reset Save", bl, new Vector2(730, 30), new Vector2(220, 80), new Color(0.45f, 0.15f, 0.15f),
                () => { Player.Instance.ResetSave(); info.text = "Save reset"; }, null, 26);
            UIB.Button(panel, "Close", new Vector2(1, 0), new Vector2(-30, 30), new Vector2(200, 80), new Color(0.3f, 0.3f, 0.35f), Toggle);

            Player.Instance.Changed += () => { if (panel.gameObject.activeSelf) Refresh(); };
            panel.gameObject.SetActive(false);
        }

        public void Show()
        {
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
            info.text = "Bless: +1..+6 always works.  Soul: +7 and up, can fail and drop a level.  The set glows from +7.";
            Refresh();
        }

        public void Toggle()
        {
            if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
            else Show();
        }

        void Refresh()
        {
            Clear(equipRoot);
            Clear(bagRoot);
            var p = Player.Instance;
            var tl = new Vector2(0, 1);

            for (int s = 0; s < GameData.EquipSlots; s++)
            {
                int index = s;
                var slot = GameData.SlotAt(s);
                var it = p.Data.equipped[s];
                var row = UIB.Rect("Slot", equipRoot, tl, new Vector2(0, -s * 70), new Vector2(660, 66));
                UIB.Image(row, new Color(1, 1, 1, 0.06f));
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
                    better ? new Color(0.15f, 0.4f, 0.2f) : usable ? new Color(0.22f, 0.22f, 0.28f) : new Color(0.35f, 0.12f, 0.12f),
                    () => info.text = p.Equip(idx), null, 15);
                b.GetComponentInChildren<Text>().color = GameData.RarityColor(it.rarity);
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
