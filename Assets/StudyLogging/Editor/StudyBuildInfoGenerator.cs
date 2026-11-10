// T2: stamps the git commit into Resources/StudyBuildInfo.txt just before a build.
// Identical file in Waddle and pennycookvr (Assets/StudyLogging/Editor/).
//
// Runs automatically on every build. "Tools/Study/Stamp Build Info" does the same thing by hand,
// which is useful when checking the stamp in the Editor without making a player build.
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PenguinStudy.EditorTools {
    public class StudyBuildInfoGenerator : IPreprocessBuildWithReport {
        const string ResourcesFolder = "Assets/StudyLogging/Resources";
        const string OutputPath = ResourcesFolder + "/" + StudyBuildInfo.ResourceName + ".txt";

        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report) {
            Stamp();
        }

        [MenuItem("Tools/Study/Stamp Build Info")]
        static public void Stamp() {
            string commit = ReadGit("rev-parse HEAD");
            if (string.IsNullOrEmpty(commit)) {
                commit = StudyBuildInfo.Unknown;
            } else if (!string.IsNullOrEmpty(ReadGit("status --porcelain"))) {
                //a build made from a dirty tree is not that commit, and saying so beats a wrong SHA
                commit += "-dirty";
            }

            string text = string.Format(
                "commit={0}\nbuild={1:yyyy-MM-ddTHH:mm:ssZ}\n",
                commit, DateTime.UtcNow);

            try {
                System.IO.Directory.CreateDirectory(ResourcesFolder);
                File.WriteAllText(OutputPath, text);
                AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
                UnityEngine.Debug.Log("[StudyBuildInfo] stamped " + commit);
            } catch (Exception e) {
                UnityEngine.Debug.LogWarning("[StudyBuildInfo] could not write " + OutputPath + ": " + e.Message);
            }
        }

        static string ReadGit(string arguments) {
            try {
                ProcessStartInfo info = new ProcessStartInfo("git", arguments);
                info.WorkingDirectory = System.IO.Directory.GetParent(Application.dataPath).FullName;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.CreateNoWindow = true;

                using (Process process = Process.Start(info)) {
                    if (process == null) {
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    return process.ExitCode == 0 ? output.Trim() : null;
                }
            } catch (Exception) {
                //no git on the build machine is not a build failure; meta.json just says "unknown"
                return null;
            }
        }
    }
}
