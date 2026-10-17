using UnityEngine;

namespace MuProto
{
    // Colors the shapes the game builds at runtime (ground, zones, trees, wings, HP bars, effects).
    // GameObject.CreatePrimitive gives them the built-in "Default-Material" in a player build, and
    // URP cannot draw it, so on Android they were simply invisible. A URP Lit material kept in
    // Resources is always shipped with the build, so every shape starts from that instead.
    public static class Paint
    {
        static Material basic;

        static Material Basic
        {
            get
            {
                if (basic == null)
                {
                    basic = Resources.Load<Material>("Materials/Basic");
                    if (basic == null)
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (shader != null) basic = new Material(shader);
                    }
                }
                return basic;
            }
        }

        public static void Color(GameObject go, UnityEngine.Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (Basic != null) r.sharedMaterial = Basic;
            r.material.color = color; // per-object copy, like before
        }
    }
}
