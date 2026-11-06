// T3: LSL streams for the Penguin VR study. Identical file in Waddle and pennycookvr (Assets/StudyLSL/).
//
// Streams
//   PenguinVR_Pose    float32 x 21 at the display rate: head, left, right; each px py pz qx qy qz qw (world space)
//   PenguinVR_Markers string, irregular: one JSON object per event
//
// Wiring
//   - Each app registers a pose source once: StudyLsl.PoseSource = myProvider (see the app's *LslPoseSource.cs).
//   - Anything can push a marker: StudyLsl.Marker("SCENE_START", "{\"scene\":\"nest\"}").
//   - T2 hookup (one line, once StudyLogger exists): StudyLogger.OnEvent += StudyLsl.Marker;
//
// Clock: LSL.LSL.local_clock() is the study clock. StudyLsl.Now is safe to call even if LSL failed to load
// (returns NaN), so StudyLogger can use it for its t_lsl column.
using System;
using System.Text;
using UnityEngine;

namespace PenguinStudy {
    public delegate bool PoseSourceFn(out Pose head, out Pose left, out Pose right);

    public sealed class StudyLsl : MonoBehaviour {
        public const string PoseStreamName = "PenguinVR_Pose";
        public const string MarkerStreamName = "PenguinVR_Markers";

        /// <summary>Set by the app adapter. Returns false if poses are not available this frame.</summary>
        static public PoseSourceFn PoseSource;

        /// <summary>App name written into the stream metadata ("waddle" or "hatched"). Set by the app adapter at AfterAssembliesLoaded.</summary>
        static public string AppName = "unknown";

        static public bool Available { get; private set; }
        static public double Now { get { return Available ? LSL.LSL.local_clock() : double.NaN; } }

        static private StudyLsl s_Instance;
        private LSL.StreamOutlet m_Pose;
        private LSL.StreamOutlet m_Markers;
        private readonly float[] m_PoseSample = new float[21];
        private readonly string[] m_MarkerSample = new string[1];
        private readonly StringBuilder m_Sb = new StringBuilder(256);
        private bool m_HasConsumers;
        private float m_NextConsumerCheck;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject m_MulticastLock;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static private void Boot() {
            if (s_Instance != null) return;
            var go = new GameObject("[StudyLsl]");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<StudyLsl>();
        }

        private void Awake() {
            AcquireMulticastLock();
            try {
                // Markers first, so early events (SESSION_START) are not lost.
                var markerInfo = new LSL.StreamInfo(MarkerStreamName, "Markers", 1, LSL.LSL.IRREGULAR_RATE, LSL.channel_format_t.cf_string, "penguinvr-markers-" + SystemInfo.deviceUniqueIdentifier);
                markerInfo.desc().append_child_value("app", AppName);
                m_Markers = new LSL.StreamOutlet(markerInfo);
                Available = true;
                Debug.Log("[StudyLsl] marker outlet open (" + AppName + ", liblsl " + LSL.LSL.library_version() + ")");
            } catch (Exception e) {
                Available = false;
                Debug.LogError("[StudyLsl] LSL unavailable, continuing without it: " + e.Message);
            }
        }

        // The pose outlet waits until the XR display reports its refresh rate (it is 0 during startup),
        // so the stream's nominal rate matches the real frame rate (72/90/120 Hz on Quest 3).
        private void Update() {
            if (!Available || m_Pose != null) return;
            float rate = UnityEngine.XR.XRDevice.refreshRate;
            if (rate < 1f) {
                if (Time.realtimeSinceStartup < 5f) return;
                rate = 90f; // Editor without a headset: nominal only; samples still follow the frame rate
            }
            try {
                var poseInfo = new LSL.StreamInfo(PoseStreamName, "MoCap", 21, rate, LSL.channel_format_t.cf_float32, "penguinvr-pose-" + SystemInfo.deviceUniqueIdentifier);
                var desc = poseInfo.desc();
                desc.append_child_value("app", AppName);
                desc.append_child_value("device", SystemInfo.deviceModel);
                desc.append_child_value("app_version", Application.version);
                var chans = desc.append_child("channels");
                foreach (string part in new[] { "head", "left", "right" }) {
                    foreach (string c in new[] { "px", "py", "pz", "qx", "qy", "qz", "qw" }) {
                        var ch = chans.append_child("channel");
                        ch.append_child_value("label", part + "_" + c);
                        ch.append_child_value("unit", c[0] == 'p' ? "meters" : "quaternion");
                    }
                }
                m_Pose = new LSL.StreamOutlet(poseInfo);
                Application.onBeforeRender += SamplePose;
                Debug.Log("[StudyLsl] pose outlet open at " + rate + " Hz");
            } catch (Exception e) {
                Debug.LogError("[StudyLsl] pose outlet failed: " + e.Message);
                enabled = false;
            }
        }

        private void OnDestroy() {
            Application.onBeforeRender -= SamplePose;
            Available = false;
            m_Pose?.Dispose();
            m_Markers?.Dispose();
#if UNITY_ANDROID && !UNITY_EDITOR
            try { m_MulticastLock?.Call("release"); } catch { }
#endif
        }

        // Sampled right before rendering, so the pose is the one the participant actually saw.
        private void SamplePose() {
            if (m_Pose == null || PoseSource == null) return;
            Pose h, l, r;
            if (!PoseSource(out h, out l, out r)) return;
            Write(h, 0); Write(l, 7); Write(r, 14);
            m_Pose.push_sample(m_PoseSample, LSL.LSL.local_clock());
        }

        private void Write(Pose p, int o) {
            m_PoseSample[o] = p.position.x; m_PoseSample[o + 1] = p.position.y; m_PoseSample[o + 2] = p.position.z;
            m_PoseSample[o + 3] = p.rotation.x; m_PoseSample[o + 4] = p.rotation.y; m_PoseSample[o + 5] = p.rotation.z; m_PoseSample[o + 6] = p.rotation.w;
        }

        /// <summary>
        /// Pushes one marker, stamped now. dataJson is a JSON object or null.
        /// Signature matches StudyLogger.OnEvent (name, dataJson).
        /// </summary>
        static public void Marker(string name, string dataJson) {
            var self = s_Instance;
            if (self == null || self.m_Markers == null) return;
            var sb = self.m_Sb;
            sb.Length = 0;
            sb.Append("{\"event\":\"").Append(name).Append("\",\"t_unity\":")
              .Append(Time.realtimeSinceStartupAsDouble.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"frame\":").Append(Time.frameCount);
            if (!string.IsNullOrEmpty(dataJson)) sb.Append(",\"data\":").Append(dataJson);
            sb.Append('}');
            self.m_MarkerSample[0] = sb.ToString();
            self.m_Markers.push_sample(self.m_MarkerSample, LSL.LSL.local_clock());
        }

        /// <summary>True if LabRecorder (or any inlet) is currently connected. Checked at most twice a second.</summary>
        static public bool Recording {
            get {
                var self = s_Instance;
                if (self == null || self.m_Pose == null) return false;
                if (Time.unscaledTime >= self.m_NextConsumerCheck) {
                    self.m_HasConsumers = self.m_Pose.have_consumers();
                    self.m_NextConsumerCheck = Time.unscaledTime + 0.5f;
                }
                return self.m_HasConsumers;
            }
        }

        private void AcquireMulticastLock() {
#if UNITY_ANDROID && !UNITY_EDITOR
            try {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi")) {
                    m_MulticastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "lsl");
                    m_MulticastLock.Call("setReferenceCounted", false);
                    m_MulticastLock.Call("acquire");
                }
            } catch (Exception e) { Debug.LogWarning("[StudyLsl] multicast lock failed: " + e.Message); }
#endif
        }
    }
}
