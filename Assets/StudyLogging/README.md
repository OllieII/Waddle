# StudyLogging (T2)

One shared local logger. The contents of this folder are app-agnostic and kept equivalent in Waddle
and pennycookvr; everything app-specific lives in each app's adapter.

- Waddle adapter: `Assets/Scripts/Study/WaddleStudyAdapter.cs`
- Waddle analytics bridge: `Assets/Scripts/Study/OGDLogForwarder.cs`

The logger is entirely local. Nothing in this folder opens a socket, so a session runs normally in
airplane mode. Remote analytics is a separate, device-only path owned by each app's adapter.

## Session folder

Created at startup under `Application.persistentDataPath`:

```
Sessions/{session_id}_{yyyyMMdd_HHmmss}/
  meta.json
  pose.csv
  events.jsonl
  ratings.jsonl
```

Lines are buffered in memory and flushed once a second on a background thread, plus on pause and on
a clean exit. **The worst-case loss window on a force kill is therefore the flush interval, one
second** — this is a bound from the design, not a measurement; the measured figure belongs with the
headset evidence.

## Clocks

Every pose row, event and rating carries both:

| field | source | meaning |
|---|---|---|
| `t_lsl` | `PenguinStudy.StudyLsl.Now` | the LSL study clock, shared with the T3 streams and the XDF recording |
| `t_unity` | `Time.realtimeSinceStartupAsDouble` | seconds since process start |

`t_lsl` is unavailable when LSL failed to load. JSON has no `NaN` literal, so it is written as
`null` in JSON and left **empty** in CSV. Neither file ever contains `NaN`.

## events.jsonl

One JSON object per line, always this envelope:

```json
{"t_lsl":1234.5,"t_unity":12.75,"scene":"Nest","event":"BUTTON_PRESS","data":{"button":"A"}}
```

`data` is always an object — never a JSON-encoded string, never absent. An event with no payload
carries `"data":{}`.

`scene` is the active scene at the moment of the event, tracked across scene changes.

`StudyLogger.OnEvent(name, dataJson)` fires for every event with `dataJson` as the complete `data`
object, which is the signature `StudyLsl.Marker` takes. The logger subscribes `StudyLsl.Marker` to
it once per open session and unsubscribes on close, so a reload or restart cannot double markers.

### Lifecycle events

`SESSION_START` and `SESSION_END` are emitted once per session. `SESSION_START` carries the session
folder name, so a local folder can be matched to its XDF recording:

```json
{"session_folder":"18f2c_20261106_143012","session_id":"18f2c","app":"waddle"}
```

A force-killed process cannot emit `SESSION_END`. No end event is invented on the next run; the
interruption is recorded by `meta.json` keeping `"complete": false`.

## pose.csv

One row per **rendered** frame, sampled from `Application.onBeforeRender` — the same hook T3's pose
stream uses, so a row and an LSL pose sample describe the same frame.

| column | unit |
|---|---|
| `frame` | `Time.frameCount` (not a row counter: a gap means a frame produced no sample) |
| `t_lsl`, `t_unity` | seconds |
| `scene` | active scene name, CSV-quoted only if it needs it |
| `head_*`, `lhand_*`, `rhand_*` | `x,y,z` in metres, world space; `qx,qy,qz,qw` unit quaternion |
| `l_trigger`, `l_grip`, `r_trigger`, `r_grip` | analog, 0..1 |
| `buttons` | unsigned decimal bitmask, see below |

### buttons bit mapping

Bit *n* is set while that control is held. The numbering is the study's own, not an SDK enum, so the
column means the same thing in both apps and survives an SDK renumbering; each adapter maps its own
input stack onto these bits.

| bit | control | bit | control |
|---|---|---|---|
| 0 | A | 10 | right grip |
| 1 | B | 11 | left thumbstick up |
| 2 | X | 12 | left thumbstick down |
| 3 | Y | 13 | left thumbstick left |
| 4 | Menu / Start | 14 | left thumbstick right |
| 5 | left thumbstick click | 15 | right thumbstick up |
| 6 | right thumbstick click | 16 | right thumbstick down |
| 7 | left trigger | 17 | right thumbstick left |
| 8 | left grip | 18 | right thumbstick right |
| 9 | right trigger | | |

## ratings.jsonl

```json
{"t_lsl":1234.5,"t_unity":12.75,"scene":"Nest","prompt":"presence_1","value":4,"note":"optional"}
```

## meta.json

Written when the session opens, refreshed on pause, and rewritten once on a clean close.

`started_utc` is captured once at open and reused by the final rewrite, so it is always the real
start time. `ended_utc` and `end_reason` are `null` until a clean close. `complete` is `false` until
then, which is how an interrupted session identifies itself.

`build_commit` is the git commit the build was made from, stamped into
`Resources/StudyBuildInfo.txt` by `Editor/StudyBuildInfoGenerator.cs` before every build (and by
*Tools ▸ Study ▸ Stamp Build Info* by hand). A build made from a dirty tree is marked `-dirty`.
It reads `unknown` in an Editor session that has never been stamped.

Also recorded: `app`, `app_version`, `unity_version`, `platform`, `device_model`, `device_id`
(`SystemInfo.deviceUniqueIdentifier`), `display_refresh_rate`, `target_frame_rate`,
`duration_seconds`, and the `pose_rows` / `event_count` / `rating_count` totals.

> `device_id` identifies a headset. Redact it before attaching logs to a public issue or PR.

## Porting to another app

1. Copy this folder across unchanged.
2. Write an adapter that sets `StudySources.AppName`, `StudySources.Pose` and `StudySources.Input`
   from a `[RuntimeInitializeOnLoadMethod]`.
3. Bridge that app's own analytics into `StudyLogger.Event` / `EventJson` if it has any.
