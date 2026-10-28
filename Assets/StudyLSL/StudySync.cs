// T3: sync flash and beep. Identical file in Waddle and pennycookvr (Assets/StudyLSL/).
// StudySync.Flash("SYNC_START") turns the view fully white for 500 ms and plays a 1 kHz beep for 500 ms.
// The marker is pushed (LSL + StudyLogger hookup) right before the first white frame renders,
// so it lines up with the flash in the screen recording and the external camera.
// Editor: F9 fires a test flash ("SYNC_TEST").
using System;
using UnityEngine;
using UnityEngine.UI;

namespace PenguinStudy {
    public sealed class StudySync : MonoBehaviour {
        public const float Duration = 0.5f;
        public const int ToneHz = 1000;

        /// <summary>Raised on the first white frame with the marker name. StudyLogger can subscribe to log it to events.jsonl.</summary>
        static public event Action<string> OnSync;

        static private StudySync s_Instance;
        private Canvas m_Canvas;
        private AudioSource m_Audio;
        private Camera m_AttachedTo;
        private string m_PendingMarker;
        private float m_HideAt = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static private void Boot() {
            if (s_Instance != null) return;
            var go = new GameObject("[StudySync]");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<StudySync>();
        }

        static public void Flash(string markerName) {
            if (s_Instance == null) Boot();
            s_Instance.Begin(markerName);
        }

        private void Awake() {
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 0f;
            m_Audio.volume = 1f;
            m_Audio.priority = 0;
            m_Audio.clip = MakeTone();

            var canvasGo = new GameObject("SyncFlashCanvas");
            canvasGo.transform.SetParent(transform, false);
            m_Canvas = canvasGo.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.WorldSpace;
            m_Canvas.sortingOrder = short.MaxValue;
            var rt = (RectTransform) canvasGo.transform;
            rt.sizeDelta = new Vector2(400, 400);   // 400 units * 0.01 = 4 m square
            rt.localScale = Vector3.one * 0.01f;
            var img = new GameObject("White").AddComponent<Image>();
            img.transform.SetParent(canvasGo.transform, false);
            img.color = Color.white;
            img.raycastTarget = false;
            var irt = img.rectTransform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            canvasGo.SetActive(false);

            Application.onBeforeRender += OnBeforeRender;
        }

        private void OnDestroy() {
            Application.onBeforeRender -= OnBeforeRender;
        }

        private void Begin(string markerName) {
            AttachToCamera();
            m_Canvas.gameObject.SetActive(true);
            m_PendingMarker = markerName;
            m_HideAt = -1; // set on the first rendered frame
        }

        // Called right before XR rendering: the first frame the white quad is drawn.
        private void OnBeforeRender() {
            if (m_PendingMarker == null) return;
            string name = m_PendingMarker;
            m_PendingMarker = null;
            m_HideAt = Time.realtimeSinceStartup + Duration;
            m_Audio.Play();
            StudyLsl.Marker(name, "{\"duration_ms\":" + (int) (Duration * 1000) + ",\"tone_hz\":" + ToneHz + "}");
            OnSync?.Invoke(name);
        }

        private void Update() {
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F9)) Flash("SYNC_TEST");
#endif
            if (m_HideAt > 0 && Time.realtimeSinceStartup >= m_HideAt) {
                m_HideAt = -1;
                m_Canvas.gameObject.SetActive(false);
            }
        }

        private void AttachToCamera() {
            Camera cam = Camera.main;
            if (cam == null) {
                foreach (var c in Camera.allCameras) {
                    if (c.enabled && (cam == null || c.depth > cam.depth)) cam = c;
                }
            }
            if (cam == null || cam == m_AttachedTo) return;
            m_AttachedTo = cam;
            var t = m_Canvas.transform;
            t.SetParent(cam.transform, false);
            t.localPosition = new Vector3(0, 0, Mathf.Max(cam.nearClipPlane + 0.02f, 0.1f));
            t.localRotation = Quaternion.identity;
            m_Canvas.worldCamera = cam;
        }

        static private AudioClip MakeTone() {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int n = Mathf.RoundToInt(rate * Duration);
            var data = new float[n];
            int ramp = rate / 200; // 5 ms fade in/out to avoid clicks
            for (int i = 0; i < n; i++) {
                float env = Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / (float) ramp);
                data[i] = 0.8f * env * Mathf.Sin(2f * Mathf.PI * ToneHz * i / rate);
            }
            var clip = AudioClip.Create("SyncTone1k", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
