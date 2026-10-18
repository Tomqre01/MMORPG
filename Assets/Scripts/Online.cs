using System;
using System.Linq;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;
using UnityEngine.UI;

namespace MuProto
{
    // Phase 2, step 1: log in to the Nakama server (server/docker-compose.yml) and keep the save there.
    // The server copy wins on login; every later local save is copied up. Offline play still works.
    public static class Online
    {
        public const int Port = 7350;
        const string ServerKey = "defaultkey";
        const string HostPref = "muproto_server_host";
        const string DevicePref = "muproto_device_id";
        const string Collection = "save";
        const string SaveKey = "player";

        public static IClient Client { get; private set; }
        public static ISession Session { get; private set; }
        public static bool IsOnline => Session != null;

        // Editor, and MuMu once Tools > Android > Connect MuMu Emulator has forwarded the port: 127.0.0.1.
        // A real phone: the PC's IPv4 address from ipconfig.
        public static string Host
        {
            get => PlayerPrefs.GetString(HostPref, "127.0.0.1");
            set { PlayerPrefs.SetString(HostPref, value); PlayerPrefs.Save(); }
        }

        static bool pushing;
        static string pendingJson;

        public static async Task Login(string host)
        {
            var client = new Client("http", host, Port, ServerKey, UnityWebRequestAdapter.Instance) { Timeout = 5 };
            var session = await client.AuthenticateDeviceAsync(DeviceId(), null, true);

            var read = await client.ReadStorageObjectsAsync(session, new IApiReadStorageObjectId[]
            {
                new StorageObjectId { Collection = Collection, Key = SaveKey, UserId = session.UserId }
            });

            Client = client;
            Session = session;
            Host = host;
            SaveSystem.Saved -= Push;
            SaveSystem.Saved += Push;

            var stored = read.Objects.FirstOrDefault();
            if (stored != null) SaveSystem.SetJson(stored.Value);
            else Push(JsonUtility.ToJson(SaveSystem.Load())); // first login: upload this device's progress
        }

        // Saves come every few seconds at most (Player.Flush), so only the newest one is queued.
        static async void Push(string json)
        {
            if (!IsOnline) return;
            if (pushing) { pendingJson = json; return; }
            pushing = true;
            try
            {
                while (json != null)
                {
                    await Client.WriteStorageObjectsAsync(Session, new IApiWriteStorageObject[]
                    {
                        new WriteStorageObject { Collection = Collection, Key = SaveKey, Value = json, PermissionRead = 1, PermissionWrite = 1 }
                    });
                    json = pendingJson;
                    pendingJson = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Online] Save upload failed: " + e.Message);
                HUD.Message("Server save failed, will retry", 2f);
            }
            finally { pushing = false; }
        }

        // ---- Ranking: each player's summary is a public storage object; anyone can list them. ----

        [Serializable]
        public class RankEntry
        {
            public string n, c;
            public int lv, rb, cp, atk, def, hp, ap;
        }

        public static async void PushRank(RankEntry e)
        {
            if (!IsOnline) return;
            try
            {
                await Client.WriteStorageObjectsAsync(Session, new IApiWriteStorageObject[]
                {
                    new WriteStorageObject { Collection = "rank", Key = "cp", Value = JsonUtility.ToJson(e), PermissionRead = 2, PermissionWrite = 1 }
                });
            }
            catch (Exception ex) { Debug.LogWarning("[Online] Rank upload failed: " + ex.Message); }
        }

        public static async Task<System.Collections.Generic.List<RankEntry>> ListRanks()
        {
            var list = await Client.ListStorageObjectsAsync(Session, "rank", 100);
            return list.Objects.Select(o => JsonUtility.FromJson<RankEntry>(o.Value)).Where(x => x != null)
                       .OrderByDescending(x => x.cp).ToList();
        }

        public static async Task SetDisplayName(string name)
        {
            if (!IsOnline) return;
            await Client.UpdateAccountAsync(Session, null, name);
        }

        static string DeviceId()
        {
            string id = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier || id.Length < 10)
            {
                id = PlayerPrefs.GetString(DevicePref, "");
                if (id == "") { id = Guid.NewGuid().ToString(); PlayerPrefs.SetString(DevicePref, id); PlayerPrefs.Save(); }
            }
            return id;
        }
    }

    // First screen: server address, Login, or Play Offline. The world is built after either.
    public class LoginScreen : MonoBehaviour
    {
        Action onDone;
        InputField hostField;
        Text status;
        Button loginButton;

        public static void Show(Action onDone)
        {
            HUD.NewCanvas("Login").AddComponent<LoginScreen>().Build(onDone);
        }

        void Build(Action done)
        {
            onDone = done;
            var center = new Vector2(0.5f, 0.5f);
            var panel = UIB.Stretch(UIB.Rect("Panel", transform, Vector2.zero, Vector2.zero, Vector2.zero));
            UIB.Image(panel, new Color(0.03f, 0.03f, 0.05f, 1f));
            UIB.Image(UIB.Rect("Glow", panel, center, new Vector2(0, 200), new Vector2(2400, 1300), center), new Color(0.7f, 0.35f, 0.12f, 0.35f), Skin.Soft).raycastTarget = false;
            var plate = UIB.Rect("Title", panel, center, new Vector2(0, 330), new Vector2(1100, 170), center);
            UIB.Image(plate, Color.white, Skin.Banner).raycastTarget = false;
            var title = Skin.Title(plate, "MU PROTO", 96, center, Vector2.zero, new Vector2(1100, 150));
            title.rectTransform.pivot = center;
            var sub = UIB.Label(panel, "an MU Origin style prototype", 30, center, new Vector2(0, 228), new Vector2(1000, 50), TextAnchor.MiddleCenter);
            sub.rectTransform.pivot = center;
            sub.color = Skin.Muted;
            Skin.Frame(panel, "Box", center, new Vector2(0, 35), new Vector2(1060, 330), center).GetComponent<Image>().raycastTarget = false;

            var server = UIB.Label(panel, "Server", 34, center, new Vector2(-410, 110), new Vector2(200, 80), TextAnchor.MiddleLeft);
            server.color = Skin.Gold;
            hostField = MakeField(panel, Online.Host, center, new Vector2(80, 110), new Vector2(720, 80));

            loginButton = UIB.Button(panel, "Login", center, new Vector2(-190, -40), new Vector2(340, 110),
                Skin.Red, Login, null, 40, center);
            UIB.Button(panel, "Play Offline", center, new Vector2(190, -40), new Vector2(340, 110),
                Skin.Slate, () => Finish("Playing offline"), null, 40, center);

            status = UIB.Label(panel, "", 32, center, new Vector2(0, -210), new Vector2(1400, 80), TextAnchor.MiddleCenter);
        }

        static InputField MakeField(Transform parent, string value, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = UIB.Rect("Server Field", parent, anchor, pos, size, anchor);
            var bg = Skin.Sliced(rt, Skin.Inset, Color.white);
            var textRt = UIB.Stretch(UIB.Rect("Text", rt, Vector2.zero, Vector2.zero, Vector2.zero));
            textRt.offsetMin = new Vector2(20, 0);
            textRt.offsetMax = new Vector2(-20, 0);
            var text = UIB.Text(textRt, "", 38, Color.white);
            text.supportRichText = false;
            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = text;
            field.contentType = InputField.ContentType.Standard;
            field.characterLimit = 64;
            field.text = value;
            return field;
        }

        async void Login()
        {
            string host = hostField.text.Trim();
            if (host == "") { status.text = "Type the server address first"; return; }
            loginButton.interactable = false;
            status.color = Color.white;
            status.text = "Connecting to " + host + " ...";
            try
            {
                await Online.Login(host);
                Finish("Logged in. Progress is saved on the server");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Online] Login failed: " + e);
                status.color = new Color(1f, 0.5f, 0.45f);
                status.text = "Can't connect: " + e.Message;
                loginButton.interactable = true;
            }
        }

        void Finish(string message)
        {
            Destroy(gameObject);
            onDone();
            HUD.Message(message, 3f);
        }
    }
}
