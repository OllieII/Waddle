// T2: Waddle's providers for the shared study logger in Assets/StudyLogging/.
//
// Everything app-specific about the logger lives here: where poses come from, how this app's input
// stack maps onto the study's button bits, and the BUTTON_PRESS producer. The shared folder stays
// free of PenguinPlayer and OVRInput so pennycookvr can use it with an adapter of its own.
using UnityEngine;

namespace PenguinStudy {
    static internal class WaddleStudyAdapter {
        static private PenguinPlayer s_Player;
        static private float s_NextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static private void Init() {
            StudySources.AppName = "waddle";
            StudySources.Pose = SamplePose;
            StudySources.Input = SampleInput;
        }

        //the telemetry host is its own object, not a component on something the game disables:
        //BUTTON_PRESS has to keep flowing while a menu is up and the movement systems are off
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static private void BootTelemetry() {
            if (WaddleButtonTelemetry.Instance != null) {
                return;
            }

            GameObject host = new GameObject("WaddleButtonTelemetry");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<WaddleButtonTelemetry>();
        }

        /// <summary>
        /// Finds the player without going through PenguinPlayer.Instance, whose getter would build
        /// an empty one if the scene had none. Retries on an interval rather than every frame.
        /// </summary>
        static internal bool ResolvePlayer() {
            if (s_Player != null) {
                return true;
            }

            if (Time.unscaledTime < s_NextSearch) {
                return false;
            }

            s_NextSearch = Time.unscaledTime + 1f;
            s_Player = Object.FindObjectOfType<PenguinPlayer>();

            return s_Player != null;
        }

        static private bool SamplePose(out Pose head, out Pose left, out Pose right) {
            head = left = right = default(Pose);

            if (!ResolvePlayer()) {
                return false;
            }

            try {
                s_Player.GetGaze(out head.position, out head.rotation);
                s_Player.GetHandTransform(true, out left.position, out left.rotation);
                s_Player.GetHandTransform(false, out right.position, out right.rotation);
                return true;
            } catch (System.NullReferenceException) {
                return false; // rig not ready yet
            }
        }

        static private bool SampleInput(out StudyInputSample input) {
            input = new StudyInputSample();

            input.LeftTrigger = OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger);
            input.LeftGrip = OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger);
            input.RightTrigger = OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
            input.RightGrip = OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger);

            for (int i = 0; i < WaddleButtonTelemetry.Buttons.Length; i++) {
                if (OVRInput.Get(WaddleButtonTelemetry.Buttons[i])) {
                    input.Set(WaddleButtonTelemetry.StudyBits[i], true);
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Emits one BUTTON_PRESS per press, for the whole session.
    /// <para>This used to sit in PlayerMovementDebugSystem, which the game loop stops running when
    /// PlayerMovementState is disabled - so presses went unrecorded exactly while a menu was open.
    /// It lives on its own DontDestroyOnLoad object instead, and is the only producer of the event
    /// so the T1 and T2 paths cannot both log the same press.</para>
    /// <para>Recording a press is not the same as acting on it: nothing here feeds gameplay.</para>
    /// </summary>
    public class WaddleButtonTelemetry : MonoBehaviour {
        static public WaddleButtonTelemetry Instance { get; private set; }

        // Every input worth logging on Touch controllers, so we can see when players reach for one.
        // Gamepad-only and Touchpad entries are left out because they never fire on Quest.
        static internal readonly OVRInput.RawButton[] Buttons = new OVRInput.RawButton[] {
            OVRInput.RawButton.A,
            OVRInput.RawButton.B,
            OVRInput.RawButton.X,
            OVRInput.RawButton.Y,
            OVRInput.RawButton.Start,
            OVRInput.RawButton.LIndexTrigger,
            OVRInput.RawButton.LHandTrigger,
            OVRInput.RawButton.LThumbstick,
            OVRInput.RawButton.LThumbstickUp,
            OVRInput.RawButton.LThumbstickDown,
            OVRInput.RawButton.LThumbstickLeft,
            OVRInput.RawButton.LThumbstickRight,
            OVRInput.RawButton.RIndexTrigger,
            OVRInput.RawButton.RHandTrigger,
            OVRInput.RawButton.RThumbstick,
            OVRInput.RawButton.RThumbstickUp,
            OVRInput.RawButton.RThumbstickDown,
            OVRInput.RawButton.RThumbstickLeft,
            OVRInput.RawButton.RThumbstickRight
        };

        // Spelled out rather than taken from ToString, so logged values stay stable for analysis.
        static internal readonly string[] ButtonNames = new string[] {
            "A",
            "B",
            "X",
            "Y",
            "Start",
            "LIndexTrigger",
            "LHandTrigger",
            "LThumbstickClick",
            "LThumbstickUp",
            "LThumbstickDown",
            "LThumbstickLeft",
            "LThumbstickRight",
            "RIndexTrigger",
            "RHandTrigger",
            "RThumbstickClick",
            "RThumbstickUp",
            "RThumbstickDown",
            "RThumbstickLeft",
            "RThumbstickRight"
        };

        // Same order as Buttons: the study bit each OVR button occupies in pose.csv.
        static internal readonly StudyButton[] StudyBits = new StudyButton[] {
            StudyButton.A,
            StudyButton.B,
            StudyButton.X,
            StudyButton.Y,
            StudyButton.Menu,
            StudyButton.LeftTrigger,
            StudyButton.LeftGrip,
            StudyButton.LeftThumbstickClick,
            StudyButton.LeftThumbstickUp,
            StudyButton.LeftThumbstickDown,
            StudyButton.LeftThumbstickLeft,
            StudyButton.LeftThumbstickRight,
            StudyButton.RightTrigger,
            StudyButton.RightGrip,
            StudyButton.RightThumbstickClick,
            StudyButton.RightThumbstickUp,
            StudyButton.RightThumbstickDown,
            StudyButton.RightThumbstickLeft,
            StudyButton.RightThumbstickRight
        };

        PenguinAnalytics _analytics;
        float _nextSearch;

        void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void OnDestroy() {
            if (Instance == this) {
                Instance = null;
            }
        }

        void Update() {
            if (!ResolveAnalytics()) {
                return;
            }

            for (int i = 0; i < Buttons.Length; i++) {
                if (OVRInput.GetDown(Buttons[i])) {
                    _analytics.LogButtonPress(ButtonNames[i]);
                }
            }
        }

        //same reason as ResolvePlayer: PenguinAnalytics.Instance would manufacture an empty
        //analytics object if the real one has not loaded yet
        bool ResolveAnalytics() {
            if (_analytics != null) {
                return true;
            }

            if (Time.unscaledTime < _nextSearch) {
                return false;
            }

            _nextSearch = Time.unscaledTime + 1f;
            _analytics = FindObjectOfType<PenguinAnalytics>();

            return _analytics != null;
        }
    }
}
