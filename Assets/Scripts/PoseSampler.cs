//NSF Penguins VR Experience
//Writes one pose.csv row per rendered frame.

using UnityEngine;

/// <summary>
/// Samples head and hand pose once per frame into the open study session's pose.csv.
/// <para>Sampling happens in Update, so one row corresponds to one rendered frame. Rows carry a
/// timestamp, so a uniform sample grid can be recovered afterwards if it is wanted.</para>
/// </summary>
public class PoseSampler : MonoBehaviour
{
    const float PlayerSearchIntervalSeconds = 1f;

    PenguinPlayer _player;
    float _nextPlayerSearch;
    long _frame;

    /// <summary>
    /// Rows written since this sampler started.
    /// </summary>
    public long FrameCount => _frame;

    void Update()
    {
        StudyLogger logger = StudyLogger.Instance;
        if (logger == null || !logger.IsOpen)
        {
            return;
        }

        if (!ResolvePlayer())
        {
            return;
        }

        Vector3 headPos;
        Quaternion headRot;
        _player.GetGaze(out headPos, out headRot);

        Vector3 leftPos;
        Quaternion leftRot;
        _player.GetHandTransform(true, out leftPos, out leftRot);

        Vector3 rightPos;
        Quaternion rightRot;
        _player.GetHandTransform(false, out rightPos, out rightRot);

        logger.Pose(_frame, headPos, headRot, leftPos, leftRot, rightPos, rightRot);
        _frame++;
    }

    /// <summary>
    /// Finds the player without going through PenguinPlayer.Instance, whose getter would build one
    /// if the scene had none. Retries on an interval rather than searching every frame.
    /// </summary>
    bool ResolvePlayer()
    {
        if (_player != null)
        {
            return true;
        }

        if (Time.realtimeSinceStartup < _nextPlayerSearch)
        {
            return false;
        }

        _nextPlayerSearch = Time.realtimeSinceStartup + PlayerSearchIntervalSeconds;
        _player = FindObjectOfType<PenguinPlayer>();

        return _player != null;
    }
}
