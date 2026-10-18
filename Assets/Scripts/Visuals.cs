using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MuProto
{
    // Enhancement glow: from +7 the gear starts to shine, +9 brighter with a pulsing light,
    // +11 lightning crackles around the body now and then, +13 it turns gold with constant lightning.
    public class Aura : MonoBehaviour
    {
        static Material glowMat;

        Color baseColor;
        int tier = -1;
        readonly List<Material> mats = new List<Material>();
        readonly Dictionary<Material, Color> baseEmission = new Dictionary<Material, Color>(); // eyes, orbs, gems keep their own glow
        Light glowLight;
        LineRenderer[] bolts;
        float boltTimer, boltShowUntil;
        Color glow;

        public static Material GlowMaterial
        {
            get
            {
                if (glowMat == null)
                {
                    glowMat = Resources.Load<Material>("Materials/Glow");
                    if (glowMat == null)
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (shader != null) glowMat = new Material(shader);
                    }
                }
                return glowMat;
            }
        }

        public static Aura Attach(Transform t, Color baseColor)
        {
            var a = t.GetComponent<Aura>();
            if (a == null) a = t.gameObject.AddComponent<Aura>();
            a.baseColor = baseColor;
            return a;
        }

        public static int TierFor(int plus) => plus >= 13 ? 4 : plus >= 11 ? 3 : plus >= 9 ? 2 : plus >= 7 ? 1 : 0;

        static readonly float[] Intensity = { 0f, 0.18f, 0.32f, 0.5f, 0.75f }; // enough to shine, not to bleach the armour colours

        void Collect()
        {
            if (mats.Count > 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer) continue;
                foreach (var m in r.materials) // per-object copies, so other players' looks stay separate
                {
                    if (m == null) continue;
                    mats.Add(m);
                    bool lit = m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor");
                    baseEmission[m] = lit ? m.GetColor("_EmissionColor") : Color.black;
                }
            }
        }

        // GearVisual repaints a gem when the gear changes: remember it as that material's own glow.
        public void SetBaseEmission(Material m, Color c)
        {
            if (mats.Count == 0) return; // not collected yet: the colour is read when it is
            baseEmission[m] = c;
            if (!mats.Contains(m)) mats.Add(m);
            ApplyGlow(m);
        }

        void ApplyGlow(Material m)
        {
            Color e = (baseEmission.TryGetValue(m, out var b) ? b : Color.black) + (tier > 0 ? glow * Intensity[tier] : Color.black);
            if (e.maxColorComponent <= 0.001f)
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            else
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", e);
            }
        }

        public void SetLevel(int plus)
        {
            int t = TierFor(plus);
            if (t == tier) return;
            tier = t;

            Collect();
            glow = tier >= 4 ? new Color(1f, 0.8f, 0.3f) : Color.Lerp(baseColor, Color.white, 0.5f);
            foreach (var m in mats)
                if (m != null) ApplyGlow(m);

            if (tier >= 1 && glowLight == null)
            {
                var go = new GameObject("Glow Light");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * 1.2f;
                glowLight = go.AddComponent<Light>();
                glowLight.type = LightType.Point;
                glowLight.shadows = LightShadows.None;
            }
            if (glowLight != null)
            {
                glowLight.enabled = tier >= 1;
                glowLight.color = glow;
                glowLight.range = 3f + tier;
            }

            if (tier >= 3 && bolts == null)
            {
                bolts = new LineRenderer[3];
                for (int i = 0; i < bolts.Length; i++)
                {
                    var go = new GameObject("Lightning");
                    go.transform.SetParent(transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = GlowMaterial;
                    lr.material.color = Color.white;
                    lr.material.SetColor("_EmissionColor", new Color(0.6f, 0.8f, 1f) * 3f);
                    lr.widthMultiplier = 0.06f;
                    lr.positionCount = 7;
                    lr.useWorldSpace = false;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.enabled = false;
                    bolts[i] = lr;
                }
            }
            if (bolts != null)
                foreach (var b in bolts) b.enabled = false;
        }

        void Update()
        {
            if (glowLight != null && glowLight.enabled)
                glowLight.intensity = (0.6f + 0.4f * Mathf.Sin(Time.time * (2f + tier))) * tier * 0.9f;

            if (bolts == null || tier < 3) return;
            boltTimer -= Time.deltaTime;
            if (boltTimer > 0)
            {
                if (Time.time > boltShowUntil) foreach (var b in bolts) b.enabled = false;
                return;
            }
            boltTimer = tier >= 4 ? 0.08f : 0.7f;   // gold: constant, otherwise a crackle now and then
            boltShowUntil = Time.time + 0.12f;
            Color c = tier >= 4 ? new Color(1f, 0.9f, 0.5f) : new Color(0.6f, 0.8f, 1f);
            foreach (var b in bolts)
            {
                b.enabled = true;
                b.material.SetColor("_EmissionColor", c * 3f);
                float a0 = Random.Range(0f, Mathf.PI * 2f);
                float a1 = a0 + Random.Range(1f, 2.5f);
                float h0 = Random.Range(0.2f, 1.8f), h1 = Random.Range(0.2f, 1.8f);
                for (int i = 0; i < b.positionCount; i++)
                {
                    float t = i / (float)(b.positionCount - 1);
                    float a = Mathf.Lerp(a0, a1, t);
                    float r = 0.55f + Random.Range(-0.12f, 0.12f);
                    b.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Lerp(h0, h1, t) + Random.Range(-0.15f, 0.15f), Mathf.Sin(a) * r));
                }
            }
        }
    }

    // A text label that stays over a point in the world (town sign, NPC names).
    public class WorldLabel : MonoBehaviour
    {
        static Transform canvas;

        Vector3 world;
        Text text;

        public static WorldLabel Create(Vector3 worldPos, string s, Color color, float height = 2.5f, int size = 30)
        {
            if (canvas == null)
            {
                var go = HUD.NewCanvas("World Labels");
                var c = go.GetComponent<Canvas>();
                c.sortingOrder = -2;
                Destroy(go.GetComponent<GraphicRaycaster>());
                canvas = go.transform;
            }
            var rt = UIB.Rect("Label", canvas, Vector2.zero, Vector2.zero, new Vector2(600, 60), new Vector2(0.5f, 0.5f));
            var wl = rt.gameObject.AddComponent<WorldLabel>();
            wl.world = worldPos + Vector3.up * height;
            wl.text = UIB.Text(rt, s, size, color, TextAnchor.MiddleCenter);
            return wl;
        }

        public void SetText(string s) => text.text = s;
        public void Move(Vector3 worldPos, float height) => world = worldPos + Vector3.up * height;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            text.enabled = sp.z > 0 && sp.z < 60f; // labels on other maps are far away
            transform.position = new Vector3(sp.x, sp.y, 0);
        }
    }
}
