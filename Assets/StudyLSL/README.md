# StudyLSL (T3)

Same folder in Waddle and pennycookvr. Do not edit one copy without the other.

- `StudyLsl.cs`: opens two LSL outlets at startup.
  - `PenguinVR_Markers` (string, irregular): `StudyLsl.Marker(name, dataJson)`.
  - `PenguinVR_Pose` (21 x float32 at the display rate): head, left, right; px py pz qx qy qz qw, world space. Sampled in `Application.onBeforeRender`.
  - `StudyLsl.Now` is the LSL clock (NaN if LSL could not load). Use it for `t_lsl` in the T2 logs.
  - The app sets `StudyLsl.PoseSource` and `StudyLsl.AppName` in its adapter (Waddle: `Assets/Scripts/Study/WaddleLslAdapter.cs`, Hatched: `Assets/Code/Study/HatchedLslAdapter.cs`).
- `StudySync.cs`: `StudySync.Flash("SYNC_START")` shows full white for 500 ms with a 1 kHz beep and pushes the marker on the first white frame. F9 in the Editor fires a test flash.
- `ThirdParty/LSL.cs`: C# binding from LSL4Unity 1.16 (MIT, see LSL4Unity-LICENSE.txt).
- `Plugins/`: `lsl.dll` (Windows Editor and Win64 only) and `liblsl.so` v1.16.2 built for Android arm64 with NDK r23b.

T2 hookup, once `StudyLogger` exists (one line each):
- `StudyLogger.OnEvent += StudyLsl.Marker;`
- `StudySync.OnSync += name => StudyLogger.Instance.Event(name, null);`

If LSL fails to load (e.g. on a Mac Editor), the app keeps running and only the LSL streams are missing.

## Delivery and validation status (2026-10-27)

The standalone Quest spike is complete and accepted by the project owner: Android arm64 liblsl loaded, SpikePose (7 channels) and SpikeMarkers were discovered by Windows LabRecorder, and an original recording longer than six minutes was saved. That capture contains pose gaps of approximately 33.96, 12.75 and 16.27 seconds. Headset removal/sleep was observed in earlier logs, but each gap has not been conclusively attributed. The original automated validation remains FAIL; owner acceptance does not alter the raw data or prove uninterrupted five-minute operation.

This PR delivers the integrated PenguinVR_Pose (21 channels), PenguinVR_Markers, StudySync flash/beep API, and app pose adapter. The spike evidence is not an integrated-app test. Integrated Unity compilation, live streams, the F9 flash/beep, scene transitions, and on-headset session behavior remain for the owner to test when access is restored. T2 logger subscription and session-start/end calls still require wiring when the logger is available; one-frame marker/flash alignment has not been measured.

The T3 task is being closed at the owner's explicit request as an accepted delivery milestone. The deferred checks and integration dependencies remain documented here; task closure does not assert that these checks passed.

The native liblsl license and dependency notices are reproduced in ThirdParty/liblsl-LICENSE.txt; the C# binding license is in ThirdParty/LSL4Unity-LICENSE.txt.
