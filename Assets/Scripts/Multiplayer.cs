using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nakama;
using UnityEngine;
using UnityEngine.UI;

namespace MuProto
{
    // What each player broadcasts about themselves several times a second.
    [Serializable]
    public class PlayerState
    {
        public string n;   // name
        public string c;   // class id
        public int lv, rb; // level, rebirths
        public int w;      // wings: tier * 100 + level
        public int pt;     // pet level, 0 = no pet
        public int g;      // armour set enhance level (glow)
        public int h;      // HP percent
        public int r;      // 1 = red name (outlaw)
        public int k;      // 1 = PK mode on
        public int cp;     // combat power
        public float x, z, ry;
        public int a;      // 0 idle, 1 attack, 2 dead
    }

    [Serializable] class HitMsg { public string t; public int d; public int c; public string n; }     // t = target session
    [Serializable] class KillMsg { public string t; public string n; public int r; }                   // t = killer session
    [Serializable] class BossMsg { public long c; public int d; public string n; }                     // cycle, total damage so far

    // Everyone who logs in joins one shared relayed match named "world": positions, PvP hits,
    // PK kills and world boss damage all travel through it.
    public class WorldSync : MonoBehaviour
    {
        const string WorldName = "world";
        const long OpState = 1, OpHit = 2, OpKilled = 3, OpBoss = 4;
        const float SendInterval = 0.1f, Heartbeat = 1f, ReconnectDelay = 5f;

        public static WorldSync Instance { get; private set; }

        ISocket socket;
        IMatch match;
        readonly Dictionary<string, RemotePlayer> remotes = new Dictionary<string, RemotePlayer>();
        PlayerState last;
        float sendTimer, lastSent, reconnectAt = -1f;
        bool connecting;
        Canvas nameCanvas;

        public int OthersOnline => remotes.Count;
        public IEnumerable<RemotePlayer> Remotes => remotes.Values.Where(r => r != null);
        string MyName => Online.Session != null ? Online.Session.Username : "Player";

        public static void Create()
        {
            if (!Online.IsOnline || Instance != null) return;
            new GameObject("World Sync").AddComponent<WorldSync>();
        }

        void Awake()
        {
            Instance = this;
            // Keep sending and receiving while the window is not focused (the editor next to an emulator,
            // or a second game window); otherwise the other player freezes or never shows up.
            Application.runInBackground = true;
            var go = HUD.NewCanvas("Nameplates");
            nameCanvas = go.GetComponent<Canvas>();
            nameCanvas.sortingOrder = -1; // under the HUD
            Destroy(go.GetComponent<GraphicRaycaster>());
        }

        void Start() => Connect();

        async void Connect()
        {
            if (connecting) return;
            connecting = true;
            try
            {
                socket = Online.Client.NewSocket(true);
                socket.ReceivedMatchPresence += OnPresence;
                socket.ReceivedMatchState += OnState;
                socket.Closed += OnClosed;
                await socket.ConnectAsync(Online.Session, true);
                match = await socket.CreateMatchAsync(WorldName); // same name, same match for everyone
                Debug.Log("[Online] Joined world match " + match.Id + " as " + Online.Session.Username +
                          ", others here: " + match.Presences.Count());
                foreach (var p in match.Presences) Debug.Log("[Online] Already here: " + p.Username);
                last = null; // send right away
                HUD.Message("Online world joined", 2f);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Online] World connect failed: " + e.Message);
                match = null;
                reconnectAt = Time.time + ReconnectDelay;
            }
            finally { connecting = false; }
        }

        void OnClosed(string reason)
        {
            if (this == null) return;
            match = null;
            ClearRemotes();
            HUD.Message("Lost connection to the world, retrying...", 2f);
            reconnectAt = Time.time + ReconnectDelay;
        }

        void OnPresence(IMatchPresenceEvent e)
        {
            if (match == null || e.MatchId != match.Id) return;
            if (e.Leaves != null) foreach (var p in e.Leaves) Remove(p.SessionId);
            if (e.Joins != null) foreach (var p in e.Joins) { Debug.Log("[Online] Joined: " + p.Username); last = null; } // newcomer needs our state now
        }

        void OnState(IMatchState s)
        {
            if (s.UserPresence == null || match == null || s.UserPresence.SessionId == match.Self.SessionId) return;
            string from = s.UserPresence.SessionId;
            string json = Encoding.UTF8.GetString(s.State);
            try
            {
                switch (s.OpCode)
                {
                    case OpState: OnPlayerState(from, JsonUtility.FromJson<PlayerState>(json)); break;
                    case OpHit:
                    {
                        var m = JsonUtility.FromJson<HitMsg>(json);
                        if (m != null && m.t == match.Self.SessionId && Player.Instance != null)
                            Player.Instance.ReceivePvpHit(m.d, m.c == 1, from, m.n);
                        break;
                    }
                    case OpKilled:
                    {
                        var m = JsonUtility.FromJson<KillMsg>(json);
                        if (m != null && m.t == match.Self.SessionId && Player.Instance != null)
                            Player.Instance.OnPvpKill(m.n, m.r == 1);
                        break;
                    }
                    case OpBoss:
                    {
                        var m = JsonUtility.FromJson<BossMsg>(json);
                        if (m != null && WorldBoss.Instance != null) WorldBoss.Instance.OnRemoteDamage(from, m.n, m.c, m.d);
                        break;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Online] Bad message " + s.OpCode + ": " + e.Message); }
        }

        void OnPlayerState(string id, PlayerState st)
        {
            if (st == null) return;
            if (!remotes.TryGetValue(id, out var rp) || rp == null)
            {
                Debug.Log("[Online] Player appeared: " + st.n + " Lv " + st.lv);
                rp = RemotePlayer.Create(id, st, nameCanvas.transform);
                remotes[id] = rp;
            }
            rp.Apply(st);
        }

        void Send(long op, object msg)
        {
            if (match == null || socket == null) return;
            socket.SendMatchStateAsync(match.Id, op, JsonUtility.ToJson(msg));
        }

        public void SendHit(RemotePlayer target, int damage, bool crit) =>
            Send(OpHit, new HitMsg { t = target.SessionId, d = damage, c = crit ? 1 : 0, n = MyName });

        public void SendKilled(string killerSession, bool iWasRed) =>
            Send(OpKilled, new KillMsg { t = killerSession, n = MyName, r = iWasRed ? 1 : 0 });

        public void SendBossDamage(long cycle, int totalDamage) =>
            Send(OpBoss, new BossMsg { c = cycle, d = totalDamage, n = MyName });

        public RemotePlayer NearestAttackable(Vector3 from, float range, bool pkMode)
        {
            RemotePlayer best = null;
            float bestDist = range;
            foreach (var rp in Remotes)
            {
                if (!rp.CanBeAttacked(pkMode)) continue;
                Vector3 d = rp.transform.position - from;
                d.y = 0;
                if (d.magnitude <= bestDist) { bestDist = d.magnitude; best = rp; }
            }
            return best;
        }

        void Update()
        {
            if (match == null)
            {
                if (!connecting && reconnectAt > 0 && Time.time >= reconnectAt)
                {
                    reconnectAt = -1f;
                    if (socket != null) { socket.ReceivedMatchPresence -= OnPresence; socket.ReceivedMatchState -= OnState; socket.Closed -= OnClosed; }
                    Connect();
                }
                return;
            }

            sendTimer -= Time.deltaTime;
            if (sendTimer > 0) return;
            sendTimer = SendInterval;

            var p = Player.Instance;
            if (p == null || !p.HasClass) return;
            var st = new PlayerState
            {
                n = MyName,
                c = p.Class.Id,
                lv = p.Data.level,
                rb = p.Data.rebirth,
                w = Player.WingCode(p.Data.wingTier, p.Data.wingLevel),
                pt = p.Data.petLevel,
                g = p.GlowPlus,
                h = Mathf.CeilToInt(100f * p.Hp / Mathf.Max(1, p.MaxHp)),
                r = p.IsRed ? 1 : 0,
                k = p.PkMode ? 1 : 0,
                cp = p.CombatPower,
                x = p.transform.position.x,
                z = p.transform.position.z,
                ry = p.transform.eulerAngles.y,
                a = p.IsDead ? 2 : p.InAction ? 1 : 0,
            };
            if (last != null && SameAs(last, st) && Time.time - lastSent < Heartbeat) return;
            last = st;
            lastSent = Time.time;
            Send(OpState, st);
        }

        static bool SameAs(PlayerState a, PlayerState b) =>
            a.c == b.c && a.lv == b.lv && a.rb == b.rb && a.w == b.w && a.pt == b.pt && a.g == b.g && a.h == b.h &&
            a.r == b.r && a.k == b.k && a.cp == b.cp && a.a == b.a &&
            Mathf.Abs(a.x - b.x) < 0.02f && Mathf.Abs(a.z - b.z) < 0.02f && Mathf.Abs(Mathf.DeltaAngle(a.ry, b.ry)) < 2f;

        void Remove(string sessionId)
        {
            if (remotes.TryGetValue(sessionId, out var rp) && rp != null) rp.Remove();
            remotes.Remove(sessionId);
        }

        void ClearRemotes()
        {
            foreach (var rp in remotes.Values) if (rp != null) rp.Remove();
            remotes.Clear();
        }

        async void OnDestroy()
        {
            if (socket == null) return;
            socket.Closed -= OnClosed;
            try { await socket.CloseAsync(); } catch { }
        }
    }

    // Another player's character: moves smoothly toward the last position they sent.
    public class RemotePlayer : MonoBehaviour
    {
        Transform body, wings;
        ModelAnimator anim;
        RemotePet pet;
        Aura aura;
        RectTransform plate, hpFill;
        Text plateText;
        PlayerState state;
        Vector3 targetPos;
        float targetRot;

        public string SessionId { get; private set; }
        public string Name => state != null ? state.n : "";
        public bool IsDead => state != null && state.a == 2;
        public bool IsRed => state != null && state.r == 1;
        public bool InTown
        {
            get
            {
                Vector3 p = transform.position;
                p.y = 0;
                return p.magnitude < GameData.TownRadius;
            }
        }

        // Anyone may hit an outlaw; others only while you are in PK mode. Never inside town.
        public bool CanBeAttacked(bool attackerPkMode) => state != null && !IsDead && !InTown && (attackerPkMode || IsRed);

        public static RemotePlayer Create(string sessionId, PlayerState st, Transform nameCanvas)
        {
            var rp = new GameObject("Other Player").AddComponent<RemotePlayer>();
            rp.SessionId = sessionId;
            rp.transform.position = rp.targetPos = new Vector3(st.x, 0, st.z);
            rp.transform.rotation = Quaternion.Euler(0, st.ry, 0);
            rp.plate = UIB.Rect("Name", nameCanvas, Vector2.zero, Vector2.zero, new Vector2(520, 70), new Vector2(0.5f, 0));
            rp.plateText = UIB.Text(UIB.Rect("Text", rp.plate, new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 44)), "", 26,
                new Color(0.6f, 0.9f, 1f), TextAnchor.MiddleCenter);
            rp.hpFill = UIB.Bar(rp.plate, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(140, 12), new Color(0.85f, 0.15f, 0.15f));
            return rp;
        }

        public void Apply(PlayerState st)
        {
            bool rebuildBody = state == null || state.c != st.c;
            bool rebuildWings = state == null || state.w != st.w;
            state = st;
            targetPos = new Vector3(st.x, 0, st.z);
            targetRot = st.ry;
            if ((targetPos - transform.position).sqrMagnitude > 100f) transform.position = targetPos; // teleported (dungeon, respawn)

            var cls = Classes.Get(st.c);
            if (rebuildBody)
            {
                if (body != null) Destroy(body.gameObject);
                body = Player.MakeBody(transform, cls, out anim);
                aura = Aura.Attach(body, cls.BodyColor);
            }
            aura.SetLevel(st.g);
            if (rebuildWings)
            {
                if (wings != null) Destroy(wings.gameObject);
                wings = Player.MakeWings(transform, st.w);
            }
            if (st.pt > 0 && pet == null) pet = RemotePet.Create(transform);
            else if (st.pt <= 0 && pet != null) { Destroy(pet.gameObject); pet = null; }

            plateText.text = (st.k == 1 ? "[PK] " : "") + (st.rb > 0 ? st.rb + "R " : "") + "Lv " + st.lv + " " + cls.Name + "  " + st.n + "\nCP " + st.cp;
            plateText.color = st.r == 1 ? new Color(1f, 0.3f, 0.25f) : st.k == 1 ? new Color(1f, 0.65f, 0.25f) : new Color(0.6f, 0.9f, 1f);
            UIB.SetBar(hpFill, st.h / 100f);
            if (st.a == 1 && anim != null) anim.Play(ModelAnimator.Anim.Attack, false, 0.5f);
        }

        void Update()
        {
            if (state == null) return;
            Vector3 delta = targetPos - transform.position;
            bool moving = delta.sqrMagnitude > 0.0025f;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, Mathf.Max(6f, delta.magnitude * 8f) * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, targetRot, 0), 12f * Time.deltaTime);

            if (anim != null)
            {
                if (state.a == 2) anim.Play(ModelAnimator.Anim.Die);
                else if (state.a == 0) anim.Play(moving ? ModelAnimator.Anim.Move : ModelAnimator.Anim.Idle);
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || plate == null) return;
            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);
            bool visible = screen.z > 0;
            plate.gameObject.SetActive(visible);
            if (!visible) return;
            var canvas = (RectTransform)plate.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
            plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0.5f);
            plate.anchoredPosition = local;
        }

        public void Remove()
        {
            if (plate != null) Destroy(plate.gameObject);
            if (pet != null) Destroy(pet.gameObject);
            Destroy(gameObject);
        }
    }

    // Another player's pet: walks behind its owner's left shoulder like our own Pet, worked out on this side
    // from the owner's synced position, so it costs nothing on the network.
    public class RemotePet : MonoBehaviour
    {
        Transform owner;
        ModelAnimator anim;
        bool isModel, walking;
        Transform visual;

        public static RemotePet Create(Transform owner)
        {
            var rp = new GameObject("Other Pet").AddComponent<RemotePet>();
            rp.owner = owner;
            rp.transform.position = rp.Slot;
            rp.transform.rotation = owner.rotation;
            rp.visual = Pet.MakeVisual(rp.transform, out rp.anim, out rp.isModel);
            return rp;
        }

        Vector3 Slot => owner.position + owner.rotation * new Vector3(-1.1f, 0, -1.1f);

        void Update()
        {
            if (owner == null) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            Vector3 to = Slot - transform.position;
            to.y = 0;
            float dist = to.magnitude;
            if (dist > 25f) { transform.position = Slot; walking = false; }
            else
            {
                if (dist > 1.2f) walking = true;
                else if (dist < 0.25f) walking = false;
                if (walking)
                {
                    float speed = Mathf.Clamp(dist * 2.5f, 2f, 9f);
                    transform.position += to / dist * Mathf.Min(dist, speed * dt);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to / dist), 10f * dt);
                }
                else
                    transform.rotation = Quaternion.Slerp(transform.rotation, owner.rotation, 4f * dt);
            }
            if (anim != null) anim.Play(walking ? ModelAnimator.Anim.Move : ModelAnimator.Anim.Idle);
            if (!isModel) visual.localPosition = Vector3.up * (0.8f + Mathf.Sin(Time.time * 4f) * 0.12f);
        }
    }
}
