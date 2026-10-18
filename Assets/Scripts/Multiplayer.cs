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
        public int lv, w;  // level, wings level
        public float x, z, ry;
        public int a;      // 0 idle, 1 attack, 2 dead
    }

    // Phase 2, step 2: everyone who logs in joins one shared relayed match named "world"
    // and sends their position; other players appear as RemotePlayer.
    public class WorldSync : MonoBehaviour
    {
        const string WorldName = "world";
        const long OpState = 1;
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
            if (s.OpCode != OpState || s.UserPresence == null || match == null || s.UserPresence.SessionId == match.Self.SessionId) return;
            PlayerState st;
            try { st = JsonUtility.FromJson<PlayerState>(Encoding.UTF8.GetString(s.State)); }
            catch { return; }
            if (st == null) return;
            string id = s.UserPresence.SessionId;
            if (!remotes.TryGetValue(id, out var rp) || rp == null)
            {
                Debug.Log("[Online] Player appeared: " + st.n + " Lv " + st.lv);
                rp = RemotePlayer.Create(st, nameCanvas.transform);
                remotes[id] = rp;
            }
            rp.Apply(st);
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
                n = Online.Session.Username,
                c = p.Class.Id,
                lv = p.Data.level,
                w = p.Data.wingsLevel,
                x = p.transform.position.x,
                z = p.transform.position.z,
                ry = p.transform.eulerAngles.y,
                a = p.IsDead ? 2 : p.InAction ? 1 : 0,
            };
            if (last != null && SameAs(last, st) && Time.time - lastSent < Heartbeat) return;
            last = st;
            lastSent = Time.time;
            socket.SendMatchStateAsync(match.Id, OpState, JsonUtility.ToJson(st));
        }

        static bool SameAs(PlayerState a, PlayerState b) =>
            a.c == b.c && a.lv == b.lv && a.w == b.w && a.a == b.a &&
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
        RectTransform plate;
        Text plateText;
        PlayerState state;
        Vector3 targetPos;
        float targetRot;

        public static RemotePlayer Create(PlayerState st, Transform nameCanvas)
        {
            var rp = new GameObject("Other Player").AddComponent<RemotePlayer>();
            rp.transform.position = rp.targetPos = new Vector3(st.x, 0, st.z);
            rp.transform.rotation = Quaternion.Euler(0, st.ry, 0);
            rp.plate = UIB.Rect("Name", nameCanvas, Vector2.zero, Vector2.zero, new Vector2(400, 50), new Vector2(0.5f, 0));
            rp.plateText = UIB.Text(rp.plate, "", 28, new Color(0.6f, 0.9f, 1f), TextAnchor.MiddleCenter);
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

            if (rebuildBody)
            {
                if (body != null) Destroy(body.gameObject);
                body = Player.MakeBody(transform, Classes.Get(st.c), out anim);
            }
            if (rebuildWings)
            {
                if (wings != null) Destroy(wings.gameObject);
                wings = Player.MakeWings(transform, st.w);
            }
            plateText.text = "Lv " + st.lv + " " + Classes.Get(st.c).Name + "  " + st.n;
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
            Destroy(gameObject);
        }
    }
}
