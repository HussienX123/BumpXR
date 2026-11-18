using System;
using System.Collections;
using BumpXR.Core;
using UnityEngine;

namespace BumpXR.UI
{
    /// <summary>
    /// Demo Friend as a floating fist orb at arm's reach (right of centre, ~0.5 m). It meets the user's fist on a bump
    /// and bumps back. Its own motion goes through the same BumpDetectorCore + BumpSettings as real hands.
    /// </summary>
    public sealed class DemoFriendOrb : MonoBehaviour, IBumpDetector
    {
        private const float HomeDistance = 0.5f;
        private const float HomeYawDegrees = 32f;   // right of centre; secondary visual, not primary UI
        private const float HomeHeight = -0.12f;

        private BumpDetectorCore _core;
        private Renderer _renderer;
        private Vector3 _home;
        private Vector3 _moveDir = Vector3.forward;
        private bool _busy;
        private bool _offering;
        private Vector3 _offerPoint;

        public event Action<BumpEvent> Bumped;

        public bool Shown { get; private set; }
        public string StateLabel => _core != null ? _core.State : "-";
        public float LeftSpeed => 0f;
        public float RightSpeed => _core?.RightSpeed ?? 0f;
        public float CooldownLeft => _core?.CooldownLeft ?? 0f;

        public void Init(BumpSettings settings)
        {
            _core = new BumpDetectorCore(settings);
            _core.Bumped += e => Bumped?.Invoke(e);

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            BumpFeedback.PaintWhiteVertexColors(sphere);
            sphere.transform.SetParent(transform, false);
            sphere.transform.localScale = Vector3.one * 0.09f;
            _renderer = sphere.GetComponent<Renderer>();
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null) _renderer.material = new Material(sh) { color = new Color(0.65f, 0.45f, 1f, 0.95f) };
            gameObject.SetActive(false);
        }

        public void Show(bool show)
        {
            if (show == Shown) return;
            Shown = show;
            if (show)
            {
                ComputeHome();
                transform.position = _home;
                gameObject.SetActive(true);
            }
            else
            {
                StopAllCoroutines();
                _busy = _offering = false;
                gameObject.SetActive(false);
            }
        }

        /// <summary>Dash to the user's bump point, stop (this is the orb's own bump), hold, then float home.</summary>
        public void MeetFist(Vector3 point)
        {
            if (!Shown) return;
            StopAllCoroutines();
            _offering = false;
            StartCoroutine(Meet(point));
        }

        /// <summary>Hold a fist out in front of the user while a card waits for "Bump to accept".</summary>
        public void Offer()
        {
            if (!Shown || _busy) return;
            Transform head = Camera.main != null ? Camera.main.transform : null;
            Vector3 origin = head != null ? head.position : new Vector3(0f, 1.2f, 0f);
            Vector3 fwd = Flat(head != null ? head.forward : Vector3.forward);
            _offerPoint = origin + fwd * 0.38f + Vector3.up * HomeHeight;
            _offering = true;
        }

        public void StopOffering() => _offering = false;

        private IEnumerator Meet(Vector3 point)
        {
            _busy = true;
            Vector3 start = transform.position;
            _moveDir = (point - start).sqrMagnitude > 1e-4f ? (point - start).normalized : Vector3.forward;
            const float dash = 0.16f;
            for (float t = 0f; t < dash; t += Time.unscaledDeltaTime)
            {
                float k = t / dash;
                transform.position = Vector3.Lerp(start, point, k * k); // accelerate into the meeting point
                yield return null;
            }

            transform.position = point; // abrupt stop = detectable deceleration
            yield return new WaitForSecondsRealtime(0.3f);

            Vector3 from = transform.position;
            ComputeHome();
            for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
            {
                transform.position = Vector3.Lerp(from, _home, Mathf.SmoothStep(0f, 1f, t / 0.7f));
                yield return null;
            }

            _busy = false;
        }

        private void LateUpdate()
        {
            if (!Shown) return;

            if (!_busy)
            {
                Vector3 target = _offering ? _offerPoint : _home + Vector3.up * Mathf.Sin(Time.unscaledTime * 1.7f) * 0.012f;
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            }

            _core?.Update(GetInstanceID(), false, Time.unscaledTime, transform.position, true, true, _moveDir);
        }

        private void ComputeHome()
        {
            Transform head = Camera.main != null ? Camera.main.transform : null;
            Vector3 origin = head != null ? head.position : new Vector3(0f, 1.2f, 0f);
            Vector3 fwd = Flat(head != null ? head.forward : Vector3.forward);
            _home = origin + Quaternion.AngleAxis(HomeYawDegrees, Vector3.up) * fwd * HomeDistance + Vector3.up * HomeHeight;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.01f ? Vector3.forward : v.normalized;
        }
    }
}

