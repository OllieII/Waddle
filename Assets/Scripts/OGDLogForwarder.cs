//NSF Penguins VR Experience
//Mirrors every OGD analytics event into the local study log.

using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Drop-in stand-in for FieldDay.OGDLog that also writes each event to events.jsonl.
/// <para>PenguinAnalytics builds every event through BeginEvent / EventParam / SubmitEvent, so
/// wrapping those three is enough to capture all of them without touching the log methods
/// themselves. Everything else is passed straight through.</para>
/// </summary>
public sealed class OGDLogForwarder : IDisposable
{
    readonly FieldDay.OGDLog _inner;

    readonly StringBuilder _fields = new StringBuilder(256);
    string _eventName;
    bool _inEvent;

    public OGDLogForwarder(FieldDay.OGDLogConsts consts)
    {
        _inner = new FieldDay.OGDLog(consts);
    }

    /// <summary>
    /// The wrapped logger, for anything this class does not forward.
    /// </summary>
    public FieldDay.OGDLog Inner => _inner;

    #region Events

    public void BeginEvent(string name)
    {
        _inner.BeginEvent(name);

        _eventName = name;
        _fields.Length = 0;
        _inEvent = true;
    }

    public void EventParam(string name, string value)
    {
        _inner.EventParam(name, value);
        AddField(name, value);
    }

    public void EventParam(string name, int value)
    {
        _inner.EventParam(name, value);
        AddRawField(name, value.ToString(CultureInfo.InvariantCulture));
    }

    public void EventParam(string name, float value)
    {
        _inner.EventParam(name, value);
        AddRawField(name, value.ToString("F4", CultureInfo.InvariantCulture));
    }

    public void EventParam(string name, bool value)
    {
        _inner.EventParam(name, value);
        AddRawField(name, value ? "true" : "false");
    }

    public void SubmitEvent()
    {
        _inner.SubmitEvent();

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

    public void BeginGameState() { _inner.BeginGameState(); }

    public void GameStateParam(string name, string value) { _inner.GameStateParam(name, value); }

    public void GameStateParam(string name, float value) { _inner.GameStateParam(name, value); }

    public void GameStateParam(string name, bool value) { _inner.GameStateParam(name, value); }

    public void SubmitGameState() { _inner.SubmitGameState(); }

    //gaze batches are bulk viewport data and are covered by pose.csv, so they are not mirrored
    public void Log(string name, StringBuilder data) { _inner.Log(name, data); }

    public void UseFirebase(FieldDay.FirebaseConsts firebase) { _inner.UseFirebase(firebase); }

    public void SetUserId(string userId) { _inner.SetUserId(userId); }

    public long GetSessionId() { return _inner.GetSessionId(); }

    public void ResetSessionId() { _inner.ResetSessionId(); }

    public void Dispose() { _inner.Dispose(); }

    #endregion // Pass-through

    #region Field Building

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
