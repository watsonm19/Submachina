using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Sirenix.OdinInspector;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Utility
{
    /// <summary>
    /// Generates 2D geometric primitive sprites procedurally at runtime or in-editor.
    /// Works with both SpriteRenderer (2D) and Image (UI) components.
    /// Supports circles, rings, regular polygons, stars, and rounded rectangles with
    /// various fill modes and customizable outline colors.
    /// </summary>
    public class SpriteShapeGenerator : MonoBehaviour
    {
        // ============================================================================
        // ENUMS
        // ============================================================================

        public enum PrimitiveShape
        {
            Circle,
            Ring,
            RegularPolygon,
            Star,
            RoundedRectangle
        }

        public enum FillMode
        {
            Filled,
            OutlineOnly,
            FilledWithOutline
        }

        // ============================================================================
        // SHAPE SETTINGS
        // ============================================================================

        [TitleGroup("Shape")]
        [SerializeField]
        [OnValueChanged("Regenerate")]
        private PrimitiveShape shapeType = PrimitiveShape.Circle;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.RegularPolygon || shapeType == PrimitiveShape.Star")]
        [Range(3, 32)]
        [OnValueChanged("Regenerate")]
        private int polygonSides = 6;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.RegularPolygon || shapeType == PrimitiveShape.Star")]
        [Range(0f, 360f)]
        [OnValueChanged("Regenerate")]
        private float rotationDegrees = 0f;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.RegularPolygon || shapeType == PrimitiveShape.Star")]
        [OnValueChanged("Regenerate")]
        [Tooltip("Scales the shape so flat edges reach the texture boundary instead of vertices")]
        private bool fitToBounds = false;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.Star")]
        [Range(0.05f, 0.95f)]
        [OnValueChanged("Regenerate")]
        private float starInnerRadiusFraction = 0.4f;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.Ring")]
        [Range(0.05f, 0.95f)]
        [OnValueChanged("Regenerate")]
        private float ringInnerRadiusFraction = 0.5f;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.RoundedRectangle")]
        [OnValueChanged("Regenerate")]
        private Vector2 aspectRatio = Vector2.one;

        [TitleGroup("Shape")]
        [SerializeField]
        [ShowIf("@shapeType == PrimitiveShape.RoundedRectangle")]
        [Range(0f, 0.5f)]
        [OnValueChanged("Regenerate")]
        private float cornerRadiusFraction = 0.2f;

        // ============================================================================
        // FILL & COLOR
        // ============================================================================

        [TitleGroup("Fill & Color")]
        [SerializeField]
        [OnValueChanged("Regenerate")]
        private FillMode fillMode = FillMode.Filled;

        [TitleGroup("Fill & Color")]
        [SerializeField]
        [HideIf("@fillMode == FillMode.OutlineOnly")]
        [ColorUsage(true, true)]
        [OnValueChanged("Regenerate")]
        private Color fillColor = Color.white;

        [TitleGroup("Fill & Color")]
        [SerializeField]
        [ShowIf("@fillMode == FillMode.OutlineOnly || fillMode == FillMode.FilledWithOutline")]
        [ColorUsage(true, true)]
        [OnValueChanged("Regenerate")]
        private Color outlineColor = Color.black;

        [TitleGroup("Fill & Color")]
        [SerializeField]
        [ShowIf("@fillMode != FillMode.Filled")]
        [Range(1f, 32f)]
        [OnValueChanged("Regenerate")]
        private float outlineThicknessPx = 3f;

        // ============================================================================
        // TEXTURE SETTINGS
        // ============================================================================

        [TitleGroup("Texture")]
        [SerializeField]
        [ValueDropdown(nameof(GetResolutionOptions))]
        [OnValueChanged("Regenerate")]
        private int resolution = 128;

        [TitleGroup("Texture")]
        [SerializeField]
        [Range(0f, 4f)]
        [OnValueChanged("Regenerate")]
        private float antiAliasEdgePx = 1.5f;

        [TitleGroup("Texture")]
        [SerializeField]
        [OnValueChanged("Regenerate")]
        private FilterMode filterMode = FilterMode.Bilinear;

        // ============================================================================
        // EXPORT SETTINGS
        // ============================================================================

        [TitleGroup("Export")]
        [InfoBox("Files saved as: <folder>/<filename>.png")]
        [SerializeField]
        private string outputFolder = "Assets/GeneratedSprites";

        [TitleGroup("Export")]
        [SerializeField]
        private string fileName = "";

#if UNITY_EDITOR
        [TitleGroup("Export")]
        [Button("Export to PNG", ButtonSizes.Large)]
        private void ExportToPNGButton()
        {
            ExportToPNG();
        }
#endif

        // ============================================================================
        // DEBUG
        // ============================================================================

        [TitleGroup("Debug")]
        [ShowInInspector]
        [ReadOnly]
        [PreviewField(128, ObjectFieldAlignment.Left)]
        private Sprite _previewSprite;

        // ============================================================================
        // RUNTIME
        // ============================================================================

        private Texture2D _texture;
        private SpriteRenderer _spriteRenderer;
        private Image _imageComponent;

        void OnEnable()
        {
            InitializeGraphicsComponent();
        }

        void OnValidate()
        {
            // Only regenerate when enabled so a disabled component has zero side effects
            if (!enabled || Application.isPlaying) return;

            InitializeGraphicsComponent();
            Regenerate();
        }

        void InitializeGraphicsComponent()
        {
            // Try to find Image component first (UI), then SpriteRenderer (2D)
            _imageComponent = GetComponent<Image>();
            _spriteRenderer = GetComponent<SpriteRenderer>();

            // If neither exists, add SpriteRenderer as default for 2D scenes
            if (_imageComponent == null && _spriteRenderer == null)
                _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        // ============================================================================
        // MAIN GENERATION
        // ============================================================================

        /// <summary>
        /// Generates the sprite from current settings and assigns it to the SpriteRenderer or Image component.
        /// </summary>
        [TitleGroup("Debug")]
        [Button("Regenerate", ButtonSizes.Medium)]
        public void Regenerate()
        {
            InitializeGraphicsComponent();
            GenerateSprite();
        }

        void GenerateSprite()
        {
            // Create texture
            if (_texture != null)
                DestroyImmediate(_texture);

            _texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            _texture.filterMode = filterMode;

            // Clear pixels
            Color[] pixels = new Color[resolution * resolution];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            // Draw shape
            DrawPixels(pixels);

            // Apply and create sprite
            _texture.SetPixels(pixels);
            _texture.Apply();

            Sprite sprite = Sprite.Create(
                _texture,
                new Rect(0, 0, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                resolution / 2f
            );

            // Assign sprite to whichever component is available
            if (_imageComponent != null)
            {
                _imageComponent.sprite = sprite;
                _imageComponent.color = Color.white;
            }
            else if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = sprite;
                _spriteRenderer.color = Color.white;
            }

            _previewSprite = sprite;
        }

        /// <summary>
        /// Iterates all pixels and computes color based on shape SDF and fill mode.
        /// </summary>
        void DrawPixels(Color[] pixels)
        {
            Vector2 center = new Vector2(resolution / 2f, resolution / 2f);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float sdf = ComputeShapeSDF(pos, center);

                    Color pixelColor = ComputePixelColor(sdf);
                    pixels[y * resolution + x] = pixelColor;
                }
            }
        }

        /// <summary>
        /// Computes the signed distance field value for the current shape at the given position.
        /// Positive = inside, negative = outside.
        /// </summary>
        float ComputeShapeSDF(Vector2 pos, Vector2 center)
        {
            float radius = (resolution / 2f) - 2f;

            return shapeType switch
            {
                PrimitiveShape.Circle => SdfCircle(pos, center, radius),
                PrimitiveShape.Ring => SdfRing(pos, center, radius),
                PrimitiveShape.RegularPolygon => SdfPolygon(pos, center, radius),
                PrimitiveShape.Star => SdfStar(pos, center, radius),
                PrimitiveShape.RoundedRectangle => SdfRoundedRect(pos, center, radius),
                _ => 0f
            };
        }

        /// <summary>
        /// Determines pixel color based on SDF value and fill mode.
        /// </summary>
        Color ComputePixelColor(float sdf)
        {
            float aa = antiAliasEdgePx;

            return fillMode switch
            {
                FillMode.Filled => ComputeFilled(sdf, aa),
                FillMode.OutlineOnly => ComputeOutlineOnly(sdf, aa),
                FillMode.FilledWithOutline => ComputeFilledWithOutline(sdf, aa),
                _ => Color.clear
            };
        }

        Color ComputeFilled(float sdf, float aa)
        {
            // Paint interior with fillColor, fade near edge
            if (sdf >= -aa)
            {
                float alpha = Mathf.Clamp01((sdf + aa) / aa);
                return fillColor * new Color(1, 1, 1, alpha);
            }
            return Color.clear;
        }

        Color ComputeOutlineOnly(float sdf, float aa)
        {
            // Paint outline band only
            float outlineHalf = outlineThicknessPx;
            if (Mathf.Abs(sdf) <= outlineHalf + aa)
            {
                float alpha = Mathf.Clamp01((outlineHalf + aa - Mathf.Abs(sdf)) / aa);
                return outlineColor * new Color(1, 1, 1, alpha);
            }
            return Color.clear;
        }

        Color ComputeFilledWithOutline(float sdf, float aa)
        {
            // Interior + outline band
            float outlineHalf = outlineThicknessPx;

            // Outline band takes priority
            if (sdf >= -outlineHalf && sdf <= outlineHalf)
            {
                float alpha = Mathf.Clamp01((outlineHalf - Mathf.Abs(sdf)) / aa);
                return outlineColor * new Color(1, 1, 1, alpha);
            }

            // Interior fill
            if (sdf >= -aa)
            {
                float alpha = Mathf.Clamp01((sdf + aa) / aa);
                return fillColor * new Color(1, 1, 1, alpha);
            }

            return Color.clear;
        }

        // ============================================================================
        // SDF FUNCTIONS
        // ============================================================================

        float SdfCircle(Vector2 pos, Vector2 center, float radius)
        {
            float dist = Vector2.Distance(pos, center);
            return radius - dist;
        }

        float SdfRing(Vector2 pos, Vector2 center, float radius)
        {
            // Ring has outer radius and inner radius
            float outerRadius = radius;
            float innerRadius = radius * ringInnerRadiusFraction;

            float dist = Vector2.Distance(pos, center);
            return Mathf.Min(outerRadius - dist, dist - innerRadius);
        }

        float SdfPolygon(Vector2 pos, Vector2 center, float radius)
        {
            // Scale radius so flat edges touch bounds instead of vertices
            if (fitToBounds)
                radius /= Mathf.Cos(Mathf.PI / polygonSides);

            // Compute N vertices equally spaced around a circle
            float angle = 360f / polygonSides;
            float minDist = float.MaxValue;
            bool isInside = true;

            Vector2[] vertices = new Vector2[polygonSides];
            for (int i = 0; i < polygonSides; i++)
            {
                float theta = (i * angle + rotationDegrees) * Mathf.Deg2Rad;
                vertices[i] = center + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * radius;
            }

            // Test each edge
            for (int i = 0; i < polygonSides; i++)
            {
                Vector2 a = vertices[i];
                Vector2 b = vertices[(i + 1) % polygonSides];

                float edgeDist = DistanceToLineSegment(pos, a, b);
                minDist = Mathf.Min(minDist, edgeDist);

                // Winding number test: point inside if cross products all have same sign
                Vector2 edge = b - a;
                Vector2 toPoint = pos - a;
                float cross = edge.x * toPoint.y - edge.y * toPoint.x;
                if (cross < 0)
                    isInside = false;
            }

            return isInside ? minDist : -minDist;
        }

        float SdfStar(Vector2 pos, Vector2 center, float radius)
        {
            // Scale radius so flat edges touch bounds instead of vertices
            if (fitToBounds)
                radius /= Mathf.Cos(Mathf.PI / polygonSides);

            // Star with N points: alternate outer/inner radius
            float outerRadius = radius;
            float innerRadius = radius * starInnerRadiusFraction;

            int numVertices = polygonSides * 2;
            float angle = 360f / numVertices;

            Vector2[] vertices = new Vector2[numVertices];
            for (int i = 0; i < numVertices; i++)
            {
                float theta = (i * angle + rotationDegrees) * Mathf.Deg2Rad;
                float r = (i % 2 == 0) ? outerRadius : innerRadius;
                vertices[i] = center + new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)) * r;
            }

            // Find minimum distance to any edge
            float minDist = float.MaxValue;
            bool isInside = true;

            for (int i = 0; i < numVertices; i++)
            {
                Vector2 a = vertices[i];
                Vector2 b = vertices[(i + 1) % numVertices];

                float edgeDist = DistanceToLineSegment(pos, a, b);
                minDist = Mathf.Min(minDist, edgeDist);

                // Winding test
                Vector2 edge = b - a;
                Vector2 toPoint = pos - a;
                float cross = edge.x * toPoint.y - edge.y * toPoint.x;
                if (cross < 0)
                    isInside = false;
            }

            return isInside ? minDist : -minDist;
        }

        float SdfRoundedRect(Vector2 pos, Vector2 center, float radius)
        {
            // Rounded rectangle using standard SDF formula
            float cornerRadius = radius * cornerRadiusFraction;
            float halfWidth = radius * aspectRatio.x;
            float halfHeight = radius * aspectRatio.y;

            Vector2 q = new Vector2(Mathf.Abs(pos.x - center.x), Mathf.Abs(pos.y - center.y)) -
                        new Vector2(halfWidth, halfHeight) + new Vector2(cornerRadius, cornerRadius);

            float sdf = cornerRadius -
                (Mathf.Sqrt(Mathf.Max(q.x, 0) * Mathf.Max(q.x, 0) + Mathf.Max(q.y, 0) * Mathf.Max(q.y, 0)) +
                 Mathf.Min(Mathf.Max(q.x, q.y), 0f));

            return sdf;
        }

        /// <summary>
        /// Computes signed distance from point P to a line segment AB.
        /// </summary>
        float DistanceToLineSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            Vector2 ap = p - a;
            float t = Vector2.Dot(ap, ab) / Vector2.Dot(ab, ab);
            t = Mathf.Clamp01(t);

            Vector2 closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }

        // ============================================================================
        // EXPORT
        // ============================================================================

#if UNITY_EDITOR
        void ExportToPNG()
        {
            if (_texture == null)
            {
                Debug.LogError("No texture generated. Please regenerate first.");
                return;
            }

            // Ensure output folder exists
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            // Build filename
            string finalFileName = fileName;
            if (string.IsNullOrEmpty(finalFileName))
            {
                string shapeStr = shapeType.ToString();
                finalFileName = $"{shapeStr}_{resolution}px_{DateTime.Now:HHmmss}";
            }

            string fullPath = Path.Combine(outputFolder, finalFileName + ".png");

            // Write PNG
            byte[] pngBytes = _texture.EncodeToPNG();
            File.WriteAllBytes(fullPath, pngBytes);

            // Refresh asset database
            AssetDatabase.Refresh();

            Debug.Log($"Exported sprite to: {fullPath}");
        }
#endif

        // ============================================================================
        // UTILITIES
        // ============================================================================

        private IEnumerable<int> GetResolutionOptions()
        {
            return new[] { 64, 128, 256, 512, 1024, 2056, 4096 };
        }
    }
}
