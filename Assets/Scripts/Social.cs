using System;
using System.Collections.Generic;
using System.Linq;
using Nakama;
using UnityEngine;

namespace MuProto
{
    [Serializable] class ChatContent { public string n, m; }
    [Serializable] class PartyInviteMsg { public string t, n; }        // t = invited session
    [Serializable] class PartyListMsg { public string p; public string[] m; public string[] names; } // party id (leader session), members
    [Serializable] class PartyLeaveMsg { public string p; }
    [Serializable] class PartyChatMsg { public string p, n, m; }

    // Chat (world / party / guild), parties, guilds and friends. Chat and guilds use Nakama's own
    // chat channels and groups; parties travel as messages in the shared world match.
    public class Social : MonoBehaviour
    {
        public const long OpInvite = 5, OpParty = 6, OpAccept = 7, OpLeave = 8, OpPartyChat = 9;
        const int MaxParty = 5;
        const int MaxLog = 60;

        public static Social Instance { get; private set; }

        public readonly List<string> Log = new List<string>();
        public event Action LogChanged;

        IChannel worldChannel, guildChannel;
        ISocket boundSocket;

        public readonly List<string> Party = new List<string>();        // session ids, leader first
        public readonly Dictionary<string, string> PartyNames = new Dictionary<string, string>();
        public string PartyId { get; private set; }
        public bool InParty => Party.Count > 1;
        public bool IsLeader => PartyId == WorldSync.Instance?.MySession;

        public string InviteFrom { get; private set; }
        public string InviteName { get; private set; }

        public string GuildId { get; private set; }
        public string GuildName { get; private set; } = "";
        public List<string> GuildMembers { get; } = new List<string>();
        public List<(string id, string name)> GuildList { get; } = new List<(string, string)>();
        public List<(string id, string name, bool online, int state)> Friends { get; } = new List<(string, string, bool, int)>();

        public static void Create()
        {
            if (Instance == null) new GameObject("Social").AddComponent<Social>();
        }

        void Awake() => Instance = this;

        // Party EXP bonus: +15% for each party member hunting near you.
        public static float PartyExpBonus(Vector3 pos)
        {
            var s = Instance;
            if (s == null || !s.InParty || WorldSync.Instance == null) return 1f;
            int near = 0;
            foreach (var rp in WorldSync.Instance.Remotes)
            {
                if (!s.Party.Contains(rp.SessionId)) continue;
                Vector3 d = rp.transform.position - pos;
                d.y = 0;
                if (d.magnitude < 30f) near++;
            }
            return 1f + 0.15f * near;
        }

        public void AddLog(string line)
        {
            Log.Add(line);
            while (Log.Count > MaxLog) Log.RemoveAt(0);
            LogChanged?.Invoke();
        }

        // Called by WorldSync after each (re)connect.
        public async void OnConnected(ISocket socket)
        {
            if (boundSocket != socket)
            {
                boundSocket = socket;
                socket.ReceivedChannelMessage += OnChannelMessage;
            }
            try
            {
                worldChannel = await socket.JoinChatAsync("world", ChannelType.Room, true, false);
                AddLog("<color=#aaaaaa>Joined world chat</color>");
            }
            catch (Exception e) { Debug.LogWarning("[Social] World chat: " + e.Message); }
            await LoadGuild();
            RefreshFriends();
        }

        void OnChannelMessage(IApiChannelMessage msg)
        {
            ChatContent c = null;
            try { c = JsonUtility.FromJson<ChatContent>(msg.Content); } catch { }
            if (c == null || string.IsNullOrEmpty(c.m)) return;
            bool guild = guildChannel != null && msg.ChannelId == guildChannel.Id;
            AddLog((guild ? "<color=#7fd4ff>[Guild] " : "<color=#ffffff>[World] ") + c.n + ": </color>" + c.m);
        }

        string MyName => Player.Instance != null && !string.IsNullOrEmpty(Player.Instance.Data.charName) ? Player.Instance.Data.charName
                       : Online.Session != null ? Online.Session.Username : "Player";

        public async void Say(string channel, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 120) text = text.Substring(0, 120);
            var ws = WorldSync.Instance;
            try
            {
                string json = JsonUtility.ToJson(new ChatContent { n = MyName, m = text });
                switch (channel)
                {
                    case "Party":
                        if (!InParty) { AddLog("<color=#ff9966>You are not in a party</color>"); return; }
                        ws?.Send(OpPartyChat, new PartyChatMsg { p = PartyId, n = MyName, m = text });
                        AddLog("<color=#9dff8a>[Party] " + MyName + ": </color>" + text);
                        break;
                    case "Guild":
                        if (guildChannel == null || boundSocket == null) { AddLog("<color=#ff9966>You are not in a guild</color>"); return; }
                        await boundSocket.WriteChatMessageAsync(guildChannel, json);
                        break;
                    default:
                        if (worldChannel == null || boundSocket == null) { AddLog("<color=#ff9966>World chat is offline</color>"); return; }
                        await boundSocket.WriteChatMessageAsync(worldChannel, json);
                        break;
                }
            }
            catch (Exception e) { AddLog("<color=#ff6655>Chat failed: " + e.Message + "</color>"); }
        }

        // ---- Party (messages through the world match) ----

        public string Invite(RemotePlayer rp)
        {
            var ws = WorldSync.Instance;
            if (ws == null) return "Play online to make a party";
            if (rp == null) return "Stand next to a player to invite them";
            if (Party.Count >= MaxParty) return "Party is full";
            if (InParty && !IsLeader) return "Only the leader can invite";
            ws.Send(OpInvite, new PartyInviteMsg { t = rp.SessionId, n = MyName });
            return "Invited " + rp.Name;
        }

        public void Receive(long op, string from, string json)
        {
            var ws = WorldSync.Instance;
            if (ws == null) return;
            switch (op)
            {
                case OpInvite:
                {
                    var m = JsonUtility.FromJson<PartyInviteMsg>(json);
                    if (m == null || m.t != ws.MySession || InParty) return;
                    InviteFrom = from;
                    InviteName = m.n;
                    HUD.Message(m.n + " invites you to a party. Open Social > Party to accept", 4f);
                    LogChanged?.Invoke();
                    break;
                }
                case OpAccept:
                {
                    var m = JsonUtility.FromJson<PartyInviteMsg>(json);
                    if (m == null || m.t != ws.MySession) return;
                    if (Party.Count == 0) { Party.Add(ws.MySession); PartyNames[ws.MySession] = MyName; PartyId = ws.MySession; }
                    if (!IsLeader || Party.Count >= MaxParty || Party.Contains(from)) return;
                    Party.Add(from);
                    PartyNames[from] = m.n;
                    BroadcastParty();
                    AddLog("<color=#9dff8a>" + m.n + " joined the party</color>");
                    break;
                }
                case OpParty:
                {
                    var m = JsonUtility.FromJson<PartyListMsg>(json);
                    if (m == null) return;
                    bool mine = m.m != null && m.m.Contains(ws.MySession);
                    if (mine)
                    {
                        Party.Clear();
                        Party.AddRange(m.m);
                        for (int i = 0; i < m.m.Length && m.names != null && i < m.names.Length; i++) PartyNames[m.m[i]] = m.names[i];
                        PartyId = m.p;
                        InviteFrom = null;
                    }
                    else if (PartyId == m.p) ClearParty("The party was disbanded");
                    LogChanged?.Invoke();
                    break;
                }
                case OpLeave:
                {
                    var m = JsonUtility.FromJson<PartyLeaveMsg>(json);
                    if (m == null || m.p != PartyId) return;
                    RemoveMember(from);
                    break;
                }
                case OpPartyChat:
                {
                    var m = JsonUtility.FromJson<PartyChatMsg>(json);
                    if (m != null && m.p == PartyId && InParty) AddLog("<color=#9dff8a>[Party] " + m.n + ": </color>" + m.m);
                    break;
                }
            }
        }

        public string AcceptInvite()
        {
            var ws = WorldSync.Instance;
            if (ws == null || InviteFrom == null) return "No invite";
            ws.Send(OpAccept, new PartyInviteMsg { t = InviteFrom, n = MyName });
            string who = InviteName;
            InviteFrom = null;
            return "Joining " + who + "'s party...";
        }

        public string LeaveParty()
        {
            var ws = WorldSync.Instance;
            if (!InParty || ws == null) { ClearParty(null); return "Not in a party"; }
            if (IsLeader) ws.Send(OpParty, new PartyListMsg { p = PartyId, m = new string[0], names = new string[0] });
            else ws.Send(OpLeave, new PartyLeaveMsg { p = PartyId });
            ClearParty(null);
            return "Left the party";
        }

        // A player went offline: drop them from the party (and disband if they led it).
        public void OnPlayerLeft(string session)
        {
            if (!Party.Contains(session)) return;
            if (session == PartyId) ClearParty("The party leader left");
            else RemoveMember(session);
        }

        void RemoveMember(string session)
        {
            if (!Party.Remove(session)) return;
            AddLog("<color=#9dff8a>" + (PartyNames.TryGetValue(session, out var n) ? n : "A member") + " left the party</color>");
            if (IsLeader) BroadcastParty();
            if (Party.Count <= 1) ClearParty(null);
        }

        void BroadcastParty()
        {
            WorldSync.Instance?.Send(OpParty, new PartyListMsg
            {
                p = PartyId,
                m = Party.ToArray(),
                names = Party.Select(id => PartyNames.TryGetValue(id, out var n) ? n : "?").ToArray(),
            });
            LogChanged?.Invoke();
        }

        void ClearParty(string message)
        {
            Party.Clear();
            PartyId = null;
            if (message != null) AddLog("<color=#9dff8a>" + message + "</color>");
            LogChanged?.Invoke();
        }

        public string PartyText()
        {
            if (!InParty) return InviteFrom != null ? "Invite from " + InviteName : "No party";
            var ws = WorldSync.Instance;
            var lines = new List<string>();
            foreach (var id in Party)
            {
                string name = PartyNames.TryGetValue(id, out var n) ? n : "?";
                string hp = "";
                if (ws != null && id != ws.MySession)
                {
                    var rp = ws.Remotes.FirstOrDefault(r => r.SessionId == id);
                    if (rp != null) hp = "  HP " + rp.HpPercent + "%";
                }
                lines.Add((id == PartyId ? "* " : "• ") + name + hp);
            }
            return string.Join("\n", lines);
        }

        // ---- Guild (Nakama groups) ----

        async System.Threading.Tasks.Task LoadGuild()
        {
            if (!Online.IsOnline) return;
            try
            {
                var mine = await Online.Client.ListUserGroupsAsync(Online.Session, null, 10, null);
                var g = mine.UserGroups.FirstOrDefault(x => x.State <= 2); // superadmin, admin or member
                if (g == null) { SetGuild(null, ""); return; }
                SetGuild(g.Group.Id, g.Group.Name);
                if (boundSocket != null) guildChannel = await boundSocket.JoinChatAsync(GuildId, ChannelType.Group, true, false);
                await RefreshGuildMembers();
            }
            catch (Exception e) { Debug.LogWarning("[Social] Guild: " + e.Message); }
        }

        void SetGuild(string id, string name)
        {
            GuildId = id;
            GuildName = name ?? "";
            if (id == null) { guildChannel = null; GuildMembers.Clear(); }
            LogChanged?.Invoke();
        }

        async System.Threading.Tasks.Task RefreshGuildMembers()
        {
            GuildMembers.Clear();
            if (GuildId == null) return;
            var users = await Online.Client.ListGroupUsersAsync(Online.Session, GuildId, null, 50, null);
            foreach (var u in users.GroupUsers)
                GuildMembers.Add((string.IsNullOrEmpty(u.User.DisplayName) ? u.User.Username : u.User.DisplayName) + (u.User.Online ? " (online)" : ""));
            LogChanged?.Invoke();
        }

        public async void CreateGuild(string name, Action<string> done)
        {
            name = (name ?? "").Trim();
            if (!Online.IsOnline) { done("Play online to make a guild"); return; }
            if (GuildId != null) { done("Leave your guild first"); return; }
            if (name.Length < 3) { done("Guild name: at least 3 letters"); return; }
            var p = Player.Instance;
            if (p.Data.gold < 30000) { done("Founding a guild costs 30000 Zen"); return; }
            try
            {
                await Online.Client.CreateGroupAsync(Online.Session, name, "King of Origin guild", null, null, true, 50);
                p.Data.gold -= 30000;
                p.Commit();
                await LoadGuild();
                done("Guild " + name + " founded!");
            }
            catch (Exception e) { done("Could not create: " + e.Message); }
        }

        public async void SearchGuilds(Action<string> done)
        {
            if (!Online.IsOnline) { done("Offline"); return; }
            try
            {
                var list = await Online.Client.ListGroupsAsync(Online.Session, null, 20, null);
                GuildList.Clear();
                foreach (var g in list.Groups) GuildList.Add((g.Id, g.Name));
                LogChanged?.Invoke();
                done(GuildList.Count + " guilds found");
            }
            catch (Exception e) { done("Search failed: " + e.Message); }
        }

        public async void JoinGuild(int index, Action<string> done)
        {
            if (index < 0 || index >= GuildList.Count) { done(""); return; }
            if (GuildId != null) { done("Leave your guild first"); return; }
            try
            {
                await Online.Client.JoinGroupAsync(Online.Session, GuildList[index].id);
                await LoadGuild();
                done(GuildId != null ? "Joined " + GuildName : "Request sent");
            }
            catch (Exception e) { done("Join failed: " + e.Message); }
        }

        public async void LeaveGuild(Action<string> done)
        {
            if (GuildId == null) { done("Not in a guild"); return; }
            try
            {
                await Online.Client.LeaveGroupAsync(Online.Session, GuildId);
                string old = GuildName;
                SetGuild(null, "");
                done("Left " + old);
            }
            catch (Exception e) { done("Leave failed: " + e.Message); }
        }

        // ---- Friends (Nakama friends) ----

        public async void AddFriend(RemotePlayer rp, Action<string> done)
        {
            if (!Online.IsOnline || rp == null || string.IsNullOrEmpty(rp.UserId)) { done("Stand next to a player to add them"); return; }
            try
            {
                await Online.Client.AddFriendsAsync(Online.Session, new[] { rp.UserId });
                RefreshFriends();
                done("Friend request sent to " + rp.Name);
            }
            catch (Exception e) { done("Failed: " + e.Message); }
        }

        public async void AcceptFriends(Action<string> done)
        {
            var incoming = Friends.Where(f => f.state == 2).Select(f => f.id).ToArray();
            if (incoming.Length == 0) { done("No friend requests"); return; }
            try
            {
                await Online.Client.AddFriendsAsync(Online.Session, incoming);
                RefreshFriends();
                done("Accepted " + incoming.Length + " request(s)");
            }
            catch (Exception e) { done("Failed: " + e.Message); }
        }

        public async void RefreshFriends()
        {
            if (!Online.IsOnline) return;
            try
            {
                var list = await Online.Client.ListFriendsAsync(Online.Session, null, 100, null);
                Friends.Clear();
                foreach (var f in list.Friends)
                    Friends.Add((f.User.Id, string.IsNullOrEmpty(f.User.DisplayName) ? f.User.Username : f.User.DisplayName, f.User.Online, f.State));
                LogChanged?.Invoke();
            }
            catch (Exception e) { Debug.LogWarning("[Social] Friends: " + e.Message); }
        }

        public string FriendsText()
        {
            if (Friends.Count == 0) return "No friends yet";
            string[] states = { "friend", "request sent", "wants to be friends", "blocked" };
            return string.Join("\n", Friends.Select(f => "• " + f.name + "  (" + states[Mathf.Clamp(f.state, 0, 3)] + (f.state == 0 ? (f.online ? ", online" : ", offline") : "") + ")"));
        }
    }
}
