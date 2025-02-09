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
        // Reflection-based parameters: Component -> (Field -> string value)
        private Dictionary<string, Dictionary<string, string>> _fieldInputs
            = new Dictionary<string, Dictionary<string, string>>();
        // Advanced TreeView foldout states
        private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        // === GUI STYLES (lazily in OnGUI) ===
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _headerStyle;

        // === ADDITIONAL FEATURES STATE ===
        private bool _manualApply = true;   // If true, only apply after "Apply" button.
        private bool _showHelp = false;
        private bool _advancedView = true;  // Switch between Simple/Advanced
        private bool _treeViewEnabled = true; // For collapsible advanced parameters

        // === WINDOW CONTROLS & RESIZING ===
        private bool _isMinimized = false;
        private Rect _prevWindowRect;
        private bool _isResizing = false;
        private Vector2 _resizeStartMousePos;
        private Rect _resizeStartWindowRect;
        private ResizeEdges _resizeEdges;
        private float _opacity = 1f; // 0=transparent .. 1=opaque

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

        // === LOCAL PREVIEW STATE ===
        //
        // Instead of updating the game param each frame while holding +/-,
        // we keep a local "preview" of the changed values, apply them visually,
        // and only set them in SledParameterManager after the user releases the button.
        //
        // We'll store these local previews for reflection-based fields in:
        //   _fieldPreview[compName][fieldName] = numeric value
        //
        // and for the Simple Tuner's local fields, we just keep them in local variables.
        //
        // We'll also track "were we holding minus/plus last frame?" to detect release.

        // For reflection-based fields: 
        private Dictionary<string, Dictionary<string, double>> _fieldPreview
            = new Dictionary<string, Dictionary<string, double>>();

        // For color channels: same approach
        //   We store compName="Light", fieldName in {r,g,b,a}, numeric double.

        // Track whether minus/plus were held previously for each field to detect release
        private HashSet<string> _minusHeldNow = new HashSet<string>();
        private HashSet<string> _minusHeldPrev = new HashSet<string>();
        private HashSet<string> _plusHeldNow = new HashSet<string>();
        private HashSet<string> _plusHeldPrev = new HashSet<string>();

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
        /// Toggles the menu. On open, load field data from SledParameterManager into local previews.
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
        /// Loads current param data from SledParameterManager into _fieldInputs (for display) 
        /// and also into _fieldPreview (for local numeric storage).
        /// </summary>
        public void RePopulateFields()
        {
            _fieldInputs.Clear();
            _fieldPreview.Clear();

            Dictionary<string, Dictionary<string, object>> currentParams
                = _sledParameterManager.GetCurrentParameters();

            foreach (var compEntry in _sledParameterManager.ComponentsToInspect)
            {
                string compName = compEntry.Key;
                _fieldInputs[compName] = new Dictionary<string, string>();
                _fieldPreview[compName] = new Dictionary<string, double>();

                foreach (string field in compEntry.Value)
                {
                    object val = _sledParameterManager.GetFieldValue(compName, field);
                    // Convert to string
                    string valStr = (val != null) ? val.ToString() : "(No data)";
                    _fieldInputs[compName][field] = valStr;

                    double previewNum = 0.0;
                    // Attempt parse if numeric
                    if (val is double d)
                        previewNum = d;
                    else if (val is float f)
                        previewNum = (double)f;
                    else if (val is int i)
                        previewNum = (double)i;
                    else
                    {
                        double tryParseD;
                        if (double.TryParse(valStr, out tryParseD))
                            previewNum = tryParseD;
                    }

                    _fieldPreview[compName][field] = previewNum;
                }
            }

            // Ensure foldout states
            foreach (var comp in _fieldInputs.Keys)
            {
                if (!_foldoutStates.ContainsKey(comp))
                    _foldoutStates[comp] = true;
            }

            MelonLogger.Msg("[GUIManager] Fields repopulated.");
        }

        /// <summary>
        /// Main IMGUI draw call. Called by Main.OnGUI().
        /// </summary>
        public void DrawMenu()
        {
            if (!_menuOpen)
                return;

            // Copy sets from "now" to "prev", then clear "now"
            _minusHeldPrev = new HashSet<string>(_minusHeldNow);
            _plusHeldPrev = new HashSet<string>(_plusHeldNow);

            _minusHeldNow.Clear();
            _plusHeldNow.Clear();

            // Lazy init GUI styles
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

            // Apply opacity
            Color prevColor = GUI.color;
            GUI.color = new Color(prevColor.r, prevColor.g, prevColor.b, _opacity);

            _windowRect = GUILayout.Window(1234, _windowRect, WindowFunction, "SledTuner Menu", _windowStyle);

            GUI.color = prevColor;
            HandleResize();
        }

        // === PRIVATE DRAW METHODS ===

        private void WindowFunction(int windowID)
        {
            if (_isMinimized)
            {
                DrawTitleBar();
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
                return;
            }

            DrawTitleBar();
            GUILayout.Space(5);

            if (_advancedView)
                DrawAdvancedTunerMenu();
            else
                DrawSimpleTunerMenu();

            // After drawing controls, detect newly released minus/plus for reflection-based fields 
            // or color fields => commit changes. We do it in the same Repaint pass for clarity:
            if (Event.current.type == EventType.Repaint)
            {
                DetectButtonReleases();
            }

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        /// <summary>
        /// If a field's minus/plus was held last frame but not this frame, the user just released => 
        /// commit changes to SledParameterManager if !_manualApply or do it only if user wants immediate apply.
        /// </summary>
        private void DetectButtonReleases()
        {
            // We'll look for any field that was in minusHeldPrev but not in minusHeldNow => just released minus
            // same for plus.
            foreach (var fieldKey in _minusHeldPrev)
            {
                if (!_minusHeldNow.Contains(fieldKey))
                {
                    // Just released minus
                    CommitFieldIfReflection(fieldKey);
                }
            }
            foreach (var fieldKey in _plusHeldPrev)
            {
                if (!_plusHeldNow.Contains(fieldKey))
                {
                    // Just released plus
                    CommitFieldIfReflection(fieldKey);
                }
            }
        }

        /// <summary>
        /// If this fieldKey references a reflection-based field, we set the manager param. 
        /// If it's "simple.xyz", we do nothing here because we've updated local variables only, 
        /// which presumably the user sees immediately. 
        /// But if we want to commit "simple" fields on release, we could do that as well.
        /// </summary>
        private void CommitFieldIfReflection(string fieldKey)
        {
            // Example fieldKey might be "MeshInterpretter.power.minus" or "Light.r.plus"
            // We'll parse out compName.fieldName => compName + "." + fieldName + ".minus"
            // or compName + "." + channel + ".plus"
            if (fieldKey.StartsWith("simple."))
            {
                // It's a simple field => we do not need to commit to the manager here 
                // if you only update the manager once the user hits "Apply" or "Reset".
                return;
            }

            // Otherwise, parse
            // e.g. "MeshInterpretter.power.minus"
            // or "Light.r.plus"
            string[] tokens = fieldKey.Split('.');
            if (tokens.Length < 3)
                return; // not enough data

            string compName = tokens[0];
            string fieldName = tokens[1];

            // If it's Light color channel, fieldName might be "r", "g", "b", "a"
            // Or it might be a reflection-based numeric field.

            // We find the local preview: 
            if (!_fieldPreview.ContainsKey(compName))
                return;
            if (!_fieldPreview[compName].ContainsKey(fieldName))
                return;

            double previewVal = _fieldPreview[compName][fieldName];

            // Now we commit that to the manager 
            _sledParameterManager.SetFieldValue(compName, fieldName, previewVal);

            if (!_manualApply)
            {
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
            GUILayout.Label("Headlight Color (RGBA)");

            if (_fieldInputs.ContainsKey("Light"))
                DrawLocalColorFields("Light");
            else
                GUILayout.Label("(No Light component found)", _labelStyle);

            // Color preview
            float rVal = 1f, gVal = 1f, bVal = 1f, aVal = 1f;
            if (_fieldInputs.ContainsKey("Light"))
            {
                var dict = _fieldInputs["Light"];
                if (dict.TryGetValue("r", out string tempStr)) float.TryParse(tempStr, out rVal);
                if (dict.TryGetValue("g", out tempStr)) float.TryParse(tempStr, out gVal);
                if (dict.TryGetValue("b", out tempStr)) float.TryParse(tempStr, out bVal);
                if (dict.TryGetValue("a", out tempStr)) float.TryParse(tempStr, out aVal);
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
        /// Local single-step float field for simple tuner fields. 
        /// We do not commit to SledParameterManager, we only store in local variable. 
        /// The user sees immediate changes in the text field, 
        /// but the game param is only updated if we want (like on an Apply button).
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
                _minusHeldNow.Add(minusKey);

            // plus
            string plusKey = uniqueKey + ".plus";
            bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
            if (plusHeld)
                _plusHeldNow.Add(plusKey);
            GUILayout.EndHorizontal();

            // on Repaint, check if we just released the minus/plus => do nothing special here
            // but if it's currently held, we do a single step
            if (Event.current.type == EventType.Repaint)
            {
                if (_minusHeldNow.Contains(minusKey) && !_minusHeldPrev.Contains(minusKey))
                {
                    // we just pressed the button => do nothing special
                }
                if (_minusHeldNow.Contains(minusKey))
                {
                    // each frame step
                    newVal = Mathf.Max(min, newVal - 0.01f);
                }
                else if (_minusHeldPrev.Contains(minusKey) && !_minusHeldNow.Contains(minusKey))
                {
                    // just released => do nothing special
                }

                if (_plusHeldNow.Contains(plusKey))
                {
                    newVal = Mathf.Min(max, newVal + 0.01f);
                }
            }

            return newVal;
        }

        /// <summary>
        /// Local color channels for the simple tuner. 
        /// We do not apply them to manager each frame. 
        /// We'll update _fieldInputs so the user sees changes in the text field. 
        /// Once user is done, they'd do "Apply" if _manualApply is off or they can skip.
        /// </summary>
        private void DrawLocalColorFields(string compName)
        {
            if (!_fieldInputs.ContainsKey(compName))
                return;
            string[] channels = { "r", "g", "b", "a" };
            var dict = _fieldInputs[compName];
            foreach (var ch in channels)
            {
                if (!dict.ContainsKey(ch))
                    continue;
                float currentVal = 0f;
                float.TryParse(dict[ch], out currentVal);
                float newVal = currentVal;

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

                // On Repaint, update local color channel
                if (Event.current.type == EventType.Repaint)
                {
                    if (_minusHeldNow.Contains(minusKey))
                        newVal = Mathf.Max(0f, newVal - 0.01f);
                    if (_plusHeldNow.Contains(plusKey))
                        newVal = Mathf.Min(1f, newVal + 0.01f);
                }

                if (Math.Abs(newVal - currentVal) > 0.0001f)
                {
                    dict[ch] = newVal.ToString("F2");
                    // We do NOT call manager right now unless we want immediate changes 
                    // We'll do so if user hits Apply or if advanced approach does it on release.
                }
            }
        }

        /// <summary>
        /// Draw all reflection-based parameters in a flat list for advanced mode 
        /// (when TreeView is off).
        /// </summary>
        private void DrawAdvancedFlatParameters()
        {
            foreach (var comp in _fieldInputs)
            {
                GUILayout.Label("<b>Component: " + comp.Key + "</b>", _labelStyle);
                DrawReflectionParameters(comp.Key, comp.Value);

                if (comp.Key == "Light")
                {
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
                    DrawReflectionParameters(comp.Key, comp.Value);

                    if (comp.Key == "Light")
                    {
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
                    GUILayout.EndVertical();
                }
                GUILayout.Space(10);
            }
        }

        /// <summary>
        /// For reflection-based numeric/bool fields. We do local preview in _fieldPreview,
        /// and only on button release do we commit to SledParameterManager.
        /// </summary>
        private void DrawReflectionParameters(string compName, Dictionary<string, string> fields)
        {
            foreach (var kvp in fields)
            {
                string fieldName = kvp.Key;
                // If Light color channel, skip => handled separately
                if (compName == "Light" &&
                    (fieldName == "r" || fieldName == "g" || fieldName == "b" || fieldName == "a"))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(fieldName + ":", _labelStyle, GUILayout.Width(150));

                Type fieldType = _sledParameterManager.GetFieldType(compName, fieldName);
                double currentVal = _fieldPreview[compName][fieldName]; // local numeric preview

                if (fieldType == typeof(float) || fieldType == typeof(double) || fieldType == typeof(int))
                {
                    float sliderMin = _sledParameterManager.GetSliderMin(compName, fieldName);
                    float sliderMax = _sledParameterManager.GetSliderMax(compName, fieldName);

                    // Single step each frame while held
                    double baseStep = (fieldType == typeof(int)) ? 1.0 : 0.01;

                    // Draw a slider for user preview
                    float sliderVal = GUILayout.HorizontalSlider(
                        (float)currentVal, sliderMin, sliderMax, GUILayout.Width(150)
                    );

                    // let user also type into text
                    string newText = GUILayout.TextField(
                        sliderVal.ToString("F2"),
                        _textFieldStyle,
                        GUILayout.Width(50)
                    );
                    if (float.TryParse(newText, out float parsedVal))
                    {
                        sliderVal = parsedVal;
                    }

                    // +/- buttons
                    GUILayout.BeginHorizontal(GUILayout.Width(60));
                    string minusKey = compName + "." + fieldName + ".minus";
                    bool minusHeld = GUILayout.RepeatButton("-", _buttonStyle, GUILayout.Width(25));
                    if (minusHeld)
                        _minusHeldNow.Add(minusKey);

                    string plusKey = compName + "." + fieldName + ".plus";
                    bool plusHeld = GUILayout.RepeatButton("+", _buttonStyle, GUILayout.Width(25));
                    if (plusHeld)
                        _plusHeldNow.Add(plusKey);
                    GUILayout.EndHorizontal();

                    double finalVal = (double)sliderVal;

                    // Apply single-step if the button is held (only in Repaint)
                    if (Event.current.type == EventType.Repaint)
                    {
                        if (_minusHeldNow.Contains(minusKey))
                            finalVal = Math.Max(sliderMin, finalVal - baseStep);
                        if (_plusHeldNow.Contains(plusKey))
                            finalVal = Math.Min(sliderMax, finalVal + baseStep);
                    }

                    // If it changed from currentVal
                    if (Math.Abs(finalVal - currentVal) > 0.0001)
                    {
                        _fieldPreview[compName][fieldName] = finalVal;
                        // Also update _fieldInputs so the UI text refreshes
                        _fieldInputs[compName][fieldName] = finalVal.ToString("F2");
                    }
                }
                else if (fieldType == typeof(bool))
                {
                    bool curBool = false;
                    bool.TryParse(_fieldInputs[compName][fieldName], out curBool);
                    bool newBool = GUILayout.Toggle(curBool, curBool ? "On" : "Off", _toggleStyle, GUILayout.Width(80));
                    if (newBool != curBool)
                    {
                        _fieldInputs[compName][fieldName] = newBool.ToString();
                        // We can set the preview as well if we want, but it's a bool so no big difference
                        if (!_manualApply)
                        {
                            _sledParameterManager.SetFieldValue(compName, fieldName, newBool);
                            _sledParameterManager.ApplyParameters();
                        }
                    }
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
                        // If it's a complex type, we might skip for now
                    }
                }

                GUILayout.EndHorizontal();
            }

            // If Light, we do color channels in a separate routine
            if (compName == "Light" && fields != null)
            {
                // Already handled in advanced => user sees color channels in separate logic
            }
        }

        private void DrawConfigButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load", _buttonStyle, GUILayout.Height(25)))
            {
                _configManager.LoadConfiguration();
                RePopulateFields();
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
        /// Finalize changes to SledParameterManager if "Apply" pressed or if _manualApply is false, 
        /// for reflection-based fields. 
        /// </summary>
        private void ApplyChanges()
        {
            // For reflection-based fields, we have a local numeric preview in _fieldPreview. 
            // We commit them all now.
            // For the user, "simple" fields can remain local. If you want them to also push to manager,
            // you'd do so here as well, but that depends on your design.
            foreach (var compKvp in _fieldPreview)
            {
                string compName = compKvp.Key;
                foreach (var fieldKvp in compKvp.Value)
                {
                    string fieldName = fieldKvp.Key;
                    double finalVal = fieldKvp.Value;

                    // If it's actually bool or something else, we'd skip. We'll do a naive approach:
                    Type ft = _sledParameterManager.GetFieldType(compName, fieldName);
                    if (ft == typeof(float) || ft == typeof(double) || ft == typeof(int))
                    {
                        _sledParameterManager.SetFieldValue(compName, fieldName, finalVal);
                    }
                    else if (ft == typeof(bool))
                    {
                        bool curBool = false;
                        bool.TryParse(_fieldInputs[compName][fieldName], out curBool);
                        _sledParameterManager.SetFieldValue(compName, fieldName, curBool);
                    }
                    // If it's color channel, we skip because we've also stored them in _fieldInputs; 
                    // you could parse them here if you want to commit color changes on "Apply".
                }
            }
            _sledParameterManager.ApplyParameters();
            MelonLogger.Msg("[GUIManager] Changes applied.");
        }

        private void ResetValues()
        {
            // Resets the simple local fields
            speed = 10f;
            gravity = originalGravity;
            power = originalPower;
            lugHeight = originalLugHeight;
            trackLength = originalTrackLength;
            pitchFactor = originalPitchFactor;

            notdriverInvincible = true;
            test = false;
        }

        // Conversion for text -> numeric if needed
        private object ConvertInput(string input, Type targetType)
        {
            if (targetType == typeof(float))
            {
                if (float.TryParse(input, out float f))
                    return f;
                return 0f;
            }
            else if (targetType == typeof(int))
            {
                if (int.TryParse(input, out int i))
                    return i;
                return 0;
            }
            else if (targetType == typeof(bool))
            {
                if (bool.TryParse(input, out bool b))
                    return b;
                return false;
            }
            else if (targetType == typeof(double))
            {
                if (double.TryParse(input, out double d))
                    return d;
                return 0.0;
            }
            else if (targetType == typeof(Vector2))
            {
                if (TryParseVector2(input, out Vector2 v2)) return v2;
                return Vector2.zero;
            }
            else if (targetType == typeof(Vector3))
            {
                if (TryParseVector3(input, out Vector3 v3)) return v3;
                return Vector3.zero;
            }
            else if (targetType == typeof(Vector4))
            {
                if (TryParseVector4(input, out Vector4 v4)) return v4;
                return Vector4.zero;
            }
            return input;
        }

        private bool TryParseVector2(string input, out Vector2 vector)
        {
            vector = Vector2.zero;
            if (string.IsNullOrEmpty(input)) return false;
            string[] parts = input.Split(',');
            if (parts.Length != 2) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y))
            {
                vector = new Vector2(x, y);
                return true;
            }
            return false;
        }

        private bool TryParseVector3(string input, out Vector3 vector)
        {
            vector = Vector3.zero;
            if (string.IsNullOrEmpty(input)) return false;
            string[] parts = input.Split(',');
            if (parts.Length != 3) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            {
                vector = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        private bool TryParseVector4(string input, out Vector4 vector)
        {
            vector = Vector4.zero;
            if (string.IsNullOrEmpty(input)) return false;
            string[] parts = input.Split(',');
            if (parts.Length != 4) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z) &&
                float.TryParse(parts[3], out float w))
            {
                vector = new Vector4(x, y, z, w);
                return true;
            }
            return false;
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
                        newRect.width = Mathf.Clamp(
                            _resizeStartWindowRect.width + delta.x,
                            minWidth, maxWidth
                        );
                    if (_resizeEdges.left)
                    {
                        newRect.x = _resizeStartWindowRect.x + delta.x;
                        newRect.width = Mathf.Clamp(
                            _resizeStartWindowRect.width - delta.x,
                            minWidth, maxWidth
                        );
                    }
                    if (_resizeEdges.bottom)
                        newRect.height = Mathf.Clamp(
                            _resizeStartWindowRect.height + delta.y,
                            minHeight, maxHeight
                        );
                    if (_resizeEdges.top)
                    {
                        newRect.y = _resizeStartWindowRect.y + delta.y;
                        newRect.height = Mathf.Clamp(
                            _resizeStartWindowRect.height - delta.y,
                            minHeight, maxHeight
                        );
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
                _windowRect = new Rect(
                    Screen.width - 150f - 10f,
                    10f,
                    150f,
                    30f
                );
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
                "  - Local changes are shown in the UI but only applied to the game param when you release the button or press 'Apply'.\n" +
                "  - If 'Manual Apply' is enabled, changes take effect only after pressing 'Apply'.\n" +
                "  - Use the window buttons to Minimize, Maximize, or Close the menu.\n" +
                "  - Footer buttons include toggles for Ragdoll, Tree Renderer, and Teleport.\n" +
                "  - In Advanced view, use the 'Tree View' toggle to collapse/expand components.\n" +
                "  - 'Switch View' toggles between Advanced and Simple tuner menus.\n",
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
    }
}
