#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Submachina.Core.EditorTools
{
    /**
     * All-purpose rocky / crystally / dirty terrain-object sprite generator (successor to
     * the old OreNuggetGenerator). Bakes albedo + normal (+ optional specular-mask) PNG
     * sets wired for URP 2D lighting and the SpriteLitSpecular glint system.
     *
     * Window layout: preset save/load at the top, tabbed generation settings (see
     * TerrainObjectSettings), then a live in-window preview (albedo / normal / lit
     * simulation) and the batch Generate button.
     *
     * Import rules (same as the ore pipeline, verified against the URP 2D shader):
     *  - normals and spec masks are Default textures, sRGB OFF, uncompressed — the sprite
     *    passes read straight RGB, NOT Unity's NormalMap encoding;
     *  - each map is attached to the albedo sprite as a Secondary Texture ("_NormalMap",
     *    "_SpecMask") so every sprite shares one material.
     *
     * Usage: Tools > Submachina > Terrain Object Generator.
     */
    public class TerrainObjectGenerator : OdinEditorWindow
    {
        [MenuItem("Tools/Submachina/Terrain Object Generator")]
        private static void Open() => GetWindow<TerrainObjectGenerator>("Terrain Object Generator");

        // =====================
        // Presets
        // =====================

        [BoxGroup("Preset"), AssetsOnly, Tooltip("Preset asset to load from / save to. Create one with 'Save As…'.")]
        public TerrainObjectPreset preset;

        [BoxGroup("Preset"), HorizontalGroup("Preset/Buttons"), Button, EnableIf(nameof(preset))]
        [Tooltip("Replace the window's settings with the preset's.")]
        private void Load()
        {
            CopySettings(preset.settings, settings);
            RequestPreview();
        }

        [BoxGroup("Preset"), HorizontalGroup("Preset/Buttons"), Button, EnableIf(nameof(preset))]
        [Tooltip("Overwrite the assigned preset with the window's current settings.")]
        private void Save()
        {
            CopySettings(settings, preset.settings);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
        }

        [BoxGroup("Preset"), HorizontalGroup("Preset/Buttons"), Button("Save As…")]
        [Tooltip("Save the current settings as a new preset asset.")]
        private void SaveAs()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Terrain Object Preset", "TerrainObjectPreset", "asset", "Choose where to save the preset.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<TerrainObjectPreset>();
            CopySettings(settings, asset.settings);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            preset = asset;
        }

        // =====================
        // Settings (the tabbed body — this is what presets snapshot)
        // =====================

        [PropertySpace(4), HideLabel, InlineProperty]
        public TerrainObjectSettings settings = new TerrainObjectSettings();

        // =====================
        // Preview
        // =====================

        [BoxGroup("Preview"), PropertyOrder(80), Tooltip("Re-bake the preview automatically whenever a setting changes (preview is capped at 256px for speed).")]
        public bool autoPreview = true;

        [BoxGroup("Preview"), PropertyOrder(80), MinValue(0), Tooltip("Which variant index to preview (same one the batch will produce).")]
        public int previewVariant;

        [BoxGroup("Preview"), PropertyOrder(80), PropertyRange(0f, 360f), Tooltip("Direction the simulated torch light comes from in the Lit preview.")]
        public float previewLightAngle = 125f;

        [BoxGroup("Preview"), PropertyOrder(80), Tooltip("Multiply the Lit preview by a scene-style tint (how RockObstacle tints ore in the water). " +
                                                         "Ignored while Output > Bake Tint is on — the tint is already in the albedo then.")]
        public bool applySceneTint = true;

        [BoxGroup("Preview"), PropertyOrder(80), ShowIf(nameof(applySceneTint))]
        [Tooltip("Scene-style tint for the Lit preview. Its ALPHA is the tint opacity (same convention as Output > Bake Tint).")]
        public Color sceneTint = new Color(0.42f, 0.52f, 0.78f);

        [BoxGroup("Preview"), PropertyOrder(80), Button(ButtonSizes.Medium)]
        private void Preview() => RegeneratePreview();

        [System.NonSerialized] private Texture2D _prevAlbedo, _prevNormal, _prevLit;
        [System.NonSerialized] private bool _previewDirty;
        [System.NonSerialized] private double _previewDirtyAt;
        [System.NonSerialized] private double _lastPollAt;
        [System.NonSerialized] private int _lastSettingsHash;
        [System.NonSerialized] private Vector2 _settingsScroll;

        // =====================
        // Generate
        // =====================

        /**
         * Bakes every variant to PNG sets in the output folder and configures their
         * importers (sprite + secondary textures). Reproducible from the seed.
         */
        [Button(ButtonSizes.Large), GUIColor(0.5f, 0.9f, 0.6f), PropertyOrder(100)]
        private void Generate()
        {
            var s = settings;
            string baseName = string.IsNullOrWhiteSpace(s.baseName) ? "TerrainObj" : s.baseName.Trim();
            Directory.CreateDirectory(s.outputFolder);

            try
            {
                for (int v = 0; v < s.variantCount; v++)
                {
                    EditorUtility.DisplayProgressBar("Terrain Object Generator",
                        $"Baking {baseName}_{v}…", (v + 0.5f) / s.variantCount);

                    var r = TerrainObjectBaker.Bake(s, v);

                    // --- Write the PNG set for this variant ---
                    string albedoPath = $"{s.outputFolder}/{baseName}_{v}_albedo.png";
                    string normalPath = $"{s.outputFolder}/{baseName}_{v}_normal.png";
                    string maskPath = $"{s.outputFolder}/{baseName}_{v}_mask.png";
                    TerrainObjectBaker.WritePng(r.albedo, r.resolution, albedoPath);
                    TerrainObjectBaker.WritePng(r.normal, r.resolution, normalPath);
                    if (r.specMask != null) TerrainObjectBaker.WritePng(r.specMask, r.resolution, maskPath);

                    AssetDatabase.ImportAsset(normalPath);
                    if (r.specMask != null) AssetDatabase.ImportAsset(maskPath);
                    AssetDatabase.ImportAsset(albedoPath);

                    // --- Configure importers (linear maps first so the sprite can reference them) ---
                    ConfigureLinearImporter(normalPath);
                    if (r.specMask != null) ConfigureLinearImporter(maskPath);
                    ConfigureSpriteImporter(albedoPath, normalPath, r.specMask != null ? maskPath : null);

                    // --- Optional separate crystal overlay sprite (with its own spec mask) ---
                    if (r.crystalAlbedo != null)
                    {
                        string cAlbedoPath = $"{s.outputFolder}/{baseName}_{v}_crystal_albedo.png";
                        string cNormalPath = $"{s.outputFolder}/{baseName}_{v}_crystal_normal.png";
                        string cMaskPath = $"{s.outputFolder}/{baseName}_{v}_crystal_mask.png";
                        TerrainObjectBaker.WritePng(r.crystalAlbedo, r.resolution, cAlbedoPath);
                        TerrainObjectBaker.WritePng(r.crystalNormal, r.resolution, cNormalPath);
                        if (r.crystalSpecMask != null) TerrainObjectBaker.WritePng(r.crystalSpecMask, r.resolution, cMaskPath);
                        AssetDatabase.ImportAsset(cNormalPath);
                        if (r.crystalSpecMask != null) AssetDatabase.ImportAsset(cMaskPath);
                        AssetDatabase.ImportAsset(cAlbedoPath);
                        ConfigureLinearImporter(cNormalPath);
                        if (r.crystalSpecMask != null) ConfigureLinearImporter(cMaskPath);
                        ConfigureSpriteImporter(cAlbedoPath, cNormalPath, r.crystalSpecMask != null ? cMaskPath : null);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TerrainObjectGenerator] Generated {s.variantCount} '{baseName}' variant(s) in {s.outputFolder}.");

            // Ping the output folder so the results are easy to find
            var folder = AssetDatabase.LoadAssetAtPath<Object>(s.outputFolder);
            if (folder != null) EditorGUIUtility.PingObject(folder);
        }

        // -------------------------------------------------------
        // Importer configuration
        // -------------------------------------------------------

        /**
         * Import settings for the linear data maps (normal + spec mask). Must stay Default,
         * linear, uncompressed textures because the URP 2D sprite passes read straight RGB.
         */
        private static void ConfigureLinearImporter(string path)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;                 // linear — these are vectors/data, not colors
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }

        /**
         * Albedo sprite import settings: a tight single sprite with a fallback physics shape,
         * plus the data maps attached as Secondary Textures ("_NormalMap", "_SpecMask") so
         * the shared lit material picks them up automatically.
         */
        private void ConfigureSpriteImporter(string albedoPath, string normalPath, string maskPath)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = settings.pixelsPerUnit;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;

            // Tight mesh + generated physics shape from the alpha silhouette
            var texSettings = new TextureImporterSettings();
            ti.ReadTextureSettings(texSettings);
            texSettings.spriteMeshType = SpriteMeshType.Tight;
            texSettings.spriteGenerateFallbackPhysicsShape = true;
            ti.SetTextureSettings(texSettings);

            // Attach the data maps as per-sprite secondary textures
            int count = maskPath != null ? 2 : 1;
            var secondaries = new SecondarySpriteTexture[count];
            secondaries[0] = new SecondarySpriteTexture
            {
                name = "_NormalMap",
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath)
            };
            if (maskPath != null)
            {
                secondaries[1] = new SecondarySpriteTexture
                {
                    name = "_SpecMask",
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath)
                };
            }
            ti.secondarySpriteTextures = secondaries;
            ti.SaveAndReimport();
        }

        // -------------------------------------------------------
        // Preview generation
        // -------------------------------------------------------

        /** Bakes the preview variant in-memory (capped at 256px) and refreshes the preview textures. */
        private void RegeneratePreview()
        {
            try
            {
                // Clone so the preview can cap resolution without touching the live settings
                var s = new TerrainObjectSettings();
                CopySettings(settings, s);
                s.resolution = Mathf.Min(s.resolution, 256);

                int variant = Mathf.Clamp(previewVariant, 0, Mathf.Max(s.variantCount - 1, 0));
                var r = TerrainObjectBaker.Bake(s, variant);
                int res = r.resolution;

                // Display albedo/normal composite crystals over the rock so the preview matches the stacked in-game look
                UpdateTex(ref _prevAlbedo, CompositeOverlay(r.albedo, r.crystalAlbedo, res), res);
                UpdateTex(ref _prevNormal, CompositeOverlay(r.normal, BlendByAlpha(r.crystalNormal, r.crystalAlbedo), res), res);
                UpdateTex(ref _prevLit, BuildLitPreview(r, s), res);
                Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TerrainObjectGenerator] Preview failed: {e}");
            }
        }

        /** Marks the preview dirty; the editor-update pump re-bakes after a short debounce. */
        private void RequestPreview()
        {
            _previewDirty = true;
            _previewDirtyAt = EditorApplication.timeSinceStartup;
        }

        /** Overlay composite (crystal layer over base) for display; passes the base through when there is no overlay. */
        private static Color[] CompositeOverlay(Color[] baseBuf, Color[] overlay, int res)
        {
            if (overlay == null) return baseBuf;
            var outBuf = new Color[baseBuf.Length];
            for (int i = 0; i < baseBuf.Length; i++)
            {
                float a = overlay[i].a;
                var c = Color.Lerp(baseBuf[i], overlay[i], a);
                c.a = Mathf.Max(baseBuf[i].a, a);
                outBuf[i] = c;
            }
            return outBuf;
        }

        /** Re-tags an RGB buffer with another buffer's alpha (crystal normals only exist where crystals are). */
        private static Color[] BlendByAlpha(Color[] rgb, Color[] alphaSource)
        {
            if (rgb == null || alphaSource == null) return null;
            var outBuf = new Color[rgb.Length];
            for (int i = 0; i < rgb.Length; i++)
                outBuf[i] = new Color(rgb[i].r, rgb[i].g, rgb[i].b, alphaSource[i].a);
            return outBuf;
        }

        /**
         * CPU simulation of the in-game look: diffuse from the baked normal against a virtual
         * torch direction, plus a Blinn-Phong glint scaled by the baked spec mask, over a dark
         * water background. Separate crystal layers composite on top untinted (that's their point).
         */
        private Color[] BuildLitPreview(TerrainObjectBaker.BakeResult r, TerrainObjectSettings s)
        {
            int res = r.resolution;
            var px = new Color[res * res];

            float rad = previewLightAngle * Mathf.Deg2Rad;
            Vector3 L = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0.7f).normalized;
            Vector3 H = (L + new Vector3(0f, 0f, 1f)).normalized;
            Color water = new Color(0.06f, 0.1f, 0.18f, 1f);
            Color specCol = new Color(1f, 1.03f, 1.12f);

            // Skip the preview-side tint when the settings already bake it into the albedo
            bool tintInPreview = applySceneTint && !s.bakeTint;

            for (int i = 0; i < px.Length; i++)
            {
                // Rock (or combined) pass
                Color a = r.albedo[i];
                Vector3 nv = DecodeNormal(r.normal[i]);
                // The tint's alpha is its opacity: lerp from untinted → fully tinted
                Color baseCol = tintInPreview ? Color.Lerp(a, a * sceneTint, sceneTint.a) : a;
                float diff = Mathf.Max(0f, Vector3.Dot(nv, L));
                // The mask's RGB tints the glint per pixel (matches the shader's specMask multiply)
                Color mask = r.specMask != null ? r.specMask[i] : Color.white;
                float sp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(nv, H)), 48f) * 1.4f;
                Color lit = baseCol * (0.35f + 0.75f * diff) + specCol * mask * sp;
                Color outc = Color.Lerp(water, lit, a.a);

                // Separate crystal overlay: lit the same way but never scene-tinted
                if (r.crystalAlbedo != null && r.crystalAlbedo[i].a > 0.003f)
                {
                    Color ca = r.crystalAlbedo[i];
                    Vector3 cn = DecodeNormal(r.crystalNormal[i]);
                    float cd = Mathf.Max(0f, Vector3.Dot(cn, L));
                    Color cMask = r.crystalSpecMask != null
                        ? r.crystalSpecMask[i]
                        : Color.white * s.crystals.specMaskValue;
                    float csp = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(cn, H)), 48f) * 1.4f;
                    Color clit = ca * (0.35f + 0.75f * cd) + specCol * cMask * csp;
                    outc = Color.Lerp(outc, clit, ca.a);
                }

                outc.a = 1f;
                px[i] = outc;
            }
            return px;
        }

        private static Vector3 DecodeNormal(Color c) =>
            new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f).normalized;

        /** (Re)creates a preview texture in place and uploads the pixels. */
        private static void UpdateTex(ref Texture2D tex, Color[] px, int res)
        {
            if (tex == null || tex.width != res)
            {
                if (tex != null) DestroyImmediate(tex);
                tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear
                };
            }
            tex.SetPixels(px);
            tex.Apply();
        }

        // -------------------------------------------------------
        // Preview drawing + auto-preview pump
        // -------------------------------------------------------

        /**
         * Splits the window: the settings (Odin's property tree) scroll in the top region while
         * the preview panes stay PINNED to the bottom edge, always visible while tweaking.
         * Odin's own scroll view is disabled in OnEnable so this one owns the scrolling.
         */
        protected override void OnImGUI()
        {
            float previewH = PreviewAreaHeight();
            float scrollH = Mathf.Max(position.height - previewH, 80f);

            _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll, GUILayout.Height(scrollH));
            base.OnImGUI();
            EditorGUILayout.EndScrollView();

            DrawPinnedPreviews();
        }

        /** Height the pinned preview strip needs (separator + label row + square panes + padding). */
        private float PreviewAreaHeight()
        {
            if (_prevAlbedo == null) return 30f;
            float size = Mathf.Min((position.width - 48f) / 3f, 230f);
            return size + 16f + 14f;
        }

        /** Draws the three preview panes (Albedo / Normal / Lit) pinned below the settings scroll. */
        private void DrawPinnedPreviews()
        {
            // Thin separator so the pinned strip reads as its own region
            Rect line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0f, 0f, 0f, 0.35f));

            if (_prevAlbedo == null)
            {
                EditorGUILayout.LabelField("Press Preview (or enable Auto Preview) to see the current settings.", EditorStyles.miniLabel);
                return;
            }

            GUILayout.Space(4);
            float size = Mathf.Min((position.width - 48f) / 3f, 230f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawPreviewPane("Albedo", _prevAlbedo, size, transparent: true);
            DrawPreviewPane("Normal", _prevNormal, size, transparent: false);
            DrawPreviewPane("Lit", _prevLit, size, transparent: false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private static void DrawPreviewPane(string label, Texture2D tex, float size, bool transparent)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(size));
            GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(size));
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (tex != null)
            {
                if (transparent) EditorGUI.DrawTextureTransparent(rect, tex);
                else EditorGUI.DrawPreviewTexture(rect, tex);
            }
            EditorGUILayout.EndVertical();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            UseScrollView = false; // OnImGUI owns the scrolling so the preview strip can stay pinned
            EditorApplication.update += OnEditorUpdate;
        }

        protected override void OnDestroy()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_prevAlbedo != null) DestroyImmediate(_prevAlbedo);
            if (_prevNormal != null) DestroyImmediate(_prevNormal);
            if (_prevLit != null) DestroyImmediate(_prevLit);
            base.OnDestroy();
        }

        /**
         * Auto-preview pump. Change detection polls a hash of the serialized settings (~4x/s)
         * rather than wrapping Odin's GUI in a change scope — the scope missed edits applied
         * outside the draw pass (e.g. fields inside paint-layer list items, like texture
         * tiling). The regenerate itself is debounced ~0.35s so slider drags stay smooth.
         */
        private void OnEditorUpdate()
        {
            if (autoPreview && EditorApplication.timeSinceStartup - _lastPollAt > 0.25)
            {
                _lastPollAt = EditorApplication.timeSinceStartup;
                int hash = ComputeSettingsHash();
                if (hash != _lastSettingsHash)
                {
                    _lastSettingsHash = hash;
                    RequestPreview();
                }
            }

            if (!_previewDirty) return;
            if (EditorApplication.timeSinceStartup - _previewDirtyAt < 0.35) return;
            _previewDirty = false;
            RegeneratePreview();
        }

        /** Hash of everything that affects the preview (settings JSON + the preview controls). */
        private int ComputeSettingsHash()
        {
            string json = EditorJsonUtility.ToJson(settings);
            return (json, previewVariant, previewLightAngle, applySceneTint, sceneTint).GetHashCode();
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /** Deep-copies settings via the editor JSON round-trip (handles nested lists + texture refs). */
        private static void CopySettings(TerrainObjectSettings from, TerrainObjectSettings to) =>
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(from), to);
    }
}
#endif
