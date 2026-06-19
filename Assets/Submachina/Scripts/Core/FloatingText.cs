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
     *
     * Optionally carries a companion SpriteRenderer (e.g. an O2 bubble the text
     * sits inside). If one is found on this object or a child, its alpha is faded
     * in lockstep with the text so the whole popup dissolves together. Bare
     * code-generated popups simply have no sprite and fade the text alone.
     */
    [RequireComponent(typeof(TextMeshPro))]
    public class FloatingText : MonoBehaviour
    {
        private TextMeshPro _text;
        private SpriteRenderer _sprite;   // optional bubble/backing; null for bare popups

        private Color _textStartColor;
        private Color _spriteStartColor;  // authored bubble color, captured once so re-plays keep full alpha
        private Vector3 _baseScale;       // prefab's authored scale, so style scale multiplies rather than replaces

        private Vector3 _startPos;
        private float _floatSpeed;
        private float _lifetime;
        private float _elapsed;

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();

            // Companion sprite is optional — only bubble-wrapped popups have one
            _sprite = GetComponentInChildren<SpriteRenderer>(true);
            if (_sprite != null) _spriteStartColor = _sprite.color;

            _baseScale = transform.localScale;
        }

        /**
         * Activates the text at its current position and begins the
         * float-up + fade-out animation over the given lifetime.
         *
         * scale multiplies the prefab's authored scale, letting the pool shrink
         * a style (e.g. low-key passive-decay popups) without baking it into art.
         */
        public void Play(string content, Color color, float floatSpeed, float lifetime, float scale = 1f)
        {
            _text.text = content;
            _textStartColor = color;
            _text.color = color;

            _floatSpeed = floatSpeed;
            _lifetime = lifetime;
            _elapsed = 0f;

            _startPos = transform.position;
            transform.localScale = _baseScale * scale;

            // Reset the bubble to its full authored alpha before fading again
            if (_sprite != null) _sprite.color = _spriteStartColor;

            gameObject.SetActive(true);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / _lifetime;

            // Float upward from spawn position
            transform.position = _startPos + Vector3.up * (_floatSpeed * _elapsed);

            // Fade alpha linearly to zero — text and (if present) the bubble together
            float fade = 1f - t;

            Color c = _textStartColor;
            c.a = fade;
            _text.color = c;

            if (_sprite != null)
            {
                Color s = _spriteStartColor;
                s.a = _spriteStartColor.a * fade;
                _sprite.color = s;
            }

            if (t >= 1f) gameObject.SetActive(false);
        }
    }
}
