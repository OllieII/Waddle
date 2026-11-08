// T2: the hooks an app fills in for the shared study logger.
// Identical file in Waddle and pennycookvr (Assets/StudyLogging/).
//
// The shared logger knows nothing about either app's player rig or input stack. Each app registers
// its own providers once, the same way T3's StudyLsl takes a PoseSource:
//
//     StudySources.AppName = "waddle";
//     StudySources.Pose    = MyAdapter.SamplePose;
//     StudySources.Input   = MyAdapter.SampleInput;
//
// See Assets/Scripts/Study/WaddleStudyAdapter.cs for this app's implementation.
using UnityEngine;

namespace PenguinStudy {
    /// <summary>
    /// Bit positions for the pose.csv "buttons" column. Deliberately the study's own numbering
    /// rather than an SDK enum, so the column means the same thing in both apps and stays stable
    /// if an SDK renumbers its own constants. Each app's adapter maps its SDK onto these bits.
    /// </summary>
    public enum StudyButton {
        A = 0,
        B = 1,
        X = 2,
        Y = 3,
        Menu = 4,
        LeftThumbstickClick = 5,
        RightThumbstickClick = 6,
        LeftTrigger = 7,
        LeftGrip = 8,
        RightTrigger = 9,
        RightGrip = 10,
        LeftThumbstickUp = 11,
        LeftThumbstickDown = 12,
        LeftThumbstickLeft = 13,
        LeftThumbstickRight = 14,
        RightThumbstickUp = 15,
        RightThumbstickDown = 16,
        RightThumbstickLeft = 17,
        RightThumbstickRight = 18
    }

    /// <summary>One frame of controller input, in the study's own app-agnostic form.</summary>
    public struct StudyInputSample {
        public float LeftTrigger;
        public float LeftGrip;
        public float RightTrigger;
        public float RightGrip;

        /// <summary>Bitmask of <see cref="StudyButton"/> positions held this frame.</summary>
        public uint Buttons;

        static public uint Bit(StudyButton button) {
            return 1u << (int)button;
        }

        public bool IsDown(StudyButton button) {
            return (Buttons & Bit(button)) != 0;
        }

        public void Set(StudyButton button, bool down) {
            if (down) {
                Buttons |= Bit(button);
            } else {
                Buttons &= ~Bit(button);
            }
        }
    }

    /// <summary>Returns false if poses are not available this frame.</summary>
    public delegate bool StudyPoseSourceFn(out Pose head, out Pose left, out Pose right);

    /// <summary>Returns false if controller input is not available this frame.</summary>
    public delegate bool StudyInputSourceFn(out StudyInputSample input);

    /// <summary>
    /// The app-supplied providers the shared logger reads. All optional: with none set the logger
    /// still writes its four files, and pose.csv simply gains no rows.
    /// </summary>
    static public class StudySources {
        /// <summary>Written into meta.json. Set by the app adapter at AfterAssembliesLoaded.</summary>
        static public string AppName = "unknown";

        static public StudyPoseSourceFn Pose;
        static public StudyInputSourceFn Input;
    }
}
