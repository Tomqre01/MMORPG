using UnityEditor;

namespace MuProto.EditorTools
{
    // The development server (server/docker-compose.yml) is plain http on the home network.
    // Builds block http unless this is allowed (Player Settings > Other > Allow downloads over HTTP).
    [InitializeOnLoad]
    static class OnlineSettings
    {
        static OnlineSettings()
        {
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        }
    }
}
