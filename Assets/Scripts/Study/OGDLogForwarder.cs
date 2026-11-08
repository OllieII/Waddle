//NSF Penguins VR Experience
//Mirrors every OGD analytics event into the local study log.

using System;
using System.Globalization;
using System.Text;
using PenguinStudy;

/// <summary>
/// Drop-in stand-in for FieldDay.OGDLog that also writes each event to events.jsonl.
/// <para>PenguinAnalytics builds every event through BeginEvent / EventParam / SubmitEvent, so
/// wrapping those three is enough to capture all of them without touching the log methods
/// themselves. Everything else is passed straight through.</para>
/// <para>The remote half only exists on a headset. Off device the inner OGDLog is never even
/// constructed, so an Editor or Simulator run writes its local session files and contacts nothing.
/// That is also what keeps local logging working with no network: the study files are written by
/// StudyLogger, which has no remote path at all.</para>
/// </summary>
public sealed class OGDLogForwarder : IDisposable
{
    //remote logging is a device-only concern; see the class comment
    readonly bool _remoteEnabled;
    readonly FieldDay.OGDLog _inner;

    readonly StringBuilder _fields = new StringBuilder(256);
    string _eventName;
    bool _inEvent;

    //stands in for the OGD session id when there is no OGD; seeds the same RNG and sign display
    long _localSessionId;

    public OGDLogForwarder(FieldDay.OGDLogConsts consts)
    {
        _remoteEnabled = RunMode.Current == RunMode.ApplicationRunMode.Device;

        if (_remoteEnabled)
        {
            _inner = new FieldDay.OGDLog(consts);
        }

        _localSessionId = NewLocalSessionId();
    }

    /// <summary>
    /// The wrapped logger, for anything this class does not forward. Null off device.
    /// </summary>
    public FieldDay.OGDLog Inner => _inner;

    /// <summary>
    /// True when events are being sent to OGD as well as written locally.
    /// </summary>
    public bool RemoteEnabled => _remoteEnabled;

    #region Events

    public void BeginEvent(string name)
    {
        if (_inner != null)
        {
            _inner.BeginEvent(name);
        }

        _eventName = name;
        _fields.Length = 0;
        _inEvent = true;
    }

    public void EventParam(string name, string value)
    {
        if (_inner != null)
        {
            _inner.EventParam(name, value);
        }

        AddField(name, value);
    }

    public void EventParam(string name, int value)
    {
        if (_inner != null)
        {
            _inner.EventParam(name, value);
        }

        AddRawField(name, value.ToString(CultureInfo.InvariantCulture));
    }

    public void EventParam(string name, float value)
    {
        if (_inner != null)
        {
            _inner.EventParam(name, value);
        }

        AddRawField(name, value.ToString("F4", CultureInfo.InvariantCulture));
    }

    public void EventParam(string name, bool value)
    {
        if (_inner != null)
        {
            _inner.EventParam(name, value);
        }

        AddRawField(name, value ? "true" : "false");
    }

    public void SubmitEvent()
    {
        if (_inner != null)
        {
            _inner.SubmitEvent();
        }

        if (_inEvent)
        {
            StudyLogger logger = StudyLogger.Instance;
            if (logger != null)
            {
                logger.EventJson(_eventName, _fields.ToString());
            }

            _inEvent = false;
        }
    }

    #endregion // Events

    #region Pass-through

    public void BeginGameState() { if (_inner != null) { _inner.BeginGameState(); } }

    public void GameStateParam(string name, string value) { if (_inner != null) { _inner.GameStateParam(name, value); } }

    public void GameStateParam(string name, float value) { if (_inner != null) { _inner.GameStateParam(name, value); } }

    public void GameStateParam(string name, bool value) { if (_inner != null) { _inner.GameStateParam(name, value); } }

    public void SubmitGameState() { if (_inner != null) { _inner.SubmitGameState(); } }

    //gaze batches are bulk viewport data and are covered by pose.csv, so they are not mirrored
    public void Log(string name, StringBuilder data) { if (_inner != null) { _inner.Log(name, data); } }

    public void UseFirebase(FieldDay.FirebaseConsts firebase) { if (_inner != null) { _inner.UseFirebase(firebase); } }

    public void SetUserId(string userId) { if (_inner != null) { _inner.SetUserId(userId); } }

    public long GetSessionId() { return _inner != null ? _inner.GetSessionId() : _localSessionId; }

    public void ResetSessionId()
    {
        if (_inner != null)
        {
            _inner.ResetSessionId();
        }

        _localSessionId = NewLocalSessionId();
    }

    public void Dispose()
    {
        if (_inner != null)
        {
            _inner.Dispose();
        }
    }

    #endregion // Pass-through

    #region Field Building

    static long NewLocalSessionId()
    {
        //positive and roughly time-ordered, like the ids OGD hands out
        return DateTime.UtcNow.Ticks & 0x7FFFFFFFFFFFFFL;
    }

    void AddField(string name, string value)
    {
        if (!_inEvent)
        {
            return;
        }

        Separate();
        _fields.Append('"');
        StudyLogger.EscapeJson(_fields, name);
        _fields.Append("\":\"");
        StudyLogger.EscapeJson(_fields, value);
        _fields.Append('"');
    }

    void AddRawField(string name, string jsonValue)
    {
        if (!_inEvent)
        {
            return;
        }

        Separate();
        _fields.Append('"');
        StudyLogger.EscapeJson(_fields, name);
        _fields.Append("\":").Append(jsonValue);
    }

    void Separate()
    {
        if (_fields.Length > 0)
        {
            _fields.Append(',');
        }
    }

    #endregion // Field Building
}
