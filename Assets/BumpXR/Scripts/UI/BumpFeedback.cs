using System.Collections;
using UnityEngine;

namespace BumpXR.UI
{
    /// <summary>
    /// Audio + visual feedback (bare hands have no haptics). All assets are generated in code: a procedural "clink",
    /// an expanding ring + pulse at the bump point, and a soft chime + glow when a transfer completes.
    /// </summary>
    public sealed class BumpFeedback : MonoBehaviour
    {
        private const int SampleRate = 44100;

        private AudioClip _clink, _softClink, _chime;
        private Material _template;

        private void Awake()
        {
            _clink = MakeClink("BumpClink", 1.0f);
            _softClink = MakeClink("BumpClinkSoft", 0.55f);
            _chime = MakeChime();
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null) _template = new Material(sh);
        }

        /// <summary>Clink + expanding ring + pulse at the bump point.</summary>
        public void Bump(Vector3 point, bool soft = false)
        {
            PlayAt(soft ? _softClink : _clink, point, soft ? 0.6f : 1f);
            StartCoroutine(Ring(point, soft ? new Color(0.8f, 0.6f, 1f) : new Color(0.3f, 0.9f, 1f)));
            StartCoroutine(Blob(point, 0.05f, soft ? 0.10f : 0.14f, 0.25f, soft ? new Color(0.8f, 0.6f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.9f)));
        }

        /// <summary>Soft chime + glow when a transfer completes.</summary>
        public void TransferComplete(Vector3 point)
        {
            PlayAt(_chime, point, 0.7f);
            StartCoroutine(Blob(point, 0.08f, 0.22f, 0.9f, new Color(0.4f, 1f, 0.6f, 0.35f)));
        }

        // ---------------- visuals ----------------

        private IEnumerator Ring(Vector3 point, Color color)
        {
            if (_template == null) yield break;
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            var go = new GameObject("BumpRing");
            go.transform.position = point;
            if (cam != null) go.transform.rotation = Quaternion.LookRotation(cam.forward, Vector3.up);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 48;
            lr.material = new Material(_template);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            const float duration = 0.5f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                float radius = Mathf.Lerp(0.03f, 0.22f, 1f - (1f - k) * (1f - k));
                for (int i = 0; i < lr.positionCount; i++)
                {
                    float a = i / (float)lr.positionCount * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
                }

                var c = new Color(color.r, color.g, color.b, 1f - k);
                lr.startColor = lr.endColor = c;
                lr.widthMultiplier = Mathf.Lerp(0.012f, 0.003f, k);
                yield return null;
            }

            Destroy(lr.material);
            Destroy(go);
        }

        private IEnumerator Blob(Vector3 point, float from, float to, float duration, Color color)
        {
            if (_template == null) yield break;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            PaintWhiteVertexColors(go);
            go.name = "BumpPulse";
            go.transform.position = point;
            var r = go.GetComponent<Renderer>();
            r.material = new Material(_template);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                go.transform.localScale = Vector3.one * Mathf.Lerp(from, to, Mathf.Sqrt(k)) * 2f;
                r.material.color = new Color(color.r, color.g, color.b, color.a * (1f - k));
                yield return null;
            }

            if (go.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(r.material);
            Destroy(go);
        }

        /// <summary>Ensure vertex COLOR attribute is white so Sprites/Default renders bright on Vulkan (Quest).</summary>
        public static void PaintWhiteVertexColors(GameObject primitive)
        {
            if (!primitive.TryGetComponent(out MeshFilter mf) || mf.mesh == null) return;
            Mesh m = mf.mesh;
            var c = new Color32[m.vertexCount];
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < c.Length; i++) c[i] = white;
            m.colors32 = c;
        }

        // ---------------- audio (all procedural) ----------------

        private void PlayAt(AudioClip clip, Vector3 point, float volume)
        {
            var go = new GameObject("BumpAudio");
            go.transform.position = point;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = 1f;           // 3D: positioned at the bump point
            src.minDistance = 0.3f;
            src.maxDistance = 6f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            Destroy(go, clip.length + 0.1f);
        }

        private static AudioClip MakeClink(string name, float brightness)
        {
            const float length = 0.32f;
            int n = (int)(SampleRate * length);
            var data = new float[n];
            var rng = new System.Random(7);
            // inharmonic metallic partials with fast exponential decay + a short noise click
            float[] freq = { 1800f, 2750f, 4300f, 5900f };
            float[] amp = { 0.55f, 0.40f, 0.25f * brightness, 0.15f * brightness };
            float[] decay = { 14f, 20f, 30f, 40f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                for (int p = 0; p < freq.Length; p++) s += amp[p] * Mathf.Sin(2f * Mathf.PI * freq[p] * t) * Mathf.Exp(-decay[p] * t);
                if (t < 0.004f) s += ((float)rng.NextDouble() * 2f - 1f) * 0.5f * (1f - t / 0.004f);
                data[i] = Mathf.Clamp(s * 0.6f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeChime()
        {
            const float length = 1.1f;
            int n = (int)(SampleRate * length);
            var data = new float[n];
            // two soft rising notes (G5 -> D6) with gentle attack and long decay
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float a = Note(t, 0f, 784f) + Note(t, 0.16f, 1175f);
                data[i] = Mathf.Clamp(a * 0.35f, -1f, 1f);
            }

            var clip = AudioClip.Create("BumpChime", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Note(float t, float start, float freq)
        {
            float x = t - start;
            if (x < 0f) return 0f;
            float env = Mathf.Min(1f, x / 0.01f) * Mathf.Exp(-4.5f * x);
            return (Mathf.Sin(2f * Mathf.PI * freq * x) + 0.3f * Mathf.Sin(4f * Mathf.PI * freq * x)) * env;
        }
    }
}

