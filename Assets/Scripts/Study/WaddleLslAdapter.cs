// T3: Waddle's pose source for StudyLsl. Same source the existing analytics and T2's PoseSampler use.
using UnityEngine;

namespace PenguinStudy {
    static internal class WaddleLslAdapter {
        static private PenguinPlayer s_Player;
        static private float s_NextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static private void Init() {
            StudyLsl.AppName = "waddle";
            StudyLsl.PoseSource = Sample;
        }

        static private bool Sample(out Pose head, out Pose left, out Pose right) {
            head = left = right = default;
            // Not PenguinPlayer.Instance: that getter creates an empty PenguinPlayer if none exists yet.
            if (s_Player == null) {
                if (Time.unscaledTime < s_NextSearch) return false;
                s_NextSearch = Time.unscaledTime + 1f;
                s_Player = Object.FindObjectOfType<PenguinPlayer>();
                if (s_Player == null) return false;
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
    }
}
