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

        #endregion // Inspector

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
            // Waddling is the only way to move - controller buttons and thumbsticks do not translate the player.

            LogFaceButtonPresses();

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick | OVRInput.Button.SecondaryThumbstick)) {
                PlayerHeadUtility.ResetHeadToBody(m_StateB);
            }
        }

        // The face buttons no longer do anything - log them to see how often players still reach for them.
        private void LogFaceButtonPresses() {
            if (OVRInput.GetDown(OVRInput.RawButton.A)) {
                PenguinAnalytics.Instance.LogButtonPress("A");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.B)) {
                PenguinAnalytics.Instance.LogButtonPress("B");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.X)) {
                PenguinAnalytics.Instance.LogButtonPress("X");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.Y)) {
                PenguinAnalytics.Instance.LogButtonPress("Y");
            }
        }
    }
}