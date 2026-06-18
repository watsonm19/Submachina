using UnityEngine;
using TMPro;

namespace Submachina.Core
{
    /**
     * A single pooled floating text instance.
     *
     * Animates upward while fading out, then deactivates itself so the pool
     * can reuse it. Driven entirely by FloatingTextPool — never add this
     * manually; the pool creates and manages these instances.
     */
    [RequireComponent(typeof(TextMeshPro))]
    public class FloatingText : MonoBehaviour
    {
        private TextMeshPro _text;
        private Color _startColor;
        private Vector3 _startPos;
        private float _floatSpeed;
        private float _lifetime;
        private float _elapsed;

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();
        }

        /**
         * Activates the text at its current position and begins the
         * float-up + fade-out animation over the given lifetime.
         */
        public void Play(string content, Color color, float floatSpeed, float lifetime)
        {
            _text.text = content;
            _startColor = color;
            _text.color = color;
            _floatSpeed = floatSpeed;
            _lifetime = lifetime;
            _elapsed = 0f;
            _startPos = transform.position;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / _lifetime;

            // Float upward from spawn position
            transform.position = _startPos + Vector3.up * (_floatSpeed * _elapsed);

            // Fade alpha linearly to zero
            Color c = _startColor;
            c.a = 1f - t;
            _text.color = c;

            if (t >= 1f) gameObject.SetActive(false);
        }
    }
}
