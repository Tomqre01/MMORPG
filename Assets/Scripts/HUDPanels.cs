using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MuProto
{
    // The MU Origin windows: map teleport, merchant shop, talents, mounts, titles, events,
    // achievements, ranking, gems, Chaos Machine and the social window.
    public partial class HUD
    {
        MenuPanel mapPanel, shopPanel, talentPanel, mountPanel, titlePanel, eventsPanel, achievePanel, rankPanel, gemsPanel, chaosPanel;
        SocialPanel socialPanel;
        Text chatMini;
        int gearSlot; // equipment slot picked in the Gems / Chaos windows

        void OpenExtra(string name)
        {
            switch (name)
            {
                case "Map": mapPanel.Show(); break;
                case "Shop": shopPanel.Show(); break;
                case "Talent": talentPanel.Show(); break;
                case "Mount": mountPanel.Show(); break;
                case "Title": titlePanel.Show(); break;
                case "Events": eventsPanel.Show(); break;
                case "Achievements": achievePanel.Show(); break;
                case "Ranking": rankPanel.Show(); Activities.Instance?.RefreshRanking(() => { if (rankPanel.IsOpen) rankPanel.Show(); }); break;
                case "Gems": gemsPanel.Show(); break;
                case "Chaos": chaosPanel.Show(); break;
                case "Social": socialPanel.Show(); break;
            }
        }

        void BuildExtra(Transform root)
        {
            // Chat box above the joystick: the last few lines, tap to open the Social window.
            var chat = UIB.Rect("Chat", root, Vector2.zero, new Vector2(16, 392), new Vector2(620, 150));
            Skin.Sliced(chat, Skin.Inset, Color.white);
            chat.gameObject.AddComponent<Button>().onClick.AddListener(() => Open("Social"));
            var tag = UIB.Rect("Tag", chat, new Vector2(0, 1), new Vector2(10, -8), new Vector2(110, 34));
            Skin.Sliced(tag, Skin.Button, new Color(0.55f, 0.2f, 0.12f)).raycastTarget = false;
            var tagText = UIB.Label(tag, "World", 20, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 34), TextAnchor.MiddleCenter);
            tagText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tagText.color = Skin.Gold;
            chatMini = UIB.Label(chat, "", 21, Vector2.zero, new Vector2(14, 8), new Vector2(592, 92), TextAnchor.LowerLeft);
            chatMini.horizontalOverflow = HorizontalWrapMode.Wrap;
            chatMini.color = Skin.Cream;

            BuildMap(root);
            BuildShop(root);
            BuildTalent(root);
            BuildMount(root);
            BuildTitle(root);
            BuildEvents(root);
            BuildAchievements(root);
            BuildRanking(root);
            BuildGems(root);
            BuildChaos(root);
            socialPanel = new SocialPanel(root);
        }

        void UpdateExtra(Player p)
        {
            var s = Social.Instance;
            if (s != null)
            {
                var lines = s.Log.Skip(Mathf.Max(0, s.Log.Count - 3));
                chatMini.text = string.Join("\n", lines) + (s.InviteFrom != null ? "\n<color=#9dff8a>Party invite from " + s.InviteName + " (Social > Accept)</color>" : "");
            }
            else chatMini.text = "<color=#a89c86>Offline. Log in online to chat with other players.</color>";
        }

        // ---- Map ----

        void BuildMap(Transform root)
        {
            mapPanel = new MenuPanel(root, "Teleport", new Vector2(1100, 760), 200, 3, () =>
                "You are in " + Maps.CurrentDef.Name + ".\nEach map has a safe town in the middle. Teleporting costs Zen.\n" +
                "Monsters get stronger from Lorencia to Tarkan. Icarus opens after the first Rebirth.");
            for (int i = 0; i < Maps.All.Length; i++)
            {
                int id = i;
                mapPanel.AddButton(() =>
                {
                    var m = Maps.All[id];
                    string req = m.MinRebirth > 0 ? m.MinRebirth + " Rebirth" : "Lv " + m.MinLevel;
                    return m.Name + (Maps.Current == id ? " (here)" : "") + "\n<size=20>" + req + " · " + (m.Cost > 0 ? m.Cost + " Zen" : "free") + "</size>";
                }, () =>
                {
                    string r = Maps.Enter(Player.Instance, id, false);
                    if (r.StartsWith("Welcome")) { mapPanel.Hide(); Message(r, 2.5f); }
                    return r;
                }, new Color(0.2f, 0.35f, 0.5f));
            }
        }

        // ---- Shop ----

        void BuildShop(Transform root)
        {
            shopPanel = new MenuPanel(root, "Merchant", new Vector2(1100, 840), 120, 2, () =>
            {
                var d = Player.Instance.Data;
                return "Zen " + d.gold + "     Diamonds " + d.diamonds + "\nDiamonds come from sign-in, achievements, events and the arena.";
            });
            foreach (var item in Growth.Shop)
            {
                var it = item;
                shopPanel.AddButton(() => it.Name + "\n<size=20>" + (it.Zen > 0 ? it.Zen + " Zen" : it.Diamonds + " Diamonds") + "</size>",
                    () => Player.Instance.Buy(it), it.Zen > 0 ? new Color(0.45f, 0.38f, 0.15f) : new Color(0.25f, 0.4f, 0.6f));
            }
        }

        // ---- Talents ----

        void BuildTalent(Transform root)
        {
            talentPanel = new MenuPanel(root, "Talents", new Vector2(1100, 760), 260, 3, () =>
            {
                var d = Player.Instance.Data;
                int left = Growth.TalentPoints(d) - Growth.TalentSpent(d);
                var lines = new List<string> { "Talent points: " + left + " free  (10 per Rebirth, then 1 per 10 levels)" };
                for (int i = 0; i < Growth.TalentNames.Length; i++)
                    lines.Add(Growth.TalentNames[i] + "  " + d.talents[i] + "/" + Growth.MaxTalent + "   (" + Growth.TalentEffects[i] + " each)");
                return string.Join("\n", lines);
            });
            for (int i = 0; i < Growth.TalentNames.Length; i++)
            {
                int idx = i;
                talentPanel.AddButton(() => Growth.TalentNames[idx] + "\n<size=20>" + Player.Instance.Data.talents[idx] + "/" + Growth.MaxTalent + "</size>",
                    () => Player.Instance.AddTalent(idx), new Color(0.35f, 0.25f, 0.55f));
            }
            talentPanel.AddButton(() => "Reset (" + Growth.TalentResetZen + " Zen)", () => Player.Instance.ResetTalents(), new Color(0.45f, 0.2f, 0.2f));
        }

        // ---- Mounts ----

        void BuildMount(Transform root)
        {
            mountPanel = new MenuPanel(root, "Mounts", new Vector2(1100, 700), 260, 2, () =>
            {
                var lines = new List<string> { "Mounts make you faster and add stats while you ride. Tap one you own to ride or get off." };
                foreach (var m in Growth.Mounts)
                    lines.Add(m.Name + ":  speed +" + m.Speed + (m.AtkPct > 0 ? "  ATK +" + Mathf.RoundToInt(m.AtkPct * 100) + "%" : "") +
                              (m.DefPct > 0 ? "  DEF +" + Mathf.RoundToInt(m.DefPct * 100) + "%" : "") +
                              "   needs " + (m.MinRebirth > 0 ? m.MinRebirth + " Rebirth" : "Lv " + m.MinLevel));
                return string.Join("\n", lines);
            });
            for (int i = 0; i < Growth.Mounts.Length; i++)
            {
                int idx = i;
                mountPanel.AddButton(() =>
                {
                    var d = Player.Instance.Data;
                    var m = Growth.Mounts[idx];
                    bool own = (d.mountsOwned & (1 << idx)) != 0;
                    string state = !own ? (m.Zen > 0 ? m.Zen + " Zen" : m.Diamonds + " Diamonds") : d.mountId == idx ? "riding" : "owned";
                    return m.Name + "\n<size=20>" + state + "</size>";
                }, () => Player.Instance.BuyMount(idx), new Color(0.45f, 0.35f, 0.2f));
            }
        }

        // ---- Titles ----

        void BuildTitle(Transform root)
        {
            titlePanel = new MenuPanel(root, "Titles", new Vector2(1200, 760), 140, 2, () =>
                "Wear one unlocked title: it shows over your head and adds its stats.\nUnlock more by playing.");
            for (int i = 0; i < Growth.Titles.Length; i++)
            {
                int idx = i;
                titlePanel.AddButton(() =>
                {
                    var d = Player.Instance.Data;
                    var t = Growth.Titles[idx];
                    string fx = (t.Atk > 0 ? " ATK+" + Mathf.RoundToInt(t.Atk * 100) + "%" : "") + (t.Def > 0 ? " DEF+" + Mathf.RoundToInt(t.Def * 100) + "%" : "") +
                                (t.Hp > 0 ? " HP+" + Mathf.RoundToInt(t.Hp * 100) + "%" : "");
                    string state = d.titleId == idx ? "worn" : t.Unlocked(d) ? "unlocked" : "locked: " + t.Requirement;
                    return t.Name + fx + "\n<size=19>" + state + "</size>";
                }, () => Player.Instance.EquipTitle(idx), new Color(0.3f, 0.3f, 0.45f), 22);
            }
        }

        // ---- Events ----

        void BuildEvents(Transform root)
        {
            eventsPanel = new MenuPanel(root, "Events & Rewards", new Vector2(1200, 800), 320, 3, () =>
            {
                var p = Player.Instance;
                var d = p.Data;
                var a = Activities.Instance;
                return "Daily sign-in: " + (Activities.CanSignIn(d) ? "ready! " : "done today. Next ") + Activities.NextSignReward(d) + "\n" +
                       Activities.OnlineStatus(d) + "\n" +
                       (a != null ? a.InvasionStatus : "") + "\n" +
                       (WorldBoss.Instance != null ? WorldBoss.Instance.Status : "") + "\n" +
                       "Chaos Castle: " + Activities.ChaosLeft(d) + "/" + Activities.ChaosRunsPerDay + " today (floor crumbles, beat every monster)\n" +
                       "Arena: " + Activities.ArenaLeft(d) + "/" + Activities.ArenaRunsPerDay + " today, " + d.arenaPoints + " points, " + d.arenaWins + " wins\n" +
                       "Achievements ready to claim: " + Activities.ReadyCount(d);
            });
            eventsPanel.AddButton(() => "Sign In", () => Activities.SignIn(Player.Instance), new Color(0.2f, 0.45f, 0.25f));
            eventsPanel.AddButton(() => "Online Reward", () => Activities.ClaimOnline(Player.Instance), new Color(0.2f, 0.45f, 0.25f));
            eventsPanel.AddButton(() => "Achievements", () => { eventsPanel.Hide(); achievePanel.Show(); return null; }, new Color(0.45f, 0.38f, 0.15f));
            eventsPanel.AddButton(() => "Chaos Castle", () =>
            {
                string err = ChaosCastle.Enter();
                if (err == null) eventsPanel.Hide();
                return err;
            }, new Color(0.5f, 0.15f, 0.15f));
            eventsPanel.AddButton(() => "Arena", () =>
            {
                string err = ArenaFight.Enter();
                if (err == null) eventsPanel.Hide();
                return err;
            }, new Color(0.55f, 0.35f, 0.15f));
            eventsPanel.AddButton(() => "Ranking", () => { eventsPanel.Hide(); rankPanel.Show(); Activities.Instance?.RefreshRanking(() => rankPanel.Show()); return null; },
                new Color(0.25f, 0.35f, 0.55f));
        }

        void BuildAchievements(Transform root)
        {
            achievePanel = new MenuPanel(root, "Achievements", new Vector2(1500, 900), 40, 3, () => "Tap a finished one to claim its Diamonds.");
            for (int i = 0; i < Activities.Achievements.Length; i++)
            {
                int idx = i;
                achievePanel.AddButton(() =>
                {
                    var d = Player.Instance.Data;
                    var a = Activities.Achievements[idx];
                    string state = Activities.Claimed(d, idx) ? "claimed" : Mathf.Min(a.Progress(d), a.Target) + "/" + a.Target + "  ·  " + a.Diamonds + " Diamonds";
                    return a.Name + "\n<size=19>" + state + "</size>";
                }, () => Activities.ClaimAchievement(Player.Instance, idx), new Color(0.3f, 0.32f, 0.2f), 21);
            }
        }

        void BuildRanking(Transform root)
        {
            rankPanel = new MenuPanel(root, "Ranking (Combat Power)", new Vector2(1300, 860), 560, 2, () =>
                Activities.Instance != null ? Activities.Instance.RankingText(Player.Instance) : "");
            rankPanel.AddButton(() => "Refresh", () => { Activities.Instance?.RefreshRanking(() => rankPanel.Show()); return "Loading..."; }, new Color(0.25f, 0.35f, 0.55f));
        }

        // ---- Gems and Chaos Machine (both work on one picked equipment slot) ----

        string GearLine()
        {
            var it = Player.Instance.Data.equipped[gearSlot];
            return "Slot: " + GameData.SlotAt(gearSlot) + (it.IsEmpty ? " (empty)" : " — " + it.Label + "\n" + InventoryUI.Extras(it) +
                   (it.life > 0 ? (InventoryUI.Extras(it).Length > 0 ? " | " : "") + "Option +" + it.life * 4 : ""));
        }

        void BuildGems(Transform root)
        {
            gemsPanel = new MenuPanel(root, "Gems & Sockets", new Vector2(1200, 800), 300, 4, () =>
            {
                var p = Player.Instance;
                var lines = new List<string> { GearLine(), "", "Ruby ATK +1.5%/lv · Sapphire DEF +2%/lv · Emerald HP +2%/lv · Topaz Critical +1%/lv", "Your gems:" };
                foreach (GemType t in new[] { GemType.Ruby, GemType.Sapphire, GemType.Emerald, GemType.Topaz })
                {
                    var counts = new List<string>();
                    for (int lv = 1; lv <= 5; lv++) { int n = p.GemCount(t, lv); if (n > 0) counts.Add("Lv" + lv + " x" + n); }
                    lines.Add("  " + t + ": " + (counts.Count > 0 ? string.Join(", ", counts) : "none"));
                }
                lines.Add("Excellent items have 0-1 sockets, Ancient items 2. Gems drop from Lv15+ monsters.");
                return string.Join("\n", lines);
            });
            gemsPanel.AddButton(() => "< Slot", () => { gearSlot = (gearSlot + GameData.EquipSlots - 1) % GameData.EquipSlots; return null; }, new Color(0.3f, 0.3f, 0.35f));
            gemsPanel.AddButton(() => "Slot >", () => { gearSlot = (gearSlot + 1) % GameData.EquipSlots; return null; }, new Color(0.3f, 0.3f, 0.35f));
            gemsPanel.AddButton(() => "Combine 3 to 1", () => Player.Instance.CombineGems(), new Color(0.2f, 0.45f, 0.25f));
            gemsPanel.AddButton(() => "Remove gems", () => Player.Instance.ClearSockets(gearSlot), new Color(0.45f, 0.2f, 0.2f));
            foreach (GemType t in new[] { GemType.Ruby, GemType.Sapphire, GemType.Emerald, GemType.Topaz })
            {
                var gem = t;
                gemsPanel.AddButton(() => "Socket " + gem, () => Player.Instance.SocketGem(gearSlot, gem), new Color(0.2f, 0.35f, 0.5f));
            }
        }

        void BuildChaos(Transform root)
        {
            chaosPanel = new MenuPanel(root, "Chaos Machine", new Vector2(1200, 760), 300, 3, () =>
            {
                var d = Player.Instance.Data;
                return GearLine() + "\n\nJewel of Chaos " + d.jewelChaos + "   Jewel of Life " + d.jewelLife + "   Soul " + d.jewelSoul + "   Bless " + d.jewelBless + "\n\n" +
                       "Jewel of Life: +4 ATK/DEF option on the picked item (50%, up to +" + ChaosMachine.MaxLife * 4 + ")\n" +
                       "Reroll: 2 Chaos, new Excellent options for the picked item\n" +
                       "Condor Flame: 3 Chaos + Soul + Bless + 5000 Zen (50%)\n" +
                       "Feathers: 1 Chaos + 3 Normal items from the bag = 5 Condor Feathers (75%)";
            });
            chaosPanel.AddButton(() => "< Slot", () => { gearSlot = (gearSlot + GameData.EquipSlots - 1) % GameData.EquipSlots; return null; }, new Color(0.3f, 0.3f, 0.35f));
            chaosPanel.AddButton(() => "Slot >", () => { gearSlot = (gearSlot + 1) % GameData.EquipSlots; return null; }, new Color(0.3f, 0.3f, 0.35f));
            chaosPanel.AddButton(() => "Jewel of Life", () => ChaosMachine.AddLife(Player.Instance, gearSlot), new Color(0.2f, 0.45f, 0.55f));
            chaosPanel.AddButton(() => "Reroll options", () => ChaosMachine.Reroll(Player.Instance, gearSlot), new Color(0.5f, 0.4f, 0.15f));
            chaosPanel.AddButton(() => "Make Condor Flame", () => ChaosMachine.MakeFlame(Player.Instance), new Color(0.55f, 0.25f, 0.1f));
            chaosPanel.AddButton(() => "Make Feathers", () => ChaosMachine.MakeFeathers(Player.Instance), new Color(0.35f, 0.4f, 0.55f));
        }
    }

    // Chat log + text box on the left, party / guild / friends on the right.
    public class SocialPanel
    {
        readonly RectTransform panel;
        readonly GameObject root;
        readonly Text log, info, result;
        readonly InputField field;
        readonly List<(Text text, System.Func<string> label)> buttons = new List<(Text, System.Func<string>)>();
        string channel = "World";
        int guildPick;

        public SocialPanel(Transform canvas)
        {
            var tl = new Vector2(0, 1);
            (root, panel) = Skin.Window(canvas, "Social", "Social", new Vector2(1600, 920), () => root.SetActive(false));
            Skin.Sliced(UIB.Rect("LogBox", panel, tl, new Vector2(20, -84), new Vector2(880, 626)), Skin.Inset, Color.white).raycastTarget = false;
            Skin.Sliced(UIB.Rect("InfoBox", panel, tl, new Vector2(910, -84), new Vector2(670, 262)), Skin.Inset, Color.white).raycastTarget = false;
            log = UIB.Label(panel, "", 22, tl, new Vector2(34, -94), new Vector2(852, 606), TextAnchor.LowerLeft);
            log.horizontalOverflow = HorizontalWrapMode.Wrap;
            field = UIB.Field(panel, "", tl, new Vector2(20, -722), new Vector2(880, 70), 28, 120);
            result = UIB.Label(panel, "", 24, new Vector2(0, 0), new Vector2(30, 30), new Vector2(860, 70));
            result.color = Skin.Gold;
            result.horizontalOverflow = HorizontalWrapMode.Wrap;
            info = UIB.Label(panel, "", 22, tl, new Vector2(926, -96), new Vector2(640, 240), TextAnchor.UpperLeft);
            info.color = Skin.Cream;
            info.horizontalOverflow = HorizontalWrapMode.Wrap;

            Add(() => "Send (" + channel + ")", Send, new Color(0.2f, 0.45f, 0.25f));
            Add(() => "Channel: " + channel, () => { channel = channel == "World" ? "Party" : channel == "Party" ? "Guild" : "World"; return null; }, new Color(0.3f, 0.3f, 0.35f));
            Add(() => "Invite nearby", () => S()?.Invite(Nearby()) ?? Offline, new Color(0.25f, 0.4f, 0.3f));
            Add(() => "Accept invite", () => S()?.AcceptInvite() ?? Offline, new Color(0.25f, 0.4f, 0.3f));
            Add(() => "Leave party", () => S()?.LeaveParty() ?? Offline, new Color(0.45f, 0.2f, 0.2f));
            Add(() => "Set my name", SetName, new Color(0.3f, 0.3f, 0.45f));
            Add(() => "Found guild\n<size=18>name in box, 30000 Zen</size>", () => { if (S() == null) return Offline; S().CreateGuild(field.text, Done); return "..."; }, new Color(0.35f, 0.3f, 0.5f));
            Add(() => "Find guilds", () => { if (S() == null) return Offline; S().SearchGuilds(Done); return "..."; }, new Color(0.35f, 0.3f, 0.5f));
            Add(() =>
            {
                var s = S();
                return s != null && s.GuildList.Count > 0 ? "Join " + s.GuildList[guildPick % s.GuildList.Count].name + "\n<size=18>tap again for next</size>" : "Join guild";
            }, JoinGuild, new Color(0.35f, 0.3f, 0.5f));
            Add(() => "Leave guild", () => { if (S() == null) return Offline; S().LeaveGuild(Done); return "..."; }, new Color(0.45f, 0.2f, 0.2f));
            Add(() => "Add friend nearby", () => { if (S() == null) return Offline; S().AddFriend(Nearby(), Done); return "..."; }, new Color(0.25f, 0.35f, 0.5f));
            Add(() => "Accept friends", () => { if (S() == null) return Offline; S().AcceptFriends(Done); return "..."; }, new Color(0.25f, 0.35f, 0.5f));

            root.SetActive(false);
        }

        const string Offline = "Social features need the server: log in online";
        static Social S() => Social.Instance;
        static RemotePlayer Nearby() => WorldSync.Instance != null && Player.Instance != null ? WorldSync.Instance.Nearest(Player.Instance.transform.position, 6f) : null;

        void Add(System.Func<string> label, System.Func<string> action, Color color)
        {
            int i = buttons.Count;
            var pos = new Vector2(920 + (i % 2) * 330, -360 - (i / 2) * 82);
            var b = UIB.Button(panel, "", new Vector2(0, 1), pos, new Vector2(320, 74), color, () =>
            {
                string r = action();
                if (r != null) result.text = r;
                Refresh();
            }, null, 23);
            buttons.Add((b.GetComponentInChildren<Text>(), label));
        }

        void Done(string msg)
        {
            result.text = msg;
            Refresh();
        }

        string Send()
        {
            if (S() == null) return Offline;
            S().Say(channel, field.text);
            field.text = "";
            return null;
        }

        string SetName()
        {
            string n = (field.text ?? "").Trim();
            if (n.Length < 3 || n.Length > 14) return "Name: 3 to 14 letters";
            var p = Player.Instance;
            p.Data.charName = n;
            p.Commit();
            field.text = "";
            _ = Online.SetDisplayName(n);
            return "Your name is now " + n;
        }

        string JoinGuild()
        {
            var s = S();
            if (s == null) return Offline;
            if (s.GuildList.Count == 0) return "Tap Find guilds first";
            int pick = guildPick % s.GuildList.Count;
            guildPick++;
            s.JoinGuild(pick, Done);
            return "...";
        }

        public void Toggle()
        {
            if (root.activeSelf) root.SetActive(false);
            else Show();
        }

        public void Show()
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            var s = S();
            if (s != null)
            {
                s.LogChanged -= Refresh;
                s.LogChanged += Refresh;
                s.RefreshFriends();
            }
            Refresh();
        }

        void Refresh()
        {
            if (root == null || !root.activeSelf) return;
            var s = S();
            var p = Player.Instance;
            string me = p != null && !string.IsNullOrEmpty(p.Data.charName) ? p.Data.charName : "(no name set)";
            if (s == null)
            {
                log.text = Offline;
                info.text = "Name: " + me;
            }
            else
            {
                log.text = string.Join("\n", s.Log.Skip(Mathf.Max(0, s.Log.Count - 22)));
                info.text = "Name: " + me + "\n\nParty (+15% EXP per member nearby):\n" + s.PartyText() +
                            "\n\nGuild: " + (s.GuildId != null ? s.GuildName + " (" + s.GuildMembers.Count + " members)" : "none") +
                            "\n\nFriends:\n" + s.FriendsText();
            }
            foreach (var (text, label) in buttons) text.text = label();
        }
    }
}
