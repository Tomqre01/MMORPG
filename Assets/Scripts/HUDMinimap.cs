using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MuProto
{
    // Round minimap in the top-right corner, centred on the player: the map ground, the safe town,
    // monster zones (coloured by how dangerous they are for you), monsters, bosses and other players.
    public partial class HUD
    {
        const float MapScale = 3.2f; // pixels per world unit
        RectTransform mapContent, mapGround, playerArrow;
        Text mapNameText;
        readonly List<Image> mapDots = new List<Image>();
        readonly List<(Image img, int level)> zoneMarks = new List<(Image, int)>();
        int builtFor = -1;

        void BuildMinimap(Transform root)
        {
            var frame = UIB.Rect("Minimap", root, TR, new Vector2(-14, -14), new Vector2(256, 256));
            var mask = UIB.Rect("Mask", frame, C, Vector2.zero, new Vector2(236, 236), C);
            UIB.Image(mask, new Color(0.10f, 0.11f, 0.09f, 0.92f), UIB.Circle);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            mask.gameObject.AddComponent<Button>().onClick.AddListener(() => Open("Map"));
            mapContent = UIB.Rect("Content", mask, C, Vector2.zero, Vector2.zero, C);
            mapGround = UIB.Rect("Ground", mapContent, C, Vector2.zero, new Vector2(96 * MapScale, 96 * MapScale), C);
            UIB.Image(mapGround, new Color(0.28f, 0.33f, 0.22f, 0.9f)).raycastTarget = false;
            var town = UIB.Rect("Town", mapGround, C, Vector2.zero, Vector2.one * GameData.TownRadius * 2 * MapScale, C);
            UIB.Image(town, new Color(0.85f, 0.75f, 0.5f, 0.55f), UIB.Circle).raycastTarget = false;

            var glow = UIB.Rect("You", mask, C, Vector2.zero, new Vector2(40, 40), C);
            UIB.Image(glow, new Color(1f, 0.9f, 0.5f, 0.6f), Skin.Soft).raycastTarget = false;
            playerArrow = UIB.Rect("Arrow", mask, C, Vector2.zero, new Vector2(18, 26), C);
            UIB.Image(playerArrow, new Color(1f, 0.88f, 0.4f), Skin.Diamond).raycastTarget = false;

            var ring = UIB.Rect("Ring", frame, C, Vector2.zero, new Vector2(256, 256), C);
            UIB.Image(ring, Color.white, Skin.Ring).raycastTarget = false;

            var plate = UIB.Rect("MapName", root, TR, new Vector2(-142, -266), new Vector2(320, 46), new Vector2(0.5f, 1));
            UIB.Image(plate, Color.white, Skin.Banner);
            plate.gameObject.AddComponent<Button>().onClick.AddListener(() => Open("Map"));
            mapNameText = UIB.Label(plate, "", 22, C, Vector2.zero, new Vector2(320, 46), TextAnchor.MiddleCenter);
            mapNameText.rectTransform.pivot = C;
            mapNameText.color = Skin.Cream;
        }

        void RebuildZones()
        {
            foreach (var z in zoneMarks) Destroy(z.img.gameObject);
            zoneMarks.Clear();
            builtFor = Maps.Current;
            foreach (var z in Maps.CurrentDef.Zones)
            {
                var rt = UIB.Rect("Zone", mapGround, C, new Vector2(z.Offset.x, z.Offset.z) * MapScale, Vector2.one * z.Radius * 2 * MapScale, C);
                var img = UIB.Image(rt, Color.white, Skin.Soft);
                img.raycastTarget = false;
                zoneMarks.Add((img, z.Level));
            }
        }

        Image Dot(int i)
        {
            while (mapDots.Count <= i)
            {
                var rt = UIB.Rect("Dot", mapContent, C, Vector2.zero, new Vector2(8, 8), C);
                var img = UIB.Image(rt, Color.red, UIB.Circle);
                img.raycastTarget = false;
                mapDots.Add(img);
            }
            var d = mapDots[i];
            d.gameObject.SetActive(true);
            return d;
        }

        void UpdateMinimap(Player p)
        {
            if (builtFor != Maps.Current) RebuildZones();
            var def = Maps.CurrentDef;
            Vector3 me = p.transform.position;
            mapGround.anchoredPosition = Vector2.zero;
            mapContent.anchoredPosition = -new Vector2(me.x - def.Center.x, me.z - def.Center.z) * MapScale;
            mapGround.anchoredPosition = Vector2.zero;
            playerArrow.localEulerAngles = new Vector3(0, 0, -p.transform.eulerAngles.y);
            int lv = p.Data.level;
            foreach (var (img, zl) in zoneMarks)
            {
                int gap = zl - lv;
                var c = gap > 5 ? new Color(1f, 0.25f, 0.2f) : gap >= -3 ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.9f, 0.5f);
                c.a = 0.35f;
                img.color = c;
            }

            int n = 0;
            float range2 = 60f * 60f;
            foreach (var m in Monster.All)
            {
                if (m == null || m.IsDead || !m.gameObject.activeInHierarchy) continue;
                Vector3 v = m.transform.position;
                if ((v - me).sqrMagnitude > range2) continue;
                var d = Dot(n++);
                d.rectTransform.anchoredPosition = new Vector2(v.x - def.Center.x, v.z - def.Center.z) * MapScale;
                bool boss = m.IsBoss || m.IsWorldBoss;
                d.rectTransform.sizeDelta = boss ? new Vector2(18, 18) : new Vector2(8, 8);
                d.color = boss ? new Color(0.85f, 0.4f, 1f) : new Color(1f, 0.3f, 0.25f);
            }
            if (WorldSync.Instance != null)
                foreach (var rp in WorldSync.Instance.Remotes)
                {
                    Vector3 v = rp.transform.position;
                    if ((v - me).sqrMagnitude > range2) continue;
                    var d = Dot(n++);
                    d.rectTransform.anchoredPosition = new Vector2(v.x - def.Center.x, v.z - def.Center.z) * MapScale;
                    d.rectTransform.sizeDelta = new Vector2(12, 12);
                    d.color = rp.IsRed ? new Color(1f, 0.2f, 0.1f) : new Color(0.35f, 0.75f, 1f);
                }
            for (int i = n; i < mapDots.Count; i++) mapDots[i].gameObject.SetActive(false);

            mapNameText.text = def.Name + "  <color=#c9b48a>" + Mathf.RoundToInt(me.x - def.Center.x + 48) + ", " + Mathf.RoundToInt(me.z - def.Center.z + 48) + "</color>";
        }
    }
}
