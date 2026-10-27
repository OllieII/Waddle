//NSF Penguins VR Experience
//Local study logger - one session folder holding meta.json, pose.csv, events.jsonl and ratings.jsonl

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Writes one study session to disk.
/// <para>Creates Sessions/{id}_{time}/ under Application.persistentDataPath on startup and opens
/// four files inside it. Lines are buffered in memory and flushed to disk once a second on a
/// background thread, so logging never blocks a frame on file IO.</para>
/// </summary>
public class StudyLogger : MonoBehaviour
{
    public const string MetaFileName = "meta.json";
    public const string PoseFileName = "pose.csv";
    public const string EventsFileName = "events.jsonl";
    public const string RatingsFileName = "ratings.jsonl";

    public const string PoseHeader =
        "frame,t," +
        "head_x,head_y,head_z,head_qx,head_qy,head_qz,head_qw," +
        "lhand_x,lhand_y,lhand_z,lhand_qx,lhand_qy,lhand_qz,lhand_qw," +
        "rhand_x,rhand_y,rhand_z,rhand_qx,rhand_qy,rhand_qz,rhand_qw";

    const float FlushIntervalSeconds = 1f;

    /// <summary>
    /// Raised after an event has been written to events.jsonl.
    /// <para>Fired on the thread that called Event, which is the main thread for game code.</para>
    /// </summary>
    public static event Action<string, string> OnEvent;

    public static StudyLogger Instance { get; private set; }

    #region Session

    /// <summary>
    /// A single append-only file with a buffer that the flush thread drains.
    /// </summary>
    class LogFile
    {
        public readonly string Path;

        readonly object _lock = new object();
        StringBuilder _pending = new StringBuilder(8192);
        StringBuilder _draining = new StringBuilder(8192);

        public int LineCount { get; private set; }

        public LogFile(string path, string header)
        {
            Path = path;

            //create the file straight away so the session folder is complete from the start
            try
            {
                using (StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8))
                {
                    if (!string.IsNullOrEmpty(header))
                    {
                        writer.Write(header);
                        writer.Write('\n');
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat("[StudyLogger] could not create {0}: {1}", path, e.Message);
            }
        }

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                _pending.Append(line).Append('\n');
                LineCount++;
            }
        }

        public void Flush()
        {
            StringBuilder toWrite;

            lock (_lock)
            {
                if (_pending.Length == 0)
                {
                    return;
                }

                toWrite = _pending;
                _pending = _draining;
                _draining = toWrite;
                _pending.Length = 0;
            }

            try
            {
                using (StreamWriter writer = new StreamWriter(Path, true, Encoding.UTF8))
                {
                    writer.Write(toWrite.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat("[StudyLogger] failed writing {0}: {1}", Path, e.Message);
            }

            toWrite.Length = 0;
        }
    }

    string _sessionId;
    string _directory;
    float _startTime;

    LogFile _pose;
    LogFile _events;
    LogFile _ratings;

    Thread _flushThread;
    volatile bool _flushThreadShouldRun;

    /// <summary>
    /// Identifier used in the session folder name.
    /// </summary>
    public string SessionId => _sessionId;

    /// <summary>
    /// Folder holding this session's four files, or null if it could not be created.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// True once the session folder and files are open.
    /// </summary>
    public bool IsOpen => _directory != null;

    public int EventCount => _events != null ? _events.LineCount : 0;

    public int PoseRowCount => _pose != null ? _pose.LineCount : 0;

    public int RatingCount => _ratings != null ? _ratings.LineCount : 0;

    #endregion // Session

    #region Unity Events

    /// <summary>
    /// Creates the logger without needing it placed in a scene or prefab.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject host = new GameObject("StudyLogger");
        DontDestroyOnLoad(host);
        host.AddComponent<StudyLogger>();
        host.AddComponent<PoseSampler>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        //open in Awake rather than Start so nothing logged on the first frame is lost
        OpenSession(DateTime.Now.Ticks.ToString("x"));
    }

    void OnApplicationPause(bool paused)
    {
        //the headset can kill a backgrounded app, so get everything on disk now
        if (paused)
        {
            FlushAll();
        }
    }

    void OnApplicationQuit()
    {
        CloseSession();
    }

    void OnDestroy()
    {
        CloseSession();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion // Unity Events

    #region Lifecycle

    /// <summary>
    /// Creates Sessions/{id}_{time}/ and opens the four files inside it.
    /// </summary>
    public void OpenSession(string sessionId)
    {
        if (IsOpen)
        {
            CloseSession();
        }

        _sessionId = string.IsNullOrEmpty(sessionId) ? "unknown" : sessionId;
        _startTime = Time.realtimeSinceStartup;

        string folderName = string.Format("{0}_{1:yyyyMMdd_HHmmss}", _sessionId, DateTime.Now);
        string path = Path.Combine(Path.Combine(Application.persistentDataPath, "Sessions"), folderName);

        try
        {
            System.IO.Directory.CreateDirectory(path);
        }
        catch (Exception e)
        {
            Debug.LogWarningFormat("[StudyLogger] could not create session folder {0}: {1}", path, e.Message);
            _directory = null;
            return;
        }

        _directory = path;

        _pose = new LogFile(Path.Combine(_directory, PoseFileName), PoseHeader);
        _events = new LogFile(Path.Combine(_directory, EventsFileName), null);
        _ratings = new LogFile(Path.Combine(_directory, RatingsFileName), null);

        WriteMeta(false);

        _flushThreadShouldRun = true;
        _flushThread = new Thread(FlushLoop);
        _flushThread.IsBackground = true;
        _flushThread.Start();

        Debug.LogFormat("[StudyLogger] session open at {0}", _directory);
    }

    /// <summary>
    /// Stops the flush thread, writes out everything buffered, and finalizes meta.json.
    /// </summary>
    public void CloseSession()
    {
        if (!IsOpen)
        {
            return;
        }

        _flushThreadShouldRun = false;

        if (_flushThread != null)
        {
            _flushThread.Join(2000);
            _flushThread = null;
        }

        FlushAll();
        WriteMeta(true);

        _pose = null;
        _events = null;
        _ratings = null;
        _directory = null;
    }

    /// <summary>
    /// Writes every buffered line to disk immediately.
    /// </summary>
    public void FlushAll()
    {
        if (_pose != null)
        {
            _pose.Flush();
        }
        if (_events != null)
        {
            _events.Flush();
        }
        if (_ratings != null)
        {
            _ratings.Flush();
        }
    }

    void FlushLoop()
    {
        int intervalMS = Mathf.CeilToInt(FlushIntervalSeconds * 1000);

        while (_flushThreadShouldRun)
        {
            Thread.Sleep(intervalMS);
            FlushAll();
        }
    }

    #endregion // Lifecycle

    #region Writing

    /// <summary>
    /// Writes one line to events.jsonl and raises OnEvent.
    /// </summary>
    /// <param name="name">Event name.</param>
    /// <param name="data">Optional payload, written as a JSON string value.</param>
    public void Event(string name, string data = null)
    {
        if (_events != null)
        {
            StringBuilder line = new StringBuilder(128);
            line.Append("{\"t\":").Append(Seconds(SessionSeconds(), 4));
            line.Append(",\"name\":\"");
            EscapeJson(line, name);
            line.Append('"');

            if (data != null)
            {
                line.Append(",\"data\":\"");
                EscapeJson(line, data);
                line.Append('"');
            }

            line.Append('}');

            _events.AppendLine(line.ToString());
        }

        RaiseOnEvent(name, data);
    }

    /// <summary>
    /// Writes one line to events.jsonl whose fields are already JSON-formatted, and raises OnEvent.
    /// </summary>
    public void EventJson(string name, string jsonFields)
    {
        if (_events != null)
        {
            StringBuilder line = new StringBuilder(256);
            line.Append("{\"t\":").Append(Seconds(SessionSeconds(), 4));
            line.Append(",\"name\":\"");
            EscapeJson(line, name);
            line.Append('"');

            if (!string.IsNullOrEmpty(jsonFields))
            {
                line.Append(",\"fields\":{").Append(jsonFields).Append('}');
            }

            line.Append('}');

            _events.AppendLine(line.ToString());
        }

        RaiseOnEvent(name, jsonFields);
    }

    void RaiseOnEvent(string name, string data)
    {
        Action<string, string> handler = OnEvent;
        if (handler != null)
        {
            //a listener throwing must not stop the session
            try
            {
                handler(name, data);
            }
            catch (Exception e)
            {
                Debug.LogWarningFormat("[StudyLogger] OnEvent listener threw on '{0}': {1}", name, e.Message);
            }
        }
    }

    /// <summary>
    /// Writes one row to pose.csv. Columns match PoseHeader.
    /// </summary>
    public void Pose(long frame,
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot)
    {
        if (_pose == null)
        {
            return;
        }

        StringBuilder row = new StringBuilder(256);
        row.Append(frame).Append(',').Append(Seconds(SessionSeconds(), 4));
        AppendPose(row, headPos, headRot);
        AppendPose(row, leftPos, leftRot);
        AppendPose(row, rightPos, rightRot);

        _pose.AppendLine(row.ToString());
    }

    static void AppendPose(StringBuilder sb, Vector3 pos, Quaternion rot)
    {
        sb.Append(',').Append(pos.x.ToString("F4", CultureInfo.InvariantCulture));
        sb.Append(',').Append(pos.y.ToString("F4", CultureInfo.InvariantCulture));
        sb.Append(',').Append(pos.z.ToString("F4", CultureInfo.InvariantCulture));
        sb.Append(',').Append(rot.x.ToString("F5", CultureInfo.InvariantCulture));
        sb.Append(',').Append(rot.y.ToString("F5", CultureInfo.InvariantCulture));
        sb.Append(',').Append(rot.z.ToString("F5", CultureInfo.InvariantCulture));
        sb.Append(',').Append(rot.w.ToString("F5", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes one line to ratings.jsonl.
    /// </summary>
    public void Rating(string promptId, int value, string note = null)
    {
        if (_ratings == null)
        {
            return;
        }

        StringBuilder line = new StringBuilder(128);
        line.Append("{\"t\":").Append(Seconds(SessionSeconds(), 4));
        line.Append(",\"prompt\":\"");
        EscapeJson(line, promptId);
        line.Append("\",\"value\":").Append(value);

        if (!string.IsNullOrEmpty(note))
        {
            line.Append(",\"note\":\"");
            EscapeJson(line, note);
            line.Append('"');
        }

        line.Append('}');

        _ratings.AppendLine(line.ToString());
    }

    static string Seconds(float value, int decimals)
    {
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    float SessionSeconds()
    {
        return Time.realtimeSinceStartup - _startTime;
    }

    #endregion // Writing

    #region Meta

    void WriteMeta(bool final)
    {
        if (_directory == null)
        {
            return;
        }

        StringBuilder sb = new StringBuilder(512);
        sb.Append("{\n");
        sb.Append("  \"session_id\": \"").Append(_sessionId).Append("\",\n");
        sb.Append("  \"started_utc\": \"").Append(DateTime.UtcNow.ToString("o")).Append("\",\n");
        sb.Append("  \"app_version\": \"").Append(Application.version).Append("\",\n");
        sb.Append("  \"platform\": \"").Append(Application.platform).Append("\",\n");
        sb.Append("  \"target_frame_rate\": ").Append(Application.targetFrameRate).Append(",\n");
        sb.Append("  \"duration_seconds\": ").Append(Seconds(SessionSeconds(), 3)).Append(",\n");
        sb.Append("  \"pose_rows\": ").Append(PoseRowCount).Append(",\n");
        sb.Append("  \"event_count\": ").Append(EventCount).Append(",\n");
        sb.Append("  \"rating_count\": ").Append(RatingCount).Append(",\n");
        sb.Append("  \"complete\": ").Append(final ? "true" : "false").Append('\n');
        sb.Append("}\n");

        try
        {
            File.WriteAllText(Path.Combine(_directory, MetaFileName), sb.ToString(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogWarningFormat("[StudyLogger] could not write {0}: {1}", MetaFileName, e.Message);
        }
    }

    #endregion // Meta

    #region Utility

    public static void EscapeJson(StringBuilder sb, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
    }

    #endregion // Utility
}
