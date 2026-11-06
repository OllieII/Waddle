#if UNITY_EDITOR || DEVELOPMENT_BUILD
// T0b desktop test rig for Waddle.
// Overrides the OVRCameraRig head and hand anchors with simulated poses when no headset is active,
// so PlayerHeadSystem / PlayerMovementDetectorSystem / slap and beak triggers run their real code.
//
// Waddling uses the real detector: Q / E play a short sideways head sway WITH a head roll
// (PlayerMovementDetectorSystem ignores motion while the head is perfectly upright).
//
// Controls (F1 toggles help):
//   Mouse            look (hold Right Mouse to move the selected flipper instead)
//   Q / E            one waddle step to the left / right (hold to keep waddling)
//   W A S D          move within the play space      Space / C   taller / crouch
//   1 / 2            select left / right flipper     F           flap selected flipper
//   R                reset flippers                  F5          start the game (Home mode)
//   F2               simulator off / on
using UnityEngine;

namespace Waddle.DesktopSim {
    [DefaultExecutionOrder(-30000)]
    public class DesktopRigDriver : MonoBehaviour {
        [Header("Head")]
        public float EyeHeight = 1.2f; // penguin-height default; adjust to match AdjustHeight
        public float MouseSensitivity = 2.5f;
        public float WalkSpeed = 1.0f;

        [Header("Waddle sway")]
        public float SwayDistance = 0.15f;  // metres of sideways head travel per step
        public float SwayRollDegrees = 12f; // head roll at peak sway
        public float SwayDuration = 0.35f;  // seconds out-and-back

        static public bool Enabled = true;

        private OVRCameraRig m_Rig;
        private Vector3 m_HeadPos;
        private float m_Yaw, m_Pitch;
        private readonly Vector3[] m_HandLocal = new Vector3[2];
        private int m_Selected = 1;
        private float m_SwayT = -1; // <0 = idle
        private float m_SwaySign;
        private float m_FlapT = -1;
        private bool m_ShowHelp = true;
        private bool m_Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static private void Boot() {
            if (FindObjectOfType<DesktopRigDriver>() != null) return;
            var go = new GameObject("[DesktopRigDriver]");
            DontDestroyOnLoad(go);
            go.AddComponent<DesktopRigDriver>();
        }

        private void Start() {
            m_HeadPos = new Vector3(0, EyeHeight, 0);
            ResetHands();
        }

        static private bool HeadsetActive() {
            return UnityEngine.XR.XRSettings.isDeviceActive && OVRManager.isHmdPresent;
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F2)) Enabled = !Enabled;
            m_Active = Enabled && !HeadsetActive();
            if (!m_Active) return;

            if (m_Rig == null) {
                m_Rig = FindObjectOfType<OVRCameraRig>();
                if (m_Rig != null) m_Rig.UpdatedAnchors += OnUpdatedAnchors;
            }

            ReadInput(Time.unscaledDeltaTime);
            Apply(); // OVR skips its anchor update without a headset, so this pose persists for the frame
        }

        private void OnDestroy() {
            if (m_Rig != null) m_Rig.UpdatedAnchors -= OnUpdatedAnchors;
        }

        // If OVR does update anchors (e.g. Link connected but HMD off-head), re-apply after it.
        private void OnUpdatedAnchors(OVRCameraRig rig) {
            if (m_Active) Apply();
        }

        private void ReadInput(float dt) {
            if (Input.GetKeyDown(KeyCode.F1)) m_ShowHelp = !m_ShowHelp;
            if (Input.GetKeyDown(KeyCode.Alpha1)) m_Selected = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) m_Selected = 1;
            if (Input.GetKeyDown(KeyCode.R)) ResetHands();
            if (Input.GetKeyDown(KeyCode.F) && m_FlapT < 0) m_FlapT = 0;
            if (Input.GetKeyDown(KeyCode.F5) && PenguinGameManager.Instance != null && !PenguinGameManager.Instance.GameWasStarted) {
                PenguinGameManager.Instance.BeginTheGame(PenguinGameManager.GameMode.HomeMode);
            }

            float mx = Input.GetAxisRaw("Mouse X") * MouseSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * MouseSensitivity;
            if (Input.GetMouseButton(1)) {
                m_HandLocal[m_Selected] += new Vector3(mx, my, 0) * 0.01f;
            } else {
                m_Yaw += mx;
                m_Pitch = Mathf.Clamp(m_Pitch - my, -80f, 80f);
            }

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move += Vector3.back;
            if (Input.GetKey(KeyCode.A)) move += Vector3.left;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            m_HeadPos += Quaternion.Euler(0, m_Yaw, 0) * move * (WalkSpeed * dt);
            if (Input.GetKey(KeyCode.Space)) m_HeadPos.y += dt;
            if (Input.GetKey(KeyCode.C)) m_HeadPos.y -= dt;
            m_HeadPos.y = Mathf.Clamp(m_HeadPos.y, 0.4f, 2.0f);

            // Sway: start a new step when idle; alternate sides if the key is held.
            if (m_SwayT < 0) {
                if (Input.GetKey(KeyCode.Q)) { m_SwayT = 0; m_SwaySign = -1; }
                else if (Input.GetKey(KeyCode.E)) { m_SwayT = 0; m_SwaySign = 1; }
            } else {
                m_SwayT += dt;
                if (m_SwayT >= SwayDuration) m_SwayT = -1;
            }
            if (m_FlapT >= 0) {
                m_FlapT += dt;
                if (m_FlapT > 0.4f) m_FlapT = -1;
            }
        }

        private void Apply() {
            if (m_Rig == null || m_Rig.centerEyeAnchor == null) return;

            // Sway profile: sin(pi * t/T) -> out and back; roll follows the offset.
            float sway = m_SwayT >= 0 ? Mathf.Sin(Mathf.PI * (m_SwayT / SwayDuration)) * m_SwaySign : 0;
            Quaternion yaw = Quaternion.Euler(0, m_Yaw, 0);
            Vector3 headPos = m_HeadPos + yaw * new Vector3(sway * SwayDistance, 0, 0);
            Quaternion headRot = Quaternion.Euler(m_Pitch, m_Yaw, -sway * SwayRollDegrees);

            SetLocal(m_Rig.centerEyeAnchor, headPos, headRot);
            SetLocal(m_Rig.leftEyeAnchor, headPos, headRot);
            SetLocal(m_Rig.rightEyeAnchor, headPos, headRot);

            for (int i = 0; i < 2; i++) {
                Vector3 local = m_HandLocal[i];
                if (i == m_Selected && m_FlapT >= 0) {
                    local.y += Mathf.Sin(Mathf.PI * 2 * (m_FlapT / 0.4f)) * 0.15f;
                }
                Transform anchor = i == 0 ? m_Rig.leftHandAnchor : m_Rig.rightHandAnchor;
                SetLocal(anchor, m_HeadPos + yaw * local, yaw);
            }
        }

        static private void SetLocal(Transform t, Vector3 pos, Quaternion rot) {
            if (t == null) return;
            t.localPosition = pos;
            t.localRotation = rot;
        }

        private void ResetHands() {
            m_HandLocal[0] = new Vector3(-0.25f, -0.4f, 0.1f);
            m_HandLocal[1] = new Vector3(0.25f, -0.4f, 0.1f);
        }

        private void OnGUI() {
            if (!m_Active || !m_ShowHelp) return;
            GUI.Box(new Rect(10, 10, 380, 150),
                "DESKTOP SIM (F1 hide, F2 off)\n" +
                "Mouse look | RMB+mouse move flipper | F flap\n" +
                "Q/E waddle step (real detector) | WASD move | Space/C height\n" +
                "1/2 select flipper: " + (m_Selected == 0 ? "LEFT" : "RIGHT") + " | R reset\n" +
                "F5 start game (Home mode)" + (m_Rig == null ? "\nwaiting for OVRCameraRig..." : ""));
        }
    }
}
#endif
