using BeauUtil.Debugger;
using FieldDay.Debugging;
using FieldDay.SharedState;
using FieldDay.Systems;
using UnityEngine;

namespace Waddle
{
    [SysUpdate(FieldDay.GameLoopPhase.Update, 100)]
    public class PlayerMovementDebugSystem : SharedStateSystemBehaviour<PlayerMovementState, PlayerHeadState>
    {
        #region Inspector

        [Header("Study Build")]
        [SerializeField]
        [Tooltip("When off, controller buttons and thumbsticks are logged but do nothing in game. " +
            "Leave off for study builds - swaying is then the only input that has any effect.")]
        private bool m_AllowButtonMovement = false;

        #endregion // Inspector

        // Every input worth logging on Touch controllers, so we can see when players reach for one.
        // Gamepad-only and Touchpad entries are left out because they never fire on Quest.
        static private readonly OVRInput.RawButton[] s_LoggedButtons = new OVRInput.RawButton[] {
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
        static private readonly string[] s_LoggedButtonNames = new string[] {
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

        public override void ProcessWork(float deltaTime) {
            CheckDebugKeys(deltaTime);
            CheckOVRKeys();
        }

        private void CheckDebugKeys(float deltaTime) {
#if UNITY_EDITOR
            if (Input.GetKey(KeyCode.LeftArrow)) {
                m_StateB.PositionRoot.Rotate(0, -90 * deltaTime, 0);
            }
            if (Input.GetKey(KeyCode.RightArrow)) {
                m_StateB.PositionRoot.Rotate(0, 90 * deltaTime, 0);
            }

            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.R)) {
                PenguinGameManager.Instance.RestartGame();
            }

            if (m_StateA.WalkCooldown <= 0) {
                if (Input.GetKey(KeyCode.UpArrow)) {
                    PlayerMovementUtility.QueueMovement(m_StateA, m_StateB.BodyRoot.forward, PlayerFoot.Invalid, 0.1f, PlayerMovementSource.Button);
                } else if (Input.GetKey(KeyCode.DownArrow)) {
                    PlayerMovementUtility.QueueMovement(m_StateA, -m_StateB.BodyRoot.forward, PlayerFoot.Invalid, 0.1f, PlayerMovementSource.Button);
                }
            }
#endif // UNITY_EDITOR
        }

        private void CheckOVRKeys() {
            // Presses are always recorded. Whether any of them does something is up to the flag.
            LogButtonPresses();

            if (!m_AllowButtonMovement) {
                return;
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick | OVRInput.Button.SecondaryThumbstick)) {
                PlayerHeadUtility.ResetHeadToBody(m_StateB);
            }
        }

        private void LogButtonPresses() {
            for (int i = 0; i < s_LoggedButtons.Length; i++) {
                if (OVRInput.GetDown(s_LoggedButtons[i])) {
                    PenguinAnalytics.Instance.LogButtonPress(s_LoggedButtonNames[i]);
                }
            }
        }
    }
}
