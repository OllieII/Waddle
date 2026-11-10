// T2: writes one pose.csv row per rendered frame.
// Identical file in Waddle and pennycookvr (Assets/StudyLogging/).
using UnityEngine;

namespace PenguinStudy {
    /// <summary>
    /// Samples head and hand pose plus controller input once per rendered frame into the open
    /// session's pose.csv.
    /// <para>Sampled from Application.onBeforeRender, the same hook T3's pose stream uses, so a
    /// pose.csv row and an LSL pose sample describe the same frame and the two can be lined up
    /// afterwards.</para>
    /// <para>Where the poses and input come from is the app's business: both are read through
    /// <see cref="StudySources"/>, which each app's adapter fills in.</para>
    /// </summary>
    public class StudyPoseSampler : MonoBehaviour {
        long _rows;

        /// <summary>Rows written since this sampler started.</summary>
        public long RowCount { get { return _rows; } }

        void OnEnable() {
            Application.onBeforeRender += Sample;
        }

        void OnDisable() {
            Application.onBeforeRender -= Sample;
        }

        void Sample() {
            StudyLogger logger = StudyLogger.Instance;
            if (logger == null || !logger.IsOpen) {
                return;
            }

            StudyPoseSourceFn poseSource = StudySources.Pose;
            if (poseSource == null) {
                return;
            }

            Pose head, left, right;
            if (!poseSource(out head, out left, out right)) {
                //rig not ready yet, or poses unavailable this frame
                return;
            }

            //input is optional: a missing or failing source leaves the analog columns at zero
            StudyInputSample input = new StudyInputSample();
            StudyInputSourceFn inputSource = StudySources.Input;
            if (inputSource != null) {
                StudyInputSample sampled;
                if (inputSource(out sampled)) {
                    input = sampled;
                }
            }

            //Time.frameCount rather than a row counter, so a gap in the column shows a frame that
            //produced no sample instead of hiding it
            logger.Pose(Time.frameCount,
                head.position, head.rotation,
                left.position, left.rotation,
                right.position, right.rotation,
                input);

            _rows++;
        }
    }
}
