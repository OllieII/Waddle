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

            CheckLegacyMovementInputs();

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick | OVRInput.Button.SecondaryThumbstick)) {
                PlayerHeadUtility.ResetHeadToBody(m_StateB);
            }
        }

        // These used to step the player forward. They no longer move anyone, but they still count as a
        // beat for the mating dance, which listens for the same waddle event.
        private void CheckLegacyMovementInputs() {
            if (OVRInput.GetDown(OVRInput.RawButton.A)) {
                OnLegacyMovementInput("A");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.B)) {
                OnLegacyMovementInput("B");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.X)) {
                OnLegacyMovementInput("X");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.Y)) {
                OnLegacyMovementInput("Y");
            }

            if (OVRInput.GetDown(OVRInput.RawButton.LThumbstickUp)) {
                OnLegacyMovementInput("LThumbstickUp");
            }
            if (OVRInput.GetDown(OVRInput.RawButton.RThumbstickUp)) {
                OnLegacyMovementInput("RThumbstickUp");
            }
        }

        private void OnLegacyMovementInput(string button) {
            PenguinAnalytics.Instance.LogButtonPress(button);
            QueueDanceBeat();
        }

        private void QueueDanceBeat() {
            FieldDay.Game.Events.Queue(PlayerMovementUtility.Event_WaddleDetected);
        }
    }
}