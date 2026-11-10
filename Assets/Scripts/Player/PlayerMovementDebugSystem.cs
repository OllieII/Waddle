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
            // Button presses are recorded by PenguinStudy.WaddleButtonTelemetry, which keeps running
            // when this system does not. Logging here too would double every press.
            if (!m_AllowButtonMovement) {
                return;
            }

            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick | OVRInput.Button.SecondaryThumbstick)) {
                PlayerHeadUtility.ResetHeadToBody(m_StateB);
            }
        }
    }
}
