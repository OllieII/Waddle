// T2: local study logger. One session folder holding meta.json, pose.csv, events.jsonl and ratings.jsonl.
// Identical file in Waddle and pennycookvr (Assets/StudyLogging/). App-specific access lives in each
// app's adapter, behind StudySources.
//
// Schema, units and the buttons bit mapping are documented in Assets/StudyLogging/README.md.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PenguinStudy {
    /// <summary>
    /// Writes one study session to disk.
    /// <para>Creates Sessions/{id}_{time}/ under Application.persistentDataPath on startup and opens
    /// four files inside it. Lines are buffered in memory and flushed to disk once a second on a
    /// background thread, so logging never blocks a frame on file IO.</para>
    /// <para>Entirely local: nothing here contacts a network service, so a session runs normally in
    /// airplane mode.</para>
    /// </summary>
    public class StudyLogger : MonoBehaviour {
        public const string MetaFileName = "meta.json";
        public const string PoseFileName = "pose.csv";
        public const string EventsFileName = "events.jsonl";
        public const string RatingsFileName = "ratings.jsonl";

        public const string SessionStartEvent = "SESSION_START";
        public const string SessionEndEvent = "SESSION_END";

        /// <summary>Columns of pose.csv. See README.md for units and the buttons bit mapping.</summary>
        public const string PoseHeader =
            "frame,t_lsl,t_unity,scene," +
            "head_x,head_y,head_z,head_qx,head_qy,head_qz,head_qw," +
            "lhand_x,lhand_y,lhand_z,lhand_qx,lhand_qy,lhand_qz,lhand_qw," +
            "rhand_x,rhand_y,rhand_z,rhand_qx,rhand_qy,rhand_qz,rhand_qw," +
            "l_trigger,l_grip,r_trigger,r_grip,buttons";

        const float FlushIntervalSeconds = 1f;

        /// <summary>
        /// Raised after an event has been written to events.jsonl. The second argument is always a
        /// complete JSON object, so the signature matches StudyLsl.Marker.
        /// <para>Fired on the thread that called Event, which is the main thread for game code.</para>
        /// </summary>
        public static event Action<string, string> OnEvent;

        public static StudyLogger Instance { get; private set; }

        #region Session

        /// <summary>
        /// A single append-only file with a buffer that the flush thread drains.
        /// </summary>
        class LogFile {
            public readonly string Path;

            //_bufferLock guards the two buffers; _writeLock serializes whole flushes. Both are needed:
            //OnApplicationPause flushes on the main thread while the background thread may be mid-flush,
            //and swapping under one lock but writing outside it lets two writers interleave and drop lines.
            readonly object _bufferLock = new object();
            readonly object _writeLock = new object();

            StringBuilder _pending = new StringBuilder(8192);
            StringBuilder _draining = new StringBuilder(8192);

            int _lineCount;

            public int LineCount { get { lock (_bufferLock) { return _lineCount; } } }

            public LogFile(string path, string header) {
                Path = path;

                //create the file straight away so the session folder is complete from the start
                try {
                    using (StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8)) {
                        if (!string.IsNullOrEmpty(header)) {
                            writer.Write(header);
                            writer.Write('\n');
                        }
                    }
                } catch (Exception e) {
                    Debug.LogWarningFormat("[StudyLogger] could not create {0}: {1}", path, e.Message);
                }
            }

            public void AppendLine(string line) {
                lock (_bufferLock) {
                    _pending.Append(line).Append('\n');
                    _lineCount++;
                }
            }

            public void Flush() {
                //held for the whole flush, so a second flusher waits rather than racing this one
                lock (_writeLock) {
                    StringBuilder toWrite;

                    lock (_bufferLock) {
                        if (_pending.Length == 0) {
                            return;
                        }

                        toWrite = _pending;
                        _pending = _draining;
                        _draining = toWrite;
                        _pending.Length = 0;
                    }

                    try {
                        using (StreamWriter writer = new StreamWriter(Path, true, Encoding.UTF8)) {
                            writer.Write(toWrite.ToString());
                        }
                    } catch (Exception e) {
                        Debug.LogWarningFormat("[StudyLogger] failed writing {0}: {1}", Path, e.Message);
                    }

                    toWrite.Length = 0;
                }
            }
        }

        string _sessionId;
        string _folderName;
        string _directory;
        string _scene = "";

        //the real session start, kept so the final meta rewrite cannot overwrite it with the end time
        DateTime _startedUtc;
        double _startUnity;

        bool _startEmitted;
        bool _endEmitted;

        //static, because OnEvent is static: an instance flag would let a second logger instance
        //subscribe StudyLsl.Marker a second time and double every marker
        static bool s_LslHooked;

        LogFile _pose;
        LogFile _events;
        LogFile _ratings;

        Thread _flushThread;
        volatile bool _flushThreadShouldRun;

        /// <summary>Identifier used in the session folder name.</summary>
        public string SessionId { get { return _sessionId; } }

        /// <summary>Name of this session's folder, as reported in SESSION_START.</summary>
        public string FolderName { get { return _folderName; } }

        /// <summary>Folder holding this session's four files, or null if it could not be created.</summary>
        public string Directory { get { return _directory; } }

        /// <summary>True once the session folder and files are open.</summary>
        public bool IsOpen { get { return _directory != null; } }

        /// <summary>Name of the scene currently active, as written into every row and event.</summary>
        public string Scene { get { return _scene; } }

        public int EventCount { get { return _events != null ? _events.LineCount : 0; } }

        public int PoseRowCount { get { return _pose != null ? _pose.LineCount : 0; } }

        public int RatingCount { get { return _ratings != null ? _ratings.LineCount : 0; } }

        #endregion // Session

        #region Unity Events

        /// <summary>
        /// Creates the logger without needing it placed in a scene or prefab.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() {
            if (Instance != null) {
                return;
            }

            GameObject host = new GameObject("StudyLogger");
            DontDestroyOnLoad(host);
            host.AddComponent<StudyLogger>();
            host.AddComponent<StudyPoseSampler>();
        }

        void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _scene = SceneManager.GetActiveScene().name;
            SceneManager.activeSceneChanged += HandleSceneChanged;

            //open in Awake rather than Start so nothing logged on the first frame is lost
            OpenSession(DateTime.Now.Ticks.ToString("x"));
        }

        void OnApplicationPause(bool paused) {
            //the headset can kill a backgrounded app, so get everything on disk now
            if (paused) {
                FlushAll();
                WriteMeta(false);
            }
        }

        void OnApplicationQuit() {
            CloseSession("quit");
        }

        void OnDestroy() {
            if (Instance != this) {
                return;
            }

            SceneManager.activeSceneChanged -= HandleSceneChanged;
            CloseSession("destroy");
            Instance = null;
        }

        void HandleSceneChanged(Scene from, Scene to) {
            _scene = to.name;
        }

        #endregion // Unity Events

        #region Lifecycle

        /// <summary>
        /// Creates Sessions/{id}_{time}/ and opens the four files inside it.
        /// </summary>
        public void OpenSession(string sessionId) {
            if (IsOpen) {
                CloseSession("reopen");
            }

            _sessionId = string.IsNullOrEmpty(sessionId) ? "unknown" : sessionId;
            _startedUtc = DateTime.UtcNow;
            _startUnity = Time.realtimeSinceStartupAsDouble;
            _startEmitted = false;
            _endEmitted = false;

            _folderName = string.Format("{0}_{1:yyyyMMdd_HHmmss}", _sessionId, DateTime.Now);
            string path = Path.Combine(Path.Combine(Application.persistentDataPath, "Sessions"), _folderName);

            try {
                System.IO.Directory.CreateDirectory(path);
            } catch (Exception e) {
                Debug.LogWarningFormat("[StudyLogger] could not create session folder {0}: {1}", path, e.Message);
                _directory = null;
                return;
            }

            _directory = path;

            _pose = new LogFile(Path.Combine(_directory, PoseFileName), PoseHeader);
            _events = new LogFile(Path.Combine(_directory, EventsFileName), null);
            _ratings = new LogFile(Path.Combine(_directory, RatingsFileName), null);

            //complete:false from here until a clean close, so an interrupted session is visible as itself
            WriteMeta(false);

            _flushThreadShouldRun = true;
            _flushThread = new Thread(FlushLoop);
            _flushThread.IsBackground = true;
            _flushThread.Start();

            //hooked before SESSION_START is emitted, so the start marker reaches LSL too
            HookLsl();

            Debug.LogFormat("[StudyLogger] session open at {0}", _directory);

            if (!_startEmitted) {
                _startEmitted = true;

                StringBuilder data = new StringBuilder(128);
                data.Append("{\"session_folder\":\"");
                EscapeJson(data, _folderName);
                data.Append("\",\"session_id\":\"");
                EscapeJson(data, _sessionId);
                data.Append("\",\"app\":\"");
                EscapeJson(data, StudySources.AppName);
                data.Append("\"}");

                Event(SessionStartEvent, data.ToString());
            }
        }

        /// <summary>
        /// Emits SESSION_END, stops the flush thread, writes out everything buffered, and finalizes meta.json.
        /// </summary>
        /// <param name="reason">Recorded in meta.json as end_reason.</param>
        public void CloseSession(string reason = "close") {
            if (!IsOpen) {
                return;
            }

            //emitted while the files are still open, so it lands in events.jsonl and on the marker stream
            if (_startEmitted && !_endEmitted) {
                _endEmitted = true;
                Event(SessionEndEvent, "{\"reason\":\"" + reason + "\"}");
            }

            _flushThreadShouldRun = false;

            if (_flushThread != null) {
                _flushThread.Join(2000);
                _flushThread = null;
            }

            FlushAll();
            WriteMeta(true, reason);

            UnhookLsl();

            _pose = null;
            _events = null;
            _ratings = null;
            _directory = null;
        }

        /// <summary>
        /// Writes every buffered line to disk immediately.
        /// </summary>
        public void FlushAll() {
            if (_pose != null) {
                _pose.Flush();
            }
            if (_events != null) {
                _events.Flush();
            }
            if (_ratings != null) {
                _ratings.Flush();
            }
        }

        void FlushLoop() {
            int intervalMS = Mathf.CeilToInt(FlushIntervalSeconds * 1000);

            while (_flushThreadShouldRun) {
                Thread.Sleep(intervalMS);
                FlushAll();
            }
        }

        #endregion // Lifecycle

        #region LSL

        //T3 owns the transport; T2 only feeds it. The flag keeps a reload or a second OpenSession
        //from subscribing twice and doubling every marker.
        static void HookLsl() {
            if (s_LslHooked) {
                return;
            }

            OnEvent += StudyLsl.Marker;
            s_LslHooked = true;
        }

        static void UnhookLsl() {
            if (!s_LslHooked) {
                return;
            }

            OnEvent -= StudyLsl.Marker;
            s_LslHooked = false;
        }

        #endregion // LSL

        #region Writing

        /// <summary>
        /// Writes one line to events.jsonl and raises OnEvent.
        /// </summary>
        /// <param name="name">Event name.</param>
        /// <param name="dataJson">Optional payload as a complete JSON object, or null for {}.</param>
        public void Event(string name, string dataJson = null) {
            WriteEvent(name, string.IsNullOrEmpty(dataJson) ? "{}" : dataJson);
        }

        /// <summary>
        /// Writes one line to events.jsonl from already-formatted object fields ("a":1,"b":"c"),
        /// and raises OnEvent.
        /// </summary>
        public void EventJson(string name, string jsonFields) {
            WriteEvent(name, string.IsNullOrEmpty(jsonFields) ? "{}" : "{" + jsonFields + "}");
        }

        //the one envelope: {"t_lsl":..,"t_unity":..,"scene":"..","event":"NAME","data":{..}}
        void WriteEvent(string name, string dataObject) {
            if (_events != null) {
                StringBuilder line = new StringBuilder(256);
                line.Append('{');
                AppendClocks(line);
                line.Append(",\"scene\":\"");
                EscapeJson(line, _scene);
                line.Append("\",\"event\":\"");
                EscapeJson(line, name);
                line.Append("\",\"data\":").Append(dataObject);
                line.Append('}');

                _events.AppendLine(line.ToString());
            }

            RaiseOnEvent(name, dataObject);
        }

        void RaiseOnEvent(string name, string dataObject) {
            Action<string, string> handler = OnEvent;
            if (handler != null) {
                //a listener throwing must not stop the session
                try {
                    handler(name, dataObject);
                } catch (Exception e) {
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
            Vector3 rightPos, Quaternion rightRot,
            StudyInputSample input) {
            if (_pose == null) {
                return;
            }

            StringBuilder row = new StringBuilder(320);
            row.Append(frame).Append(',');
            AppendLslCsv(row);
            row.Append(',').Append(UnityNow().ToString("F4", CultureInfo.InvariantCulture));
            row.Append(',');
            AppendCsvText(row, _scene);

            AppendPose(row, headPos, headRot);
            AppendPose(row, leftPos, leftRot);
            AppendPose(row, rightPos, rightRot);

            row.Append(',').Append(input.LeftTrigger.ToString("F3", CultureInfo.InvariantCulture));
            row.Append(',').Append(input.LeftGrip.ToString("F3", CultureInfo.InvariantCulture));
            row.Append(',').Append(input.RightTrigger.ToString("F3", CultureInfo.InvariantCulture));
            row.Append(',').Append(input.RightGrip.ToString("F3", CultureInfo.InvariantCulture));
            row.Append(',').Append(input.Buttons.ToString(CultureInfo.InvariantCulture));

            _pose.AppendLine(row.ToString());
        }

        static void AppendPose(StringBuilder sb, Vector3 pos, Quaternion rot) {
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
        public void Rating(string promptId, int value, string note = null) {
            if (_ratings == null) {
                return;
            }

            StringBuilder line = new StringBuilder(192);
            line.Append('{');
            AppendClocks(line);
            line.Append(",\"scene\":\"");
            EscapeJson(line, _scene);
            line.Append("\",\"prompt\":\"");
            EscapeJson(line, promptId);
            line.Append("\",\"value\":").Append(value.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(note)) {
                line.Append(",\"note\":\"");
                EscapeJson(line, note);
                line.Append('"');
            }

            line.Append('}');

            _ratings.AppendLine(line.ToString());
        }

        #endregion // Writing

        #region Clocks

        /// <summary>Seconds on the Unity clock. Paired with t_lsl on every row and event.</summary>
        static public double UnityNow() {
            return Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>The LSL study clock, or NaN when LSL is unavailable.</summary>
        static public double LslNow() {
            try {
                return StudyLsl.Now;
            } catch (Exception) {
                return double.NaN;
            }
        }

        //JSON has no NaN literal, so an unavailable LSL clock is written as null rather than
        //something a strict parser would reject.
        void AppendClocks(StringBuilder sb) {
            double lsl = LslNow();

            sb.Append("\"t_lsl\":");
            if (double.IsNaN(lsl) || double.IsInfinity(lsl)) {
                sb.Append("null");
            } else {
                sb.Append(lsl.ToString("R", CultureInfo.InvariantCulture));
            }

            sb.Append(",\"t_unity\":").Append(UnityNow().ToString("R", CultureInfo.InvariantCulture));
        }

        //the CSV counterpart: an unavailable clock leaves the field empty
        static void AppendLslCsv(StringBuilder sb) {
            double lsl = LslNow();
            if (!double.IsNaN(lsl) && !double.IsInfinity(lsl)) {
                sb.Append(lsl.ToString("F6", CultureInfo.InvariantCulture));
            }
        }

        #endregion // Clocks

        #region Meta

        void WriteMeta(bool final, string endReason = null) {
            if (_directory == null) {
                return;
            }

            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\n");
            AppendMetaString(sb, "session_id", _sessionId);
            AppendMetaString(sb, "session_folder", _folderName);
            AppendMetaString(sb, "app", StudySources.AppName);
            AppendMetaString(sb, "app_version", Application.version);
            AppendMetaString(sb, "build_commit", StudyBuildInfo.Commit);
            AppendMetaString(sb, "build_id", StudyBuildInfo.BuildId);
            AppendMetaString(sb, "unity_version", Application.unityVersion);
            AppendMetaString(sb, "platform", Application.platform.ToString());
            AppendMetaString(sb, "device_model", SystemInfo.deviceModel);
            AppendMetaString(sb, "device_id", SystemInfo.deviceUniqueIdentifier);

            //the real start, written once at OpenSession and reused here so the final rewrite
            //cannot stamp the end time into started_utc
            AppendMetaString(sb, "started_utc", _startedUtc.ToString("o", CultureInfo.InvariantCulture));

            sb.Append("  \"ended_utc\": ");
            if (final) {
                sb.Append('"').Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append('"');
            } else {
                sb.Append("null");
            }
            sb.Append(",\n");

            sb.Append("  \"end_reason\": ");
            if (final && !string.IsNullOrEmpty(endReason)) {
                sb.Append('"').Append(endReason).Append('"');
            } else {
                sb.Append("null");
            }
            sb.Append(",\n");

            sb.Append("  \"display_refresh_rate\": ")
              .Append(UnityEngine.XR.XRDevice.refreshRate.ToString("F2", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"target_frame_rate\": ")
              .Append(Application.targetFrameRate.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"duration_seconds\": ")
              .Append(SessionSeconds().ToString("F3", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"pose_rows\": ").Append(PoseRowCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"event_count\": ").Append(EventCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"rating_count\": ").Append(RatingCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");

            //false until a clean close. A force-killed process leaves it false: that is the record of
            //the interruption, and no SESSION_END is invented after the fact.
            sb.Append("  \"complete\": ").Append(final ? "true" : "false").Append('\n');
            sb.Append("}\n");

            try {
                File.WriteAllText(Path.Combine(_directory, MetaFileName), sb.ToString(), Encoding.UTF8);
            } catch (Exception e) {
                Debug.LogWarningFormat("[StudyLogger] could not write {0}: {1}", MetaFileName, e.Message);
            }
        }

        static void AppendMetaString(StringBuilder sb, string key, string value) {
            sb.Append("  \"").Append(key).Append("\": \"");
            EscapeJson(sb, value);
            sb.Append("\",\n");
        }

        double SessionSeconds() {
            return UnityNow() - _startUnity;
        }

        #endregion // Meta

        #region Utility

        public static void EscapeJson(StringBuilder sb, string value) {
            if (string.IsNullOrEmpty(value)) {
                return;
            }

            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') {
                            sb.Append(' ');
                        } else {
                            sb.Append(c);
                        }
                        break;
                }
            }
        }

        //scene names are author-controlled, so the CSV field is quoted only when it has to be
        static void AppendCsvText(StringBuilder sb, string value) {
            if (string.IsNullOrEmpty(value)) {
                return;
            }

            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0) {
                sb.Append(value);
                return;
            }

            sb.Append('"');
            for (int i = 0; i < value.Length; i++) {
                char c = value[i];
                if (c == '"') {
                    sb.Append("\"\"");
                } else if (c == '\n' || c == '\r') {
                    sb.Append(' ');
                } else {
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }

        #endregion // Utility
    }
}
