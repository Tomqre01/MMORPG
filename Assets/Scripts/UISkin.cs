using UnityEngine;
using UnityEngine.UI;

namespace MuProto
{
    // MU Origin look built entirely in code: dark lacquered panels with gold trim, glossy orbs with
    // gold rings, gradient bars and title banners. Every sprite is drawn into a small texture once
    // and 9-sliced, so the prototype still needs no art files.
    public static class Skin
    {
        public static readonly Color Gold = new Color(0.97f, 0.82f, 0.48f);
        public static readonly Color GoldDark = new Color(0.55f, 0.40f, 0.18f);
        public static readonly Color Cream = new Color(0.96f, 0.92f, 0.82f);
        public static readonly Color Muted = new Color(0.72f, 0.68f, 0.60f);
        public static readonly Color Red = new Color(0.62f, 0.12f, 0.10f);
        public static readonly Color Green = new Color(0.20f, 0.52f, 0.24f);
        public static readonly Color Blue = new Color(0.18f, 0.32f, 0.58f);
        public static readonly Color Purple = new Color(0.42f, 0.20f, 0.55f);
        public static readonly Color Brown = new Color(0.50f, 0.36f, 0.16f);
        public static readonly Color Slate = new Color(0.26f, 0.27f, 0.32f);

        static Sprite panel, inset, button, buttonEdge, orb, ring, barBack, barFill, banner, soft, diamond, tab;

        public static Sprite Panel => panel ??= MakePanel();
        public static Sprite Inset => inset ??= MakeInset();
        public static Sprite Button => button ??= MakeButton();
        public static Sprite ButtonEdge => buttonEdge ??= MakeButtonEdge();
        public static Sprite Orb => orb ??= MakeOrb();
        public static Sprite Ring => ring ??= MakeRing();
        public static Sprite BarBack => barBack ??= MakeBarBack();
        public static Sprite BarFill => barFill ??= MakeBarFill();
        public static Sprite Banner => banner ??= MakeBanner();
        public static Sprite Soft => soft ??= MakeSoft();
        public static Sprite Diamond => diamond ??= MakeDiamond();
        public static Sprite Tab => tab ??= MakeTab();

        // ---- drawing helpers ----

        delegate Color Pixel(float x, float y, float w, float h);

        static Sprite Draw(int w, int h, int slice, Pixel f)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x + 0.5f, y + 0.5f, w, h);
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(slice, slice, slice, slice));
        }

        // Signed distance to a rounded rectangle filling the texture (negative inside).
        static float RoundRect(float x, float y, float w, float h, float r)
        {
            float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r);
            float qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
            float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        static float Band(float d, float from, float to) => Mathf.Clamp01(Mathf.Min(d - from + 0.5f, to - d + 0.5f));
        static Color A(Color c, float a) { c.a *= a; return c; }
        static Color Over(Color top, Color bottom)
        {
            float a = top.a + bottom.a * (1 - top.a);
            if (a <= 0) return Color.clear;
            var c = (top * top.a + bottom * bottom.a * (1 - top.a)) / a;
            c.a = a;
            return c;
        }

        static Sprite MakePanel() => Draw(64, 64, 22, (x, y, w, h) =>
        {
            float d = RoundRect(x, y, w, h, 9);
            if (d > 0.5f) return Color.clear;
            float t = y / h;
            var body = Color.Lerp(new Color(0.045f, 0.045f, 0.06f), new Color(0.12f, 0.10f, 0.09f), t);
            var c = A(body, 0.96f);
            c = Over(A(new Color(0.03f, 0.02f, 0.02f), Band(d, -6, -4)), c);
            c = Over(A(Gold, 0.35f * Band(d, -8, -7)), c);
            var trim = Color.Lerp(GoldDark, Gold, Mathf.Clamp01(t * 1.2f));
            c = Over(A(trim, Band(d, -3.5f, -0.5f)), c);
            c.a *= Mathf.Clamp01(0.5f - d);
            return c;
        });

        static Sprite MakeInset() => Draw(32, 32, 10, (x, y, w, h) =>
        {
            float d = RoundRect(x, y, w, h, 6);
            if (d > 0.5f) return Color.clear;
            var c = new Color(0, 0, 0, 0.45f);
            c = Over(A(GoldDark, 0.7f * Band(d, -1.5f, -0.5f)), c);
            c.a *= Mathf.Clamp01(0.5f - d);
            return c;
        });

        // Grey-to-white gloss, tinted by the Image colour.
        static Sprite MakeButton() => Draw(48, 48, 16, (x, y, w, h) =>
        {
            float d = RoundRect(x, y, w, h, 10);
            if (d > 0.5f) return Color.clear;
            float t = y / h;
            float v = Mathf.Lerp(0.55f, 1f, t);
            if (t > 0.55f) v = Mathf.Lerp(v, 1.15f, (t - 0.55f) * 0.6f);
            var c = new Color(v, v, v, 1);
            c = Over(A(new Color(0.15f, 0.15f, 0.15f), Band(d, -3.5f, -2.5f) * 0.6f), c);
            c.a *= Mathf.Clamp01(0.5f - d);
            return c;
        });

        static Sprite MakeButtonEdge() => Draw(48, 48, 16, (x, y, w, h) =>
        {
            float d = RoundRect(x, y, w, h, 10);
            float t = y / h;
            var c = A(Color.Lerp(GoldDark, Gold, t), Band(d, -2.5f, -0.5f));
            c = Over(A(Color.white, 0.25f * Band(d, -4, -3) * Mathf.Clamp01((t - 0.6f) * 3)), c);
            return c;
        });

        static Sprite MakeOrb() => Draw(128, 128, 0, (x, y, w, h) =>
        {
            float r = w / 2f;
            float dx = x - r, dy = y - r, dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist > r) return Color.clear;
            float k = dist / r;
            float v = Mathf.Lerp(1.05f, 0.45f, k * k);
            // glossy highlight in the upper half
            float hl = Mathf.Clamp01(1 - new Vector2(dx / (r * 0.75f), (dy - r * 0.42f) / (r * 0.42f)).magnitude);
            v += hl * 0.35f;
            return new Color(v, v, v, Mathf.Clamp01(r - dist));
        });

        static Sprite MakeRing() => Draw(128, 128, 0, (x, y, w, h) =>
        {
            float r = w / 2f;
            float dx = x - r, dy = y - r, dist = Mathf.Sqrt(dx * dx + dy * dy);
            float d = dist - r; // negative inside
            float t = y / h;
            var c = A(Color.Lerp(GoldDark, Gold, t), Band(d, -6f, -0.5f));
            c = Over(A(new Color(1f, 0.95f, 0.75f), 0.6f * Band(d, -4.5f, -3.5f) * Mathf.Clamp01(t * 1.5f - 0.3f)), c);
            c = Over(A(new Color(0.05f, 0.03f, 0.01f), Band(d, -8.5f, -6f)), c);
            return c;
        });

        static Sprite MakeBarBack() => Draw(32, 16, 7, (x, y, w, h) =>
        {
            float d = RoundRect(x, y, w, h, 7);
            if (d > 0.5f) return Color.clear;
            var c = new Color(0.02f, 0.02f, 0.03f, 0.85f);
            c = Over(A(GoldDark, Band(d, -1.5f, -0.5f)), c);
            c.a *= Mathf.Clamp01(0.5f - d);
            return c;
        });

        static Sprite MakeBarFill() => Draw(32, 16, 6, (x, y, w, h) =>
        {
            float d = RoundRect(x, y, w, h, 6);
            if (d > 0.5f) return Color.clear;
            float t = y / h;
            float v = Mathf.Lerp(0.55f, 1f, t);
            if (t > 0.62f && t < 0.82f) v = 1.25f;
            return new Color(v, v, v, Mathf.Clamp01(0.5f - d));
        });

        // Title plate: dark red lacquer fading out at both ends, gold rules top and bottom.
        static Sprite MakeBanner() => Draw(256, 48, 0, (x, y, w, h) =>
        {
            float u = x / w, t = y / h;
            float fade = 1 - Mathf.Pow(Mathf.Abs(u * 2 - 1), 3);
            var body = Color.Lerp(new Color(0.22f, 0.05f, 0.04f), new Color(0.42f, 0.12f, 0.06f), Mathf.Sin(t * Mathf.PI));
            var c = A(body, 0.95f * fade);
            float rule = Mathf.Max(Band(y, 1, 3), Band(y, h - 3, h - 1));
            c = Over(A(Gold, rule * fade), c);
            return c;
        });

        static Sprite MakeSoft() => Draw(64, 64, 0, (x, y, w, h) =>
        {
            float r = w / 2f;
            float k = Mathf.Clamp01(new Vector2(x - r, y - r).magnitude / r);
            return new Color(1, 1, 1, (1 - k) * (1 - k));
        });

        static Sprite MakeDiamond() => Draw(64, 64, 0, (x, y, w, h) =>
        {
            float r = w / 2f;
            float d = (Mathf.Abs(x - r) + Mathf.Abs(y - r)) - r + 2;
            if (d > 0.5f) return Color.clear;
            float v = Mathf.Lerp(0.6f, 1.15f, y / h) + (x < r ? 0.1f : 0);
            return new Color(v, v, v, Mathf.Clamp01(0.5f - d));
        });

        static Sprite MakeTab() => Draw(48, 48, 16, (x, y, w, h) =>
        {
            float d = RoundRect(x, y + 24, w, h + 24, 10); // rounded top corners only
            if (d > 0.5f) return Color.clear;
            float t = y / h;
            float v = Mathf.Lerp(0.6f, 1f, t);
            var c = new Color(v, v, v, 1);
            c.a *= Mathf.Clamp01(0.5f - d);
            return c;
        });

        // ---- building blocks ----

        public static Image Sliced(RectTransform rt, Sprite s, Color c)
        {
            var img = UIB.Image(rt, c, s);
            img.type = Image.Type.Sliced;
            return img;
        }

        public static RectTransform Frame(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var rt = UIB.Rect(name, parent, anchor, pos, size, pivot);
            Sliced(rt, Panel, Color.white);
            return rt;
        }

        public static Image Child(Transform parent, string name, Sprite s, Color c, bool sliced = false)
        {
            var img = UIB.Image(UIB.Stretch(UIB.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero)), c, s);
            if (sliced) img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        public static Text Title(Transform parent, string s, int size, Vector2 anchor, Vector2 pos, Vector2 sz, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = UIB.Label(parent, s, size, anchor, pos, sz, align);
            t.color = Gold;
            t.fontStyle = FontStyle.Bold;
            return t;
        }

        // A title plate centred at the top of a window.
        public static Text Header(RectTransform window, string title)
        {
            var plate = UIB.Rect("Header", window, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(Mathf.Min(760, window.sizeDelta.x * 0.7f), 70), new Vector2(0.5f, 1));
            UIB.Image(plate, Color.white, Banner).raycastTarget = false;
            var t = Title(plate, title, 38, new Vector2(0.5f, 0.5f), Vector2.zero, plate.sizeDelta);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return t;
        }

        // Round red X in the window's top-right corner.
        public static Button CloseX(RectTransform window, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIB.Button(window, "X", new Vector2(1, 1), new Vector2(18, 18), new Vector2(76, 76), new Color(0.75f, 0.18f, 0.12f), onClick, UIB.Circle, 34, new Vector2(1, 1));
            b.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            return b;
        }

        public static readonly System.Collections.Generic.List<GameObject> Windows = new System.Collections.Generic.List<GameObject>();

        // Full-screen dimmer (tap outside closes) with a framed window on top.
        // Returns the root (activate / deactivate it) and the window rect.
        public static (GameObject root, RectTransform window) Window(Transform canvas, string name, string title, Vector2 size, UnityEngine.Events.UnityAction close)
        {
            var root = UIB.Stretch(UIB.Rect(name, canvas, Vector2.zero, Vector2.zero, Vector2.zero));
            var dim = UIB.Stretch(UIB.Rect("Dim", root, Vector2.zero, Vector2.zero, Vector2.zero));
            UIB.Image(dim, new Color(0, 0, 0, 0.55f));
            dim.gameObject.AddComponent<Button>().onClick.AddListener(close);
            var c = new Vector2(0.5f, 0.5f);
            var win = Frame(root, "Window", c, Vector2.zero, size, c);
            Header(win, title);
            CloseX(win, close);
            Windows.Add(root.gameObject);
            return (root.gameObject, win);
        }

        // A glossy round icon with a gold ring, a short glyph, a caption underneath and a red
        // notice dot (returned so callers can switch it on when something is waiting).
        public static (Button button, GameObject badge) Icon(Transform parent, string glyph, string caption, Color color, Vector2 anchor, Vector2 pos, float size, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIB.Button(parent, glyph, anchor, pos, new Vector2(size, size), color, onClick, UIB.Circle, Mathf.RoundToInt(size * 0.36f), new Vector2(0.5f, 0.5f));
            var gt = b.GetComponentInChildren<Text>();
            gt.fontStyle = FontStyle.Bold;
            gt.color = Cream;
            if (!string.IsNullOrEmpty(caption))
            {
                var cap = UIB.Label(b.transform, caption, Mathf.RoundToInt(Mathf.Clamp(size * 0.24f, 17, 24)), new Vector2(0.5f, 0), new Vector2(0, -size * 0.06f), new Vector2(size * 1.7f, 28), TextAnchor.MiddleCenter);
                cap.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                cap.color = Cream;
            }
            var dot = UIB.Rect("Badge", b.transform, new Vector2(1, 1), new Vector2(-size * 0.12f, -size * 0.12f), new Vector2(size * 0.28f, size * 0.28f), new Vector2(0.5f, 0.5f));
            UIB.Image(dot, new Color(1f, 0.15f, 0.1f), UIB.Circle).raycastTarget = false;
            dot.gameObject.SetActive(false);
            return (b, dot.gameObject);
        }

        // Small rounded pill with an icon dot and a value, used for currencies.
        public static Text Pill(Transform parent, Sprite icon, Color iconColor, Vector2 anchor, Vector2 pos, float width)
        {
            var rt = UIB.Rect("Pill", parent, anchor, pos, new Vector2(width, 44));
            Sliced(rt, BarBack, Color.white).raycastTarget = false;
            var ic = UIB.Rect("Icon", rt, new Vector2(0, 0.5f), new Vector2(-6, 0), new Vector2(46, 46), new Vector2(0, 0.5f));
            UIB.Image(ic, iconColor, icon).raycastTarget = false;
            var t = UIB.Label(rt, "", 24, new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(width - 56, 40), TextAnchor.MiddleRight);
            t.rectTransform.pivot = new Vector2(0, 0.5f);
            t.color = Cream;
            return t;
        }
    }
}
