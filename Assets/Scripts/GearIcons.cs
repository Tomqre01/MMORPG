using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MuProto
{
    // Item icons for the Backpack, photographed from the same 3D pieces the hero wears:
    // a spare hero model stands far above the world in its rest pose, shows one piece at a time,
    // and a small camera takes its picture. Rings and necklaces (not worn on the model) are drawn.
    public static class GearIcons
    {
        const int Layer = 29, Size = 128;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, GearVisual> models = new Dictionary<string, GearVisual>();
        static Transform stage;
        static Camera cam;
        static RenderTexture rt;

        public static Texture Get(Item it)
        {
            if (it == null || it.IsEmpty) return null;
            int tier = Mathf.Clamp(it.tier, 1, GameData.MaxTier);
            if (it.slot == Slot.Ring || it.slot == Slot.Necklace) return Jewel(it.slot, tier, (int)it.rarity);
            string cls = string.IsNullOrEmpty(it.classId) ? Player.Instance.Class.Id : it.classId;
            string key = cls + "|" + it.slot + "|" + tier + "|" + (int)it.rarity;
            if (cache.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = Photograph(cls, it.slot, tier, (int)it.rarity);
            if (tex != null) cache[key] = tex;
            return tex;
        }

        static void Setup()
        {
            if (stage != null) return;
            stage = new GameObject("Gear Icon Stage").transform;
            stage.position = new Vector3(600f, 700f, 0f);
            Object.DontDestroyOnLoad(stage.gameObject);
            cam = new GameObject("Icon Camera").AddComponent<Camera>();
            cam.transform.SetParent(stage, false);
            cam.enabled = false;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 22f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Gear Icon", antiAliasing = 4 };
            cam.targetTexture = rt;
            var key = new GameObject("Icon Light").AddComponent<Light>();
            key.transform.SetParent(stage, false);
            key.transform.localPosition = new Vector3(1.2f, 2.4f, 2.5f);
            key.type = LightType.Point;
            key.range = 8f;
            key.intensity = 2.5f;
            key.cullingMask = 1 << Layer;
            foreach (var c in Camera.allCameras) if (c != cam) c.cullingMask &= ~(1 << Layer);
        }

        static GearVisual Model(string cls)
        {
            if (models.TryGetValue(cls, out var g) && g != null) return g;
            Setup();
            var root = new GameObject(cls + " Icon Model").transform;
            root.SetParent(stage, false);
            var model = ModelLibrary.Spawn(cls, root, 1.9f, out var anim, false);
            if (model == null) return null;
            if (anim != null) Object.Destroy(anim);                      // stay in the rest pose
            foreach (var a in model.GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            g = model.GetComponent<GearVisual>();
            models[cls] = g;
            return g;
        }

        static Texture2D Photograph(string cls, Slot slot, int tier, int rarity)
        {
            var g = Model(cls);
            if (g == null) return null;
            var piece = g.ShowOnly(slot, tier, rarity);
            if (piece == null) return null;
            Bounds b = piece.bounds;
            // from the front at a slight angle (blades are flat across X, so this shows their face)
            Vector3 dir = slot == Slot.Weapon ? new Vector3(0.25f, 0.15f, 1f) : new Vector3(0.45f, 0.3f, 1f);
            float extent = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            float dist = extent / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            cam.transform.position = b.center + dir.normalized * dist;
            cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position);
            Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Icon " + cls + " " + slot + tier };
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            piece.enabled = false;
            return tex;
        }

        static void Render()
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        // ---- rings and necklaces, drawn ----

        static Texture2D Jewel(Slot slot, int tier, int rarity)
        {
            string key = slot + "|" + tier + "|" + rarity;
            if (cache.TryGetValue(key, out var tex) && tex != null) return tex;
            var metal = Color.Lerp(new Color(0.8f, 0.55f, 0.3f), new Color(1f, 0.9f, 0.55f), (tier - 1) / 5f);
            Color[] gems = { new Color(0.9f, 0.2f, 0.25f), new Color(0.3f, 0.6f, 1f), new Color(0.35f, 1f, 0.5f),
                             new Color(0.75f, 0.35f, 1f), new Color(1f, 0.9f, 0.4f), new Color(1f, 0.5f, 0.15f) };
            var gem = gems[tier - 1];
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Icon " + key };
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f, v = (y + 0.5f) / Size * 2f - 1f;
                    Color c = Color.clear;
                    if (slot == Slot.Ring)
                    {
                        float r = Mathf.Sqrt(u * u + (v + 0.12f) * (v + 0.12f) / 0.6f);
                        float band = 1f - Mathf.Abs(r - 0.55f) / 0.09f;
                        if (band > 0f) c = Shade(metal, band, u, v);
                        float g = 1f - Mathf.Sqrt(u * u + (v - 0.42f) * (v - 0.42f)) / 0.2f;
                        if (g > 0f) c = Shade(gem, g, u, v);
                    }
                    else
                    {
                        float arc = Mathf.Abs(Mathf.Sqrt(u * u + (v - 0.9f) * (v - 0.9f)) - 0.95f);
                        bool chain = arc < 0.05f && v > -0.05f && Mathf.Repeat(Mathf.Atan2(v - 0.9f, u) * 12f, 1f) < 0.7f;
                        if (chain) c = Shade(metal, 1f - arc / 0.05f, u, v);
                        float g = 1f - (Mathf.Abs(u) + Mathf.Abs(v + 0.35f) * 0.75f) / 0.32f;
                        if (g > 0f) c = Shade(gem, g, u, v);
                        float setting = 1f - Mathf.Abs((Mathf.Abs(u) + Mathf.Abs(v + 0.35f) * 0.75f) - 0.34f) / 0.05f;
                        if (setting > 0f && g <= 0f) c = Shade(metal, setting, u, v);
                    }
                    if (rarity > 0 && c.a > 0f) c = Color.Lerp(c, Color.white, 0.12f * rarity);
                    px[y * Size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            cache[key] = tex;
            return tex;
        }

        static Color Shade(Color baseColor, float k, float u, float v)
        {
            float light = Mathf.Clamp01(0.55f + 0.45f * k - 0.25f * u + 0.25f * v);
            var c = baseColor * light;
            c.a = Mathf.Clamp01(k * 3f);
            return c;
        }
    }
}
