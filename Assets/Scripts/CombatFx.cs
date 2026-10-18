using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MuProto
{
    // Hit feel: glowing slash trails, sparks, crescent arcs, shockwaves, hit-stop and camera shake.
    // Everything is drawn with one additive particle material (Resources/Materials/FxAdditive,
    // made by the editor script FxAssets) and textures generated here, so there is nothing to import.
    public static class Fx
    {
        static Material template;
        static Texture2D band, star;
        static MonoBehaviour host;
        static float stopUntil;

        public static Material NewMaterial(Texture tex, Color tint)
        {
            if (template == null)
            {
                template = Resources.Load<Material>("Materials/FxAdditive");
                if (template == null) template = MakeAdditive();
            }
            var m = new Material(template);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            m.color = tint;
            return m;
        }

        // Also used by the editor to save the material asset (so builds keep the shader).
        public static Material MakeAdditive()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = "FxAdditive" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f); // additive
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        // Across a slash (v): faint at the hilt, a hot thin edge at the tip.
        public static Texture2D Band
        {
            get
            {
                if (band != null) return band;
                band = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FxBand" };
                for (int y = 0; y < 64; y++)
                {
                    float v = y / 63f;
                    float a = Mathf.Max(Mathf.Pow(v, 2.2f) * 0.75f, Mathf.Exp(-(1f - v) * 14f));
                    float w = Mathf.Exp(-(1f - v) * 22f); // white core at the edge
                    var c = new Color(Mathf.Lerp(0.75f, 1f, w), Mathf.Lerp(0.85f, 1f, w), 1f, a);
                    for (int x = 0; x < 4; x++) band.SetPixel(x, y, c);
                }
                band.Apply();
                return band;
            }
        }

        // A soft glow with four sharp rays.
        public static Texture2D Star
        {
            get
            {
                if (star != null) return star;
                const int n = 64;
                star = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FxStar" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float glow = Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r);
                        float rays = Mathf.Clamp01(1f - r) * (Mathf.Exp(-Mathf.Abs(dx) * 22f) + Mathf.Exp(-Mathf.Abs(dy) * 22f));
                        float a = Mathf.Clamp01(glow * 0.9f + rays);
                        star.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                star.Apply();
                return star;
            }
        }

        static MonoBehaviour Host
        {
            get
            {
                if (host == null)
                {
                    var go = new GameObject("Fx Host");
                    Object.DontDestroyOnLoad(go);
                    host = go.AddComponent<FxHost>();
                }
                return host;
            }
        }

        class FxHost : MonoBehaviour { }

        // Freeze the action for a moment when a blow lands (real time, so it never sticks).
        public static void HitStop(float seconds, float scale = 0.06f)
        {
            float until = Time.unscaledTime + seconds;
            if (until <= stopUntil) return;
            bool running = stopUntil > Time.unscaledTime;
            stopUntil = until;
            if (!running) Host.StartCoroutine(Stop(scale));
        }

        static IEnumerator Stop(float scale)
        {
            Time.timeScale = scale;
            while (Time.unscaledTime < stopUntil) yield return null;
            Time.timeScale = 1f;
        }

        // A flash with rays where a hit lands.
        public static void Spark(Vector3 pos, Color color, float size = 1.3f)
        {
            var q = Quad("Spark", Star, Color.Lerp(color, Color.white, 0.35f) * 1.6f);
            q.transform.position = pos;
            var s = q.AddComponent<FxSprite>();
            s.size = size;
            s.life = 0.18f;
            s.spin = Random.Range(0f, 90f);
        }

        // A glowing crescent: radius around `center`, swept from `startDeg` through `sweepDeg`
        // in the plane given by `rot` (local XZ). Reveals along the sweep, then fades.
        public static void Arc(Vector3 center, Quaternion rot, float radius, float startDeg, float sweepDeg, Color color, float width = 0.9f, float life = 0.28f)
        {
            var go = new GameObject("Slash Arc");
            go.transform.SetPositionAndRotation(center, rot);
            var a = go.AddComponent<FxArc>();
            a.Init(radius, startDeg, sweepDeg, width, color, life, false);
        }

        // A ring on the ground that races outwards.
        public static void Shockwave(Vector3 pos, float radius, Color color)
        {
            var go = new GameObject("Shockwave");
            go.transform.position = new Vector3(pos.x, pos.y + 0.08f, pos.z);
            go.AddComponent<FxArc>().Init(radius, 0f, 360f, 0.8f, color, 0.35f, true);
            Spark(pos + Vector3.up * 0.3f, color, Mathf.Min(radius * 0.8f, 3f));
        }

        static GameObject Quad(string name, Texture tex, Color tint)
        {
            var go = new GameObject(name);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = QuadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = NewMaterial(tex, tint);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        static Mesh quad;
        static Mesh QuadMesh
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "FxQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                quad.RecalculateBounds();
                return quad;
            }
        }

        public static Color TrailColor(string classId) =>
            classId == "DarkWizard" ? new Color(0.65f, 0.45f, 1f) : classId == "Elf" ? new Color(0.45f, 1f, 0.6f) : new Color(0.45f, 0.75f, 1f);
    }

    // A camera-facing flash that pops up and fades.
    public class FxSprite : MonoBehaviour
    {
        public float size = 1f, life = 0.2f, spin;
        float t;
        Material mat;
        Color baseColor;

        void Start()
        {
            mat = GetComponent<MeshRenderer>().sharedMaterial; // already its own copy (Fx.NewMaterial)
            baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
        }

        void LateUpdate()
        {
            t += Time.deltaTime;
            float k = t / life;
            if (k >= 1f) { Destroy(gameObject); return; }
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation * Quaternion.Euler(0, 0, spin + k * 40f);
            transform.localScale = Vector3.one * size * Mathf.Lerp(0.5f, 1.15f, Mathf.Sqrt(k));
            var c = baseColor;
            c.a = baseColor.a * (1f - k * k);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else mat.color = c;
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }

    // A crescent or ring mesh (local XZ plane). Outer edge bright.
    public class FxArc : MonoBehaviour
    {
        float radius, start, sweep, width, life, t;
        bool expand;
        Color color;
        Mesh mesh;
        Material mat;
        const int Segs = 40;

        public void Init(float radius, float startDeg, float sweepDeg, float width, Color color, float life, bool expand)
        {
            this.radius = radius;
            start = startDeg;
            sweep = sweepDeg;
            this.width = width;
            this.color = color;
            this.life = life;
            this.expand = expand;
            mesh = new Mesh { name = "FxArc" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mat = Fx.NewMaterial(Fx.Band, color * 1.6f);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Build(0f);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = t / life;
            if (k >= 1f) { Destroy(gameObject); return; }
            Build(k);
        }

        void Build(float k)
        {
            float r = expand ? Mathf.Lerp(radius * 0.15f, radius, Mathf.Sqrt(k)) : radius;
            float shown = expand ? 1f : Mathf.Clamp01(k / 0.35f);   // the slash draws itself, then fades
            float fade = expand ? 1f - k : (k < 0.35f ? 1f : 1f - (k - 0.35f) / 0.65f);
            float w = expand ? width * (1f - 0.5f * k) : width * Mathf.Lerp(1f, 0.6f, k);
            var verts = new Vector3[(Segs + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var cols = new Color[verts.Length];
            var tris = new int[Segs * 6];
            for (int i = 0; i <= Segs; i++)
            {
                float u = i / (float)Segs;
                float a = (start + sweep * shown * u) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                // crescent: thick in the middle of what is shown, thin at both ends (rings stay even)
                float thick = expand ? 1f : Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)) * 0.85f + 0.15f;
                verts[i * 2] = dir * Mathf.Max(0.05f, r - w * thick);
                verts[i * 2 + 1] = dir * r;
                uvs[i * 2] = new Vector2(u, 0);
                uvs[i * 2 + 1] = new Vector2(u, 1);
                float tail = expand ? 1f : Mathf.Lerp(0.25f, 1f, u); // the leading end is brightest
                cols[i * 2] = cols[i * 2 + 1] = new Color(1, 1, 1, fade * tail);
                if (i < Segs)
                {
                    int v = i * 2, ti = i * 6;
                    tris[ti] = v; tris[ti + 1] = v + 1; tris[ti + 2] = v + 3;
                    tris[ti + 3] = v; tris[ti + 4] = v + 3; tris[ti + 5] = v + 2;
                }
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (mesh != null) Destroy(mesh);
        }
    }

    // A ribbon that follows the weapon between the TrailBase and TrailTip bones while emitting,
    // smoothed with Catmull-Rom so fast swings still draw a clean curve.
    public class WeaponTrail : MonoBehaviour
    {
        const float Life = 0.2f;
        const int Sub = 4;

        Transform a, b;
        readonly List<(Vector3 a, Vector3 b, float t)> raw = new List<(Vector3, Vector3, float)>();
        Mesh mesh;
        GameObject ribbon;
        Material mat;
        float emitUntil;

        public static WeaponTrail Attach(Transform model, Color color)
        {
            Transform ta = null, tb = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "TrailBase") ta = t;
                else if (t.name == "TrailTip") tb = t;
            }
            if (ta == null || tb == null) return null;
            var w = model.gameObject.AddComponent<WeaponTrail>();
            w.a = ta;
            w.b = tb;
            w.ribbon = new GameObject("Weapon Trail");
            w.mesh = new Mesh { name = "WeaponTrail" };
            w.mesh.MarkDynamic();
            w.ribbon.AddComponent<MeshFilter>().sharedMesh = w.mesh;
            var mr = w.ribbon.AddComponent<MeshRenderer>();
            w.mat = Fx.NewMaterial(Fx.Band, color * 1.8f);
            mr.sharedMaterial = w.mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return w;
        }

        public void Emit(float seconds) => emitUntil = Mathf.Max(emitUntil, Time.time + seconds);

        void LateUpdate()
        {
            float now = Time.time;
            if (now < emitUntil && a != null && b != null) raw.Add((a.position, b.position, now));
            while (raw.Count > 0 && now - raw[0].t > Life) raw.RemoveAt(0);
            Build(now);
        }

        void Build(float now)
        {
            mesh.Clear();
            if (raw.Count < 2) return;
            var pa = new List<Vector3>();
            var pb = new List<Vector3>();
            var pt = new List<float>();
            for (int i = 0; i < raw.Count - 1; i++)
            {
                var p0 = raw[Mathf.Max(0, i - 1)];
                var p1 = raw[i];
                var p2 = raw[i + 1];
                var p3 = raw[Mathf.Min(raw.Count - 1, i + 2)];
                for (int s = 0; s < Sub; s++)
                {
                    float u = s / (float)Sub;
                    pa.Add(CatmullRom(p0.a, p1.a, p2.a, p3.a, u));
                    pb.Add(CatmullRom(p0.b, p1.b, p2.b, p3.b, u));
                    pt.Add(Mathf.Lerp(p1.t, p2.t, u));
                }
            }
            var last = raw[raw.Count - 1];
            pa.Add(last.a);
            pb.Add(last.b);
            pt.Add(last.t);

            int n = pa.Count;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var cols = new Color[n * 2];
            var tris = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                float age = Mathf.Clamp01((now - pt[i]) / Life);
                float alpha = (1f - age) * (1f - age);
                verts[i * 2] = pa[i];
                verts[i * 2 + 1] = pb[i];
                uvs[i * 2] = new Vector2(age, 0);
                uvs[i * 2 + 1] = new Vector2(age, 1);
                cols[i * 2] = cols[i * 2 + 1] = new Color(1, 1, 1, alpha);
                if (i < n - 1)
                {
                    int v = i * 2, ti = i * 6;
                    tris[ti] = v; tris[ti + 1] = v + 1; tris[ti + 2] = v + 3;
                    tris[ti + 3] = v; tris[ti + 4] = v + 3; tris[ti + 5] = v + 2;
                }
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void OnDestroy()
        {
            if (ribbon != null) Destroy(ribbon);
            if (mat != null) Destroy(mat);
            if (mesh != null) Destroy(mesh);
        }
    }
}
