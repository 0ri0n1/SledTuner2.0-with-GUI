using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;

namespace SledTunerProject
{
    public class GUIManager
    {
        // === REFERENCES ===
        private SledParameterManager _sledParameterManager;
        private ConfigManager _configManager;

        // === WINDOW & FIELD STATE ===
        private bool _menuOpen = false;
        private Rect _windowRect;
        private Vector2 _scrollPos = Vector2.zero;
        // Reflection-based parameters (string-based for display):
        //   Component -> (Field -> string)
        public Dictionary<string, string[]> ComponentsToInspect => _sledParameterManager.ComponentsToInspect;
        private Dictionary<string, Dictionary<string, string>> _fieldInputs = new Dictionary<string, Dictionary<string, string>>();
        // For Advanced Tree View foldouts
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        // === GUI STYLES (initialized in OnGUI if null) ===
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _headerStyle;

        // === ADDITIONAL FEATURES STATE ===
        private bool _manualApply = true;  // If true, changes only commit after "Apply".
        private bool _showHelp = false;
        private bool _advancedView = true; // Switch between Simple/Advanced
        private bool _treeViewEnabled = true; // Collapsible advanced parameters

        // === WINDOW CONTROLS & RESIZING ===
        private bool _isMinimized = false;
        private Rect _prevWindowRect;
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Rect _resizeStartWindowRect;
        private ResizeEdges _resizeEdges;
        private float _opacity = 1f; // 0=transparent, 1=opaque

        private struct ResizeEdges
        {
            public bool left, right, top, bottom;
        }

        // === SIMPLE VIEW LOCAL FIELDS (NOT reflection) ===
        private float speed = 10f;
        private float gravity = -9.81f;
        private float power = 143000f;
        private float lugHeight = 0.18f;
        private float trackLength = 1f;
        private float pitchFactor = 7f;

        private bool notdriverInvincible = true;
        private bool test = false;
        private bool apply = false;

        // Original simple param defaults
        private float originalPower = 143000f;
        private float originalLugHeight = 0.18f;
        private float originalTrackLength = 1f;
        private float originalGravity = -9.81f;
        private float originalPitchFactor = 7f;

        // === COLOR PREVIEW TEXTURE ===
        private Texture2D _colorPreviewTexture;

        // === LOCAL PREVIEW FOR REFLECTION FIELDS (ADVANCED) ===
        // For numeric fields, we store the "live" double in _fieldPreview[compName][fieldName].
        // Only on button release or 'Apply' do we commit to SledParameterManager.
        private Dictionary<string, Dictionary<string, double>> _fieldPreview = new Dictionary<string, Dictionary<string, double>>();

        // For minus/plus detection in reflection-based fields:
        private HashSet<string> _minusHeldNow = new HashSet<string>();
        private HashSet<string> _minusHeldPrev = new HashSet<string>();
        private HashSet<string> _plusHeldNow = new HashSet<string>();
        private HashSet<string> _plusHeldPrev = new HashSet<string>();

        // === DEBOUNCE / THROTTLE VARIABLES ===
        private bool _pendingCommit = false;
        private float _lastCommitTime = 0f;
        private const float _commitDelay = 0.2f; // 200 milliseconds

        // === CONSTRUCTOR ===
        public GUIManager(SledParameterManager sledParameterManager, ConfigManager configManager)
        {
            _sledParameterManager = sledParameterManager;
            _configManager = configManager;

            _windowRect = new Rect(
                Screen.width * 0.2f,
                Screen.height * 0.2f,
                Screen.width * 0.6f,
                Screen.height * 0.6f
            );
            _resizeEdges = new ResizeEdges();
            _advancedView = true;
            _treeViewEnabled = true;
        }

        // === PUBLIC METHODS ===

        /// <summary>
        /// Toggles the menu. On open, we populate from SledParameterManager
        /// but do not re-run reflection each time a user presses +/-.
        /// </summary>
        public void ToggleMenu()
        {
            _menuOpen = !_menuOpen;
            if (_menuOpen)
            {
                RePopulateFields();
                MelonLogger.Msg("[GUIManager] Menu opened.");
            }
            else
            {
                MelonLogger.Msg("[GUIManager] Menu closed.");
            }
        }

        /// <summary>
        /// Loads current param data from SledParameterManager into local dictionaries
        /// so we do not have to re-run reflection or cause flicker mid-press.
        /// </summary>
        public void RePopulateFields()
        {
            _fieldInputs.Clear();
            _fieldPreview.Clear();

            var currentParams = _sledParameterManager.GetCurrentParameters();
            foreach (var compEntry in _sledParameterManager.ComponentsToInspect)
            {
                string compName = compEntry.Key;
                _fieldInputs[compName] = new Dictionary<string, string>();
                _fieldPreview[compName] = new Dictionary<string, double>();

                foreach (string field in compEntry.Value)
                {
                    object val = _sledParameterManager.GetFieldValue(compName, field);
                    string valStr = (val != null) ? val.ToString() : "(No data)";
                    _fieldInputs[compName][field] = valStr;

                    double numericVal = 0.0;
                    if (val is double dd) numericVal = dd;
                    else if (val is float ff) numericVal = ff;
                    else if (val is int ii) numericVal = ii;
                    else
                    {
                        double tryD;
                        if (double.TryParse(valStr, out tryD))
                            numericVal = tryD;
                    }
                    _fieldPreview[compName][field] = numericVal;
                }
            }

            foreach (var comp in _fieldInputs.Keys)
            {
                if (!_foldoutStates.ContainsKey(comp))
                    _foldoutStates[comp] = true;
            }
            MelonLogger.Msg("[GUIManager] Fields repopulated.");
        }

        /// <summary>
        /// Main IMGUI draw call.
        /// We do not re-run reflection each time; we only manipulate local data.
        /// </summary>
        public void DrawMenu()
        {
            if (!_menuOpen)
                return;

            // Move 'now' sets to 'prev' sets, then clear 'now'
            _minusHeldPrev = new HashSet<string>(_minusHeldNow);
            _plusHeldPrev = new HashSet<string>(_plusHeldNow);
            _minusHeldNow.Clear();
            _plusHeldNow.Clear();

            if (_windowStyle == null)
            {
                _windowStyle = new GUIStyle(GUI.skin.window);
                _labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12 };
                _textFieldStyle = new GUIStyle(GUI.skin.textField);
                _buttonStyle = new GUIStyle(GUI.skin.button);
                _toggleStyle = new GUIStyle(GUI.skin.toggle);
                _foldoutStyle = new GUIStyle(GUI.skin.toggle) { richText = true, fontSize = 13 };
                _headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            }

            Color prevColor = GUI.color;
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, _opacity);

            _windowRect = GUILayout.Window(1234, _windowRect, WindowFunction, "SledTuner Menu", _windowStyle);

            GUI.color = prevColor;
            HandleResize();

            // If a commit is pending and enough time has passed, commit all pending changes.
            if (_pendingCommit && (Time.realtimeSinceStartup - _lastCommitTime >= _commitDelay))
            {
                CommitPendingReflectionChanges();
                _pendingCommit = false;
            }
        }

        private void WindowFunction(int windowID)
        {
            // If the sled is not initialized, show a message and a "Retry" button.
            if (!_sledParameterManager.IsInitialized)
            {
                GUILayout.Label("<color=red>No sled spawned. Please wait for the sled to load.</color>", _labelStyle);
                if (GUILayout.Button("Retry", _buttonStyle))
                {
                    MelonLogger.Msg("[GUIManager] Retrying to initialize sled...");
                    _sledParameterManager.InitializeComponents();
                    RePopulateFields();
                }
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
                return;
            }

            DrawTitleBar();
            GUILayout.Space(5);

            if (_advancedView)
                DrawAdvancedTunerMenu();
            else
                DrawSimpleTunerMenu();

            // On Repaint, detect newly released minus/plus buttons to flag pending commit.
            if (Event.current.type == EventType.Repaint)
            {
                DetectButtonReleases();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        /// <summary>
        /// Checks if any minus/plus button that was held in the previous frame is no longer held.
        /// Instead of committing immediately per field, we now mark a pending commit.
        /// </summary>
        private void DetectButtonReleases()
        {
            bool commitNeeded = false;
            foreach (string key in _minusHeldPrev)
            {
                if (!_minusHeldNow.Contains(key))
                {
                    commitNeeded = true;
                    break;
                }
            }
            if (!commitNeeded)
            {
                foreach (string key in _plusHeldPrev)
                {
                    if (!_plusHeldNow.Contains(key))
                    {
                        commitNeeded = true;
                        break;
                    }
                }
            }

            if (commitNeeded)
            {
                _pendingCommit = true;
                _lastCommitTime = Time.realtimeSinceStartup;
                MelonLogger.Msg("[GUIManager] Commit pending (debounced).");
            }
        }

        /// <summary>
        /// Commits all reflection-based fields from the _fieldPreview to the SledParameterManager.
        /// This is called after the debounce delay has passed.
        /// </summary>
        private void CommitPendingReflectionChanges()
        {
            MelonLogger.Msg("[GUIManager] Committing pending reflection changes.");
            foreach (var compKvp in _fieldPreview)
            {
                string compName = compKvp.Key;
                foreach (var fieldKvp in compKvp.Value)
                {
                    string fieldName = fieldKvp.Key;
                    double val = fieldKvp.Value;

                    Type ft = _sledParameterManager.GetFieldType(compName, fieldName);
                    if (ft == typeof(float) || ft == typeof(double) || ft == typeof(int))
                    {
                        _sledParameterManager.SetFieldValue(compName, fieldName, val);
                    }
                    else if (ft == typeof(bool))
                    {
                        bool b;
                        bool.TryParse(_fieldInputs[compName][fieldName], out b);
                        _sledParameterManager.SetFieldValue(compName, fieldName, b);
                    }
                    else if (compName == "Light" &&
                             (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
                    {
                        if (_fieldInputs[compName].TryGetValue(fieldName, out string channelStr))
                        {
                            if (float.TryParse(channelStr, out float cVal))
                            {
                                _sledParameterManager.SetFieldValue(compName, fieldName, cVal);
                            }
                        }
                    }
                }
            }
            if (!_manualApply)
            {
                MelonLogger.Msg("[GUIManager] Auto-applying changes.");
                _sledParameterManager.ApplyParameters();
            }
        }

        private void DrawTitleBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("SledTuner Menu", _headerStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("?", _buttonStyle, GUILayout.Width(30)))
                _showHelp = !_showHelp;
            if (GUILayout.Button("_", _buttonStyle, GUILayout.Width(30)))
                MinimizeWindow();
            if (GUILayout.Button("[ ]", _buttonStyle, GUILayout.Width(30)))
                MaximizeWindow();
            if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(30)))
                CloseWindow();
            if (GUILayout.Button("Switch View", _buttonStyle, GUILayout.Width(100)))
            {
                _advancedView = !_advancedView;
                MelonLogger.Msg("[GUIManager] Switched to " + (_advancedView ? "Advanced View" : "Simple View") + ".");
            }
            GUILayout.EndHorizontal();

            if (_showHelp)
            {
                DrawHelpPanel();
                GUILayout.Space(5);
            }
        }

        private void DrawAdvancedTunerMenu()
        {
            DrawConfigButtons();
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            _manualApply = GUILayout.Toggle(_manualApply, "Manual Apply", _toggleStyle, GUILayout.Width(120));
            _treeViewEnabled = GUILayout.Toggle(_treeViewEnabled, "Tree View", _toggleStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            if (_treeViewEnabled)
                DrawTreeViewParameters();
            else
                DrawAdvancedFlatParameters();
            GUILayout.EndScrollView();

            GUILayout.Space(5);
            DrawFooter();
            GUILayout.Space(5);
            DrawOpacitySlider();
        }

        private void DrawSimpleTunerMenu()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Simple Tuner Menu", _headerStyle);
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            apply = GUILayout.Toggle(apply, "Apply", _toggleStyle, GUILayout.Width(80));
            if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Width(80)))
                ResetValues();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // local fields only
            GUILayout.Label("FlySpeed");
            speed = DrawLocalFloatField("simple.speed", speed, 0f, 200f);

            GUILayout.Label("Gravity");
            gravity = DrawLocalFloatField("simple.gravity", gravity, -10f, 10f);

            GUILayout.Label("Power");
            power = DrawLocalFloatField("simple.power", power, 0f, 300000f);

            GUILayout.Label("Lug Height");
            lugHeight = DrawLocalFloatField("simple.lugHeight", lugHeight, 0f, 2f);

            GUILayout.Label("Track Length");
            trackLength = DrawLocalFloatField("simple.trackLength", trackLength, 0.5f, 2f);

            GUILayout.Label("Pitch Factor");
            pitchFactor = DrawLocalFloatField("simple.pitchFactor", pitchFactor, 2f, 30f);

            GUILayout.Space(10);

            // Now we unify color channels for "Light" (both simple & advanced can call the same code).
            GUILayout.Label("Headlight Color (RGBA)");
            if (_fieldInputs.ContainsKey("Light"))
            {
                DrawColorChannelsCommon("Light", isAdvanced: false);
            }
            else
            {
                GUILayout.Label("(No Light component found)", _labelStyle);
            }

            // color preview
            float rVal = 1f, gVal = 1f, bVal = 1f, aVal = 1f;
            if (_fieldInputs.ContainsKey("Light"))
            {
                var dict = _fieldInputs["Light"];
                if (dict.TryGetValue("r", out string tmp)) float.TryParse(tmp, out rVal);
                if (dict.TryGetValue("g", out tmp)) float.TryParse(tmp, out gVal);
                if (dict.TryGetValue("b", out tmp)) float.TryParse(tmp, out bVal);
                if (dict.TryGetValue("a", out tmp)) float.TryParse(tmp, out aVal);
            }
            Color currentColor = new Color(rVal, gVal, bVal, aVal);
            GUILayout.Label("Color Preview:");
            UpdateColorPreviewTexture(currentColor);
            GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));

            GUILayout.Space(10);
            notdriverInvincible = GUILayout.Toggle(notdriverInvincible, "Driver Ragdoll", _toggleStyle, GUILayout.Width(150));
            test = GUILayout.Toggle(test, "Test", _toggleStyle, GUILayout.Width(150));

            GUILayout.Space(10);
            GUILayout.Label("Made by Samisalami", _labelStyle, GUILayout.Width(200));
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Single-step local approach for simple tuner float fields.
        /// We do NOT commit to SledParameterManager each frame;
        /// we only store changes in local variable for the user to see.
        /// </summary>
        private float DrawLocalFloatField(string uniqueKey, float currentVal, float min, float max)
        {
            float newVal = GUILayout.HorizontalSlider(currentVal, min, max, GUILayout.Width(150));
            string textVal = GUILayout.TextField(newVal.ToString("F2"), GUILayout.Width(50));
            if (float.TryParse(textVal, out float parsed))
                newVal = parsed;

            GUILayout.BeginHorizontal(GUILayout.Width(60));
            // minus
            string minusKey = uniqueKey + ".minus";
            bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
            if (minusHeld)
            {
                _minusHeldNow.Add(minusKey);
            }

            // plus
            string plusKey = uniqueKey + ".plus";
            bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
            if (plusHeld)
            {
                _plusHeldNow.Add(plusKey);
            }
            GUILayout.EndHorizontal();

            // On Repaint, do a single step if held
            if (Event.current.type == EventType.Repaint)
            {
                if (_minusHeldNow.Contains(minusKey))
                    newVal = Mathf.Max(min, newVal - 0.01f);
                if (_plusHeldNow.Contains(plusKey))
                    newVal = Mathf.Min(max, newVal + 0.01f);
            }
            return newVal;
        }

        /// <summary>
        /// Draw color channels for "Light" from the same code,
        /// used by both Simple (isAdvanced=false) and Advanced (isAdvanced=true).
        /// </summary>
        private void DrawColorChannelsCommon(string compName, bool isAdvanced)
        {
            // compName should be "Light"
            if (!_fieldInputs.ContainsKey(compName))
                return;

            string[] channels = { "r", "g", "b", "a" };
            foreach (string ch in channels)
            {
                if (!_fieldInputs[compName].ContainsKey(ch))
                    continue;

                if (!float.TryParse(_fieldInputs[compName][ch], out float curVal))
                    curVal = 0f;
                float newVal = curVal;

                GUILayout.BeginHorizontal();
                string label = (ch == "r") ? "Red" :
                               (ch == "g") ? "Green" :
                               (ch == "b") ? "Blue" :
                               (ch == "a") ? "Alpha" : ch;

                GUILayout.Label(label + ":", _labelStyle, GUILayout.Width(40));
                newVal = GUILayout.HorizontalSlider(newVal, 0f, 1f, GUILayout.Width(150));
                string textVal = GUILayout.TextField(newVal.ToString("F2"), _textFieldStyle, GUILayout.Width(40));
                if (float.TryParse(textVal, out float parsedVal))
                    newVal = parsedVal;

                // minus
                string minusKey = compName + "." + ch + ".minus";
                bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                if (minusHeld)
                    _minusHeldNow.Add(minusKey);

                // plus
                string plusKey = compName + "." + ch + ".plus";
                bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
                if (plusHeld)
                    _plusHeldNow.Add(plusKey);

                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.Repaint)
                {
                    if (_minusHeldNow.Contains(minusKey))
                        newVal = Mathf.Max(0f, newVal - 0.01f);
                    if (_plusHeldNow.Contains(plusKey))
                        newVal = Mathf.Min(1f, newVal + 0.01f);
                }

                if (Mathf.Abs(newVal - curVal) > 0.0001f)
                {
                    // update the UI string
                    _fieldInputs[compName][ch] = newVal.ToString("F2");

                    if (isAdvanced)
                    {
                        // In advanced mode, we also store numeric in _fieldPreview
                        // so that final commit can happen on release or 'Apply'
                        if (_fieldPreview.ContainsKey(compName) &&
                            _fieldPreview[compName].ContainsKey(ch))
                        {
                            _fieldPreview[compName][ch] = newVal;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to draw numeric fields using a style similar to the Light color channels.
        /// </summary>
        private void DrawNumericField(string compName, string fieldName, double currentVal, float sliderMin, float sliderMax, double step)
        {
            GUILayout.BeginHorizontal();
            // Use a moderately sized label (adjust width as needed)
            GUILayout.Label(fieldName + ":", _labelStyle, GUILayout.Width(100));
            float sliderVal = GUILayout.HorizontalSlider((float)currentVal, sliderMin, sliderMax, GUILayout.Width(150));
            string textVal = GUILayout.TextField(sliderVal.ToString("F2"), _textFieldStyle, GUILayout.Width(40));
            if (float.TryParse(textVal, out float parsedVal))
                sliderVal = parsedVal;
            // +/- buttons
            string minusKey = compName + "." + fieldName + ".minus";
            bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
            if (minusHeld)
                _minusHeldNow.Add(minusKey);
            string plusKey = compName + "." + fieldName + ".plus";
            bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
            if (plusHeld)
                _plusHeldNow.Add(plusKey);
            GUILayout.EndHorizontal();

            float newVal = sliderVal;
            if (Event.current.type == EventType.Repaint)
            {
                if (_minusHeldNow.Contains(minusKey))
                    newVal = Mathf.Max(sliderMin, newVal - (float)step);
                if (_plusHeldNow.Contains(plusKey))
                    newVal = Mathf.Min(sliderMax, newVal + (float)step);
            }
            if (Math.Abs(newVal - currentVal) > 0.0001)
            {
                _fieldPreview[compName][fieldName] = newVal;
                _fieldInputs[compName][fieldName] = newVal.ToString("F2");
            }
        }

        /// <summary>
        /// Reflection-based advanced parameters in a flat list.
        /// We'll skip color channels in the normal numeric loop below and call DrawColorChannelsCommon for 'Light'.
        /// </summary>
        private void DrawAdvancedFlatParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                GUILayout.Label("<b>Component: " + comp.Key + "</b>", _labelStyle);

                if (comp.Key == "Light")
                {
                    // unify color channels
                    DrawColorChannelsCommon("Light", isAdvanced: true);

                    // color preview
                    GUILayout.Space(5);
                    GUILayout.Label("Color Preview:", _labelStyle);
                    float r = 1f, g = 1f, b = 1f, a = 1f;
                    if (comp.Value.ContainsKey("r")) float.TryParse(comp.Value["r"], out r);
                    if (comp.Value.ContainsKey("g")) float.TryParse(comp.Value["g"], out g);
                    if (comp.Value.ContainsKey("b")) float.TryParse(comp.Value["b"], out b);
                    if (comp.Value.ContainsKey("a")) float.TryParse(comp.Value["a"], out a);
                    Color previewColor = new Color(r, g, b, a);
                    UpdateColorPreviewTexture(previewColor);
                    GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));
                }
                else
                {
                    DrawReflectionParameters(comp.Key, comp.Value);
                }

                GUILayout.Space(10);
            }
        }

        private void DrawTreeViewParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                if (!_foldoutStates.ContainsKey(comp.Key))
                    _foldoutStates[comp.Key] = true;

                _foldoutStates[comp.Key] = GUILayout.Toggle(
                    _foldoutStates[comp.Key],
                    "<b>" + comp.Key + "</b>",
                    _foldoutStyle
                );

                if (_foldoutStates[comp.Key])
                {
                    GUILayout.BeginVertical(GUI.skin.box);

                    if (comp.Key == "Light")
                    {
                        // unify color channels
                        DrawColorChannelsCommon("Light", isAdvanced: true);

                        // color preview
                        GUILayout.Space(5);
                        GUILayout.Label("Color Preview:", _labelStyle);
                        float r = 1f, g = 1f, b = 1f, a = 1f;
                        if (comp.Value.ContainsKey("r")) float.TryParse(comp.Value["r"], out r);
                        if (comp.Value.ContainsKey("g")) float.TryParse(comp.Value["g"], out g);
                        if (comp.Value.ContainsKey("b")) float.TryParse(comp.Value["b"], out b);
                        if (comp.Value.ContainsKey("a")) float.TryParse(comp.Value["a"], out a);
                        Color previewColor = new Color(r, g, b, a);
                        UpdateColorPreviewTexture(previewColor);
                        GUILayout.Box(_colorPreviewTexture, GUILayout.Width(30), GUILayout.Height(30));
                    }
                    else
                    {
                        DrawReflectionParameters(comp.Key, comp.Value);
                    }

                    GUILayout.EndVertical();
                }
                GUILayout.Space(10);
            }
        }

        /// <summary>
        /// Reflection-based numeric/bool fields.
        /// For color channels, we skip them here and call DrawColorChannelsCommon.
        /// For numeric fields, we now call DrawNumericField so that their layout and update behavior match that of Light.
        /// </summary>
        private void DrawReflectionParameters(string compName, Dictionary<string, string> fields)
        {
            foreach (var kvp in fields)
            {
                string fieldName = kvp.Key;
                // skip color channels for Light => handled in DrawColorChannelsCommon
                if (compName == "Light" &&
                    (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(fieldName + ":", _labelStyle, GUILayout.Width(100));

                Type fieldType = _sledParameterManager.GetFieldType(compName, fieldName);
                double currentVal = _fieldPreview[compName][fieldName]; // numeric preview

                if (fieldType == typeof(float) || fieldType == typeof(double) || fieldType == typeof(int))
                {
                    float sliderMin = _sledParameterManager.GetSliderMin(compName, fieldName);
                    float sliderMax = _sledParameterManager.GetSliderMax(compName, fieldName);
                    double step = (fieldType == typeof(int)) ? 1.0 : 0.01;
                    GUILayout.EndHorizontal(); // End the current horizontal group before calling our helper
                    DrawNumericField(compName, fieldName, currentVal, sliderMin, sliderMax, step);
                }
                else if (fieldType == typeof(bool))
                {
                    // parse as bool
                    bool curBool = false;
                    bool.TryParse(_fieldInputs[compName][fieldName], out curBool);
                    bool newBool = GUILayout.Toggle(curBool, curBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
                    if (newBool != curBool)
                    {
                        _fieldInputs[compName][fieldName] = newBool.ToString();
                        // if manual is off, commit immediately
                        if (!_manualApply)
                        {
                            _sledParameterManager.SetFieldValue(compName, fieldName, newBool);
                            _sledParameterManager.ApplyParameters();
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    // fallback: string or other type
                    string newVal = GUILayout.TextField(
                        _fieldInputs[compName][fieldName],
                        _textFieldStyle,
                        GUILayout.ExpandWidth(true)
                    );
                    if (newVal != _fieldInputs[compName][fieldName])
                    {
                        _fieldInputs[compName][fieldName] = newVal;
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawConfigButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.LoadConfiguration();
                RePopulateFields(); // after load, re-populate
                MelonLogger.Msg("[GUIManager] Load clicked.");
            }
            if (GUILayout.Button("Save", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.SaveConfiguration();
                MelonLogger.Msg("[GUIManager] Save clicked.");
            }
            if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.ResetParameters();
                _sledParameterManager.RevertParameters();
                RePopulateFields();
                MelonLogger.Msg("[GUIManager] Reset clicked.");
            }
            if (GUILayout.Button("Apply", _buttonStyle, GUILayout.Height(25)))
            {
                ApplyChanges();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle Ragdoll", _buttonStyle, GUILayout.Width(120)))
                _configManager.ToggleRagdoll();
            if (GUILayout.Button("Toggle Tree Renderer", _buttonStyle, GUILayout.Width(150)))
                _configManager.ToggleTreeRenderer();
            if (GUILayout.Button("Teleport", _buttonStyle, GUILayout.Width(100)))
                _configManager.TeleportSled();
            GUILayout.EndHorizontal();
        }

        private void DrawOpacitySlider()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity:", GUILayout.Width(70));
            float newOpacity = GUILayout.HorizontalSlider(_opacity, 0.1f, 1f, GUILayout.Width(150));
            _opacity = newOpacity;
            GUILayout.Label((_opacity * 100f).ToString("F0") + "%", GUILayout.Width(40));
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Called by "Apply" button. Commits advanced reflection fields from _fieldPreview
        /// to the SledParameterManager. Simple fields remain local unless we want to sync them too.
        /// </summary>
        private void ApplyChanges()
        {
            // commit reflection-based fields
            foreach (var compKvp in _fieldPreview)
            {
                string compName = compKvp.Key;
                foreach (var fieldKvp in compKvp.Value)
                {
                    string fieldName = fieldKvp.Key;
                    double val = fieldKvp.Value;

                    Type ft = _sledParameterManager.GetFieldType(compName, fieldName);
                    if (ft == typeof(float) || ft == typeof(double) || ft == typeof(int))
                    {
                        _sledParameterManager.SetFieldValue(compName, fieldName, val);
                    }
                    else if (ft == typeof(bool))
                    {
                        bool b;
                        bool.TryParse(_fieldInputs[compName][fieldName], out b);
                        _sledParameterManager.SetFieldValue(compName, fieldName, b);
                    }
                    else if (compName == "Light" &&
                             (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
                    {
                        // If we want to commit color channels on Apply:
                        // parse from _fieldInputs and do SetFieldValue
                        if (_fieldInputs[compName].TryGetValue(fieldName, out string channelStr))
                        {
                            if (float.TryParse(channelStr, out float cVal))
                            {
                                _sledParameterManager.SetFieldValue(compName, fieldName, cVal);
                            }
                        }
                    }
                }
            }
            _sledParameterManager.ApplyParameters();
            MelonLogger.Msg("[GUIManager] Changes applied.");
        }

        private void ResetValues()
        {
            // local simple fields
            speed = 10f;
            gravity = originalGravity;
            power = originalPower;
            lugHeight = originalLugHeight;
            trackLength = originalTrackLength;
            pitchFactor = originalPitchFactor;

            notdriverInvincible = true;
            test = false;
        }

        private void HandleResize()
        {
            Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            bool nearLeft = mousePos.x >= _windowRect.x && mousePos.x <= _windowRect.x + 10f;
            bool nearRight = mousePos.x >= _windowRect.xMax - 10f && mousePos.x <= _windowRect.xMax;
            bool nearTop = mousePos.y >= _windowRect.y && mousePos.y <= _windowRect.y + 10f;
            bool nearBottom = mousePos.y >= _windowRect.yMax - 10f && mousePos.y <= _windowRect.yMax;

            if (Event.current.type == EventType.MouseDown && (nearLeft || nearRight || nearTop || nearBottom))
            {
                _isResizing = true;
                _resizeStartMousePos = mousePos;
                _resizeStartWindowRect = _windowRect;
                _resizeEdges.left = nearLeft;
                _resizeEdges.right = nearRight;
                _resizeEdges.top = nearTop;
                _resizeEdges.bottom = nearBottom;
                Event.current.Use();
            }

            if (_isResizing)
            {
                if (Event.current.rawType == EventType.MouseDrag || Event.current.rawType == EventType.MouseMove)
                {
                    Vector2 delta = mousePos - _resizeStartMousePos;
                    Rect newRect = _resizeStartWindowRect;
                    float minWidth = Screen.width * 0.3f;
                    float maxWidth = Screen.width * 0.8f;
                    float minHeight = 30f;
                    float maxHeight = Screen.height * 0.8f;

                    if (_resizeEdges.right)
                        newRect.width = Mathf.Clamp(_resizeStartWindowRect.width + delta.x, minWidth, maxWidth);
                    if (_resizeEdges.left)
                    {
                        newRect.x = _resizeStartWindowRect.x + delta.x;
                        newRect.width = Mathf.Clamp(_resizeStartWindowRect.width - delta.x, minWidth, maxWidth);
                    }
                    if (_resizeEdges.bottom)
                        newRect.height = Mathf.Clamp(_resizeStartWindowRect.height + delta.y, minHeight, maxHeight);
                    if (_resizeEdges.top)
                    {
                        newRect.y = _resizeStartWindowRect.y + delta.y;
                        newRect.height = Mathf.Clamp(_resizeStartWindowRect.height - delta.y, minHeight, maxHeight);
                    }
                    _windowRect = newRect;
                    Event.current.Use();
                }
                if (Event.current.type == EventType.MouseUp)
                {
                    _isResizing = false;
                    _resizeEdges = new ResizeEdges();
                    Event.current.Use();
                }
            }
        }

        private void MinimizeWindow()
        {
            if (!_isMinimized)
            {
                _prevWindowRect = _windowRect;
                _windowRect = new Rect(Screen.width - 150f - 10f, 10f, 150f, 30f);
                _isMinimized = true;
                MelonLogger.Msg("[GUIManager] Window minimized.");
            }
        }

        private void MaximizeWindow()
        {
            if (_isMinimized)
            {
                _windowRect = _prevWindowRect;
                _isMinimized = false;
                MelonLogger.Msg("[GUIManager] Window restored from minimized state.");
            }
            else
            {
                _windowRect.width = Screen.width * 0.8f;
                _windowRect.height = Screen.height * 0.8f;
                MelonLogger.Msg("[GUIManager] Window maximized.");
            }
            _menuOpen = true;
        }

        private void CloseWindow()
        {
            _menuOpen = false;
            _isMinimized = false;
            MelonLogger.Msg("[GUIManager] Window closed.");
        }

        private void DrawHelpPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Help:</b>\n\n" +
                "Hotkeys:\n  F2 - Toggle Menu\n  F3 - Refresh Fields\n\n" +
                "Instructions:\n" +
                "  - Adjust parameters using sliders, text fields, or +/- (click-and-hold) buttons.\n" +
                "  - Color channels for 'Light' are drawn the same in Simple/Advanced for easier maintenance.\n" +
                "  - In Advanced mode, only on button release or 'Apply' do changes commit (if Manual Apply is off).\n" +
                "  - If 'Manual Apply' is on, changes only commit when you press 'Apply'.\n" +
                "  - Use window buttons to Minimize, Maximize, or Close.\n" +
                "  - Footer toggles: Ragdoll, Tree Renderer, Teleport.\n" +
                "  - 'Tree View' collapses advanced components. 'Switch View' toggles Simple/Advanced.\n",
                _labelStyle);
            GUILayout.EndVertical();
        }

        private void UpdateColorPreviewTexture(Color color)
        {
            if (_colorPreviewTexture == null)
            {
                _colorPreviewTexture = new Texture2D(30, 30, TextureFormat.RGBA32, false);
                _colorPreviewTexture.filterMode = FilterMode.Bilinear;
                _colorPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            }

            Color[] pixels = new Color[30 * 30];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            _colorPreviewTexture.SetPixels(pixels);
            _colorPreviewTexture.Apply();
        }

        public static class Vector3FieldControl
        {
            /// <summary>
            /// Draws a Vector3 field with three text fields (for X, Y, and Z),
            /// a slider to adjust the value of the currently selected text field,
            /// and plus/minus buttons for fine-tuned control.
            /// </summary>
            /// <param name="label">A label describing the field group.</param>
            /// <param name="value">The current Vector3 value.</param>
            /// <param name="min">The minimum allowed value (slider lower bound).</param>
            /// <param name="max">The maximum allowed value (slider upper bound).</param>
            /// <param name="step">The increment/decrement step for the plus/minus buttons.</param>
            /// <returns>The updated Vector3 value.</returns>
            public static Vector3 DrawVector3Field(string label, Vector3 value, float min, float max, float step)
            {
                GUILayout.BeginVertical();
                GUILayout.Label(label);

                // Begin horizontal group for the three text fields.
                GUILayout.BeginHorizontal();
                // Assign unique control names so we can detect which one has focus.
                GUI.SetNextControlName(label + "_X");
                string xStr = GUILayout.TextField(value.x.ToString("F2"), GUILayout.Width(50));
                GUI.SetNextControlName(label + "_Y");
                string yStr = GUILayout.TextField(value.y.ToString("F2"), GUILayout.Width(50));
                GUI.SetNextControlName(label + "_Z");
                string zStr = GUILayout.TextField(value.z.ToString("F2"), GUILayout.Width(50));
                GUILayout.EndHorizontal();

                // Parse the text field values.
                float x, y, z;
                if (!float.TryParse(xStr, out x))
                    x = value.x;
                if (!float.TryParse(yStr, out y))
                    y = value.y;
                if (!float.TryParse(zStr, out z))
                    z = value.z;

                // Log the parsed values.
                MelonLogger.Msg($"[Vector3FieldControl] {label} parsed values: X={x}, Y={y}, Z={z}");

                // Determine which component's text field is currently focused.
                // Default is X (index 0); otherwise, index 1 for Y and 2 for Z.
                string focusedControl = GUI.GetNameOfFocusedControl();
                int selectedIndex = 0;
                if (focusedControl == label + "_Y")
                    selectedIndex = 1;
                else if (focusedControl == label + "_Z")
                    selectedIndex = 2;

                // Get the currently selected component's value.
                float selectedValue = (selectedIndex == 0) ? x : (selectedIndex == 1 ? y : z);

                // Draw a slider for the selected component.
                selectedValue = GUILayout.HorizontalSlider(selectedValue, min, max);
                // Update the appropriate component with the slider value.
                if (selectedIndex == 0)
                    x = selectedValue;
                else if (selectedIndex == 1)
                    y = selectedValue;
                else
                    z = selectedValue;

                // Draw plus and minus buttons for fine-tuning.
                GUILayout.BeginHorizontal();
                if (GUILayout.RepeatButton("-", GUILayout.Width(25)))
                {
                    if (selectedIndex == 0)
                        x = Mathf.Max(min, x - step);
                    else if (selectedIndex == 1)
                        y = Mathf.Max(min, y - step);
                    else
                        z = Mathf.Max(min, z - step);
                }
                if (GUILayout.RepeatButton("+", GUILayout.Width(25)))
                {
                    if (selectedIndex == 0)
                        x = Mathf.Min(max, x + step);
                    else if (selectedIndex == 1)
                        y = Mathf.Min(max, y + step);
                    else
                        z = Mathf.Min(max, z + step);
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                Vector3 newValue = new Vector3(x, y, z);
                MelonLogger.Msg($"[Vector3FieldControl] {label} updated Vector3: {newValue}");
                return newValue;
            }
        }
    }
}
