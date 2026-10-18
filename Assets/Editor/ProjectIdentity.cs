using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MuProto.EditorTools
{
    // Keeps the project's identity in Player Settings: the product name is also the window title of
    // the desktop build and the app name on Android. Runs whenever scripts reload.
    [InitializeOnLoad]
    public static class ProjectIdentity
    {
        public const string GameName = "King of Origin";
        public const string Company = "King of Origin Team";
        public const string AndroidId = "com.kingoforigin.game";

        static ProjectIdentity()
        {
            EditorApplication.delayCall += Apply;
        }

        [MenuItem("Tools/Apply King of Origin Project Settings")]
        static void Apply()
        {
            bool changed = false;
            if (PlayerSettings.productName != GameName) { PlayerSettings.productName = GameName; changed = true; }
            if (PlayerSettings.companyName != Company) { PlayerSettings.companyName = Company; changed = true; }
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != AndroidId)
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidId);
                changed = true;
            }
            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[ProjectIdentity] Player Settings set to " + GameName + " (" + AndroidId + ")");
            }
        }
    }
}
