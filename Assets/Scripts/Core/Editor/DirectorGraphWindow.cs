using System.Collections.Generic;
using Core.Audio;
using Core.Modulation;
using UnityEditor;
using UnityEngine;

namespace Core.Editor
{
    /**
     * Director Graph — the admin window for the environmental modulation system.
     *
     * Shows the whole pipeline as a left-to-right dataflow graph:
     *
     *   SIGNALS  ──contributions──►  PARAMETERS  ──routes──►  OUTPUTS
     *                                     │
     *                                     └───────rules──►  EVENTS & AUDIO
     *
     * Everything is scanned live from the scene: nodes are clickable (ping + select the
     * underlying object), values/bars/sparklines update in play mode, and "+" menus create
     * correctly-wired contributions, routes, rules, signals, and definition assets.
     * Layout is fixed-column on purpose — the pipeline is a layered DAG, so columns give
     * node-graph readability with zero manual node dragging.
     */
    public class DirectorGraphWindow : EditorWindow
    {
        // ------------------------------------------------------------------ layout constants
        private const float ColSignals = 16f;
        private const float ColParams = 320f;
        private const float ColOutputs = 660f;
        private const float ColEvents = 1000f;
        private const float ColWidth = 280f;
        private const float RowGap = 10f;
        private const int HistorySize = 240;   // ~24s at 10 Hz

        // ------------------------------------------------------------------ scanned scene state
        private EnvironmentDirector[] _directors = new EnvironmentDirector[0];
        private int _directorIndex;
        private AudioDirector _audioDirector;
        private readonly List<FloatSignal> _signals = new List<FloatSignal>();
        private readonly List<ParameterModifierTrigger> _modTriggers = new List<ParameterModifierTrigger>();
        private readonly List<SignalContribution> _contributions = new List<SignalContribution>();
        private readonly List<ModulatedFloatTarget> _targets = new List<ModulatedFloatTarget>();
        private readonly List<DirectorRule> _rules = new List<DirectorRule>();
        private readonly List<DirectorParameterDef> _parameters = new List<DirectorParameterDef>();
        private readonly List<AudioStingerDef> _stingerDefs = new List<AudioStingerDef>();
        private readonly List<AudioOneShotDef> _oneShotDefs = new List<AudioOneShotDef>();

        // ------------------------------------------------------------------ live/UI state
        private readonly Dictionary<Object, Rect> _nodeRects = new Dictionary<Object, Rect>();
        private readonly Dictionary<DirectorParameterDef, Rect> _paramRects = new Dictionary<DirectorParameterDef, Rect>();
        private readonly Dictionary<DirectorParameterDef, float[]> _history = new Dictionary<DirectorParameterDef, float[]>();
        private readonly Dictionary<DirectorParameterDef, int> _historyHead = new Dictionary<DirectorParameterDef, int>();
        private readonly Dictionary<DirectorParameterDef, float> _overrideValues = new Dictionary<DirectorParameterDef, float>();
        private readonly List<EnvironmentDirector.ParameterSnapshot> _snapshotBuffer = new List<EnvironmentDirector.ParameterSnapshot>();
        private readonly List<AudioDirector.AmbienceSnapshot> _ambienceBuffer = new List<AudioDirector.AmbienceSnapshot>();
        // Solo: restrict the graph to one node's upstream chain (Input) or its full
        // upstream + downstream chain (Output). Siblings disappear in both modes.
        private enum SoloSide { None, Input, Output }
        private Object _soloKey;
        private SoloSide _soloSide = SoloSide.None;
        private readonly HashSet<Object> _soloVisible = new HashSet<Object>();
        private readonly Dictionary<Object, List<Object>> _edgesForward = new Dictionary<Object, List<Object>>();
        private readonly Dictionary<Object, List<Object>> _edgesReverse = new Dictionary<Object, List<Object>>();

        private Vector2 _pan;
        private float _zoom = 1f;
        private Matrix4x4 _prevGuiMatrix;
        private double _lastSampleTime;
        private double _lastRepaintTime;
        private bool _showAudioLane = true;
        private bool _showRulesLane = true;

        private const float ToolbarHeight = 22f;
        private const float TabHeight = 21f;          // height of the implicit EditorWindow group Unity opens
        private const float CanvasWidth = ColEvents + ColWidth + 40f;
        private const float MinZoom = 0.35f;
        private const float MaxZoom = 1.25f;

        private EnvironmentDirector Director =>
            _directors.Length == 0 ? null : _directors[Mathf.Clamp(_directorIndex, 0, _directors.Length - 1)];

        // ------------------------------------------------------------------ lifecycle

        [MenuItem("Tools/Submachina/Director Graph")]
        public static void Open()
        {
            var window = GetWindow<DirectorGraphWindow>("Director Graph");
            window.minSize = new Vector2(900f, 500f);
            window.ScanScene();
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += ScanScene;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ScanScene();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= ScanScene;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode) ScanScene();
        }

        /** Samples parameter history at ~10 Hz and repaints at ~15 Hz while playing. */
        private void OnEditorUpdate()
        {
            if (!Application.isPlaying || Director == null) return;

            if (EditorApplication.timeSinceStartup - _lastSampleTime > 0.1)
            {
                _lastSampleTime = EditorApplication.timeSinceStartup;
                foreach (var p in _parameters)
                {
                    if (!_history.TryGetValue(p, out var buffer)) { buffer = new float[HistorySize]; _history[p] = buffer; _historyHead[p] = 0; }
                    buffer[_historyHead[p] % HistorySize] = Director.GetValue(p);
                    _historyHead[p] = (_historyHead[p] + 1) % HistorySize;
                }
            }

            if (EditorApplication.timeSinceStartup - _lastRepaintTime > 0.066)
            {
                _lastRepaintTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        // ------------------------------------------------------------------ scanning

        /** Re-reads every modulation-related component in the open scene(s). Cheap enough to run on hierarchy changes. */
        private void ScanScene()
        {
            _directors = FindObjectsByType<EnvironmentDirector>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            _audioDirector = FindFirstObjectByType<AudioDirector>(FindObjectsInactive.Include);

            _signals.Clear(); _signals.AddRange(FindObjectsByType<FloatSignal>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
            _modTriggers.Clear(); _modTriggers.AddRange(FindObjectsByType<ParameterModifierTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
            _contributions.Clear(); _contributions.AddRange(FindObjectsByType<SignalContribution>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
            _targets.Clear(); _targets.AddRange(FindObjectsByType<ModulatedFloatTarget>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
            _rules.Clear(); _rules.AddRange(FindObjectsByType<DirectorRule>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));

            // Parameter list = every def referenced by any wiring piece (order: params column stays stable).
            _parameters.Clear();
            void AddParam(DirectorParameterDef def) { if (def != null && !_parameters.Contains(def)) _parameters.Add(def); }
            foreach (var c in _contributions) AddParam(c.Parameter);
            foreach (var m in _modTriggers) AddParam(m.Parameter);
            foreach (var t in _targets) AddParam(t.BoundParameter);
            foreach (var r in _rules) AddParam(r.Parameter);

            // Definition assets for the audio quick-play strip.
            _stingerDefs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioStingerDef"))
                _stingerDefs.Add(AssetDatabase.LoadAssetAtPath<AudioStingerDef>(AssetDatabase.GUIDToAssetPath(guid)));
            _oneShotDefs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioOneShotDef"))
                _oneShotDefs.Add(AssetDatabase.LoadAssetAtPath<AudioOneShotDef>(AssetDatabase.GUIDToAssetPath(guid)));

            RebuildSolo();
            Repaint();
        }

        // ------------------------------------------------------------------ solo

        private bool SoloActive => _soloSide != SoloSide.None && _soloKey != null;

        private void SetSolo(Object key, SoloSide side)
        {
            // Clicking the active handle again un-solos.
            if (_soloKey == key && _soloSide == side) { ClearSolo(); return; }
            _soloKey = key;
            _soloSide = side;
            RebuildSolo();
            Repaint();
        }

        private void ClearSolo()
        {
            _soloKey = null;
            _soloSide = SoloSide.None;
            _soloVisible.Clear();
            Repaint();
        }

        /**
         * Rebuilds the visible-node set from the wiring graph:
         *   Input solo  → solo node + transitive upstream.
         *   Output solo → solo node + transitive upstream + transitive downstream.
         * Runs on scan and solo changes only — value changes never alter topology.
         */
        private void RebuildSolo()
        {
            _soloVisible.Clear();
            if (!SoloActive) return;

            // Adjacency from the same relations the edge pass draws.
            _edgesForward.Clear();
            _edgesReverse.Clear();
            void Edge(Object from, Object to)
            {
                if (from == null || to == null) return;
                if (!_edgesForward.TryGetValue(from, out var f)) { f = new List<Object>(); _edgesForward[from] = f; }
                f.Add(to);
                if (!_edgesReverse.TryGetValue(to, out var r)) { r = new List<Object>(); _edgesReverse[to] = r; }
                r.Add(from);
            }
            foreach (var c in _contributions) if (c != null) Edge(c.Signal, c.Parameter);
            foreach (var m in _modTriggers) if (m != null) Edge(m, m.Parameter);
            foreach (var t in _targets) if (t != null && t.BoundParameter != null) Edge(t.BoundParameter, t);
            foreach (var r in _rules) if (r != null) Edge(r.Parameter, r);

            _soloVisible.Add(_soloKey);
            Traverse(_soloKey, _edgesReverse);                                // upstream, always
            if (_soloSide == SoloSide.Output) Traverse(_soloKey, _edgesForward); // downstream, output solo only
        }

        private void Traverse(Object start, Dictionary<Object, List<Object>> adjacency)
        {
            var stack = new Stack<Object>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!adjacency.TryGetValue(node, out var next)) continue;
                foreach (var n in next)
                    if (_soloVisible.Add(n)) stack.Push(n);
            }
        }

        private bool IsVisible(Object node) => !SoloActive || _soloVisible.Contains(node);

        /**
         * Draws the per-side solo handles on a node's title row and returns the title rect
         * shrunk to fit. ◄ solos the input side, ► the output side; the active handle is tinted.
         */
        private Rect SoloHandles(Rect titleRect, Object key, bool canSoloInput, bool canSoloOutput)
        {
            var title = titleRect;
            var previousColor = GUI.backgroundColor;

            if (canSoloInput)
            {
                bool active = _soloKey == key && _soloSide == SoloSide.Input;
                GUI.backgroundColor = active ? new Color(1f, 0.85f, 0.3f) : previousColor;
                if (GUI.Button(new Rect(titleRect.x, titleRect.y + 1f, 18f, 14f), "◄", EditorStyles.miniButton)) SetSolo(key, SoloSide.Input);
                GUI.backgroundColor = previousColor;
                title.x += 21f;
                title.width -= 21f;
            }

            if (canSoloOutput)
            {
                bool active = _soloKey == key && _soloSide == SoloSide.Output;
                GUI.backgroundColor = active ? new Color(1f, 0.85f, 0.3f) : previousColor;
                if (GUI.Button(new Rect(titleRect.xMax - 18f, titleRect.y + 1f, 18f, 14f), "►", EditorStyles.miniButton)) SetSolo(key, SoloSide.Output);
                GUI.backgroundColor = previousColor;
                title.width -= 21f;
            }

            return title;
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            // Esc drops back to the full graph from any solo view.
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && SoloActive)
            {
                ClearSolo();
                Event.current.Use();
            }

            DrawToolbar();

            if (Director == null)
            {
                EditorGUILayout.HelpBox("No EnvironmentDirector found in the open scene.", MessageType.Info);
                if (GUILayout.Button("Create Environment Director", GUILayout.Width(220f)))
                {
                    var go = new GameObject("Environment Director", typeof(EnvironmentDirector));
                    Undo.RegisterCreatedObjectUndo(go, "Create Environment Director");
                    Selection.activeGameObject = go;
                    ScanScene();
                }
                return;
            }

            // Pan/zoom input first so this frame draws with the updated view transform.
            float canvasHeight = Mathf.Max(EstimateCanvasHeight(), (position.height - ToolbarHeight) / _zoom);
            var viewport = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight);
            HandleCanvasInput(viewport, canvasHeight);

            // Two-pass draw: measure/draw nodes into rect maps, edges go underneath on the same pass
            // using last frame's rects (stable after one repaint, which IMGUI gives us for free).
            BeginZoomArea(viewport);
            GUI.BeginGroup(new Rect(-_pan.x, -_pan.y, CanvasWidth, canvasHeight));

            DrawEdges();
            DrawColumnHeaders();

            _nodeRects.Clear();
            _paramRects.Clear();
            DrawSignalsColumn();
            DrawParametersColumn();
            DrawOutputsColumn();
            DrawEventsColumn();

            GUI.EndGroup();
            EndZoomArea();
        }

        // ------------------------------------------------------------------ pan & zoom

        /**
         * Scroll = pan (Shift swaps axes), Ctrl+scroll = zoom toward the cursor,
         * middle-mouse or Alt+left drag = pan. All in unscaled window coordinates.
         */
        private void HandleCanvasInput(Rect viewport, float canvasHeight)
        {
            var e = Event.current;
            if (!viewport.Contains(e.mousePosition) && e.type != EventType.MouseDrag) return;

            if (e.type == EventType.ScrollWheel)
            {
                if (e.control || e.command)
                {
                    // Zoom around the cursor: keep the canvas point under the mouse stationary.
                    Vector2 mouseLocal = e.mousePosition - viewport.position;
                    Vector2 canvasPoint = _pan + mouseLocal / _zoom;
                    _zoom = Mathf.Clamp(_zoom * (1f - e.delta.y * 0.03f), MinZoom, MaxZoom);
                    _pan = canvasPoint - mouseLocal / _zoom;
                }
                else
                {
                    var delta = e.shift ? new Vector2(e.delta.y, e.delta.x) : e.delta;
                    _pan += delta * (13f / _zoom);
                }
                ClampPan(viewport, canvasHeight);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 0 && e.alt)))
            {
                _pan -= e.delta / _zoom;
                ClampPan(viewport, canvasHeight);
                e.Use();
                Repaint();
            }
        }

        private void ClampPan(Rect viewport, float canvasHeight)
        {
            _pan.x = Mathf.Clamp(_pan.x, 0f, Mathf.Max(0f, CanvasWidth - viewport.width / _zoom));
            _pan.y = Mathf.Clamp(_pan.y, 0f, Mathf.Max(0f, canvasHeight - viewport.height / _zoom));
        }

        /**
         * Standard IMGUI zoom-area trick: leave the implicit EditorWindow group, open a clip
         * rect sized for the zoomed content, and scale GUI.matrix around its top-left corner.
         * IMGUI transforms mouse events through GUI.matrix, so every control keeps working.
         */
        private void BeginZoomArea(Rect viewport)
        {
            GUI.EndGroup();
            var clipped = new Rect(viewport.x, viewport.y + TabHeight, viewport.width / _zoom, viewport.height / _zoom);
            GUI.BeginGroup(clipped);

            _prevGuiMatrix = GUI.matrix;
            var pivot = new Vector2(clipped.x, clipped.y);
            GUI.matrix = Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one)
                         * Matrix4x4.Scale(new Vector3(_zoom, _zoom, 1f))
                         * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one)
                         * GUI.matrix;
        }

        private void EndZoomArea()
        {
            GUI.matrix = _prevGuiMatrix;
            GUI.EndGroup();
            GUI.BeginGroup(new Rect(0f, TabHeight, position.width, position.height - TabHeight));
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Director picker (only shown when several exist — normally there's just one per scene).
            if (_directors.Length > 1)
            {
                var names = new string[_directors.Length];
                for (int i = 0; i < _directors.Length; i++) names[i] = _directors[i].name;
                _directorIndex = EditorGUILayout.Popup(_directorIndex, names, EditorStyles.toolbarPopup, GUILayout.Width(180f));
            }
            else if (_directors.Length == 1 && GUILayout.Button(_directors[0].name, EditorStyles.toolbarButton, GUILayout.Width(180f)))
            {
                PingAndSelect(_directors[0]);
            }

            if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(60f))) ScanScene();
            _showAudioLane = GUILayout.Toggle(_showAudioLane, "Audio", EditorStyles.toolbarButton, GUILayout.Width(50f));
            _showRulesLane = GUILayout.Toggle(_showRulesLane, "Rules", EditorStyles.toolbarButton, GUILayout.Width(50f));

            // Active-solo indicator with a clear button (Esc also clears).
            if (SoloActive)
            {
                string soloName = _soloKey is DirectorParameterDef def ? def.Id : _soloKey.name;
                GUILayout.Space(8f);
                GUILayout.Label($"◉ Solo: {soloName} ({(_soloSide == SoloSide.Input ? "input" : "output")})", EditorStyles.miniLabel);
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24f))) ClearSolo();
            }

            GUILayout.FlexibleSpace();

            // Zoom controls (Ctrl+scroll zooms toward the cursor; middle-drag or Alt+drag pans).
            GUILayout.Label($"{Mathf.RoundToInt(_zoom * 100f)}%", EditorStyles.miniLabel, GUILayout.Width(36f));
            _zoom = GUILayout.HorizontalSlider(_zoom, MinZoom, MaxZoom, GUILayout.Width(110f));
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(46f))) { _zoom = 1f; _pan = Vector2.zero; }

            GUILayout.Label(Application.isPlaying ? "● LIVE" : "edit mode", EditorStyles.miniLabel, GUILayout.Width(70f));

            if (GUILayout.Button("+ Add", EditorStyles.toolbarDropDown, GUILayout.Width(60f))) ShowAddMenu();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawColumnHeaders()
        {
            GUI.Label(new Rect(ColSignals, 4f, ColWidth, 18f), "SIGNALS & MODIFIERS", EditorStyles.boldLabel);
            GUI.Label(new Rect(ColParams, 4f, ColWidth, 18f), "SEMANTIC PARAMETERS", EditorStyles.boldLabel);
            GUI.Label(new Rect(ColOutputs, 4f, ColWidth, 18f), "OUTPUTS", EditorStyles.boldLabel);
            GUI.Label(new Rect(ColEvents, 4f, ColWidth, 18f), "EVENTS & AUDIO", EditorStyles.boldLabel);
        }

        private float EstimateCanvasHeight()
        {
            float signals = 30f + _signals.Count * 70f + _modTriggers.Count * 74f;
            float parameters = 30f + _parameters.Count * 190f;
            float outputs = 30f + _targets.Count * 108f;
            float events = 30f + (_showAudioLane ? 200f + Mathf.Max(_stingerDefs.Count, _oneShotDefs.Count) * 4f : 0f)
                           + (_showRulesLane ? _rules.Count * 120f : 0f);
            return Mathf.Max(Mathf.Max(signals, parameters), Mathf.Max(outputs, events)) + 60f;
        }

        // ------------------------------------------------------------------ edges

        /** Draws every wiring curve using the node rects captured on the previous repaint. */
        private void DrawEdges()
        {
            foreach (var c in _contributions)
            {
                if (c == null || c.Signal == null || c.Parameter == null) continue;
                if (!IsVisible(c.Signal) || !IsVisible(c.Parameter)) continue;
                if (_nodeRects.TryGetValue(c.Signal, out var from) && _paramRects.TryGetValue(c.Parameter, out var to))
                    DrawWire(from, to, BlendColor(c.Blend), c.isActiveAndEnabled ? 3f : 1.5f);
            }

            foreach (var m in _modTriggers)
            {
                if (m == null || m.Parameter == null) continue;
                if (!IsVisible(m) || !IsVisible(m.Parameter)) continue;
                if (_nodeRects.TryGetValue(m, out var from) && _paramRects.TryGetValue(m.Parameter, out var to))
                    DrawWire(from, to, new Color(1f, 0.7f, 0.2f, m.HasActiveModifier ? 1f : 0.45f), 2f);
            }

            // Parameter bindings: each output target may be bound to exactly one parameter.
            foreach (var t in _targets)
            {
                if (t == null || t.BoundParameter == null) continue;
                if (!IsVisible(t) || !IsVisible(t.BoundParameter)) continue;
                if (_paramRects.TryGetValue(t.BoundParameter, out var from) && _nodeRects.TryGetValue(t, out var to))
                    DrawWire(from, to, new Color(0.35f, 0.85f, 0.95f, 0.9f), t.isActiveAndEnabled ? 3f : 1.5f);
            }

            foreach (var r in _rules)
            {
                if (r == null || r.Parameter == null) continue;
                if (!IsVisible(r) || !IsVisible(r.Parameter)) continue;
                if (_paramRects.TryGetValue(r.Parameter, out var from) && _nodeRects.TryGetValue(r, out var to))
                    DrawWire(from, to, new Color(0.75f, 0.75f, 0.75f, 0.55f), 2f);
            }
        }

        private static void DrawWire(Rect from, Rect to, Color color, float width)
        {
            var start = new Vector3(from.xMax, from.center.y);
            var end = new Vector3(to.xMin, to.center.y);
            float tangent = Mathf.Clamp(Mathf.Abs(end.x - start.x) * 0.5f, 30f, 90f);
            Handles.DrawBezier(start, end, start + Vector3.right * tangent, end + Vector3.left * tangent, color, null, width);
        }

        private static Color BlendColor(ParameterBlendMode blend)
        {
            switch (blend)
            {
                case ParameterBlendMode.Add: return new Color(0.4f, 0.9f, 0.4f, 0.9f);
                case ParameterBlendMode.Max: return new Color(1f, 0.65f, 0.2f, 0.9f);
                case ParameterBlendMode.Min: return new Color(0.4f, 0.6f, 1f, 0.9f);
                case ParameterBlendMode.Multiply: return new Color(0.8f, 0.5f, 1f, 0.9f);
                default: return new Color(1f, 0.35f, 0.35f, 0.9f);
            }
        }

        // ------------------------------------------------------------------ signals column

        private void DrawSignalsColumn()
        {
            float y = 28f;
            foreach (var signal in _signals)
            {
                if (signal == null || !IsVisible(signal)) continue;
                bool manual = signal is ManualFloatSignal;
                var rect = new Rect(ColSignals, y, ColWidth, manual ? 82f : 62f);
                _nodeRects[signal] = rect;
                GUI.Box(rect, GUIContent.none, "helpBox");

                var inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
                NodeTitle(SoloHandles(inner, signal, false, true), signal.gameObject.name, TypeShortName(signal), signal);

                inner.y += 18f;
                GUI.Label(inner, $"value  {SafeSignalValue(signal):0.###}" + (signal.IsValid ? "" : "   (invalid)"), EditorStyles.miniLabel);

                // Manual test signals get their slider right in the graph.
                if (manual)
                {
                    inner.y += 18f;
                    var so = new SerializedObject(signal);
                    var valueProp = so.FindProperty("value");
                    var rangeProp = so.FindProperty("range");
                    EditorGUI.BeginChangeCheck();
                    float newValue = EditorGUI.Slider(new Rect(inner.x, inner.y, inner.width, 16f), valueProp.floatValue,
                        rangeProp.vector2Value.x, rangeProp.vector2Value.y);
                    if (EditorGUI.EndChangeCheck()) { valueProp.floatValue = newValue; so.ApplyModifiedProperties(); }
                }

                y += rect.height + RowGap;
            }

            // Scripted modifier triggers live here too — they're "signals" a script/event pushes in.
            foreach (var trigger in _modTriggers)
            {
                if (trigger == null || !IsVisible(trigger)) continue;
                var rect = new Rect(ColSignals, y, ColWidth, 66f);
                _nodeRects[trigger] = rect;
                GUI.Box(rect, GUIContent.none, "helpBox");

                var inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
                NodeTitle(SoloHandles(inner, trigger, false, true), trigger.gameObject.name, "Modifier", trigger);

                inner.y += 18f;
                string state = trigger.HasActiveModifier ? "● active" : "○ idle";
                GUI.Label(inner, $"{trigger.Blend} → {(trigger.Parameter != null ? trigger.Parameter.Id : "(none)")}   {state}", EditorStyles.miniLabel);

                inner.y += 18f;
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUI.Button(new Rect(inner.x, inner.y, 70f, 16f), "Apply", EditorStyles.miniButtonLeft)) trigger.Apply();
                    if (GUI.Button(new Rect(inner.x + 70f, inner.y, 70f, 16f), "Release", EditorStyles.miniButtonRight)) trigger.ReleaseModifier();
                }

                y += rect.height + RowGap;
            }
        }

        // ------------------------------------------------------------------ parameters column

        private void DrawParametersColumn()
        {
            // Live snapshots for contribution/modifier counts while playing.
            if (Application.isPlaying && Director != null) Director.GetParameterSnapshots(_snapshotBuffer);

            float y = 28f;
            foreach (var param in _parameters)
            {
                if (param == null || !IsVisible(param)) continue;

                // Collect this parameter's wiring rows up front so the node height fits them.
                var rows = new List<(string label, Object select)>();
                foreach (var c in _contributions)
                    if (c != null && c.Parameter == param)
                        rows.Add(($"◄ {(c.Signal != null ? c.Signal.gameObject.name : "(no signal)")}  ·  {c.Blend} ×{c.Weight:0.##}", c));
                foreach (var m in _modTriggers)
                    if (m != null && m.Parameter == param)
                        rows.Add(($"◄ {m.gameObject.name}  ·  {m.Blend} (modifier{(m.HasActiveModifier ? ", active" : "")})", m));

                int liveModifiers = 0;
                if (Application.isPlaying)
                    foreach (var snap in _snapshotBuffer)
                        if (snap.Def == param) liveModifiers = snap.ModifierCount;

                float height = 96f + rows.Count * 16f + (Application.isPlaying ? 46f : 0f);
                var rect = new Rect(ColParams, y, ColWidth, height);
                _paramRects[param] = rect;
                GUI.Box(rect, GUIContent.none, "helpBox");

                var inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
                NodeTitle(SoloHandles(inner, param, true, true), param.Id, "Parameter", param);

                // Value bar (normalized into the def's authored range).
                inner.y += 18f;
                float current = Director != null ? Director.GetValue(param) : param.baseValue;
                float target = Director != null ? Director.GetTarget(param) : param.baseValue;
                float normalized = Mathf.InverseLerp(param.minValue, param.maxValue, current);
                EditorGUI.ProgressBar(new Rect(inner.x, inner.y, inner.width, 15f), normalized, $"{current:0.000}  (target {target:0.000})");

                // Sparkline + debug override slider only mean something in play mode.
                if (Application.isPlaying)
                {
                    inner.y += 19f;
                    DrawSparkline(new Rect(inner.x, inner.y, inner.width, 22f), param);
                    inner.y += 24f;
                    DrawOverrideControls(new Rect(inner.x, inner.y, inner.width, 16f), param);
                    inner.y -= 4f;
                }

                // Incoming wiring rows — click to select the contribution/modifier component.
                inner.y += 20f;
                foreach (var row in rows)
                {
                    if (GUI.Button(new Rect(inner.x, inner.y, inner.width, 15f), row.label, EditorStyles.miniLabel)) PingAndSelect(row.select);
                    inner.y += 16f;
                }
                if (Application.isPlaying && liveModifiers > 0)
                {
                    GUI.Label(new Rect(inner.x, inner.y, inner.width, 15f), $"({liveModifiers} live modifier{(liveModifiers > 1 ? "s" : "")})", EditorStyles.centeredGreyMiniLabel);
                }

                // Creation strip.
                var strip = new Rect(inner.x, rect.yMax - 20f, inner.width, 16f);
                if (GUI.Button(new Rect(strip.x, strip.y, 88f, 16f), "+ Contribution", EditorStyles.miniButtonLeft)) ShowAddContributionMenu(param);
                if (GUI.Button(new Rect(strip.x + 88f, strip.y, 62f, 16f), "+ Output", EditorStyles.miniButtonMid)) ShowBindOutputMenu(param);
                if (GUI.Button(new Rect(strip.x + 150f, strip.y, 55f, 16f), "+ Rule", EditorStyles.miniButtonRight)) CreateRule(param);

                y += rect.height + RowGap;
            }
        }

        private void DrawSparkline(Rect rect, DirectorParameterDef param)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
            if (!_history.TryGetValue(param, out var buffer)) return;

            int head = _historyHead[param];
            var points = new Vector3[HistorySize];
            for (int i = 0; i < HistorySize; i++)
            {
                float value = buffer[(head + i) % HistorySize];
                float t = Mathf.InverseLerp(param.minValue, param.maxValue, value);
                points[i] = new Vector3(rect.x + rect.width * i / (HistorySize - 1f), rect.yMax - 1f - t * (rect.height - 2f));
            }
            Handles.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            Handles.DrawAAPolyLine(2f, points);
            Handles.color = Color.white;
        }

        /** Test-override toggle + slider: forces the parameter through a top-priority Override modifier. */
        private void DrawOverrideControls(Rect rect, DirectorParameterDef param)
        {
            bool has = Director.HasDebugOverride(param);
            bool wantOverride = GUI.Toggle(new Rect(rect.x, rect.y, 60f, 16f), has, "force", EditorStyles.miniButton);
            if (!_overrideValues.ContainsKey(param)) _overrideValues[param] = Director.GetValue(param);

            using (new EditorGUI.DisabledScope(!wantOverride))
            {
                EditorGUI.BeginChangeCheck();
                float newValue = GUI.HorizontalSlider(new Rect(rect.x + 66f, rect.y + 2f, rect.width - 66f, 14f),
                    _overrideValues[param], param.minValue, param.maxValue);
                if (EditorGUI.EndChangeCheck()) { _overrideValues[param] = newValue; if (wantOverride) Director.SetDebugOverride(param, newValue); }
            }

            if (wantOverride && !has) Director.SetDebugOverride(param, _overrideValues[param]);
            else if (!wantOverride && has) Director.ClearDebugOverride(param);
        }

        // ------------------------------------------------------------------ outputs column

        private void DrawOutputsColumn()
        {
            if (Application.isPlaying && _audioDirector != null) _audioDirector.GetAmbienceSnapshots(_ambienceBuffer);
            else _ambienceBuffer.Clear();

            float y = 28f;
            foreach (var target in _targets)
            {
                if (target == null || !IsVisible(target)) continue;
                bool isAmbience = target is AmbienceInfluenceTarget;
                var rect = new Rect(ColOutputs, y, ColWidth, (isAmbience ? 84f : 66f) + 16f);
                _nodeRects[target] = rect;
                GUI.Box(rect, GUIContent.none, "helpBox");

                var inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
                NodeTitle(SoloHandles(inner, target, true, false), target.TargetDescription, TypeShortName(target), target);

                // Binding row: which parameter drives this target's Baseline (or none).
                inner.y += 16f;
                GUI.Label(inner, target.BoundParameter != null
                    ? $"◄ {target.BoundParameter.Id}  →  {target.MapParameter(Director != null ? Director.GetValue(target.BoundParameter) : 0f):0.###}"
                    : "◄ (unbound — Baseline set externally)", EditorStyles.miniLabel);

                inner.y += 16f;
                GUI.Label(inner, $"final {target.FinalValue:0.000}   ·   B {target.Baseline:0.##}  A {target.Additive:0.##}  M {target.Multiplier:0.##}", EditorStyles.miniLabel);

                inner.y += 16f;
                GUI.Label(inner, "on " + target.gameObject.name, EditorStyles.centeredGreyMiniLabel);

                // Ambience targets also show what the AudioDirector is actually outputting for that layer.
                if (isAmbience && target is AmbienceInfluenceTarget ambience && ambience.Layer != null)
                {
                    inner.y += 16f;
                    float volume = 0f;
                    foreach (var snap in _ambienceBuffer)
                        if (snap.Def == ambience.Layer) volume = snap.Volume;
                    EditorGUI.ProgressBar(new Rect(inner.x, inner.y, inner.width, 13f), Mathf.Clamp01(volume), $"layer volume {volume:0.00}");
                }

                y += rect.height + RowGap;
            }
        }

        // ------------------------------------------------------------------ events & audio column

        private void DrawEventsColumn()
        {
            float y = 28f;

            // In solo mode the audio panel only appears when the soloed chain touches ambience.
            bool audioRelevant = !SoloActive;
            if (SoloActive)
                foreach (var t in _targets)
                    if (t is AmbienceInfluenceTarget && _soloVisible.Contains(t)) { audioRelevant = true; break; }
            if (_showAudioLane && audioRelevant) y = DrawAudioBox(y);

            if (!_showRulesLane) return;
            foreach (var rule in _rules)
            {
                if (rule == null || !IsVisible(rule)) continue;
                int listeners = rule.onTriggered.GetPersistentEventCount();
                var rect = new Rect(ColEvents, y, ColWidth, 92f + listeners * 14f);
                _nodeRects[rule] = rect;
                GUI.Box(rect, GUIContent.none, "helpBox");

                var inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
                NodeTitle(SoloHandles(inner, rule, true, false), rule.gameObject.name, "Rule", rule);

                inner.y += 18f;
                string arrow = rule.Direction == DirectorRule.TriggerDirection.RisesAbove ? "≥" : "≤";
                GUI.Label(inner, $"{(rule.Parameter != null ? rule.Parameter.Id : "(none)")} {arrow} {rule.TriggerThreshold:0.##}" +
                                 $"   cd {rule.CooldownDescription}   p={rule.Probability:0.##}{(rule.OneShot ? "   one-shot" : "")}", EditorStyles.miniLabel);

                inner.y += 16f;
                string state = !Application.isPlaying ? "—"
                    : rule.IsSpent ? "SPENT (one-shot fired)"
                    : !rule.IsArmed ? "disarmed (waiting reset)"
                    : rule.CooldownRemaining > 0f ? $"armed · cooldown {rule.CooldownRemaining:0.0}s"
                    : "armed · ready";
                GUI.Label(inner, state, EditorStyles.miniLabel);

                // Wired actions, each row selectable.
                inner.y += 16f;
                for (int i = 0; i < listeners; i++)
                {
                    var listenerTarget = rule.onTriggered.GetPersistentTarget(i);
                    string label = $"→ {(listenerTarget != null ? listenerTarget.name : "(missing)")}.{rule.onTriggered.GetPersistentMethodName(i)}";
                    if (GUI.Button(new Rect(inner.x, inner.y, inner.width, 13f), label, EditorStyles.miniLabel) && listenerTarget != null) PingAndSelect(listenerTarget);
                    inner.y += 14f;
                }

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    if (GUI.Button(new Rect(inner.x, rect.yMax - 20f, 80f, 16f), "Fire (test)", EditorStyles.miniButton)) rule.Fire();

                y += rect.height + RowGap;
            }
        }

        /** AudioDirector status + ambience volumes + quick-play strip for stingers/one-shots. */
        private float DrawAudioBox(float y)
        {
            int ambienceRows = _ambienceBuffer.Count;
            if (!Application.isPlaying && _audioDirector != null) ambienceRows = 0;
            float height = 66f + ambienceRows * 16f
                           + (_stingerDefs.Count + _oneShotDefs.Count > 0 ? 20f + (_stingerDefs.Count + _oneShotDefs.Count) * 16f : 0f);
            var rect = new Rect(ColEvents, y, ColWidth, height);
            GUI.Box(rect, GUIContent.none, "helpBox");

            var inner = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
            if (_audioDirector == null)
            {
                GUI.Label(inner, "No AudioDirector in scene", EditorStyles.boldLabel);
                return y + height + RowGap;
            }

            NodeTitle(inner, _audioDirector.gameObject.name, "AudioDirector", _audioDirector);
            inner.y += 18f;
            GUI.Label(inner, Application.isPlaying
                ? $"duck ×{_audioDirector.DuckMultiplier:0.00}   one-shots {_audioDirector.ActiveOneShotCount}   last stinger {Mathf.Min(_audioDirector.SecondsSinceAnyStinger, 999f):0}s ago"
                : "(enter play mode for live state)", EditorStyles.miniLabel);

            inner.y += 18f;
            foreach (var snap in _ambienceBuffer)
            {
                EditorGUI.ProgressBar(new Rect(inner.x, inner.y, inner.width - 4f, 13f), Mathf.Clamp01(snap.Volume),
                    $"{snap.Def.name}   inf {snap.Influence:0.00} · vol {snap.Volume:0.00}");
                inner.y += 16f;
            }

            // Quick-play: audition any stinger/one-shot def straight from the graph.
            if (_stingerDefs.Count + _oneShotDefs.Count > 0)
            {
                GUI.Label(new Rect(inner.x, inner.y, inner.width, 15f), "audition (play mode):", EditorStyles.centeredGreyMiniLabel);
                inner.y += 18f;
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    foreach (var def in _stingerDefs)
                    {
                        if (def == null) continue;
                        if (GUI.Button(new Rect(inner.x, inner.y, 20f, 14f), "▶", EditorStyles.miniButton)) _audioDirector.PlayStinger(def);
                        if (GUI.Button(new Rect(inner.x + 24f, inner.y, inner.width - 24f, 14f), def.name + "  (stinger)", EditorStyles.miniLabel)) PingAndSelect(def);
                        inner.y += 16f;
                    }
                    foreach (var def in _oneShotDefs)
                    {
                        if (def == null) continue;
                        if (GUI.Button(new Rect(inner.x, inner.y, 20f, 14f), "▶", EditorStyles.miniButton)) _audioDirector.PlayOneShot(def);
                        if (GUI.Button(new Rect(inner.x + 24f, inner.y, inner.width - 24f, 14f), def.name, EditorStyles.miniLabel)) PingAndSelect(def);
                        inner.y += 16f;
                    }
                }
            }

            return y + height + RowGap;
        }

        // ------------------------------------------------------------------ creation menus

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Signal/Manual (test slider)"), false, () => CreateSignal<ManualFloatSignal>("Manual Signal"));
            menu.AddItem(new GUIContent("Signal/Transform Depth"), false, () => CreateSignal<TransformDepthSignal>("Depth Signal"));
            menu.AddItem(new GUIContent("Signal/Timer"), false, () => CreateSignal<TimerSignal>("Timer Signal"));
            menu.AddItem(new GUIContent("Signal/Stinger Timer"), false, () => CreateSignal<StingerTimerSignal>("Stinger Timer Signal"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Asset/Parameter Def…"), false, () =>
                CreateDefAsset(CreateInstance<DirectorParameterDef>(), "Assets/Submachina/Data/Director/Parameters/NewParameter.asset"));
            menu.AddItem(new GUIContent("Asset/Ambience Layer Def…"), false, () =>
                CreateDefAsset(CreateInstance<AmbienceLayerDef>(), "Assets/Submachina/Data/Audio/Ambience/NewAmbienceLayer.asset"));
            menu.AddItem(new GUIContent("Asset/One-Shot Def…"), false, () =>
                CreateDefAsset(CreateInstance<AudioOneShotDef>(), "Assets/Submachina/Data/Audio/OneShots/NewOneShot.asset"));
            menu.AddItem(new GUIContent("Asset/Stinger Def…"), false, () =>
                CreateDefAsset(CreateInstance<AudioStingerDef>(), "Assets/Submachina/Data/Audio/Stingers/NewStinger.asset"));
            menu.ShowAsContext();
        }

        private void ShowAddContributionMenu(DirectorParameterDef param)
        {
            var menu = new GenericMenu();
            foreach (var signal in _signals)
            {
                if (signal == null) continue;
                var captured = signal;
                menu.AddItem(new GUIContent($"{signal.gameObject.name} ({TypeShortName(signal)})"), false, () =>
                {
                    var contribution = Undo.AddComponent<SignalContribution>(captured.gameObject);
                    var so = new SerializedObject(contribution);
                    so.FindProperty("director").objectReferenceValue = Director;
                    so.FindProperty("signal").objectReferenceValue = captured;
                    so.FindProperty("parameter").objectReferenceValue = param;
                    so.ApplyModifiedProperties();
                    PingAndSelect(contribution);
                    ScanScene();
                });
            }
            if (_signals.Count == 0) menu.AddDisabledItem(new GUIContent("(no signals in scene — use + Add ▸ Signal)"));
            menu.ShowAsContext();
        }

        /** Binds an existing output target's Baseline to this parameter. Already-bound targets are shown but disabled. */
        private void ShowBindOutputMenu(DirectorParameterDef param)
        {
            var menu = new GenericMenu();
            foreach (var target in _targets)
            {
                if (target == null) continue;
                var captured = target;
                string label = target.TargetDescription.Replace('/', '-');

                // One binding per target — offer rebinding only through the inspector, not by accident here.
                if (target.BoundParameter != null)
                {
                    menu.AddDisabledItem(new GUIContent($"{label}  (bound to {target.BoundParameter.Id})"));
                    continue;
                }

                menu.AddItem(new GUIContent(label), false, () =>
                {
                    var so = new SerializedObject(captured);
                    Undo.RecordObject(captured, "Bind Output");
                    so.FindProperty("director").objectReferenceValue = Director;
                    so.FindProperty("parameter").objectReferenceValue = param;
                    so.ApplyModifiedProperties();
                    PingAndSelect(captured);
                    ScanScene();
                });
            }
            if (_targets.Count == 0) menu.AddDisabledItem(new GUIContent("(no ModulatedFloatTargets in scene)"));
            menu.ShowAsContext();
        }

        private void CreateRule(DirectorParameterDef param)
        {
            var group = FindOrCreateGroup("Rules");
            var go = new GameObject($"Rule_{param.Id}");
            go.transform.SetParent(group.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Director Rule");
            var rule = go.AddComponent<DirectorRule>();
            var so = new SerializedObject(rule);
            so.FindProperty("director").objectReferenceValue = Director;
            so.FindProperty("parameter").objectReferenceValue = param;
            so.ApplyModifiedProperties();
            PingAndSelect(rule);
            ScanScene();
        }

        private void CreateSignal<T>(string name) where T : FloatSignal
        {
            var group = FindOrCreateGroup("Signals");
            var go = new GameObject(name);
            go.transform.SetParent(group.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Signal");
            var signal = go.AddComponent<T>();
            PingAndSelect(signal);
            ScanScene();
        }

        private static void CreateDefAsset(ScriptableObject instance, string path)
        {
            string folder = path.Substring(0, path.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(folder))
            {
                int slash = folder.LastIndexOf('/');
                AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
            }
            ProjectWindowUtil.CreateAsset(instance, path);
        }

        /** Group GameObjects live beside the director (same parent) so hierarchies stay tidy. */
        private GameObject FindOrCreateGroup(string groupName)
        {
            var parent = Director.transform.parent;
            if (parent != null)
                foreach (Transform child in parent)
                    if (child.name == groupName) return child.gameObject;

            var group = new GameObject(groupName);
            if (parent != null) group.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(group, "Create Group");
            return group;
        }

        // ------------------------------------------------------------------ small helpers

        /** Node title row: bold clickable name + grey type tag; clicking pings and selects the object. */
        private void NodeTitle(Rect rect, string title, string tag, Object select)
        {
            if (GUI.Button(rect, title, EditorStyles.boldLabel)) PingAndSelect(select);
            var size = EditorStyles.boldLabel.CalcSize(new GUIContent(title));
            GUI.Label(new Rect(rect.x + Mathf.Min(size.x, rect.width - 60f) + 6f, rect.y + 1f, 80f, 14f), tag, EditorStyles.centeredGreyMiniLabel);
        }

        private static void PingAndSelect(Object obj)
        {
            if (obj == null) return;
            var component = obj as Component;
            EditorGUIUtility.PingObject(component != null ? component.gameObject : obj);
            Selection.activeObject = obj;
        }

        private static float SafeSignalValue(FloatSignal signal)
        {
            try { return signal.Value; }
            catch { return float.NaN; }
        }

        /** "ManualFloatSignal" → "Manual", "Light2DFloatTarget" → "Light2D", "TransformDepthSignal" → "TransformDepth". */
        private static string TypeShortName(Object obj)
        {
            string name = obj.GetType().Name;
            foreach (var suffix in new[] { "FloatSignal", "FloatTarget", "InfluenceTarget", "TimerSignal", "Signal", "Target" })
                if (name.EndsWith(suffix) && name.Length > suffix.Length) return name.Substring(0, name.Length - suffix.Length);
            return name;
        }
    }
}
