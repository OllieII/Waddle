// T2: the commit the running build was made from, for meta.json.
// Identical file in Waddle and pennycookvr (Assets/StudyLogging/).
//
// Application.version is the version string an author typed in Player Settings, which does not say
// which commit a build came from. The editor script in Editor/StudyBuildInfoGenerator.cs stamps the
// real commit into Resources/StudyBuildInfo.txt just before each build; this reads it back.
using UnityEngine;

namespace PenguinStudy {
    static public class StudyBuildInfo {
        /// <summary>Resources path (no extension) of the generated file.</summary>
        public const string ResourceName = "StudyBuildInfo";

        public const string Unknown = "unknown";

        static bool s_Loaded;
        static string s_Commit = Unknown;
        static string s_BuildId = Unknown;

        /// <summary>Git commit of the build, or "unknown" in an editor session with no stamp yet.</summary>
        static public string Commit { get { Load(); return s_Commit; } }

        /// <summary>Build timestamp written alongside the commit.</summary>
        static public string BuildId { get { Load(); return s_BuildId; } }

        static void Load() {
            if (s_Loaded) {
                return;
            }

            s_Loaded = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) {
                return;
            }

            //plain key=value lines, so the file stays readable and needs no JSON parser here
            string[] lines = asset.text.Split('\n');
            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i].Trim();
                int split = line.IndexOf('=');
                if (split <= 0) {
                    continue;
                }

                string key = line.Substring(0, split).Trim();
                string value = line.Substring(split + 1).Trim();

                if (string.IsNullOrEmpty(value)) {
                    continue;
                }

                if (key == "commit") {
                    s_Commit = value;
                } else if (key == "build") {
                    s_BuildId = value;
                }
            }
        }
    }
}
