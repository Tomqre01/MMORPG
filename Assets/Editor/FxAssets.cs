using System.IO;
using UnityEditor;
using UnityEngine;

namespace MuProto.EditorTools
{
    // Saves the additive effects material (slash trails, sparks, arcs) into Resources once,
    // so player builds keep the URP particle shader it uses.
    [InitializeOnLoad]
    public static class FxAssets
    {
        const string Path = "Assets/Resources/Materials/FxAdditive.mat";

        static FxAssets()
        {
            EditorApplication.delayCall += Make;
        }

        static void Make()
        {
            if (File.Exists(Path)) return;
            var m = Fx.MakeAdditive();
            AssetDatabase.CreateAsset(m, Path);
            AssetDatabase.SaveAssets();
            Debug.Log("[FxAssets] Created " + Path);
        }
    }
}
