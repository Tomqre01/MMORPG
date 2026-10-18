using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MuProto
{
    // Small helpers for building uGUI from code, so the prototype needs no prefabs or scene setup.
    public static class UIB
    {
        static Font font;
        static Sprite circle;

        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        public static Sprite Circle => circle != null ? circle : (circle = MakeCircle(128));

        static Sprite MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // Anchors to a point of the parent; by default the pivot matches the anchor so
        // positions are measured from that corner.
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Image(RectTransform rt, Color color, Sprite sprite = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            return img;
        }

        public static Text Text(RectTransform rt, string s, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = s;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.8f);
            return t;
        }

        public static Text Label(Transform parent, string s, int size, Vector2 anchor, Vector2 pos, Vector2 sz, TextAnchor align = TextAnchor.MiddleLeft)
            => Text(Rect("Label", parent, anchor, pos, sz), s, size, Color.white, align);

        public static Button Button(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, UnityAction onClick, Sprite sprite = null, int fontSize = 30, Vector2? pivot = null)
        {
            var rt = Rect(label + " Button", parent, anchor, pos, size, pivot);
            Image img;
            if (sprite == null)
            {
                img = Skin.Sliced(rt, Skin.Button, Brighten(bg));
                Skin.Child(rt, "Edge", Skin.ButtonEdge, Color.white, true);
            }
            else if (sprite == Circle)
            {
                img = Image(rt, Brighten(bg), Skin.Orb);
                Skin.Child(rt, "Ring", Skin.Ring, Color.white);
            }
            else img = Image(rt, bg, sprite);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.highlightedColor = Color.white;
            b.colors = colors;
            b.onClick.AddListener(onClick);
            Text(Stretch(Rect("Text", rt, Vector2.zero, Vector2.zero, Vector2.zero)), label, fontSize, Color.white, TextAnchor.MiddleCenter);
            return b;
        }

        // Dark muted colours read better a little lighter on the glossy sprites.
        static Color Brighten(Color c) => new Color(Mathf.Min(1, c.r * 1.25f + 0.04f), Mathf.Min(1, c.g * 1.25f + 0.04f), Mathf.Min(1, c.b * 1.25f + 0.04f), Mathf.Max(c.a, 0.9f));

        // A horizontal bar; returns the fill so callers can set anchorMax.x to the ratio.
        public static RectTransform Bar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fill, Vector2? pivot = null)
        {
            var bg = Rect("Bar", parent, anchor, pos, size, pivot);
            Skin.Sliced(bg, Skin.BarBack, Color.white).raycastTarget = false;
            var f = Stretch(Rect("Fill", bg, Vector2.zero, Vector2.zero, Vector2.zero));
            f.offsetMin = new Vector2(2, 2);
            f.offsetMax = new Vector2(-2, -2);
            Skin.Sliced(f, Skin.BarFill, fill).raycastTarget = false;
            return f;
        }

        public static void SetBar(RectTransform fill, float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            fill.anchorMax = new Vector2(ratio, 1);
            fill.gameObject.SetActive(ratio > 0.001f);
        }

        // A single-line text box (chat, names).
        public static InputField Field(Transform parent, string value, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize = 30, int limit = 64)
        {
            var rt = Rect("Field", parent, anchor, pos, size);
            var bg = Skin.Sliced(rt, Skin.Inset, Color.white);
            var textRt = Stretch(Rect("Text", rt, Vector2.zero, Vector2.zero, Vector2.zero));
            textRt.offsetMin = new Vector2(14, 0);
            textRt.offsetMax = new Vector2(-14, 0);
            var text = Text(textRt, "", fontSize, Color.white);
            text.supportRichText = false;
            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = text;
            field.characterLimit = limit;
            field.text = value;
            return field;
        }
    }
}
