# Agent instructions (Codex) - Waddle study build

Unity 2021.3.17f1, Oculus Integration (OVR) 1.79, FieldDay framework. Target: Meta Quest 3 (Android).
Branch flow: work on feature branches off `study-build`; never push to `main`.

## Commands (PowerShell, Unity Editor must be closed)
- Compile check: `powershell -ExecutionPolicy Bypass -File tools\ci.ps1`
- Compile + tests: `powershell -ExecutionPolicy Bypass -File tools\ci.ps1 -Tests`
- Logs: `Builds\ci\*.log`

## Loop
Run ci.ps1 -> read the first error in Builds\ci\compile.log -> fix the root cause -> rerun. Stop after 6 failed rounds and write `Builds/AGENT_REPORT.md`.

## Do not touch
`Assets/Oculus/`, `Assets/Firebase/`, `Assets/TextMesh Pro/`, `Assets/StylizedWater2/`, `Assets/FieldDay/` (shared framework), `ProjectSettings/` (unless the task says so), any `.unity` scene or `.prefab` file unless the task says so.
Do not git push. Commit only when the task asks.

## Project notes
- Player head/movement: `Assets/Scripts/Player/` (PlayerHeadSystem, PlayerMovementDetectorSystem, PlayerMovementDebugSystem).
- Desktop test rig (T0b): `Assets/Scripts/DesktopSim/DesktopRigDriver.cs`, Editor/Development builds only.
- Do not change PlayerMovementDebugSystem.CheckOVRKeys - that is task T1 (Nikhil).
